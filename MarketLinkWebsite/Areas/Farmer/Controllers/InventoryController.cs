using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Areas.Farmer.Controllers;

[Area("Farmer")]
[Authorize(Policy = "ActiveFarmerAccess")]
public sealed class InventoryController : FarmerControllerBase
{
    public InventoryController(ApplicationDbContext db) : base(db)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? filter, CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);

        var products = await Db.Products
            .AsNoTracking()
            .Include(product => product.Inventory)
            .Include(product => product.Category)
            .Include(product => product.WeeklyStockPlan)
            .Where(product => product.FarmerProfileId == profile.Id)
            .OrderBy(product => product.Name)
            .ToListAsync(cancellationToken);

        var normalizedFilter = string.IsNullOrWhiteSpace(filter) ? "All items" : filter.Trim();
        var items = products
            .Select(product => MapItem(product))
            .ToList();

        var visible = items
            .Where(item => normalizedFilter switch
            {
                "Low stock" => item.IsListed && item.Stock <= item.ReorderThreshold,
                "Sold out" => item.Stock == 0,
                "Not listed" => !item.IsListed,
                _ => true
            })
            .ToList();

        var model = new InventoryViewModel
        {
            Filter = normalizedFilter,
            TotalItems = items.Count,
            ListedItems = items.Count(item => item.IsListed),
            LowStockItems = items.Count(item => item.IsListed && item.Stock <= item.ReorderThreshold),
            SoldOutItems = items.Count(item => item.Stock == 0),
            TotalUnits = items.Sum(item => item.Stock),
            Notice = Request.Query["notice"].FirstOrDefault(),
            Items = visible
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStock(
        int id,
        int stock,
        int reorderThreshold,
        bool? keepPlan,
        CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        var product = await Db.Products
            .Include(item => item.Inventory)
            .Include(item => item.WeeklyStockPlan)
            .SingleOrDefaultAsync(item => item.Id == id && item.FarmerProfileId == profile.Id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        stock = Math.Clamp(stock, 0, 1_000_000);
        reorderThreshold = Math.Clamp(reorderThreshold, 0, 1_000_000);

        if (product.Inventory is null)
        {
            product.Inventory = new Inventory { ProductId = product.Id };
        }

        product.Inventory.QuantityAvailable = stock;
        product.Inventory.ReorderThreshold = reorderThreshold;
        product.Inventory.LastUpdated = DateTime.UtcNow;

        if (keepPlan == true && product.WeeklyStockPlan is not null)
        {
            product.WeeklyStockPlan.MondayStock = stock;
            product.WeeklyStockPlan.TuesdayStock = stock;
            product.WeeklyStockPlan.WednesdayStock = stock;
            product.WeeklyStockPlan.ThursdayStock = stock;
            product.WeeklyStockPlan.FridayStock = stock;
            product.WeeklyStockPlan.SaturdayStock = stock;
            product.WeeklyStockPlan.SundayStock = stock;
            product.WeeklyStockPlan.UpdatedAt = DateTime.UtcNow;
        }

        product.IsAvailable = stock > 0;
        product.UpdatedAt = DateTime.UtcNow;

        AddAudit("Update stock", nameof(Product), product.Id, $"Set {product.Name} stock to {stock} with a reorder point of {reorderThreshold}.");
        await Db.SaveChangesAsync(cancellationToken);

        return RedirectToAction(nameof(Index), new { notice = $"{product.Name} stock updated to {stock}." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateWeeklyPlan(InventoryWeeklyPlanForm model, CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        var product = await Db.Products
            .Include(item => item.WeeklyStockPlan)
            .Include(item => item.Inventory)
            .SingleOrDefaultAsync(item => item.Id == model.Id && item.FarmerProfileId == profile.Id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        if (product.WeeklyStockPlan is null)
        {
            product.WeeklyStockPlan = new WeeklyStockPlan { ProductId = product.Id };
        }

        product.WeeklyStockPlan.Enabled = model.Enabled;
        product.WeeklyStockPlan.MondayStock = Clamp(model.MondayStock);
        product.WeeklyStockPlan.TuesdayStock = Clamp(model.TuesdayStock);
        product.WeeklyStockPlan.WednesdayStock = Clamp(model.WednesdayStock);
        product.WeeklyStockPlan.ThursdayStock = Clamp(model.ThursdayStock);
        product.WeeklyStockPlan.FridayStock = Clamp(model.FridayStock);
        product.WeeklyStockPlan.SaturdayStock = Clamp(model.SaturdayStock);
        product.WeeklyStockPlan.SundayStock = Clamp(model.SundayStock);
        product.WeeklyStockPlan.UpdatedAt = DateTime.UtcNow;

        if (model.ApplyToToday && product.Inventory is not null)
        {
            var todayStock = DayStock(product.WeeklyStockPlan, DateTime.UtcNow.DayOfWeek);
            product.Inventory.QuantityAvailable = todayStock;
            product.Inventory.LastUpdated = DateTime.UtcNow;
            product.IsAvailable = todayStock > 0;
        }

        product.UpdatedAt = DateTime.UtcNow;

        AddAudit("Update weekly plan", nameof(WeeklyStockPlan), product.Id, $"Saved the recurring weekly stock template for {product.Name}.");
        await Db.SaveChangesAsync(cancellationToken);

        return RedirectToAction(nameof(Index), new { notice = $"Weekly stock template saved for {product.Name}." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(int id, CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        var product = await Db.Products
            .Include(item => item.Inventory)
            .Include(item => item.WeeklyStockPlan)
            .SingleOrDefaultAsync(item => item.Id == id && item.FarmerProfileId == profile.Id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        var reservedUnits = await Db.OrderItems
            .Where(item => item.ProductId == product.Id
                && item.Order.Status != OrderStatus.Cancelled
                && item.Order.Status != OrderStatus.Declined
                && item.Order.Status != OrderStatus.Completed)
            .SumAsync(item => (int?)item.Quantity, cancellationToken) ?? 0;

        if (reservedUnits > 0)
        {
            TempData["Error"] = $"{product.Name} is still reserved on {reservedUnits} open order unit(s). Wait for those orders to finish before removing it.";
            return RedirectToAction(nameof(Index));
        }

        if (product.Inventory is not null)
        {
            Db.Inventories.Remove(product.Inventory);
        }

        if (product.WeeklyStockPlan is not null)
        {
            Db.WeeklyStockPlans.Remove(product.WeeklyStockPlan);
        }

        Db.Products.Remove(product);
        AddAudit("Remove product", nameof(Product), product.Id, $"Removed {product.Name} and its stock records.");
        await Db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = $"{product.Name} has been removed from your catalogue.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> ToggleListing(int id, CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        var product = await Db.Products
            .Include(item => item.Inventory)
            .SingleOrDefaultAsync(item => item.Id == id && item.FarmerProfileId == profile.Id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        var hasStock = product.Inventory is not null && product.Inventory.QuantityAvailable > 0;

        if (product.IsAvailable)
        {
            product.IsAvailable = false;
        }
        else
        {
            if (!hasStock)
            {
                TempData["Error"] = "Add stock before relisting this product.";
                return RedirectToAction(nameof(Index));
            }

            product.IsAvailable = true;
        }

        product.UpdatedAt = DateTime.UtcNow;
        AddAudit("Toggle listing", nameof(Product), product.Id, $"{(product.IsAvailable ? "Relisted" : "Paused")} {product.Name}.");
        await Db.SaveChangesAsync(cancellationToken);

        return RedirectToAction(nameof(Index), new
        {
            notice = product.IsAvailable
                ? $"{product.Name} is live for pre-orders."
                : $"{product.Name} is temporarily unavailable."
        });
    }

    private static int Clamp(int value) => Math.Clamp(value, 0, 1_000_000);

    private static int DayStock(WeeklyStockPlan plan, DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => plan.MondayStock,
        DayOfWeek.Tuesday => plan.TuesdayStock,
        DayOfWeek.Wednesday => plan.WednesdayStock,
        DayOfWeek.Thursday => plan.ThursdayStock,
        DayOfWeek.Friday => plan.FridayStock,
        DayOfWeek.Saturday => plan.SaturdayStock,
        _ => plan.SundayStock
    };

    private InventoryItemViewModel MapItem(Product product)
    {
        var stock = product.Inventory?.QuantityAvailable ?? 0;
        var threshold = product.Inventory?.ReorderThreshold ?? 0;
        var plan = product.WeeklyStockPlan;

        return new InventoryItemViewModel
        {
            Id = product.Id,
            Name = product.Name,
            Category = product.Category?.Name ?? "Uncategorised",
            Unit = GetUnitLabel(product.Unit),
            ImageUrl = product.ImageUrl,
            Stock = stock,
            ReorderThreshold = threshold,
            IsListed = product.IsAvailable,
            IsOrganic = product.IsOrganic,
            UpdatedLabel = product.UpdatedAt == default ? "Not updated yet" : FormatRelativeTime(product.UpdatedAt),
            PlanEnabled = plan?.Enabled ?? false,
            MondayStock = plan?.MondayStock ?? 0,
            TuesdayStock = plan?.TuesdayStock ?? 0,
            WednesdayStock = plan?.WednesdayStock ?? 0,
            ThursdayStock = plan?.ThursdayStock ?? 0,
            FridayStock = plan?.FridayStock ?? 0,
            SaturdayStock = plan?.SaturdayStock ?? 0,
            SundayStock = plan?.SundayStock ?? 0
        };
    }
}
