using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Areas.Farmer.Controllers;

[Area("Farmer")]
[Authorize(Policy = "ActiveFarmerAccess")]
public sealed class DashboardController : FarmerControllerBase
{
    public DashboardController(ApplicationDbContext db) : base(db)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);
        var now = DateTime.UtcNow;
        var localToday = DateTime.Now.Date;
        var monthStart = new DateTime(localToday.Year, localToday.Month, 1);
        var nextMonth = monthStart.AddMonths(1);
        var previousMonthStart = monthStart.AddMonths(-1);
        var previousMonthEnd = monthStart;

        var orders = await OwnedOrders(profile)
            .AsNoTracking()
            .Include(order => order.User)
            .Include(order => order.Market)
            .Include(order => order.OrderItems)
                .ThenInclude(item => item.Product)
            .OrderByDescending(order => order.OrderDate)
            .ToListAsync(cancellationToken);
        var products = await Db.Products
            .AsNoTracking()
            .Include(product => product.Inventory)
            .Where(product => product.FarmerProfileId == profile.Id)
            .OrderBy(product => product.Name)
            .ToListAsync(cancellationToken);
        var reviews = await Db.Reviews
            .AsNoTracking()
            .Include(review => review.User)
            .Include(review => review.Product)
            .Where(review => review.FarmerProfileId == profile.Id
                || (review.ProductId.HasValue && review.Product != null && review.Product.FarmerProfileId == profile.Id))
            .OrderByDescending(review => review.CreatedAt)
            .ToListAsync(cancellationToken);
        var notifications = await Db.Notifications
            .AsNoTracking()
            .Where(notification => notification.UserId == profile.UserId)
            .OrderByDescending(notification => notification.CreatedAt)
            .Take(12)
            .ToListAsync(cancellationToken);
        var unreadNotifications = await Db.Notifications.CountAsync(notification => notification.UserId == profile.UserId && !notification.IsRead, cancellationToken);

        var ownedSubtotal = new Func<Order, decimal>(order => order.OrderItems
            .Where(item => item.Product is not null && item.Product.FarmerProfileId == profile.Id)
            .Sum(item => item.SubTotal));
        var monthlyOrders = orders.Where(order => order.OrderDate >= monthStart && order.OrderDate < nextMonth && order.Status is not OrderStatus.Declined and not OrderStatus.Cancelled);
        var previousOrders = orders.Where(order => order.OrderDate >= previousMonthStart && order.OrderDate < previousMonthEnd && order.Status is not OrderStatus.Declined and not OrderStatus.Cancelled);
        var monthlyRevenue = monthlyOrders.Sum(ownedSubtotal);
        var previousRevenue = previousOrders.Sum(ownedSubtotal);
        var revenueChange = CalculateRevenueChange(monthlyRevenue, previousRevenue);
        var pendingOrders = orders.Count(order => order.Status == OrderStatus.Pending);
        var activeProducts = products.Count(product => product.IsAvailable && (product.Inventory?.QuantityAvailable ?? 0) > 0);
        var lowStockProducts = products.Count(product => (product.Inventory?.QuantityAvailable ?? 0) <= (product.Inventory?.ReorderThreshold ?? 5));
        var newReviews = reviews.Count(review => review.CreatedAt >= now.AddDays(-30) && string.IsNullOrWhiteSpace(review.FarmerReply));
        var recentOrders = orders.Take(6).Select(order => MapDashboardOrder(order, profile)).ToList();
        var revenuePoints = BuildRevenuePoints(orders, ownedSubtotal, localToday);
        var activities = BuildActivities(orders, reviews, products, notifications);

        var firstName = profile.User?.FirstName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(firstName))
        {
            firstName = profile.FarmName;
        }

        var model = new FarmerDashboardViewModel
        {
            FarmerName = firstName,
            FarmName = profile.FarmName,
            Greeting = GetGreeting(),
            DateLabel = DateTime.Now.ToString("dddd, MMMM d, yyyy"),
            FarmImageUrl = string.IsNullOrWhiteSpace(profile.User?.ProfilePictureUrl) ? FarmFallbackImageUrl : profile.User.ProfilePictureUrl,
            WelcomeTitle = $"Your farm is growing, {firstName}. Keep it up.",
            WelcomeMessage = lowStockProducts > 0
                ? $"{lowStockProducts} product{(lowStockProducts == 1 ? string.Empty : "s")} need a quick restock before the next market."
                : "Your catalog is stocked and ready for the next market pickup.",
            RevenuePeriodLabel = $"{monthStart:MMM d} - {DateTime.Now:MMM d, yyyy}",
            RevenueTotalLabel = monthlyRevenue.ToString("C"),
            MonthlyRevenue = monthlyRevenue,
            RevenueChange = revenueChange,
            PendingOrders = pendingOrders,
            ActiveProducts = activeProducts,
            LowStockProducts = lowStockProducts,
            NewReviews = newReviews,
            UnreadNotifications = unreadNotifications,
            Metrics = new List<FarmerMetricViewModel>
            {
                new()
                {
                    Label = "This month",
                    Value = monthlyRevenue.ToString("C"),
                    Detail = "Revenue collected",
                    Icon = "bi-wallet2",
                    Tone = "green",
                    Trend = revenueChange
                },
                new()
                {
                    Label = "Orders to fulfill",
                    Value = pendingOrders.ToString(),
                    Detail = $"{orders.Count(order => order.Status is OrderStatus.Accepted or OrderStatus.Preparing)} in preparation",
                    Icon = "bi-bag-check",
                    Tone = "amber",
                    Trend = "View orders"
                },
                new()
                {
                    Label = "Live listings",
                    Value = activeProducts.ToString(),
                    Detail = $"{lowStockProducts} low in stock",
                    Icon = "bi-box-seam",
                    Tone = "blue",
                    Trend = "Manage"
                }
            },
            RevenuePoints = revenuePoints,
            RecentOrders = recentOrders,
            Activities = activities
        };

        return View(model);
    }

    private static List<FarmerRevenuePointViewModel> BuildRevenuePoints(List<Order> orders, Func<Order, decimal> ownedSubtotal, DateTime today)
    {
        var points = new List<FarmerRevenuePointViewModel>();
        var start = today.AddDays(-6);
        for (var offset = 0; offset < 7; offset++)
        {
            var date = start.AddDays(offset);
            var amount = orders
                .Where(order => order.OrderDate.ToLocalTime().Date == date && order.Status is not OrderStatus.Declined and not OrderStatus.Cancelled)
                .Sum(ownedSubtotal);
            points.Add(new FarmerRevenuePointViewModel
            {
                Label = date.ToString("ddd"),
                Amount = amount,
                IsCurrent = date == today
            });
        }

        var maximum = points.Max(point => point.Amount);
        foreach (var point in points)
        {
            point.BarHeight = maximum <= 0 ? 4 : (int)Math.Clamp(Math.Round(point.Amount / maximum * 100m), 4, 100);
        }

        return points;
    }

    private static List<FarmerActivityViewModel> BuildActivities(
        List<Order> orders,
        List<Review> reviews,
        List<Product> products,
        List<Notification> notifications)
    {
        var activities = new List<FarmerActivityViewModel>();
        activities.AddRange(notifications.Select(notification => new FarmerActivityViewModel
        {
            Icon = GetNotificationIcon(notification.Type),
            Title = notification.Title,
            Description = notification.Message,
            TimeLabel = FormatRelativeTime(notification.CreatedAt),
            Tone = GetNotificationTone(notification.Type),
            ActionUrl = notification.ActionUrl,
            ActivityAt = notification.CreatedAt
        }));
        activities.AddRange(reviews.Where(review => review.CreatedAt >= DateTime.UtcNow.AddDays(-30)).Take(4).Select(review => new FarmerActivityViewModel
        {
            Icon = "bi-star-fill",
            Title = $"New {review.Rating}-star review",
            Description = $"{GetReviewCustomerName(review.User)} shared feedback about {review.Product?.Name ?? "your farm"}.",
            TimeLabel = FormatRelativeTime(review.CreatedAt),
            Tone = "yellow",
            ActionUrl = "/Farmer/Review",
            ActivityAt = review.CreatedAt
        }));
        activities.AddRange(products.Where(product => (product.Inventory?.QuantityAvailable ?? 0) <= (product.Inventory?.ReorderThreshold ?? 5)).Take(4).Select(product => new FarmerActivityViewModel
        {
            Icon = "bi-box-arrow-in-down",
            Title = "Inventory is running low",
            Description = $"{product.Name} has {product.Inventory?.QuantityAvailable ?? 0} units remaining.",
            TimeLabel = FormatRelativeTime(product.UpdatedAt),
            Tone = "amber",
            ActionUrl = $"/Farmer/Product/Details/{product.Id}",
            ActivityAt = product.UpdatedAt
        }));
        activities.AddRange(orders.Take(4).Select(order => new FarmerActivityViewModel
        {
            Icon = "bi-bag-check-fill",
            Title = $"Order {FormatOrderNumber(order)} received",
            Description = $"{GetReviewCustomerName(order.User)} placed an order for {FormatPickupWindow(order)}.",
            TimeLabel = FormatRelativeTime(order.OrderDate),
            Tone = "blue",
            ActionUrl = $"/Farmer/Order/Details/{order.Id}",
            ActivityAt = order.OrderDate
        }));
        return activities.OrderByDescending(activity => activity.ActivityAt).Take(6).ToList();
    }

    private static FarmerDashboardOrderViewModel MapDashboardOrder(Order order, FarmerProfile profile)
    {
        var ownedItems = order.OrderItems
            .Where(item => item.Product is not null && item.Product.FarmerProfileId == profile.Id)
            .ToList();
        var customerName = GetReviewCustomerName(order.User);
        var ownSubtotal = ownedItems.Sum(item => item.SubTotal);
        return new FarmerDashboardOrderViewModel
        {
            Id = order.Id,
            OrderNumber = FormatOrderNumber(order),
            CustomerName = customerName,
            CustomerInitials = GetInitials(customerName),
            ItemSummary = string.Join(", ", ownedItems.Select(item => item.ProductName).Take(2)) + (ownedItems.Count > 2 ? $" +{ownedItems.Count - 2} more" : string.Empty),
            PlacedLabel = FormatRelativeTime(order.OrderDate),
            Total = ownSubtotal,
            Status = GetStatusLabel(order.Status),
            StatusTone = GetStatusTone(order.Status),
            StatusIcon = GetStatusIcon(order.Status)
        };
    }

    private static string GetReviewCustomerName(ApplicationUser? user)
    {
        var name = $"{user?.FirstName ?? string.Empty} {user?.LastName ?? string.Empty}".Trim();
        return string.IsNullOrWhiteSpace(name) ? "A MarketLink neighbor" : name;
    }

    private static string GetNotificationIcon(NotificationType type)
    {
        return type switch
        {
            NotificationType.Order => "bi-bag-check-fill",
            NotificationType.Review => "bi-star-fill",
            NotificationType.Account => "bi-person-fill",
            NotificationType.Announcement => "bi-megaphone-fill",
            _ => "bi-bell-fill"
        };
    }

    private static string GetNotificationTone(NotificationType type)
    {
        return type switch
        {
            NotificationType.Review => "yellow",
            NotificationType.Order => "blue",
            NotificationType.Account => "green",
            _ => "muted"
        };
    }

    private static string FormatOrderNumber(Order order)
    {
        var value = string.IsNullOrWhiteSpace(order.OrderNumber) ? order.Id.ToString() : order.OrderNumber.Trim();
        return value.StartsWith('#') ? value : $"#{value}";
    }

    private static string FormatPickupWindow(Order order)
    {
        var date = order.PickupDate.ToLocalTime().ToString("MMM d");
        return string.IsNullOrWhiteSpace(order.PickupTimeSlot) ? date : $"{date} - {order.PickupTimeSlot}";
    }

    private static string CalculateRevenueChange(decimal current, decimal previous)
    {
        if (previous <= 0)
        {
            return current > 0 ? "New sales" : "No change";
        }

        var change = (current - previous) / previous * 100m;
        return $"{change:+0.0;-0.0;0}%";
    }

    private static string GetGreeting()
    {
        return DateTime.Now.Hour switch
        {
            < 12 => "Good morning",
            < 17 => "Good afternoon",
            _ => "Good evening"
        };
    }
}
