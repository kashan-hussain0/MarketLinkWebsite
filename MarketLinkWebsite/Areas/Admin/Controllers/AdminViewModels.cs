using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using MarketLinkWebsite.Models.Enums;
using MarketLinkWebsite.Validation;

namespace MarketLinkWebsite.Areas.Admin.Models;

public sealed class AdminDashboardViewModel
{
    public string AdminName { get; set; } = string.Empty;
    public string PeriodLabel { get; set; } = string.Empty;
    public decimal CurrentRevenue { get; set; }
    public decimal RevenueAxisMaximum { get; set; }
    public List<AdminMetric> Metrics { get; set; } = new();
    public List<RevenuePoint> RevenueTrend { get; set; } = new();
    public List<ActivityItem> RecentActivity { get; set; } = new();
    public List<FarmerSummary> PendingFarmers { get; set; } = new();
    public List<AdminNotificationItem> Notifications { get; set; } = new();
    public int UnreadNotificationCount { get; set; }
}

public sealed class AdminMetric
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Change { get; set; } = string.Empty;
    public string ChangeTone { get; set; } = "success";
    public string Icon { get; set; } = "bi-activity";
    public string IconTone { get; set; } = "sage";
    public string Detail { get; set; } = string.Empty;
}

public sealed class RevenuePoint
{
    public string Month { get; set; } = string.Empty;
    public string Amount { get; set; } = string.Empty;
    public int Orders { get; set; }
    public int Height { get; set; }
    public bool IsCurrent { get; set; }
}

public sealed class ActivityItem
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TimeLabel { get; set; } = string.Empty;
    public string Icon { get; set; } = "bi-activity";
    public string Tone { get; set; } = "sage";
}

public sealed class FarmerSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FarmName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string AvatarTone { get; set; } = "sage";
    public string Status { get; set; } = string.Empty;
    public string StatusTone { get; set; } = "success";
    public string SubmittedLabel { get; set; } = string.Empty;
    public int ProductCount { get; set; }
    public decimal Rating { get; set; }
    public decimal MonthlyRevenue { get; set; }
    public bool IsActive { get; set; }
    public int ReviewCount { get; set; }
    public int UnansweredReviewCount { get; set; }
    public string LatestReviewLabel { get; set; } = string.Empty;
    public string LatestReviewText { get; set; } = string.Empty;
    public int LatestReviewRating { get; set; }
}

public sealed class FarmerListViewModel
{
    public string SearchTerm { get; set; } = string.Empty;
    public string StatusFilter { get; set; } = "All farmers";
    public int TotalFarmers { get; set; }
    public int ActiveFarmers { get; set; }
    public int PendingFarmers { get; set; }
    public List<FarmerSummary> Farmers { get; set; } = new();
}

public sealed class FarmerApprovalViewModel
{
    public int PendingCount { get; set; }
    public int ApprovedThisMonth { get; set; }
    public int AverageReviewTime { get; set; }
    public List<FarmerSummary> PendingFarmers { get; set; } = new();
}

public sealed class FarmerDecisionViewModel : IValidatableObject
{
    [Range(1, int.MaxValue, ErrorMessage = "Select a valid farmer.")]
    public int Id { get; set; }

    [StringLength(300, ErrorMessage = "Notes can be up to 300 characters.")]
    public string? Reason { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Reason) && Reason.Trim().Length < 3)
        {
            yield return new ValidationResult("Enter at least 3 characters.", new[] { nameof(Reason) });
        }
    }
}

public sealed class CustomerSummary
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string AvatarTone { get; set; } = "mint";
    public string Status { get; set; } = string.Empty;
    public string StatusTone { get; set; } = "success";
    public string JoinedLabel { get; set; } = string.Empty;
    public string LastOrderLabel { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public decimal TotalSpent { get; set; }
    public bool IsActive { get; set; }
}

public sealed class CustomerListViewModel
{
    public string SearchTerm { get; set; } = string.Empty;
    public string StatusFilter { get; set; } = "All customers";
    public int TotalCustomers { get; set; }
    public int ActiveCustomers { get; set; }
    public int NewCustomers { get; set; }
    public List<CustomerSummary> Customers { get; set; } = new();
}

public sealed class CustomerStatusUpdateViewModel
{
    [Required(ErrorMessage = "Select a valid customer.")]
    [StringLength(450, ErrorMessage = "The customer identifier is invalid.")]
    public string Id { get; set; } = string.Empty;

    public bool IsActive { get; set; }
}

public sealed class MarketSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Hours { get; set; } = string.Empty;
    public int ProductCount { get; set; }
    public int OrderCount { get; set; }
    public decimal Revenue { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusTone { get; set; } = "success";
    public bool IsFeatured { get; set; }
    public string Initials { get; set; } = string.Empty;
    public string AvatarTone { get; set; } = "sage";
}

public sealed class MarketListViewModel
{
    public string SearchTerm { get; set; } = string.Empty;
    public string StatusFilter { get; set; } = "All markets";
    public int TotalMarkets { get; set; }
    public int ActiveMarkets { get; set; }
    public int FeaturedMarkets { get; set; }
    public List<MarketSummary> Markets { get; set; } = new();
}

