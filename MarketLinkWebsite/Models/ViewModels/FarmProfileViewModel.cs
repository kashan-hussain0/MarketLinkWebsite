using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Models.ViewModels;

public class FarmProfileViewModel
{
    public int Id { get; init; }
    public string FarmName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string OperatingDays { get; init; } = string.Empty;
    public string PickupWindows { get; init; } = string.Empty;
    public int OrderCutoffHours { get; init; }
    public decimal Latitude { get; init; }
    public decimal Longitude { get; init; }
    public decimal Rating { get; init; }
    public int ReviewCount { get; init; }
    public string ImageUrl { get; init; } = string.Empty;
    public string Initials { get; init; } = string.Empty;
    public string MemberSince { get; init; } = string.Empty;
    public List<string> Markets { get; init; } = new();
    public List<FarmStallViewModel> Stalls { get; init; } = new();
    public List<FarmProductViewModel> Products { get; init; } = new();
    public List<FarmReviewViewModel> Reviews { get; init; } = new();
    public bool IsFavorite { get; init; }
    public string MapEmbedUrl { get; init; } = string.Empty;
    public string MapViewUrl { get; init; } = string.Empty;
    public string DirectionsUrl { get; init; } = string.Empty;
    public string MapProviderLabel { get; init; } = string.Empty;
}

/// <summary>
/// One market a farm trades at, including the stall the farmer actually occupies.
/// </summary>
public class FarmStallViewModel
{
    public int MarketId { get; init; }
    public string MarketName { get; init; } = string.Empty;
    public string StallNumber { get; init; } = string.Empty;
    public string MarketDay { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string OpenTime { get; init; } = string.Empty;
    public string CloseTime { get; init; } = string.Empty;
    public string MapUrl { get; init; } = string.Empty;
    public string DirectionsUrl { get; init; } = string.Empty;
    public bool HasCoordinates { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}

public class FarmProductViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ShortDescription { get; init; } = string.Empty;
    public string Price { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public bool IsOrganic { get; init; }
    public int QuantityAvailable { get; init; }
    public decimal Rating { get; init; }
    public int ReviewCount { get; init; }
    public bool IsFavorite { get; init; }
}

public class FarmReviewViewModel
{
    public string ReviewerName { get; init; } = string.Empty;
    public string Initials { get; init; } = string.Empty;
    public int Rating { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string DateLabel { get; init; } = string.Empty;
    public string? FarmerReply { get; init; }
    public bool VerifiedPurchase { get; init; }
}
