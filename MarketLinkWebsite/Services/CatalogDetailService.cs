using System.Globalization;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using MarketLinkWebsite.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Services;

public sealed class CatalogDetailService
{
    private readonly ApplicationDbContext db;

    public CatalogDetailService(ApplicationDbContext db)
    {
        this.db = db;
    }

    public async Task<ProductDetailViewModel?> GetProductDetailAsync(int id, string? viewerId, CancellationToken cancellationToken = default)
    {
        var product = await db.Products
            .AsNoTracking()
            .Include(item => item.Category)
            .Include(item => item.Inventory)
            .Include(item => item.FarmerProfile).ThenInclude(profile => profile.User)
            .Include(item => item.FarmerProfile).ThenInclude(profile => profile.MarketFarmers).ThenInclude(link => link.Market)
            .FirstOrDefaultAsync(item => item.Id == id && item.IsAvailable, cancellationToken);

        if (product is null || !product.Category.IsActive)
        {
            return null;
        }

        if (product.FarmerProfile.Status != FarmerStatus.Active || !product.FarmerProfile.User.IsActive)
        {
            return null;
        }

        var market = product.FarmerProfile.MarketFarmers
            .Where(link => link.Market.IsActive)
            .Select(link => link.Market)
            .FirstOrDefault();

        var reviews = await db.Reviews
            .AsNoTracking()
            .Include(review => review.User)
            .Where(review => review.ProductId == id)
            .OrderByDescending(review => review.HelpfulCount)
            .ThenByDescending(review => review.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);

        var distribution = new int[6];
        foreach (var review in reviews)
        {
            if (review.Rating is >= 1 and <= 5)
            {
                distribution[review.Rating]++;
            }
        }

        var hasCompletedPurchase = !string.IsNullOrWhiteSpace(viewerId)
            && await db.OrderItems.AnyAsync(item => item.ProductId == id
                && item.Order.UserId == viewerId
                && item.Order.Status == OrderStatus.Completed, cancellationToken);

        var isSignedIn = !string.IsNullOrWhiteSpace(viewerId);

        var alreadyReviewed = !string.IsNullOrWhiteSpace(viewerId)
            && await db.Reviews.AnyAsync(review => review.UserId == viewerId && review.ProductId == id, cancellationToken);

        var isFavorite = !string.IsNullOrWhiteSpace(viewerId)
            && await db.FavouriteProducts.AnyAsync(item => item.UserId == viewerId && item.ProductId == id, cancellationToken);

        var farmerProductIds = await db.Products
            .AsNoTracking()
            .Where(item => item.FarmerProfileId == product.FarmerProfileId)
            .Select(item => item.Id)
            .ToListAsync(cancellationToken);

        var farmerStats = await db.Reviews
            .AsNoTracking()
            .Where(review => review.FarmerProfileId == product.FarmerProfileId
                || (review.ProductId != null && farmerProductIds.Contains(review.ProductId.Value)))
            .GroupBy(review => review.Rating)
            .Select(group => new { Rating = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var farmerTotal = farmerStats.Sum(row => row.Count);

        return new ProductDetailViewModel
        {
            Id = product.Id,
            Name = product.Name,
            ShortDescription = Shorten(product.Description, 110),
            Description = product.Description,
            Category = product.Category.Name,
            Price = product.Price,
            Unit = product.Unit.ToString().ToLowerInvariant(),
            QuantityAvailable = product.Inventory?.QuantityAvailable ?? 0,
            ImageUrl = product.ImageUrl,
            FarmerName = $"{product.FarmerProfile.User.FirstName} {product.FarmerProfile.User.LastName}".Trim(),
            FarmName = product.FarmerProfile.FarmName,
            FarmerProfileId = product.FarmerProfileId,
            MarketName = market?.Name ?? string.Empty,
            MarketDay = market?.OperatingDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty,
            DistanceKm = 0,
            Rating = reviews.Count == 0 ? 0 : Math.Round((double)reviews.Average(review => review.Rating), 1),
            ReviewCount = reviews.Count,
            IsOrganic = product.IsOrganic,
            IsFavorite = isFavorite,
            Badge = BuildBadge(product, product.Inventory?.QuantityAvailable ?? 0),
            CanReview = isSignedIn && !alreadyReviewed,
            HasReviewed = alreadyReviewed,
            StarDistribution = distribution,
            FarmerRating = farmerTotal == 0
                ? 0
                : Math.Round(farmerStats.Sum(row => row.Rating * row.Count) / (double)farmerTotal, 1),
            FarmerReviewCount = farmerTotal,
            FarmImageUrl = FarmImage(product.FarmerProfile.User.ProfilePictureUrl, await db.Products
                .Where(item => item.FarmerProfileId == product.FarmerProfileId && item.IsAvailable && item.ImageUrl != string.Empty)
                .OrderByDescending(item => item.UpdatedAt)
                .Select(item => item.ImageUrl)
                .FirstOrDefaultAsync(cancellationToken)),
            FarmInitials = InitialsOf(product.FarmerProfile.FarmName),
            FarmCity = product.FarmerProfile.City,
            FarmAddress = product.FarmerProfile.Address,
            FarmOperatingDays = product.FarmerProfile.OperatingDays,
            FarmPickupWindows = product.FarmerProfile.PickupWindows,
            FarmLatitude = (double)product.FarmerProfile.Latitude,
            FarmLongitude = (double)product.FarmerProfile.Longitude,
            FarmMapUrl = $"https://www.openstreetmap.org/?mlat={product.FarmerProfile.Latitude}&mlon={product.FarmerProfile.Longitude}#map=15/{product.FarmerProfile.Latitude}/{product.FarmerProfile.Longitude}",
            FarmProductCount = await db.Products.CountAsync(item => item.FarmerProfileId == product.FarmerProfileId && item.IsAvailable, cancellationToken),
            FarmReviewTotal = await db.Reviews.CountAsync(review => review.FarmerProfileId == product.FarmerProfileId
                || (review.ProductId.HasValue && review.Product!.FarmerProfileId == product.FarmerProfileId), cancellationToken),
            MarketAddress = market?.Address ?? string.Empty,
            MarketOpenTime = market?.OpenTime ?? string.Empty,
            MarketCloseTime = market?.CloseTime ?? string.Empty,
            MarketMapUrl = market is null
                ? string.Empty
                : $"https://www.openstreetmap.org/?mlat={market.Latitude}&mlon={market.Longitude}#map=16/{market.Latitude}/{market.Longitude}",
            MarketLatitude = market is null ? 0d : (double)market.Latitude,
            MarketLongitude = market is null ? 0d : (double)market.Longitude,
            MarketDirectionsUrl = market is null
                ? string.Empty
                : $"https://www.openstreetmap.org/directions?to={market.Latitude}%2C{market.Longitude}",
            FarmDirectionsUrl = $"https://www.openstreetmap.org/directions?to={product.FarmerProfile.Latitude}%2C{product.FarmerProfile.Longitude}",
            Reviews = reviews.Select(review => new ProductReviewViewModel
            {
                Id = review.Id,
                ReviewerName = $"{review.User.FirstName} {review.User.LastName}".Trim(),
                Initials = InitialsOf($"{review.User.FirstName} {review.User.LastName}"),
                Rating = review.Rating,
                Title = review.Title,
                Body = review.Comment,
                DateLabel = review.CreatedAt.ToLocalTime().ToString("d MMM yyyy", CultureInfo.GetCultureInfo("en-US")),
                HelpfulCount = review.HelpfulCount,
                VerifiedPurchase = review.VerifiedPurchase,
                IsMine = review.UserId == viewerId,
                FarmerReply = review.FarmerReply
            }).ToList()
        };
    }

    public async Task<CatalogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        var categories = await db.Categories
            .AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => category.Name)
            .ToListAsync(cancellationToken);

        var markets = await db.Markets
            .AsNoTracking()
            .Where(market => market.IsActive)
            .OrderBy(market => market.Name)
            .Select(market => market.Name)
            .ToListAsync(cancellationToken);

        var days = markets
            .SelectMany(name => db.Markets
                .Where(market => market.Name == name)
                .Select(market => market.OperatingDays))
            .ToList();

        var marketDays = days
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(day => DayOrder(day))
            .ThenBy(day => day, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var maxPrice = await db.Products
            .AsNoTracking()
            .Where(product => product.IsAvailable)
            .Select(product => (decimal?)product.Price)
            .MaxAsync(cancellationToken) ?? 20m;

        var nextMarket = await db.Markets
            .AsNoTracking()
            .Where(market => market.IsActive)
            .OrderBy(market => market.Name)
            .FirstOrDefaultAsync(cancellationToken);

        return new CatalogFilterOptions
        {
            Categories = categories,
            Markets = markets,
            MarketDays = marketDays,
            MaxCatalogPrice = Math.Round(maxPrice, 2),
            NextMarketLabel = nextMarket is null ? "Check the market calendar" : $"{nextMarket.Name} · {nextMarket.OperatingDays}"
        };
    }

    public async Task<IReadOnlyList<FarmSpotlightViewModel>> GetFarmSpotlightAsync(int take = 6, CancellationToken cancellationToken = default)
    {
        var farms = await db.FarmerProfiles
            .AsNoTracking()
            .AsSplitQuery()
            .Where(profile => profile.Status == FarmerStatus.Active && profile.User.IsActive)
            .Include(profile => profile.User)
            .Include(profile => profile.MarketFarmers).ThenInclude(link => link.Market)
            .Include(profile => profile.Products).ThenInclude(product => product.Inventory)
            .OrderByDescending(profile => profile.Rating)
            .ThenBy(profile => profile.FarmName)
            .Take(take)
            .ToListAsync(cancellationToken);

        return farms.Select(profile =>
        {
            var live = profile.Products
                .Where(product => product.IsAvailable && (product.Inventory?.QuantityAvailable ?? 0) > 0)
                .OrderBy(product => product.Name)
                .ToList();

            return new FarmSpotlightViewModel
            {
                Id = profile.Id,
                FarmName = profile.FarmName,
                OwnerName = $"{profile.User.FirstName} {profile.User.LastName}".Trim(),
                City = profile.City,
                Description = profile.Description,
                ImageUrl = profile.User.ProfilePictureUrl,
                Initials = InitialsOf(profile.FarmName),
                Rating = profile.Rating,
                ReviewCount = profile.ReviewCount,
                ProductCount = live.Count,
                ProductNames = live.Count == 0
                    ? "No live listings this week"
                    : string.Join(", ", live.Take(3).Select(product => product.Name)) + (live.Count > 3 ? $" +{live.Count - 3} more" : string.Empty),
                Markets = string.Join(", ", profile.MarketFarmers
                    .Where(link => link.Market.IsActive)
                    .Select(link => link.Market.Name)
                    .OrderBy(name => name)
                    .Take(2))
            };
        }).ToList();
    }

    private static string FarmImage(string? profilePicture, string? fallbackProductImage)
    {
        if (!string.IsNullOrWhiteSpace(profilePicture))
        {
            return profilePicture.Trim();
        }

        return string.IsNullOrWhiteSpace(fallbackProductImage) ? string.Empty : fallbackProductImage.Trim();
    }

    private static string? BuildBadge(Product product, int stock)
    {
        if (stock <= 0)
        {
            return "Sold out";
        }

        if (stock <= 10)
        {
            return "Limited harvest";
        }

        return product.IsOrganic ? "Organic pick" : "In season";
    }

    private static string Shorten(string value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= length ? value : string.Concat(value.AsSpan(0, length).TrimEnd(), "...");
    }

    private static string InitialsOf(string value)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return "ML";
        }

        return words.Length == 1
            ? words[0][..1].ToUpperInvariant()
            : string.Concat(words[0][..1], words[1][..1]).ToUpperInvariant();
    }

    private static int DayOrder(string day) => day.ToLowerInvariant() switch
    {
        "monday" => 1,
        "tuesday" => 2,
        "wednesday" => 3,
        "thursday" => 4,
        "friday" => 5,
        "saturday" => 6,
        "sunday" => 7,
        _ => 8
    };
}
