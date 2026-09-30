using System.ComponentModel.DataAnnotations;
using MarketLinkWebsite.Validation;
using Microsoft.AspNetCore.Http;

namespace MarketLinkWebsite.Areas.Farmer.Controllers;

public sealed class FarmerDashboardViewModel
{
    public string FarmerName { get; set; } = string.Empty;
    public string FarmName { get; set; } = string.Empty;
    public string Greeting { get; set; } = string.Empty;
    public string DateLabel { get; set; } = string.Empty;
    public string FarmImageUrl { get; set; } = string.Empty;
    public string WelcomeTitle { get; set; } = string.Empty;
    public string WelcomeMessage { get; set; } = string.Empty;
    public string RevenuePeriodLabel { get; set; } = string.Empty;
    public string RevenueTotalLabel { get; set; } = string.Empty;
    public decimal MonthlyRevenue { get; set; }
    public string RevenueChange { get; set; } = string.Empty;
    public int PendingOrders { get; set; }
    public int ActiveProducts { get; set; }
    public int LowStockProducts { get; set; }
    public int NewReviews { get; set; }
    public int UnreadNotifications { get; set; }
    public List<FarmerMetricViewModel> Metrics { get; set; } = new();
    public List<FarmerRevenuePointViewModel> RevenuePoints { get; set; } = new();
    public List<FarmerDashboardOrderViewModel> RecentOrders { get; set; } = new();
    public List<FarmerActivityViewModel> Activities { get; set; } = new();
}

public sealed class FarmerMetricViewModel
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Icon { get; set; } = "bi-graph-up-arrow";
    public string Tone { get; set; } = "green";
    public string? Trend { get; set; }
}

public sealed class FarmerRevenuePointViewModel
{
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int BarHeight { get; set; }
    public bool IsCurrent { get; set; }
}

public sealed class FarmerDashboardOrderViewModel
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerInitials { get; set; } = string.Empty;
    public string ItemSummary { get; set; } = string.Empty;
    public string PlacedLabel { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusTone { get; set; } = "green";
    public string StatusIcon { get; set; } = "bi-check2";
}

public sealed class FarmerActivityViewModel
{
    public string Icon { get; set; } = "bi-bell";
    public string Title { get; set; } = string.Empty;
    [StringLength(2000, MinimumLength = 10, ErrorMessage = "Description must be between 10 and 2,000 characters.")]
    public string Description { get; set; } = string.Empty;
    public string TimeLabel { get; set; } = string.Empty;
    public string Tone { get; set; } = "green";
    public string? ActionUrl { get; set; }
    public DateTime ActivityAt { get; set; }
}

public sealed class ProductListViewModel
{
    public string SearchTerm { get; set; } = string.Empty;
    public string StatusFilter { get; set; } = "All products";
    public int TotalProducts { get; set; }
    public int ActiveProducts { get; set; }
    public int LowStockProducts { get; set; }
    public int RemovedProducts { get; set; }
    public string? Notice { get; set; }
    public List<FarmerCategoryOption> Categories { get; set; } = new();
    public List<FarmerProductSummaryViewModel> Products { get; set; } = new();
}

public sealed class FarmerCategoryOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class FarmerProductSummaryViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public int ReorderThreshold { get; set; }
    public bool IsOrganic { get; set; }
    public bool IsAvailable { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusTone { get; set; } = "green";
    public string UpdatedLabel { get; set; } = string.Empty;
    public bool RecurringStockEnabled { get; set; }
    public int MondayStock { get; set; }
    public int TuesdayStock { get; set; }
    public int WednesdayStock { get; set; }
    public int ThursdayStock { get; set; }
    public int FridayStock { get; set; }
    public int SaturdayStock { get; set; }
    public int SundayStock { get; set; }
    public int WeeklyStockTotal => MondayStock + TuesdayStock + WednesdayStock + ThursdayStock + FridayStock + SaturdayStock + SundayStock;
}

public sealed class ProductFormViewModel
{
    public int Id { get; set; }

    [Required, StringLength(150, MinimumLength = 2)]
    [BusinessName(ErrorMessage = "Enter a product name using letters and numbers only.")]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string Category { get; set; } = string.Empty;

