using System.ComponentModel.DataAnnotations;
using MarketLinkWebsite.Validation;

namespace MarketLinkWebsite.Areas.Admin.Models;

public sealed class SettingsViewModel
{
    [StringLength(80)]
    [BusinessName]
    public string PlatformName { get; set; } = "MarketLink";

    [EmailAddress]
    [StringLength(150)]
    public string SupportEmail { get; set; } = string.Empty;

    [StringLength(40)]
    public string SupportPhone { get; set; } = string.Empty;

    [StringLength(200)]
    public string SupportAddress { get; set; } = string.Empty;

    [Range(-90, 90, ErrorMessage = "Latitude must be between -90 and 90.")]
    public double Latitude { get; set; }

    [Range(-180, 180, ErrorMessage = "Longitude must be between -180 and 180.")]
    public double Longitude { get; set; }

    [StringLength(6)]
    public string CurrencySymbol { get; set; } = "$";

    [Range(1, 90, ErrorMessage = "Booking window must be between 1 and 90 days.")]
    public int MarketBookingWindowDays { get; set; } = 14;

    public bool AllowGuestBrowsing { get; set; } = true;
    public bool ShowSoldOutItems { get; set; } = true;

    [StringLength(200)]
    public string AnnouncementBar { get; set; } = string.Empty;

    [StringLength(120)]
    public string GoogleMapsApiKey { get; set; } = string.Empty;

    [AllowedValues("openstreetmap", "google", ErrorMessage = "Choose OpenStreetMap or Google Maps.")]
    public string MapProvider { get; set; } = "openstreetmap";
    [AllowedValues("gemini", "openai", "local", "none", ErrorMessage = "Choose a supported assistant provider.")]
    public string AiProvider { get; set; } = "local";
    public bool AiApiKeyPresent { get; set; }
    public List<string> Roles { get; set; } = new();
    public int TotalUsers { get; set; }
    public int TotalFarmers { get; set; }
    public int ActiveFarmers { get; set; }
    public int PendingFarmers { get; set; }
    public int TotalCustomers { get; set; }
    public int TotalMarkets { get; set; }
    public int ActiveMarkets { get; set; }
    public int InactiveMarkets { get; set; }
    public int TotalProducts { get; set; }
    public int LiveProducts { get; set; }
    public int TotalOrders { get; set; }
    public int TotalReviews { get; set; }
    public int TotalCategories { get; set; }
    public int ActiveCategories { get; set; }
}
