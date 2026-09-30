using System.ComponentModel.DataAnnotations;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using MarketLinkWebsite.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Controllers;

[Authorize(Policy = "CustomerOnly")]
public sealed class CustomerOrderController : Controller
{
    private readonly ApplicationDbContext db;

    public CustomerOrderController(ApplicationDbContext db)
    {
        this.db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var customer = await GetActiveCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return RedirectToAction(nameof(AccountController.Login), "Account");
        }

        var order = await db.Orders
            .AsNoTracking()
            .Where(item => item.Id == id && item.UserId == customer.Id)
            .Include(item => item.Market)
            .Include(item => item.OrderItems)
            .ThenInclude(item => item.Product)
                .ThenInclude(product => product!.Inventory)
            .FirstOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        if (order.Status is not (OrderStatus.Pending or OrderStatus.Accepted))
        {
            TempData["Error"] = "Only new or accepted reservations can be changed.";
            return RedirectToAction(nameof(CustomerController.OrderDetails), "Customer", new { id });
        }

        var markets = await db.Markets
            .AsNoTracking()
            .Where(market => market.IsActive)
            .OrderBy(market => market.Name)
            .Select(market => new CustomerOrderEditMarket
            {
                Id = market.Id,
                Name = market.Name,
                City = market.City,
                Address = market.Address,
                OperatingDays = market.OperatingDays
            })
            .ToListAsync(cancellationToken);

