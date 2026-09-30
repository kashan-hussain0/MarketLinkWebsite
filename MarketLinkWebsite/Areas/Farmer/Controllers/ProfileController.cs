using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Areas.Farmer.Controllers;

[Area("Farmer")]
[Authorize(Policy = "ActiveFarmerAccess")]
public sealed class ProfileController : FarmerControllerBase
{
    public ProfileController(ApplicationDbContext db) : base(db)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);
        return View(await BuildViewModelAsync(profile, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(FarmerProfileViewModel model, CancellationToken cancellationToken = default)
    {
        model ??= new FarmerProfileViewModel();
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);
        Normalize(model);
        if (!ModelState.IsValid)
        {
            await PopulateMetadataAsync(model, profile, cancellationToken);
            return View("Index", model);
        }

        var normalizedEmail = model.Email.ToUpperInvariant();
        var emailOwner = await Db.Users
            .AnyAsync(user => user.Id != profile.UserId && user.Email != null && user.Email.ToUpper() == normalizedEmail, cancellationToken);
        if (emailOwner)
        {
            ModelState.AddModelError(nameof(model.Email), "That email address is already in use.");
            await PopulateMetadataAsync(model, profile, cancellationToken);
            return View("Index", model);
        }

        string? uploadedImageUrl;
        try
        {
            uploadedImageUrl = await SaveImageAsync(model.FarmImageFile, "farmer-profiles", cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(nameof(model.FarmImageFile), exception.Message);
            await PopulateMetadataAsync(model, profile, cancellationToken);
            return View("Index", model);
        }

        var nameParts = model.OwnerName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var firstName = nameParts.Length > 0 ? nameParts[0] : profile.User?.FirstName ?? string.Empty;
        var lastName = nameParts.Length > 1 ? string.Join(' ', nameParts.Skip(1)) : string.Empty;
        if (profile.User is not null)
        {
            profile.User.FirstName = firstName;
            profile.User.LastName = lastName;
            profile.User.Email = model.Email;
            profile.User.PhoneNumber = model.Phone;
            if (uploadedImageUrl is not null)
            {
                profile.User.ProfilePictureUrl = uploadedImageUrl;
            }
        }

        profile.FarmName = model.FarmName;
        profile.Description = model.Bio;
        profile.Address = model.Address;
        profile.City = model.City;
        profile.OperatingDays = model.OperatingDays;
        profile.PickupWindows = model.PickupWindows;
        profile.OrderCutoffHours = model.OrderCutoffHours;
        var coordinates = ResolveCoordinates(model, profile);
        profile.Latitude = coordinates.Latitude;
        profile.Longitude = coordinates.Longitude;
        profile.UpdatedAt = DateTime.UtcNow;
        await SaveMarketsAsync(profile, model.MarketIds, cancellationToken);
        await Db.SaveChangesAsync(cancellationToken);

        QueueNotification(profile.UserId, NotificationType.Account, "Farm profile updated", "Your farm profile and pickup preferences are up to date.", "/Farmer/Profile");
        await Db.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index), new { area = "Farmer", notice = "Your farm profile has been updated." });
    }

    private async Task SaveMarketsAsync(FarmerProfile profile, List<int> marketIds, CancellationToken cancellationToken)
    {
        var validIds = await Db.Markets
            .Where(market => market.IsActive && marketIds.Contains(market.Id))
            .Select(market => market.Id)
            .ToListAsync(cancellationToken);

        var existing = await Db.MarketFarmers
            .Where(link => link.FarmerProfileId == profile.Id)
            .ToListAsync(cancellationToken);

        var existingByMarket = existing.ToDictionary(link => link.MarketId);

        foreach (var marketId in validIds.Except(existingByMarket.Keys))
        {
            Db.MarketFarmers.Add(new MarketFarmer
            {
                FarmerProfileId = profile.Id,
                MarketId = marketId,
                JoinedDate = DateTime.UtcNow
            });
        }

        foreach (var marketId in existingByMarket.Keys.Except(validIds))
        {
            Db.MarketFarmers.Remove(existingByMarket[marketId]);
        }

        await Db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<FarmerMarketOption>> LoadMarketOptionsAsync(int profileId, CancellationToken cancellationToken)
    {
        var selected = await Db.MarketFarmers
            .AsNoTracking()
            .Where(link => link.FarmerProfileId == profileId)
            .Select(link => link.MarketId)
            .ToListAsync(cancellationToken);

        return await Db.Markets
            .AsNoTracking()
            .Where(market => market.IsActive)
            .OrderBy(market => market.Name)
            .Select(market => new FarmerMarketOption
                {
                    Id = market.Id,
                    Name = market.Name,
                    City = market.City,
                    OperatingDays = market.OperatingDays,
                    OpenTime = market.OpenTime,
                    CloseTime = market.CloseTime,
                    IsSelected = selected.Contains(market.Id),
                    Latitude = market.Latitude,
                    Longitude = market.Longitude
                })
            .ToListAsync(cancellationToken);
    }

    private async Task<FarmerProfileViewModel> BuildViewModelAsync(FarmerProfile profile, CancellationToken cancellationToken)
    {
        var model = new FarmerProfileViewModel
        {
            OwnerName = GetOwnerName(profile),
            FarmName = profile.FarmName,
            Email = profile.User?.Email ?? string.Empty,
            Phone = profile.User?.PhoneNumber ?? string.Empty,
            Address = profile.Address,
            City = profile.City,
            Bio = profile.Description,
            OperatingDays = profile.OperatingDays,
            PickupWindows = profile.PickupWindows,
            OrderCutoffHours = profile.OrderCutoffHours,
            Latitude = profile.Latitude,
            Longitude = profile.Longitude,
            Markets = await LoadMarketOptionsAsync(profile.Id, cancellationToken),
            FarmImageUrl = string.IsNullOrWhiteSpace(profile.User?.ProfilePictureUrl) ? FarmFallbackImageUrl : profile.User.ProfilePictureUrl,
            MemberSince = profile.CreatedAt.ToLocalTime().ToString("MMMM yyyy"),
            VerificationStatus = profile.Status == FarmerStatus.Active ? "Verified farmer" : profile.Status.ToString(),
            OwnerInitials = GetInitials(GetOwnerName(profile))
        };
        await PopulateMetadataAsync(model, profile, cancellationToken);
        return model;
    }

    private async Task PopulateMetadataAsync(FarmerProfileViewModel model, FarmerProfile profile, CancellationToken cancellationToken)
    {
        var productIds = await GetOwnedProductIdsAsync(profile, cancellationToken);
        var reviews = await Db.Reviews
            .AsNoTracking()
            .Where(review => review.FarmerProfileId == profile.Id || (review.ProductId.HasValue && productIds.Contains(review.ProductId.Value)))
            .ToListAsync(cancellationToken);
        model.Rating = reviews.Count == 0 ? 0 : Math.Round((decimal)reviews.Average(review => review.Rating), 1);
        model.ReviewCount = reviews.Count;
        model.ListingCount = productIds.Count;
        model.Markets = await LoadMarketOptionsAsync(profile.Id, cancellationToken);
        model.FollowerCount = await Db.FavouriteFarmers.CountAsync(favourite => favourite.FarmerProfileId == profile.Id, cancellationToken);
        model.ProfileStrength = CalculateProfileStrength(model);
        if (string.IsNullOrWhiteSpace(model.FarmImageUrl))
        {
            model.FarmImageUrl = FarmFallbackImageUrl;
        }

        if (string.IsNullOrWhiteSpace(model.VerificationStatus))
        {
            model.VerificationStatus = profile.Status == FarmerStatus.Active ? "Verified farmer" : profile.Status.ToString();
        }
    }

    private static int CalculateProfileStrength(FarmerProfileViewModel model)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(model.FarmName)) score += 15;
        if (!string.IsNullOrWhiteSpace(model.Bio)) score += 20;
        if (!string.IsNullOrWhiteSpace(model.Address)) score += 15;
        if (!string.IsNullOrWhiteSpace(model.City)) score += 10;
        if (!string.IsNullOrWhiteSpace(model.OperatingDays)) score += 10;
        if (!string.IsNullOrWhiteSpace(model.PickupWindows)) score += 10;
        if (!string.IsNullOrWhiteSpace(model.Phone)) score += 10;
        if (!string.IsNullOrWhiteSpace(model.FarmImageUrl) && model.FarmImageUrl != FarmFallbackImageUrl) score += 10;
        return Math.Clamp(score, 0, 100);
    }

    private static void Normalize(FarmerProfileViewModel model)
    {
        model.OwnerName = (model.OwnerName ?? string.Empty).Trim();
        model.FarmName = (model.FarmName ?? string.Empty).Trim();
        model.Email = (model.Email ?? string.Empty).Trim();
        model.Phone = (model.Phone ?? string.Empty).Trim();
        model.Address = (model.Address ?? string.Empty).Trim();
        model.City = (model.City ?? string.Empty).Trim();
        model.Bio = (model.Bio ?? string.Empty).Trim();
        model.OperatingDays = (model.OperatingDays ?? string.Empty).Trim();
        model.PickupWindows = (model.PickupWindows ?? string.Empty).Trim();
    }

    private static (decimal Latitude, decimal Longitude) ResolveCoordinates(FarmerProfileViewModel model, FarmerProfile profile)
    {
        var hasLatitude = model.Latitude != 0m;
        var hasLongitude = model.Longitude != 0m;

        if (!hasLatitude || !hasLongitude)
        {
            return (profile.Latitude, profile.Longitude);
        }

        return (Round7(model.Latitude), Round7(model.Longitude));
    }

    private static decimal Round7(decimal value) =>
        Math.Round(value, 7, MidpointRounding.AwayFromZero);
}
