using System.ComponentModel.DataAnnotations;

namespace MarketLinkWebsite.Models.Entities;

public class Category
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
