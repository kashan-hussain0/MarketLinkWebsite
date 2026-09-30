using MarketLinkWebsite.Models.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketLinkWebsite.Models.Entities;

public class Order
{
    public int Id { get; set; }

    [Required, MaxLength(32)]
    public string OrderNumber { get; set; } = string.Empty;

    [Required]
    public string UserId { get; set; } = string.Empty;

    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;

    public int MarketId { get; set; }

    [ForeignKey(nameof(MarketId))]
    public Market Market { get; set; } = null!;

    public DateTime PickupDate { get; set; }

    [Required, MaxLength(40)]
    public string PickupTimeSlot { get; set; } = string.Empty;

    [Required, MaxLength(250)]
    public string PickupAddress { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string City { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Notes { get; set; } = string.Empty;

    [MaxLength(30)]
    public string PaymentMethod { get; set; } = "Cash at pickup";

    public decimal TotalAmount { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }

    public ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public ICollection<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();
}
