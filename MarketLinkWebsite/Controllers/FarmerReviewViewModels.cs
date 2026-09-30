using System.ComponentModel.DataAnnotations;

namespace MarketLinkWebsite.Controllers;

public sealed class FarmerReviewOption
{
    public int Id { get; set; }
    public string FarmName { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public bool CanReview { get; set; }
}

public sealed class FarmerReviewSubmission
{
    [Range(1, int.MaxValue, ErrorMessage = "Choose the farm you want to review.")]
    public int FarmerProfileId { get; set; }

    public string FarmName { get; set; } = string.Empty;

    [Range(1, 5, ErrorMessage = "Choose a rating from 1 to 5 stars.")]
    public int Rating { get; set; }

    [StringLength(120)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Tell other shoppers about your experience.")]
    [StringLength(1000, MinimumLength = 10, ErrorMessage = "Your review must be at least 10 characters.")]
    public string Body { get; set; } = string.Empty;

    public List<FarmerReviewOption> Options { get; set; } = new();
}
