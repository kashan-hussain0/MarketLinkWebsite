using System.Globalization;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using MarketLinkWebsite.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Areas.Farmer.Controllers;

[Area("Farmer")]
[Authorize(Policy = "ActiveFarmerAccess")]
public sealed class OrderController : FarmerControllerBase
{
    private readonly OrderEmailDispatcher orderEmails;

    public OrderController(ApplicationDbContext db, OrderEmailDispatcher orderEmails) : base(db)
    {
        this.orderEmails = orderEmails;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? status, CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);
        var orders = await OwnedOrders(profile)
            .AsNoTracking()
            .Include(order => order.User)
            .Include(order => order.Market)
            .Include(order => order.OrderItems)
                .ThenInclude(item => item.Product)
            .OrderByDescending(order => order.OrderDate)
            .ToListAsync(cancellationToken);

        var summaries = orders.Select(order => MapSummary(order, profile)).ToList();
        var normalizedStatus = NormalizeStatusFilter(status);
        var filteredOrders = normalizedStatus is null
            ? summaries
            : summaries.Where(order => order.StatusValue == normalizedStatus).ToList();

        var model = new OrderListViewModel
        {
            StatusFilter = GetStatusFilterLabel(normalizedStatus),
            TotalOrders = summaries.Count,
            NewOrders = summaries.Count(order => order.StatusValue == nameof(OrderStatus.Pending)),
            AcceptedOrders = summaries.Count(order => order.StatusValue == nameof(OrderStatus.Accepted)),
            ToPrepare = summaries.Count(order => order.StatusValue == nameof(OrderStatus.Preparing)),
            ReadyForPickup = summaries.Count(order => order.StatusValue == nameof(OrderStatus.ReadyForPickup)),
            CompletedOrders = summaries.Count(order => order.StatusValue == nameof(OrderStatus.Completed)),
            DeclinedOrders = summaries.Count(order => order.StatusValue is nameof(OrderStatus.Declined) or nameof(OrderStatus.Cancelled)),
            Notice = Request.Query["notice"].FirstOrDefault(),
            Orders = filteredOrders
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int? id, CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);
        if (id is not > 0)
        {
            return NotFound();
        }

