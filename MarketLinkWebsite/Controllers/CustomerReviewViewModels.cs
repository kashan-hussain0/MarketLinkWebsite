namespace MarketLinkWebsite.Controllers;

/// <summary>
/// Everything a customer wrote, together with whatever the farmer said back, so the
/// reply is never hidden away on the public page.
/// </summary>
public sealed class CustomerReviewListViewModel
{
    public List<CustomerReviewItem> Reviews { get; set; } = new();
}

public sealed class CustomerReviewItem
{
    public int Id { get; set; }
    public int Rating { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public bool VerifiedPurchase { get; set; }
    public int HelpfulCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? FarmerReply { get; set; }
    public DateTime? FarmerRepliedAt { get; set; }
    public string Target { get; set; } = string.Empty;
    public string TargetKind { get; set; } = string.Empty;
    public int TargetId { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
}
