using Microsoft.AspNetCore.Identity;

namespace MarketLinkWebsite.Models.Entities;

public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string ProfilePictureUrl { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;

    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }

    public bool HasLocation => Latitude.HasValue && Longitude.HasValue;

    public FarmerProfile? FarmerProfile { get; set; }
    public ICollection<Order> Orders { get; set; } = new List<Order>();
    public ICollection<Review> Reviews { get; set; } = new List<Review>();
    public ICollection<ReviewHelpfulVote> ReviewHelpfulVotes { get; set; } = new List<ReviewHelpfulVote>();
    public ICollection<FavouriteFarmer> FavouriteFarmers { get; set; } = new List<FavouriteFarmer>();
    public ICollection<FavouriteProduct> FavouriteProducts { get; set; } = new List<FavouriteProduct>();
    public ICollection<FavouriteMarket> FavouriteMarkets { get; set; } = new List<FavouriteMarket>();
    public ICollection<PasswordResetCode> PasswordResetCodes { get; set; } = new List<PasswordResetCode>();
    public ICollection<Notification> Notifications { get; set; } = new List<Notification>();
    public ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
    public ICollection<Address> Addresses { get; set; } = new List<Address>();
    public ICollection<CartItem> CartItems { get; set; } = new List<CartItem>();
    public ICollection<OrderStatusHistory> OrderStatusChanges { get; set; } = new List<OrderStatusHistory>();
}
