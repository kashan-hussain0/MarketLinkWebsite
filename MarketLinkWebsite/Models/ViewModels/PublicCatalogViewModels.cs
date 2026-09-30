using MarketLinkWebsite.Services;
using MarketLinkWebsite.Validation;
using System.ComponentModel.DataAnnotations;

namespace MarketLinkWebsite.Models.ViewModels;

public class ProductCardViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ShortDescription { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string Unit { get; init; } = string.Empty;
    public int QuantityAvailable { get; init; }
    public string ImageUrl { get; init; } = string.Empty;
    public string FarmerName { get; init; } = string.Empty;
    public string FarmName { get; init; } = string.Empty;
    public int FarmerProfileId { get; init; }
    public string MarketName { get; init; } = string.Empty;
    public string MarketDay { get; init; } = string.Empty;
    public double DistanceKm { get; init; }
    public bool HasDistance { get; init; }
    public string DistanceLabel => HasDistance ? GeoDistance.Label(DistanceKm) : string.Empty;
    public double Rating { get; init; }
    public int ReviewCount { get; init; }
    public bool IsOrganic { get; init; }
    public bool IsFavorite { get; init; }
    public string? Badge { get; init; }
}

public class MarketCardViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string Day { get; init; } = string.Empty;
    public string OpenTime { get; init; } = string.Empty;
    public string CloseTime { get; init; } = string.Empty;
    public int FarmerCount { get; init; }
    public int ProductCount { get; init; }
    public IReadOnlyList<string> FarmNames { get; init; } = [];
    public double DistanceKm { get; init; }
    public bool HasDistance { get; init; }
    public string DistanceLabel => HasDistance ? GeoDistance.Label(DistanceKm) : string.Empty;
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public bool IsOpenThisWeek { get; init; }
    public string ImageUrl { get; init; } = string.Empty;
    public string EmbedUrl { get; init; } = string.Empty;
    public string ViewUrl { get; init; } = string.Empty;
    public string DirectionsUrl { get; init; } = string.Empty;
    public string MapProviderLabel { get; init; } = string.Empty;
    public bool IsFavorite { get; init; }
}

public class MarketFarmerCardViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string FarmName { get; init; } = string.Empty;
    public string Specialty { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ProductSummary { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public string StallNumber { get; init; } = string.Empty;
    public decimal Rating { get; init; }
    public int ProductCount { get; init; }
    public int LiveProductCount { get; init; }
}

public class HomeViewModel
{
    public IReadOnlyList<ProductCardViewModel> FeaturedProducts { get; init; } = [];
    public IReadOnlyList<MarketCardViewModel> Markets { get; init; } = [];
    public IReadOnlyList<CategoryTileViewModel> Categories { get; init; } = [];
}

public class CategoryTileViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public string IconClass { get; init; } = "bi-basket2";
    public int ProductCount { get; init; }
}

public class CheckoutBasketItem
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FarmName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Unit { get; set; } = "piece";
    public decimal UnitPrice { get; set; }
    public int InStock { get; set; }
    public List<CheckoutItemMarket> Markets { get; set; } = new();
}

public class CheckoutItemMarket
{
    public int MarketId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Day { get; set; } = string.Empty;
}

public class CheckoutMarketOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Day { get; set; } = string.Empty;
    public string OpenTime { get; set; } = string.Empty;
    public string CloseTime { get; set; } = string.Empty;
    public int FarmCount { get; set; }
    public List<string> FarmNames { get; set; } = new();
    public string FarmNamesLabel => string.Join(", ", FarmNames);
    public bool CoversWholeBasket { get; set; }
}
public class CheckoutFormViewModel
{
    [Required(ErrorMessage = "Select a pickup market.")]
    public int MarketId { get; set; } = 1;

    [DataType(DataType.Date)]
    [NotBeforeToday("Choose a pickup date from today onwards.")]
    [Display(Name = "Pickup date")]
    public DateTime PickupDate { get; set; } = DateTime.Today.AddDays(2);

    [Required(ErrorMessage = "Select a pickup time slot.")]
    [Display(Name = "Pickup time")]
    public string PickupSlot { get; set; } = "09:00 AM - 10:00 AM";

    [Required(ErrorMessage = "Enter your contact number.")]
    [Phone]
    [PhoneNumber]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your name.")]
    [MaxLength(80)]
    [PersonName]
    public string CustomerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the pickup address.")]
    [MaxLength(250)]
    [StreetAddress]
    public string PickupAddress { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the pickup city.")]
    [MaxLength(100)]
    [BusinessName]
    public string City { get; set; } = string.Empty;

    [MaxLength(500)]
    public string PickupNotes { get; set; } = string.Empty;
    public List<CheckoutBasketItem> BasketItems { get; set; } = new();
    public List<CheckoutMarketOption> MarketOptions { get; set; } = new();
    public List<string> BasketConflicts { get; set; } = new();
    public bool CanCheckout => BasketConflicts.Count == 0 && MarketOptions.Count > 0;
}

public class ContactFormViewModel
{
    [Required(ErrorMessage = "Enter your name.")]
    [MaxLength(80)]
    [PersonName]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter your email address.")]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose a help topic.")]
    [MaxLength(80)]
    public string Topic { get; set; } = "General question";

    [Required(ErrorMessage = "Enter your message.")]
    [MaxLength(1000)]
    public string Message { get; set; } = string.Empty;
}
