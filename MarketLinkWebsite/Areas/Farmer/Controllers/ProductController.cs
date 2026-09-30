using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Areas.Farmer.Controllers;

[Area("Farmer")]
[Authorize(Policy = "ActiveFarmerAccess")]
public sealed class ProductController : FarmerControllerBase
{
    public ProductController(ApplicationDbContext db) : base(db)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? search, string? status, CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);
        var normalizedSearch = search?.Trim() ?? string.Empty;
        var normalizedStatus = string.IsNullOrWhiteSpace(status) ? "All products" : status.Trim();
        var products = await Db.Products
            .AsNoTracking()
            .Include(product => product.Category)
            .Include(product => product.Inventory)
            .Where(product => product.FarmerProfileId == profile.Id)
            .OrderBy(product => product.Name)
            .ToListAsync(cancellationToken);

        var weeklyPlans = await GetWeeklyStockPlansAsync(products.Select(product => product.Id), cancellationToken);
        var mappedProducts = products.Select(product => MapProduct(product, weeklyPlans.GetValueOrDefault(product.Id))).ToList();
        var filteredProducts = mappedProducts
            .Where(product => string.IsNullOrEmpty(normalizedSearch)
                || product.Name.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase)
                || product.Category.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            .Where(product => normalizedStatus switch
            {
                "Active" => product.IsAvailable && product.Stock > 0,
                "Low stock" => product.IsAvailable && product.Stock > 0 && product.Stock <= product.ReorderThreshold,
                "Drafts" => !product.IsAvailable,
                "Removed" => !product.IsAvailable,
                _ => product.IsAvailable
            })
            .ToList();

        var model = new ProductListViewModel
        {
            SearchTerm = normalizedSearch,
            StatusFilter = normalizedStatus,
            TotalProducts = mappedProducts.Count,
            ActiveProducts = mappedProducts.Count(product => product.IsAvailable && product.Stock > 0),
            LowStockProducts = mappedProducts.Count(product => product.IsAvailable && product.Stock > 0 && product.Stock <= product.ReorderThreshold),
            RemovedProducts = mappedProducts.Count(product => !product.IsAvailable),
            Notice = Request.Query["notice"].FirstOrDefault(),
            Categories = await GetCategoriesAsync(cancellationToken: cancellationToken),
            Products = filteredProducts
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);
        var categories = await GetCategoriesAsync(cancellationToken: cancellationToken);
        return View(new ProductFormViewModel
        {
            Categories = categories,
            Category = categories.FirstOrDefault()?.Name ?? "Vegetables",
            Unit = "per piece",
            IsOrganic = true,
            IsAvailable = true,
            ReorderThreshold = 5
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductFormViewModel model, CancellationToken cancellationToken = default)
    {
        model ??= new ProductFormViewModel();
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);
        Normalize(model);
        model.Categories = await GetCategoriesAsync(model.Category, cancellationToken);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var category = await FindCategoryAsync(model.Category, cancellationToken);
        if (category is null)
        {
            ModelState.AddModelError(nameof(model.Category), "Choose a valid category.");
            return View(model);
        }

        string? uploadedImageUrl;
        try
        {
            uploadedImageUrl = await SaveImageAsync(model.ImageFile, "farmer-products", cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(nameof(model.ImageFile), exception.Message);
            return View(model);
        }

        var product = new Product
        {
            Name = model.Name,
            Description = model.Description,
            Price = model.Price,
            Unit = ParseUnit(model.Unit),
            ImageUrl = uploadedImageUrl ?? model.ImageUrl?.Trim() ?? string.Empty,
            IsOrganic = model.IsOrganic,
            IsAvailable = model.IsAvailable && model.Stock > 0,
            FarmerProfileId = profile.Id,
            CategoryId = category.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        product.Inventory = new Inventory
        {
            QuantityAvailable = model.Stock,
            ReorderThreshold = model.ReorderThreshold,
            LastUpdated = DateTime.UtcNow
        };

        Db.Products.Add(product);
        await Db.SaveChangesAsync(cancellationToken);
        await SaveWeeklyStockPlanAsync(product.Id, model, cancellationToken);

        if (model.Stock <= model.ReorderThreshold)
        {
            QueueNotification(profile.UserId, NotificationType.System, "Low stock reminder", $"{product.Name} is at {model.Stock} units. Restock it before the next market.", $"/Farmer/Product/Details/{product.Id}");
        }

        await Db.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index), new { area = "Farmer", notice = "Product saved successfully." });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int? id, CancellationToken cancellationToken = default)
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

        var product = await Db.Products
            .Include(item => item.Category)
            .Include(item => item.Inventory)
            .SingleOrDefaultAsync(item => item.Id == id && item.FarmerProfileId == profile.Id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var plan = await GetWeeklyStockPlanAsync(product.Id, cancellationToken);
        return View(MapProductForm(product, await GetCategoriesAsync(product.Category?.Name, cancellationToken), plan));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProductFormViewModel model, bool removeImage = false, CancellationToken cancellationToken = default)
    {
        model ??= new ProductFormViewModel();
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);
        if (id <= 0)
        {
            id = model.Id;
        }

        var product = await Db.Products
            .Include(item => item.Inventory)
            .SingleOrDefaultAsync(item => item.Id == id && item.FarmerProfileId == profile.Id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        model.Id = product.Id;
        Normalize(model);
        model.Categories = await GetCategoriesAsync(model.Category, cancellationToken);
        if (!ModelState.IsValid)
        {
            model.ExistingImageUrl = product.ImageUrl;
            return View(model);
        }

        var category = await FindCategoryAsync(model.Category, cancellationToken);
        if (category is null)
        {
            ModelState.AddModelError(nameof(model.Category), "Choose a valid category.");
            model.ExistingImageUrl = product.ImageUrl;
            return View(model);
        }

        string? uploadedImageUrl;
        try
        {
            uploadedImageUrl = await SaveImageAsync(model.ImageFile, "farmer-products", cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            ModelState.AddModelError(nameof(model.ImageFile), exception.Message);
            model.ExistingImageUrl = product.ImageUrl;
            return View(model);
        }

        product.Name = model.Name;
        product.Description = model.Description;
        product.Price = model.Price;
        product.Unit = ParseUnit(model.Unit);
        var pastedImageUrl = model.ImageUrl?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(uploadedImageUrl))
        {
            product.ImageUrl = uploadedImageUrl;
        }
        else if (removeImage)
        {
            product.ImageUrl = string.Empty;
        }
        else if (pastedImageUrl.Length > 0)
        {
            product.ImageUrl = pastedImageUrl;
        }
        product.IsOrganic = model.IsOrganic;
        product.IsAvailable = model.IsAvailable && model.Stock > 0;
        product.CategoryId = category.Id;
        product.UpdatedAt = DateTime.UtcNow;

        if (product.Inventory is null)
        {
            product.Inventory = new Inventory
            {
                ProductId = product.Id,
                LastUpdated = DateTime.UtcNow
            };
            Db.Inventories.Add(product.Inventory);
        }

        product.Inventory.QuantityAvailable = model.Stock;
        product.Inventory.ReorderThreshold = model.ReorderThreshold;
        product.Inventory.LastUpdated = DateTime.UtcNow;
        await Db.SaveChangesAsync(cancellationToken);
        await SaveWeeklyStockPlanAsync(product.Id, model, cancellationToken);

        if (model.Stock <= model.ReorderThreshold)
        {
            QueueNotification(profile.UserId, NotificationType.System, "Low stock reminder", $"{product.Name} is at {model.Stock} units. Restock it before the next market.", $"/Farmer/Product/Details/{product.Id}");
        }

        await Db.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index), new { area = "Farmer", notice = "Product changes are ready for your next harvest." });
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

        var product = await Db.Products
            .AsNoTracking()
            .Include(item => item.Category)
            .Include(item => item.Inventory)
            .SingleOrDefaultAsync(item => item.Id == id && item.FarmerProfileId == profile.Id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var plan = await GetWeeklyStockPlanAsync(product.Id, cancellationToken);
        return View(MapProduct(product, plan));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);
        var product = await Db.Products
            .SingleOrDefaultAsync(item => item.Id == id && item.FarmerProfileId == profile.Id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var productName = product.Name;
        product.IsAvailable = false;
        product.UpdatedAt = DateTime.UtcNow;
        AddAudit("Archive product", nameof(Product), product.Id, $"Removed {productName} from the catalog.");
        QueueNotification(profile.UserId, NotificationType.System, "Product removed", $"{productName} is no longer available for pre-orders.", "/Farmer/Product");
        await Db.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index), new { area = "Farmer", notice = $"{productName} was removed from your catalog." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(int id, CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);
        var product = await Db.Products
            .Include(item => item.Inventory)
            .SingleOrDefaultAsync(item => item.Id == id && item.FarmerProfileId == profile.Id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        var productName = product.Name;
        var hasStock = product.Inventory is not null && product.Inventory.QuantityAvailable > 0;

        if (!hasStock)
        {
            TempData["Error"] = "Add stock before relisting this product.";
            return RedirectToAction(nameof(Index), new { area = "Farmer" });
        }

        product.IsAvailable = true;
        product.UpdatedAt = DateTime.UtcNow;
        AddAudit("Restore product", nameof(Product), product.Id, $"Relisted {productName} on the marketplace catalog.");
        QueueNotification(profile.UserId, NotificationType.System, "Product relisted", $"{productName} is back on the marketplace catalog.", "/Farmer/Product");
        await Db.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Index), new { area = "Farmer", notice = $"{productName} is live on the marketplace again." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateInventory(int id, int stock, int reorderThreshold, CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);
        if (stock < 0 || stock > 1000000 || reorderThreshold < 0 || reorderThreshold > 1000000)
        {
            return RedirectToAction(nameof(Details), new { area = "Farmer", id, notice = "Stock and reorder threshold must be between 0 and 1,000,000." });
        }

        var product = await Db.Products
            .Include(item => item.Inventory)
            .SingleOrDefaultAsync(item => item.Id == id && item.FarmerProfileId == profile.Id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        if (product.Inventory is null)
        {
            product.Inventory = new Inventory { ProductId = product.Id, LastUpdated = DateTime.UtcNow };
            Db.Inventories.Add(product.Inventory);
        }

        product.Inventory.QuantityAvailable = stock;
        product.Inventory.ReorderThreshold = reorderThreshold;
        product.Inventory.LastUpdated = DateTime.UtcNow;
        product.UpdatedAt = DateTime.UtcNow;
        product.IsAvailable = stock > 0;
        await Db.SaveChangesAsync(cancellationToken);

        var weeklyPlan = await GetWeeklyStockPlanAsync(product.Id, cancellationToken);
        if (weeklyPlan is { Enabled: true })
        {
            SetCurrentDayStock(weeklyPlan, stock);
            await SaveWeeklyStockPlanAsync(weeklyPlan, cancellationToken);
        }

        if (stock <= reorderThreshold)
        {
            QueueNotification(profile.UserId, NotificationType.System, "Low stock reminder", $"{product.Name} is at {stock} units. Restock it before the next market.", $"/Farmer/Product/Details/{product.Id}");
        }

        await Db.SaveChangesAsync(cancellationToken);
        return RedirectToAction(nameof(Details), new { area = "Farmer", id, notice = "Inventory updated." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UpdateStock(int id, int stock, int reorderThreshold, CancellationToken cancellationToken = default)
    {
        return UpdateInventory(id, stock, reorderThreshold, cancellationToken);
    }

    private async Task<List<FarmerCategoryOption>> GetCategoriesAsync(string? selectedCategory = null, CancellationToken cancellationToken = default)
    {
        var categories = await Db.Categories
            .AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .Select(category => new FarmerCategoryOption { Id = category.Id, Name = category.Name })
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(selectedCategory) && categories.All(category => !string.Equals(category.Name, selectedCategory, StringComparison.OrdinalIgnoreCase)))
        {
            categories.Insert(0, new FarmerCategoryOption { Name = selectedCategory });
        }

        return categories;
    }

    private Task<Category?> FindCategoryAsync(string categoryName, CancellationToken cancellationToken)
    {
        var normalizedName = categoryName.Trim();
        return Db.Categories.FirstOrDefaultAsync(category => category.Name == normalizedName, cancellationToken);
    }

    private static void Normalize(ProductFormViewModel model)
    {
        model.Name = (model.Name ?? string.Empty).Trim();
        model.Category = (model.Category ?? string.Empty).Trim();
        model.Unit = (model.Unit ?? string.Empty).Trim();
        model.Description = (model.Description ?? string.Empty).Trim();
        model.ImageUrl = model.ImageUrl?.Trim();
        model.ReorderThreshold = Math.Max(0, model.ReorderThreshold);
        model.Stock = Math.Max(0, model.Stock);
    }

    private async Task<Dictionary<int, WeeklyStockPlan>> GetWeeklyStockPlansAsync(IEnumerable<int> productIds, CancellationToken cancellationToken)
    {
        var ids = productIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await Db.WeeklyStockPlans
            .AsNoTracking()
            .Where(plan => ids.Contains(plan.ProductId))
            .ToDictionaryAsync(plan => plan.ProductId, cancellationToken);
    }

    private async Task<WeeklyStockPlan?> GetWeeklyStockPlanAsync(int productId, CancellationToken cancellationToken)
    {
        return await Db.WeeklyStockPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(plan => plan.ProductId == productId, cancellationToken);
    }

    private Task SaveWeeklyStockPlanAsync(int productId, ProductFormViewModel model, CancellationToken cancellationToken = default)
    {
        return SaveWeeklyStockPlanAsync(new WeeklyStockPlan
        {
            ProductId = productId,
            Enabled = model.RecurringStockEnabled,
            MondayStock = Math.Max(0, model.MondayStock),
            TuesdayStock = Math.Max(0, model.TuesdayStock),
            WednesdayStock = Math.Max(0, model.WednesdayStock),
            ThursdayStock = Math.Max(0, model.ThursdayStock),
            FridayStock = Math.Max(0, model.FridayStock),
            SaturdayStock = Math.Max(0, model.SaturdayStock),
            SundayStock = Math.Max(0, model.SundayStock)
        }, cancellationToken);
    }

    private async Task SaveWeeklyStockPlanAsync(WeeklyStockPlan plan, CancellationToken cancellationToken = default)
    {
        var existing = await Db.WeeklyStockPlans.FirstOrDefaultAsync(item => item.ProductId == plan.ProductId, cancellationToken);
        if (existing is null)
        {
            plan.UpdatedAt = DateTime.UtcNow;
            Db.WeeklyStockPlans.Add(plan);
            return;
        }

        existing.Enabled = plan.Enabled;
        existing.MondayStock = plan.MondayStock;
        existing.TuesdayStock = plan.TuesdayStock;
        existing.WednesdayStock = plan.WednesdayStock;
        existing.ThursdayStock = plan.ThursdayStock;
        existing.FridayStock = plan.FridayStock;
        existing.SaturdayStock = plan.SaturdayStock;
        existing.SundayStock = plan.SundayStock;
        existing.UpdatedAt = DateTime.UtcNow;
    }

    private static void SetCurrentDayStock(WeeklyStockPlan plan, int stock)
    {
        switch (DateTime.Now.DayOfWeek)
        {
            case DayOfWeek.Monday:
                plan.MondayStock = stock;
                break;
            case DayOfWeek.Tuesday:
                plan.TuesdayStock = stock;
                break;
            case DayOfWeek.Wednesday:
                plan.WednesdayStock = stock;
                break;
            case DayOfWeek.Thursday:
                plan.ThursdayStock = stock;
                break;
            case DayOfWeek.Friday:
                plan.FridayStock = stock;
                break;
            case DayOfWeek.Saturday:
                plan.SaturdayStock = stock;
                break;
            case DayOfWeek.Sunday:
                plan.SundayStock = stock;
                break;
        }
    }

    private static FarmerProductSummaryViewModel MapProduct(Product product, WeeklyStockPlan? plan = null)
    {
        var inventory = product.Inventory;
        var stock = inventory?.QuantityAvailable ?? 0;
        var threshold = inventory?.ReorderThreshold ?? 5;
        var status = !product.IsAvailable ? "Draft" : stock <= threshold ? "Low stock" : "Active";
        return new FarmerProductSummaryViewModel
        {
            Id = product.Id,
            Name = product.Name,
            Category = product.Category?.Name ?? "Uncategorized",
            Unit = GetUnitLabel(product.Unit),
            Description = product.Description,
            Price = product.Price,
            Stock = stock,
            ReorderThreshold = threshold,
            IsOrganic = product.IsOrganic,
            IsAvailable = product.IsAvailable,
            ImageUrl = string.IsNullOrWhiteSpace(product.ImageUrl) ? FarmFallbackImageUrl : product.ImageUrl,
            Status = status,
            StatusTone = status == "Active" ? "green" : status == "Low stock" ? "amber" : "muted",
            UpdatedLabel = FormatRelativeTime(product.UpdatedAt),
            RecurringStockEnabled = plan?.Enabled ?? false,
            MondayStock = plan?.MondayStock ?? stock,
            TuesdayStock = plan?.TuesdayStock ?? stock,
            WednesdayStock = plan?.WednesdayStock ?? stock,
            ThursdayStock = plan?.ThursdayStock ?? stock,
            FridayStock = plan?.FridayStock ?? stock,
            SaturdayStock = plan?.SaturdayStock ?? stock,
            SundayStock = plan?.SundayStock ?? stock
        };
    }

    private static ProductFormViewModel MapProductForm(Product product, List<FarmerCategoryOption> categories, WeeklyStockPlan? plan = null)
    {
        var stock = product.Inventory?.QuantityAvailable ?? 0;
        return new ProductFormViewModel
        {
            Id = product.Id,
            Name = product.Name,
            Category = product.Category?.Name ?? string.Empty,
            Price = product.Price,
            Unit = GetUnitLabel(product.Unit),
            Stock = stock,
            ReorderThreshold = product.Inventory?.ReorderThreshold ?? 5,
            Description = product.Description,
            ImageUrl = product.ImageUrl,
            ExistingImageUrl = product.ImageUrl,
            IsOrganic = product.IsOrganic,
            IsAvailable = product.IsAvailable,
            RecurringStockEnabled = plan?.Enabled ?? false,
            MondayStock = plan?.MondayStock ?? stock,
            TuesdayStock = plan?.TuesdayStock ?? stock,
            WednesdayStock = plan?.WednesdayStock ?? stock,
            ThursdayStock = plan?.ThursdayStock ?? stock,
            FridayStock = plan?.FridayStock ?? stock,
            SaturdayStock = plan?.SaturdayStock ?? stock,
            SundayStock = plan?.SundayStock ?? stock,
            Categories = categories
        };
    }

}
