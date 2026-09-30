using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace MarketLinkWebsite.Controllers;

[Authorize(Policy = "CustomerAccess")]
public sealed class FavoriteController : Controller
{
    private readonly ApplicationDbContext db;

    public FavoriteController(ApplicationDbContext db)
    {
        this.db = db;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id, string kind, string? returnUrl, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId) || id <= 0)
        {
            return RedirectToAction("Dashboard", "Customer");
        }

        if (!await db.Users.AnyAsync(user => user.Id == userId && user.IsActive, cancellationToken))
        {
            return Forbid();
        }

        if (kind.Equals("farmer", StringComparison.OrdinalIgnoreCase))
        {
            var farmerExists = await db.FarmerProfiles.AnyAsync(item => item.Id == id, cancellationToken);
            if (!farmerExists)
            {
                return RedirectToAction("Dashboard", "Customer");
            }

            var favourite = await db.FavouriteFarmers.FindAsync(new object[] { userId, id }, cancellationToken);
            if (favourite is null)
            {
                db.FavouriteFarmers.Add(new FavouriteFarmer { UserId = userId, FarmerProfileId = id });
            }
            else
            {
                db.FavouriteFarmers.Remove(favourite);
            }
        }
        else if (kind.Equals("market", StringComparison.OrdinalIgnoreCase))
        {
            var marketExists = await db.Markets.AnyAsync(item => item.Id == id && item.IsActive, cancellationToken);
            if (!marketExists)
            {
                return RedirectToAction("Dashboard", "Customer");
            }

            var favourite = await db.FavouriteMarkets.FindAsync(new object[] { userId, id }, cancellationToken);
            if (favourite is null)
            {
                db.FavouriteMarkets.Add(new FavouriteMarket { UserId = userId, MarketId = id });
            }
            else
            {
                db.FavouriteMarkets.Remove(favourite);
            }
        }
        else
        {
            var productExists = await db.Products.AnyAsync(item => item.Id == id, cancellationToken);
            if (!productExists)
            {
                return RedirectToAction("Dashboard", "Customer");
            }

            var favourite = await db.FavouriteProducts.FindAsync(new object[] { userId, id }, cancellationToken);
            if (favourite is null)
            {
                db.FavouriteProducts.Add(new FavouriteProduct { UserId = userId, ProductId = id });
            }
            else
            {
                db.FavouriteProducts.Remove(favourite);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Favorites", "Customer");
    }
}
