using System.Globalization;
using System.Text;
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
public sealed class CustomerController : AdminControllerBase
{
    public CustomerController(
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
        var customerRoleId = await Db.Roles.Where(role => role.Name == "Customer").Select(role => role.Id).FirstOrDefaultAsync(cancellationToken);
        var adminRoleId = await Db.Roles.Where(role => role.Name == "Admin").Select(role => role.Id).FirstOrDefaultAsync(cancellationToken);
        var query = Db.Users
            .AsNoTracking()
            .Where(user => customerRoleId != null
                && Db.UserRoles.Any(userRole => userRole.UserId == user.Id && userRole.RoleId == customerRoleId)
                && !Db.UserRoles.Any(userRole => userRole.UserId == user.Id && userRole.RoleId == adminRoleId));
        var term = Clean(search);
        if (term.Length > 0)
        {
            var pattern = SearchPattern(term);
            query = query.Where(user => EF.Functions.Like(user.FirstName, pattern, "\\")
                || EF.Functions.Like(user.LastName, pattern, "\\")
                || EF.Functions.Like(user.Email!, pattern, "\\")
                || EF.Functions.Like(user.PhoneNumber!, pattern, "\\"));
        }

        var now = DateTime.UtcNow;
        var newThreshold = now.AddDays(-14);
        var activeThreshold = now.AddDays(-30);
        var statusFilter = NormalizeStatusFilter(status);
        if (statusFilter == "Active")
        {
            query = query.Where(user => user.IsActive && user.CreatedAt < newThreshold);
        }
        else if (statusFilter == "New")
        {
            query = query.Where(user => user.IsActive && user.CreatedAt >= newThreshold);
        }
        else if (statusFilter == "Inactive")
        {
            query = query.Where(user => !user.IsActive);
        }

        var customers = await query
            .OrderBy(user => user.LastName).ThenBy(user => user.FirstName)
            .Select(user => new CustomerSummary
            {
                Id = user.Id,
                Name = user.FirstName + " " + user.LastName,
                Email = user.Email ?? string.Empty,
                Phone = user.PhoneNumber ?? string.Empty,
                Location = user.Addresses.Any(address => address.IsDefault)
                    ? user.Addresses.Where(address => address.IsDefault).Select(address => address.City).FirstOrDefault() ?? string.Empty
                    : user.Addresses.OrderByDescending(address => address.IsDefault).Select(address => address.City).FirstOrDefault() ?? string.Empty,
                Initials = user.FirstName + " " + user.LastName,
                Status = !user.IsActive ? "Inactive" : user.CreatedAt >= newThreshold ? "New" : "Active",
                JoinedLabel = user.CreatedAt.ToString("MMM d, yyyy", CultureInfo.InvariantCulture),
                LastOrderLabel = user.Orders.Count == 0 ? "Not yet" : user.Orders.Max(order => order.OrderDate).ToString("MMM d, yyyy", CultureInfo.InvariantCulture),
                OrderCount = user.Orders.Count,
                TotalSpent = user.Orders.Where(order => order.PaymentStatus == PaymentStatus.Paid && order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Declined).Sum(order => order.TotalAmount),
                IsActive = user.IsActive
            })
            .ToListAsync(cancellationToken);
        var normalizedCustomers = customers.Select(NormalizeCustomer).ToList();

        var allCustomers = await Db.Users
            .AsNoTracking()
            .Where(user => customerRoleId != null
                && Db.UserRoles.Any(userRole => userRole.UserId == user.Id && userRole.RoleId == customerRoleId)
                && !Db.UserRoles.Any(userRole => userRole.UserId == user.Id && userRole.RoleId == adminRoleId))
            .Select(user => new { user.IsActive, user.CreatedAt, user.LastLoginAt, HasRecentOrder = user.Orders.Any(order => order.OrderDate >= activeThreshold) })
            .ToListAsync(cancellationToken);
        var model = new CustomerListViewModel
        {
            SearchTerm = term,
            StatusFilter = statusFilter,
            TotalCustomers = allCustomers.Count,
            ActiveCustomers = allCustomers.Count(customer => customer.IsActive && (customer.LastLoginAt >= activeThreshold || customer.HasRecentOrder)),
            NewCustomers = allCustomers.Count(customer => customer.IsActive && customer.CreatedAt >= now.AddDays(-30)),
            Customers = normalizedCustomers
        };
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(CustomerStatusUpdateViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Select a valid customer.";
            return RedirectToAction(nameof(Index));
        }

        if (CurrentUserId == model.Id)
        {
            return Forbid();
        }

        var customer = await Db.Users.FirstOrDefaultAsync(user => user.Id == model.Id, cancellationToken);
        if (customer is null)
        {
            return NotFound();
        }

        if (!await UserManager.IsInRoleAsync(customer, "Customer") || await UserManager.IsInRoleAsync(customer, "Admin"))
        {
            return Forbid();
        }

        if (customer.IsActive == model.IsActive)
        {
            TempData["Error"] = model.IsActive ? "The customer is already active." : "The customer is already inactive.";
            return RedirectToAction(nameof(Index));
        }

        AddNotification(
            customer,
            NotificationType.Account,
            model.IsActive ? "Account activated" : "Account deactivated",
            model.IsActive ? "Your MarketLink account is active again." : "Your MarketLink account has been deactivated by an administrator.");
        customer.IsActive = model.IsActive;
        if (!model.IsActive)
        {
            var securityStampResult = await UserManager.UpdateSecurityStampAsync(customer);
            if (!securityStampResult.Succeeded)
            {
                TempData["Error"] = "The customer session could not be invalidated.";
                return RedirectToAction(nameof(Index));
            }
        }

        var action = model.IsActive ? "Activate customer" : "Deactivate customer";
        AddAudit(action, nameof(ApplicationUser), 0, $"{(model.IsActive ? "Activated" : "Deactivated")} customer {customer.Id} ({customer.Email}).");
        await Db.SaveChangesAsync(cancellationToken);
        TempData["Success"] = model.IsActive ? "The customer account was activated." : "The customer account was deactivated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var customerRoleId = await Db.Roles.Where(role => role.Name == "Customer").Select(role => role.Id).FirstOrDefaultAsync(cancellationToken);
        var adminRoleId = await Db.Roles.Where(role => role.Name == "Admin").Select(role => role.Id).FirstOrDefaultAsync(cancellationToken);
        var customers = await Db.Users
            .AsNoTracking()
            .Where(user => customerRoleId != null
                && Db.UserRoles.Any(userRole => userRole.UserId == user.Id && userRole.RoleId == customerRoleId)
                && !Db.UserRoles.Any(userRole => userRole.UserId == user.Id && userRole.RoleId == adminRoleId))
            .OrderBy(user => user.LastName).ThenBy(user => user.FirstName)
            .Select(user => new
            {
                user.Id,
                Name = user.FirstName + " " + user.LastName,
                user.Email,
                user.PhoneNumber,
                City = user.Addresses.Any(address => address.IsDefault)
                    ? user.Addresses.Where(address => address.IsDefault).Select(address => address.City).FirstOrDefault() ?? string.Empty
                    : user.Addresses.OrderByDescending(address => address.IsDefault).Select(address => address.City).FirstOrDefault() ?? string.Empty,
                user.IsActive,
                user.CreatedAt,
                user.LastLoginAt,
                OrderCount = user.Orders.Count,
                TotalSpent = user.Orders.Where(order => order.PaymentStatus == PaymentStatus.Paid && order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Declined).Sum(order => order.TotalAmount)
            })
            .ToListAsync(cancellationToken);
        var builder = new StringBuilder();
        builder.AppendLine("Name,Email,Phone,Location,Status,Joined,Last login,Orders,Total spent");
        foreach (var customer in customers)
        {
            builder.AppendLine(string.Join(',', new[]
            {
                CsvCell(customer.Name),
                CsvCell(customer.Email),
                CsvCell(customer.PhoneNumber),
                CsvCell(customer.City),
                CsvCell(customer.IsActive ? "Active" : "Inactive"),
                CsvCell(customer.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                CsvCell(customer.LastLoginAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty),
                customer.OrderCount.ToString(CultureInfo.InvariantCulture),
                customer.TotalSpent.ToString("0.00", CultureInfo.InvariantCulture)
            }));
        }

        AddAudit("Export customers", nameof(ApplicationUser), 0, $"Exported {customers.Count} customer records.");
        await Db.SaveChangesAsync(cancellationToken);
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
        return File(bytes, "text/csv", $"marketlink-customers-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private static string NormalizeStatusFilter(string? status)
    {
        var value = Clean(status);
        return value.Equals("Active", StringComparison.OrdinalIgnoreCase)
            || value.Equals("New", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Inactive", StringComparison.OrdinalIgnoreCase)
                ? value
                : "All customers";
    }

    private static CustomerSummary NormalizeCustomer(CustomerSummary customer)
    {
        var status = !customer.IsActive ? "Inactive" : customer.Status == "New" ? "New" : customer.IsActive ? "Active" : "Inactive";
        return new CustomerSummary
        {
            Id = customer.Id,
            Name = customer.Name,
            Email = customer.Email,
            Phone = customer.Phone,
            Location = customer.Location,
            Initials = Initials(customer.Name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), customer.Name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).LastOrDefault()),
            AvatarTone = AvatarTone(string.IsNullOrWhiteSpace(customer.Email) ? customer.Id.ToString(CultureInfo.InvariantCulture) : customer.Email),
            Status = status,
            StatusTone = StatusTone(status),
            JoinedLabel = customer.JoinedLabel,
            LastOrderLabel = customer.LastOrderLabel,
            OrderCount = customer.OrderCount,
            TotalSpent = customer.TotalSpent,
            IsActive = customer.IsActive
        };
    }

    private static string CsvCell(string? value)
    {
        var normalized = value ?? string.Empty;
        if (normalized.StartsWith('=') || normalized.StartsWith('+') || normalized.StartsWith('-') || normalized.StartsWith('@'))
        {
            normalized = "'" + normalized;
        }

        return $"\"{normalized.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
