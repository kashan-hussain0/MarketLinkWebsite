using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Areas.Farmer.Controllers;

[Area("Farmer")]
[Authorize(Policy = "ActiveFarmerAccess")]
public sealed class InsightsController : FarmerControllerBase
{
    public InsightsController(ApplicationDbContext db) : base(db)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? period, CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);

        var ownedItems = await Db.OrderItems
            .AsNoTracking()
            .Include(item => item.Product)
            .Where(item => item.Product != null && item.Product.FarmerProfileId == profile.Id)
            .ToListAsync(cancellationToken);

        var relevantOrderIds = ownedItems.Select(item => item.OrderId).Distinct().ToList();
        var orders = await Db.Orders
            .AsNoTracking()
            .Where(order => relevantOrderIds.Contains(order.Id))
            .OrderByDescending(order => order.OrderDate)
            .ToListAsync(cancellationToken);

        var (rangeStart, rangeLabel) = ResolveRange(period);

        var settled = orders
            .Where(order => order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Declined)
            .ToList();

        var inRange = settled.Where(order => order.OrderDate >= rangeStart).ToList();

        decimal SubtotalOf(Order order) => ownedItems
            .Where(item => item.OrderId == order.Id)
            .Sum(item => item.Quantity * item.UnitPrice);

        var totalRevenue = inRange.Sum(SubtotalOf);
        var lifetimeRevenue = settled.Sum(SubtotalOf);
        var pendingOrders = orders.Count(order =>
            order.Status is OrderStatus.Pending or OrderStatus.Accepted or OrderStatus.Preparing or OrderStatus.ReadyForPickup);

        var bestSellers = ownedItems
            .GroupBy(item => item.ProductName)
            .Select(group => new
            {
                Name = group.Key,
                ImageUrl = group
                    .Select(item => item.Product == null ? string.Empty : item.Product.ImageUrl)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty,
                Units = group.Where(item => orders.Any(order => order.Id == item.OrderId && order.OrderDate >= rangeStart
                        && order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Declined))
                    .Sum(item => item.Quantity),
                Revenue = group.Where(item => orders.Any(order => order.Id == item.OrderId && order.Status == OrderStatus.Completed))
                    .Sum(item => item.Quantity * item.UnitPrice)
            })
            .OrderByDescending(item => item.Units)
            .ThenByDescending(item => item.Revenue)
            .Take(6)
            .Select(item => new BestSellerViewModel
            {
                Name = item.Name,
                ImageUrl = item.ImageUrl,
                UnitsSold = item.Units,
                Revenue = item.Revenue,
                RevenueLabel = item.Revenue.ToString("C")
            })
            .ToList();

        var recentOrders = orders
            .Take(12)
            .Select(order => new InsightOrderRow
            {
                Id = order.Id,
                OrderNumber = order.OrderNumber,
                OrderDate = order.OrderDate,
                DateLabel = FormatShortDate(order.OrderDate),
                Status = GetStatusLabel(order.Status),
                StatusKey = order.Status.ToString(),
                StatusTone = GetStatusTone(order.Status),
                StatusIcon = GetStatusIcon(order.Status),
                ItemCount = ownedItems.Count(item => item.OrderId == order.Id),
                Subtotal = SubtotalOf(order),
                SubtotalLabel = SubtotalOf(order).ToString("C")
            })
            .ToList();

        var statusRows = new List<StatusBreakdownRow>
        {
            Row("Accepted", nameof(OrderStatus.Accepted), "bi-check2-circle", "blue"),
            Row("Preparing", nameof(OrderStatus.Preparing), "bi-box-seam", "amber"),
            Row("Ready for pickup", nameof(OrderStatus.ReadyForPickup), "bi-bag-check", "green"),
            Row("Completed", nameof(OrderStatus.Completed), "bi-check2-all", "green"),
            Row("New", nameof(OrderStatus.Pending), "bi-stars", "amber"),
            Row("Declined or cancelled", nameof(OrderStatus.Declined) + "|" + nameof(OrderStatus.Cancelled), "bi-x-circle", "red")
        };
        var statusTotal = Math.Max(1, orders.Count);
        foreach (var row in statusRows)
        {
            var keys = row.StatusKey.Split('|');
            row.Count = orders.Count(order => keys.Any(key => key == order.Status.ToString()));
            row.Percent = (int)Math.Round(row.Count * 100m / statusTotal);
        }

        var unitsSold = ownedItems
            .Where(item => inRange.Any(order => order.Id == item.OrderId && order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Declined))
            .Sum(item => item.Quantity);

        var topSeller = bestSellers.FirstOrDefault();
        var topShare = totalRevenue <= 0m || topSeller is null
            ? 0m
            : Math.Round(topSeller.Revenue * 100m / totalRevenue, 1);

        var busiestDay = inRange.Count == 0
            ? null
            : inRange
                .GroupBy(order => order.OrderDate.DayOfWeek)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .First();

        var model = new InsightsViewModel
        {
            Period = rangeLabel,
            TotalOrders = settled.Count,
            PeriodOrders = inRange.Count,
            PendingOrders = pendingOrders,
            Revenue = totalRevenue,
            RevenueLabel = totalRevenue.ToString("C"),
            LifetimeRevenue = lifetimeRevenue,
            LifetimeRevenueLabel = lifetimeRevenue.ToString("C"),
            AverageOrderValue = inRange.Count == 0 ? 0m : totalRevenue / inRange.Count,
            AverageOrderValueLabel = (inRange.Count == 0 ? 0m : totalRevenue / inRange.Count).ToString("C"),
            CompletedCount = orders.Count(order => order.Status == OrderStatus.Completed),
            CancelledCount = orders.Count(order => order.Status is OrderStatus.Cancelled or OrderStatus.Declined),
            WeeklySales = BuildWeeklySeries(inRange, SubtotalOf),
            BestSellers = bestSellers,
            RecentOrders = recentOrders,
            StatusBreakdown = statusRows.Where(row => row.Count > 0).ToList(),
            UnitsSold = unitsSold,
            UnitsSoldLabel = unitsSold.ToString("N0"),
            TopProductShare = topShare,
            TopProductShareLabel = topShare <= 0m ? "No settled sales yet" : $"{topShare:0.#}% of revenue",
            BusiestDayLabel = busiestDay is null
                ? "No sales recorded yet"
                : $"{busiestDay.Key} is your busiest order day ({busiestDay.Count()} order{(busiestDay.Count() == 1 ? string.Empty : "s")})",
            PeriodNote = $"{inRange.Count} order{(inRange.Count == 1 ? string.Empty : "s")} placed in {rangeLabel.ToLowerInvariant()}"
        };

        return View(model);
    }

    private static StatusBreakdownRow Row(string label, string statusKey, string icon, string tone) => new()
    {
        Label = label,
        StatusKey = statusKey,
        Icon = icon,
        Tone = tone
    };

    private static (DateTime Start, string Label) ResolveRange(string? period)
    {
        var today = DateTime.UtcNow.Date;

        return period switch
        {
            "Last 7 days" => (today.AddDays(-6), "Last 7 days"),
            "Last 90 days" => (today.AddDays(-89), "Last 90 days"),
            "This year" => (new DateTime(today.Year, 1, 1), "This year"),
            _ => (today.AddDays(-29), "Last 30 days")
        };
    }

    private static List<WeeklySalesPoint> BuildWeeklySeries(List<Order> orders, Func<Order, decimal> subtotalOf)
    {
        var points = new List<WeeklySalesPoint>();
        var start = DateTime.UtcNow.Date.AddDays(-27);

        for (var day = 0; day < 28; day++)
        {
            var current = start.AddDays(day);
            var revenue = orders.Where(order => order.OrderDate.Date == current).Sum(subtotalOf);

            points.Add(new WeeklySalesPoint
            {
                Label = current.ToString("ddd"),
                DateLabel = current.ToString("MMM d"),
                Revenue = revenue,
                Height = revenue <= 0 ? 0 : Math.Max(6, (int)Math.Round(revenue * 78 / Math.Max(1, orders.Max(subtotalOf))))
            });
        }

        return points;
    }
}
