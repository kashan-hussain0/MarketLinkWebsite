using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.ViewModels;
using MarketLinkWebsite.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using MarketLinkWebsite.Validation;

namespace MarketLinkWebsite.Controllers;

public sealed class CartController : Controller
{
    internal const string CustomerOnlyMessage = "Customer account required to place an order. Sign in as a customer, or create a customer account, to reserve from a stall.";

    private readonly ICartService cartService;
    private readonly ApplicationDbContext db;
    private readonly ILogger<CartController> logger;

    public CartController(ICartService cartService, ApplicationDbContext db, ILogger<CartController> logger)
    {
        this.cartService = cartService;
        this.db = db;
        this.logger = logger;
    }

    // An administrator or a farmer must never place a customer order.
    private bool IsCustomer() => User.IsInRole("Customer");

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        try
        {
            return View(await cartService.GetCartAsync(cancellationToken));
        }
        catch (OperationCanceledException)
        {
            return new EmptyResult();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(
        int productId,
        int quantity,
        CancellationToken cancellationToken,
        string? returnUrl = null)
    {
        try
        {
            await cartService.AddAsync(productId, quantity, cancellationToken);
            TempData["Success"] = "Item added to your pre-order basket.";
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            TempData["Error"] = "The basket service is temporarily unavailable. Please try again.";
        }
        catch (OperationCanceledException)
        {
            return new EmptyResult();
        }
        catch (InvalidOperationException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToBasket(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(
        int productId,
        int quantity,
        CancellationToken cancellationToken,
        string? returnUrl = null)
    {
        try
        {
            await cartService.UpdateAsync(productId, quantity, cancellationToken);
            TempData["Success"] = "Basket quantity updated.";
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            TempData["Error"] = "The basket service is temporarily unavailable. Please try again.";
        }
        catch (OperationCanceledException)
        {
            return new EmptyResult();
        }
        catch (InvalidOperationException exception)
        {
            TempData["Error"] = exception.Message;
        }

        return RedirectToBasket(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(
        int productId,
        CancellationToken cancellationToken,
        string? returnUrl = null)
    {
        try
        {
            await cartService.RemoveAsync(productId, cancellationToken);
            TempData["Success"] = "Item removed from your basket.";
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            TempData["Error"] = "The basket service is temporarily unavailable. Please try again.";
        }

        return RedirectToBasket(returnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        try
        {
            await cartService.ClearAsync(cancellationToken);
            TempData["Success"] = "Your basket is now empty.";
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            TempData["Error"] = "The basket service is temporarily unavailable. Please try again.";
        }

        return RedirectToAction(nameof(Index));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Checkout(CancellationToken cancellationToken)
    {
        // Only a customer owns an order. An administrator or a farmer is sent back
        // to the basket with a clear reason instead of an unfriendly error page.
        if (!IsCustomer())
        {
            TempData["Error"] = CustomerOnlyMessage;
            return RedirectToAction(nameof(Index));
        }

        var cart = await cartService.GetCartAsync(cancellationToken);
        if (cart.Items.Count == 0)
        {
            TempData["Error"] = "Add an item before starting checkout.";
            return RedirectToAction(nameof(Index));
        }

        var markets = await GetMarketsAsync(cancellationToken);
        if (markets.Count == 0)
        {
            TempData["Error"] = "No active pickup market is available for checkout.";
            return RedirectToAction(nameof(Index));
        }

        var model = new CheckoutFormViewModel
        {
            MarketId = markets[0].Id,
            PickupDate = GetNextPickupDate(markets[0].Day),
            CustomerName = await GetCustomerDisplayNameAsync(cancellationToken)
        };
        PopulateCheckoutView(model, markets, cancellationToken);

        return View(model);
    }

    [Authorize(Policy = "CustomerOnly")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(CheckoutFormViewModel model, CancellationToken cancellationToken)
    {
        model ??= new CheckoutFormViewModel();
        var markets = await GetMarketsAsync(cancellationToken);

        if (!ModelState.IsValid)
        {
            PopulateCheckoutView(model, markets, cancellationToken);
            return View(model);
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        if (markets.All(market => market.Id != model.MarketId))
        {
            ModelState.AddModelError(nameof(model.MarketId), "Select an active pickup market.");
            PopulateCheckoutView(model, markets, cancellationToken);
            return View(model);
        }

        var cart = await cartService.GetCartAsync(cancellationToken);
        if (cart.Items.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Your basket is empty.");
            PopulateCheckoutView(model, markets, cancellationToken);
            return View(model);
        }

        try
        {
            var result = await cartService.CheckoutAsync(userId, model, cancellationToken);
            TempData["OrderId"] = result.OrderId;
            TempData["OrderNumber"] = result.OrderNumber;
            return RedirectToAction(nameof(Confirmation), new { id = result.OrderId });
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            ModelState.AddModelError(string.Empty, "The reservation service is temporarily unavailable. Please try again.");
            PopulateCheckoutView(model, markets, cancellationToken);
            return View(model);
        }
        catch (InvalidOperationException exception) when (IsBusinessRule(exception))
        {
            // These messages are written by CheckoutAsync and are safe to show.
            ModelState.AddModelError(string.Empty, exception.Message);
            PopulateCheckoutView(model, markets, cancellationToken);
            return View(model);
        }
        catch (Exception exception)
        {
            // Anything unexpected is logged for the developer and reduced to a
            // plain sentence for the shopper. A raw exception or LINQ message
            // must never reach the page.
            logger.LogError(exception, "Checkout failed for basket of user {UserId}.", userId);
            ModelState.AddModelError(string.Empty, "We could not complete your reservation just now. Please try again.");
            PopulateCheckoutView(model, markets, cancellationToken);
            return View(model);
        }
    }

    // A rule the shopper can actually fix, such as an empty basket or a market
    // that is closed that day. Anything longer is a bug, not a rule.
    private static bool IsBusinessRule(InvalidOperationException exception) =>
        !string.IsNullOrWhiteSpace(exception.Message)
        && exception.Message.Length < 200
        && !exception.Message.Contains("LINQ", StringComparison.OrdinalIgnoreCase)
        && !exception.Message.Contains("Expression", StringComparison.OrdinalIgnoreCase);

    [Authorize(Policy = "CustomerOnly")]
    [HttpGet]
    public async Task<IActionResult> Confirmation(int? id = null, CancellationToken cancellationToken = default)
    {
        var orderId = id ?? ParseOrderId(TempData["OrderId"]);
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (orderId <= 0 || string.IsNullOrWhiteSpace(userId))
        {
            return RedirectToAction(nameof(Index));
        }

        var order = await db.Orders
            .AsNoTracking()
            .Include(item => item.Market)
            .FirstOrDefaultAsync(item => item.Id == orderId && item.UserId == userId, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        ViewData["OrderNumber"] = order.OrderNumber;
        ViewData["OrderId"] = order.Id;
        ViewData["PickupMarketName"] = order.Market.Name;
        ViewData["PickupAddress"] = order.Market.Address;
        ViewData["PickupCity"] = order.Market.City;
        ViewData["PickupDate"] = order.PickupDate.ToString("dddd, MMM d, yyyy", CultureInfo.CurrentCulture);
        ViewData["PickupSlot"] = order.PickupTimeSlot;
        ViewData["PickupDetails"] = $"{order.PickupDate:dddd, MMM d} · {order.PickupTimeSlot}";
        ViewData["OrderTotal"] = order.TotalAmount;

        return View();
    }

    private async Task<string> GetCustomerDisplayNameAsync(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return string.Empty;
        }

        var user = await db.Users
            .AsNoTracking()
            .Where(item => item.Id == userId)
            .Select(item => new { item.FirstName, item.LastName, item.Email })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return string.Empty;
        }

        var name = string.Join(' ', new[] { user.FirstName, user.LastName }.Where(part => !string.IsNullOrWhiteSpace(part))).Trim();
        return name.Length > 0 ? name : (user.Email ?? string.Empty);
    }

    private async Task<List<MarketCardViewModel>> GetMarketsAsync(CancellationToken cancellationToken)
    {
        var markets = await db.Markets
            .AsNoTracking()
            .Include(market => market.MarketFarmers)
                .ThenInclude(link => link.FarmerProfile)
                    .ThenInclude(profile => profile.Products)
                        .ThenInclude(product => product.Inventory)
            .Where(market => market.IsActive)
            .OrderByDescending(market => market.IsFeatured)
            .ThenBy(market => market.Name)
            .ToListAsync(cancellationToken);

        return markets.Select(MapMarket).ToList();
    }

    private static MarketCardViewModel MapMarket(MarketLinkWebsite.Models.Entities.Market market)
    {
        var links = market.MarketFarmers?.Where(link => link.FarmerProfile is not null).ToList() ?? [];
        var farmerCount = links.Select(link => link.FarmerProfile.Id).Distinct().Count();
        var productCount = links
            .SelectMany(link => link.FarmerProfile.Products ?? (ICollection<MarketLinkWebsite.Models.Entities.Product>)Array.Empty<MarketLinkWebsite.Models.Entities.Product>())
            .Where(product => product.IsAvailable && product.Inventory is not null && product.Inventory.QuantityAvailable > 0)
            .Select(product => product.Id)
            .Distinct()
            .Count();

        return new MarketCardViewModel
        {
            Id = market.Id,
            Name = market.Name,
            Description = market.Description,
            Address = market.Address,
            City = market.City,
            Day = market.OperatingDays,
            OpenTime = market.OpenTime,
            CloseTime = market.CloseTime,
            FarmerCount = farmerCount,
            ProductCount = productCount,
            DistanceKm = 0,
            Latitude = (double)market.Latitude,
            Longitude = (double)market.Longitude,
            IsOpenThisWeek = !string.IsNullOrWhiteSpace(market.OperatingDays)
                && market.OperatingDays.Contains(DateTime.Today.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase),
            ImageUrl = market.ImageUrl ?? string.Empty,
            EmbedUrl = BuildEmbedUrl(market)
        };
    }

    private void PopulateCheckoutView(CheckoutFormViewModel model, IReadOnlyList<MarketCardViewModel> markets, CancellationToken cancellationToken)
    {
        ViewData["Markets"] = markets;
        PopulateCheckoutBasket(model, markets);
        FillPickupSlots(model, markets, cancellationToken);

        // The slot list belongs to the chosen market, so the browser is given the
        // slots for every market up front. Changing the market then swaps the list
        // straight away instead of leaving the previous market's times on screen.
        // Each list is built for the weekday the browser will also pick, which is
        // the market's next trading day.
        var slotsByMarket = markets.ToDictionary(
            market => market.Id.ToString(CultureInfo.InvariantCulture),
            market => GetPickupSlots(market, model.BasketItems, NextTradingDay(market.Day)));

        ViewData["SlotsByMarket"] = JsonSerializer.Serialize(slotsByMarket);

        // A pickup date is only valid on a day the chosen market actually trades.
        // The days travel with the page so the date can follow the market.
        var daysByMarket = markets.ToDictionary(
            market => market.Id.ToString(CultureInfo.InvariantCulture),
            market => market.Day
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray());

        ViewData["DaysByMarket"] = JsonSerializer.Serialize(daysByMarket);
    }

    private void PopulateCheckoutBasket(CheckoutFormViewModel model, IReadOnlyList<MarketCardViewModel> markets)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
        {
            model.BasketItems = [];
            model.MarketOptions = markets
                .Select(market => new CheckoutMarketOption
                {
                    Id = market.Id,
                    Name = market.Name,
                    Day = market.Day,
                    OpenTime = market.OpenTime,
                    CloseTime = market.CloseTime,
                    FarmCount = market.FarmerCount,
                    FarmNames = market.FarmNames.ToList(),
                    CoversWholeBasket = false
                })
                .ToList();
            return;
        }

        var basket = cartService.GetCartAsync(HttpContext.RequestAborted).GetAwaiter().GetResult();

        model.BasketItems = basket.Items
            .Select(item => new CheckoutBasketItem
            {
                ProductId = item.ProductId,
                Name = item.ProductName,
                FarmName = item.FarmName,
                Quantity = item.Quantity,
                Unit = item.Unit.ToString().ToLowerInvariant(),
                UnitPrice = item.UnitPrice,
                InStock = item.QuantityAvailable,
                Markets = item.FarmerMarkets
                    .Select(link => new CheckoutItemMarket
                    {
                        MarketId = link.MarketId,
                        Name = link.MarketName,
                        Day = link.MarketDay
                    })
                    .OrderBy(market => market.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            })
            .ToList();

        var basketMarketIds = model.BasketItems
            .SelectMany(item => item.Markets)
            .Select(market => market.MarketId)
            .ToHashSet();

        // Only the farms in this basket are shown, so the shopper can see which
        // bakery or stall they will actually collect from.
        var basketFarms = model.BasketItems
            .Select(item => item.FarmName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        model.MarketOptions = markets
            .Select(market =>
            {
                var farmsHere = market.FarmNames
                    .Where(basketFarms.Contains)
                    .ToList();

                return new CheckoutMarketOption
                {
                    Id = market.Id,
                    Name = market.Name,
                    Day = market.Day,
                    OpenTime = market.OpenTime,
                    CloseTime = market.CloseTime,
                    FarmCount = market.FarmerCount,
                    FarmNames = farmsHere,
                    CoversWholeBasket = model.BasketItems.Count > 0
                        && model.BasketItems.All(item => item.Markets.Any(link => link.MarketId == market.Id))
                };
            })
            .OrderByDescending(option => option.CoversWholeBasket)
            .ThenByDescending(option => option.FarmNames.Count)
            .ThenBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (model.BasketItems.Count > 0 && !model.MarketOptions.Any(option => option.CoversWholeBasket))
        {
            model.BasketConflicts = model.BasketItems
                .Where(item => item.Markets.Count > 0)
                .GroupBy(item => item.FarmName, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var shared = group
                        .SelectMany(item => item.Markets)
                        .Select(market => market.MarketId)
                        .Distinct()
                        .ToHashSet();
                    return $"{group.Key} only trades at {string.Join(", ", group.First().Markets.Select(market => market.Name).Distinct())}.";
                })
                .ToList();

            if (model.BasketConflicts.Count == 0)
            {
                model.BasketConflicts = ["No single pickup market can cover every farm in your basket."];
            }
        }
    }

    private void FillPickupSlots(CheckoutFormViewModel model, IReadOnlyList<MarketCardViewModel> markets, CancellationToken cancellationToken)
    {
        if (model.MarketId <= 0 && markets.Count > 0)
        {
            model.MarketId = model.MarketOptions.FirstOrDefault(option => option.CoversWholeBasket)?.Id
                ?? model.MarketOptions.FirstOrDefault()?.Id
                ?? markets[0].Id;
        }

        var selectedMarket = markets.FirstOrDefault(market => market.Id == model.MarketId) ?? markets.FirstOrDefault();

        // A posted date that is not on a trading day would produce a slot list the
        // shopper cannot use, so the weekday falls back to the next open day.
        var day = model.PickupDate.Date >= DateTime.Today
            && (selectedMarket is null
                || string.IsNullOrWhiteSpace(selectedMarket.Day)
                || selectedMarket.Day.Contains(model.PickupDate.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase))
                ? model.PickupDate.DayOfWeek
                : NextTradingDay(selectedMarket?.Day);

        var pickupSlots = GetPickupSlots(selectedMarket, model.BasketItems, day);
        if (!pickupSlots.Contains(model.PickupSlot, StringComparer.OrdinalIgnoreCase))
        {
            model.PickupSlot = pickupSlots[0];
        }

        ViewData["PickupSlots"] = pickupSlots;
    }

    private static bool IsDatabaseFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is DbException)
            {
                return true;
            }
        }

        return false;
    }

    private IActionResult RedirectToBasket(string? returnUrl)
    {
        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction(nameof(Index));
    }

    private static int ParseOrderId(object? value)
    {
        return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var orderId)
            ? orderId
            : 0;
    }

    private static DateTime GetNextPickupDate(string? operatingDays)
    {
        if (!string.IsNullOrWhiteSpace(operatingDays))
        {
            for (var offset = 0; offset < 14; offset++)
            {
                var date = DateTime.Today.AddDays(offset);
                if (operatingDays.Contains(date.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return date;
                }
            }
        }

        return DateTime.Today.AddDays(2);
    }

    private IReadOnlyList<string> GetPickupSlots(MarketCardViewModel? market, IReadOnlyList<CheckoutBasketItem>? basketItems, DayOfWeek dayOfWeek)
    {
        // The market opening hours are the hard limit. A slot outside them can
        // never be booked, so they are always the starting point.
        var marketStart = ParseTime(market?.OpenTime) ?? TimeSpan.FromHours(9);
        var marketEnd = ParseTime(market?.CloseTime) ?? marketStart + TimeSpan.FromHours(4);
        if (marketEnd <= marketStart)
        {
            marketEnd = marketStart + TimeSpan.FromHours(4);
        }

        // Published slots win when every farm in the basket has set some for this
        // weekday. The farmer told us exactly when they are at the stall, so the
        // shopper is only offered those times.
        var published = LoadPublishedSlots(basketItems, dayOfWeek);
        if (published is { Count: > 0 })
        {
            var slots = IntersectSlots(published, marketStart, marketEnd);
            if (slots.Count > 0)
            {
                return slots;
            }
        }

        var start = marketStart;
        var end = marketEnd;

        // Narrow the window to the farmers in the basket, but only when their own
        // pickup windows actually sit inside the market hours. A farmer who does
        // not overlap this market must not remove every slot from the list.
        if (basketItems is { Count: > 0 })
        {
            var farmerNames = basketItems
                .Select(item => item.FarmName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (farmerNames.Count > 0)
            {
                var profiles = db.FarmerProfiles
                    .AsNoTracking()
                    .Where(profile => farmerNames.Contains(profile.FarmName))
                    .Select(profile => profile.PickupWindows)
                    .ToList();

                var starts = new List<TimeSpan>();
                var ends = new List<TimeSpan>();

                foreach (var window in profiles)
                {
                    foreach (var (windowStart, windowEnd) in ParsePickupWindows(window))
                    {
                        starts.Add(windowStart);
                        ends.Add(windowEnd);
                    }
                }

                if (starts.Count > 0)
                {
                    var earliestStart = starts.Max();
                    var latestEnd = ends.Min();

                    // The shared farm window is only worth using when it leaves at
                    // least one hour inside the market's own hours. A farm that does
                    // not trade at this market would otherwise push the end before the
                    // start and leave the shopper with a single odd slot.
                    var narrowedStart = earliestStart > start ? earliestStart : start;
                    var narrowedEnd = latestEnd < end ? latestEnd : end;

                    if (narrowedStart <= narrowedEnd - TimeSpan.FromHours(1))
                    {
                        start = narrowedStart;
                        end = narrowedEnd;
                    }
                }
            }
        }

        var hourly = new List<string>();
        for (var current = start; current + TimeSpan.FromHours(1) <= end && hourly.Count < 8; current += TimeSpan.FromHours(1))
        {
            hourly.Add($"{FormatTime(current)} - {FormatTime(current + TimeSpan.FromHours(1))}");
        }

        return hourly.Count > 0 ? hourly : [$"{FormatTime(marketStart)} - {FormatTime(marketStart + TimeSpan.FromHours(1))}"];
    }

    /// <summary>
    /// Reads the pickup slots every farm in the basket has published for this
    /// weekday. The result is null unless all of them have at least one, because a
    /// partial list would offer a time that one of the farms cannot serve.
    /// </summary>
    private Dictionary<string, List<(TimeSpan Start, TimeSpan End)>>? LoadPublishedSlots(
        IReadOnlyList<CheckoutBasketItem>? basketItems,
        DayOfWeek dayOfWeek)
    {
        if (basketItems is not { Count: > 0 })
        {
            return null;
        }

        var farmerNames = basketItems
            .Select(item => item.FarmName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (farmerNames.Count == 0)
        {
            return null;
        }

        var rows = db.PickupSlots
            .AsNoTracking()
            .Where(slot => slot.IsActive
                && slot.DayOfWeek == (int)dayOfWeek
                && farmerNames.Contains(slot.FarmerProfile.FarmName))
            .Select(slot => new { slot.FarmerProfile.FarmName, slot.StartTime, slot.EndTime })
            .ToList();

        if (rows.Count == 0)
        {
            return null;
        }

        var published = rows
            .Where(row => TryParseTime(row.StartTime, out var start) && TryParseTime(row.EndTime, out var end) && end > start)
            .GroupBy(row => row.FarmName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(row => (Start: ParseTime(row.StartTime)!.Value, End: ParseTime(row.EndTime)!.Value))
                    .OrderBy(window => window.Start)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

        return published.Count == farmerNames.Count ? published : null;
    }

    /// <summary>
    /// Finds the stretches of time that every farm has open at the same time, cut
    /// to the hours the market itself is open.
    /// </summary>
    private static List<string> IntersectSlots(
        Dictionary<string, List<(TimeSpan Start, TimeSpan End)>> published,
        TimeSpan marketStart,
        TimeSpan marketEnd)
    {
        // Start from the first farm's windows and keep only the stretches that the
        // rest of the farms also cover.
        var shared = published.Values.First()
            .Select(window => (window.Start, window.End))
            .ToList();

        foreach (var windows in published.Values.Skip(1))
        {
            var trimmed = new List<(TimeSpan Start, TimeSpan End)>();

            foreach (var (start, end) in shared)
            {
                foreach (var window in windows)
                {
                    var overlapStart = start > window.Start ? start : window.Start;
                    var overlapEnd = end < window.End ? end : window.End;

                    if (overlapEnd > overlapStart)
                    {
                        trimmed.Add((overlapStart, overlapEnd));
                    }
                }
            }

            shared = trimmed;
        }

        var slots = new List<string>();
        foreach (var (start, end) in shared)
        {
            var from = start > marketStart ? start : marketStart;
            var to = end < marketEnd ? end : marketEnd;

            for (var current = from; current < to && slots.Count < 8; current += TimeSpan.FromHours(1))
            {
                var slotEnd = current + TimeSpan.FromHours(1) <= to
                    ? current + TimeSpan.FromHours(1)
                    : to;

                slots.Add($"{FormatTime(current)} - {FormatTime(slotEnd)}");
                current = slotEnd - TimeSpan.FromHours(1);
            }
        }

        return slots;
    }

    /// <summary>
    /// The first weekday a market trades on, counting from today. Used so the slot
    /// list on the page matches the date the browser chooses for that market.
    /// </summary>
    private static DayOfWeek NextTradingDay(string? operatingDays)
    {
        if (string.IsNullOrWhiteSpace(operatingDays))
        {
            return DateTime.Today.DayOfWeek;
        }

        var days = operatingDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var offset = 0; offset < 14; offset++)
        {
            var candidate = DateTime.Today.AddDays(offset);
            if (days.Any(day => day.Equals(candidate.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                return candidate.DayOfWeek;
            }
        }

        return DateTime.Today.DayOfWeek;
    }

    private List<(TimeSpan Start, TimeSpan End)> ParsePickupWindows(string? pickupWindows)
    {
        var windows = new List<(TimeSpan Start, TimeSpan End)>();
        if (string.IsNullOrWhiteSpace(pickupWindows))
        {
            return windows;
        }

        var parts = pickupWindows.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var times = part.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (times.Length == 2
                && TryParseTime(times[0].Trim(), out var start)
                && TryParseTime(times[1].Trim(), out var end))
            {
                windows.Add((start, end));
            }
        }

        return windows;
    }

    private static TimeSpan? ParseTime(string? value)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dateTime))
        {
            return dateTime.TimeOfDay;
        }

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var time) ? time : null;
    }

    private static bool TryParseTime(string? value, out TimeSpan time)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dateTime))
        {
            time = dateTime.TimeOfDay;
            return true;
        }

        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out time);
    }

    private static string FormatTime(TimeSpan value)
    {
        return DateTime.Today.Add(value).ToString("h:mm tt", CultureInfo.InvariantCulture);
    }

    private static string BuildEmbedUrl(MarketLinkWebsite.Models.Entities.Market market)
    {
        var longitude = Math.Clamp((double)market.Longitude, -180, 180);
        var latitude = Math.Clamp((double)market.Latitude, -90, 90);
        var west = Math.Max(-180, longitude - 0.01);
        var east = Math.Min(180, longitude + 0.01);
        var south = Math.Max(-90, latitude - 0.01);
        var north = Math.Min(90, latitude + 0.01);

        return string.Format(
            CultureInfo.InvariantCulture,
            "https://www.openstreetmap.org/export/embed.html?bbox={0:F6}%2C{1:F6}%2C{2:F6}%2C{3:F6}&layer=mapnik&marker={4:F6}%2C{5:F6}",
            west,
            south,
            east,
            north,
            longitude,
            latitude);
    }
}
