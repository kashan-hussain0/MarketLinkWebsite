using MarketLinkWebsite.Models.ViewModels;

namespace MarketLinkWebsite.Services;

public interface ICatalogService
{
    Task<HomeViewModel> GetHomeAsync(CancellationToken cancellationToken = default);
    Task<List<ProductCardViewModel>> GetProductsAsync(string? searchQuery, string? category, string? marketLocation, string? marketDay, decimal? maxPrice, string? sort, CancellationToken cancellationToken = default);
    Task<ProductCardViewModel?> GetProductAsync(int id, CancellationToken cancellationToken = default);
    Task<ProductDetailViewModel?> GetProductDetailAsync(int id, string? viewerId, CancellationToken cancellationToken = default);
    Task<CatalogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FarmSpotlightViewModel>> GetFarmSpotlightAsync(int take = 6, CancellationToken cancellationToken = default);
    Task<List<MarketCardViewModel>> GetMarketsAsync(string? searchQuery, string? marketDay, string? sort = null, CancellationToken cancellationToken = default);
    Task<MarketCardViewModel?> GetMarketAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MarketFarmerCardViewModel>> GetMarketFarmersAsync(int marketId, CancellationToken cancellationToken = default);
}
