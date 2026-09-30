using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketLinkWebsite.Models.Entities;

public class AuditLog
{
    public int Id { get; set; }

    public string? UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public ApplicationUser? User { get; set; }

    [Required, MaxLength(100)]
    public string Action { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string EntityName { get; set; } = string.Empty;

    public string Details { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
