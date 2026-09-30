using MarketLinkWebsite.Models.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketLinkWebsite.Models.Entities;

public class OrderStatusHistory
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    [ForeignKey(nameof(OrderId))]
    public Order Order { get; set; } = null!;

    public OrderStatus Status { get; set; }

    public string? ChangedById { get; set; }

    [ForeignKey(nameof(ChangedById))]
    public ApplicationUser? ChangedBy { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}
