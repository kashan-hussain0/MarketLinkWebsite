using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketLinkWebsite.Models.Entities;

public class MarketFarmer
{
    public int MarketId { get; set; }

    [ForeignKey(nameof(MarketId))]
    public Market Market { get; set; } = null!;

    public int FarmerProfileId { get; set; }

    [ForeignKey(nameof(FarmerProfileId))]
    public FarmerProfile FarmerProfile { get; set; } = null!;

    [MaxLength(30)]
    public string StallNumber { get; set; } = string.Empty;

    public DateTime JoinedDate { get; set; } = DateTime.UtcNow;
}
