using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketLinkWebsite.Models.Entities;

public class Address
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;

    [Required, MaxLength(40)]
    public string Label { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string RecipientName { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string Phone { get; set; } = string.Empty;

    [Required, MaxLength(250)]
    public string AddressLine { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string City { get; set; } = string.Empty;

    [MaxLength(100)]
    public string Region { get; set; } = string.Empty;

    [MaxLength(20)]
    public string PostalCode { get; set; } = string.Empty;

    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    public bool HasLocation => Latitude.HasValue && Longitude.HasValue;
}
