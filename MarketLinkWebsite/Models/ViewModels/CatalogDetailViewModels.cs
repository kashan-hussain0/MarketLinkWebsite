namespace MarketLinkWebsite.Models.ViewModels;

public sealed class ProductDetailViewModel
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
    public double Rating { get; init; }
    public int ReviewCount { get; init; }
    public bool IsOrganic { get; init; }
    public bool IsFavorite { get; init; }
    public string? Badge { get; init; }
    public List<ProductReviewViewModel> Reviews { get; init; } = new();
    public int[] StarDistribution { get; init; } = new int[6];
    public int StarTotal => StarDistribution.Skip(1).Sum();
    public bool CanReview { get; init; }
    public bool HasReviewed { get; init; }
    public double FarmerRating { get; init; }
    public int FarmerReviewCount { get; init; }
    public string FarmImageUrl { get; init; } = string.Empty;
    public string FarmInitials { get; init; } = string.Empty;
    public string FarmCity { get; init; } = string.Empty;
    public string FarmAddress { get; init; } = string.Empty;
    public string FarmOperatingDays { get; init; } = string.Empty;
    public string FarmPickupWindows { get; init; } = string.Empty;
    public double FarmLatitude { get; init; }
    public double FarmLongitude { get; init; }
    public string FarmMapUrl { get; init; } = string.Empty;
    public int FarmProductCount { get; init; }
    public int FarmReviewTotal { get; init; }
    public string MarketAddress { get; init; } = string.Empty;
    public string MarketOpenTime { get; init; } = string.Empty;
    public string MarketCloseTime { get; init; } = string.Empty;
    public string MarketMapUrl { get; init; } = string.Empty;
    public double MarketLatitude { get; init; }
    public double MarketLongitude { get; init; }
    public string MarketDirectionsUrl { get; init; } = string.Empty;
    public string FarmDirectionsUrl { get; init; } = string.Empty;
}

public sealed class ProductReviewViewModel
{
    public int Id { get; init; }
    public string ReviewerName { get; init; } = string.Empty;
    public string Initials { get; init; } = string.Empty;
    public int Rating { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string DateLabel { get; init; } = string.Empty;
    public int HelpfulCount { get; init; }
    public bool VerifiedPurchase { get; init; }
    public bool IsMine { get; init; }
    public string? FarmerReply { get; init; }
}

public sealed class CatalogFilterOptions
{
    public IReadOnlyList<string> Categories { get; init; } = [];
    public IReadOnlyList<string> Markets { get; init; } = [];
    public IReadOnlyList<string> MarketDays { get; init; } = [];
    public decimal MaxCatalogPrice { get; init; }
    public string NextMarketLabel { get; init; } = "Check the market calendar";
}

public sealed class FarmSpotlightViewModel
{
    public int Id { get; init; }
    public string FarmName { get; init; } = string.Empty;
    public string OwnerName { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public string Initials { get; init; } = string.Empty;
    public decimal Rating { get; init; }
    public int ReviewCount { get; init; }
    public int ProductCount { get; init; }
    public string ProductNames { get; init; } = string.Empty;
    public string Markets { get; init; } = string.Empty;
}
