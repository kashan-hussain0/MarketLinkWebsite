using System.Globalization;
using MarketLinkWebsite.Areas.Admin.Models;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = "AdminAccess")]
public sealed class DashboardController : AdminControllerBase
{
    public DashboardController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
        : base(db, userManager, roleManager)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var currentMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonth = currentMonth.AddMonths(1);
        var previousMonth = currentMonth.AddMonths(-1);
        var sixMonthStart = currentMonth.AddMonths(-5);
        var farmerRoleId = await Db.Roles
            .Where(role => role.Name == "Farmer")
            .OrderBy(role => role.Name)
            .Select(role => role.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var currentRevenue = await Db.Orders
            .Where(order => order.OrderDate >= currentMonth && order.OrderDate < nextMonth && order.PaymentStatus == PaymentStatus.Paid && order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Declined)
            .SumAsync(order => (decimal?)order.TotalAmount, cancellationToken) ?? 0m;
        var previousRevenue = await Db.Orders
            .Where(order => order.OrderDate >= previousMonth && order.OrderDate < currentMonth && order.PaymentStatus == PaymentStatus.Paid && order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Declined)
            .SumAsync(order => (decimal?)order.TotalAmount, cancellationToken) ?? 0m;
        var currentOrderCount = await Db.Orders.CountAsync(order => order.OrderDate >= currentMonth && order.OrderDate < nextMonth, cancellationToken);
        var previousOrderCount = await Db.Orders.CountAsync(order => order.OrderDate >= previousMonth && order.OrderDate < currentMonth, cancellationToken);
        var todayOrderCount = await Db.Orders.CountAsync(order => order.OrderDate.Date == now.Date, cancellationToken);
        var effectiveFarmerRoleId = farmerRoleId ?? string.Empty;
        var activeFarmerCount = await Db.FarmerProfiles.CountAsync(
            profile => profile.Status == FarmerStatus.Active
                && profile.User.IsActive
                && Db.UserRoles.Any(userRole => userRole.UserId == profile.UserId && userRole.RoleId == effectiveFarmerRoleId),
            cancellationToken);
        var totalFarmerCount = await Db.FarmerProfiles.CountAsync(cancellationToken);
        var pendingFarmerCount = await Db.FarmerProfiles.CountAsync(profile => profile.Status == FarmerStatus.PendingApproval, cancellationToken);
        var totalCustomerCount = await Db.Users.CountAsync(user => !Db.FarmerProfiles.Any(profile => profile.UserId == user.Id), cancellationToken);
        var totalMarketCount = await Db.Markets.CountAsync(cancellationToken);
        var totalOrderCount = await Db.Orders.CountAsync(cancellationToken);
        var reviewSummary = await Db.Reviews
            .GroupBy(_ => 1)
            .OrderBy(group => group.Key)
            .Select(group => new
            {
                Average = group.Average(review => (decimal?)review.Rating),
                Count = group.Count()
            })
            .FirstOrDefaultAsync(cancellationToken);
        var averageRating = reviewSummary?.Average ?? 0m;
        var reviewCount = reviewSummary?.Count ?? 0;

        var monthlyRevenue = await Db.Orders
            .Where(order => order.OrderDate >= sixMonthStart && order.OrderDate < nextMonth && order.PaymentStatus == PaymentStatus.Paid && order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Declined)
            .GroupBy(order => new { order.OrderDate.Year, order.OrderDate.Month })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                Revenue = group.Sum(order => order.TotalAmount),
                Orders = group.Count()
            })
            .ToListAsync(cancellationToken);
        var revenueLookup = monthlyRevenue.ToDictionary(item => new DateTime(item.Year, item.Month, 1, 0, 0, 0, DateTimeKind.Utc));
        var maximumRevenue = monthlyRevenue.Count == 0 ? 0m : monthlyRevenue.Max(item => item.Revenue);
        var revenueTrend = Enumerable.Range(0, 6)
            .Select(offset =>
            {
                var month = currentMonth.AddMonths(offset - 5);
                var hasValue = revenueLookup.TryGetValue(month, out var value);
                var revenue = hasValue ? value!.Revenue : 0m;
                var orders = hasValue ? value!.Orders : 0;
                return new RevenuePoint
                {
                    Month = month.ToString("MMM", CultureInfo.InvariantCulture),
                    Amount = CompactCurrency(revenue),
                    Orders = orders,
                    Height = maximumRevenue <= 0m ? 0 : (int)Math.Round(revenue / maximumRevenue * 100m, MidpointRounding.AwayFromZero),
                    IsCurrent = offset == 5
                };
            })
            .ToList();

        var auditLogs = await Db.AuditLogs
            .AsNoTracking()
            .Include(log => log.User)
            .OrderByDescending(log => log.Timestamp)
            .Take(4)
            .ToListAsync(cancellationToken);
        var recentActivity = auditLogs.Select(ActivityForAudit).ToList();
        var pendingFarmerRows = await Db.FarmerProfiles
            .AsNoTracking()
            .Where(profile => profile.Status == FarmerStatus.PendingApproval)
            .OrderBy(profile => profile.CreatedAt)
            .Take(3)
            .Select(profile => new
            {
                profile.Id,
                profile.FarmName,
                profile.City,
                profile.Address,
                profile.Status,
                profile.CreatedAt,
                profile.Rating,
                FirstName = profile.User.FirstName,
                LastName = profile.User.LastName,
                Email = profile.User.Email ?? string.Empty,
                IsActive = profile.User.IsActive,
                ProductCount = profile.Products.Count
            })
            .ToListAsync(cancellationToken);
        var pendingFarmers = pendingFarmerRows.Select(profile => new FarmerSummary
        {
            Id = profile.Id,
            FarmName = profile.FarmName,
            Location = string.Join(", ", new[] { profile.City, profile.Address }.Where(value => !string.IsNullOrWhiteSpace(value))),
            Status = FarmerStatusLabel(profile.Status),
            StatusTone = StatusTone(FarmerStatusLabel(profile.Status)),
            SubmittedLabel = RelativeTime(profile.CreatedAt),
            Name = $"{profile.FirstName} {profile.LastName}".Trim(),
            Email = profile.Email,
            Initials = Initials(profile.FirstName, profile.LastName),
            AvatarTone = AvatarTone(string.IsNullOrWhiteSpace(profile.Email) ? profile.Id.ToString(CultureInfo.InvariantCulture) : profile.Email),
            ProductCount = profile.ProductCount,
            Rating = profile.Rating
        }).ToList();

        var adminId = CurrentUserId;
        var notifications = new List<AdminNotificationItem>();
        if (!string.IsNullOrWhiteSpace(adminId))
        {
            var notificationRows = await Db.Notifications
                .AsNoTracking()
                .Where(notification => notification.UserId == adminId)
                .OrderByDescending(notification => notification.CreatedAt)
                .Take(3)
                .Select(notification => new
                {
                    notification.Id,
                    notification.Title,
                    notification.Message,
                    notification.CreatedAt,
                    notification.IsRead,
                    notification.ActionUrl,
                    notification.Type
                })
                .ToListAsync(cancellationToken);
            notifications = notificationRows.Select(notification => new AdminNotificationItem
            {
                Id = notification.Id,
                Title = notification.Title,
                Message = notification.Message,
                TimeLabel = notification.CreatedAt.ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture),
                Icon = NotificationIcon(notification.Type),
                Tone = NotificationTone(notification.Type),
                IsRead = notification.IsRead,
                ActionUrl = notification.ActionUrl,
                Type = notification.Type,
                Timestamp = notification.CreatedAt
            }).ToList();
        }

        foreach (var notification in notifications)
        {
            if (!string.IsNullOrWhiteSpace(notification.ActionUrl) && !Url.IsLocalUrl(notification.ActionUrl))
            {
                notification.ActionUrl = null;
            }
        }

        var unreadCount = string.IsNullOrWhiteSpace(adminId)
            ? 0
            : await Db.Notifications.CountAsync(notification => notification.UserId == adminId && !notification.IsRead, cancellationToken);
        var admin = string.IsNullOrWhiteSpace(CurrentUserId) ? null : await UserManager.FindByIdAsync(CurrentUserId);
        var adminName = admin is null ? "Administrator" : string.IsNullOrWhiteSpace(admin.FirstName) ? admin.Email ?? "Administrator" : admin.FirstName;
        var greeting = now.Hour < 12 ? "Good morning" : now.Hour < 18 ? "Good afternoon" : "Good evening";
        ViewData["Greeting"] = greeting;

        var model = new AdminDashboardViewModel
        {
            AdminName = adminName,
            PeriodLabel = currentMonth.ToString("MMMM d", CultureInfo.InvariantCulture) + " - " + now.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture),
            CurrentRevenue = currentRevenue,
            RevenueAxisMaximum = Math.Ceiling(maximumRevenue / 10000m) * 10000m,
            Metrics = new List<AdminMetric>
            {
                new() { Label = "Total farmers", Value = totalFarmerCount.ToString("N0", CultureInfo.GetCultureInfo("en-US")), Change = "Current", ChangeTone = "success", Icon = "bi-people", IconTone = "violet", Detail = $"{activeFarmerCount} active · {pendingFarmerCount} awaiting approval" },
                new() { Label = "Total customers", Value = totalCustomerCount.ToString("N0", CultureInfo.GetCultureInfo("en-US")), Change = "Current", ChangeTone = "success", Icon = "bi-person-circle", IconTone = "blue", Detail = "Registered customers" },
                new() { Label = "Total markets", Value = totalMarketCount.ToString("N0", CultureInfo.GetCultureInfo("en-US")), Change = "Current", ChangeTone = "success", Icon = "bi-shop", IconTone = "sage", Detail = "Active pickup markets" },
                new() { Label = "Total orders", Value = totalOrderCount.ToString("N0", CultureInfo.GetCultureInfo("en-US")), Change = "Current", ChangeTone = "success", Icon = "bi-bag-check", IconTone = "amber", Detail = $"{currentOrderCount} this month" },
                new() { Label = "Revenue", Value = currentRevenue.ToString("C0", CultureInfo.GetCultureInfo("en-US")), Change = PercentageChange(currentRevenue, previousRevenue), ChangeTone = ChangeTone(currentRevenue, previousRevenue), Icon = "bi-wallet2", IconTone = "sage", Detail = $"vs. {previousRevenue:C0} last month" },
                new() { Label = "Customer satisfaction", Value = (reviewCount == 0 ? "No reviews" : $"{averageRating:0.0} / 5"), Change = (reviewCount == 0 ? "No data" : $"{averageRating:0.0} average"), ChangeTone = "success", Icon = "bi-star", IconTone = "amber", Detail = $"Based on {reviewCount} reviews" }
            },
            RevenueTrend = revenueTrend,
            RecentActivity = recentActivity,
            PendingFarmers = pendingFarmers,
            Notifications = notifications,
            UnreadNotificationCount = unreadCount
        };
        return View(model);
    }

    private static ActivityItem ActivityForAudit(AuditLog log)
    {
        var action = log.Action.ToLowerInvariant();
        var (icon, tone) = action switch
        {
            _ when action.Contains("farmer") || action.Contains("approve") => ("bi-person-check", "sage"),
            _ when action.Contains("market") => ("bi-shop", "amber"),
            _ when action.Contains("order") => ("bi-bag-check", "blue"),
            _ when action.Contains("customer") => ("bi-person", "violet"),
            _ when action.Contains("announcement") => ("bi-megaphone", "coral"),
            _ when action.Contains("moder") || action.Contains("product") => ("bi-shield-check", "amber"),
            _ => ("bi-activity", "sage")
        };
        var actor = log.User is null ? "An administrator" : $"{log.User.FirstName} {log.User.LastName}".Trim();
        return new ActivityItem
        {
            Title = log.Action,
            Description = $"{actor}: {log.Details}",
            TimeLabel = RelativeTime(log.Timestamp),
            Icon = icon,
            Tone = tone
        };
    }

    private static string NotificationIcon(NotificationType type) => type switch
    {
        NotificationType.Order => "bi-bag-check",
        NotificationType.Account => "bi-person-check",
        NotificationType.Review => "bi-star",
        NotificationType.Announcement => "bi-megaphone",
        _ => "bi-bell"
    };

    private static string NotificationTone(NotificationType type) => type switch
    {
        NotificationType.Order => "blue",
        NotificationType.Account => "violet",
        NotificationType.Review => "amber",
        NotificationType.Announcement => "coral",
        _ => "sage"
    };

    private static string PercentageChange(decimal current, decimal previous)
    {
        if (previous == 0m)
        {
            return current == 0m ? "0.0%" : "New";
        }

        var change = (current - previous) / previous * 100m;
        return $"{(change >= 0m ? "+" : string.Empty)}{change:0.0}%";
    }

    private static string ChangeTone(decimal current, decimal previous) => current >= previous ? "success" : "danger";
}
