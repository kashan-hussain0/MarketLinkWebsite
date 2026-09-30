using MarketLinkWebsite.Models.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketLinkWebsite.Models.Entities;

public class OrderItem
{
    public int Id { get; set; }

    public int OrderId { get; set; }

    [ForeignKey(nameof(OrderId))]
    public Order Order { get; set; } = null!;

    public int? ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product? Product { get; set; }

    [Required, MaxLength(150)]
    public string ProductName { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string FarmerName { get; set; } = string.Empty;

    public UnitType Unit { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    [NotMapped]
    public decimal SubTotal => Quantity * UnitPrice;
}
