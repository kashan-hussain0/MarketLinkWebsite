namespace MarketLinkWebsite.Services;

public interface IDatabaseSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
