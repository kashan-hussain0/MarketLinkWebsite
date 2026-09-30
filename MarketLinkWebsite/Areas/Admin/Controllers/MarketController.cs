using MarketLinkWebsite.Areas.Admin.Models;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using MarketLinkWebsite.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = "AdminAccess")]
public sealed class MarketController : AdminControllerBase
{
    private readonly IImageStorage images;

    public MarketController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IImageStorage images)
        : base(db, userManager, roleManager)
    {
        this.images = images;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] string? search,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var query = Db.Markets.AsNoTracking();
        var term = Clean(search);
        if (term.Length > 0)
        {
            var pattern = SearchPattern(term);
            query = query.Where(market => EF.Functions.Like(market.Name, pattern, "\\")
                || EF.Functions.Like(market.Address, pattern, "\\")
                || EF.Functions.Like(market.City, pattern, "\\"));
        }

        var statusFilter = NormalizeStatusFilter(status);
        if (statusFilter == "Active")
        {
            query = query.Where(market => market.IsActive);
        }
        else if (statusFilter == "Paused")
        {
            query = query.Where(market => !market.IsActive);
        }

        var marketRows = await query
            .OrderByDescending(market => market.IsFeatured)
            .ThenByDescending(market => market.UpdatedAt)
            .Select(market => new
            {
                market.Id,
                market.Name,
                market.Address,
                market.City,
                market.OperatingDays,
                market.OpenTime,
                market.CloseTime,
                market.IsActive,
                market.IsFeatured,
                ProductCount = market.Orders.SelectMany(order => order.OrderItems).Where(item => item.ProductId != null).Select(item => item.ProductId).Distinct().Count(),
                OrderCount = market.Orders.Count(),
                Revenue = market.Orders.Where(order => order.PaymentStatus == PaymentStatus.Paid && order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Declined).Sum(order => order.TotalAmount)
            })
            .ToListAsync(cancellationToken);
        var markets = marketRows.Select(market => new MarketSummary
        {
            Id = market.Id,
            Name = market.Name,
            Location = market.Address + (string.IsNullOrWhiteSpace(market.City) ? string.Empty : ", " + market.City),
            Hours = string.Join(" · ", new[] { market.OperatingDays, $"{market.OpenTime}-{market.CloseTime}" }.Where(value => !string.IsNullOrWhiteSpace(value))),
            ProductCount = market.ProductCount,
            OrderCount = market.OrderCount,
            Revenue = market.Revenue,
            Status = market.IsActive ? "Active" : "Paused",
            StatusTone = market.IsActive ? "success" : "secondary",
            IsFeatured = market.IsFeatured,
            Initials = market.Name,
            AvatarTone = market.Name
        }).ToList();
        var totalMarkets = await Db.Markets.CountAsync(cancellationToken);
        var model = new MarketListViewModel
        {
            SearchTerm = term,
            StatusFilter = statusFilter,
            TotalMarkets = totalMarkets,
            ActiveMarkets = await Db.Markets.CountAsync(market => market.IsActive, cancellationToken),
            FeaturedMarkets = await Db.Markets.CountAsync(market => market.IsFeatured && market.IsActive, cancellationToken),
            Markets = markets
        };
        return View(model);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new MarketFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Create(MarketFormViewModel model, IFormFile? photo, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (string.IsNullOrWhiteSpace(model.ImageUrl) && photo is not null && photo.Length > 0)
        {
            if (!images.TryStore(photo, "markets", out var stored, out var failure))
            {
                ModelState.AddModelError("Photo", failure);
                return View(model);
            }

            model.ImageUrl = stored.PublicPath;
        }

        var market = new Market();
        Apply(model, market);
        Db.Markets.Add(market);
        await NotifyAdministratorsAsync($"New market: {market.Name}", $"{market.Name} was added to the marketplace.", "/Admin/Market", cancellationToken);
        AddAudit("Create market", nameof(Market), 0, $"Created market {market.Name} in {market.City}.");
        await Db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = $"{market.Name} was created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest();
        }

        var market = await Db.Markets.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (market is null)
        {
            return NotFound();
        }

        return View(ToForm(market));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Edit(MarketFormViewModel model, IFormFile? photo, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (model.Id <= 0)
        {
            return BadRequest();
        }

        var market = await Db.Markets.FirstOrDefaultAsync(item => item.Id == model.Id, cancellationToken);
        if (market is null)
        {
            return NotFound();
        }

        if (photo is not null && photo.Length > 0)
        {
            if (!images.TryStore(photo, "markets", out var stored, out var failure))
            {
                ModelState.AddModelError("Photo", failure);
                return View(model);
            }

            model.ImageUrl = stored.PublicPath;
        }

        Apply(model, market);
        AddAudit("Update market", nameof(Market), market.Id, $"Updated market {market.Name}.");
        await NotifyAdministratorsAsync("Market details updated", $"{market.Name} details were updated.", $"/Admin/Market/Edit/{market.Id}", cancellationToken);
        await Db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = $"{market.Name} was updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest();
        }

        var market = await Db.Markets.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (market is null)
        {
            return NotFound();
        }

        if (await Db.Orders.AnyAsync(order => order.MarketId == market.Id, cancellationToken))
        {
            TempData["Error"] = $"{market.Name} has linked orders and cannot be deleted. Pause it instead.";
            return RedirectToAction(nameof(Index));
        }

        var name = market.Name;
        Db.Markets.Remove(market);
        AddAudit("Delete market", nameof(Market), id, $"Deleted market {name}.");
        try
        {
            await Db.SaveChangesAsync(cancellationToken);
            TempData["Success"] = $"{name} was deleted.";
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = $"{name} could not be deleted because related records still reference it.";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task NotifyAdministratorsAsync(string title, string message, string actionUrl, CancellationToken cancellationToken)
    {
        var adminRoleId = await Db.Roles.Where(role => role.Name == "Admin").Select(role => role.Id).FirstOrDefaultAsync(cancellationToken);
        if (adminRoleId is null)
        {
            return;
        }

        var administrators = await Db.Users
            .Where(user => user.IsActive && Db.UserRoles.Any(userRole => userRole.UserId == user.Id && userRole.RoleId == adminRoleId))
            .ToListAsync(cancellationToken);
        foreach (var administrator in administrators)
        {
            AddNotification(administrator, NotificationType.Announcement, title, message, actionUrl);
        }
    }

    private static void Apply(MarketFormViewModel model, Market market)
    {
        market.Name = Clean(model.Name);
        market.Description = Clean(model.Description);
        market.Address = Clean(model.Address);
        market.City = Clean(model.City);
        market.OperatingDays = Clean(model.OperatingDays);
        market.OpenTime = model.OpenTime.Trim();
        market.CloseTime = model.CloseTime.Trim();
        market.Latitude = model.Latitude;
        market.Longitude = model.Longitude;
        market.ImageUrl = Clean(model.ImageUrl);
        market.IsActive = model.IsActive;
        market.IsFeatured = model.IsActive && model.IsFeatured;
        market.UpdatedAt = DateTime.UtcNow;
    }

    private static MarketFormViewModel ToForm(Market market)
    {
        return new MarketFormViewModel
        {
            Id = market.Id,
            Name = market.Name,
            Description = market.Description,
            Address = market.Address,
            City = market.City,
            OperatingDays = market.OperatingDays,
            OpenTime = market.OpenTime,
            CloseTime = market.CloseTime,
            Latitude = market.Latitude,
            Longitude = market.Longitude,
            ImageUrl = market.ImageUrl,
            IsActive = market.IsActive,
            IsFeatured = market.IsFeatured
        };
    }

    private static string NormalizeStatusFilter(string? status)
    {
        var value = Clean(status);
        return value.Equals("Active", StringComparison.OrdinalIgnoreCase) || value.Equals("Paused", StringComparison.OrdinalIgnoreCase) ? value : "All markets";
    }
}
