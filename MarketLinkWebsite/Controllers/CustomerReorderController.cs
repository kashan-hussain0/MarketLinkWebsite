using System.Security.Claims;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Controllers;

[Authorize(Policy = "CustomerOnly")]
public sealed class CustomerReorderController : Controller
{
    private readonly ApplicationDbContext db;
    private readonly ICartService cart;

    public CustomerReorderController(ApplicationDbContext db, ICartService cart)
    {
        this.db = db;
        this.cart = cart;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int id, CancellationToken cancellationToken)
    {
        var customer = await GetActiveCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return RedirectToAction(nameof(AccountController.Login), "Account");
        }

        var order = await db.Orders
            .AsNoTracking()
            .Where(item => item.Id == id && item.UserId == customer.Id)
            .Select(item => new ReorderPreview
            {
                Id = item.Id,
                OrderNumber = item.OrderNumber,
                Status = item.Status.ToString(),
                PlacedLabel = item.OrderDate.ToString("MMM d, yyyy"),
                Lines = item.OrderItems.Select(line => new ReorderLine
                {
                    ProductId = line.ProductId ?? 0,
                    ProductName = line.ProductName,
                    Quantity = line.Quantity,
                    Available = line.Product != null && line.Product.IsAvailable && line.Product.Inventory != null
                        ? line.Product.Inventory.QuantityAvailable
                        : 0
                }).ToList()
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        ViewData["Title"] = "Reorder " + order.OrderNumber;
        return View(order);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(int id, CancellationToken cancellationToken)
    {
        var customer = await GetActiveCustomerAsync(cancellationToken);
        if (customer is null)
        {
            return RedirectToAction(nameof(AccountController.Login), "Account");
        }

        var lines = await db.OrderItems
            .AsNoTracking()
            .Where(line => line.OrderId == id && line.Order.UserId == customer.Id && line.ProductId != null)
            .Select(line => new { ProductId = line.ProductId!.Value, line.Quantity, line.ProductName })
            .ToListAsync(cancellationToken);

        if (lines.Count == 0)
        {
            TempData["Error"] = "That order has no items to reorder.";
            return RedirectToAction(nameof(CustomerController.Orders), "Customer");
        }

        var added = 0;
        var skipped = new List<string>();

        foreach (var line in lines)
        {
            try
            {
                await cart.AddAsync(line.ProductId, line.Quantity, cancellationToken);
                added++;
            }
            catch (InvalidOperationException exception)
            {
                skipped.Add(string.IsNullOrWhiteSpace(exception.Message) ? line.ProductName : exception.Message);
            }
        }

        if (added == 0)
        {
            TempData["Error"] = skipped.Count > 0
                ? "None of those items are available right now. " + string.Join(" ", skipped.Distinct().Take(2))
                : "None of those items are available right now.";
            return RedirectToAction(nameof(CustomerController.Orders), "Customer");
        }

        TempData["Success"] = skipped.Count == 0
            ? $"{added} item{(added == 1 ? string.Empty : "s")} added to your pre-order basket."
            : $"{added} item{(added == 1 ? string.Empty : "s")} added. Some are unavailable right now: {string.Join(" ", skipped.Distinct().Take(2))}";


        return RedirectToAction(nameof(CartController.Index), "Cart");
    }

    private async Task<ApplicationUser?> GetActiveCustomerAsync(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return await db.Users
            .FirstOrDefaultAsync(user => user.Id == userId && user.IsActive, cancellationToken);
    }
}

public sealed class ReorderPreview
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string PlacedLabel { get; set; } = string.Empty;
    public List<ReorderLine> Lines { get; set; } = new();
    public int ReorderableCount => Lines.Count(line => line.Available >= line.Quantity);
}

public sealed class ReorderLine
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int Available { get; set; }
}
