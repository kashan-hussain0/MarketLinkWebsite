using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketLinkWebsite.Models.Entities;

public class ReviewHelpfulVote
{
    public int ReviewId { get; set; }

    [ForeignKey(nameof(ReviewId))]
    public Review Review { get; set; } = null!;

    [Required]
    public string UserId { get; set; } = string.Empty;

    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
