using System.Globalization;
using System.Security.Claims;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.ViewModels;
using MarketLinkWebsite.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Controllers;

public sealed class FarmController : Controller
{
    private readonly ApplicationDbContext db;
    private readonly MapService maps;

    public FarmController(ApplicationDbContext db, MapService maps)
    {
        this.db = db;
        this.maps = maps;
    }

    [HttpGet]
    [Route("Farm")]
    [Route("Farm/Index")]
    [Route("Farm/{id:int}")]
    public async Task<IActionResult> Index(int id, CancellationToken cancellationToken)
    {
        var profile = await db.FarmerProfiles
            .AsNoTracking()
            // MarketFarmers and Products are both collections, so a single query
            // would multiply the rows and drag far more data back than needed.
            .AsSplitQuery()
            .Include(item => item.User)
            .Include(item => item.MarketFarmers).ThenInclude(link => link.Market)
            .Include(item => item.Products).ThenInclude(product => product.Category)
            .Include(item => item.Products).ThenInclude(product => product.Inventory)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);

        if (profile is null || profile.Status != Models.Enums.FarmerStatus.Active || !profile.User.IsActive)
        {
            return NotFound();
        }

        var productIds = profile.Products.Select(product => product.Id).ToList();

        var productRatings = await db.Reviews
            .AsNoTracking()
            .Where(review => review.ProductId != null && productIds.Contains(review.ProductId.Value))
            .GroupBy(review => review.ProductId!.Value)
            .Select(group => new { ProductId = group.Key, Average = group.Average(review => (decimal)review.Rating), Count = group.Count() })
            .ToDictionaryAsync(row => row.ProductId, cancellationToken);

        var reviews = await db.Reviews
            .AsNoTracking()
            .Include(review => review.User)
            .Where(review =>
                review.FarmerProfileId == profile.Id ||
                (review.ProductId != null && productIds.Contains(review.ProductId.Value)))
            .OrderByDescending(review => review.CreatedAt)
            .Take(8)
            .ToListAsync(cancellationToken);

        var viewerId = (User as ClaimsPrincipal)?.FindFirstValue(ClaimTypes.NameIdentifier);
        var isCustomer = viewerId is not null && User.Identity?.IsAuthenticated == true;

        var favoriteProductIds = isCustomer
            ? await db.FavouriteProducts
                .Where(item => item.UserId == viewerId && productIds.Contains(item.ProductId))
                .Select(item => item.ProductId)
                .ToListAsync(cancellationToken)
            : new List<int>();

        var isFavoriteFarmer = isCustomer
            && await db.FavouriteFarmers.AnyAsync(item => item.UserId == viewerId && item.FarmerProfileId == profile.Id, cancellationToken);

        var model = new FarmProfileViewModel
        {
            Id = profile.Id,
            FarmName = profile.FarmName,
            Description = profile.Description,
            City = profile.City,
            Address = profile.Address,
            OperatingDays = profile.OperatingDays,
            PickupWindows = profile.PickupWindows,
            OrderCutoffHours = profile.OrderCutoffHours,
            Latitude = profile.Latitude,
            Longitude = profile.Longitude,
            Rating = profile.Rating,
            ReviewCount = profile.ReviewCount,
            ImageUrl = profile.User.ProfilePictureUrl,
            Initials = BuildInitials(profile.FarmName),
            MemberSince = profile.CreatedAt.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-US")),
            Markets = profile.MarketFarmers
                .Where(link => link.Market.IsActive)
                .Select(link => link.Market.Name)
                .OrderBy(name => name)
                .ToList(),
            Stalls = profile.MarketFarmers
                .Where(link => link.Market.IsActive)
                .OrderBy(link => link.Market.Name)
                .Select(link => new FarmStallViewModel
                {
                    MarketId = link.MarketId,
                    MarketName = link.Market.Name,
                    StallNumber = link.StallNumber ?? string.Empty,
                    MarketDay = link.Market.OperatingDays,
                    Address = link.Market.Address,
                    City = link.Market.City,
                    OpenTime = link.Market.OpenTime,
                    CloseTime = link.Market.CloseTime,
                    MapUrl = maps.ViewUrl(link.Market.Latitude, link.Market.Longitude),
                    DirectionsUrl = maps.DirectionsUrl(link.Market.Latitude, link.Market.Longitude),
                    HasCoordinates = link.Market.Latitude != 0 && link.Market.Longitude != 0,
                    Latitude = (double)link.Market.Latitude,
                    Longitude = (double)link.Market.Longitude
                })
                .ToList(),
            MapEmbedUrl = profile.Latitude != 0 && profile.Longitude != 0
                ? maps.EmbedUrlFor(profile.Latitude, profile.Longitude, 14)
                : string.Empty,
            MapViewUrl = maps.ViewUrl(profile.Latitude, profile.Longitude),
            DirectionsUrl = maps.DirectionsUrl(profile.Latitude, profile.Longitude),
            MapProviderLabel = maps.ProviderLabel,
            Products = profile.Products
                .Where(product => product.IsAvailable)
                .OrderBy(product => product.Name)
                .Select(product =>
                {
                    var stats = productRatings.GetValueOrDefault(product.Id);
                    return new FarmProductViewModel
                    {
                        Id = product.Id,
                        Name = product.Name,
                        ShortDescription = Truncate(product.Description, 90),
                        Price = product.Price.ToString("C"),
                        Unit = UnitLabel(product.Unit),
                        ImageUrl = product.ImageUrl,
                        Category = product.Category?.Name ?? "Uncategorised",
                        IsOrganic = product.IsOrganic,
                        QuantityAvailable = product.Inventory?.QuantityAvailable ?? 0,
                        Rating = stats?.Average ?? 0m,
                        ReviewCount = stats?.Count ?? 0,
                        IsFavorite = favoriteProductIds.Contains(product.Id)
                    };
                })
                .ToList(),
            Reviews = reviews.Select(review => new FarmReviewViewModel
            {
                ReviewerName = $"{review.User.FirstName} {review.User.LastName}".Trim(),
                Initials = BuildInitials($"{review.User.FirstName} {review.User.LastName}"),
                Rating = review.Rating,
                Title = review.Title,
                Body = review.Comment,
                DateLabel = review.CreatedAt.ToString("d MMM yyyy", CultureInfo.GetCultureInfo("en-US")),
                FarmerReply = review.FarmerReply,
                VerifiedPurchase = review.VerifiedPurchase
            }).ToList(),
            IsFavorite = isFavoriteFarmer
        };

        ViewData["Title"] = model.FarmName;
        return View(model);
    }

    private static string BuildInitials(string value)
    {
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return "ML";
        }

        if (words.Length == 1)
        {
            return words[0][..1].ToUpperInvariant();
        }

        return string.Concat(words[0][..1], words[1][..1]).ToUpperInvariant();
    }

    private static string Truncate(string value, int length)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= length ? value : string.Concat(value.AsSpan(0, length).TrimEnd(), "...");
    }

    private static string UnitLabel(Models.Enums.UnitType unit) => unit switch
    {
        Models.Enums.UnitType.Kg => "kg",
        Models.Enums.UnitType.Gram => "gram",
        Models.Enums.UnitType.Litre => "litre",
        Models.Enums.UnitType.Piece => "piece",
        Models.Enums.UnitType.Dozen => "dozen",
        _ => "bundle"
    };
}
