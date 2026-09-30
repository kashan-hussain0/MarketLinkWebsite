using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketLinkWebsite.Models.Entities;

/// <summary>
/// A one-time code mailed to a customer who has forgotten their password. Only a
/// hash of the code is stored, so a leaked database row cannot be used to log in.
/// </summary>
public class PasswordResetCode
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    [ForeignKey(nameof(UserId))]
    public ApplicationUser User { get; set; } = null!;

    /// <summary>SHA-256 of the emailed code, hex encoded. Never the code itself.</summary>
    [Required, MaxLength(64)]
    public string CodeHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Set the moment the code is redeemed, so it can never be used twice.</summary>
    public DateTime? ConsumedAt { get; set; }

    public int AttemptCount { get; set; }

    public string? RequestedFromIp { get; set; }
}
