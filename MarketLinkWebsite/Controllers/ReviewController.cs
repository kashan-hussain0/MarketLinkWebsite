using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using MarketLinkWebsite.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarketLinkWebsite.Validation;

namespace MarketLinkWebsite.Controllers
{
    public sealed class ReviewController : Controller
    {
        private readonly ApplicationDbContext db;
        private readonly SignInManager<ApplicationUser> signInManager;
        private readonly ReviewAggregateService reviewAggregates;

        public ReviewController(
            ApplicationDbContext db,
            SignInManager<ApplicationUser> signInManager,
            ReviewAggregateService reviewAggregates)
        {
            this.db = db;
            this.signInManager = signInManager;
            this.reviewAggregates = reviewAggregates;
        }

        [HttpGet]
        public async Task<IActionResult> Index(int? productId = null, CancellationToken cancellationToken = default)
        {
            var product = productId.HasValue
                ? await db.Products
                    .AsNoTracking()
                    .Include(item => item.FarmerProfile)
                    .Include(item => item.Category)
                    .Include(item => item.Reviews)
                    .ThenInclude(review => review.User)
                    .FirstOrDefaultAsync(item => item.Id == productId, cancellationToken)
                : await db.Products
                    .AsNoTracking()
                    .Where(item => item.IsAvailable)
                    .OrderBy(item => item.Id)
                    .Include(item => item.FarmerProfile)
                    .Include(item => item.Category)
                    .Include(item => item.Reviews)
                    .ThenInclude(review => review.User)
                    .FirstOrDefaultAsync(cancellationToken);

            if (product is null)
            {
                return NotFound();
            }

            var reviews = product.Reviews
                .OrderByDescending(review => review.CreatedAt)
                .ToList();
            var totalReviews = reviews.Count;
            var model = new ReviewListViewModel
            {
                ProductId = product.Id,
                ProductName = product.Name,
                ProductFarm = product.FarmerProfile.FarmName,
                ProductImageUrl = product.ImageUrl,
                ProductCategory = product.Category?.Name ?? "Local harvest",
                AverageRating = totalReviews == 0 ? 0m : reviews.Average(review => (decimal)review.Rating),
                TotalReviews = totalReviews,
                RecommendedPercent = totalReviews == 0
                    ? 0
                    : (int)Math.Round(reviews.Count(review => review.Rating >= 4) * 100m / totalReviews, MidpointRounding.AwayFromZero),
                RatingBreakdown = Enumerable.Range(5, 5)
                    .Select(stars =>
                    {
                        var count = reviews.Count(review => review.Rating == stars);
                        return new ReviewRatingBreakdown
                        {
                            Stars = stars,
                            Count = count,
                            Percent = totalReviews == 0 ? 0 : (int)Math.Round(count * 100m / totalReviews, MidpointRounding.AwayFromZero)
                        };
                    })
                    .ToList(),
                Reviews = reviews.Select(review => new ReviewItemViewModel
                {
                    Id = review.Id,
                    ReviewerName = $"{review.User.FirstName} {review.User.LastName}".Trim(),
                    Initials = GetInitials(review.User.FirstName, review.User.LastName),
                    Rating = review.Rating,
                    Title = review.Title,
                    Body = review.Comment,
                    CreatedLabel = CreatedLabel(review.CreatedAt),
                    VerifiedPurchase = review.VerifiedPurchase,
                    HelpfulCount = review.HelpfulCount
                }).ToList()
            };

            ViewData["Title"] = "Community reviews";
            return View(model);
        }

