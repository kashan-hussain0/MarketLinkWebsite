using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketLinkWebsite.Models.Entities;

public class FavouriteFarmer
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;

    public int FarmerProfileId { get; set; }

    [ForeignKey(nameof(FarmerProfileId))]
    public FarmerProfile FarmerProfile { get; set; } = null!;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
