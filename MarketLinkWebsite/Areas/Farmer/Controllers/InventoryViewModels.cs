using System.ComponentModel.DataAnnotations;
using MarketLinkWebsite.Validation;

namespace MarketLinkWebsite.Areas.Farmer.Controllers;

public sealed class InventoryViewModel
{
    public string Filter { get; set; } = "All items";
    public int TotalItems { get; set; }
    public int ListedItems { get; set; }
    public int LowStockItems { get; set; }
    public int SoldOutItems { get; set; }
    public int TotalUnits { get; set; }
    public string? Notice { get; set; }
    public List<InventoryItemViewModel> Items { get; set; } = new();
}

public sealed class InventoryItemViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public int Stock { get; set; }
    public int ReorderThreshold { get; set; }
    public bool IsListed { get; set; }
    public bool IsOrganic { get; set; }
    public string UpdatedLabel { get; set; } = string.Empty;
    public bool PlanEnabled { get; set; }
    public int MondayStock { get; set; }
    public int TuesdayStock { get; set; }
    public int WednesdayStock { get; set; }
    public int ThursdayStock { get; set; }
    public int FridayStock { get; set; }
    public int SaturdayStock { get; set; }
    public int SundayStock { get; set; }

    public bool IsLowStock => IsListed && Stock <= ReorderThreshold;
    public bool IsSoldOut => Stock == 0;
    public bool IsPaused => !IsListed;
    public int WeeklyTotal => MondayStock + TuesdayStock + WednesdayStock + ThursdayStock + FridayStock + SaturdayStock + SundayStock;
    public int TodayStock => DateTime.UtcNow.DayOfWeek switch
    {
        DayOfWeek.Monday => MondayStock,
        DayOfWeek.Tuesday => TuesdayStock,
        DayOfWeek.Wednesday => WednesdayStock,
        DayOfWeek.Thursday => ThursdayStock,
        DayOfWeek.Friday => FridayStock,
        DayOfWeek.Saturday => SaturdayStock,
        _ => SundayStock
    };
}

public sealed class InventoryWeeklyPlanForm
{
    public int Id { get; set; }

    public bool Enabled { get; set; }

    [Range(0, 1000000)]
    public int MondayStock { get; set; }

    [Range(0, 1000000)]
    public int TuesdayStock { get; set; }

    [Range(0, 1000000)]
    public int WednesdayStock { get; set; }

    [Range(0, 1000000)]
    public int ThursdayStock { get; set; }

    [Range(0, 1000000)]
    public int FridayStock { get; set; }

    [Range(0, 1000000)]
    public int SaturdayStock { get; set; }

    [Range(0, 1000000)]
    public int SundayStock { get; set; }

    public bool ApplyToToday { get; set; }
}
