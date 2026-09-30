using System.ComponentModel.DataAnnotations;

namespace MarketLinkWebsite.Models.Entities;

public class Market
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    [Required, MaxLength(250)]
    public string Address { get; set; } = string.Empty;

    [MaxLength(100)]
    public string City { get; set; } = string.Empty;

    [MaxLength(100)]
    public string OperatingDays { get; set; } = string.Empty;

    [MaxLength(20)]
    public string OpenTime { get; set; } = string.Empty;

    [MaxLength(20)]
    public string CloseTime { get; set; } = string.Empty;

    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<MarketFarmer> MarketFarmers { get; set; } = new List<MarketFarmer>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<FavouriteMarket> FavouritedByUsers { get; set; } = new List<FavouriteMarket>();
}