    [Required, Range(typeof(decimal), "0.01", "100000")]
    public decimal Price { get; set; }

    [Required, StringLength(30)]
    public string Unit { get; set; } = "per piece";

    [Required, Range(0, 1000000)]
    public int Stock { get; set; }

    [Range(0, 1000000)]
    public int ReorderThreshold { get; set; } = 5;

    [Required, StringLength(600, MinimumLength = 10)]
    public string Description { get; set; } = string.Empty;

    [StringLength(300)]
    public string? ImageUrl { get; set; }

    public bool IsOrganic { get; set; }
    public bool IsAvailable { get; set; } = true;
    public bool RecurringStockEnabled { get; set; }
    [Range(0, 1000000)] public int MondayStock { get; set; }
    [Range(0, 1000000)] public int TuesdayStock { get; set; }
    [Range(0, 1000000)] public int WednesdayStock { get; set; }
    [Range(0, 1000000)] public int ThursdayStock { get; set; }
    [Range(0, 1000000)] public int FridayStock { get; set; }
    [Range(0, 1000000)] public int SaturdayStock { get; set; }
    [Range(0, 1000000)] public int SundayStock { get; set; }
    public List<FarmerCategoryOption> Categories { get; set; } = new();
    public string? ExistingImageUrl { get; set; }
    [ImageUpload(8, "Choose a JPG, PNG, GIF, BMP or WEBP picture up to 8 MB.")]
    public IFormFile? ImageFile { get; set; }
    public string? Notice { get; set; }
}

public sealed class OrderListViewModel
{
    public string StatusFilter { get; set; } = "All orders";
    public int TotalOrders { get; set; }
    public int NewOrders { get; set; }
    public int AcceptedOrders { get; set; }
    public int ToPrepare { get; set; }
    public int ReadyForPickup { get; set; }
    public int CompletedOrders { get; set; }
    public int DeclinedOrders { get; set; }
    public string? Notice { get; set; }
    public List<FarmerOrderSummaryViewModel> Orders { get; set; } = new();
}

