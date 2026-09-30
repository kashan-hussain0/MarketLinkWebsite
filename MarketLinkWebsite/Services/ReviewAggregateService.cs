using MarketLinkWebsite.Data;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Services;

public sealed class ReviewAggregateService
{
    private readonly ApplicationDbContext db;

    public ReviewAggregateService(ApplicationDbContext db)
    {
        this.db = db;
    }

    public async Task UpdateForProductAsync(int productId, CancellationToken cancellationToken = default)
    {
        var farmerId = await db.Products
            .AsNoTracking()
            .Where(product => product.Id == productId)
            .Select(product => (int?)product.FarmerProfileId)
            .FirstOrDefaultAsync(cancellationToken);
        if (farmerId.HasValue)
        {
            await UpdateForFarmerAsync(farmerId.Value, cancellationToken);
        }
    }

    public async Task UpdateForFarmerAsync(int farmerId, CancellationToken cancellationToken = default)
    {
        var profile = await db.FarmerProfiles.FirstOrDefaultAsync(item => item.Id == farmerId, cancellationToken);
        if (profile is null)
        {
            return;
        }

        var ratings = await db.Reviews
            .AsNoTracking()
            .Where(review => review.FarmerProfileId == farmerId
                || (review.ProductId.HasValue && review.Product!.FarmerProfileId == farmerId))
            .Select(review => review.Rating)
            .ToListAsync(cancellationToken);
        profile.Rating = ratings.Count == 0 ? 0m : ratings.Average(rating => (decimal)rating);
        profile.ReviewCount = ratings.Count;
        profile.UpdatedAt = DateTime.UtcNow;
    }
}
