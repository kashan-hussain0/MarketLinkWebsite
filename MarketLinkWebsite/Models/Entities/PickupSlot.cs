using System.ComponentModel.DataAnnotations;

namespace MarketLinkWebsite.Models.Entities;

/// <summary>
/// A time window a farmer has opened for pickup on a given weekday. A farmer keeps
/// these so customers can only book the slots they actually work on, instead of
/// every hour the market happens to be open.
/// </summary>
public class PickupSlot
{
    public int Id { get; set; }

    public int FarmerProfileId { get; set; }

    public FarmerProfile FarmerProfile { get; set; } = null!;

    [Range(0, 6)]
    public int DayOfWeek { get; set; }

    [Required, MaxLength(5)]
    public string StartTime { get; set; } = string.Empty;

    [Required, MaxLength(5)]
    public string EndTime { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
