using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MarketLinkWebsite.Models.Entities;

public class WeeklyStockPlan
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    [ForeignKey(nameof(ProductId))]
    public Product Product { get; set; } = null!;

    public bool Enabled { get; set; }
    public int MondayStock { get; set; }
    public int TuesdayStock { get; set; }
    public int WednesdayStock { get; set; }
    public int ThursdayStock { get; set; }
    public int FridayStock { get; set; }
    public int SaturdayStock { get; set; }
    public int SundayStock { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The day the template was last carried over to live stock. Kept apart from
    /// <see cref="UpdatedAt"/> so a nightly apply never hides a farmer's own edit.
    /// </summary>
    public DateTime? LastAppliedOn { get; set; }
}
