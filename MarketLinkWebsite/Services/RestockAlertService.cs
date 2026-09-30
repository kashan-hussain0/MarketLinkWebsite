using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Services;

/// <summary>
/// Tells a shopper when something they saved has come back. A product counts as
/// back in stock when it is listed and stocked again after a genuine dip, and a
/// farm alert fires when any of that farm's produce is available again.
/// </summary>
public sealed class RestockAlertService
{
    private const int LookbackDays = 21;

    private readonly ApplicationDbContext db;

    public RestockAlertService(ApplicationDbContext db)
    {
        this.db = db;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow.AddDays(-LookbackDays);

        // An item counts as "back" when it is listed, stocked, and either it has
        // sold in the lookback window (so it must have dipped) or the farmer has
        // just topped the stock up. Freshly seeded items are excluded so a first
        // run does not flood everyone.
        var candidates = await (
            from product in db.Products.AsNoTracking()
            where product.IsAvailable
                && product.Inventory != null
                && product.Inventory.QuantityAvailable > 0
                && product.FarmerProfile.Status == FarmerStatus.Active
                && product.FarmerProfile.User.IsActive
                && product.CreatedAt < DateTime.UtcNow.AddDays(-LookbackDays)
            let stock = product.Inventory
            let sold = db.OrderItems.AsNoTracking()
                .Where(item => item.ProductId == product.Id
                    && item.Order.Status != OrderStatus.Cancelled
                    && item.Order.Status != OrderStatus.Declined
                    && item.Order.OrderDate >= since)
                .Sum(item => item.Quantity)
            where sold > 0 || stock.LastUpdated >= DateTime.UtcNow.AddDays(-3)
            select new
            {
                product.Id,
                product.Name,
                product.FarmerProfile.FarmName,
                product.FarmerProfileId
            })
            .Take(200)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return 0;
        }

        var productIds = candidates.Select(row => row.Id).ToList();
        var farmIds = candidates.Select(row => row.FarmerProfileId).Distinct().ToList();
        var productLookup = candidates.ToDictionary(row => row.Id, row => (row.Name, row.FarmName));
        var farmLookup = candidates.GroupBy(row => row.FarmerProfileId)
            .ToDictionary(group => group.Key, group => group.First().FarmName);

        var sent = 0;

        // Shoppers who saved a specific item.
        var productWatchers = await db.FavouriteProducts
            .AsNoTracking()
            .Where(favourite => productIds.Contains(favourite.ProductId) && favourite.User.IsActive)
            .Select(favourite => new { favourite.UserId, favourite.ProductId })
            .ToListAsync(cancellationToken);

        foreach (var watcher in productWatchers)
        {
            if (!productLookup.TryGetValue(watcher.ProductId, out var product))
            {
                continue;
            }

            if (await AlreadyNotifiedAsync(watcher.UserId, $"/Product/Details/{watcher.ProductId}", cancellationToken))
            {
                continue;
            }

            db.Notifications.Add(new Notification
            {
                UserId = watcher.UserId,
                Type = NotificationType.System,
                Title = "Back in stock",
                Message = $"{product.Name} from {product.FarmName} is available again. You saved this item earlier.",
                ActionUrl = $"/Product/Details/{watcher.ProductId}",
                CreatedAt = DateTime.UtcNow
            });

            sent++;
        }

        // Shoppers who saved the farm itself.
        var farmWatchers = await db.FavouriteFarmers
            .AsNoTracking()
            .Where(favourite => farmIds.Contains(favourite.FarmerProfileId) && favourite.User.IsActive)
            .Select(favourite => new { favourite.UserId, favourite.FarmerProfileId })
            .ToListAsync(cancellationToken);

        foreach (var watcher in farmWatchers)
        {
            if (!farmLookup.TryGetValue(watcher.FarmerProfileId, out var farmName))
            {
                continue;
            }

            if (await AlreadyNotifiedAsync(watcher.UserId, $"/Farm/Index?id={watcher.FarmerProfileId}", cancellationToken))
            {
                continue;
            }

            db.Notifications.Add(new Notification
            {
                UserId = watcher.UserId,
                Type = NotificationType.System,
                Title = "New harvest from a saved farm",
                Message = $"{farmName} has produce available again. You saved this farm earlier.",
                ActionUrl = $"/Farm/Index?id={watcher.FarmerProfileId}",
                CreatedAt = DateTime.UtcNow
            });

            sent++;
        }

        if (sent > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return sent;
    }

    // A shopper is told once about each item or farm inside the lookback window,
    // so a seasonal return is not repeated on every sweep.
    private async Task<bool> AlreadyNotifiedAsync(string userId, string actionUrl, CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow.AddDays(-LookbackDays);

        return await db.Notifications.AsNoTracking().AnyAsync(notification =>
            notification.UserId == userId
            && notification.Type == NotificationType.System
            && notification.ActionUrl == actionUrl
            && notification.CreatedAt >= since, cancellationToken);
    }
}

public sealed class RestockAlertHostedService : BackgroundService
{
    private readonly IServiceProvider services;
    private readonly ILogger<RestockAlertHostedService> logger;

    public RestockAlertHostedService(IServiceProvider services, ILogger<RestockAlertHostedService> logger)
    {
        this.services = services;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
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
            var service = scope.ServiceProvider.GetRequiredService<RestockAlertService>();
            var sent = await service.RunAsync(cancellationToken);
            if (sent > 0)
            {
                logger.LogInformation("Restock alerts sent for {Count} saved items and farms.", sent);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Restock alert sweep failed.");
        }
    }
}
