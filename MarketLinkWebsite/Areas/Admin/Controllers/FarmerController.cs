using System.Globalization;
using System.Text;
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
public sealed class FarmerController : AdminControllerBase
{
    public FarmerController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
        : base(db, userManager, roleManager)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] string? search,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var query = Db.FarmerProfiles.AsNoTracking();
        var term = Clean(search);
        if (term.Length > 0)
        {
            var pattern = SearchPattern(term);
            query = query.Where(profile => EF.Functions.Like(profile.FarmName, pattern, "\\")
                || EF.Functions.Like(profile.User.FirstName, pattern, "\\")
                || EF.Functions.Like(profile.User.LastName, pattern, "\\")
                || EF.Functions.Like(profile.User.Email!, pattern, "\\")
                || EF.Functions.Like(profile.City, pattern, "\\"));
        }

        var statusFilter = NormalizeFarmerStatusFilter(status);
        if (statusFilter == "Approved")
        {
            query = query.Where(profile => profile.Status == FarmerStatus.Active);
        }
        else if (statusFilter == "Pending")
        {
            query = query.Where(profile => profile.Status == FarmerStatus.PendingApproval);
        }
        else if (statusFilter == "Paused")
        {
            query = query.Where(profile => profile.Status == FarmerStatus.Suspended);
        }
        else if (statusFilter == "Rejected")
        {
            query = query.Where(profile => profile.Status == FarmerStatus.Rejected);
        }

        var profiles = await query
            .OrderBy(profile => profile.FarmName)
            .Select(profile => new
            {
                profile.Id,
                profile.FarmName,
                profile.City,
                profile.Address,
                profile.Status,
                profile.CreatedAt,
                profile.UpdatedAt,
                profile.Rating,
                profile.UserId,
                FirstName = profile.User.FirstName,
                LastName = profile.User.LastName,
                Email = profile.User.Email ?? string.Empty,
                IsActive = profile.User.IsActive,
                ProductCount = profile.Products.Count
            })
            .ToListAsync(cancellationToken);

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var farmerIds = profiles.Select(profile => profile.Id).ToArray();
        var revenueLookup = farmerIds.Length == 0
            ? new Dictionary<int, decimal>()
            : await Db.OrderItems
                .Where(item => item.Product != null && farmerIds.Contains(item.Product.FarmerProfileId) && item.Order.OrderDate >= monthStart && item.Order.PaymentStatus == PaymentStatus.Paid && item.Order.Status != OrderStatus.Cancelled && item.Order.Status != OrderStatus.Declined)
                .GroupBy(item => item.Product!.FarmerProfileId)
                .Select(group => new { FarmerId = group.Key, Revenue = group.Sum(item => item.Quantity * item.UnitPrice) })
                .ToDictionaryAsync(item => item.FarmerId, item => item.Revenue, cancellationToken);

        var allStatuses = await Db.FarmerProfiles
            .AsNoTracking()
            .Select(profile => profile.Status)
            .ToListAsync(cancellationToken);

        var reviewRows = farmerIds.Length == 0
            ? new List<FarmerReviewRollup>()
            : await Db.Reviews
                .AsNoTracking()
                .Where(review => review.FarmerProfileId != null
                    && farmerIds.Contains(review.FarmerProfileId.Value))
                .Select(review => new FarmerReviewRollup
                {
                    FarmerId = review.FarmerProfileId!.Value,
                    Rating = review.Rating,
                    HasReply = review.FarmerReply != null && review.FarmerReply != string.Empty,
                    CreatedAt = review.CreatedAt,
                    Title = review.Title,
                    Comment = review.Comment
                })
                .ToListAsync(cancellationToken);

        var reviewLookup = reviewRows
            .GroupBy(row => row.FarmerId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var latest = group.OrderByDescending(row => row.CreatedAt).First();
                    return new FarmerReviewStats
                    {
                        Count = group.Count(),
                        Unanswered = group.Count(row => !row.HasReply),
                        LatestDate = latest.CreatedAt,
                        LatestTitle = latest.Title,
                        LatestComment = latest.Comment,
                        LatestRating = latest.Rating
                    };
                });

        var displayedFarmers = profiles.Select(profile =>
        {
            var stats = reviewLookup.GetValueOrDefault(profile.Id);
            var summary = ToSummary(
                profile.Id,
                profile.FarmName,
                string.IsNullOrWhiteSpace(profile.City) ? profile.Address : $"{profile.City}, {profile.Address}",
                profile.Status,
                profile.CreatedAt,
                $"{profile.FirstName} {profile.LastName}".Trim(),
                profile.Email,
                profile.IsActive,
                profile.ProductCount,
                profile.Rating,
                revenueLookup.GetValueOrDefault(profile.Id));

            summary.ReviewCount = stats?.Count ?? 0;
            summary.UnansweredReviewCount = stats?.Unanswered ?? 0;
            summary.LatestReviewLabel = stats is null ? string.Empty : RelativeTime(stats.LatestDate);
            summary.LatestReviewText = stats is null ? string.Empty : string.IsNullOrWhiteSpace(stats.LatestTitle) ? stats.LatestComment : stats.LatestTitle;
            summary.LatestReviewRating = stats?.LatestRating ?? 0;
            return summary;
        }).ToList();

        var model = new FarmerListViewModel
        {
            SearchTerm = term,
            StatusFilter = statusFilter,
            TotalFarmers = allStatuses.Count,
            ActiveFarmers = allStatuses.Count(status => status == FarmerStatus.Active),
            PendingFarmers = allStatuses.Count(status => status == FarmerStatus.PendingApproval),
            Farmers = displayedFarmers
        };
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Approvals(CancellationToken cancellationToken)
    {
        var profiles = await Db.FarmerProfiles
            .AsNoTracking()
            .Where(profile => profile.Status == FarmerStatus.PendingApproval)
            .OrderBy(profile => profile.CreatedAt)
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
        var pending = profiles.Select(profile => ToSummary(
            profile.Id,
            profile.FarmName,
            string.IsNullOrWhiteSpace(profile.City) ? profile.Address : $"{profile.City}, {profile.Address}",
            profile.Status,
            profile.CreatedAt,
            $"{profile.FirstName} {profile.LastName}".Trim(),
            profile.Email,
            profile.IsActive,
            profile.ProductCount,
            profile.Rating,
            0m)).ToList();
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var reviewedProfiles = await Db.FarmerProfiles
            .AsNoTracking()
            .Where(profile => profile.UpdatedAt >= monthStart && profile.Status != FarmerStatus.PendingApproval)
            .Select(profile => new { profile.CreatedAt, profile.UpdatedAt, profile.Status })
            .ToListAsync(cancellationToken);
        var averageHours = reviewedProfiles.Count == 0
            ? 0
            : (int)Math.Round(reviewedProfiles.Average(profile => Math.Max(0, (profile.UpdatedAt - profile.CreatedAt).TotalHours)));
        var model = new FarmerApprovalViewModel
        {
            PendingCount = pending.Count,
            ApprovedThisMonth = reviewedProfiles.Count(profile => profile.Status == FarmerStatus.Active),
            AverageReviewTime = averageHours,
            PendingFarmers = pending
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest();
        }

        var profile = await Db.FarmerProfiles.Include(item => item.User).FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        if (profile.Status != FarmerStatus.PendingApproval)
        {
            TempData["Error"] = "Only pending applications can be approved.";
            return RedirectToAction(nameof(Approvals));
        }

        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);
        if (!await UserManager.IsInRoleAsync(profile.User, "Farmer"))
        {
            var roleResult = await UserManager.AddToRoleAsync(profile.User, "Farmer");
            if (!roleResult.Succeeded)
            {
                TempData["Error"] = "The Farmer role could not be assigned.";
                return RedirectToAction(nameof(Approvals));
            }
        }

        profile.Status = FarmerStatus.Active;
        profile.UpdatedAt = DateTime.UtcNow;
        profile.User.IsActive = true;
        AddNotification(profile.User, NotificationType.Account, "Farmer application approved", $"{profile.FarmName} is approved and can now manage marketplace products.", "/Farmer");
        AddAudit("Approve farmer", nameof(FarmerProfile), profile.Id, $"Approved {profile.FarmName} for {profile.User.Email}.");
        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        TempData["Success"] = $"{profile.FarmName} was approved and notified.";
        return RedirectToAction(nameof(Approvals));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(FarmerDecisionViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.Reason))
        {
            TempData["Error"] = "Enter a reason with at least 3 characters.";
            return RedirectToAction(nameof(Approvals));
        }

        var profile = await Db.FarmerProfiles.Include(item => item.User).FirstOrDefaultAsync(item => item.Id == model.Id, cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        if (profile.Status != FarmerStatus.PendingApproval)
        {
            TempData["Error"] = "Only pending applications can be declined.";
            return RedirectToAction(nameof(Approvals));
        }

        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);
        if (await UserManager.IsInRoleAsync(profile.User, "Farmer"))
        {
            var roleResult = await UserManager.RemoveFromRoleAsync(profile.User, "Farmer");
            if (!roleResult.Succeeded)
            {
                TempData["Error"] = "The Farmer role could not be updated.";
                return RedirectToAction(nameof(Approvals));
            }
        }

        profile.Status = FarmerStatus.Rejected;
        profile.UpdatedAt = DateTime.UtcNow;
        var securityStampResult = await UserManager.UpdateSecurityStampAsync(profile.User);
        if (!securityStampResult.Succeeded)
        {
            TempData["Error"] = "The farmer session could not be invalidated.";
            return RedirectToAction(nameof(Approvals));
        }

        var reason = Clean(model.Reason);
        AddNotification(profile.User, NotificationType.Account, "Farmer application needs changes", $"Your application for {profile.FarmName} needs attention: {reason}", "/Farmer");
        AddAudit("Reject farmer", nameof(FarmerProfile), profile.Id, $"Rejected {profile.FarmName}. Reason: {reason}");
        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        TempData["Success"] = $"{profile.FarmName} was declined and notified.";
        return RedirectToAction(nameof(Approvals));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Suspend(FarmerDecisionViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(model.Reason))
        {
            TempData["Error"] = "Enter a suspension reason.";
            return RedirectToAction(nameof(Index));
        }

        var profile = await Db.FarmerProfiles.Include(item => item.User).FirstOrDefaultAsync(item => item.Id == model.Id, cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        if (profile.Status != FarmerStatus.Active)
        {
            TempData["Error"] = "Only active farmers can be suspended.";
            return RedirectToAction(nameof(Index));
        }

        var reason = Clean(model.Reason);
        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);

        profile.Status = FarmerStatus.Suspended;
        profile.UpdatedAt = DateTime.UtcNow;

        var products = await Db.Products.Where(product => product.FarmerProfileId == profile.Id).ToListAsync(cancellationToken);
        var hiddenIds = new List<int>();
        foreach (var product in products)
        {
            if (product.IsAvailable)
            {
                hiddenIds.Add(product.Id);
            }

            product.IsAvailable = false;
            product.UpdatedAt = DateTime.UtcNow;
        }

        var securityStampResult = await UserManager.UpdateSecurityStampAsync(profile.User);
        if (!securityStampResult.Succeeded)
        {
            TempData["Error"] = "The farmer session could not be invalidated.";
            return RedirectToAction(nameof(Index));
        }

        AddNotification(profile.User, NotificationType.Account, "Farmer account suspended", $"Your access to {profile.FarmName} was suspended and {products.Count} products were hidden. Reason: {reason}", "/Account/Profile");
        AddAudit("Suspend farmer", nameof(FarmerProfile), profile.Id, $"Suspended {profile.FarmName} and hid {hiddenIds.Count} products. Reason: {reason}");
        AddAudit("Hidden by suspension", nameof(FarmerProfile), profile.Id, string.Join(",", hiddenIds));
        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        TempData["Success"] = $"{profile.FarmName} was suspended.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivate(int id, CancellationToken cancellationToken)
    {
        var profile = await Db.FarmerProfiles.Include(item => item.User).FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (profile is null)
        {
            return NotFound();
        }

        if (profile.Status != FarmerStatus.Suspended)
        {
            TempData["Error"] = "Only suspended farmers can be reactivated.";
            return RedirectToAction(nameof(Index));
        }

        if (!profile.User.IsActive)
        {
            TempData["Error"] = "Reactivate the user account before reactivating the farmer profile.";
            return RedirectToAction(nameof(Index));
        }

        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);
        if (!await UserManager.IsInRoleAsync(profile.User, "Farmer"))
        {
            var roleResult = await UserManager.AddToRoleAsync(profile.User, "Farmer");
            if (!roleResult.Succeeded)
            {
                TempData["Error"] = "The Farmer role could not be assigned.";
                return RedirectToAction(nameof(Index));
            }
        }

        profile.Status = FarmerStatus.Active;
        profile.UpdatedAt = DateTime.UtcNow;

        var hiddenProductIds = await Db.AuditLogs
            .Where(log => log.Action == "Hidden by suspension"
                && log.EntityName == $"{nameof(FarmerProfile)} #{profile.Id}")
            .OrderByDescending(log => log.Id)
            .Select(log => log.Details)
            .FirstOrDefaultAsync(cancellationToken);

        var restored = 0;
        if (!string.IsNullOrWhiteSpace(hiddenProductIds))
        {
            var ids = hiddenProductIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => int.TryParse(value, out var parsed) ? parsed : 0)
                .Where(id => id > 0)
                .ToList();

            if (ids.Count > 0)
            {
                var toRestore = await Db.Products
                    .Where(product => ids.Contains(product.Id) && product.Inventory != null && product.Inventory.QuantityAvailable > 0)
                    .ToListAsync(cancellationToken);

                foreach (var product in toRestore)
                {
                    product.IsAvailable = true;
                    product.UpdatedAt = DateTime.UtcNow;
                    restored++;
                }
            }
        }

        AddNotification(profile.User, NotificationType.Account, "Farmer account reactivated", $"{profile.FarmName} can manage products again and {restored} listing(s) are back on the marketplace.", "/Farmer");
        AddAudit("Reactivate farmer", nameof(FarmerProfile), profile.Id, $"Reactivated {profile.FarmName} and restored {restored} listing(s).");
        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        TempData["Success"] = $"{profile.FarmName} was reactivated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(FarmerDeleteViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid || model.Id <= 0)
        {
            TempData["Error"] = "Choose a farmer and type DELETE to confirm.";
            return RedirectToAction(nameof(Index));
        }

        if (!string.Equals(model.Confirmation?.Trim(), "DELETE", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "Type DELETE in the confirmation box to remove this farmer permanently.";
            return RedirectToAction(nameof(Index));
        }

        var profile = await Db.FarmerProfiles
            .Include(item => item.User)
            .Include(item => item.Products)
            .FirstOrDefaultAsync(item => item.Id == model.Id, cancellationToken);

        if (profile is null)
        {
            return NotFound();
        }

        if (string.Equals(profile.User.Email, "admin@marketlink.com", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "The administrator account cannot be removed from the farmer directory.";
            return RedirectToAction(nameof(Index));
        }

        var openOrders = await Db.Orders.CountAsync(order =>
            order.Status == OrderStatus.Pending
            || order.Status == OrderStatus.Accepted
            || order.Status == OrderStatus.Preparing
            || order.Status == OrderStatus.ReadyForPickup, cancellationToken);

        if (openOrders > 0)
        {
            TempData["Error"] = $"There are {openOrders} open order(s) on the platform. Ask the farmers to accept, complete or cancel them before removing a farmer.";
            return RedirectToAction(nameof(Index));
        }

        var farmName = profile.FarmName;
        var userId = profile.UserId;

        var productIds = profile.Products.Select(product => product.Id).ToList();
        var inventoryCount = await Db.Inventories.CountAsync(inventory => productIds.Contains(inventory.ProductId), cancellationToken);
        var planCount = await Db.WeeklyStockPlans.CountAsync(plan => productIds.Contains(plan.ProductId), cancellationToken);

        profile.Products.Clear();
        await Db.SaveChangesAsync(cancellationToken);

        await Db.Reviews.Where(review => review.FarmerProfileId == profile.Id).ExecuteDeleteAsync(cancellationToken);
        await Db.Reviews.Where(review => review.ProductId != null && productIds.Contains(review.ProductId.Value)).ExecuteDeleteAsync(cancellationToken);
        await Db.FavouriteFarmers.Where(favourite => favourite.FarmerProfileId == profile.Id).ExecuteDeleteAsync(cancellationToken);
        await Db.FavouriteProducts.Where(favourite => productIds.Contains(favourite.ProductId)).ExecuteDeleteAsync(cancellationToken);
        await Db.CartItems.Where(item => productIds.Contains(item.ProductId)).ExecuteDeleteAsync(cancellationToken);
        await Db.WeeklyStockPlans.Where(plan => productIds.Contains(plan.ProductId)).ExecuteDeleteAsync(cancellationToken);
        await Db.Inventories.Where(inventory => productIds.Contains(inventory.ProductId)).ExecuteDeleteAsync(cancellationToken);
        await Db.Products.Where(product => productIds.Contains(product.Id)).ExecuteDeleteAsync(cancellationToken);
        await Db.FarmerProfiles.Where(item => item.Id == profile.Id).ExecuteDeleteAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(model.Reason))
        {
            await Db.Notifications.AddAsync(new Notification
            {
                UserId = userId,
                Type = NotificationType.Account,
                Title = "Farmer account removed",
                Message = $"The farm {farmName} was removed from MarketLink by an administrator. Reason: {model.Reason.Trim()}",
                ActionUrl = "/Account/Login",
                CreatedAt = DateTime.UtcNow
            }, cancellationToken);
        }

        await Db.SaveChangesAsync(cancellationToken);

        var removeResult = await UserManager.DeleteAsync(profile.User);
        if (!removeResult.Succeeded)
        {
            TempData["Error"] = "The farmer record was removed but the sign-in account could not be deleted: " + string.Join(" ", removeResult.Errors.Select(error => error.Description));
        }
        else
        {
            TempData["Success"] = $"{farmName} was removed along with {productIds.Count} product(s), {inventoryCount} stock record(s) and {planCount} weekly plan(s).";
        }

        AddAudit("Delete farmer", nameof(FarmerProfile), profile.Id, $"Removed {farmName} and {productIds.Count} product(s). Reason: {(string.IsNullOrWhiteSpace(model.Reason) ? "not given" : model.Reason.Trim())}");
        await Db.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var farmers = await Db.FarmerProfiles
            .AsNoTracking()
            .OrderBy(profile => profile.FarmName)
            .Select(profile => new
            {
                profile.FarmName,
                Owner = profile.User.FirstName + " " + profile.User.LastName,
                profile.User.Email,
                profile.Address,
                profile.City,
                Status = profile.Status.ToString(),
                profile.Rating,
                ProductCount = profile.Products.Count
            })
            .ToListAsync(cancellationToken);
        var builder = new StringBuilder();
        builder.AppendLine("Farm,Owner,Email,Address,City,Status,Rating,Products");
        foreach (var farmer in farmers)
        {
            builder.AppendLine(string.Join(',', new[]
            {
                CsvCell(farmer.FarmName),
                CsvCell(farmer.Owner),
                CsvCell(farmer.Email),
                CsvCell(farmer.Address),
                CsvCell(farmer.City),
                CsvCell(farmer.Status),
                farmer.Rating.ToString("0.00", CultureInfo.InvariantCulture),
                farmer.ProductCount.ToString(CultureInfo.InvariantCulture)
            }));
        }

        AddAudit("Export farmers", nameof(FarmerProfile), 0, $"Exported {farmers.Count} farmer records.");
        await Db.SaveChangesAsync(cancellationToken);
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
        return File(bytes, "text/csv", $"marketlink-farmers-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private static string NormalizeFarmerStatusFilter(string? status)
    {
        var value = Clean(status);
        return value.Equals("Approved", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Pending", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Paused", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Rejected", StringComparison.OrdinalIgnoreCase)
                ? value
                : "All farmers";
    }

    private static string CsvCell(string? value)
    {
        var normalized = value ?? string.Empty;
        if (normalized.StartsWith('=') || normalized.StartsWith('+') || normalized.StartsWith('-') || normalized.StartsWith('@'))
        {
            normalized = "'" + normalized;
        }

        return $"\"{normalized.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static FarmerSummary ToSummary(
        int id,
        string farmName,
        string location,
        FarmerStatus status,
        DateTime createdAt,
        string name,
        string email,
        bool isActive,
        int productCount,
        decimal rating,
        decimal monthlyRevenue)
    {
        var statusLabel = FarmerStatusLabel(status);
        return new FarmerSummary
        {
            Id = id,
            FarmName = farmName,
            Location = location,
            Status = statusLabel,
            StatusTone = StatusTone(statusLabel),
            SubmittedLabel = RelativeTime(createdAt),
            Name = name,
            Email = email,
            IsActive = isActive,
            Initials = Initials(name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()),
            AvatarTone = AvatarTone(string.IsNullOrWhiteSpace(email) ? id.ToString(CultureInfo.InvariantCulture) : email),
            ProductCount = productCount,
            Rating = rating,
            MonthlyRevenue = monthlyRevenue
        };
    }
}
