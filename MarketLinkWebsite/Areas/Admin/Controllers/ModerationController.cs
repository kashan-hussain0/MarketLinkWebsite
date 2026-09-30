using System.Globalization;
using MarketLinkWebsite.Areas.Admin.Models;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using MarketLinkWebsite.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = "AdminAccess")]
public sealed class ModerationController : AdminControllerBase
{
    private readonly ReviewAggregateService reviewAggregates;

    public ModerationController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        ReviewAggregateService reviewAggregates)
        : base(db, userManager, roleManager)
    {
        this.reviewAggregates = reviewAggregates;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] string? search, CancellationToken cancellationToken)
    {
        var term = Clean(search);
        var reviewQuery = Db.Reviews.AsNoTracking();
        var productQuery = Db.Products.AsNoTracking();
        if (term.Length > 0)
        {
            var pattern = SearchPattern(term);
            reviewQuery = reviewQuery.Where(review => EF.Functions.Like(review.Comment, pattern, "\\")
                || EF.Functions.Like(review.Title, pattern, "\\")
                || EF.Functions.Like(review.Product!.Name, pattern, "\\")
                || EF.Functions.Like(review.FarmerProfile!.FarmName, pattern, "\\")
                || EF.Functions.Like(review.User.FirstName, pattern, "\\")
                || EF.Functions.Like(review.User.LastName, pattern, "\\"));
            productQuery = productQuery.Where(product => EF.Functions.Like(product.Name, pattern, "\\")
                || EF.Functions.Like(product.FarmerProfile.FarmName, pattern, "\\"));
        }

        var reviews = await reviewQuery
            .OrderByDescending(review => review.CreatedAt)
            .Take(100)
            .Select(review => new ReviewModerationItem
            {
                Id = review.Id,
                Target = review.Product != null ? review.Product.Name : review.FarmerProfile != null ? review.FarmerProfile.FarmName : "Marketplace",
                Author = review.User.FirstName + " " + review.User.LastName,
                Rating = review.Rating,
                Title = review.Title,
                Comment = review.Comment,
                VerifiedPurchase = review.VerifiedPurchase,
                HasFarmerReply = review.FarmerReply != null,
                TimeLabel = review.CreatedAt.ToString("MMM d, yyyy, h:mm tt", CultureInfo.InvariantCulture)
            })
            .ToListAsync(cancellationToken);

        var products = await productQuery
            .OrderByDescending(product => product.UpdatedAt)
            .Take(100)
            .Select(product => new ProductModerationItem
            {
                Id = product.Id,
                Name = product.Name,
                FarmerName = product.FarmerProfile.FarmName,
                CategoryName = product.Category.Name,
                Price = product.Price,
                IsAvailable = product.IsAvailable,
                IsOrganic = product.IsOrganic
            })
            .ToListAsync(cancellationToken);
        var model = new ModerationListViewModel
        {
            SearchTerm = term,
            ReviewCount = await Db.Reviews.CountAsync(cancellationToken),
            HiddenProductCount = await Db.Products.CountAsync(product => !product.IsAvailable, cancellationToken),
            VerifiedReviewCount = await Db.Reviews.CountAsync(review => review.VerifiedPurchase, cancellationToken),
            Reviews = reviews,
            Products = products
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteReview(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest();
        }

        var review = await Db.Reviews.Include(item => item.User).FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (review is null)
        {
            return NotFound();
        }

        var target = review.ProductId.HasValue
            ? $"product {review.ProductId}"
            : (review.FarmerProfileId.HasValue ? $"farmer {review.FarmerProfileId}" : "marketplace");
        var targetProductId = review.ProductId;
        var targetFarmerId = review.FarmerProfileId;
        await using var transaction = await Db.Database.BeginTransactionAsync(cancellationToken);
        Db.Reviews.Remove(review);
        AddNotification(review.User, NotificationType.Review, "Review removed by moderation", "A marketplace review was removed by an administrator.");
        AddAudit("Delete review", nameof(Review), id, $"Removed review {id} for {target} by {review.User.Email}.");
        await Db.SaveChangesAsync(cancellationToken);
        if (targetProductId.HasValue)
        {
            await reviewAggregates.UpdateForProductAsync(targetProductId.Value, cancellationToken);
        }
        else if (targetFarmerId.HasValue)
        {
            await reviewAggregates.UpdateForFarmerAsync(targetFarmerId.Value, cancellationToken);
        }

        await Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        TempData["Success"] = "The review was removed and the action was audited.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetProductAvailability(ProductModerationUpdateViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Select a valid product.";
            return RedirectToAction(nameof(Index));
        }

        var product = await Db.Products
            .Include(item => item.FarmerProfile)
                .ThenInclude(item => item.User)
            .FirstOrDefaultAsync(item => item.Id == model.Id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        if (model.IsAvailable && (product.FarmerProfile.Status != FarmerStatus.Active || !product.FarmerProfile.User.IsActive))
        {
            TempData["Error"] = "An inactive farmer's product cannot be made available.";
            return RedirectToAction(nameof(Index));
        }

        if (model.IsAvailable && !await UserManager.IsInRoleAsync(product.FarmerProfile.User, "Farmer"))
        {
            return Forbid();
        }

        if (product.IsAvailable == model.IsAvailable)
        {
            TempData["Error"] = "The product already has that visibility.";
            return RedirectToAction(nameof(Index));
        }

        product.IsAvailable = model.IsAvailable;
        product.UpdatedAt = DateTime.UtcNow;
        AddNotification(
            product.FarmerProfile.User,
            NotificationType.Account,
            model.IsAvailable ? "Product restored" : "Product hidden by moderation",
            model.IsAvailable ? $"{product.Name} is visible in the catalog again." : $"{product.Name} was hidden pending review.");
        AddAudit(model.IsAvailable ? "Restore product" : "Hide product", nameof(Product), product.Id, $"{(model.IsAvailable ? "Restored" : "Hid")} {product.Name} for {product.FarmerProfile.FarmName}.");
        await Db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = model.IsAvailable ? "The product was restored." : "The product was hidden.";
        return RedirectToAction(nameof(Index));
    }
}