public sealed class FarmerOrderSummaryViewModel
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerInitials { get; set; } = string.Empty;
    public string CustomerLocation { get; set; } = string.Empty;
    public string ItemSummary { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public string PlacedLabel { get; set; } = string.Empty;
    public string PickupWindow { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public decimal OwnSubtotal { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusValue { get; set; } = string.Empty;
    public string StatusTone { get; set; } = "green";
    public string StatusIcon { get; set; } = "bi-check2";
    public bool CanAccept { get; set; }
    public bool CanPrepare { get; set; }
    public bool CanReady { get; set; }
    public bool CanComplete { get; set; }
    public bool CanDecline { get; set; }
    public bool CanCancel { get; set; }
}

public sealed class OrderDetailViewModel
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerInitials { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerAddress { get; set; } = string.Empty;
    public string PlacedLabel { get; set; } = string.Empty;
    public string PickupWindow { get; set; } = string.Empty;
    public string PickupLocation { get; set; } = string.Empty;
    public string PickupAddress { get; set; } = string.Empty;
    public string CutoffLabel { get; set; } = string.Empty;
    public string PaymentLabel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusValue { get; set; } = string.Empty;
    public string StatusTone { get; set; } = "green";
    public string StatusIcon { get; set; } = "bi-check2";
    public decimal Subtotal { get; set; }
    public decimal ServiceFee { get; set; }
    public decimal Total { get; set; }
    public bool HasOtherFarmerItems { get; set; }
    public string? Notice { get; set; }
    public List<FarmerOrderLineViewModel> Items { get; set; } = new();
    public List<FarmerOrderHistoryViewModel> StatusHistory { get; set; } = new();
    public bool CanAccept { get; set; }
    public bool CanPrepare { get; set; }
    public bool CanReady { get; set; }
    public bool CanComplete { get; set; }
    public bool CanDecline { get; set; }
    public bool CanCancel { get; set; }
}

public sealed class FarmerOrderLineViewModel
{
    public int? ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Variant { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal LineTotal => Price * Quantity;
}

public sealed class FarmerOrderHistoryViewModel
{
    public string Status { get; set; } = string.Empty;
    public string StatusTone { get; set; } = "green";
    public string ChangedAtLabel { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public string ChangedBy { get; set; } = string.Empty;
}

public sealed class FarmerMarketOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    [Required(ErrorMessage = "Enter the days you trade.")]
    public string OperatingDays { get; set; } = string.Empty;
    public string OpenTime { get; set; } = string.Empty;
    public string CloseTime { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
}

public sealed class FarmerProfileViewModel
{
    [Required, StringLength(100, MinimumLength = 2)]
    [PersonName]
    public string OwnerName { get; set; } = string.Empty;

    [Required, StringLength(150, MinimumLength = 2)]
    [BusinessName]
    public string FarmName { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, Phone, StringLength(30)]
    [PhoneNumber]
    public string Phone { get; set; } = string.Empty;

    [Required, StringLength(250, MinimumLength = 5)]
    [StreetAddress]
    public string Address { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 2)]
    [BusinessName]
    public string City { get; set; } = string.Empty;

    [StringLength(600)]
    public string Bio { get; set; } = string.Empty;

    [StringLength(100)]
    public string OperatingDays { get; set; } = string.Empty;

    [StringLength(100)]
    [Required(ErrorMessage = "Enter your pickup window, for example 09:00 AM - 12:00 PM.")]
    public string PickupWindows { get; set; } = string.Empty;

    [Range(-90, 90)]
    public decimal Latitude { get; set; }

    [Range(-180, 180)]
    public decimal Longitude { get; set; }

    public List<int> MarketIds { get; set; } = new();
    public List<FarmerMarketOption> Markets { get; set; } = new();

    [Range(0, 168)]
    public int OrderCutoffHours { get; set; } = 12;

    public string FarmImageUrl { get; set; } = string.Empty;
    public string MemberSince { get; set; } = string.Empty;
    public string VerificationStatus { get; set; } = string.Empty;
    public string OwnerInitials { get; set; } = string.Empty;
    public decimal Rating { get; set; }
    public int ReviewCount { get; set; }
    public int ListingCount { get; set; }
    public int FollowerCount { get; set; }
    public int ProfileStrength { get; set; }
    [ImageUpload(8, "Choose a JPG, PNG, GIF, BMP or WEBP picture up to 8 MB.")]
    public IFormFile? FarmImageFile { get; set; }
    public string? Notice { get; set; }
}

public sealed class ReviewListViewModel
{
    public string Filter { get; set; } = "All reviews";
    public decimal AverageRating { get; set; }
    public int ReviewCount { get; set; }
    public int ResponseCount { get; set; }
    public int FiveStarPercentage { get; set; }
    public int FourStarPercentage { get; set; }
    public int ThreeStarPercentage { get; set; }
    public int TwoStarPercentage { get; set; }
    public int OneStarPercentage { get; set; }
    public string InsightTitle { get; set; } = string.Empty;
    public string InsightText { get; set; } = string.Empty;
    public string? Notice { get; set; }
    public List<FarmerReviewViewModel> Reviews { get; set; } = new();
}

public sealed class FarmerReviewViewModel
{
    public int Id { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerInitials { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string Comment { get; set; } = string.Empty;
    public string DateLabel { get; set; } = string.Empty;
    public string? Response { get; set; }
    public string? ReplyText { get; set; }
    public bool VerifiedPurchase { get; set; }
    public bool HasResponse => !string.IsNullOrWhiteSpace(Response);
}

public sealed class NotificationListViewModel
{
    public int UnreadCount { get; set; }
    public int TotalCount { get; set; }
    public string? Notice { get; set; }
    public List<FarmerNotificationViewModel> Notifications { get; set; } = new();
}

public sealed class FarmerNotificationViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string TimeLabel { get; set; } = string.Empty;
    public string Icon { get; set; } = "bi-bell";
    public string Tone { get; set; } = "green";
    public bool IsRead { get; set; }
    public string? ActionUrl { get; set; }
}
