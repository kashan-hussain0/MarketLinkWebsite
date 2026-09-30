using MarketLinkWebsite.Models.ViewModels;

namespace MarketLinkWebsite.Services;

public interface ICartService
{
    Task<CartViewModel> GetCartAsync(CancellationToken cancellationToken = default);
    Task<int> GetItemCountAsync(CancellationToken cancellationToken = default);
    Task AddAsync(int productId, int quantity, CancellationToken cancellationToken = default);
    Task UpdateAsync(int productId, int quantity, CancellationToken cancellationToken = default);
    Task RemoveAsync(int productId, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    Task<CheckoutResult> CheckoutAsync(string userId, CheckoutFormViewModel model, CancellationToken cancellationToken = default);
}

public sealed record CheckoutResult(int OrderId, string OrderNumber);
