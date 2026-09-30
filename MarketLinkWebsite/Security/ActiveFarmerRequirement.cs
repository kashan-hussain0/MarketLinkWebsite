using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Security;

public sealed class ActiveFarmerRequirement : IAuthorizationRequirement
{
}

public sealed class ActiveFarmerRequirementHandler : AuthorizationHandler<ActiveFarmerRequirement>
{
    private readonly ApplicationDbContext db;

    public ActiveFarmerRequirementHandler(ApplicationDbContext db)
    {
        this.db = db;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveFarmerRequirement requirement)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var profile = await db.FarmerProfiles
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => new { item.Status, item.User.IsActive })
            .FirstOrDefaultAsync();

        if (profile is not null && profile.Status == FarmerStatus.Active && profile.IsActive)
        {
            context.Succeed(requirement);
        }
    }
}
