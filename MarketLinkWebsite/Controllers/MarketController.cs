using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.ViewModels;
using MarketLinkWebsite.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Controllers;

public sealed class MarketController : Controller
{
    private readonly ICatalogService catalogService;
    private readonly MapService maps;
    private readonly ApplicationDbContext db;

    public MarketController(ICatalogService catalogService, MapService maps, ApplicationDbContext db)
    {
        this.catalogService = catalogService;
        this.maps = maps;
        this.db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? searchQuery, string? marketDay, string? sort, CancellationToken cancellationToken)
    {
        var markets = await catalogService.GetMarketsAsync(searchQuery, marketDay, sort, cancellationToken);
        ViewData["SearchQuery"] = searchQuery;
        ViewData["MarketDay"] = marketDay;
        ViewData["Sort"] = sort;

        var allDays = await catalogService.GetFilterOptionsAsync(cancellationToken);
        ViewData["MarketDays"] = allDays.MarketDays;
        ViewData["MapProvider"] = maps.ProviderLabel;
        ViewData["HasLocation"] = markets.Any(market => market.HasDistance);

        // One map for the whole pickup network: every market plus every farmer
        // stall that has a pinned location, each with its own directions link.
        ViewData["PickupMap"] = await BuildPickupMapAsync(cancellationToken);

        return View(markets);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var market = await catalogService.GetMarketAsync(id, cancellationToken);
        if (market is null)
        {
            return NotFound();
        }

        ViewData["MarketFarmers"] = await catalogService.GetMarketFarmersAsync(id, cancellationToken);
        ViewData["MarketPin"] = MarketMapViewModel.ForDetail(market);
        return View(market);
    }

    private async Task<MarketMapViewModel> BuildPickupMapAsync(CancellationToken cancellationToken)
    {
        var markets = await db.Markets
            .AsNoTracking()
            .Where(market => market.IsActive)
            .OrderBy(market => market.Name)
            .ToListAsync(cancellationToken);

        var links = await db.MarketFarmers
            .AsNoTracking()
            .Include(link => link.FarmerProfile).ThenInclude(profile => profile.User)
            .ToListAsync(cancellationToken);

        var model = MarketMapViewModel.ForMarkets(markets);

        var marketById = markets.ToDictionary(market => market.Id, market => market);
        var profileById = links
            .GroupBy(link => link.FarmerProfileId)
            .ToDictionary(group => group.Key, group => group.First().FarmerProfile);

        var stalls = MarketMapViewModel.ForStalls(
            links,
            id => marketById.TryGetValue(id, out var found) ? found : null,
            id => profileById.TryGetValue(id, out var found) ? found : null);

        model.Markers.AddRange(stalls.Markers);
        return model;
    }
}