        var model = new CustomerOrderEditViewModel
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            MarketId = order.MarketId,
            PickupDate = order.PickupDate,
            PickupTimeSlot = order.PickupTimeSlot,
            PickupAddress = order.PickupAddress,
            City = order.City,
            Phone = order.Phone,
            Notes = order.Notes,
            Markets = markets,
            Lines = order.OrderItems.Select(item =>
            {
                var free = item.Product?.Inventory?.QuantityAvailable ?? 0;
                return new CustomerOrderEditLine
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    FarmerName = item.FarmerName,
                    Unit = item.Unit.ToString().ToLowerInvariant(),
                    UnitPrice = item.UnitPrice,
                    Quantity = item.Quantity,
                    InStock = free,
                    StockLabel = free > 0
                        ? $"{free} in stock, {item.Quantity} reserved for you"
                        : "No extra stock is available right now",
                    CanIncrease = free > 0,
                    MaxQuantity = free + item.Quantity
                };
            }).ToList()
        };

        ViewData["Title"] = "Edit reservation";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CustomerOrderEditViewModel model, CancellationToken cancellationToken)
    {
        var customer = await GetActiveCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return RedirectToAction(nameof(AccountController.Login), "Account");
        }

        var order = await db.Orders
            .Include(item => item.OrderItems)
            .ThenInclude(item => item.Product)
                .ThenInclude(product => product!.Inventory)
            .Include(item => item.Market)
            .FirstOrDefaultAsync(item => item.Id == model.Id && item.UserId == customer.Id, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        if (order.Status is not (OrderStatus.Pending or OrderStatus.Accepted))
        {
            TempData["Error"] = "Only new or accepted reservations can be changed.";
            return RedirectToAction(nameof(CustomerController.OrderDetails), "Customer", new { id = order.Id });
        }

        var markets = await db.Markets
            .AsNoTracking()
            .Where(market => market.IsActive)
            .OrderBy(market => market.Name)
            .Select(market => new CustomerOrderEditMarket
            {
                Id = market.Id,
                Name = market.Name,
                City = market.City,
                Address = market.Address,
                OperatingDays = market.OperatingDays
            })
            .ToListAsync(cancellationToken);

        model.Markets = markets;
        model.OrderNumber = order.OrderNumber;

        var requested = new Dictionary<int, int>();
        foreach (var line in model.Lines)
        {
            if (line.ProductId is int productId && productId > 0)
            {
                requested[productId] = Math.Max(0, line.Quantity);
            }
        }

        if (requested.Count == 0 || requested.Values.All(quantity => quantity == 0))
        {
            ModelState.AddModelError(string.Empty, "Keep at least one item in your reservation.");
        }

        var market = markets.FirstOrDefault(item => item.Id == model.MarketId);
        if (market is null)
        {
            ModelState.AddModelError(nameof(model.MarketId), "Choose a pickup market.");
        }
        else if (!market.OperatingDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(model.PickupDate.DayOfWeek.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(model.PickupDate), $"{market.Name} operates on {market.OperatingDays}.");
        }

        if (string.IsNullOrWhiteSpace(model.PickupTimeSlot))
        {
            ModelState.AddModelError(nameof(model.PickupTimeSlot), "Choose a pickup slot.");
        }

        if (string.IsNullOrWhiteSpace(model.Phone))
        {
            ModelState.AddModelError(nameof(model.Phone), "A contact number is required.");
        }

        var productIds = requested.Keys.Where(id => id > 0).ToList();
        var inventories = await db.Inventories
            .Where(inventory => productIds.Contains(inventory.ProductId))
            .ToDictionaryAsync(inventory => inventory.ProductId, cancellationToken);

        foreach (var pair in requested)
        {
            if (inventories.TryGetValue(pair.Key, out var inventory))
            {
                var line = order.OrderItems.FirstOrDefault(item => item.ProductId == pair.Key);
                var alreadyReserved = line?.Quantity ?? 0;
                var available = inventory.QuantityAvailable + alreadyReserved;

                if (pair.Value > available)
                {
                    ModelState.AddModelError(string.Empty, $"Only {available} units of that item are available.");
                }
            }
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var linesToRemove = new List<OrderItem>();
        foreach (var line in order.OrderItems)
        {
            if (line.ProductId is not int productId || !requested.TryGetValue(productId, out var quantity))
            {
                continue;
            }

            if (inventories.TryGetValue(productId, out var inventory))
            {
                inventory.QuantityAvailable += line.Quantity - quantity;
                inventory.LastUpdated = DateTime.UtcNow;
            }

            if (quantity == 0)
            {
                linesToRemove.Add(line);
            }
            else
            {
                line.Quantity = quantity;
            }
        }

        foreach (var line in linesToRemove)
        {
            order.OrderItems.Remove(line);
            db.OrderItems.Remove(line);
        }

        order.MarketId = model.MarketId;
        order.PickupDate = model.PickupDate.Date;
        order.PickupTimeSlot = model.PickupTimeSlot.Trim();
        order.PickupAddress = string.IsNullOrWhiteSpace(model.PickupAddress) ? market!.Address : model.PickupAddress.Trim();
        order.City = string.IsNullOrWhiteSpace(model.City) ? market!.City : model.City.Trim();
        order.Phone = model.Phone.Trim();
        order.Notes = model.Notes?.Trim() ?? string.Empty;
        order.TotalAmount = order.OrderItems.Sum(item => item.Quantity * item.UnitPrice);
        order.UpdatedAt = DateTime.UtcNow;

        order.StatusHistory.Add(new OrderStatusHistory
        {
            Status = order.Status,
            ChangedById = customer.Id,
            Note = "The customer changed the reservation details.",
            ChangedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);

        db.Notifications.Add(new Notification
        {
            UserId = customer.Id,
            Type = NotificationType.Order,
            Title = $"Reservation {order.OrderNumber} updated",
            Message = "Your pickup details were changed and the farmers have been notified.",
            ActionUrl = $"/Customer/OrderDetails/{order.Id}",
            CreatedAt = DateTime.UtcNow
        });

        foreach (var farmerUserId in FarmerUserIdsFor(order))
        {
            db.Notifications.Add(new Notification
            {
                UserId = farmerUserId,
                Type = NotificationType.Order,
                Title = $"Reservation {order.OrderNumber} changed",
                Message = "The customer updated the items or pickup window. Review the order to confirm.",
                ActionUrl = $"/Farmer/Order/Details/{order.Id}",
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "Your reservation has been updated.";
        return RedirectToAction(nameof(CustomerController.OrderDetails), "Customer", new { id = order.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, CancellationToken cancellationToken)
    {
        var customer = await GetActiveCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return RedirectToAction(nameof(AccountController.Login), "Account");
        }

        var order = await db.Orders
            .Include(item => item.OrderItems)
            .Include(item => item.OrderItems).ThenInclude(item => item.Product)
            .FirstOrDefaultAsync(item => item.Id == id && item.UserId == customer.Id, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        if (order.Status != OrderStatus.Pending)
        {
            TempData["Error"] = order.Status == OrderStatus.Cancelled
                ? "This reservation is already cancelled."
                : "Your farmer has already accepted this reservation, so it can no longer be cancelled online. Please contact the market if you need help.";
            return RedirectToAction(nameof(CustomerController.OrderDetails), "Customer", new { id });
        }

        var orderProductIds = order.OrderItems
            .Where(line => line.ProductId != null)
            .Select(line => line.ProductId!.Value)
            .ToList();

        var inventories = await db.Inventories
            .Where(inventory => orderProductIds.Contains(inventory.ProductId))
            .ToDictionaryAsync(inventory => inventory.ProductId, cancellationToken);

        foreach (var line in order.OrderItems)
        {
            if (line.ProductId is int productId && inventories.TryGetValue(productId, out var inventory))
            {
                inventory.QuantityAvailable += line.Quantity;
                inventory.LastUpdated = DateTime.UtcNow;
            }
        }

        order.Status = OrderStatus.Cancelled;
        order.PaymentStatus = PaymentStatus.Refunded;
        order.CancelledAt = DateTime.UtcNow;
        order.CancellationReason = "Cancelled by the customer.";
        order.UpdatedAt = DateTime.UtcNow;

        order.StatusHistory.Add(new OrderStatusHistory
        {
            Status = OrderStatus.Cancelled,
            ChangedById = customer.Id,
            Note = "The customer cancelled this reservation and the stock was returned.",
            ChangedAt = DateTime.UtcNow
        });

        db.Notifications.Add(new Notification
        {
            UserId = customer.Id,
            Type = NotificationType.Order,
            Title = $"Reservation {order.OrderNumber} cancelled",
            Message = "Your reservation was cancelled and the reserved items were released back to the farmers.",
            ActionUrl = $"/Customer/OrderDetails/{order.Id}",
            CreatedAt = DateTime.UtcNow
        });

        foreach (var farmerUserId in FarmerUserIdsFor(order))
        {
            db.Notifications.Add(new Notification
            {
                UserId = farmerUserId,
                Type = NotificationType.Order,
                Title = $"Reservation {order.OrderNumber} cancelled",
                Message = "The customer cancelled this order. The items are available again.",
                ActionUrl = $"/Farmer/Order/Details/{order.Id}",
                CreatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "Your reservation has been cancelled and the items were released.";
        return RedirectToAction(nameof(CustomerController.OrderDetails), "Customer", new { id });
    }

    private List<string> FarmerUserIdsFor(Order order)
    {
        var farmNames = order.OrderItems.Select(item => item.FarmerName).Distinct().ToList();

        return db.FarmerProfiles
            .Where(profile => farmNames.Contains(profile.FarmName))
            .Select(profile => profile.UserId)
            .ToList();
    }

    private async Task<ApplicationUser?> GetActiveCustomerAsync(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return await db.Users.FirstOrDefaultAsync(user => user.Id == userId && user.IsActive, cancellationToken);
    }
}

public sealed class CustomerOrderEditViewModel
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    [Required(ErrorMessage = "Choose the pickup market.")]
    public int MarketId { get; set; }
    public DateTime PickupDate { get; set; } = DateTime.UtcNow.Date.AddDays(1);
    [Required(ErrorMessage = "Choose a pickup time.")]
    [StringLength(40, ErrorMessage = "Pickup time must be 40 characters or fewer.")]
    public string PickupTimeSlot { get; set; } = string.Empty;

    [Required]
    [MaxLength(250)]
    [StreetAddress]
    public string PickupAddress { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [BusinessName]
    public string City { get; set; } = string.Empty;

    [Required]
    [Phone]
    [PhoneNumber]
    public string Phone { get; set; } = string.Empty;

    public string? Notes { get; set; }
    public List<CustomerOrderEditLine> Lines { get; set; } = new();
    public List<CustomerOrderEditMarket> Markets { get; set; } = new();
}

public sealed class CustomerOrderEditLine
{
    public int? ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string FarmerName { get; set; } = string.Empty;
    public string Unit { get; set; } = "piece";
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public int MaxQuantity { get; set; }
    public int InStock { get; set; }
    public bool CanIncrease { get; set; }
    public string StockLabel { get; set; } = string.Empty;
}

public sealed class CustomerOrderEditMarket
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string OperatingDays { get; set; } = string.Empty;
}
