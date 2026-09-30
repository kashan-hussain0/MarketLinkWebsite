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
public sealed class OrderController : AdminControllerBase
{
    public OrderController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
        : base(db, userManager, roleManager)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] string? search,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var query = Db.Orders.AsNoTracking();
        var term = Clean(search);
        if (term.Length > 0)
        {
            var pattern = SearchPattern(term);
            query = query.Where(order => EF.Functions.Like(order.OrderNumber, pattern, "\\")
                || EF.Functions.Like(order.User.FirstName, pattern, "\\")
                || EF.Functions.Like(order.User.LastName, pattern, "\\")
                || EF.Functions.Like(order.User.Email!, pattern, "\\")
                || order.OrderItems.Any(item => EF.Functions.Like(item.FarmerName, pattern, "\\"))
                || order.OrderItems.Any(item => EF.Functions.Like(item.ProductName, pattern, "\\")));
        }

        var statusFilter = NormalizeStatusFilter(status);
        var selectedStatus = ParseStatus(statusFilter);
        if (selectedStatus is not null)
        {
            query = query.Where(order => order.Status == selectedStatus.Value);
        }

        var orders = await query
            .OrderByDescending(order => order.OrderDate)
            .Select(order => new OrderRecord
            {
                Id = order.Id,
                OrderNumber = order.OrderNumber,
                CustomerName = order.User.FirstName + " " + order.User.LastName,
                PlacedLabel = order.OrderDate.ToString("MMM d, yyyy, h:mm tt", CultureInfo.InvariantCulture),
                PickupLabel = order.PickupDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) + " · " + order.PickupTimeSlot,
                Total = order.TotalAmount,
                Status = order.Status,
                Payment = order.PaymentStatus,
                Farmers = string.Join(", ", order.OrderItems
                    .Select(item => item.FarmerName)
                    .Distinct()
                    .OrderBy(name => name)),
                Items = string.Join(", ", order.OrderItems
                    .Select(item => item.ProductName + " x" + item.Quantity)
                    .OrderBy(name => name))
            })
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var todayStart = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonth = monthStart.AddMonths(1);

        var dailyCounts = await Db.Orders
            .AsNoTracking()
            .Where(order => order.OrderDate >= todayStart.AddDays(-13))
            .GroupBy(order => order.OrderDate.Date)
            .Select(group => new DailyOrderCount
            {
                Day = group.Key,
                Count = group.Count()
            })
            .ToListAsync(cancellationToken);

        var model = new OrderListViewModel
        {
            SearchTerm = term,
            StatusFilter = statusFilter,
            TotalOrders = await Db.Orders.CountAsync(order => order.OrderDate >= monthStart && order.OrderDate < nextMonth, cancellationToken),
            AwaitingFulfillment = await Db.Orders.CountAsync(order => order.Status == OrderStatus.Pending || order.Status == OrderStatus.Accepted || order.Status == OrderStatus.Preparing || order.Status == OrderStatus.ReadyForPickup, cancellationToken),
            RevenueThisMonth = await Db.Orders.Where(order => order.OrderDate >= monthStart && order.OrderDate < nextMonth && order.PaymentStatus == PaymentStatus.Paid && order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Declined).SumAsync(order => (decimal?)order.TotalAmount, cancellationToken) ?? 0m,
            OrdersToday = await Db.Orders.CountAsync(order => order.OrderDate >= todayStart, cancellationToken),
            Orders = orders.Select(ToSummary).ToList()
        };

        var countsByDay = dailyCounts.ToDictionary(row => row.Day.Date, row => row.Count);
        model.DailyCounts = Enumerable.Range(0, 14)
            .Select(offset => todayStart.AddDays(offset - 13))
            .Select(day => new DailyOrderCount
            {
                Day = day,
                Count = countsByDay.TryGetValue(day.Date, out var count) ? count : 0
            })
            .ToList();

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest();
        }

        var order = await Db.Orders
            .AsNoTracking()
            .Include(item => item.User)
            .Include(item => item.Market)
            .Include(item => item.OrderItems)
            .Include(item => item.StatusHistory)
                .ThenInclude(item => item.ChangedBy)
            .FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        var history = order.StatusHistory.OrderBy(item => item.ChangedAt).ToList();
        var timeline = new List<OrderStatusEvent>();
        for (var index = 0; index < history.Count; index++)
        {
            var item = history[index];
            var description = item.Note;
            if (string.IsNullOrWhiteSpace(description))
            {
                description = item.ChangedBy is null ? "Status recorded." : $"Recorded by {item.ChangedBy.FirstName} {item.ChangedBy.LastName}.";
            }
            timeline.Add(new OrderStatusEvent
            {
                Title = OrderStatusLabel(item.Status),
                TimeLabel = item.ChangedAt.ToString("MMM d, yyyy, h:mm tt", CultureInfo.InvariantCulture),
                Description = description,
                IsComplete = true,
                IsCurrent = index == history.Count - 1
            });
        }
        if (timeline.Count == 0)
        {
            timeline.Add(new OrderStatusEvent
            {
                Title = "Order placed",
                TimeLabel = order.OrderDate.ToString("MMM d, yyyy, h:mm tt", CultureInfo.InvariantCulture),
                Description = "The order was placed successfully and is waiting for the farmer to respond.",
                IsComplete = true,
                IsCurrent = true
            });
        }

        var summary = ToSummary(new OrderRecord
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            CustomerName = $"{order.User.FirstName} {order.User.LastName}",
            PlacedLabel = order.OrderDate.ToString("MMM d, yyyy, h:mm tt", CultureInfo.InvariantCulture),
            PickupLabel = order.PickupDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) + " · " + order.PickupTimeSlot,
            Total = order.TotalAmount,
            Status = order.Status,
            Payment = order.PaymentStatus,
            Farmers = string.Join(", ", order.OrderItems.Select(item => item.FarmerName).Distinct().OrderBy(name => name)),
            Items = string.Join(", ", order.OrderItems.Select(item => item.ProductName + " x" + item.Quantity).OrderBy(name => name))
        });

        var model = new OrderDetailsViewModel
        {
            Order = summary,
            ShippingAddress = $"{order.PickupAddress}\n{order.City}",
            PaymentMethod = order.PaymentMethod,
            MarketName = order.Market.Name,
            CustomerEmail = order.User.Email ?? string.Empty,
            CustomerPhone = order.Phone,
            Notes = order.Notes,
            Items = order.OrderItems
                .OrderBy(item => item.FarmerName)
                .ThenBy(item => item.ProductName)
                .Select(item => new OrderLineItem
                {
                    ProductName = item.ProductName,
                    Variant = item.FarmerName,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    LineTotal = item.SubTotal
                })
                .ToList(),
            Timeline = timeline
        };
        return View(model);
    }

    private static OrderSummary ToSummary(OrderRecord order)
    {
        var statusLabel = OrderStatusLabel(order.Status);
        var paymentLabel = PaymentStatusLabel(order.Payment);
        var firstName = order.CustomerName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var lastName = order.CustomerName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return new OrderSummary
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            CustomerName = order.CustomerName,
            CustomerInitials = Initials(firstName, lastName),
            CustomerTone = AvatarTone(order.CustomerName),
            PlacedLabel = order.PlacedLabel,
            DeliveryLabel = order.PickupLabel,
            ItemCount = order.Items.Split(',').Length,
            Total = order.Total,
            PaymentStatus = paymentLabel,
            PaymentTone = StatusTone(paymentLabel),
            FulfillmentStatus = statusLabel,
            FulfillmentTone = StatusTone(statusLabel),
            Status = order.Status,
            Payment = order.Payment,
            Farmers = order.Farmers,
            Items = order.Items
        };
    }

    private static string NormalizeStatusFilter(string? status)
    {
        var value = Clean(status);
        return ParseStatus(value) is null && !value.Equals("All orders", StringComparison.OrdinalIgnoreCase) ? "All orders" : value;
    }

    private static OrderStatus? ParseStatus(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "pending" => OrderStatus.Pending,
            "accepted" => OrderStatus.Accepted,
            "preparing" or "processing" => OrderStatus.Preparing,
            "ready for pickup" => OrderStatus.ReadyForPickup,
            "completed" => OrderStatus.Completed,
            "declined" => OrderStatus.Declined,
            "cancelled" => OrderStatus.Cancelled,
            _ => null
        };
    }
}
