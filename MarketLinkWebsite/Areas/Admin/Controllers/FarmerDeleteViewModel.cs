using System.ComponentModel.DataAnnotations;

namespace MarketLinkWebsite.Areas.Admin.Models;

public sealed class FarmerDeleteViewModel
{
    [Range(1, int.MaxValue, ErrorMessage = "Select a valid farmer.")]
    public int Id { get; set; }

    [Required(ErrorMessage = "Type DELETE to confirm.")]
    [StringLength(10)]
    public string? Confirmation { get; set; }

    [StringLength(300, ErrorMessage = "Reasons can be up to 300 characters.")]
    public string? Reason { get; set; }
}