        var order = await OwnedOrders(profile)
            .AsNoTracking()
            // OrderItems and StatusHistory are both collections, so a split query
            // keeps the join from multiplying the rows.
            .AsSplitQuery()
            .Include(item => item.User)
            .Include(item => item.Market)
            .Include(item => item.OrderItems)
                .ThenInclude(line => line.Product)
            .Include(item => item.StatusHistory)
                .ThenInclude(history => history.ChangedBy)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        var model = MapDetail(order, profile);
        model.Notice = Request.Query["notice"].FirstOrDefault();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UpdateStatus(int id, string? status, string? note = null, CancellationToken cancellationToken = default)
    {
        var target = ParseStatus(status);
        if (target is null)
        {
            return Task.FromResult<IActionResult>(RedirectToAction(nameof(Details), new { area = "Farmer", id, notice = "Choose a supported order status." }));
        }

        return ChangeStatusAsync(id, target.Value, note, cancellationToken);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Accept(int id, CancellationToken cancellationToken = default)
    {
        return ChangeStatusAsync(id, OrderStatus.Accepted, "Order accepted by the farmer.", cancellationToken);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Decline(int id, string? reason = null, CancellationToken cancellationToken = default)
    {
        return ChangeStatusAsync(id, OrderStatus.Declined, reason, cancellationToken);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Ready(int id, CancellationToken cancellationToken = default)
    {
        return ChangeStatusAsync(id, OrderStatus.ReadyForPickup, "Order is ready for pickup.", cancellationToken);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Prepare(int id, CancellationToken cancellationToken = default)
    {
        return ChangeStatusAsync(id, OrderStatus.Preparing, "Order moved into preparation.", cancellationToken);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Complete(int id, CancellationToken cancellationToken = default)
    {
        return ChangeStatusAsync(id, OrderStatus.Completed, "Order marked completed and paid at the stall.", cancellationToken);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string? reason = null, CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);
        if (id <= 0)
        {
            return NotFound();
        }

        var order = await OwnedOrders(profile)
            .Include(item => item.User)
            .Include(item => item.OrderItems)
                .ThenInclude(line => line.Product)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status is OrderStatus.Completed or OrderStatus.Cancelled)
        {
            return RedirectToAction(nameof(Details), new { area = "Farmer", id, notice = $"This order is already {GetStatusLabel(order.Status).ToLowerInvariant()}." });
        }

        var cleanReason = string.IsNullOrWhiteSpace(reason)
            ? "The farmer could not fulfil this reservation."
            : reason.Trim();
        if (cleanReason.Length > 300)
        {
            cleanReason = cleanReason[..300];
        }

        await RestoreInventoryAsync(order, profile, cancellationToken);

        order.Status = OrderStatus.Cancelled;
        order.PaymentStatus = PaymentStatus.Refunded;
        order.CancelledAt = DateTime.UtcNow;
        order.CancellationReason = $"Cancelled by {profile.FarmName}: {cleanReason}";
        order.UpdatedAt = DateTime.UtcNow;

        order.StatusHistory.Add(new OrderStatusHistory
        {
            Status = OrderStatus.Cancelled,
            ChangedById = profile.UserId,
            Note = cleanReason,
            ChangedAt = DateTime.UtcNow
        });

        Db.Notifications.Add(new Notification
        {
            UserId = order.UserId,
            Type = NotificationType.Order,
            Title = $"Your order {order.OrderNumber} has been cancelled",
            Message = $"{profile.FarmName} could not fulfil this reservation. Reason: {cleanReason} Any reserved items have been released back to the farm.",
            ActionUrl = $"/Customer/OrderDetails/{order.Id}",
            CreatedAt = DateTime.UtcNow
        });

        AddAudit("Cancel order", nameof(Order), order.Id, $"Order {order.OrderNumber} cancelled by {profile.FarmName}. Reason: {cleanReason}");
        await Db.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Details), new { area = "Farmer", id, notice = "Order cancelled and the customer has been notified." });
    }

    private async Task<IActionResult> ChangeStatusAsync(int id, OrderStatus target, string? note, CancellationToken cancellationToken)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);
        if (id <= 0)
        {
            return NotFound();
        }

