using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using MarketLinkWebsite.Models.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Data.Common;
using System.Globalization;

namespace MarketLinkWebsite.Services;

public sealed class CartService : ICartService
{
    private const string GuestCartCookieName = ".MarketLink.Cart";

    private readonly ApplicationDbContext db;
    private readonly IHttpContextAccessor httpContextAccessor;
    private readonly IConfiguration configuration;
    private readonly OrderEmailDispatcher orderEmails;

    public CartService(
        ApplicationDbContext db,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration,
        OrderEmailDispatcher orderEmails)
    {
        this.db = db;
        this.httpContextAccessor = httpContextAccessor;
        this.configuration = configuration;
        this.orderEmails = orderEmails;
    }

    public async Task<int> GetItemCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var guestId = GetGuestId();

            if (userId is null)
            {
                return await db.CartItems
                    .Where(item => item.UserId == null && item.GuestId == guestId)
                    .SumAsync(item => (int?)item.Quantity, cancellationToken) ?? 0;
            }

            return await db.CartItems
                .Where(item => item.UserId == userId)
                .SumAsync(item => (int?)item.Quantity, cancellationToken) ?? 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return 0;
        }
    }

    public async Task<CartViewModel> GetCartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetUserId();
            var guestId = GetGuestId();
            if (userId is not null)
            {
                await MergeGuestCartAsync(userId, cancellationToken);
            }

            var items = await CartQuery()
                .AsNoTracking()
                .Where(item => userId == null ? item.UserId == null && item.GuestId == guestId : item.UserId == userId)
                .ToListAsync(cancellationToken);

            return Map(items);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return new CartViewModel();
        }
    }

    public async Task AddAsync(int productId, int quantity, CancellationToken cancellationToken = default)
    {
        if (quantity < 1)
        {
            throw new InvalidOperationException("Quantity must be at least one.");
        }

        var product = await db.Products
            .Include(item => item.Inventory)
            .Include(item => item.FarmerProfile)
                .ThenInclude(profile => profile.User)
            .FirstOrDefaultAsync(item => item.Id == productId
                && item.IsAvailable
                && item.FarmerProfile.Status == FarmerStatus.Active
                && item.FarmerProfile.User.IsActive,
                cancellationToken)
            ?? throw new InvalidOperationException("This product is not available.");

        var stock = product.Inventory?.QuantityAvailable ?? 0;
        if (stock < quantity)
        {
            throw new InvalidOperationException("The requested quantity is not available.");
        }

        var userId = GetUserId();
        var guestId = GetGuestId();
        if (userId is not null)
        {
            await MergeGuestCartAsync(userId, cancellationToken);
        }

        var item = await CartQuery().FirstOrDefaultAsync(cartItem =>
            userId == null
                ? cartItem.UserId == null && cartItem.GuestId == guestId && cartItem.ProductId == productId
                : cartItem.UserId == userId && cartItem.ProductId == productId,
            cancellationToken);

        if (item is null)
        {
            item = new CartItem
            {
                UserId = userId,
                GuestId = guestId,
                ProductId = productId,
                Quantity = quantity
            };
            db.CartItems.Add(item);
        }
        else
        {
            item.Quantity += quantity;
            item.UpdatedAt = DateTime.UtcNow;
        }

        if (item.Quantity > stock)
        {
            throw new InvalidOperationException("The requested quantity is not available.");
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(int productId, int quantity, CancellationToken cancellationToken = default)
    {
        if (quantity < 1)
        {
            await RemoveAsync(productId, cancellationToken);
            return;
        }

        var userId = GetUserId();
        var guestId = GetGuestId();
        if (userId is not null)
        {
            await MergeGuestCartAsync(userId, cancellationToken);
        }

        var item = await CartQuery()
            .Include(cartItem => cartItem.Product)
            .ThenInclude(product => product.Inventory)
            .FirstOrDefaultAsync(cartItem =>
                userId == null
                    ? cartItem.UserId == null && cartItem.GuestId == guestId && cartItem.ProductId == productId
                    : cartItem.UserId == userId && cartItem.ProductId == productId,
                cancellationToken)
            ?? throw new InvalidOperationException("Cart item not found.");

        var stock = item.Product.Inventory?.QuantityAvailable ?? 0;
        if (quantity > stock)
        {
            throw new InvalidOperationException("The requested quantity is not available.");
        }

        item.Quantity = quantity;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(int productId, CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();
        var guestId = GetGuestId();
        if (userId is not null)
        {
            await MergeGuestCartAsync(userId, cancellationToken);
        }

        var item = await CartQuery().FirstOrDefaultAsync(cartItem =>
            userId == null
                ? cartItem.UserId == null && cartItem.GuestId == guestId && cartItem.ProductId == productId
                : cartItem.UserId == userId && cartItem.ProductId == productId,
            cancellationToken);

        if (item is not null)
        {
            db.CartItems.Remove(item);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();
        var guestId = GetGuestId();
        if (userId is not null)
        {
            await MergeGuestCartAsync(userId, cancellationToken);
        }

        var items = await CartQuery()
            .Where(item => userId == null ? item.UserId == null && item.GuestId == guestId : item.UserId == userId)
            .ToListAsync(cancellationToken);
        db.CartItems.RemoveRange(items);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CheckoutResult> CheckoutAsync(string userId, CheckoutFormViewModel model, CancellationToken cancellationToken = default)
    {
        if (model.PickupDate.Date < DateTime.Today)
        {
            throw new InvalidOperationException("Pickup date must be today or later.");
        }

        var bookingWindowDays = configuration.GetValue("Platform:MarketBookingWindowDays", 14);
        if (model.PickupDate.Date > DateTime.Today.AddDays(bookingWindowDays))
        {
            throw new InvalidOperationException($"Pickup date must be within {bookingWindowDays} days from today.");
        }

        if (string.IsNullOrWhiteSpace(model.PickupAddress) || string.IsNullOrWhiteSpace(model.City) || string.IsNullOrWhiteSpace(model.Phone))
        {
            throw new InvalidOperationException("Pickup address, city and phone are required.");
        }

        await MergeGuestCartAsync(userId, cancellationToken);

        var market = await db.Markets.FirstOrDefaultAsync(item => item.Id == model.MarketId && item.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("The selected market is not available.");

        if (!IsWithinMarketHours(model.PickupSlot, market.OpenTime, market.CloseTime))
        {
            throw new InvalidOperationException("Choose a pickup time inside the market hours.");
        }

        var operatingDays = market.OperatingDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (operatingDays.Length > 0 && !operatingDays.Contains(model.PickupDate.DayOfWeek.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The selected market is not open on that day.");
        }

        var checkoutItems = await CartQuery()
            .Include(item => item.Product)
                .ThenInclude(product => product.Inventory)
            .Include(item => item.Product)
                .ThenInclude(product => product.FarmerProfile)
                    .ThenInclude(profile => profile.User)
            .Include(item => item.Product)
                .ThenInclude(product => product.FarmerProfile)
                    .ThenInclude(profile => profile.MarketFarmers)
            .Where(item => item.UserId == userId)
            .ToListAsync(cancellationToken);

        if (checkoutItems.Count == 0)
        {
            throw new InvalidOperationException("Your basket is empty.");
        }

        // The basket is already in memory, so the farmer ids are collected first.
        // Passing a loaded list straight into the query would ask EF to translate
        // it, which it cannot do.
        var farmerIds = checkoutItems
            .Select(item => item.Product.FarmerProfileId)
            .Distinct()
            .ToList();

        var farmerProfiles = await db.FarmerProfiles
            .AsNoTracking()
            .Where(profile => farmerIds.Contains(profile.Id))
            .Select(profile => new { profile.FarmName, profile.OrderCutoffHours, profile.PickupWindows })
            .ToListAsync(cancellationToken);

        foreach (var profile in farmerProfiles)
        {
            if (profile.OrderCutoffHours > 0)
            {
                var cutoff = model.PickupDate.AddHours(-profile.OrderCutoffHours);
                if (DateTime.UtcNow >= cutoff)
                {
                    throw new InvalidOperationException($"{profile.FarmName} stops accepting pre-orders {profile.OrderCutoffHours} hours before pickup. Please choose a later date.");
                }
            }
        }

        await EnsurePublishedSlotIsHonouredAsync(farmerIds, model, cancellationToken);

        var items = await CartQuery()
            .Include(item => item.Product)
                .ThenInclude(product => product.Inventory)
            .Include(item => item.Product)
                .ThenInclude(product => product.FarmerProfile)
                    .ThenInclude(profile => profile.User)
            .Include(item => item.Product)
                .ThenInclude(product => product.FarmerProfile)
                    .ThenInclude(profile => profile.MarketFarmers)
            .Where(item => item.UserId == userId)
            .ToListAsync(cancellationToken);

        if (items.Count == 0)
        {
            throw new InvalidOperationException("Your basket is empty.");
        }

        foreach (var item in items)
        {
            if (!item.Product.IsAvailable || item.Product.Inventory is null || item.Product.Inventory.QuantityAvailable < item.Quantity)
            {
                throw new InvalidOperationException($"The requested quantity for {item.Product.Name} is no longer available.");
            }

            if (item.Product.FarmerProfile.Status != FarmerStatus.Active || !item.Product.FarmerProfile.User.IsActive)
            {
                throw new InvalidOperationException($"{item.Product.FarmerProfile.FarmName} is not currently accepting pre-orders.");
            }

            if (!item.Product.FarmerProfile.MarketFarmers.Any(link => link.MarketId == market.Id))
            {
                throw new InvalidOperationException($"{item.Product.FarmerProfile.FarmName} does not sell at the selected market.");
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var order = new Order
            {
                OrderNumber = $"ML-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
                UserId = userId,
                MarketId = market.Id,
                OrderDate = DateTime.UtcNow,
                PickupDate = model.PickupDate.Date,
                PickupTimeSlot = model.PickupSlot.Trim(),
                PickupAddress = model.PickupAddress.Trim(),
                City = model.City.Trim(),
                Phone = model.Phone.Trim(),
                Notes = model.PickupNotes.Trim(),
                TotalAmount = items.Sum(item => item.Product.Price * item.Quantity),
                Status = OrderStatus.Pending,
                PaymentStatus = PaymentStatus.Pending
            };

            foreach (var item in items)
            {
                order.OrderItems.Add(new OrderItem
                {
                    ProductId = item.ProductId,
                    ProductName = item.Product.Name,
                    FarmerName = item.Product.FarmerProfile.FarmName,
                    Unit = item.Product.Unit,
                    Quantity = item.Quantity,
                    UnitPrice = item.Product.Price
                });

                item.Product.Inventory!.QuantityAvailable -= item.Quantity;
                item.Product.Inventory.LastUpdated = DateTime.UtcNow;
                item.Product.IsAvailable = item.Product.Inventory.QuantityAvailable > 0;
            }

            order.StatusHistory.Add(new OrderStatusHistory
            {
                Status = OrderStatus.Pending,
                ChangedById = userId,
                Note = "Order placed by the customer.",
                ChangedAt = DateTime.UtcNow
            });

            db.Orders.Add(order);
            db.CartItems.RemoveRange(items);
            db.Notifications.Add(new Notification
            {
                UserId = userId,
                Type = NotificationType.Order,
                Title = "Pre-order received",
                Message = $"Your reservation {order.OrderNumber} is waiting for farmer confirmation.",
                ActionUrl = $"/Customer/OrderDetails/{order.Id}"
            });

            foreach (var farmerUserId in items.Select(item => item.Product.FarmerProfile.UserId).Distinct())
            {
                db.Notifications.Add(new Notification
                {
                    UserId = farmerUserId,
                    Type = NotificationType.Order,
                    Title = "New pre-order",
                    Message = $"A new reservation {order.OrderNumber} is waiting for your review.",
                    ActionUrl = $"/Farmer/Order/Details/{order.Id}"
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            // The order is safely stored at this point, so the confirmation email
            // goes out afterwards. A mail problem must never undo the reservation.
            await orderEmails.SendOrderPlacedAsync(order.Id, cancellationToken);

            return new CheckoutResult(order.Id, order.OrderNumber);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// A farmer who has published pickup slots owns the times a shopper may book.
    /// When every farm in the basket has published slots for the pickup weekday,
    /// the chosen time has to sit inside one of them, otherwise a farm would be
    /// asked to hand over produce at a time it is closed.
    /// </summary>
    private async Task EnsurePublishedSlotIsHonouredAsync(
        List<int> farmerIds,
        CheckoutFormViewModel model,
        CancellationToken cancellationToken)
    {
        if (farmerIds.Count == 0)
        {
            return;
        }

        var rows = await db.PickupSlots
            .AsNoTracking()
            .Where(slot => slot.IsActive
                && slot.DayOfWeek == (int)model.PickupDate.DayOfWeek
                && farmerIds.Contains(slot.FarmerProfileId))
            .Select(slot => new { slot.FarmerProfile.FarmName, slot.StartTime, slot.EndTime })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return;
        }

        var published = rows
            .GroupBy(row => row.FarmName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        // A partial list would let the shopper book a time the farms without
        // published slots cannot serve, so it is only enforced when all of them set.
        if (published.Count < farmerIds.Count)
        {
            return;
        }

        if (!TryParseTimeRange(model.PickupSlot, out var start, out var end))
        {
            throw new InvalidOperationException("Choose a pickup time from the published slots.");
        }

        var coversSlot = rows
            .Where(row => TryParseTimeRange($"{row.StartTime} - {row.EndTime}", out var slotStart, out var slotEnd)
                && start >= slotStart
                && end <= slotEnd)
            .Select(row => row.FarmName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (coversSlot < farmerIds.Count)
        {
            var name = published.Keys.FirstOrDefault() ?? "This farm";
            throw new InvalidOperationException($"{name} does not offer that pickup time. Please choose one of the published slots.");
        }
    }

    private static bool TryParseTimeRange(string? slot, out TimeSpan start, out TimeSpan end)
    {
        start = default;
        end = default;

        if (string.IsNullOrWhiteSpace(slot))
        {
            return false;
        }

        var parts = slot.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !TryParseTime(parts[0], out start) || !TryParseTime(parts[1], out end))
        {
            return false;
        }

        if (end <= start)
        {
            end = end.Add(TimeSpan.FromDays(1));
        }

        return true;
    }

    private static bool IsWithinMarketHours(string? slot, string? openTime, string? closeTime)
    {
        if (string.IsNullOrWhiteSpace(slot))
        {
            return false;
        }

        var parts = slot.Split(" - ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !TryParseTime(parts[0], out var start) || !TryParseTime(parts[1], out var end))
        {
            return false;
        }

        if (end <= start)
        {
            end = end.Add(TimeSpan.FromDays(1));
        }

        if (!TryParseTime(openTime, out var marketStart) || !TryParseTime(closeTime, out var marketEnd))
        {
            return true;
        }

        if (marketEnd <= marketStart)
        {
            marketEnd = marketEnd.Add(TimeSpan.FromDays(1));
            if (end <= start)
            {
                end = end.Add(TimeSpan.FromDays(1));
            }
        }

        return start >= marketStart && end <= marketEnd;
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

    private IQueryable<CartItem> CartQuery()
    {
        return db.CartItems
            .AsSplitQuery()
            .Include(item => item.Product)
                .ThenInclude(product => product.Inventory)
            .Include(item => item.Product)
                .ThenInclude(product => product.FarmerProfile)
                    .ThenInclude(profile => profile.User)
            .Include(item => item.Product)
                .ThenInclude(product => product.FarmerProfile)
                    .ThenInclude(profile => profile.MarketFarmers)
                        .ThenInclude(link => link.Market);
    }

    private CartViewModel Map(IEnumerable<CartItem> items)
    {
        return new CartViewModel
        {
            Items = items.Select(item => new CartItemViewModel
            {
                ProductId = item.ProductId,
                ProductName = item.Product.Name,
                ImageUrl = item.Product.ImageUrl,
                Unit = item.Product.Unit.ToString(),
                UnitPrice = item.Product.Price,
                Quantity = item.Quantity,
                QuantityAvailable = item.Product.Inventory?.QuantityAvailable ?? 0,
                FarmerProfileId = item.Product.FarmerProfileId,
                FarmerName = item.Product.FarmerProfile.User.FirstName + " " + item.Product.FarmerProfile.User.LastName,
                FarmName = item.Product.FarmerProfile.FarmName,
                MarketName = item.Product.FarmerProfile.MarketFarmers.FirstOrDefault()?.Market.Name ?? string.Empty,
                IsAvailable = item.Product.IsAvailable,
                FarmerMarkets = item.Product.FarmerProfile.MarketFarmers
                    .Where(link => link.Market.IsActive)
                    .Select(link => new CartFarmMarket
                    {
                        MarketId = link.MarketId,
                        MarketName = link.Market.Name,
                        MarketDay = link.Market.OperatingDays
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .FirstOrDefault() ?? link.Market.OperatingDays
                    })
                    .OrderBy(link => link.MarketName, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            }).ToList()
        };
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

    private async Task MergeGuestCartAsync(string userId, CancellationToken cancellationToken)
    {
        var guestId = GetGuestId();
        var guestItems = await db.CartItems
            .Where(item => item.UserId == null && item.GuestId == guestId)
            .ToListAsync(cancellationToken);
        if (guestItems.Count == 0)
        {
            return;
        }

        var userItems = await db.CartItems
            .Where(item => item.UserId == userId)
            .ToDictionaryAsync(item => item.ProductId, cancellationToken);
        foreach (var guestItem in guestItems)
        {
            if (userItems.TryGetValue(guestItem.ProductId, out var userItem))
            {
                userItem.Quantity += guestItem.Quantity;
                userItem.UpdatedAt = DateTime.UtcNow;
                db.CartItems.Remove(guestItem);
            }
            else
            {
                guestItem.UserId = userId;
                guestItem.UpdatedAt = DateTime.UtcNow;
                userItems[guestItem.ProductId] = guestItem;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private string? GetUserId()
    {
        return httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated == true
            ? httpContextAccessor.HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            : null;
    }

    private string GetGuestId()
    {
        var httpContext = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("HttpContext is not available.");

        var existing = httpContext.Request.Cookies[GuestCartCookieName];
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var session = httpContext.Session;
        var fromSession = session.GetString("CartId");

        var guestId = string.IsNullOrWhiteSpace(fromSession) ? Guid.NewGuid().ToString("N") : fromSession;

        httpContext.Response.Cookies.Append(GuestCartCookieName, guestId, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = httpContext.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        });

        session.SetString("CartId", guestId);

        return guestId;
    }
}
