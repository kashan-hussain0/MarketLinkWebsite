using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Services;

public sealed class DatabaseSeeder : IDatabaseSeeder
{
    private readonly ApplicationDbContext db;
    private readonly RoleManager<IdentityRole> roleManager;
    private readonly UserManager<ApplicationUser> userManager;
    private readonly IConfiguration configuration;
    private readonly ILogger<DatabaseSeeder> logger; 
    private readonly MarketplaceDataSeeder marketplaceSeeder;

    public DatabaseSeeder(
        ApplicationDbContext db,
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ILogger<DatabaseSeeder> logger,
        MarketplaceDataSeeder marketplaceSeeder)
    {
        this.db = db;
        this.roleManager = roleManager;
        this.userManager = userManager;
        this.configuration = configuration;
        this.logger = logger;
        this.marketplaceSeeder = marketplaceSeeder;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedRolesAsync();
        await SeedCategoriesAsync(cancellationToken);
        await SeedMarketsAsync(cancellationToken);
        await SeedAdminAsync(cancellationToken);
        await marketplaceSeeder.SeedAsync(cancellationToken);
    }

    private async Task SeedRolesAsync()
    {
        foreach (var role in new[] { "Admin", "Farmer", "Customer" })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    private async Task SeedCategoriesAsync(CancellationToken cancellationToken)
    {
        var categories = new[]
        {
            new Category { Name = "Vegetables", Description = "Field vegetables grown in open ground.", SortOrder = 1 },
            new Category { Name = "Fruits", Description = "Seasonal fruit from nearby orchards.", SortOrder = 2 },
            new Category { Name = "Dairy", Description = "Milk, cheese, yoghurt and farm-fresh eggs.", SortOrder = 3 },
            new Category { Name = "Baked goods", Description = "Bread and baked goods made in small batches.", SortOrder = 4 }
        };

        foreach (var category in categories)
        {
            if (!await db.Categories.AnyAsync(item => item.Name == category.Name, cancellationToken))
            {
                db.Categories.Add(category);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedMarketsAsync(CancellationToken cancellationToken)
    {
        var markets = new[]
        {
            new Market
            {
                Name = "Downtown Farmers Market",
                Description = "The city's main weekend market with the widest range of stalls.",
                Address = "Central Square, Main Avenue",
                City = "Downtown",
                OperatingDays = "Saturday",
                OpenTime = "07:00",
                CloseTime = "13:00",
                Latitude = 33.6844m,
                Longitude = 73.0479m,
                IsFeatured = true
            },
            new Market
            {
                Name = "Community Green Square",
                Description = "A relaxed neighbourhood market focused on certified organic product.",
                Address = "East Park Boulevard",
                City = "Riverside",
                OperatingDays = "Sunday",
                OpenTime = "08:00",
                CloseTime = "14:00",
                Latitude = 33.7032m,
                Longitude = 73.0658m,
                IsFeatured = true
            },
            new Market
            {
                Name = "Riverside Growers Market",
                Description = "Mid-week pickup for dairy, honey and pantry goods.",
                Address = "Canal Walk, Warehouse District",
                City = "Riverside",
                OperatingDays = "Wednesday",
                OpenTime = "15:00",
                CloseTime = "19:00",
                Latitude = 33.6621m,
                Longitude = 73.0614m
            },
            new Market
            {
                Name = "Harbor District Market",
                Description = "An evening market beside the docks, popular for meat and bread.",
                Address = "Pier 4, Harbor Street",
                City = "Downtown",
                OperatingDays = "Friday",
                OpenTime = "14:00",
                CloseTime = "18:00",
                Latitude = 33.6901m,
                Longitude = 73.0523m
            },
            new Market
            {
                Name = "Old Mill Saturday Market",
                Description = "A small family-run market beside the restored grain mill.",
                Address = "Old Mill Road, Meadow End",
                City = "Northbridge",
                OperatingDays = "Saturday",
                OpenTime = "09:00",
                CloseTime = "15:00",
                Latitude = 33.7155m,
                Longitude = 73.0402m
            }
        };

        foreach (var market in markets)
        {
            if (!await db.Markets.AnyAsync(item => item.Name == market.Name, cancellationToken))
            {
                db.Markets.Add(market);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedAdminAsync(CancellationToken cancellationToken)
    {
        var email = configuration["Seed:AdminEmail"];
        var password = configuration["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Seed:AdminEmail and Seed:AdminPassword are required, so no administrator account was created.");
            return;
        }

        var admin = await userManager.FindByEmailAsync(email);
        if (admin is not null)
        {
            return;
        }

        admin = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = "MarketLink",
            LastName = "Administrator",
            IsActive = true
        };

        var result = await userManager.CreateAsync(admin, password);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(admin, "Admin");
            logger.LogInformation("Created the administrator account {Email}.", email);
            return;
        }

        logger.LogWarning("Could not create the administrator account {Email}: {Errors}", email, string.Join(" ", result.Errors.Select(error => error.Description)));
    }
}
