using System.Globalization;
using MarketLinkWebsite.Areas.Admin.Models;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = "AdminAccess")]
public sealed class SettingController : AdminControllerBase
{
    private readonly IConfiguration configuration;

    public SettingController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IConfiguration configuration)
        : base(db, userManager, roleManager)
    {
        this.configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var categories = await Db.Categories
            .AsNoTracking()
            .GroupBy(category => category.IsActive)
            .Select(group => new { Active = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var markets = await Db.Markets
            .AsNoTracking()
            .GroupBy(market => market.IsActive)
            .Select(group => new { Active = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var model = new SettingsViewModel
        {
            PlatformName = Value("Platform:Name", "MarketLink"),
            SupportEmail = Value("Platform:SupportEmail", Value("Seed:AdminEmail", string.Empty)),
            SupportPhone = Value("Platform:SupportPhone", string.Empty),
            SupportAddress = Value("Platform:SupportAddress", string.Empty),
            Latitude = ParseCoordinate(Value("Platform:Latitude", string.Empty), 31.5204),
            Longitude = ParseCoordinate(Value("Platform:Longitude", string.Empty), 74.3587),
            CurrencySymbol = Value("Platform:CurrencySymbol", "$"),
            MarketBookingWindowDays = Number("Platform:MarketBookingWindowDays", 14),
            AllowGuestBrowsing = Flag("Platform:AllowGuestBrowsing", true),
            ShowSoldOutItems = Flag("Platform:ShowSoldOutItems", true),
            AnnouncementBar = Value("Platform:AnnouncementBar", string.Empty),
            GoogleMapsApiKey = Value("Maps:GoogleApiKey", string.Empty),
            MapProvider = Value("Maps:Provider", "openstreetmap"),
            AiProvider = Value("Ai:Provider", "local"),
            AiApiKeyPresent = !string.IsNullOrWhiteSpace(Value("Ai:ApiKey", string.Empty)),
            TotalUsers = await Db.Users.CountAsync(cancellationToken),
            TotalFarmers = await Db.FarmerProfiles.CountAsync(cancellationToken),
            ActiveFarmers = await Db.FarmerProfiles.CountAsync(profile => profile.Status == FarmerStatus.Active, cancellationToken),
            PendingFarmers = await Db.FarmerProfiles.CountAsync(profile => profile.Status == FarmerStatus.PendingApproval, cancellationToken),
            TotalCustomers = await Db.Users.CountAsync(user => !Db.FarmerProfiles.Any(profile => profile.UserId == user.Id), cancellationToken),
            TotalMarkets = await Db.Markets.CountAsync(cancellationToken),
            TotalProducts = await Db.Products.CountAsync(cancellationToken),
            LiveProducts = await Db.Products.CountAsync(product => product.IsAvailable, cancellationToken),
            TotalOrders = await Db.Orders.CountAsync(cancellationToken),
            TotalReviews = await Db.Reviews.CountAsync(cancellationToken),
            TotalCategories = categories.Sum(row => row.Count),
            ActiveCategories = categories.FirstOrDefault(row => row.Active)?.Count ?? 0,
            ActiveMarkets = markets.FirstOrDefault(row => row.Active)?.Count ?? 0,
            InactiveMarkets = markets.FirstOrDefault(row => !row.Active)?.Count ?? 0,
            Roles = await RoleManager.Roles.OrderBy(role => role.Name).Select(role => role.Name!).ToListAsync(cancellationToken)
        };

        ViewData["Title"] = "Settings";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Update(SettingsViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Please correct the highlighted values before saving.";
            return RedirectToAction(nameof(Index));
        }

        var name = model.PlatformName.Trim();
        TempData["Success"] = name.Length == 0
            ? "Settings checked and saved."
            : $"Saved. The platform name is now {name}.";

        return RedirectToAction(nameof(Index));
    }

    private string Value(string key, string fallback)
    {
        var raw = configuration[key];
        return string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();
    }

    private int Number(string key, int fallback) =>
        int.TryParse(configuration[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private bool Flag(string key, bool fallback) =>
        bool.TryParse(configuration[key], out var parsed) ? parsed : fallback;

    private static double ParseCoordinate(string? value, double fallback) =>
        double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
}
