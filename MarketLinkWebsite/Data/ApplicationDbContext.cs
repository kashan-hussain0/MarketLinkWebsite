using MarketLinkWebsite.Models.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<FarmerProfile> FarmerProfiles { get; set; } = null!;
    public DbSet<Market> Markets { get; set; } = null!;
    public DbSet<MarketFarmer> MarketFarmers { get; set; } = null!;
    public DbSet<Category> Categories { get; set; } = null!;
    public DbSet<Product> Products { get; set; } = null!;
    public DbSet<Inventory> Inventories { get; set; } = null!;
    public DbSet<WeeklyStockPlan> WeeklyStockPlans { get; set; } = null!;
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderItem> OrderItems { get; set; } = null!;
    public DbSet<OrderStatusHistory> OrderStatusHistory { get; set; } = null!;
    public DbSet<Review> Reviews { get; set; } = null!;
    public DbSet<ReviewHelpfulVote> ReviewHelpfulVotes { get; set; } = null!;
    public DbSet<FavouriteFarmer> FavouriteFarmers { get; set; } = null!;
    public DbSet<FavouriteProduct> FavouriteProducts { get; set; } = null!;
    public DbSet<FavouriteMarket> FavouriteMarkets { get; set; } = null!;
    public DbSet<PickupSlot> PickupSlots { get; set; } = null!;
    public DbSet<PasswordResetCode> PasswordResetCodes { get; set; } = null!;
    public DbSet<Notification> Notifications { get; set; } = null!;
    public DbSet<Address> Addresses { get; set; } = null!;
    public DbSet<CartItem> CartItems { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.FirstName).HasMaxLength(50).IsRequired();
            entity.Property(user => user.LastName).HasMaxLength(50).IsRequired();
            entity.Property(user => user.ProfilePictureUrl).HasMaxLength(300);
            entity.Property(user => user.Latitude).HasPrecision(10, 7);
            entity.Property(user => user.Longitude).HasPrecision(10, 7);
            entity.HasIndex(user => user.CreatedAt);
        });

        builder.Entity<FarmerProfile>(entity =>
        {
            entity.HasIndex(profile => profile.UserId).IsUnique();
            entity.Property(profile => profile.FarmName).HasMaxLength(150).IsRequired();
            entity.Property(profile => profile.Address).HasMaxLength(250).IsRequired();
            entity.Property(profile => profile.City).HasMaxLength(100);
            entity.Property(profile => profile.OperatingDays).HasMaxLength(100);
            entity.Property(profile => profile.PickupWindows).HasMaxLength(100);
            entity.Property(profile => profile.Latitude).HasPrecision(10, 7);
            entity.Property(profile => profile.Longitude).HasPrecision(10, 7);
            entity.Property(profile => profile.Rating).HasPrecision(3, 2);
            entity.HasOne(profile => profile.User)
                .WithOne(user => user.FarmerProfile)
                .HasForeignKey<FarmerProfile>(profile => profile.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Market>(entity =>
        {
            entity.Property(market => market.Name).HasMaxLength(150).IsRequired();
            entity.Property(market => market.Address).HasMaxLength(250).IsRequired();
            entity.Property(market => market.City).HasMaxLength(100);
            entity.Property(market => market.OperatingDays).HasMaxLength(100);
            entity.Property(market => market.OpenTime).HasMaxLength(20);
            entity.Property(market => market.CloseTime).HasMaxLength(20);
            entity.Property(market => market.ImageUrl).HasMaxLength(300);
            entity.Property(market => market.Latitude).HasPrecision(10, 7);
            entity.Property(market => market.Longitude).HasPrecision(10, 7);
            entity.HasIndex(market => new { market.City, market.IsActive });
        });

        builder.Entity<MarketFarmer>(entity =>
        {
            entity.HasKey(link => new { link.MarketId, link.FarmerProfileId });
            entity.Property(link => link.StallNumber).HasMaxLength(30);
            entity.HasOne(link => link.Market)
                .WithMany(market => market.MarketFarmers)
                .HasForeignKey(link => link.MarketId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(link => link.FarmerProfile)
                .WithMany(profile => profile.MarketFarmers)
                .HasForeignKey(link => link.FarmerProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Category>(entity =>
        {
            entity.Property(category => category.Name).HasMaxLength(100).IsRequired();
            entity.Property(category => category.Description).HasMaxLength(500);
            entity.Property(category => category.ImageUrl).HasMaxLength(300);
            entity.HasIndex(category => category.Name).IsUnique();
        });

        builder.Entity<Product>(entity =>
        {
            entity.Property(product => product.Name).HasMaxLength(150).IsRequired();
            entity.Property(product => product.Description).HasMaxLength(2000);
            entity.Property(product => product.ImageUrl).HasMaxLength(300);
            entity.Property(product => product.Price).HasPrecision(18, 2);
            entity.HasIndex(product => new { product.CategoryId, product.IsAvailable });
            entity.HasIndex(product => new { product.FarmerProfileId, product.IsAvailable });
            entity.ToTable("Products", table =>
            {
                table.HasCheckConstraint("CK_Product_Price", "[Price] >= 0");
            });
            entity.HasOne(product => product.Category)
                .WithMany(category => category.Products)
                .HasForeignKey(product => product.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(product => product.FarmerProfile)
                .WithMany(profile => profile.Products)
                .HasForeignKey(product => product.FarmerProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Inventory>(entity =>
        {
            entity.HasIndex(inventory => inventory.ProductId).IsUnique();
            entity.Property(inventory => inventory.RowVersion).IsRowVersion();
            entity.ToTable("Inventories", table =>
            {
                table.HasCheckConstraint("CK_Inventory_Quantity", "[QuantityAvailable] >= 0");
                table.HasCheckConstraint("CK_Inventory_Threshold", "[ReorderThreshold] >= 0");
            });
            entity.HasOne(inventory => inventory.Product)
                .WithOne(product => product.Inventory)
                .HasForeignKey<Inventory>(inventory => inventory.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WeeklyStockPlan>(entity =>
        {
            entity.HasIndex(plan => plan.ProductId).IsUnique();
            entity.ToTable("WeeklyStockPlans", table =>
            {
                table.HasCheckConstraint("CK_WeeklyStockPlan_Monday", "[MondayStock] >= 0");
                table.HasCheckConstraint("CK_WeeklyStockPlan_Tuesday", "[TuesdayStock] >= 0");
                table.HasCheckConstraint("CK_WeeklyStockPlan_Wednesday", "[WednesdayStock] >= 0");
                table.HasCheckConstraint("CK_WeeklyStockPlan_Thursday", "[ThursdayStock] >= 0");
                table.HasCheckConstraint("CK_WeeklyStockPlan_Friday", "[FridayStock] >= 0");
                table.HasCheckConstraint("CK_WeeklyStockPlan_Saturday", "[SaturdayStock] >= 0");
                table.HasCheckConstraint("CK_WeeklyStockPlan_Sunday", "[SundayStock] >= 0");
            });
            entity.HasOne(plan => plan.Product)
                .WithOne(product => product.WeeklyStockPlan)
                .HasForeignKey<WeeklyStockPlan>(plan => plan.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Order>(entity =>
        {
            entity.Property(order => order.OrderNumber).HasMaxLength(32).IsRequired();
            entity.Property(order => order.PickupTimeSlot).HasMaxLength(40).IsRequired();
            entity.Property(order => order.PickupAddress).HasMaxLength(250).IsRequired();
            entity.Property(order => order.City).HasMaxLength(100).IsRequired();
            entity.Property(order => order.Phone).HasMaxLength(30).IsRequired();
            entity.Property(order => order.Notes).HasMaxLength(500);
            entity.Property(order => order.PaymentMethod).HasMaxLength(30);
            entity.Property(order => order.TotalAmount).HasPrecision(18, 2);
            entity.HasIndex(order => order.OrderNumber).IsUnique();
            entity.HasIndex(order => new { order.UserId, order.OrderDate });
            entity.HasIndex(order => new { order.MarketId, order.PickupDate, order.Status });
            entity.ToTable("Orders", table =>
            {
                table.HasCheckConstraint("CK_Order_Total", "[TotalAmount] >= 0");
            });
            entity.HasOne(order => order.User)
                .WithMany(user => user.Orders)
                .HasForeignKey(order => order.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(order => order.Market)
                .WithMany(market => market.Orders)
                .HasForeignKey(order => order.MarketId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<OrderItem>(entity =>
        {
            entity.Property(item => item.ProductName).HasMaxLength(150).IsRequired();
            entity.Property(item => item.FarmerName).HasMaxLength(150).IsRequired();
            entity.Property(item => item.UnitPrice).HasPrecision(18, 2);
            entity.ToTable("OrderItems", table =>
            {
                table.HasCheckConstraint("CK_OrderItem_Quantity", "[Quantity] > 0");
                table.HasCheckConstraint("CK_OrderItem_Price", "[UnitPrice] >= 0");
            });
            entity.HasOne(item => item.Order)
                .WithMany(order => order.OrderItems)
                .HasForeignKey(item => item.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Product)
                .WithMany(product => product.OrderItems)
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<OrderStatusHistory>(entity =>
        {
            entity.Property(history => history.Note).HasMaxLength(300);
            entity.HasIndex(history => new { history.OrderId, history.ChangedAt });
            entity.HasOne(history => history.Order)
                .WithMany(order => order.StatusHistory)
                .HasForeignKey(history => history.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(history => history.ChangedBy)
                .WithMany(user => user.OrderStatusChanges)
                .HasForeignKey(history => history.ChangedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<Review>(entity =>
        {
            entity.Property(review => review.Title).HasMaxLength(120);
            entity.Property(review => review.Comment).HasMaxLength(1000).IsRequired();
            entity.Property(review => review.FarmerReply).HasMaxLength(1000);
            entity.HasIndex(review => new { review.ProductId, review.CreatedAt });
            entity.HasIndex(review => new { review.FarmerProfileId, review.CreatedAt });
            entity.HasIndex(review => new { review.UserId, review.ProductId })
                .IsUnique()
                .HasFilter("[ProductId] IS NOT NULL");
            entity.HasIndex(review => new { review.UserId, review.FarmerProfileId })
                .IsUnique()
                .HasFilter("[FarmerProfileId] IS NOT NULL");
            entity.ToTable("Reviews", table =>
            {
                table.HasCheckConstraint("CK_Review_Target", "([ProductId] IS NOT NULL AND [FarmerProfileId] IS NULL) OR ([ProductId] IS NULL AND [FarmerProfileId] IS NOT NULL)");
            });
            entity.HasOne(review => review.User)
                .WithMany(user => user.Reviews)
                .HasForeignKey(review => review.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(review => review.Product)
                .WithMany(product => product.Reviews)
                .HasForeignKey(review => review.ProductId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(review => review.FarmerProfile)
                .WithMany(profile => profile.Reviews)
                .HasForeignKey(review => review.FarmerProfileId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<ReviewHelpfulVote>(entity =>
        {
            entity.HasKey(vote => new { vote.ReviewId, vote.UserId });
            entity.HasOne(vote => vote.Review)
                .WithMany(review => review.HelpfulVotes)
                .HasForeignKey(vote => vote.ReviewId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(vote => vote.User)
                .WithMany(user => user.ReviewHelpfulVotes)
                .HasForeignKey(vote => vote.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PasswordResetCode>(entity =>
        {
            entity.HasOne(code => code.User)
                .WithMany(user => user.PasswordResetCodes)
                .HasForeignKey(code => code.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(code => new { code.UserId, code.CreatedAt });
        });

        builder.Entity<FavouriteFarmer>(entity =>
        {
            entity.HasKey(favourite => new { favourite.UserId, favourite.FarmerProfileId });
            entity.HasOne(favourite => favourite.User)
                .WithMany(user => user.FavouriteFarmers)
                .HasForeignKey(favourite => favourite.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(favourite => favourite.FarmerProfile)
                .WithMany(profile => profile.FavoritedByUsers)
                .HasForeignKey(favourite => favourite.FarmerProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<FavouriteProduct>(entity =>
        {
            entity.HasKey(favourite => new { favourite.UserId, favourite.ProductId });
            entity.HasOne(favourite => favourite.User)
                .WithMany(user => user.FavouriteProducts)
                .HasForeignKey(favourite => favourite.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(favourite => favourite.Product)
                .WithMany(product => product.FavouritedByUsers)
                .HasForeignKey(favourite => favourite.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<FavouriteMarket>(entity =>
        {
            entity.HasKey(favourite => new { favourite.UserId, favourite.MarketId });
            entity.HasOne(favourite => favourite.User)
                .WithMany(user => user.FavouriteMarkets)
                .HasForeignKey(favourite => favourite.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(favourite => favourite.Market)
                .WithMany(market => market.FavouritedByUsers)
                .HasForeignKey(favourite => favourite.MarketId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<PickupSlot>(entity =>
        {
            entity.HasIndex(slot => new { slot.FarmerProfileId, slot.DayOfWeek });
            entity.HasOne(slot => slot.FarmerProfile)
                .WithMany(profile => profile.PickupSlots)
                .HasForeignKey(slot => slot.FarmerProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.ToTable("PickupSlots", table =>
            {
                table.HasCheckConstraint("CK_PickupSlot_DayOfWeek", "[DayOfWeek] BETWEEN 0 AND 6");
            });
        });

        builder.Entity<Notification>(entity =>
        {
            entity.Property(notification => notification.Title).HasMaxLength(150).IsRequired();
            entity.Property(notification => notification.Message).HasMaxLength(500).IsRequired();
            entity.Property(notification => notification.ActionUrl).HasMaxLength(300);
            entity.HasIndex(notification => new { notification.UserId, notification.IsRead, notification.CreatedAt });
            entity.HasOne(notification => notification.User)
                .WithMany(user => user.Notifications)
                .HasForeignKey(notification => notification.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Address>(entity =>
        {
            entity.Property(address => address.Label).HasMaxLength(40).IsRequired();
            entity.Property(address => address.RecipientName).HasMaxLength(100).IsRequired();
            entity.Property(address => address.Phone).HasMaxLength(30).IsRequired();
            entity.Property(address => address.AddressLine).HasMaxLength(250).IsRequired();
            entity.Property(address => address.City).HasMaxLength(100).IsRequired();
            entity.Property(address => address.Region).HasMaxLength(100);
            entity.Property(address => address.PostalCode).HasMaxLength(20);
            entity.Property(address => address.Latitude).HasPrecision(10, 7);
            entity.Property(address => address.Longitude).HasPrecision(10, 7);
            entity.HasIndex(address => new { address.UserId, address.IsDefault });
            entity.HasOne(address => address.User)
                .WithMany(user => user.Addresses)
                .HasForeignKey(address => address.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CartItem>(entity =>
        {
            entity.Property(item => item.GuestId).HasMaxLength(100).IsRequired();
            entity.HasIndex(item => new { item.UserId, item.ProductId }).IsUnique().HasFilter("[UserId] IS NOT NULL");
            entity.HasIndex(item => new { item.GuestId, item.ProductId }).IsUnique().HasFilter("[UserId] IS NULL");
            entity.ToTable("CartItems", table =>
            {
                table.HasCheckConstraint("CK_CartItem_Quantity", "[Quantity] > 0");
            });
            entity.HasOne(item => item.User)
                .WithMany(user => user.CartItems)
                .HasForeignKey(item => item.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Product)
                .WithMany(product => product.CartItems)
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<AuditLog>(entity =>
        {
            entity.Property(log => log.Action).HasMaxLength(100).IsRequired();
            entity.Property(log => log.EntityName).HasMaxLength(100).IsRequired();
            entity.Property(log => log.Details).HasMaxLength(2000);
            entity.HasOne(log => log.User)
                .WithMany(user => user.AuditLogs)
                .HasForeignKey(log => log.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
