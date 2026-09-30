using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketLinkWebsite.Models.Entities;

public class CartItem
{
    public int Id { get; set; }

    public string? UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public ApplicationUser? User { get; set; }

    [Required, MaxLength(100)]
    public string GuestId { get; set; } = string.Empty;

    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;

    public int Quantity { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
