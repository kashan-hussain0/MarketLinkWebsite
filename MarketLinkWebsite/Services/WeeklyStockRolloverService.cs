using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Services;

/// <summary>
/// Applies each farmer's recurring weekly stock template to the live inventory, so
/// the Monday figure becomes Monday's stock without anyone touching a form.
/// </summary>
public sealed class WeeklyStockRolloverService
{
    private readonly ApplicationDbContext db;

    public WeeklyStockRolloverService(ApplicationDbContext db)
    {
        this.db = db;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        // A template is carried over once a day. The day it ran is remembered on
        // the plan, so an hourly sweep neither repeats the work nor overwrites a
        // stock figure the farmer changed by hand during the day.
        //
        // The weekday is read in local time, not UTC. A farmer thinks "it is Monday
        // here, so Monday's harvest is what I am selling", and a UTC clock would
        // still be on Sunday evening for anyone east of Greenwich in the early
        // hours. The stamp uses the same local day so the two can never disagree.
        var today = DateTime.Now.Date;

        var plans = await db.WeeklyStockPlans
            .AsNoTracking()
            .Where(plan => plan.Enabled
                && (plan.LastAppliedOn == null || plan.LastAppliedOn < today)
                && db.Inventories.Any(inventory => inventory.ProductId == plan.ProductId))
            .Select(plan => new
            {
                plan.Id,
                plan.ProductId,
                plan.MondayStock,
                plan.TuesdayStock,
                plan.WednesdayStock,
                plan.ThursdayStock,
                plan.FridayStock,
                plan.SaturdayStock,
                plan.SundayStock
            })
            .ToListAsync(cancellationToken);

        if (plans.Count == 0)
        {
            return 0;
        }

        var weekday = DateTime.Now.DayOfWeek;

        var stocks = plans.ToDictionary(
            plan => plan.ProductId,
            plan => weekday switch
            {
                DayOfWeek.Monday => plan.MondayStock,
                DayOfWeek.Tuesday => plan.TuesdayStock,
                DayOfWeek.Wednesday => plan.WednesdayStock,
                DayOfWeek.Thursday => plan.ThursdayStock,
                DayOfWeek.Friday => plan.FridayStock,
                DayOfWeek.Saturday => plan.SaturdayStock,
                _ => plan.SundayStock
            });

        var productIds = stocks.Keys.ToList();
        var now = DateTime.UtcNow;

        var inventories = await db.Inventories
            .Where(inventory => productIds.Contains(inventory.ProductId))
            .ToListAsync(cancellationToken);

        var products = await db.Products
            .Where(product => productIds.Contains(product.Id))
            .ToListAsync(cancellationToken);

        var productLookup = products.ToDictionary(product => product.Id);
        var applied = 0;

        foreach (var inventory in inventories)
        {
            if (!stocks.TryGetValue(inventory.ProductId, out var templateStock)
                || !productLookup.TryGetValue(inventory.ProductId, out var product))
            {
                continue;
            }

            // A listing the farmer has pulled is left alone. The template says how
            // much there is, not that the stall should suddenly be open again.
            if (inventory.QuantityAvailable == templateStock || !product.IsAvailable)
            {
                continue;
            }

            inventory.QuantityAvailable = templateStock;
            inventory.LastUpdated = now;
            product.UpdatedAt = now;

            // A template figure of zero means the harvest ran out, so the listing
            // drops out of the catalogue on its own.
            product.IsAvailable = templateStock > 0;
            applied++;
        }

        // Stamp every plan we looked at, applied or not, so the next sweep skips it.
        var stamps = await db.WeeklyStockPlans
            .Where(plan => plans.Select(row => row.Id).Contains(plan.Id))
            .ToListAsync(cancellationToken);

        foreach (var plan in stamps)
        {
            plan.LastAppliedOn = today;
        }

        await db.SaveChangesAsync(cancellationToken);

        return applied;
    }
}

public sealed class WeeklyStockRolloverHostedService : BackgroundService
{
    private readonly IServiceProvider services;
    private readonly ILogger<WeeklyStockRolloverHostedService> logger;

    public WeeklyStockRolloverHostedService(IServiceProvider services, ILogger<WeeklyStockRolloverHostedService> logger)
    {
        this.services = services;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run on startup and then hourly. The service records the day it applied,
        // so a restart does not overwrite a farmer's manual change.
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        await RunOnceAsync(stoppingToken);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<WeeklyStockRolloverService>();
            var applied = await service.RunAsync(cancellationToken);
            if (applied > 0)
            {
                logger.LogInformation("Weekly stock template applied to {Count} listings.", applied);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Weekly stock rollover failed.");
        }
    }
}
