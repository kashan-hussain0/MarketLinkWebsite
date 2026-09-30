using System.Globalization;
using System.Text;
using MarketLinkWebsite.Areas.Admin.Models;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = "AdminAccess")]
public sealed class ReportController : AdminControllerBase
{
    public ReportController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
        : base(db, userManager, roleManager)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] string? period, CancellationToken cancellationToken)
    {
        return View(await BuildModelAsync(period, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Generate(string? period)
    {
        var selected = NormalizePeriod(period);
        TempData["Success"] = $"The {selected.ToLowerInvariant()} report was refreshed from live marketplace data.";
        return RedirectToAction(nameof(Index), new { period = selected });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Export(string? period, CancellationToken cancellationToken)
    {
        var model = await BuildModelAsync(period, cancellationToken);
        var builder = new StringBuilder();
        builder.AppendLine("MarketLink marketplace report");
        builder.AppendLine($"Period,{CsvCell(model.DateRangeLabel)}");
        builder.AppendLine();
        builder.AppendLine("Metric,Value");
        foreach (var metric in model.Metrics)
        {
            builder.AppendLine($"{CsvCell(metric.Label)},{CsvCell(metric.Value)}");
        }

        builder.AppendLine();
        builder.AppendLine("Farmer,Orders,Revenue");
        foreach (var farmer in model.TopFarmers)
        {
            builder.AppendLine($"{CsvCell(farmer.Name)},{CsvCell(farmer.Orders)},{farmer.RevenueValue.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        builder.AppendLine();
        builder.AppendLine("Product,Category,Units sold,Revenue");
        foreach (var product in model.TopProducts)
        {
            builder.AppendLine($"{CsvCell(product.Name)},{CsvCell(product.Category)},{product.UnitsSold},{product.Revenue.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        builder.AppendLine();
        builder.AppendLine("Market,Orders,Revenue");
        foreach (var market in model.RevenueByMarket)
        {
            builder.AppendLine($"{CsvCell(market.Name)},{CsvCell(market.Orders)},{market.RevenueValue.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        AddAudit("Export report", "Marketplace report", 0, $"Exported the {model.SelectedPeriod} analytics report.");
        await Db.SaveChangesAsync(cancellationToken);
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
        return File(bytes, "text/csv", $"marketlink-report-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private async Task<ReportViewModel> BuildModelAsync(string? requestedPeriod, CancellationToken cancellationToken)
    {
        var period = ResolvePeriod(requestedPeriod);
        var validOrders = Db.Orders.Where(order => order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Declined);
        var currentOrders = validOrders.Where(order => order.OrderDate >= period.Start && order.OrderDate < period.End);
        var previousOrders = validOrders.Where(order => order.OrderDate >= period.PreviousStart && order.OrderDate < period.PreviousEnd);
        var paidOrders = currentOrders.Where(order => order.PaymentStatus == PaymentStatus.Paid);
        var previousPaidOrders = previousOrders.Where(order => order.PaymentStatus == PaymentStatus.Paid);
        var currentRevenue = await paidOrders.SumAsync(order => (decimal?)order.TotalAmount, cancellationToken) ?? 0m;
        var previousRevenue = await previousPaidOrders.SumAsync(order => (decimal?)order.TotalAmount, cancellationToken) ?? 0m;
        var currentOrderCount = await currentOrders.CountAsync(cancellationToken);
        var previousOrderCount = await previousOrders.CountAsync(cancellationToken);
        var currentCustomerCount = await currentOrders.Select(order => order.UserId).Distinct().CountAsync(cancellationToken);
        var currentReturningCount = await currentOrders.GroupBy(order => order.UserId).CountAsync(group => group.Count() > 1, cancellationToken);
        var previousCustomerCount = await previousOrders.Select(order => order.UserId).Distinct().CountAsync(cancellationToken);
        var previousReturningCount = await previousOrders.GroupBy(order => order.UserId).CountAsync(group => group.Count() > 1, cancellationToken);
        var currentRefunds = await currentOrders.CountAsync(order => order.PaymentStatus == PaymentStatus.Refunded, cancellationToken);
        var previousRefunds = await previousOrders.CountAsync(order => order.PaymentStatus == PaymentStatus.Refunded, cancellationToken);
        var currentReturningRate = currentCustomerCount == 0 ? 0m : currentReturningCount * 100m / currentCustomerCount;
        var previousReturningRate = previousCustomerCount == 0 ? 0m : previousReturningCount * 100m / previousCustomerCount;
        var currentRefundRate = currentOrderCount == 0 ? 0m : currentRefunds * 100m / currentOrderCount;
        var previousRefundRate = previousOrderCount == 0 ? 0m : previousRefunds * 100m / previousOrderCount;

        var chartStart = new DateTime(period.End.Year, period.End.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-5);
        var monthlyRows = await Db.Orders
            .Where(order => order.OrderDate >= chartStart && order.OrderDate < period.End && order.PaymentStatus == PaymentStatus.Paid && order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Declined)
            .GroupBy(order => new { order.OrderDate.Year, order.OrderDate.Month })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                Revenue = group.Sum(order => order.TotalAmount)
            })
            .ToListAsync(cancellationToken);
        var monthLookup = monthlyRows.ToDictionary(item => new DateTime(item.Year, item.Month, 1, 0, 0, 0, DateTimeKind.Utc), item => item.Revenue);
        var maximumRevenue = monthlyRows.Count == 0 ? 0m : monthlyRows.Max(item => item.Revenue);
        var revenueBars = Enumerable.Range(0, 6)
            .Select(offset =>
            {
                var month = chartStart.AddMonths(offset);
                var revenue = monthLookup.TryGetValue(month, out var rev) ? rev : 0m;
                return new ReportBar
                {
                    Label = month.ToString("MMM", CultureInfo.InvariantCulture),
                    Value = CompactCurrency(revenue),
                    Height = maximumRevenue <= 0m ? 0 : (int)Math.Round(revenue / maximumRevenue * 100m, MidpointRounding.AwayFromZero),
                    Tone = offset == 5 ? "current" : "sage"
                };
            })
            .ToList();

        var currentFarmerRows = await Db.OrderItems
            .Where(item => item.Order.OrderDate >= period.Start && item.Order.OrderDate < period.End && item.Order.PaymentStatus == PaymentStatus.Paid && item.Order.Status != OrderStatus.Cancelled && item.Order.Status != OrderStatus.Declined)
            .GroupBy(item => item.FarmerName)
            .Select(group => new
            {
                Name = group.Key,
                Orders = group.Select(item => item.OrderId).Distinct().Count(),
                Revenue = group.Sum(item => item.Quantity * item.UnitPrice)
            })
            .OrderByDescending(item => item.Revenue)
            .Take(5)
            .ToListAsync(cancellationToken);
        var previousFarmerRows = await Db.OrderItems
            .Where(item => item.Order.OrderDate >= period.PreviousStart && item.Order.OrderDate < period.PreviousEnd && item.Order.PaymentStatus == PaymentStatus.Paid && item.Order.Status != OrderStatus.Cancelled && item.Order.Status != OrderStatus.Declined)
            .GroupBy(item => item.FarmerName)
            .Select(group => new { Name = group.Key, Revenue = group.Sum(item => item.Quantity * item.UnitPrice) })
            .ToListAsync(cancellationToken);
        var previousFarmerLookup = previousFarmerRows.ToDictionary(item => item.Name, item => item.Revenue);
        var farmerNames = currentFarmerRows.Select(row => row.Name).ToList();
        var farmerStatuses = await Db.FarmerProfiles
            .AsNoTracking()
            .Where(profile => farmerNames.Contains(profile.FarmName))
            .Select(profile => new { profile.FarmName, profile.Status, profile.User.IsActive })
            .ToDictionaryAsync(item => item.FarmName, StringComparer.OrdinalIgnoreCase);
        var topFarmers = currentFarmerRows.Select(row =>
        {
            var previous = previousFarmerLookup.TryGetValue(row.Name, out var prev) ? prev : 0m;
            var change = Percentage(row.Revenue, previous);
            var knownProfile = farmerStatuses.TryGetValue(row.Name, out var profile) ? profile : null;
            var isActive = knownProfile is not null && knownProfile.Status == FarmerStatus.Active && knownProfile.IsActive;
            return new ReportTableRow
            {
                Name = row.Name,
                Detail = knownProfile switch
                {
                    null => "Unlinked farmer record",
                    { Status: FarmerStatus.Active, IsActive: true } => "Active marketplace farmer",
                    { Status: FarmerStatus.PendingApproval } => "Awaiting admin approval",
                    { Status: FarmerStatus.Suspended } => "Suspended by admin",
                    _ => "Rejected by admin"
                },
                Tone = isActive ? "success" : "warning",
                Initials = Initials(row.Name, null),
                Orders = row.Orders.ToString("N0", CultureInfo.GetCultureInfo("en-US")),
                Revenue = row.Revenue.ToString("C0", CultureInfo.GetCultureInfo("en-US")),
                RevenueValue = row.Revenue,
                Change = change,
                ChangeTone = change.StartsWith('-') ? "danger" : "success"
            };
        }).ToList();

        var productRows = await Db.OrderItems
            .Where(item => item.Order.OrderDate >= period.Start && item.Order.OrderDate < period.End && item.Order.PaymentStatus == PaymentStatus.Paid && item.Order.Status != OrderStatus.Cancelled && item.Order.Status != OrderStatus.Declined)
            .GroupBy(item => item.ProductName)
            .Select(group => new
            {
                Name = group.Key,
                UnitsSold = group.Sum(item => item.Quantity),
                Revenue = group.Sum(item => item.Quantity * item.UnitPrice)
            })
            .OrderByDescending(item => item.UnitsSold)
            .Take(5)
            .ToListAsync(cancellationToken);
        var productNames = productRows.Select(item => item.Name).ToArray();
        var categoryLookup = productNames.Length == 0
            ? new Dictionary<string, string>()
            : await Db.Products
                .AsNoTracking()
                .Where(product => productNames.Contains(product.Name))
                .GroupBy(product => product.Name)
                .Select(group => new { Name = group.Key, CategoryName = group.Select(product => product.Category.Name).First() })
                .ToDictionaryAsync(product => product.Name, product => product.CategoryName, cancellationToken);
        var topProducts = new List<PopularProduct>();
        for (var index = 0; index < productRows.Count; index++)
        {
            var row = productRows[index];
            topProducts.Add(new PopularProduct
            {
                Name = row.Name,
                Category = categoryLookup.TryGetValue(row.Name, out var cat) ? cat : "Uncategorized",
                UnitsSold = row.UnitsSold,
                Revenue = row.Revenue,
                Initials = Initials(row.Name, null),
                ImageTone = new[] { "sage", "amber", "coral", "violet", "blue" }[index % 5]
            });
        }

        var marketRows = await Db.Orders
            .Where(order => order.OrderDate >= period.Start && order.OrderDate < period.End && order.PaymentStatus == PaymentStatus.Paid && order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Declined)
            .GroupBy(order => new { order.MarketId, order.Market.Name, order.Market.City, order.Market.OperatingDays })
            .Select(group => new
            {
                group.Key.Name,
                group.Key.City,
                group.Key.OperatingDays,
                Orders = group.Count(),
                Revenue = group.Sum(order => order.TotalAmount)
            })
            .OrderByDescending(item => item.Revenue)
            .ToListAsync(cancellationToken);

        var marketFarmerCounts = await Db.OrderItems
            .Where(item => item.Order.OrderDate >= period.Start && item.Order.OrderDate < period.End && item.Order.PaymentStatus == PaymentStatus.Paid && item.Order.Status != OrderStatus.Cancelled && item.Order.Status != OrderStatus.Declined)
            .GroupBy(item => item.Order.Market.Name)
            .Select(group => new { MarketName = group.Key, Farmers = group.Select(item => item.FarmerName).Distinct().Count() })
            .ToDictionaryAsync(item => item.MarketName, item => item.Farmers, cancellationToken);
        var revenueByMarket = marketRows.Select(row => new ReportTableRow
        {
            Name = row.Name,
            Detail = string.IsNullOrWhiteSpace(row.City) ? row.OperatingDays : $"{row.City} - {row.OperatingDays}",
            Tone = "success",
            Initials = Initials(row.Name, null),
            Orders = row.Orders.ToString("N0", CultureInfo.GetCultureInfo("en-US")),
            Revenue = row.Revenue.ToString("C0", CultureInfo.GetCultureInfo("en-US")),
            RevenueValue = row.Revenue,
            Change = $"{(marketFarmerCounts.TryGetValue(row.Name, out var farmerCount) ? farmerCount : 0)} active",
            ChangeTone = "success"
        }).ToList();

        var auditLogs = await Db.AuditLogs
            .AsNoTracking()
            .Include(log => log.User)
            .OrderByDescending(log => log.Timestamp)
            .Take(8)
            .ToListAsync(cancellationToken);
        var recentActions = auditLogs.Select(log => new AuditSummary
        {
            Id = log.Id,
            Action = log.Action,
            EntityName = log.EntityName,
            Details = log.Details,
            Actor = log.User is null ? "System" : $"{log.User.FirstName} {log.User.LastName}".Trim(),
            TimeLabel = RelativeTime(log.Timestamp)
        }).ToList();

        return new ReportViewModel
        {
            SelectedPeriod = period.Name,
            DateRangeLabel = $"{period.Start.ToLocalTime():MMM d, yyyy} - {(period.End.AddTicks(-1)).ToLocalTime():MMM d, yyyy}",
            Metrics = new List<ReportMetric>
            {
                new() { Label = "Net sales", Value = currentRevenue.ToString("C0", CultureInfo.GetCultureInfo("en-US")), Change = Percentage(currentRevenue, previousRevenue), ChangeTone = Tone(currentRevenue, previousRevenue), Icon = "bi-graph-up-arrow", IconTone = "sage" },
                new() { Label = "Orders", Value = currentOrderCount.ToString("N0", CultureInfo.GetCultureInfo("en-US")), Change = Percentage(currentOrderCount, previousOrderCount), ChangeTone = Tone(currentOrderCount, previousOrderCount), Icon = "bi-bag-check", IconTone = "blue" },
                new() { Label = "Returning customers", Value = $"{currentReturningRate:0.0}%", Change = Percentage(currentReturningRate, previousReturningRate), ChangeTone = Tone(currentReturningRate, previousReturningRate), Icon = "bi-arrow-repeat", IconTone = "violet" },
                new() { Label = "Refund rate", Value = $"{currentRefundRate:0.0}%", Change = Percentage(currentRefundRate, previousRefundRate), ChangeTone = currentRefundRate <= previousRefundRate ? "success" : "danger", Icon = "bi-arrow-counterclockwise", IconTone = "amber" }
            },
            RevenueByMonth = revenueBars,
            TopFarmers = topFarmers,
            RevenueByMarket = revenueByMarket,
            TopProducts = topProducts,
            RecentAdminActions = recentActions
        };
    }

    private static ReportPeriod ResolvePeriod(string? requestedPeriod)
    {
        var name = NormalizePeriod(requestedPeriod);
        var now = DateTime.UtcNow;
        var tomorrow = now.Date.AddDays(1);
        if (name == "Last 7 days")
        {
            var start = now.Date.AddDays(-6);
            return new ReportPeriod(name, start, tomorrow, start.AddDays(-7), start, $"Last 7 days ending {now:MMM d, yyyy}");
        }

        if (name == "This year")
        {
            var start = new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return new ReportPeriod(name, start, tomorrow, start.AddYears(-1), start, $"January 1 - {now:MMMM d, yyyy}");
        }

        var monthStart = now.Date.AddDays(-29);
        return new ReportPeriod("Last 30 days", monthStart, tomorrow, monthStart.AddDays(-30), monthStart, $"Last 30 days ending {now:MMM d, yyyy}");
    }

    private static string NormalizePeriod(string? period)
    {
        var value = Clean(period);
        return value.Equals("Last 7 days", StringComparison.OrdinalIgnoreCase) || value.Equals("This year", StringComparison.OrdinalIgnoreCase) ? value : "Last 30 days";
    }

    private static string Percentage(decimal current, decimal previous)
    {
        if (previous == 0m)
        {
            return current == 0m ? "0.0%" : "New";
        }

        var value = (current - previous) / previous * 100m;
        return $"{(value >= 0m ? "+" : string.Empty)}{value:0.0}%";
    }

    private static string Tone(decimal current, decimal previous) => current >= previous ? "success" : "danger";

    private static string CsvCell(string? value)
    {
        var normalized = value ?? string.Empty;
        if (normalized.StartsWith('=') || normalized.StartsWith('+') || normalized.StartsWith('-') || normalized.StartsWith('@'))
        {
            normalized = "'" + normalized;
        }

        return $"\"{normalized.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private sealed record ReportPeriod(
        string Name,
        DateTime Start,
        DateTime End,
        DateTime PreviousStart,
        DateTime PreviousEnd,
        string Label);
}
