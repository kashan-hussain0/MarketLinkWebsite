using System.Globalization;
using System.Security.Claims;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MarketLinkWebsite.Areas.Admin.Controllers;

public abstract class AdminControllerBase : Controller
{
    protected AdminControllerBase(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        Db = db;
        UserManager = userManager;
        RoleManager = roleManager;
    }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var currentUser = await UserManager.GetUserAsync(User);
            if (currentUser is null || !currentUser.IsActive || !await UserManager.IsInRoleAsync(currentUser, "Admin"))
            {
                context.Result = Forbid();
                return;
            }
        }

        await next();
    }

    protected ApplicationDbContext Db { get; }
    protected UserManager<ApplicationUser> UserManager { get; }
    protected RoleManager<IdentityRole> RoleManager { get; }
    protected string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);

    protected void AddAudit(string action, string entityName, int id, string details)
    {
        Db.AuditLogs.Add(new AuditLog
        {
            UserId = CurrentUserId,
            Action = Trim(action, 100),
            EntityName = id > 0 ? Trim($"{entityName} #{id}", 100) : Trim(entityName, 100),
            Details = Trim(details, 2000),
            Timestamp = DateTime.UtcNow
        });
    }

    protected void AddNotification(
        ApplicationUser user,
        NotificationType type,
        string title,
        string message,
        string? actionUrl = null)
    {
        if (!user.IsActive)
        {
            return;
        }

        Db.Notifications.Add(new Notification
        {
            UserId = user.Id,
            Type = type,
            Title = Trim(title, 150),
            Message = Trim(message, 500),
            ActionUrl = string.IsNullOrWhiteSpace(actionUrl) ? null : Trim(actionUrl, 300),
            CreatedAt = DateTime.UtcNow
        });
    }

    protected static string Clean(string? value) => value?.Trim() ?? string.Empty;

    protected static string SearchPattern(string value)
    {
        var escaped = value.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "[[]", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    protected static string Initials(string? firstName, string? lastName)
    {
        var first = string.IsNullOrWhiteSpace(firstName) ? string.Empty : firstName.Trim()[0].ToString().ToUpperInvariant();
        var last = string.IsNullOrWhiteSpace(lastName) ? string.Empty : lastName.Trim()[0].ToString().ToUpperInvariant();
        return string.Concat(first, last) is { Length: > 0 } initials ? initials : "U";
    }

    protected static string AvatarTone(string value)
    {
        var tones = new[] { "sage", "amber", "violet", "blue", "mint", "coral" };
        var hash = value.Aggregate(0, (current, character) => HashCode.Combine(current, character));
        return tones[Math.Abs(hash) % tones.Length];
    }

    protected static string RelativeTime(DateTime value)
    {
        var elapsed = DateTime.UtcNow - value;
        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return "Just now";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes} min ago";
        }

        if (elapsed < TimeSpan.FromDays(1))
        {
            return $"{(int)elapsed.TotalHours} hrs ago";
        }

        if (elapsed < TimeSpan.FromDays(7))
        {
            return $"{(int)elapsed.TotalDays} days ago";
        }

        return value.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }

    protected static string CompactCurrency(decimal value)
    {
        if (value >= 1_000_000m)
        {
            return $"${value / 1_000_000m:0.#}m";
        }

        if (value >= 1_000m)
        {
            return $"${value / 1_000m:0.#}k";
        }

        return $"${value:0}";
    }

    protected static string FarmerStatusLabel(FarmerStatus status) => status switch
    {
        FarmerStatus.Active => "Approved",
        FarmerStatus.PendingApproval => "Pending",
        FarmerStatus.Suspended => "Paused",
        FarmerStatus.Rejected => "Rejected",
        _ => "Unknown"
    };

    protected static string StatusTone(string status) => status switch
    {
        "Approved" or "Active" or "Completed" or "Paid" or "Open" => "success",
        "Pending" or "Processing" or "Preparing" => "warning",
        "Paused" or "Inactive" or "Refunded" => "secondary",
        "Declined" or "Cancelled" or "Failed" => "danger",
        "Ready for pickup" or "New" => "info",
        _ => "success"
    };

    protected static string OrderStatusLabel(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "Pending",
        OrderStatus.Accepted => "Accepted",
        OrderStatus.Preparing => "Preparing",
        OrderStatus.ReadyForPickup => "Ready for pickup",
        OrderStatus.Completed => "Completed",
        OrderStatus.Declined => "Declined",
        OrderStatus.Cancelled => "Cancelled",
        _ => "Unknown"
    };

    protected static string PaymentStatusLabel(PaymentStatus status) => status.ToString();

    protected static string Trim(string value, int maximumLength)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }
}
