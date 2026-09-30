using System.Security.Claims;
using MarketLinkWebsite.Models.ViewModels;
using MarketLinkWebsite.Services;
using Microsoft.AspNetCore.Mvc;

namespace MarketLinkWebsite.Controllers;

public sealed class ProductController : Controller
{
    private readonly ICatalogService catalogService;

    public ProductController(ICatalogService catalogService)
    {
        this.catalogService = catalogService;
    }

    [HttpGet("Product")]
    [HttpGet("Product/Index")]
    public async Task<IActionResult> Index(string? category, string? searchQuery, string? marketLocation, string? marketDay, decimal? maxPrice, string? sort, CancellationToken cancellationToken)
    {
        var products = await catalogService.GetProductsAsync(searchQuery, category, marketLocation, marketDay, maxPrice, sort, cancellationToken);
        var options = await catalogService.GetFilterOptionsAsync(cancellationToken);

        ViewData["SelectedCategory"] = string.IsNullOrWhiteSpace(category) ? "All products" : category;
        ViewData["SearchQuery"] = searchQuery;
        ViewData["MarketLocation"] = marketLocation;
        ViewData["MarketDay"] = marketDay;
        ViewData["MaxPrice"] = maxPrice;
        ViewData["Sort"] = sort;
        ViewData["Categories"] = options.Categories;
        ViewData["Markets"] = options.Markets;
        ViewData["MarketDays"] = options.MarketDays;
        ViewData["MaxCatalogPrice"] = options.MaxCatalogPrice;
        ViewData["NextMarketLabel"] = options.NextMarketLabel;

        return View(products);
    }

    [HttpGet("Product/Details/{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var viewerId = (User as ClaimsPrincipal)?.FindFirstValue(ClaimTypes.NameIdentifier);
        var product = await catalogService.GetProductDetailAsync(id, viewerId, cancellationToken);
        return product is null ? NotFound() : View(product);
    }
}
