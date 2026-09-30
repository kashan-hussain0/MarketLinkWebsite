using System.Security.Claims;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Services;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Navigation;

public sealed class NavBarViewModel
{
    public bool IsSignedIn { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Initials { get; init; } = string.Empty;
    public string RoleName { get; init; } = string.Empty;
    public string DashboardUrl { get; init; } = "/";
    public string DashboardLabel { get; init; } = "Dashboard";
    public bool IsCustomer { get; init; }
    public int CartCount { get; init; }
    public int FavouriteCount { get; init; }
    public int UnreadNotificationCount { get; init; }
}

public sealed class NavBarService
{
    private readonly ApplicationDbContext db;
    private readonly ICartService cartService;

    public NavBarService(ApplicationDbContext db, ICartService cartService)
    {
        this.db = db;
        this.cartService = cartService;
    }

    public async Task<NavBarViewModel> BuildAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var cartCount = await ResolveCartCountAsync();

        var userId = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(userId))
        {
            return new NavBarViewModel { IsSignedIn = false, CartCount = cartCount };
        }

        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return new NavBarViewModel { IsSignedIn = false, CartCount = cartCount };
        }

        var roles = await db.UserRoles
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .Join(db.Roles.AsNoTracking(), item => item.RoleId, role => role.Id, (item, role) => role.Name)
            .ToListAsync(cancellationToken);

        var isAdmin = roles.Any(role => role == "Admin");
        var isFarmer = roles.Any(role => role == "Farmer");
        var isCustomer = roles.Any(role => role == "Customer");

        var dashboardUrl = isAdmin
            ? "/Admin/Dashboard"
            : isFarmer
                ? "/Farmer/Dashboard"
                : isCustomer
                    ? "/Customer/Dashboard"
                    : "/";

        var dashboardLabel = isAdmin
            ? "Admin console"
            : isFarmer
                ? "Farmer workspace"
                : isCustomer
                    ? "Customer dashboard"
                    : "My account";

        var favouriteCount = isCustomer
            ? await db.FavouriteProducts.CountAsync(item => item.UserId == userId, cancellationToken)
                + await db.FavouriteFarmers.CountAsync(item => item.UserId == userId, cancellationToken)
            : 0;

        var unreadCount = await db.Notifications
            .CountAsync(item => item.UserId == userId && !item.IsRead, cancellationToken);

        return new NavBarViewModel
        {
            IsSignedIn = true,
            DisplayName = $"{user.FirstName} {user.LastName}".Trim(),
            Initials = BuildInitials(user.FirstName, user.LastName),
            RoleName = dashboardLabel,
            DashboardUrl = dashboardUrl,
            DashboardLabel = dashboardLabel,
            IsCustomer = isCustomer,
            CartCount = cartCount,
            FavouriteCount = favouriteCount,
            UnreadNotificationCount = unreadCount
        };
    }

    private async Task<int> ResolveCartCountAsync()
    {
        try
        {
            return await cartService.GetItemCountAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string BuildInitials(string firstName, string lastName)
    {
        var initial = string.Concat(
            string.IsNullOrWhiteSpace(firstName) ? string.Empty : firstName[..1],
            string.IsNullOrWhiteSpace(lastName) ? string.Empty : lastName[..1]);

        return string.IsNullOrWhiteSpace(initial) ? "ML" : initial.ToUpperInvariant();
    }
}
