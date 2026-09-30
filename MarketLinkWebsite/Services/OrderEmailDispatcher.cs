using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Services;

/// <summary>
/// Sends the order emails. Every public method swallows its own failures, because
/// a shopper must never lose a pre-order to a mail server problem, and the
/// in-app notification is the record of what happened.
/// </summary>
public sealed class OrderEmailDispatcher
{
    private readonly ApplicationDbContext db;
    private readonly IEmailSender emailSender;
    private readonly OrderEmailComposer composer;
    private readonly ILogger<OrderEmailDispatcher> logger;

    public OrderEmailDispatcher(
        ApplicationDbContext db,
        IEmailSender emailSender,
        OrderEmailComposer composer,
        ILogger<OrderEmailDispatcher> logger)
    {
        this.db = db;
        this.emailSender = emailSender;
        this.composer = composer;
        this.logger = logger;
    }

    /// <summary>Confirms the pre-order to the customer and to every farm in it.</summary>
    public Task SendOrderPlacedAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return RunAsync(async order =>
        {
            await emailSender.SendAsync(
                order.CustomerEmail,
                $"Pre-order {order.OrderNumber} confirmed",
                composer.BuildCustomerOrderPlaced(order),
                cancellationToken);

            foreach (var farm in order.Farms.Where(item => !string.IsNullOrWhiteSpace(item.FarmerEmail)))
            {
                await emailSender.SendAsync(
                    farm.FarmerEmail,
                    $"New pre-order {order.OrderNumber} for {farm.FarmName}",
                    composer.BuildFarmerOrderPlaced(order, farm),
                    cancellationToken);
            }
        }, orderId, cancellationToken);
    }

    /// <summary>Tells the customer that the order moved to a new stage.</summary>
    public Task SendStatusChangedAsync(int orderId, OrderStatus status, CancellationToken cancellationToken = default)
    {
        if (status == OrderStatus.Pending)
        {
            // A brand new order already got its confirmation email.
            return Task.CompletedTask;
        }

        return RunAsync(async order =>
        {
            await emailSender.SendAsync(
                order.CustomerEmail,
                SubjectFor(order, status),
                composer.BuildOrderStatusChanged(order, status),
                cancellationToken);
        }, orderId, cancellationToken);
    }

    private string SubjectFor(OrderEmailContext order, OrderStatus status) => status switch
    {
        OrderStatus.Accepted => $"Pre-order {order.OrderNumber} accepted",
        OrderStatus.Preparing => $"Pre-order {order.OrderNumber} is being prepared",
        OrderStatus.ReadyForPickup => $"Pre-order {order.OrderNumber} is ready for pickup",
        OrderStatus.Completed => $"Pre-order {order.OrderNumber} collected",
        OrderStatus.Declined => $"Pre-order {order.OrderNumber} declined",
        OrderStatus.Cancelled => $"Pre-order {order.OrderNumber} cancelled",
        _ => $"Pre-order {order.OrderNumber} updated"
    };

    private async Task RunAsync(Func<OrderEmailContext, Task> action, int orderId, CancellationToken cancellationToken)
    {
        if (!emailSender.IsConfigured)
        {
            logger.LogInformation("No mail server is configured, so the order email for order {OrderId} was skipped.", orderId);
            return;
        }

        try
        {
            var context = await LoadAsync(orderId, cancellationToken);
            if (context is not null)
            {
                await action(context);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The order email for order {OrderId} could not be sent.", orderId);
        }
    }

    private async Task<OrderEmailContext?> LoadAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(item => item.User)
            .Include(item => item.Market)
            .Include(item => item.OrderItems)
            .AsSplitQuery()
            .FirstOrDefaultAsync(item => item.Id == orderId, cancellationToken);

        if (order is null)
        {
            logger.LogWarning("Order {OrderId} was not found, so its email was skipped.", orderId);
            return null;
        }

        // A guest checkout has no account to write to, so there is nobody to mail.
        if (string.IsNullOrWhiteSpace(order.User?.Email))
        {
            return null;
        }

        // A line keeps its product name even after a farmer removes the product, so
        // the farm details are read by id rather than walked through the navigation.
        var productIds = order.OrderItems
            .Where(item => item.ProductId.HasValue)
            .Select(item => item.ProductId!.Value)
            .Distinct()
            .ToList();

        var productRows = await db.Products
            .AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .Select(product => new
            {
                product.Id,
                product.Name,
                product.FarmerProfileId,
                FarmName = product.FarmerProfile.FarmName,
                FarmerEmail = product.FarmerProfile.User.Email
            })
            .ToListAsync(cancellationToken);

        var byProductId = productRows.ToDictionary(row => row.Id);

        var farms = order.OrderItems
            .Where(item => item.ProductId.HasValue && byProductId.ContainsKey(item.ProductId!.Value))
            .GroupBy(item => byProductId[item.ProductId!.Value].FarmerProfileId)
            .Select(group =>
            {
                var farm = byProductId[group.First().ProductId!.Value];

                return new FarmEmailGroup
                {
                    FarmName = farm.FarmName,
                    FarmerEmail = farm.FarmerEmail ?? string.Empty,
                    Lines = group
                        .Select(item =>
                        {
                            var product = byProductId[item.ProductId!.Value];
                            return new ProductEmailLine(product.Name, item.Quantity, item.UnitPrice);
                        })
                        .OrderBy(line => line.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                };
            })
            .OrderBy(farm => farm.FarmName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new OrderEmailContext
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            CustomerName = order.User.FirstName is { Length: > 0 } first ? first : order.User.UserName ?? "there",
            CustomerEmail = order.User.Email!,
            MarketName = order.Market?.Name ?? "the market",
            PickupAddress = order.PickupAddress,
            PickupCity = order.City,
            PickupDate = order.PickupDate,
            PickupSlot = order.PickupTimeSlot,
            OrderDate = order.OrderDate,
            Total = order.TotalAmount,
            Farms = farms
        };
    }
}
