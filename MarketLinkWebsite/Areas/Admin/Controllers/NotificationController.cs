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
public sealed class NotificationController : AdminControllerBase
{
    public NotificationController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
        : base(db, userManager, roleManager)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] string? filter, CancellationToken cancellationToken)
    {
        return View("Index", await BuildListAsync(null, filter, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Announce(
        // The publish form lives on the Index page, so its fields arrive with an
        // "Announcement." prefix. Without this prefix the binder never saw the
        // title or message and every submission came back as a validation failure.
        [Bind(Prefix = "Announcement")] AnnouncementFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (!IsAudienceAllowed(model.Audience))
        {
            ModelState.AddModelError(nameof(model.Audience), "Select a supported audience.");
        }

        if (!string.IsNullOrWhiteSpace(model.ActionUrl) && !Url.IsLocalUrl(model.ActionUrl))
        {
            ModelState.AddModelError(nameof(model.ActionUrl), "Use a local URL that starts with /.");
        }

        if (!ModelState.IsValid)
        {
            return View("Index", await BuildListAsync(model, filter: null, cancellationToken));
        }

        var roleIds = await Db.Roles
            .Where(role => role.Name == "Admin" || role.Name == "Farmer" || role.Name == "Customer")
            .ToDictionaryAsync(role => role.Name!, role => role.Id, cancellationToken);
        var requiredRole = model.Audience switch
        {
            "Customers" => "Customer",
            "Farmers" => "Farmer",
            "Administrators" => "Admin",
            _ => null
        };
        if (requiredRole is not null && !roleIds.ContainsKey(requiredRole))
        {
            ModelState.AddModelError(nameof(model.Audience), "The selected Identity role is unavailable.");
            return View("Index", await BuildListAsync(model, filter: null, cancellationToken));
        }

        var allowedRoleIds = roleIds.Values.ToArray();
        var targetQuery = Db.Users.Where(user => user.IsActive && Db.UserRoles.Any(userRole => allowedRoleIds.Contains(userRole.RoleId)));
        if (model.Audience == "Customers" && roleIds.TryGetValue("Customer", out var customerRoleId))
        {
            targetQuery = targetQuery.Where(user => Db.UserRoles.Any(userRole => userRole.UserId == user.Id && userRole.RoleId == customerRoleId));
        }
        else if (model.Audience == "Farmers" && roleIds.TryGetValue("Farmer", out var farmerRoleId))
        {
            targetQuery = targetQuery.Where(user => Db.UserRoles.Any(userRole => userRole.UserId == user.Id && userRole.RoleId == farmerRoleId));
        }
        else if (model.Audience == "Administrators" && roleIds.TryGetValue("Admin", out var adminRoleId))
        {
            targetQuery = targetQuery.Where(user => Db.UserRoles.Any(userRole => userRole.UserId == user.Id && userRole.RoleId == adminRoleId));
        }

        var recipients = await targetQuery.ToListAsync(cancellationToken);
        var title = Clean(model.Title);
        var message = Clean(model.Message);
        var actionUrl = Clean(model.ActionUrl);
        foreach (var recipient in recipients)
        {
            AddNotification(recipient, NotificationType.Announcement, title, message, actionUrl.Length == 0 ? null : actionUrl);
        }

        AddAudit("Publish announcement", nameof(Notification), 0, $"Sent '{title}' to {recipients.Count} active users ({model.Audience}).");
        await Db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = $"The announcement was sent to {recipients.Count} active users.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id, CancellationToken cancellationToken)
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(CurrentUserId))
        {
            return BadRequest();
        }

        var notification = await Db.Notifications.FirstOrDefaultAsync(item => item.Id == id && item.UserId == CurrentUserId, cancellationToken);
        if (notification is null)
        {
            return NotFound();
        }

        if (notification.IsRead)
        {
            return RedirectToAction(nameof(Index));
        }

        notification.IsRead = true;
        notification.ReadAt = DateTime.UtcNow;
        await Db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = "Notification marked as read.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(CurrentUserId))
        {
            return Unauthorized();
        }

        var unread = await Db.Notifications
            .Where(notification => notification.UserId == CurrentUserId && !notification.IsRead)
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var notification in unread)
        {
            notification.IsRead = true;
            notification.ReadAt = now;
        }

        await Db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = $"{unread.Count} notifications marked as read.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<NotificationListViewModel> BuildListAsync(AnnouncementFormViewModel? announcement, string? filter, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(CurrentUserId))
        {
            return new NotificationListViewModel { Announcement = announcement ?? new AnnouncementFormViewModel() };
        }

        var query = Db.Notifications.AsNoTracking().Where(notification => notification.UserId == CurrentUserId);
        var normalizedFilter = Clean(filter);
        if (normalizedFilter.Equals("Unread", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(notification => !notification.IsRead);
        }
        else if (normalizedFilter.Equals("Read", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(notification => notification.IsRead);
        }
        else
        {
            normalizedFilter = "All notifications";
        }

        var notifications = await query
            .OrderByDescending(notification => notification.CreatedAt)
            .Select(notification => new AdminNotificationItem
            {
                Id = notification.Id,
                Title = notification.Title,
                Message = notification.Message,
                TimeLabel = notification.CreatedAt.ToString("MMM d, yyyy, h:mm tt", CultureInfo.InvariantCulture),
                IsRead = notification.IsRead,
                ActionUrl = notification.ActionUrl,
                Type = notification.Type,
                Timestamp = notification.CreatedAt
            })
            .ToListAsync(cancellationToken);
        foreach (var notification in notifications)
        {
            notification.TimeLabel = RelativeTime(notification.Timestamp);
            notification.Icon = NotificationIcon(notification.Type);
            notification.Tone = NotificationTone(notification.Type);
            if (!string.IsNullOrWhiteSpace(notification.ActionUrl) && !Url.IsLocalUrl(notification.ActionUrl))
            {
                notification.ActionUrl = null;
            }
        }

        return new NotificationListViewModel
        {
            Filter = normalizedFilter,
            TotalCount = await Db.Notifications.CountAsync(notification => notification.UserId == CurrentUserId, cancellationToken),
            UnreadCount = await Db.Notifications.CountAsync(notification => notification.UserId == CurrentUserId && !notification.IsRead, cancellationToken),
            SystemCount = await Db.Notifications.CountAsync(notification => notification.UserId == CurrentUserId && notification.Type == NotificationType.System, cancellationToken),
            Notifications = notifications,
            Announcement = announcement ?? new AnnouncementFormViewModel()
        };
    }

    private static bool IsAudienceAllowed(string? audience)
    {
        return Clean(audience) is "All active users" or "Customers" or "Farmers" or "Administrators";
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
}
