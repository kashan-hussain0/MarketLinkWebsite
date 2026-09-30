using System.Globalization;
using System.Text;
using MarketLinkWebsite.Models.Enums;
using Microsoft.AspNetCore.Hosting;

namespace MarketLinkWebsite.Services;

/// <summary>
/// The handful of order facts an email needs. Building it in the caller keeps this
/// service free of entity navigation rules, and keeps the wording in one place so
/// the same order always reads the same way.
/// </summary>
public sealed record OrderEmailContext
{
    public int OrderId { get; init; }

    public string OrderNumber { get; init; } = string.Empty;

    public string CustomerName { get; init; } = string.Empty;

    public string CustomerEmail { get; init; } = string.Empty;

    public string MarketName { get; init; } = string.Empty;

    public string PickupAddress { get; init; } = string.Empty;

    public string PickupCity { get; init; } = string.Empty;

    public DateTime PickupDate { get; init; }

    public string PickupSlot { get; init; } = string.Empty;

    public DateTime OrderDate { get; init; }

    public decimal Total { get; init; }

    /// <summary>Every line on the order, already grouped by the farm that grew it.</summary>
    public IReadOnlyList<FarmEmailGroup> Farms { get; init; } = [];
}

public sealed record FarmEmailGroup
{
    public string FarmName { get; init; } = string.Empty;

    public string FarmerEmail { get; init; } = string.Empty;

    public IReadOnlyList<ProductEmailLine> Lines { get; init; } = [];
}

public sealed record ProductEmailLine(string Name, int Quantity, decimal UnitPrice);

/// <summary>
/// Builds the plain text messages customers and farmers receive when a pre-order
/// is placed or its status changes.
/// </summary>
public sealed class OrderEmailComposer
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    private readonly IConfiguration configuration;
    private readonly IWebHostEnvironment environment;

    public OrderEmailComposer(IConfiguration configuration, IWebHostEnvironment environment)
    {
        this.configuration = configuration;
        this.environment = environment;
    }

    public string PlatformName => configuration["Platform:Name"] ?? "MarketLink";

    public string BuildCustomerOrderPlaced(OrderEmailContext order)
    {
        var message = new StringBuilder();
        message.AppendLine($"Hello {order.CustomerName},");
        message.AppendLine();
        message.AppendLine($"Your {PlatformName} pre-order is confirmed. The farms will review it shortly.");
        message.AppendLine();
        message.AppendLine($"Order {order.OrderNumber}, placed {FormatDate(order.OrderDate)}");
        message.AppendLine();
        AppendFarms(message, order);
        message.AppendLine($"Total to pay at the stall: {order.Total.ToString("C", Culture)}");
        message.AppendLine();
        AppendCollection(message, order);
        message.AppendLine("You pay at the stall when you collect, so there is nothing to pay now.");
        message.AppendLine();
        message.AppendLine($"Track it here: {OrderUrl(order)}");
        message.AppendLine();
        message.AppendLine($"- The {PlatformName} team");
        return message.ToString();
    }

    public string BuildFarmerOrderPlaced(OrderEmailContext order, FarmEmailGroup farm)
    {
        var message = new StringBuilder();
        message.AppendLine($"Hello {farm.FarmName},");
        message.AppendLine();
        message.AppendLine($"A new pre-order has arrived on {PlatformName} and includes your produce.");
        message.AppendLine();
        message.AppendLine($"Order {order.OrderNumber} from {order.CustomerName}");
        message.AppendLine($"Placed {FormatDate(order.OrderDate)}");
        message.AppendLine();
        AppendLines(message, farm.Lines);
        AppendCollection(message, order);
        message.AppendLine("Accept or decline it from your orders page.");
        message.AppendLine();
        message.AppendLine($"- The {PlatformName} team");
        return message.ToString();
    }

    public string BuildOrderStatusChanged(OrderEmailContext order, OrderStatus status)
    {
        var message = new StringBuilder();
        message.AppendLine($"Hello {order.CustomerName},");
        message.AppendLine();
        message.AppendLine(HeadlineFor(order, status));
        message.AppendLine();
        message.AppendLine($"Order {order.OrderNumber}, placed {FormatDate(order.OrderDate)}");
        message.AppendLine($"Total to pay at the stall: {order.Total.ToString("C", Culture)}");
        message.AppendLine();
        AppendCollection(message, order);
        message.AppendLine($"Details: {OrderUrl(order)}");
        message.AppendLine();
        message.AppendLine($"- The {PlatformName} team");
        return message.ToString();
    }

    private string HeadlineFor(OrderEmailContext order, OrderStatus status)
    {
        var farms = order.Farms.Count == 1
            ? order.Farms[0].FarmName
            : $"{string.Join(", ", order.Farms.Take(order.Farms.Count - 1))} and {order.Farms[^1].FarmName}";

        return status switch
        {
            OrderStatus.Accepted => $"{farms} has accepted your pre-order. It is being prepared for collection at {order.MarketName}.",
            OrderStatus.Preparing => $"Your pre-order is being prepared for collection at {order.MarketName}.",
            OrderStatus.ReadyForPickup => $"Your pre-order is ready. Please collect it from {order.MarketName} in your chosen slot.",
            OrderStatus.Completed => $"Your pre-order has been collected. Thank you for shopping with {PlatformName}.",
            OrderStatus.Declined => $"The farm could not take part of your pre-order. Anything that had been held has been released back to the stall.",
            OrderStatus.Cancelled => $"Your pre-order has been cancelled. Any amount already paid has been returned.",
            _ => $"Your pre-order is now marked {status}."
        };
    }

    private static void AppendFarms(StringBuilder message, OrderEmailContext order)
    {
        foreach (var farm in order.Farms)
        {
            message.AppendLine($"From {farm.FarmName}");
            AppendLines(message, farm.Lines);
        }
    }

    private static void AppendLines(StringBuilder message, IReadOnlyList<ProductEmailLine> lines)
    {
        foreach (var line in lines)
        {
            message.AppendLine($"  {line.Quantity} x {line.Name} - {line.UnitPrice.ToString("C", Culture)} each");
        }

        message.AppendLine();
    }

    private static void AppendCollection(StringBuilder message, OrderEmailContext order)
    {
        message.AppendLine("Collection details");
        message.AppendLine($"Market:  {order.MarketName}");
        message.AppendLine($"Address: {order.PickupAddress}, {order.PickupCity}");
        message.AppendLine($"Date:    {order.PickupDate.ToLocalTime().ToString("dddd d MMMM yyyy", Culture)}");
        message.AppendLine($"Time:    {order.PickupSlot}");
        message.AppendLine();
    }

    private static string FormatDate(DateTime value) =>
        value.ToLocalTime().ToString("d MMMM yyyy 'at' h:mm tt", Culture);

    private string OrderUrl(OrderEmailContext order)
    {
        var path = $"/Customer/OrderDetails/{order.OrderId}";
        var baseUrl = configuration["App:BaseUrl"]?.Trim();

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            return $"{baseUrl.TrimEnd('/')}{path}";
        }

        return environment.IsDevelopment() ? $"http://localhost:5049{path}" : path;
    }
}
