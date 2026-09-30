using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketLinkWebsite.Models.Entities;

public class Review
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;

    public int? ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product? Product { get; set; }

    public int? FarmerProfileId { get; set; }

    [ForeignKey(nameof(FarmerProfileId))]
    public FarmerProfile? FarmerProfile { get; set; }

    [Range(1, 5)]
    public int Rating { get; set; }

    [MaxLength(120)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(1000)]
    public string Comment { get; set; } = string.Empty;

    public bool VerifiedPurchase { get; set; }
    public int HelpfulCount { get; set; }
    [MaxLength(1000)]
    public string? FarmerReply { get; set; }
    public DateTime? FarmerRepliedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<ReviewHelpfulVote> HelpfulVotes { get; set; } = new List<ReviewHelpfulVote>();
}
