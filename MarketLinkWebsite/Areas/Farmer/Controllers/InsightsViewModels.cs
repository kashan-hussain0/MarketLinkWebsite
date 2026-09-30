namespace MarketLinkWebsite.Areas.Farmer.Controllers;

public sealed class InsightsViewModel
{
    public string Period { get; set; } = "Last 30 days";
    public int TotalOrders { get; set; }
    public int PeriodOrders { get; set; }
    public int PendingOrders { get; set; }
    public int CompletedCount { get; set; }
    public int CancelledCount { get; set; }
    public decimal Revenue { get; set; }
    public string RevenueLabel { get; set; } = string.Empty;
    public decimal LifetimeRevenue { get; set; }
    public string LifetimeRevenueLabel { get; set; } = string.Empty;
    public decimal AverageOrderValue { get; set; }
    public string AverageOrderValueLabel { get; set; } = string.Empty;
    public List<WeeklySalesPoint> WeeklySales { get; set; } = new();
    public List<BestSellerViewModel> BestSellers { get; set; } = new();
    public List<InsightOrderRow> RecentOrders { get; set; } = new();
    public List<StatusBreakdownRow> StatusBreakdown { get; set; } = new();
    public int UnitsSold { get; set; }
    public string UnitsSoldLabel { get; set; } = string.Empty;
    public decimal TopProductShare { get; set; }
    public string TopProductShareLabel { get; set; } = string.Empty;
    public string BusiestDayLabel { get; set; } = "No sales recorded yet";
    public string PeriodNote { get; set; } = string.Empty;
}

public sealed class StatusBreakdownRow
{
    public string Label { get; set; } = string.Empty;
    public string StatusKey { get; set; } = string.Empty;
    public int Count { get; set; }
    public int Percent { get; set; }
    public string Tone { get; set; } = "green";
    public string Icon { get; set; } = "bi-check2-circle";
}

public sealed class WeeklySalesPoint
{
    public string Label { get; set; } = string.Empty;
    public string DateLabel { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public int Height { get; set; }
}

public sealed class BestSellerViewModel
{
    public string Name { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public int UnitsSold { get; set; }
    public decimal Revenue { get; set; }
    public string RevenueLabel { get; set; } = string.Empty;
}

public sealed class InsightOrderRow
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public string DateLabel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusKey { get; set; } = string.Empty;
    public string StatusTone { get; set; } = string.Empty;
    public string StatusIcon { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public decimal Subtotal { get; set; }
    public string SubtotalLabel { get; set; } = string.Empty;
}
