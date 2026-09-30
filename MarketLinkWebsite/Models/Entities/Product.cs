using MarketLinkWebsite.Models.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketLinkWebsite.Models.Entities;

public class Product
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public UnitType Unit { get; set; } = UnitType.Kg;
    public string ImageUrl { get; set; } = string.Empty;
    public bool IsOrganic { get; set; }
    public bool IsAvailable { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int FarmerProfileId { get; set; }

    [ForeignKey(nameof(FarmerProfileId))]
    public FarmerProfile FarmerProfile { get; set; } = null!;

    public int CategoryId { get; set; }

    [ForeignKey(nameof(CategoryId))]
    public Category Category { get; set; } = null!;

    public Inventory? Inventory { get; set; }
    public WeeklyStockPlan? WeeklyStockPlan { get; set; }
    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<FavouriteProduct> FavouritedByUsers { get; set; } = new List<FavouriteProduct>();
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
}