        [Authorize(Policy = "CustomerAccess")]
        [HttpGet]
        public async Task<IActionResult> Create(int? productId = null, CancellationToken cancellationToken = default)
        {
            var customer = await GetActiveUserAsync(cancellationToken);
            if (customer is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            ViewData["Title"] = "Share a review";
            return View(await BuildSubmissionAsync(customer, productId, cancellationToken));
        }

        [Authorize(Policy = "CustomerAccess")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ReviewSubmission model, CancellationToken cancellationToken)
        {
            var customer = await GetActiveUserAsync(cancellationToken);
            if (customer is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            model ??= new ReviewSubmission();
            if (!ModelState.IsValid)
            {
                ViewData["Title"] = "Share a review";
                ViewData["ErrorMessage"] = "Please complete the required fields and choose a rating.";
                var invalidModel = await BuildSubmissionAsync(customer, model.ProductId, cancellationToken);
                invalidModel.Rating = model.Rating;
                invalidModel.Title = model.Title;
                invalidModel.Body = model.Body;
                return View(invalidModel);
            }

            var eligibleProduct = await db.Products
                .AsNoTracking()
                .Where(product => product.Id == model.ProductId
                    && product.IsAvailable
                    && product.FarmerProfile.Status == FarmerStatus.Active
                    && product.FarmerProfile.User.IsActive)
                .FirstOrDefaultAsync(cancellationToken);

            if (eligibleProduct is null)
            {
                ModelState.AddModelError(nameof(model.ProductId), "This item is no longer available to review.");
            }

            var purchasedThisItem = await db.OrderItems
                .AsNoTracking()
                .AnyAsync(item => item.ProductId == model.ProductId
                    && item.Order.UserId == customer.Id
                    && item.Order.Status == OrderStatus.Completed, cancellationToken);

            if (!purchasedThisItem)
            {
                ModelState.AddModelError(nameof(model.ProductId), "You can only review items you have ordered and collected.");
            }

            var existingReview = await db.Reviews
                .AnyAsync(review => review.UserId == customer.Id && review.ProductId == model.ProductId, cancellationToken);
            if (existingReview)
            {
                ModelState.AddModelError(nameof(model.ProductId), "You have already reviewed this item.");
            }

            if (!ModelState.IsValid)
            {
                ViewData["Title"] = "Share a review";
                ViewData["ErrorMessage"] = "This item is not eligible for a new review.";
                var invalidModel = await BuildSubmissionAsync(customer, model.ProductId, cancellationToken);
                invalidModel.Rating = model.Rating;
                invalidModel.Title = model.Title;
                invalidModel.Body = model.Body;
                return View(invalidModel);
            }

            var review = new Review
            {
                UserId = customer.Id,
                ProductId = model.ProductId,
                Rating = model.Rating,
                Title = model.Title.Trim(),
                Comment = model.Body.Trim(),
                VerifiedPurchase = purchasedThisItem,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                db.Reviews.Add(review);
                await db.SaveChangesAsync(cancellationToken);
                await reviewAggregates.UpdateForProductAsync(model.ProductId, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                ModelState.AddModelError(nameof(model.ProductId), "This review could not be saved. Refresh the eligible item list and try again.");
                ViewData["Title"] = "Share a review";
                ViewData["ErrorMessage"] = "This review could not be saved. Refresh the eligible item list and try again.";
                return View(await BuildSubmissionAsync(customer, model.ProductId, cancellationToken));
            }

            TempData["SuccessMessage"] = "Thanks for helping your neighbors shop with confidence.";
            return RedirectToAction(nameof(Index), new { productId = model.ProductId });
        }

        [HttpGet]
        public async Task<IActionResult> CreateForFarmer(int farmerId, CancellationToken cancellationToken = default)
        {
            var customer = await GetActiveUserAsync(cancellationToken);
            if (customer is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            ViewData["Title"] = "Rate this farm";
            return View(await BuildFarmerSubmissionAsync(customer, farmerId, cancellationToken));
        }

        [Authorize(Policy = "CustomerAccess")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateForFarmer(FarmerReviewSubmission model, CancellationToken cancellationToken)
        {
            var customer = await GetActiveUserAsync(cancellationToken);
            if (customer is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            model ??= new FarmerReviewSubmission();
            ViewData["Title"] = "Rate this farm";

            if (!ModelState.IsValid)
            {
                ViewData["ErrorMessage"] = "Please choose a rating and write at least 10 characters.";
                var invalid = await BuildFarmerSubmissionAsync(customer, model.FarmerProfileId, cancellationToken);
                invalid.Rating = model.Rating;
                invalid.Title = model.Title;
                invalid.Body = model.Body;
                return View(invalid);
            }

            var farm = await db.FarmerProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(profile => profile.Id == model.FarmerProfileId, cancellationToken);

            if (farm is null || farm.Status != FarmerStatus.Active || !farm.User.IsActive)
            {
                ModelState.AddModelError(nameof(model.FarmerProfileId), "This farm is not accepting reviews right now.");
            }

            var purchased = await db.OrderItems
                .AsNoTracking()
                .Where(item => item.FarmerName == farm!.FarmName
                    && item.Order.UserId == customer.Id
                    && item.Order.Status == OrderStatus.Completed)
                .AnyAsync(cancellationToken);

            if (!purchased)
            {
                ModelState.AddModelError(nameof(model.FarmerProfileId), "You can only review farms you have ordered from and collected.");
            }

            if (farm is not null)
            {
                var alreadyReviewed = await db.Reviews.AnyAsync(
                    review => review.UserId == customer.Id && review.FarmerProfileId == farm.Id,
                    cancellationToken);

                if (alreadyReviewed)
                {
                    ModelState.AddModelError(nameof(model.FarmerProfileId), "You have already reviewed this farm.");
                }
            }

            if (!ModelState.IsValid)
            {
                ViewData["ErrorMessage"] = "This farm cannot be reviewed right now.";
                var invalid = await BuildFarmerSubmissionAsync(customer, model.FarmerProfileId, cancellationToken);
                invalid.Rating = model.Rating;
                invalid.Title = model.Title;
                invalid.Body = model.Body;
                return View(invalid);
            }

            db.Reviews.Add(new Review
            {
                UserId = customer.Id,
                FarmerProfileId = model.FarmerProfileId,
                Rating = model.Rating,
                Title = model.Title.Trim(),
                Comment = model.Body.Trim(),
                VerifiedPurchase = purchased,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken);
            await reviewAggregates.UpdateForFarmerAsync(model.FarmerProfileId, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            db.Notifications.Add(new Notification
            {
                UserId = farm!.UserId,
                Type = NotificationType.Review,
                Title = "New farm review",
                Message = $"{customer.FirstName} {customer.LastName} rated {farm.FarmName} {model.Rating} out of 5.",
                ActionUrl = "/Farmer/Review",
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken);

            TempData["SuccessMessage"] = "Thanks! Your review helps other shoppers choose with confidence.";
            return RedirectToAction("Index", "Farm", new { id = model.FarmerProfileId });
        }

        private async Task<FarmerReviewSubmission> BuildFarmerSubmissionAsync(ApplicationUser customer, int farmerId, CancellationToken cancellationToken)
        {
            var farm = await db.FarmerProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(profile => profile.Id == farmerId, cancellationToken);

            var purchasedFarmNames = await db.OrderItems
                .AsNoTracking()
                .Where(item => item.Order.UserId == customer.Id
                    && item.Order.Status == OrderStatus.Completed)
                .Select(item => item.FarmerName)
                .Distinct()
                .ToListAsync(cancellationToken);

            var options = await db.FarmerProfiles
                .AsNoTracking()
                .Where(profile => profile.Status == FarmerStatus.Active && profile.User.IsActive
                    && purchasedFarmNames.Contains(profile.FarmName))
                .OrderByDescending(profile => profile.Rating)
                .ThenBy(profile => profile.FarmName)
                .Select(profile => new FarmerReviewOption
                {
                    Id = profile.Id,
                    FarmName = profile.FarmName,
                    City = profile.City
                })
                .ToListAsync(cancellationToken);

            var alreadyReviewed = await db.Reviews
                .AsNoTracking()
                .Where(review => review.UserId == customer.Id && review.FarmerProfileId != null)
                .Select(review => review.FarmerProfileId!.Value)
                .ToListAsync(cancellationToken);

            foreach (var option in options)
            {
                option.CanReview = !alreadyReviewed.Contains(option.Id);
            }

            var selected = options.FirstOrDefault(option => option.Id == farmerId && option.CanReview)
                ?? options.FirstOrDefault(option => option.CanReview);

            return new FarmerReviewSubmission
            {
                FarmerProfileId = selected?.Id ?? (farmerId > 0 ? farmerId : 0),
                FarmName = selected?.FarmName ?? farm?.FarmName ?? string.Empty,
                Options = options
            };
        }

        [Authorize(Policy = "CustomerAccess")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkHelpful(int id, int? productId = null, CancellationToken cancellationToken = default)
        {
            var customer = await GetActiveUserAsync(cancellationToken);
            if (customer is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            if (id <= 0)
            {
                TempData["ErrorMessage"] = "Choose a valid review.";
                return RedirectToAction(nameof(Index), new { productId });
            }

            var review = await db.Reviews.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (review is null)
            {
                TempData["ErrorMessage"] = "That review could not be found.";
                return RedirectToAction(nameof(Index), new { productId });
            }

            if (review.UserId == customer.Id)
            {
                TempData["ErrorMessage"] = "You cannot mark your own review as helpful.";
                return RedirectToAction(nameof(Index), new { productId = review.ProductId });
            }

            var alreadyVoted = await db.ReviewHelpfulVotes
                .AnyAsync(vote => vote.ReviewId == id && vote.UserId == customer.Id, cancellationToken);
            if (alreadyVoted)
            {
                TempData["ErrorMessage"] = "You have already marked this review as helpful.";
                return RedirectToAction(nameof(Index), new { productId = review.ProductId });
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                db.ReviewHelpfulVotes.Add(new ReviewHelpfulVote
                {
                    ReviewId = review.Id,
                    UserId = customer.Id,
                    CreatedAt = DateTime.UtcNow
                });
                review.HelpfulCount += 1;
                review.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(cancellationToken);
                TempData["ErrorMessage"] = "You have already marked this review as helpful.";
                return RedirectToAction(nameof(Index), new { productId = review.ProductId });
            }

            TempData["SuccessMessage"] = "Thanks for your feedback.";
            return RedirectToAction(nameof(Index), new { productId = review.ProductId });
        }

        [Authorize(Policy = "AdminAccess")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteReview(int id, int? productId = null, CancellationToken cancellationToken = default)
        {
            var administrator = await GetActiveUserAsync(cancellationToken);
            if (administrator is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            if (id <= 0)
            {
                TempData["ErrorMessage"] = "Choose a valid review.";
                return RedirectToAction(nameof(Index), new { productId });
            }

            var review = await db.Reviews.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
            if (review is null)
            {
                TempData["ErrorMessage"] = "That review has already been removed.";
                return RedirectToAction(nameof(Index), new { productId });
            }

            var targetProductId = review.ProductId;
            var targetFarmerId = review.FarmerProfileId;
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            db.Reviews.Remove(review);
            await db.SaveChangesAsync(cancellationToken);
            if (targetProductId.HasValue)
            {
                await reviewAggregates.UpdateForProductAsync(targetProductId.Value, cancellationToken);
            }
            else if (targetFarmerId.HasValue)
            {
                await reviewAggregates.UpdateForFarmerAsync(targetFarmerId.Value, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            TempData["SuccessMessage"] = "The review was removed by moderation.";
            return RedirectToAction(nameof(Index), new { productId = targetProductId ?? productId });
        }

        private async Task<ReviewSubmission> BuildSubmissionAsync(ApplicationUser customer, int? requestedProductId, CancellationToken cancellationToken)
        {
            var purchasedIds = await db.OrderItems
                .AsNoTracking()
                .Where(item => item.ProductId.HasValue
                    && item.Order.UserId == customer.Id
                    && item.Order.Status == OrderStatus.Completed)
                .Select(item => item.ProductId!.Value)
                .Distinct()
                .ToListAsync(cancellationToken);
            var products = await db.Products
                .AsNoTracking()
                .Where(product => product.IsAvailable
                    && product.Category.IsActive
                    && product.FarmerProfile.Status == FarmerStatus.Active
                    && product.FarmerProfile.User.IsActive
                    && purchasedIds.Contains(product.Id))
                .OrderBy(product => product.Name)
                .Select(product => new ReviewProductOption
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    FarmName = product.FarmerProfile.FarmName
                })
                .ToListAsync(cancellationToken);

            var reviewedIds = await db.Reviews
                .AsNoTracking()
                .Where(review => review.UserId == customer.Id && review.ProductId.HasValue)
                .Select(review => review.ProductId!.Value)
                .ToListAsync(cancellationToken);

            var eligibleProducts = products
                .Where(product => !reviewedIds.Contains(product.ProductId))
                .ToList();
            var selectedProduct = eligibleProducts.FirstOrDefault(product => product.ProductId == requestedProductId)
                ?? eligibleProducts.FirstOrDefault();

            return new ReviewSubmission
            {
                ProductId = selectedProduct?.ProductId ?? requestedProductId.GetValueOrDefault(),
                ProductName = selectedProduct?.ProductName,
                FarmName = selectedProduct?.FarmName,
                ReviewerName = $"{customer.FirstName} {customer.LastName}".Trim(),
                Email = customer.Email,
                VerifiedPurchase = true,
                EligibleProducts = eligibleProducts,
                EligibilityMessage = eligibleProducts.Count == 0
                    ? "You do not have any unreviewed items from completed orders yet."
                    : "Only items from your completed orders are shown."
            };
        }

        private async Task<ApplicationUser?> GetActiveUserAsync(CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                await signInManager.SignOutAsync();
                return null;
            }

            var account = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);
            if (account is null || !account.IsActive)
            {
                await signInManager.SignOutAsync();
                TempData["ErrorMessage"] = "Your account is inactive. Contact MarketLink support for help.";
                return null;
            }

            return account;
        }

        private static string GetInitials(string firstName, string lastName)
        {
            var first = string.IsNullOrWhiteSpace(firstName) ? string.Empty : firstName.Trim()[0].ToString();
            var last = string.IsNullOrWhiteSpace(lastName) ? string.Empty : lastName.Trim()[0].ToString();
            return $"{first}{last}".ToUpperInvariant();
        }

        private static string CreatedLabel(DateTime createdAt)
        {
            var local = createdAt.ToLocalTime();
            if (local.Date == DateTime.Today)
            {
                return $"Reviewed today at {local:h:mm tt}";
            }

            if (local.Date == DateTime.Today.AddDays(-1))
            {
                return "Reviewed yesterday";
            }

            return $"Reviewed {local:MMM d, yyyy}";
        }
    }

    public class ReviewListViewModel
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string ProductFarm { get; set; } = string.Empty;
        public string ProductImageUrl { get; set; } = string.Empty;
        public string ProductCategory { get; set; } = string.Empty;
        public decimal AverageRating { get; set; }
        public int TotalReviews { get; set; }
        public int RecommendedPercent { get; set; }
        public List<ReviewRatingBreakdown> RatingBreakdown { get; set; } = new();
        public List<ReviewItemViewModel> Reviews { get; set; } = new();
    }

    public class ReviewRatingBreakdown
    {
        public int Stars { get; set; }
        public int Count { get; set; }
        public int Percent { get; set; }
    }

    public class ReviewItemViewModel
    {
        public int Id { get; set; }
        public string ReviewerName { get; set; } = string.Empty;
        public string Initials { get; set; } = string.Empty;
        public int Rating { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string CreatedLabel { get; set; } = string.Empty;
        public bool VerifiedPurchase { get; set; }
        public int HelpfulCount { get; set; }
    }

    public class ReviewSubmission
    {
        [Required(ErrorMessage = "Choose the item you are reviewing.")]
        [Range(1, int.MaxValue, ErrorMessage = "Choose the item you are reviewing.")]
        [Display(Name = "Item")]
        public int ProductId { get; set; }

        [Range(1, 5, ErrorMessage = "Choose a rating from 1 to 5 stars.")]
        [Display(Name = "Your rating")]
        public int Rating { get; set; }

        [Required(ErrorMessage = "Tell other shoppers about your experience.")]
        [StringLength(1000, MinimumLength = 10, ErrorMessage = "Your review must be at least 10 characters.")]
        [Display(Name = "Review")]
        public string Body { get; set; } = string.Empty;

        [StringLength(100, ErrorMessage = "Review title must be 100 characters or fewer.")]
        [Display(Name = "Review title (optional)")]
        public string Title { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public string ReviewerName { get; set; } = string.Empty;

        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public string? Email { get; set; }

        [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
        public bool VerifiedPurchase { get; set; }
        public List<ReviewProductOption> EligibleProducts { get; set; } = new();
        public string? ProductName { get; set; }
        public string? FarmName { get; set; }
        public string EligibilityMessage { get; set; } = string.Empty;
    }

    public class ReviewProductOption
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public string FarmName { get; set; } = string.Empty;
    }
}
