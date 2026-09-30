using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Areas.Farmer.Controllers;

[Area("Farmer")]
[Authorize(Policy = "ActiveFarmerAccess")]
public sealed class NotificationController : FarmerControllerBase
{
    public NotificationController(ApplicationDbContext db) : base(db)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? filter, CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);
        var notifications = await Db.Notifications
            .AsNoTracking()
            .Where(notification => notification.UserId == profile.UserId)
            .OrderByDescending(notification => notification.CreatedAt)
            .ToListAsync(cancellationToken);
        var normalizedFilter = filter?.Equals("Unread", StringComparison.OrdinalIgnoreCase) == true ? "Unread" : "All";
        var visibleNotifications = normalizedFilter == "Unread"
            ? notifications.Where(notification => !notification.IsRead).ToList()
            : notifications;
        var model = new NotificationListViewModel
        {
            UnreadCount = notifications.Count(notification => !notification.IsRead),
            TotalCount = notifications.Count,
            Notice = Request.Query["notice"].FirstOrDefault(),
            Notifications = visibleNotifications.Select(MapNotification).ToList()
        };
        ViewData["NotificationFilter"] = normalizedFilter;
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);
        var notifications = await Db.Notifications
            .Where(notification => notification.UserId == profile.UserId && !notification.IsRead)
            .ToListAsync(cancellationToken);
        var readAt = DateTime.UtcNow;
        foreach (var notification in notifications)
        {
            notification.IsRead = true;
            notification.ReadAt = readAt;
        }

        await Db.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index), new { area = "Farmer", notice = "All notifications marked as read." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id, CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);
        var notification = await Db.Notifications
            .SingleOrDefaultAsync(item => item.Id == id && item.UserId == profile.UserId, cancellationToken);
        if (notification is null)
        {
            return NotFound();
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await Db.SaveChangesAsync(cancellationToken);
        }

        return RedirectToAction(nameof(Index), new { area = "Farmer", notice = "Notification marked as read." });
    }

    private static FarmerNotificationViewModel MapNotification(Notification notification)
    {
        return new FarmerNotificationViewModel
        {
            Id = notification.Id,
            Title = notification.Title,
            Message = notification.Message,
            TimeLabel = FormatRelativeTime(notification.CreatedAt),
            Icon = GetIcon(notification.Type),
            Tone = GetTone(notification.Type),
            IsRead = notification.IsRead,
            ActionUrl = notification.ActionUrl
        };
    }

    private static string GetIcon(NotificationType type)
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

    private static string GetTone(NotificationType type)
    {
        return type switch
        {
            NotificationType.Order => "green",
            NotificationType.Review => "yellow",
            NotificationType.Account => "blue",
            _ => "muted"
        };
    }
}
