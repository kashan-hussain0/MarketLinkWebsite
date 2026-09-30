using MarketLinkWebsite.Areas.Admin.Models;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = "AdminAccess")]
public sealed class CategoryController : AdminControllerBase
{
    private static readonly string[] Icons = { "bi-flower1", "bi-egg-fried", "bi-cake2", "bi-archive", "bi-basket", "bi-flower2" };
    private static readonly string[] Tones = { "sage", "amber", "coral", "violet", "blue", "mint" };

    public CategoryController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
        : base(db, userManager, roleManager)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var categories = await Db.Categories
            .AsNoTracking()
            .OrderBy(category => category.SortOrder).ThenBy(category => category.Name)
            .Select(category => new CategorySummary
            {
                Id = category.Id,
                Name = category.Name,
                Description = category.Description,
                ProductCount = category.Products.Count,
                SortOrder = category.SortOrder,
                Status = category.IsActive ? "Active" : "Paused",
                StatusTone = category.IsActive ? "success" : "secondary"
            })
            .ToListAsync(cancellationToken);
        for (var index = 0; index < categories.Count; index++)
        {
            categories[index].Icon = Icons[index % Icons.Length];
            categories[index].IconTone = Tones[index % Tones.Length];
        }

        var model = new CategoryListViewModel
        {
            TotalCategories = categories.Count,
            ActiveCategories = categories.Count(category => category.Status == "Active"),
            TotalProducts = categories.Sum(category => category.ProductCount),
            Categories = categories
        };
        return View(model);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CategoryFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CategoryFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var name = Clean(model.Name);
        if (await Db.Categories.AnyAsync(category => category.Name == name, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.Name), "A category with this name already exists.");
            return View(model);
        }

        var category = new Category();
        Apply(model, category);
        Db.Categories.Add(category);
        AddAudit("Create category", nameof(Category), 0, $"Created category {category.Name}.");
        try
        {
            await Db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(nameof(model.Name), "A category with this name already exists.");
            return View(model);
        }

        TempData["Success"] = $"{category.Name} was created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest();
        }

        var category = await Db.Categories.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        return View(new CategoryFormViewModel
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            ImageUrl = category.ImageUrl,
            SortOrder = category.SortOrder,
            IsActive = category.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CategoryFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (model.Id <= 0)
        {
            return BadRequest();
        }

        var category = await Db.Categories.FirstOrDefaultAsync(item => item.Id == model.Id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        var name = Clean(model.Name);
        if (await Db.Categories.AnyAsync(item => item.Id != model.Id && item.Name == name, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.Name), "A category with this name already exists.");
            return View(model);
        }

        Apply(model, category);
        AddAudit("Update category", nameof(Category), category.Id, $"Updated category {category.Name}.");
        try
        {
            await Db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(nameof(model.Name), "A category with this name already exists.");
            return View(model);
        }

        TempData["Success"] = $"{category.Name} was updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        if (id <= 0)
        {
            return BadRequest();
        }

        var category = await Db.Categories.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        if (await Db.Products.AnyAsync(product => product.CategoryId == category.Id, cancellationToken))
        {
            TempData["Error"] = $"{category.Name} contains products and cannot be deleted. Pause it instead.";
            return RedirectToAction(nameof(Index));
        }

        var name = category.Name;
        Db.Categories.Remove(category);
        AddAudit("Delete category", nameof(Category), id, $"Deleted category {name}.");
        try
        {
            await Db.SaveChangesAsync(cancellationToken);
            TempData["Success"] = $"{name} was deleted.";
        }
        catch (DbUpdateException)
        {
            TempData["Error"] = $"{name} could not be deleted because related records still reference it.";
        }

        return RedirectToAction(nameof(Index));
    }

    private static void Apply(CategoryFormViewModel model, Category category)
    {
        category.Name = Clean(model.Name);
        category.Description = Clean(model.Description);
        category.ImageUrl = Clean(model.ImageUrl);
        category.SortOrder = model.SortOrder;
        category.IsActive = model.IsActive;
    }
}
