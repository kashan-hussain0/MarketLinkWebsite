using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Security.Claims;

namespace MarketLinkWebsite.Services;

public sealed class CatalogService : ICatalogService
{
    private readonly ApplicationDbContext db;
    private readonly IConfiguration configuration;
    private readonly CatalogDetailService detailService;
    private readonly GeoLocationService geo;
    private readonly MapService maps;
    private readonly IHttpContextAccessor httpContext;

    public CatalogService(
        ApplicationDbContext db,
        IConfiguration configuration,
        CatalogDetailService detailService,
        GeoLocationService geo,
        MapService maps,
        IHttpContextAccessor httpContext)
    {
        this.db = db;
        this.configuration = configuration;
        this.detailService = detailService;
        this.geo = geo;
        this.maps = maps;
        this.httpContext = httpContext;
    }

    public Task<ProductDetailViewModel?> GetProductDetailAsync(int id, string? viewerId, CancellationToken cancellationToken = default)
    {
        return detailService.GetProductDetailAsync(id, viewerId, cancellationToken);
    }

    public Task<CatalogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        return detailService.GetFilterOptionsAsync(cancellationToken);
    }

    public Task<IReadOnlyList<FarmSpotlightViewModel>> GetFarmSpotlightAsync(int take = 6, CancellationToken cancellationToken = default)
    {
        return detailService.GetFarmSpotlightAsync(take, cancellationToken);
    }

    public async Task<HomeViewModel> GetHomeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var productEntities = await ProductQuery()
                .Where(product => product.IsAvailable && product.Category.IsActive && product.FarmerProfile.Status == Models.Enums.FarmerStatus.Active && product.FarmerProfile.User.IsActive)
                .OrderBy(product => product.UpdatedAt)
                .Take(4)
                .ToListAsync(cancellationToken);
            var viewerId = (httpContext.HttpContext?.User as ClaimsPrincipal)?.FindFirstValue(ClaimTypes.NameIdentifier);
            var favoriteProductIds = viewerId is not null
                ? await db.FavouriteProducts.Where(item => item.UserId == viewerId).Select(item => item.ProductId).ToListAsync(cancellationToken)
                : new List<int>();
            var products = productEntities.Select(p => MapProduct(p, favoriteProductIds.Contains(p.Id))).ToList();
            var marketEntities = await MarketQuery()
                .Where(market => market.IsActive)
                .OrderByDescending(market => market.IsFeatured)
                .ThenBy(market => market.Name)
                .ToListAsync(cancellationToken);
            var markets = marketEntities.Select(m => MapMarket(m)).ToList();
            var categories = await GetCategoryTilesAsync(cancellationToken);

            if (products.Count > 0 || markets.Count > 0 || categories.Count > 0 || !UseDemoFallback)
            {
                return new HomeViewModel { FeaturedProducts = products, Markets = markets, Categories = categories };
            }
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
        }

        return UseDemoFallback ? DemoHome() : new HomeViewModel();
    }

    public async Task<List<ProductCardViewModel>> GetProductsAsync(string? searchQuery, string? category, string? marketLocation, string? marketDay, decimal? maxPrice, string? sort, CancellationToken cancellationToken = default)
    {
        try
        {
            IQueryable<Product> query = ProductQuery()
                .Where(product => product.IsAvailable && product.Category.IsActive && product.FarmerProfile.Status == Models.Enums.FarmerStatus.Active && product.FarmerProfile.User.IsActive);
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var term = searchQuery.Trim();
                query = query.Where(product => product.Name.Contains(term) || product.FarmerProfile.FarmName.Contains(term) || product.Category.Name.Contains(term));
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                query = query.Where(product => product.Category.Name == category);
            }

            if (!string.IsNullOrWhiteSpace(marketLocation))
            {
                // The matching market ids are resolved first. Filtering with a
                // nested Any() instead makes SQL Server walk the join table for
                // every product, which is very slow on a catalogue of any size.
                var marketIds = await db.Markets
                    .AsNoTracking()
                    .Where(market => market.IsActive && market.Name.Contains(marketLocation.Trim()))
                    .Select(market => market.Id)
                    .ToListAsync(cancellationToken);

                if (marketIds.Count == 0)
                {
                    return [];
                }

                query = query.Where(product =>
                    product.FarmerProfile.MarketFarmers.Any(link => marketIds.Contains(link.MarketId)));
            }

            if (!string.IsNullOrWhiteSpace(marketDay))
            {
                var day = marketDay.Trim();
                var marketIds = await db.Markets
                    .AsNoTracking()
                    .Where(market => market.IsActive && market.OperatingDays.Contains(day))
                    .Select(market => market.Id)
                    .ToListAsync(cancellationToken);

                if (marketIds.Count == 0)
                {
                    return [];
                }

                query = query.Where(product =>
                    product.FarmerProfile.MarketFarmers.Any(link => marketIds.Contains(link.MarketId)));
            }

            if (maxPrice is > 0)
            {
                query = query.Where(product => product.Price <= maxPrice.Value);
            }

            IQueryable<Product> ordered = sort switch
            {
                "price-low" => query.OrderBy(product => product.Price),
                "price-high" => query.OrderByDescending(product => product.Price),
                "rating" => query.OrderByDescending(product => product.FarmerProfile.Rating),
                _ => query.OrderByDescending(product => product.UpdatedAt)
            };

            var productEntities = await ordered.ToListAsync(cancellationToken);
            var viewerId = (httpContext.HttpContext?.User as ClaimsPrincipal)?.FindFirstValue(ClaimTypes.NameIdentifier);
            var favoriteProductIds = viewerId is not null
                ? await db.FavouriteProducts.Where(item => item.UserId == viewerId).Select(item => item.ProductId).ToListAsync(cancellationToken)
                : new List<int>();
            var products = productEntities.Select(p => MapProduct(p, favoriteProductIds.Contains(p.Id))).ToList();
            if (products.Count > 0 || !UseDemoFallback)
            {
                return products;
            }
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
        }

        return UseDemoFallback
            ? FilterDemoProducts(searchQuery, category, marketLocation, marketDay, maxPrice, sort)
            : [];
    }

    public async Task<ProductCardViewModel?> GetProductAsync(int id, CancellationToken cancellationToken = default)
    {
        var databaseAvailable = true;
        try
        {
            var product = await ProductQuery().FirstOrDefaultAsync(item => item.Id == id
                && item.IsAvailable
                && item.Category.IsActive
                && item.FarmerProfile.Status == Models.Enums.FarmerStatus.Active
                && item.FarmerProfile.User.IsActive,
                cancellationToken);
            if (product is not null)
            {
                var viewerId = (httpContext.HttpContext?.User as ClaimsPrincipal)?.FindFirstValue(ClaimTypes.NameIdentifier);
                var isFavorite = viewerId is not null && await db.FavouriteProducts.AnyAsync(item => item.UserId == viewerId && item.ProductId == id, cancellationToken);
                return MapProduct(product, isFavorite);
            }
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            databaseAvailable = false;
        }

        return !databaseAvailable && UseDemoFallback
            ? DemoCatalogData.Products.FirstOrDefault(item => item.Id == id)
            : null;
    }

    public async Task<List<MarketCardViewModel>> GetMarketsAsync(string? searchQuery, string? marketDay, string? sort = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = MarketQuery().Where(market => market.IsActive);
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                var term = searchQuery.Trim();
                query = query.Where(market => market.Name.Contains(term) || market.Address.Contains(term) || market.City.Contains(term));
            }

            if (!string.IsNullOrWhiteSpace(marketDay))
            {
                query = query.Where(market => market.OperatingDays.Contains(marketDay));
            }

            var marketEntities = await query.OrderBy(market => market.Name).ToListAsync(cancellationToken);
            var viewerId = (httpContext.HttpContext?.User as ClaimsPrincipal)?.FindFirstValue(ClaimTypes.NameIdentifier);
            var favoriteMarketIds = viewerId is not null
                ? await db.FavouriteMarkets.Where(item => item.UserId == viewerId).Select(item => item.MarketId).ToListAsync(cancellationToken)
                : new List<int>();
            var markets = marketEntities.Select(m => MapMarket(m, favoriteMarketIds.Contains(m.Id))).ToList();

            // Once the browser has shared a location the shopper can ask for the
            // closest markets first, which is what "near me" really means.
            if (string.Equals(sort, "distance", StringComparison.OrdinalIgnoreCase) && markets.Any(m => m.HasDistance))
            {
                markets = markets
                    .OrderBy(m => m.HasDistance ? 0 : 1)
                    .ThenBy(m => m.DistanceKm)
                    .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            else if (string.Equals(sort, "farmers", StringComparison.OrdinalIgnoreCase))
            {
                markets = markets
                    .OrderByDescending(m => m.FarmerCount)
                    .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            if (markets.Count > 0 || !UseDemoFallback)
            {
                return markets;
            }
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
        }

        return UseDemoFallback ? FilterDemoMarkets(searchQuery, marketDay) : [];
    }

    public async Task<MarketCardViewModel?> GetMarketAsync(int id, CancellationToken cancellationToken = default)
    {
        var databaseAvailable = true;
        try
        {
            var market = await MarketQuery().FirstOrDefaultAsync(item => item.Id == id && item.IsActive, cancellationToken);
            if (market is not null)
            {
                return MapMarket(market);
            }
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            databaseAvailable = false;
        }

        return !databaseAvailable && UseDemoFallback
            ? DemoCatalogData.Markets.FirstOrDefault(item => item.Id == id)
            : null;
    }

    private static string BuildProductSummary(IEnumerable<Product> products)
    {
        var live = products.Where(product => product.IsAvailable).ToList();

        if (live.Count == 0)
        {
            return "No live listings this week";
        }

        var names = live
            .OrderBy(product => product.Name)
            .Select(product => product.Name)
            .ToList();

        var shown = names.Take(4).ToList();
        var summary = string.Join(", ", shown);

        return names.Count > shown.Count
            ? $"{summary} +{names.Count - shown.Count} more"
            : summary;
    }

    public async Task<IReadOnlyList<MarketFarmerCardViewModel>> GetMarketFarmersAsync(int marketId, CancellationToken cancellationToken = default)
    {
        try
        {
            var market = await MarketQuery().FirstOrDefaultAsync(item => item.Id == marketId && item.IsActive, cancellationToken);
            if (market is null)
            {
                return [];
            }

            return market.MarketFarmers
                .OrderBy(link => link.StallNumber)
                .Select(link => new MarketFarmerCardViewModel
                {
                    Id = link.FarmerProfileId,
                    Name = $"{link.FarmerProfile.User.FirstName} {link.FarmerProfile.User.LastName}".Trim(),
                    FarmName = link.FarmerProfile.FarmName,
                    Specialty = link.FarmerProfile.Description,
                    Description = link.FarmerProfile.Description,
                    ProductSummary = BuildProductSummary(link.FarmerProfile.Products),
                    LiveProductCount = link.FarmerProfile.Products.Count(product => product.IsAvailable),
                    ImageUrl = link.FarmerProfile.User.ProfilePictureUrl,
                    StallNumber = link.StallNumber,
                    Rating = link.FarmerProfile.Rating,
                    ProductCount = link.FarmerProfile.Products.Count
                })
                .ToList();
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return [];
        }
    }

    private static bool IsDatabaseFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException)
            {
                return true;
            }
        }

        return false;
    }

    private IQueryable<Product> ProductQuery()
    {
        return db.Products
            .AsNoTracking()
            .AsSplitQuery()
            .Include(product => product.Category)
            .Include(product => product.Inventory)
            .Include(product => product.FarmerProfile)
                .ThenInclude(profile => profile.User)
            .Include(product => product.FarmerProfile)
                .ThenInclude(profile => profile.MarketFarmers)
                    .ThenInclude(link => link.Market);
    }

    private IQueryable<Market> MarketQuery()
    {
        return db.Markets
            .AsNoTracking()
            .AsSplitQuery()
            .Include(market => market.MarketFarmers)
                .ThenInclude(link => link.FarmerProfile)
                    .ThenInclude(profile => profile.Products)
            .Include(market => market.MarketFarmers)
                .ThenInclude(link => link.FarmerProfile)
                    .ThenInclude(profile => profile.User);
    }

    private bool UseDemoFallback => configuration.GetValue("Catalog:UseDemoFallback", true);

    private ProductCardViewModel MapProduct(Product product, bool isFavorite = false)
    {
        geo.Read();
        var market = product.FarmerProfile.MarketFarmers.FirstOrDefault()?.Market;
        return new ProductCardViewModel
        {
            Id = product.Id,
            Name = product.Name,
            ShortDescription = product.Description,
            Description = product.Description,
            Category = product.Category.Name,
            Price = product.Price,
            Unit = product.Unit.ToString(),
            QuantityAvailable = product.Inventory?.QuantityAvailable ?? 0,
            ImageUrl = product.ImageUrl,
            FarmerName = $"{product.FarmerProfile.User.FirstName} {product.FarmerProfile.User.LastName}",
            FarmName = product.FarmerProfile.FarmName,
            FarmerProfileId = product.FarmerProfileId,
            MarketName = market?.Name ?? string.Empty,
            MarketDay = market?.OperatingDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty,
            DistanceKm = market is null ? 0 : geo.DistanceTo((double)market.Latitude, (double)market.Longitude),
            HasDistance = market is not null && geo.HasLocation,
            Rating = (double)product.FarmerProfile.Rating,
            ReviewCount = product.FarmerProfile.ReviewCount,
            IsOrganic = product.IsOrganic,
            IsFavorite = isFavorite,
            Badge = product.Inventory is { QuantityAvailable: 0 } ? "Sold out" : null
        };
    }

    private MarketCardViewModel MapMarket(Market market, bool isFavorite = false)
    {
        geo.Read();
        var farmerCount = market.MarketFarmers.Count;
        var productCount = market.MarketFarmers.SelectMany(link => link.FarmerProfile.Products).Select(product => product.Id).Distinct().Count();
        var embedUrl = string.Format(
            CultureInfo.InvariantCulture,
            "https://www.openstreetmap.org/export/embed.html?bbox={0:F6}%2C{1:F6}%2C{2:F6}%2C{3:F6}&layer=mapnik&marker={4:F6}%2C{5:F6}",
            market.Longitude - 0.02m,
            market.Latitude - 0.01m,
            market.Longitude + 0.02m,
            market.Latitude + 0.01m,
            market.Latitude,
            market.Longitude);
        return new MarketCardViewModel
        {
            Id = market.Id,
            Name = market.Name,
            Description = market.Description,
            Address = market.Address,
            City = market.City,
            Day = market.OperatingDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? market.OperatingDays,
            OpenTime = market.OpenTime,
            CloseTime = market.CloseTime,
            FarmerCount = farmerCount,
            ProductCount = productCount,
            FarmNames = market.MarketFarmers
                .Select(link => link.FarmerProfile.FarmName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            DistanceKm = geo.DistanceTo((double)market.Latitude, (double)market.Longitude),
            HasDistance = geo.HasLocation,
            Latitude = (double)market.Latitude,
            Longitude = (double)market.Longitude,
            IsOpenThisWeek = market.IsActive,
            ImageUrl = market.ImageUrl,
            EmbedUrl = embedUrl,
            ViewUrl = maps.ViewUrl(market.Latitude, market.Longitude),
            DirectionsUrl = maps.DirectionsUrl(market.Latitude, market.Longitude),
            MapProviderLabel = maps.ProviderLabel,
            IsFavorite = isFavorite
        };
    }

    private static HomeViewModel DemoHome()
    {
        return new HomeViewModel
        {
            FeaturedProducts = DemoCatalogData.Products.Take(4).ToList(),
            Markets = DemoCatalogData.Markets,
            Categories = DemoCategories()
        };
    }

    private async Task<List<CategoryTileViewModel>> GetCategoryTilesAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Categories
            .AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => new
            {
                category.Id,
                category.Name,
                category.Description,
                category.ImageUrl,
                ProductCount = category.Products.Count(product => product.IsAvailable
                    && product.FarmerProfile.Status == Models.Enums.FarmerStatus.Active
                    && product.FarmerProfile.User.IsActive)
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row => new CategoryTileViewModel
            {
                Id = row.Id,
                Name = row.Name,
                Description = row.Description,
                ImageUrl = row.ImageUrl,
                IconClass = CategoryIcon(row.Name),
                ProductCount = row.ProductCount
            })
            .ToList();
    }

    private static readonly string[] FallbackCategoryNames =
    [
        "Vegetables",
        "Fruits",
        "Dairy",
        "Baked goods"
    ];

    private static List<CategoryTileViewModel> DemoCategories() =>
        FallbackCategoryNames
            .Select(name => new CategoryTileViewModel
            {
                Name = name,
                Description = string.Empty,
                IconClass = CategoryIcon(name),
                ProductCount = DemoCatalogData.Products.Count(product =>
                    product.Category.Equals(name, StringComparison.OrdinalIgnoreCase))
            })
            .ToList();

    public static string CategoryIcon(string? name)
    {
        var value = (name ?? string.Empty).Trim().ToLowerInvariant();

        if (value.Contains("leaf") || value.Contains("green") || value.Contains("salad")) return "bi-flower3";
        if (value.Contains("herb") || value.Contains("flower")) return "bi-flower2";
        if (value.Contains("dairy") || value.Contains("milk") || value.Contains("cheese") || value.Contains("yoghurt") || value.Contains("yogurt")) return "bi-cup-straw";
        if (value.Contains("egg")) return "bi-egg";
        if (value.Contains("bakery") || value.Contains("bread") || value.Contains("cake") || value.Contains("pastry")) return "bi-cake2";
        if (value.Contains("meat") || value.Contains("poultry") || value.Contains("chicken") || value.Contains("beef") || value.Contains("lamb")) return "bi-egg-fried";
        if (value.Contains("dry") || value.Contains("nut") || value.Contains("rice") || value.Contains("pulse")) return "bi-basket2-fill";
        if (value.Contains("pantry") || value.Contains("oil") || value.Contains("spice") || value.Contains("honey") || value.Contains("jam")) return "bi-basket-fill";
        if (value.Contains("fruit") || value.Contains("apple") || value.Contains("berry") || value.Contains("orange") || value.Contains("melon")) return "bi-apple";
        if (value.Contains("vegetable") || value.Contains("veg") || value.Contains("root")) return "bi-flower1";
        if (value.Contains("fish") || value.Contains("seafood")) return "bi-water";
        if (value.Contains("coffee") || value.Contains("tea")) return "bi-cup-hot";
        if (value.Contains("snack") || value.Contains("sweet") || value.Contains("chocolate")) return "bi-cup-straw";
        if (value.Contains("beverage") || value.Contains("drink") || value.Contains("juice")) return "bi-cup-straw";

        return "bi-basket2";
    }

    private static List<ProductCardViewModel> FilterDemoProducts(string? searchQuery, string? category, string? marketLocation, string? marketDay, decimal? maxPrice, string? sort)
    {
        var products = DemoCatalogData.Products.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            products = products.Where(product => product.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) || product.FarmName.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) || product.Category.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            products = products.Where(product => product.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(marketLocation))
        {
            products = products.Where(product => product.MarketName.Contains(marketLocation, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(marketDay))
        {
            products = products.Where(product => product.MarketDay.Equals(marketDay, StringComparison.OrdinalIgnoreCase));
        }

        if (maxPrice is > 0)
        {
            products = products.Where(product => product.Price <= maxPrice.Value);
        }

        return sort switch
        {
            "price-low" => products.OrderBy(product => product.Price).ToList(),
            "price-high" => products.OrderByDescending(product => product.Price).ToList(),
            "rating" => products.OrderByDescending(product => product.Rating).ToList(),
            _ => products.ToList()
        };
    }

    private static List<MarketCardViewModel> FilterDemoMarkets(string? searchQuery, string? marketDay)
    {
        var markets = DemoCatalogData.Markets.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            markets = markets.Where(market => market.Name.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) || market.Address.Contains(searchQuery, StringComparison.OrdinalIgnoreCase) || market.City.Contains(searchQuery, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(marketDay))
        {
            markets = markets.Where(market => market.Day.Equals(marketDay, StringComparison.OrdinalIgnoreCase));
        }

        return markets.ToList();
    }
}