        var order = await OwnedOrders(profile)
            .Include(item => item.User)
            .Include(item => item.OrderItems)
                .ThenInclude(line => line.Product)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status == target)
        {
            return RedirectToAction(nameof(Details), new { area = "Farmer", id, notice = $"Order is already {GetStatusLabel(target).ToLowerInvariant()}." });
        }

        if (!CanTransition(order.Status, target))
        {
            return RedirectToAction(nameof(Details), new { area = "Farmer", id, notice = $"An order cannot move from {GetStatusLabel(order.Status).ToLowerInvariant()} to {GetStatusLabel(target).ToLowerInvariant()}." });
        }

        var requiresCutoffCheck = target is OrderStatus.Accepted or OrderStatus.Preparing or OrderStatus.ReadyForPickup;
        if (requiresCutoffCheck && IsPastCutoff(order, profile.OrderCutoffHours))
        {
            return RedirectToAction(nameof(Details), new { area = "Farmer", id, notice = "The farmer pickup cutoff has passed for this order. Decline it or contact the customer." });
        }

        if (target == OrderStatus.Declined)
        {
            await RestoreInventoryAsync(order, profile, cancellationToken);
        }

        order.Status = target;
        order.UpdatedAt = DateTime.UtcNow;
        if (target == OrderStatus.Completed)
        {
            order.PaymentStatus = PaymentStatus.Paid;
        }

        var historyNote = string.IsNullOrWhiteSpace(note)
            ? $"Status changed to {GetStatusLabel(target).ToLowerInvariant()}."
            : note.Trim();
        if (historyNote.Length > 300)
        {
            historyNote = historyNote[..300];
        }

        order.StatusHistory.Add(new OrderStatusHistory
        {
            Status = target,
            ChangedById = profile.UserId,
            Note = historyNote,
            ChangedAt = DateTime.UtcNow
        });

        var customerId = order.UserId;
        var actionTitle = target switch
        {
            OrderStatus.Accepted => "Your farmer accepted an order",
            OrderStatus.Preparing => "Your order is being prepared",
            OrderStatus.ReadyForPickup => "Your order is ready for pickup",
            OrderStatus.Completed => "Your order is complete",
            OrderStatus.Declined => "Your farmer declined an order",
            _ => "Your order status changed"
        };
        QueueNotification(customerId, NotificationType.Order, actionTitle, $"{order.OrderNumber} is now {GetStatusLabel(target).ToLowerInvariant()}.", $"/Customer/OrderDetails/{order.Id}");

        await Db.SaveChangesAsync(cancellationToken);

        // The in-app alert above is the record of what happened. The email is a
        // courtesy on top, sent only once the change is safely stored.
        await orderEmails.SendStatusChangedAsync(order.Id, target, cancellationToken);

        return RedirectToAction(nameof(Details), new { area = "Farmer", id, notice = $"Order marked {GetStatusLabel(target).ToLowerInvariant()}." });
    }

    private async Task RestoreInventoryAsync(Order order, FarmerProfile profile, CancellationToken cancellationToken)
    {
        var ownedProductIds = order.OrderItems
            .Where(item => item.Product is not null && item.Product.FarmerProfileId == profile.Id && item.ProductId.HasValue)
            .Select(item => item.ProductId!.Value)
            .ToList();
        if (ownedProductIds.Count == 0)
        {
            return;
        }

        var inventories = await Db.Inventories
            .Include(inventory => inventory.Product)
            .Where(inventory => ownedProductIds.Contains(inventory.ProductId) && inventory.Product != null && inventory.Product.FarmerProfileId == profile.Id)
            .ToListAsync(cancellationToken);
        var inventoryByProduct = inventories.ToDictionary(inventory => inventory.ProductId);
        foreach (var item in order.OrderItems.Where(item => item.ProductId.HasValue && item.Product?.FarmerProfileId == profile.Id))
        {
            if (inventoryByProduct.TryGetValue(item.ProductId!.Value, out var inventory))
            {
                var updatedAfterOrder = inventory.Product.UpdatedAt > order.OrderDate;
                inventory.QuantityAvailable += item.Quantity;
                inventory.LastUpdated = DateTime.UtcNow;
                inventory.Product.UpdatedAt = DateTime.UtcNow;
                if (!updatedAfterOrder)
                {
                    inventory.Product.IsAvailable = inventory.QuantityAvailable > 0;
                }
            }
        }
    }

    private static FarmerOrderSummaryViewModel MapSummary(Order order, FarmerProfile profile)
    {
        var ownedItems = order.OrderItems
            .Where(item => item.Product is not null && item.Product.FarmerProfileId == profile.Id)
            .ToList();
        var ownSubtotal = ownedItems.Sum(item => item.SubTotal);
        var customerName = GetCustomerName(order.User);
        return new FarmerOrderSummaryViewModel
        {
            Id = order.Id,
            OrderNumber = FormatOrderNumber(order),
            CustomerName = customerName,
            CustomerInitials = GetInitials(customerName),
            CustomerLocation = order.Market?.Name ?? order.City,
            ItemSummary = string.Join(", ", ownedItems.Select(item => item.ProductName).Take(2)) + (ownedItems.Count > 2 ? $" +{ownedItems.Count - 2} more" : string.Empty),
            ItemCount = ownedItems.Sum(item => item.Quantity),
            PlacedLabel = FormatRelativeTime(order.OrderDate),
            PickupWindow = FormatPickupWindow(order),
            Total = order.TotalAmount,
            OwnSubtotal = ownSubtotal,
            Status = GetStatusLabel(order.Status),
            StatusValue = order.Status.ToString(),
            StatusTone = GetStatusTone(order.Status),
            StatusIcon = GetStatusIcon(order.Status),
            CanAccept = order.Status == OrderStatus.Pending,
            CanPrepare = order.Status is OrderStatus.Pending or OrderStatus.Accepted,
            CanReady = order.Status is OrderStatus.Pending or OrderStatus.Accepted or OrderStatus.Preparing,
            CanComplete = order.Status == OrderStatus.ReadyForPickup,
            CanDecline = order.Status == OrderStatus.Pending,
            CanCancel = order.Status is OrderStatus.Accepted or OrderStatus.Preparing or OrderStatus.ReadyForPickup
        };
    }

    private static OrderDetailViewModel MapDetail(Order order, FarmerProfile profile)
    {
        var ownedItems = order.OrderItems
            .Where(item => item.Product is not null && item.Product.FarmerProfileId == profile.Id)
            .ToList();
        var ownSubtotal = ownedItems.Sum(item => item.SubTotal);
        var customerName = GetCustomerName(order.User);
        var model = new OrderDetailViewModel
        {
            Id = order.Id,
            OrderNumber = FormatOrderNumber(order),
            CustomerName = customerName,
            CustomerInitials = GetInitials(customerName),
            CustomerEmail = order.User?.Email ?? string.Empty,
            CustomerPhone = order.Phone,
            CustomerAddress = order.PickupAddress,
            PlacedLabel = FormatDate(order.OrderDate),
            PickupWindow = FormatPickupWindow(order),
            PickupLocation = order.Market?.Name ?? profile.FarmName,
            PickupAddress = !string.IsNullOrWhiteSpace(profile.Address) ? profile.Address : order.Market?.Address ?? order.PickupAddress,
            CutoffLabel = profile.OrderCutoffHours == 0 ? "Orders close at pickup time" : $"Orders close {profile.OrderCutoffHours} hours before pickup",
            PaymentLabel = GetPaymentLabel(order.PaymentStatus),
            Status = GetStatusLabel(order.Status),
            StatusValue = order.Status.ToString(),
            StatusTone = GetStatusTone(order.Status),
            StatusIcon = GetStatusIcon(order.Status),
            Subtotal = ownSubtotal,
            ServiceFee = Math.Max(0, order.TotalAmount - ownSubtotal),
            Total = order.TotalAmount,
            HasOtherFarmerItems = order.OrderItems.Any(item => item.Product is null || item.Product.FarmerProfileId != profile.Id),
            CanAccept = order.Status == OrderStatus.Pending,
            CanPrepare = order.Status is OrderStatus.Pending or OrderStatus.Accepted,
            CanReady = order.Status is OrderStatus.Pending or OrderStatus.Accepted or OrderStatus.Preparing,
            CanComplete = order.Status == OrderStatus.ReadyForPickup,
            CanDecline = order.Status == OrderStatus.Pending,
            CanCancel = order.Status is OrderStatus.Accepted or OrderStatus.Preparing or OrderStatus.ReadyForPickup,
            Items = ownedItems.Select(item => new FarmerOrderLineViewModel
            {
                ProductId = item.ProductId,
                Name = item.ProductName,
                Variant = item.Product is null ? "Farm item" : (item.Product.IsOrganic ? "Organic - " : string.Empty) + GetUnitLabel(item.Product.Unit),
                ImageUrl = string.IsNullOrWhiteSpace(item.Product?.ImageUrl) ? FarmFallbackImageUrl : item.Product.ImageUrl,
                Price = item.UnitPrice,
                Quantity = item.Quantity,
                Unit = GetUnitLabel(item.Product?.Unit ?? UnitType.Piece)
            }).ToList()
        };

        model.StatusHistory = order.StatusHistory
            .OrderBy(history => history.ChangedAt)
            .Select(history => new FarmerOrderHistoryViewModel
            {
                Status = GetStatusLabel(history.Status),
                StatusTone = GetStatusTone(history.Status),
                ChangedAtLabel = FormatDate(history.ChangedAt),
                Note = history.Note ?? string.Empty,
                ChangedBy = history.ChangedBy is null ? "MarketLink" : GetOwnerName(new FarmerProfile { User = history.ChangedBy, FarmName = string.Empty })
            }).ToList();
        return model;
    }

    private static string GetCustomerName(ApplicationUser? user)
    {
        var name = $"{user?.FirstName ?? string.Empty} {user?.LastName ?? string.Empty}".Trim();
        return string.IsNullOrWhiteSpace(name) ? "MarketLink customer" : name;
    }

    private static string FormatOrderNumber(Order order)
    {
        var value = string.IsNullOrWhiteSpace(order.OrderNumber) ? order.Id.ToString() : order.OrderNumber.Trim();
        return value.StartsWith('#') ? value : $"#{value}";
    }

    private static string FormatPickupWindow(Order order)
    {
        var date = order.PickupDate.ToLocalTime().ToString("MMM d");
        return string.IsNullOrWhiteSpace(order.PickupTimeSlot) ? date : $"{date} - {order.PickupTimeSlot}";
    }

    private static string GetPaymentLabel(PaymentStatus paymentStatus)
    {
        return paymentStatus switch
        {
            PaymentStatus.Paid => "Paid",
            PaymentStatus.Failed => "Payment failed",
            PaymentStatus.Refunded => "Refunded",
            _ => "Pay at pickup"
        };
    }

    private static bool CanTransition(OrderStatus current, OrderStatus target)
    {
        return (current, target) switch
        {
            (OrderStatus.Pending, OrderStatus.Accepted) => true,
            (OrderStatus.Pending, OrderStatus.Preparing) => true,
            (OrderStatus.Pending, OrderStatus.ReadyForPickup) => true,
            (OrderStatus.Pending, OrderStatus.Declined) => true,
            (OrderStatus.Accepted, OrderStatus.Preparing) => true,
            (OrderStatus.Accepted, OrderStatus.ReadyForPickup) => true,
            (OrderStatus.Preparing, OrderStatus.ReadyForPickup) => true,
            (OrderStatus.ReadyForPickup, OrderStatus.Completed) => true,
            _ => false
        };
    }

    private static OrderStatus? ParseStatus(string? status)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "new" or "pending" => OrderStatus.Pending,
            "accept" or "accepted" => OrderStatus.Accepted,
            "prepare" or "preparing" => OrderStatus.Preparing,
            "ready" or "ready for pickup" or "readyforpickup" or "ready_for_pickup" => OrderStatus.ReadyForPickup,
            "completed" => OrderStatus.Completed,
            "declined" => OrderStatus.Declined,
            "cancelled" or "canceled" => OrderStatus.Cancelled,
            _ => null
        };
    }

    private static string? NormalizeStatusFilter(string? status)
    {
        if (string.IsNullOrWhiteSpace(status) || status.Equals("All orders", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return ParseStatus(status) switch
        {
            OrderStatus.Pending => nameof(OrderStatus.Pending),
            OrderStatus.Accepted => nameof(OrderStatus.Accepted),
            OrderStatus.Preparing => nameof(OrderStatus.Preparing),
            OrderStatus.ReadyForPickup => nameof(OrderStatus.ReadyForPickup),
            OrderStatus.Completed => nameof(OrderStatus.Completed),
            OrderStatus.Declined => nameof(OrderStatus.Declined),
            OrderStatus.Cancelled => nameof(OrderStatus.Cancelled),
            _ => null
        };
    }

    private static string GetStatusFilterLabel(string? status)
    {
        return status switch
        {
            nameof(OrderStatus.Pending) => "New",
            nameof(OrderStatus.ReadyForPickup) => "Ready",
            null => "All orders",
            _ => GetStatusLabel(Enum.Parse<OrderStatus>(status))
        };
    }

    private static bool IsPastCutoff(Order order, int cutoffHours)
    {
        if (order.PickupDate == default || cutoffHours < 0)
        {
            return false;
        }

        var pickupStart = ResolvePickupStart(order);

        // The cutoff exists so a farmer is not surprised by an order they can no
        // longer prepare. Once the pickup window has begun the customer is already
        // on the way, so the order must stay workable.
        if (DateTime.Now >= pickupStart)
        {
            return false;
        }

        return DateTime.Now > pickupStart.AddHours(-cutoffHours);
    }

    private static DateTime ResolvePickupStart(Order order)
    {
        if (order.PickupDate == default)
        {
            return DateTime.Now;
        }

        var pickupStart = order.PickupDate.Date;
        var firstPart = order.PickupTimeSlot?
            .Split(new[] { '-', '-', ':' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(firstPart))
        {
            if (DateTime.TryParse(firstPart, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedTime))
            {
                pickupStart = pickupStart.Add(parsedTime.TimeOfDay);
            }
            else if (TimeSpan.TryParse(firstPart, CultureInfo.InvariantCulture, out var time))
            {
                pickupStart = pickupStart.Add(time);
            }
        }

        return pickupStart;
    }
}
