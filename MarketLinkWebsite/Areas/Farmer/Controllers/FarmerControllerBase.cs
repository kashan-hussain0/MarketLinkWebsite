using System.Security.Claims;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Areas.Farmer.Controllers;

public abstract class FarmerControllerBase : Controller
{
    protected const string FarmFallbackImageUrl = "https://images.unsplash.com/photo-1500076656116-558758c991c1?auto=format&fit=crop&w=900&q=80";
    protected readonly ApplicationDbContext Db;

    protected FarmerControllerBase(ApplicationDbContext db)
    {
        Db = db;
    }

    protected async Task<FarmerProfile?> GetCurrentFarmerAsync(CancellationToken cancellationToken = default)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        var identityName = User.Identity?.Name;

        if (string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(identityName))
        {
            var user = await Db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.UserName == identityName || item.Email == identityName, cancellationToken);
            userId = user?.Id;
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var profile = await Db.FarmerProfiles
            .Include(profile => profile.User)
            .SingleOrDefaultAsync(profile => profile.UserId == userId, cancellationToken);
        return profile is not null && profile.Status == FarmerStatus.Active && profile.User.IsActive
            ? profile
            : null;
    }

    protected IActionResult FarmerAccessResult()
    {
        return User.Identity?.IsAuthenticated == true ? Forbid() : Challenge();
    }

    protected async Task SetFarmerChromeAsync(FarmerProfile profile, string? userId = null, CancellationToken cancellationToken = default)
    {
        userId ??= profile.UserId;
        var ownerName = GetOwnerName(profile);
        ViewData["FarmerFarmName"] = profile.FarmName;
        ViewData["FarmerOwnerName"] = ownerName;
        ViewData["FarmerOwnerInitials"] = GetInitials(ownerName);
        ViewData["FarmerFarmInitials"] = GetInitials(profile.FarmName);
        ViewData["FarmerFarmImageUrl"] = profile.User?.ProfilePictureUrl ?? string.Empty;
        ViewData["FarmerStatus"] = profile.Status.ToString();

        var productIds = await Db.Products
            .Where(product => product.FarmerProfileId == profile.Id)
            .Select(product => product.Id)
            .ToListAsync(cancellationToken);

        ViewData["FarmerProductCount"] = productIds.Count;
        ViewData["FarmerOrderCount"] = await Db.Orders.CountAsync(order =>
            order.OrderItems.Any(item => item.ProductId.HasValue && productIds.Contains(item.ProductId.Value)), cancellationToken);
        ViewData["FarmerPendingOrderCount"] = await Db.Orders.CountAsync(order =>
            order.OrderItems.Any(item => item.ProductId.HasValue && productIds.Contains(item.ProductId.Value))
            && (order.Status == OrderStatus.Pending || order.Status == OrderStatus.Accepted || order.Status == OrderStatus.Preparing), cancellationToken);
        ViewData["FarmerReviewCount"] = await Db.Reviews.CountAsync(review =>
            review.FarmerProfileId == profile.Id
            || (review.ProductId.HasValue && productIds.Contains(review.ProductId.Value)), cancellationToken);
        ViewData["FarmerUnreadNotificationCount"] = string.IsNullOrWhiteSpace(userId)
            ? 0
            : await Db.Notifications.CountAsync(notification => notification.UserId == userId && !notification.IsRead, cancellationToken);
        ViewData["FarmerLowStockCount"] = await Db.Inventories.CountAsync(inventory =>
            inventory.QuantityAvailable <= inventory.ReorderThreshold
            && Db.Products.Any(product => product.Id == inventory.ProductId && product.FarmerProfileId == profile.Id), cancellationToken);
    }

    protected IQueryable<Order> OwnedOrders(FarmerProfile profile)
    {
        return Db.Orders.Where(order => order.OrderItems.Any(item =>
            item.ProductId.HasValue
            && item.Product != null
            && item.Product.FarmerProfileId == profile.Id));
    }

    protected async Task<List<int>> GetOwnedProductIdsAsync(FarmerProfile profile, CancellationToken cancellationToken = default)
    {
        return await Db.Products
            .Where(product => product.FarmerProfileId == profile.Id)
            .Select(product => product.Id)
            .ToListAsync(cancellationToken);
    }

    protected static string GetOwnerName(FarmerProfile profile)
    {
        var firstName = profile.User?.FirstName?.Trim() ?? string.Empty;
        var lastName = profile.User?.LastName?.Trim() ?? string.Empty;
        var name = $"{firstName} {lastName}".Trim();
        return string.IsNullOrWhiteSpace(name) ? profile.FarmName : name;
    }

    protected static string GetInitials(string value)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length switch
        {
            0 => "ML",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => $"{words[0][0]}{words[^1][0]}".ToUpperInvariant()
        };
    }

    protected static string FormatDate(DateTime value)
    {
        return value.ToLocalTime().ToString("MMMM d, yyyy 'at' h:mm tt");
    }

    protected static string FormatShortDate(DateTime value)
    {
        return value.ToLocalTime().ToString("MMM d, yyyy");
    }

    protected static string FormatRelativeTime(DateTime value)
    {
        var difference = DateTime.UtcNow - value;
        if (difference.TotalMinutes < 1)
        {
            return "Just now";
        }

        if (difference.TotalHours < 1)
        {
            return $"{(int)difference.TotalMinutes} min ago";
        }

        if (difference.TotalDays < 1)
        {
            return $"{(int)difference.TotalHours} hr ago";
        }

        if (difference.TotalDays < 7)
        {
            return $"{(int)difference.TotalDays} day{(difference.Days == 1 ? string.Empty : "s")} ago";
        }

        return value.ToLocalTime().ToString("MMM d, yyyy");
    }

    protected static string GetUnitLabel(UnitType unit)
    {
        return unit switch
        {
            UnitType.Kg => "per kg",
            UnitType.Gram => "per gram",
            UnitType.Litre => "per litre",
            UnitType.Piece => "per piece",
            UnitType.Dozen => "per dozen",
            UnitType.Bundle => "per bundle",
            _ => "per piece"
        };
    }

    protected static UnitType ParseUnit(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            "kg" or "kilogram" or "per kg" or "per kilogram" or "per pound" or "lb" => UnitType.Kg,
            "gram" or "grams" or "per gram" => UnitType.Gram,
            "litre" or "liter" or "litres" or "liters" or "per litre" or "per liter" => UnitType.Litre,
            "dozen" or "per dozen" => UnitType.Dozen,
            "bundle" or "bundles" or "per bundle" => UnitType.Bundle,
            _ => UnitType.Piece
        };
    }

    protected static string GetStatusLabel(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Pending => "New",
            OrderStatus.Accepted => "Accepted",
            OrderStatus.Preparing => "Preparing",
            OrderStatus.ReadyForPickup => "Ready for pickup",
            OrderStatus.Completed => "Completed",
            OrderStatus.Declined => "Declined",
            OrderStatus.Cancelled => "Cancelled",
            _ => status.ToString()
        };
    }

    protected static string GetStatusTone(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Pending => "amber",
            OrderStatus.Accepted => "blue",
            OrderStatus.Preparing => "blue",
            OrderStatus.ReadyForPickup => "green",
            OrderStatus.Completed => "muted",
            _ => "muted"
        };
    }

    protected static string GetStatusIcon(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Pending => "bi-stars",
            OrderStatus.Accepted => "bi-check2",
            OrderStatus.Preparing => "bi-box-seam",
            OrderStatus.ReadyForPickup => "bi-check2-circle",
            OrderStatus.Completed => "bi-check2-all",
            _ => "bi-dash-circle"
        };
    }

    protected void AddAudit(string action, string entityName, int id, string details)
    {
        var entity = id > 0 ? $"{entityName} #{id}" : entityName;
        if (entity.Length > 100)
        {
            entity = entity[..100];
        }

        Db.AuditLogs.Add(new AuditLog
        {
            UserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
            Action = action.Length > 100 ? action[..100] : action,
            EntityName = entity,
            Details = details.Length > 2000 ? details[..2000] : details,
            Timestamp = DateTime.UtcNow
        });
    }

    protected void QueueNotification(string userId, NotificationType type, string title, string message, string? actionUrl = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        Db.Notifications.Add(new Notification
        {
            UserId = userId,
            Type = type,
            Title = title.Length > 150 ? title[..150] : title,
            Message = message.Length > 500 ? message[..500] : message,
            ActionUrl = actionUrl?.Length > 300 ? actionUrl[..300] : actionUrl
        });
    }

    protected async Task<string?> SaveImageAsync(IFormFile? file, string folder, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
        {
            return null;
        }

        if (file.Length > ImageUpload.MaxBytes)
        {
            throw new InvalidOperationException("Images must be 8 MB or smaller.");
        }

        await using var probe = file.OpenReadStream();
        var header = new byte[16];
        var read = await probe.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
        var extension = ImageUpload.ExtensionFor(file.FileName, file.ContentType, header.AsSpan(0, read).ToArray());
        if (extension is null)
        {
            throw new InvalidOperationException($"That file is not a picture. Upload an image: {ImageUpload.Describe()}.");
        }

        var environment = HttpContext?.RequestServices.GetService<IWebHostEnvironment>();
        if (environment?.WebRootPath is null)
        {
            return null;
        }

        var relativeFolder = Path.Combine("uploads", folder).Replace(Path.DirectorySeparatorChar, '/');
        var absoluteFolder = Path.Combine(environment.WebRootPath, "uploads", folder);
        Directory.CreateDirectory(absoluteFolder);
        var fileName = $"{Guid.NewGuid():N}{extension}";
        var absolutePath = Path.Combine(absoluteFolder, fileName);
        await using (var target = new FileStream(absolutePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            await file.CopyToAsync(target, cancellationToken);
        }

        return $"/{relativeFolder}/{fileName}";
    }
}
