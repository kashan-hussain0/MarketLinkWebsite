using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Areas.Farmer.Controllers;

[Area("Farmer")]
[Authorize(Policy = "ActiveFarmerAccess")]
public sealed class ReviewController : FarmerControllerBase
{
    public ReviewController(ApplicationDbContext db) : base(db)
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
        var reviews = await Db.Reviews
            .AsNoTracking()
            .Include(review => review.User)
            .Include(review => review.Product)
            .Where(review => review.FarmerProfileId == profile.Id
                || (review.ProductId.HasValue && review.Product != null && review.Product.FarmerProfileId == profile.Id))
            .OrderByDescending(review => review.CreatedAt)
            .ToListAsync(cancellationToken);
        var normalizedFilter = string.Equals(filter, "With responses", StringComparison.OrdinalIgnoreCase) ? "With responses" : "All reviews";
        var visibleReviews = normalizedFilter == "With responses"
            ? reviews.Where(review => !string.IsNullOrWhiteSpace(review.FarmerReply)).ToList()
            : reviews;
        var average = reviews.Count == 0 ? 0 : Math.Round((decimal)reviews.Average(review => review.Rating), 1);
        var fiveStar = reviews.Count == 0 ? 0 : (int)Math.Round((decimal)reviews.Count(review => review.Rating == 5) / reviews.Count * 100m);
        var fourStar = reviews.Count == 0 ? 0 : (int)Math.Round((decimal)reviews.Count(review => review.Rating == 4) / reviews.Count * 100m);
        var threeStar = reviews.Count == 0 ? 0 : (int)Math.Round((decimal)reviews.Count(review => review.Rating == 3) / reviews.Count * 100m);
        var twoStar = reviews.Count == 0 ? 0 : (int)Math.Round((decimal)reviews.Count(review => review.Rating == 2) / reviews.Count * 100m);
        var oneStar = reviews.Count == 0 ? 0 : (int)Math.Round((decimal)reviews.Count(review => review.Rating == 1) / reviews.Count * 100m);
        var verifiedCount = reviews.Count(review => review.VerifiedPurchase);
        var model = new ReviewListViewModel
        {
            Filter = normalizedFilter,
            AverageRating = average,
            ReviewCount = reviews.Count,
            ResponseCount = reviews.Count(review => !string.IsNullOrWhiteSpace(review.FarmerReply)),
            FiveStarPercentage = fiveStar,
            FourStarPercentage = fourStar,
            ThreeStarPercentage = threeStar,
            TwoStarPercentage = twoStar,
            OneStarPercentage = oneStar,
            InsightTitle = reviews.Count == 0 ? "Your first review is waiting" : $"Neighbors rate your farm {average:0.0} out of 5",
            InsightText = reviews.Count == 0
                ? "Share a clear story on your farm profile so neighbors know what you grow."
                : $"{verifiedCount} of {reviews.Count} reviews are verified purchases. Keep replying to build lasting trust.",
            Notice = Request.Query["notice"].FirstOrDefault(),
            Reviews = visibleReviews.Select(MapReview).ToList()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reply(int id, string? response, CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);
        var review = await Db.Reviews
            .Include(item => item.User)
            .Include(item => item.Product)
            .SingleOrDefaultAsync(item => item.Id == id
                && (item.FarmerProfileId == profile.Id || (item.ProductId.HasValue && item.Product != null && item.Product.FarmerProfileId == profile.Id)), cancellationToken);
        if (review is null)
        {
            return NotFound();
        }

        var trimmedResponse = response?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedResponse))
        {
            return RedirectToAction(nameof(Index), new { area = "Farmer", notice = "Add a short response before sending." });
        }

        if (trimmedResponse.Length > 1000)
        {
            return RedirectToAction(nameof(Index), new { area = "Farmer", notice = "Responses must be 1,000 characters or fewer." });
        }

        review.FarmerReply = trimmedResponse;
        review.FarmerRepliedAt = DateTime.UtcNow;
        review.UpdatedAt = DateTime.UtcNow;
        QueueNotification(review.UserId, NotificationType.Review, "Your farmer replied to a review", $"{profile.FarmName} replied to your review.", "/Customer");
        var productIds = await GetOwnedProductIdsAsync(profile, cancellationToken);
        var reviewRatings = await Db.Reviews
            .Where(item => item.FarmerProfileId == profile.Id || (item.ProductId.HasValue && productIds.Contains(item.ProductId.Value)))
            .Select(item => item.Rating)
            .ToListAsync(cancellationToken);
        profile.ReviewCount = reviewRatings.Count;
        profile.Rating = reviewRatings.Count == 0 ? 0 : Math.Round((decimal)reviewRatings.Average(), 1);
        profile.UpdatedAt = DateTime.UtcNow;
        await Db.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index), new { area = "Farmer", notice = "Your response has been published." });
    }

    private static FarmerReviewViewModel MapReview(Review review)
    {
        var customerName = $"{review.User?.FirstName ?? string.Empty} {review.User?.LastName ?? string.Empty}".Trim();
        if (string.IsNullOrWhiteSpace(customerName))
        {
            customerName = "MarketLink neighbor";
        }

        return new FarmerReviewViewModel
        {
            Id = review.Id,
            CustomerName = customerName,
            CustomerInitials = GetInitials(customerName),
            ProductName = review.Product?.Name ?? "Farm experience",
            Title = review.Title,
            Rating = review.Rating,
            Comment = review.Comment,
            DateLabel = FormatShortDate(review.CreatedAt),
            Response = review.FarmerReply,
            ReplyText = review.FarmerReply,
            VerifiedPurchase = review.VerifiedPurchase
        };
    }
}