public sealed class MarketFormViewModel : IValidatableObject
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Enter a market name.")]
    [StringLength(150, ErrorMessage = "Market names can be up to 150 characters.")]
    [BusinessName]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a short description.")]
    [StringLength(2000, ErrorMessage = "Descriptions can be up to 2,000 characters.")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a street address.")]
    [StringLength(250, ErrorMessage = "Addresses can be up to 250 characters.")]
    [StreetAddress]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a city.")]
    [StringLength(100, ErrorMessage = "Cities can be up to 100 characters.")]
    [BusinessName]
    public string City { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the operating days.")]
    [StringLength(100, ErrorMessage = "Operating days can be up to 100 characters.")]
    public string OperatingDays { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter the opening time.")]
    [RegularExpression("^([01][0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Enter a valid opening time.")]
    public string OpenTime { get; set; } = "08:00";

    [Required(ErrorMessage = "Enter the closing time.")]
    [RegularExpression("^([01][0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Enter a valid closing time.")]
    public string CloseTime { get; set; } = "18:00";

    [Range(typeof(decimal), "-90", "90", ErrorMessage = "Latitude must be between -90 and 90.")]
    public decimal Latitude { get; set; }

    [Range(typeof(decimal), "-180", "180", ErrorMessage = "Longitude must be between -180 and 180.")]
    public decimal Longitude { get; set; }

    [Url(ErrorMessage = "Enter a valid image URL.")]
    [StringLength(300, ErrorMessage = "Image URLs can be up to 300 characters.")]
    public string? ImageUrl { get; set; }

    [ImageUpload(8, "Choose a JPG, PNG, GIF, BMP or WEBP picture up to 8 MB.")]
    [Display(Name = "Market picture")]
    public IFormFile? Photo { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsFeatured { get; set; }

    public bool HasCoordinates => Latitude != 0m && Longitude != 0m;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OpenTime == CloseTime)
        {
            yield return new ValidationResult("Opening and closing times must be different.", new[] { nameof(CloseTime) });
        }

        if (Photo is not null && Photo.Length > 8L * 1024 * 1024)
        {
            yield return new ValidationResult("That picture is larger than 8 MB. Please choose a smaller one.", new[] { nameof(Photo) });
        }
    }
}

public sealed class CategorySummary
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = "bi-basket";
    public string IconTone { get; set; } = "sage";
    public int ProductCount { get; set; }
    public int SortOrder { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusTone { get; set; } = "success";
}

public sealed class CategoryListViewModel
{
    public int TotalCategories { get; set; }
    public int ActiveCategories { get; set; }
    public int TotalProducts { get; set; }
    public List<CategorySummary> Categories { get; set; } = new();
}

public sealed class CategoryFormViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Enter a category name.")]
    [StringLength(100, ErrorMessage = "Category names can be up to 100 characters.")]
    [BusinessName]
    public string Name { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Descriptions can be up to 500 characters.")]
    public string Description { get; set; } = string.Empty;

    [Url(ErrorMessage = "Enter a valid image URL.")]
    [StringLength(300, ErrorMessage = "Image URLs can be up to 300 characters.")]
    public string? ImageUrl { get; set; }

    [Range(0, 999, ErrorMessage = "Sort order must be between 0 and 999.")]
    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed class FarmerReviewRollup
{
    public int FarmerId { get; set; }
    public int Rating { get; set; }
    public bool HasReply { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
}

public sealed class FarmerReviewStats
{
    public int Count { get; set; }
    public int Unanswered { get; set; }
    public DateTime LatestDate { get; set; }
    public string LatestTitle { get; set; } = string.Empty;
    public string LatestComment { get; set; } = string.Empty;
    public int LatestRating { get; set; }
}

public sealed class OrderRecord
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string PlacedLabel { get; set; } = string.Empty;
    public string PickupLabel { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public OrderStatus Status { get; set; }
    public PaymentStatus Payment { get; set; }
    public string Farmers { get; set; } = string.Empty;
    public string Items { get; set; } = string.Empty;
}

public sealed class DailyOrderCount
{
    public DateTime Day { get; set; }
    public int Count { get; set; }
}

public sealed class OrderSummary
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerInitials { get; set; } = string.Empty;
    public string CustomerTone { get; set; } = "mint";
    public string PlacedLabel { get; set; } = string.Empty;
    public string DeliveryLabel { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public decimal Total { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string PaymentTone { get; set; } = "success";
    public string FulfillmentStatus { get; set; } = string.Empty;
    public string FulfillmentTone { get; set; } = "warning";
    public OrderStatus Status { get; set; }
    public PaymentStatus Payment { get; set; }
    public string Farmers { get; set; } = string.Empty;
    public string Items { get; set; } = string.Empty;
}

public sealed class OrderListViewModel
{
    public string SearchTerm { get; set; } = string.Empty;
    public string StatusFilter { get; set; } = "All orders";
    public int TotalOrders { get; set; }
    public int AwaitingFulfillment { get; set; }
    public decimal RevenueThisMonth { get; set; }
    public int OrdersToday { get; set; }
    public List<DailyOrderCount> DailyCounts { get; set; } = new();
    public List<OrderSummary> Orders { get; set; } = new();
}

public sealed class OrderStatusUpdateViewModel
{
    [StringLength(300, ErrorMessage = "Notes can be up to 300 characters.")]
    public string? Note { get; set; }

}

public sealed class OrderLineItem
{
    public string ProductName { get; set; } = string.Empty;
    public string Variant { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class OrderStatusEvent
{
    public string Title { get; set; } = string.Empty;
    public string TimeLabel { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsComplete { get; set; }
    public bool IsCurrent { get; set; }
}

public sealed class OrderDetailsViewModel
{
    public OrderSummary Order { get; set; } = new();
    public string ShippingAddress { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string MarketName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public List<OrderLineItem> Items { get; set; } = new();
    public List<OrderStatusEvent> Timeline { get; set; } = new();
}

public sealed class ReportMetric
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Change { get; set; } = string.Empty;
    public string ChangeTone { get; set; } = "success";
    public string Icon { get; set; } = "bi-activity";
    public string IconTone { get; set; } = "sage";
}

public sealed class ReportBar
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public int Height { get; set; }
    public string Tone { get; set; } = "sage";
}

public sealed class ReportTableRow
{
    public string Name { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Initials { get; set; } = string.Empty;
    public string Orders { get; set; } = string.Empty;
    public decimal RevenueValue { get; set; }
    public string Revenue { get; set; } = string.Empty;
    public string Change { get; set; } = string.Empty;
    public string ChangeTone { get; set; } = "success";
    public string Tone { get; set; } = "success";
}

public sealed class PopularProduct
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int UnitsSold { get; set; }
    public decimal Revenue { get; set; }
    public string ImageTone { get; set; } = "sage";
    public string Initials { get; set; } = string.Empty;
}

public sealed class AuditSummary
{
    public int Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string TimeLabel { get; set; } = string.Empty;
}

public sealed class ReportViewModel
{
    public string SelectedPeriod { get; set; } = "Last 30 days";
    public string DateRangeLabel { get; set; } = string.Empty;
    public List<ReportMetric> Metrics { get; set; } = new();
    public List<ReportBar> RevenueByMonth { get; set; } = new();
    public List<ReportTableRow> TopFarmers { get; set; } = new();
    public List<ReportTableRow> RevenueByMarket { get; set; } = new();
    public List<PopularProduct> TopProducts { get; set; } = new();
    public List<AuditSummary> RecentAdminActions { get; set; } = new();
}

public sealed class NotificationListViewModel
{
    public string Filter { get; set; } = "All notifications";
    public int TotalCount { get; set; }
    public int UnreadCount { get; set; }
    public int SystemCount { get; set; }
    public List<AdminNotificationItem> Notifications { get; set; } = new();
    public AnnouncementFormViewModel Announcement { get; set; } = new();
}

public sealed class AdminNotificationItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string TimeLabel { get; set; } = string.Empty;
    public string Icon { get; set; } = "bi-bell";
    public string Tone { get; set; } = "sage";
    public bool IsRead { get; set; }
    public string? ActionUrl { get; set; }
    public NotificationType Type { get; set; }
    public DateTime Timestamp { get; set; }
}

public sealed class AnnouncementFormViewModel
{
    [Required(ErrorMessage = "Enter an announcement title.")]
    [StringLength(150, ErrorMessage = "Titles can be up to 150 characters.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter an announcement message.")]
    [StringLength(500, ErrorMessage = "Messages can be up to 500 characters.")]
    public string Message { get; set; } = string.Empty;

    [StringLength(300, ErrorMessage = "Action URLs can be up to 300 characters.")]
    public string? ActionUrl { get; set; }

    [Required(ErrorMessage = "Select an audience.")]
    public string Audience { get; set; } = "All active users";
}

public sealed class ReviewModerationItem
{
    public int Id { get; set; }
    public string Target { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public int Rating { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public bool VerifiedPurchase { get; set; }
    public bool HasFarmerReply { get; set; }
    public string TimeLabel { get; set; } = string.Empty;
}

public sealed class ProductModerationItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FarmerName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool IsAvailable { get; set; }
    public bool IsOrganic { get; set; }
}

public sealed class ModerationListViewModel
{
    public string SearchTerm { get; set; } = string.Empty;
    public int ReviewCount { get; set; }
    public int HiddenProductCount { get; set; }
    public int VerifiedReviewCount { get; set; }
    public List<ReviewModerationItem> Reviews { get; set; } = new();
    public List<ProductModerationItem> Products { get; set; } = new();
}

public sealed class ProductModerationUpdateViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Select a valid product.")]
    public int Id { get; set; }

    public bool IsAvailable { get; set; }
}
