using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketLinkWebsite.Models.Entities;

public class FavouriteMarket
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;

    public int MarketId { get; set; }

    [ForeignKey(nameof(MarketId))]
    public Market Market { get; set; } = null!;

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
