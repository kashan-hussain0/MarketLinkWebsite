using MarketLinkWebsite.Models.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketLinkWebsite.Models.Entities;

public class FarmerProfile
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;

    [Required, MaxLength(150)]
    public string FarmName { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [Required, MaxLength(250)]
    public string Address { get; set; } = string.Empty;

    [MaxLength(100)]
    public string City { get; set; } = string.Empty;

    [MaxLength(100)]
    public string OperatingDays { get; set; } = string.Empty;

    [MaxLength(100)]
    public string PickupWindows { get; set; } = string.Empty;

    public int OrderCutoffHours { get; set; } = 12;
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public FarmerStatus Status { get; set; } = FarmerStatus.PendingApproval;
    public decimal Rating { get; set; }
    public int ReviewCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<MarketFarmer> MarketFarmers { get; set; } = new List<MarketFarmer>();
    public ICollection<Product> Products { get; set; } = new List<Product>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<FavouriteFarmer> FavoritedByUsers { get; set; } = new List<FavouriteFarmer>();
    public ICollection<PickupSlot> PickupSlots { get; set; } = new List<PickupSlot>();
}
