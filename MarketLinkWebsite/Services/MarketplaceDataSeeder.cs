using System.Globalization;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Services;

public sealed class MarketplaceDataSeeder
{
    private readonly ApplicationDbContext db;
    private readonly UserManager<ApplicationUser> userManager;
    private readonly IConfiguration configuration;
    private readonly ILogger<MarketplaceDataSeeder> logger;

    public MarketplaceDataSeeder(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        ILogger<MarketplaceDataSeeder> logger)
    {
        this.db = db;
        this.userManager = userManager;
        this.configuration = configuration;
        this.logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!configuration.GetValue<bool>("Seed:MarketplaceData"))
        {
            return;
        }

        var password = configuration["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Marketplace data is enabled but Seed:DemoPassword is empty, so no demo accounts were created.");
            return;
        }

        var farmers = await SeedFarmersAsync(password, cancellationToken);
        var customers = await SeedCustomersAsync(password, cancellationToken);

        await SeedOrdersAsync(customers, cancellationToken);
        await SeedReviewsAsync(cancellationToken);
        await SeedFavouritesAsync(customers, cancellationToken);
        await SeedAuditLogsAsync(customers, cancellationToken);
    }

    private async Task<List<FarmerProfile>> SeedFarmersAsync(string password, CancellationToken cancellationToken)
    {
        var profiles = new List<FarmerProfile>();

        foreach (var farm in FarmDefinitions())
        {
            var profile = await EnsureFarmerAsync(farm, password, cancellationToken);
            if (profile is not null)
            {
                profiles.Add(profile);
            }
        }

        return profiles;
    }

    private async Task<FarmerProfile?> EnsureFarmerAsync(FarmDefinition farm, string password, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(farm.Email);

        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = farm.Email,
                Email = farm.Email,
                EmailConfirmed = true,
                FirstName = farm.FirstName,
                LastName = farm.LastName,
                PhoneNumber = farm.Phone,
                IsActive = true,
                CreatedAt = DateTime.UtcNow.AddDays(-180)
            };

            var created = await userManager.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                logger.LogWarning("Could not create the demo farmer {Email}: {Errors}", farm.Email, string.Join(" ", created.Errors.Select(error => error.Description)));
                return null;
            }

            await userManager.AddToRoleAsync(user, "Farmer");
        }

        var profile = await db.FarmerProfiles.FirstOrDefaultAsync(item => item.UserId == user.Id, cancellationToken);
        if (profile is not null)
        {
            await SeedPickupSlotsAsync(profile, cancellationToken);
            return profile;
        }

        profile = new FarmerProfile
        {
            UserId = user.Id,
            FarmName = farm.FarmName,
            Description = farm.Description,
            Address = farm.Address,
            City = farm.City,
            OperatingDays = farm.OperatingDays,
            PickupWindows = farm.PickupWindows,
            OrderCutoffHours = farm.OrderCutoffHours,
            Latitude = farm.Latitude,
            Longitude = farm.Longitude,
            Status = farm.Status,
            CreatedAt = DateTime.UtcNow.AddDays(-175)
        };

        db.FarmerProfiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken);

        await LinkMarketsAsync(profile, farm, cancellationToken);
        await AddProductsAsync(profile, farm, cancellationToken);
        await SeedPickupSlotsAsync(profile, cancellationToken);

        return profile;
    }

    /// <summary>
    /// Gives a demo farm the same published pickup slots as its profile window, so
    /// the slot picker has something to show before the farmer edits it. A farm that
    /// already manages its own slots is left alone.
    /// </summary>
    private async Task SeedPickupSlotsAsync(FarmerProfile profile, CancellationToken cancellationToken)
    {
        if (await db.PickupSlots.AnyAsync(slot => slot.FarmerProfileId == profile.Id, cancellationToken))
        {
            return;
        }

        var windows = (profile.PickupWindows ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var days = (profile.OperatingDays ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var day in days)
        {
            if (!Enum.TryParse<DayOfWeek>(day, ignoreCase: true, out var weekday))
            {
                continue;
            }

            foreach (var window in windows)
            {
                var times = window.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (times.Length != 2
                    || !DateTime.TryParse(times[0].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var from)
                    || !DateTime.TryParse(times[1].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var to))
                {
                    continue;
                }

                db.PickupSlots.Add(new PickupSlot
                {
                    FarmerProfileId = profile.Id,
                    DayOfWeek = (int)weekday,
                    StartTime = $"{from.Hour:D2}:{from.Minute:D2}",
                    EndTime = $"{to.Hour:D2}:{to.Minute:D2}",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddDays(-170)
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task LinkMarketsAsync(FarmerProfile profile, FarmDefinition farm, CancellationToken cancellationToken)
    {
        var markets = await db.Markets.Where(item => item.IsActive).ToDictionaryAsync(item => item.Name, cancellationToken);

        var stallIndex = 1;
        foreach (var marketName in farm.Markets)
        {
            if (!markets.TryGetValue(marketName, out var market))
            {
                continue;
            }

            var alreadyLinked = await db.MarketFarmers
                .AnyAsync(item => item.MarketId == market.Id && item.FarmerProfileId == profile.Id, cancellationToken);

            if (alreadyLinked)
            {
                continue;
            }

            db.MarketFarmers.Add(new MarketFarmer
            {
                MarketId = market.Id,
                FarmerProfileId = profile.Id,
                StallNumber = $"{market.Name.Split(' ')[0][..1].ToUpperInvariant()}-{stallIndex:D2}",
                JoinedDate = DateTime.UtcNow.AddDays(-170)
            });

            stallIndex++;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task AddProductsAsync(FarmerProfile profile, FarmDefinition farm, CancellationToken cancellationToken)
    {
        if (await db.Products.AnyAsync(item => item.FarmerProfileId == profile.Id, cancellationToken))
        {
            return;
        }

        var categories = await db.Categories.ToDictionaryAsync(item => item.Name, cancellationToken);
        if (categories.Count == 0)
        {
            return;
        }

        foreach (var item in farm.Products)
        {
            if (!categories.TryGetValue(item.Category, out var category))
            {
                continue;
            }

            var product = new Product
            {
                FarmerProfileId = profile.Id,
                CategoryId = category.Id,
                Name = item.Name,
                Description = item.Description,
                Price = item.Price,
                Unit = item.Unit,
                ImageUrl = item.ImageUrl,
                IsOrganic = item.IsOrganic,
                IsAvailable = true,
                CreatedAt = DateTime.UtcNow.AddDays(-160),
                UpdatedAt = DateTime.UtcNow.AddDays(-12)
            };

            product.Inventory = new Inventory
            {
                QuantityAvailable = item.Stock,
                ReorderThreshold = item.ReorderThreshold,
                LastUpdated = DateTime.UtcNow.AddDays(-2)
            };

            product.WeeklyStockPlan = new WeeklyStockPlan
            {
                // The weekly figures are filled in so a farmer can see the shape of a
                // template straight away, but the rollover stays off until the farmer
                // switches it on from the inventory page. Otherwise the demo catalogue
                // would quietly empty itself on a weekday a crop is not harvested.
                Enabled = false,
                MondayStock = item.Monday,
                TuesdayStock = item.Tuesday,
                WednesdayStock = item.Wednesday,
                ThursdayStock = item.Thursday,
                FridayStock = item.Friday,
                SaturdayStock = item.Saturday,
                SundayStock = item.Sunday,
                UpdatedAt = DateTime.UtcNow.AddDays(-7)
            };

            db.Products.Add(product);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<ApplicationUser>> SeedCustomersAsync(string password, CancellationToken cancellationToken)
    {
        var customers = new List<ApplicationUser>();

        foreach (var definition in CustomerDefinitions())
        {
            var user = await userManager.FindByEmailAsync(definition.Email);

            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = definition.Email,
                    Email = definition.Email,
                    EmailConfirmed = true,
                    FirstName = definition.FirstName,
                    LastName = definition.LastName,
                    PhoneNumber = definition.Phone,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddDays(-150)
                };

                var created = await userManager.CreateAsync(user, password);
                if (!created.Succeeded)
                {
                    logger.LogWarning("Could not create the demo customer {Email}: {Errors}", definition.Email, string.Join(" ", created.Errors.Select(error => error.Description)));
                    continue;
                }

                await userManager.AddToRoleAsync(user, "Customer");
            }

            var hasAddress = await db.Addresses.AnyAsync(item => item.UserId == user.Id, cancellationToken);
            if (!hasAddress)
            {
                db.Addresses.Add(new Address
                {
                    UserId = user.Id,
                    Label = "Home",
                    RecipientName = $"{user.FirstName} {user.LastName}",
                    Phone = definition.Phone,
                    AddressLine = definition.Address,
                    City = definition.City,
                    Region = "OR",
                    PostalCode = definition.PostalCode,
                    IsDefault = true
                });

                await db.SaveChangesAsync(cancellationToken);
            }

            customers.Add(user);
        }

        return customers;
    }

    private async Task SeedOrdersAsync(List<ApplicationUser> customers, CancellationToken cancellationToken)
    {
        if (customers.Count == 0)
        {
            return;
        }

        if (await db.Orders.AnyAsync(cancellationToken))
        {
            return;
        }

        var markets = await db.Markets.ToDictionaryAsync(item => item.Name, cancellationToken);
        var products = await db.Products
            .Include(item => item.FarmerProfile)
            .ToListAsync(cancellationToken);

        if (markets.Count == 0 || products.Count == 0)
        {
            return;
        }

        var usedNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var plan in OrderPlans())
        {
            var customer = MatchUser(customers, plan.CustomerEmail);
            if (customer is null)
            {
                continue;
            }

            if (!markets.TryGetValue(plan.Market, out var market))
            {
                continue;
            }

            var lines = new List<OrderItem>();
            var total = 0m;

            foreach (var requested in plan.Items)
            {
                var product = products.FirstOrDefault(item =>
                    item.FarmerProfile.FarmName == requested.FarmName &&
                    string.Equals(item.Name, requested.ProductName, StringComparison.OrdinalIgnoreCase));

                if (product is null)
                {
                    continue;
                }

                lines.Add(new OrderItem
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    FarmerName = product.FarmerProfile.FarmName,
                    Unit = product.Unit,
                    Quantity = requested.Quantity,
                    UnitPrice = product.Price
                });

                total += product.Price * requested.Quantity;
            }

            if (lines.Count == 0)
            {
                continue;
            }

            var orderNumber = BuildOrderNumber(plan.OrderedAt, usedNumbers);

            var order = new Order
            {
                OrderNumber = orderNumber,
                UserId = customer.Id,
                MarketId = market.Id,
                PickupDate = plan.PickupDate,
                PickupTimeSlot = plan.TimeSlot,
                PickupAddress = market.Address,
                City = market.City,
                Phone = customer.PhoneNumber ?? string.Empty,
                Notes = plan.Notes,
                PaymentMethod = "Cash at pickup",
                TotalAmount = total,
                Status = plan.Status,
                PaymentStatus = plan.PaymentStatus,
                OrderDate = plan.OrderedAt,
                UpdatedAt = plan.LatestUpdate
            };

            order.OrderItems = lines;
            order.StatusHistory = BuildStatusHistory(plan, customer.Id);

            db.Orders.Add(order);
            await db.SaveChangesAsync(cancellationToken);

            await AddCustomerNotificationsAsync(order, plan, cancellationToken);
            await AddFarmerNotificationsAsync(order, products, cancellationToken);
        }

        await RecalculateFarmerRatingsAsync(cancellationToken);
    }

    private static List<OrderStatusHistory> BuildStatusHistory(OrderPlan plan, string customerId)
    {
        var history = new List<OrderStatusHistory>
        {
            new()
            {
                Status = OrderStatus.Pending,
                ChangedById = customerId,
                Note = "Order placed by the customer.",
                ChangedAt = plan.OrderedAt
            }
        };

        if (plan.Status == OrderStatus.Pending)
        {
            return history;
        }

        history.Add(new OrderStatusHistory
        {
            Status = OrderStatus.Accepted,
            ChangedById = null,
            Note = "The farmer accepted this reservation.",
            ChangedAt = plan.OrderedAt.AddHours(3)
        });

        if (plan.Status is OrderStatus.Declined or OrderStatus.Cancelled)
        {
            history.Add(new OrderStatusHistory
            {
                Status = plan.Status,
                ChangedById = null,
                Note = plan.Status == OrderStatus.Declined ? "The farmer could not fulfil this order." : "The order was cancelled.",
                ChangedAt = plan.OrderedAt.AddHours(6)
            });

            return history;
        }

        history.Add(new OrderStatusHistory
        {
            Status = OrderStatus.Preparing,
            ChangedById = null,
            Note = "The farmer started preparing the harvest.",
            ChangedAt = plan.OrderedAt.AddHours(8)
        });

        if (plan.Status == OrderStatus.Preparing)
        {
            return history;
        }

        history.Add(new OrderStatusHistory
        {
            Status = OrderStatus.ReadyForPickup,
            ChangedById = null,
            Note = "The order is packed and waiting at the stall.",
            ChangedAt = plan.OrderedAt.AddHours(26)
        });

        if (plan.Status == OrderStatus.ReadyForPickup)
        {
            return history;
        }

        history.Add(new OrderStatusHistory
        {
            Status = OrderStatus.Completed,
            ChangedById = null,
            Note = "Picked up by the customer.",
            ChangedAt = plan.PickupDate.AddHours(1)
        });

        return history;
    }

    private async Task AddCustomerNotificationsAsync(Order order, OrderPlan plan, CancellationToken cancellationToken)
    {
        db.Notifications.Add(new Notification
        {
            UserId = order.UserId,
            Type = NotificationType.Order,
            Title = $"Reservation {order.OrderNumber} received",
            Message = "We sent your request to the farmer. You will be notified when it is confirmed.",
            ActionUrl = $"/Customer/Orders/Details/{order.Id}",
            IsRead = plan.Status != OrderStatus.Pending,
            CreatedAt = order.OrderDate,
            ReadAt = plan.Status != OrderStatus.Pending ? order.OrderDate.AddHours(1) : null
        });

        if (plan.Status == OrderStatus.Completed)
        {
            db.Notifications.Add(new Notification
            {
                UserId = order.UserId,
                Type = NotificationType.Review,
                Title = "How was your pickup?",
                Message = "Share a review to help other shoppers pick with confidence.",
                ActionUrl = $"/Customer/Orders/Details/{order.Id}",
                IsRead = false,
                CreatedAt = order.UpdatedAt.AddHours(2)
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task AddFarmerNotificationsAsync(Order order, List<Product> products, CancellationToken cancellationToken)
    {
        var farmerIds = order.OrderItems
            .Select(item => products.FirstOrDefault(product => product.Id == item.ProductId))
            .Where(product => product is not null)
            .Select(product => product!.FarmerProfileId)
            .Distinct();

        var farmerUserIds = await db.FarmerProfiles
            .Where(item => farmerIds.Contains(item.Id))
            .Select(item => item.UserId)
            .ToListAsync(cancellationToken);

        foreach (var userId in farmerUserIds)
        {
            db.Notifications.Add(new Notification
            {
                UserId = userId,
                Type = NotificationType.Order,
                Title = $"New reservation {order.OrderNumber}",
                Message = "A customer reserved items from your stall. Open the order to confirm or decline.",
                ActionUrl = $"/Farmer/Orders/Details/{order.Id}",
                IsRead = order.Status != OrderStatus.Pending,
                CreatedAt = order.OrderDate,
                ReadAt = order.Status != OrderStatus.Pending ? order.OrderDate.AddHours(2) : null
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RecalculateFarmerRatingsAsync(CancellationToken cancellationToken)
    {
        var profiles = await db.FarmerProfiles.ToListAsync(cancellationToken);

        foreach (var profile in profiles)
        {
            var productIds = await db.Products
                .Where(item => item.FarmerProfileId == profile.Id)
                .Select(item => item.Id)
                .ToListAsync(cancellationToken);

            var reviews = await db.Reviews
                .Where(item =>
                    (item.FarmerProfileId == profile.Id) ||
                    (item.ProductId != null && productIds.Contains(item.ProductId.Value)))
                .Select(item => item.Rating)
                .ToListAsync(cancellationToken);

            if (reviews.Count == 0)
            {
                continue;
            }

            profile.Rating = Math.Round((decimal)reviews.Average(), 2);
            profile.ReviewCount = reviews.Count;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedReviewsAsync(CancellationToken cancellationToken)
    {
        if (await db.Reviews.AnyAsync(cancellationToken))
        {
            return;
        }

        var completedOrders = await db.Orders
            .Include(item => item.OrderItems)
            .Where(item => item.Status == OrderStatus.Completed)
            .OrderBy(item => item.OrderDate)
            .ToListAsync(cancellationToken);

        if (completedOrders.Count == 0)
        {
            return;
        }

        var profiles = await db.FarmerProfiles.ToDictionaryAsync(item => item.Id, cancellationToken);
        var snippets = ReviewSnippets();

        foreach (var order in completedOrders)
        {
            foreach (var item in order.OrderItems)
            {
                if (item.ProductId is null)
                {
                    continue;
                }

                if (!snippets.TryGetValue(item.ProductName, out var snippet))
                {
                    continue;
                }

                var alreadyReviewed = await db.Reviews
                    .AnyAsync(review => review.UserId == order.UserId && review.ProductId == item.ProductId, cancellationToken);

                if (alreadyReviewed)
                {
                    continue;
                }

                db.Reviews.Add(new Review
                {
                    UserId = order.UserId,
                    ProductId = item.ProductId,
                    Rating = snippet.Rating,
                    Title = snippet.Title,
                    Comment = snippet.Comment,
                    VerifiedPurchase = true,
                    HelpfulCount = snippet.HelpfulCount,
                    CreatedAt = order.UpdatedAt.AddHours(6),
                    UpdatedAt = order.UpdatedAt.AddHours(6)
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        await SeedFarmerReviewsAsync(completedOrders, profiles, snippets, cancellationToken);
        await RecalculateFarmerRatingsAsync(cancellationToken);
    }

    private async Task SeedFarmerReviewsAsync(
        List<Order> completedOrders,
        Dictionary<int, FarmerProfile> profiles,
        Dictionary<string, ReviewSnippet> snippets,
        CancellationToken cancellationToken)
    {
        var farmerPraises = new (string FarmName, string ReviewerEmail, int Rating, string Title, string Comment, string? Reply)[]
        {
            ("Greenwood Farm", "aisha.khan@example.test", 5, "The soil really shows in the taste", "Everything arrived crisp and tasted like it was picked that morning. The spinach is the best I have bought at a weekend market.", "Thank you! We harvest the greens at dawn on pickup day, so they reach you the same afternoon."),
            ("Greenwood Farm", "marcus.webb@example.test", 4, "Great product, friendly stall", "Consistent quality all season. The only note is that I would love a wider range of herbs.", "Noted, we are adding more herb beds for the next rotation."),
            ("Raza Orchard", "jordan.lee@example.test", 5, "The pears were perfect", "Ripe but firm, exactly as described. A really careful packer too, nothing was bruised.", null),
            ("Sunrise Dairy", "priya.sharma@example.test", 5, "Milk that actually tastes like milk", "Cheddar and yoghurt were both excellent. The eggs have the orange yolks I grew up with.", "Thank you, the herd is out on pasture every day from April."),
            ("Sunrise Dairy", "marcus.webb@example.test", 4, "Very fresh, would order again", "The goat cheese is a standout. Pickup queue was a little long at 9 but the staff were lovely.", null),
            ("Mill Street Bakery", "aisha.khan@example.test", 5, "Best sourdough in the city", "Crackling crust, open crumb and it stayed fresh for two days. I now plan my Saturday around it.", "That is the highest compliment a baker can get, thank you!")
        };

        foreach (var praise in farmerPraises)
        {
            var profile = profiles.Values.FirstOrDefault(item => item.FarmName == praise.FarmName);
            if (profile is null)
            {
                continue;
            }

            var reviewer = await db.Users.FirstOrDefaultAsync(item => item.Email == praise.ReviewerEmail, cancellationToken);
            if (reviewer is null)
            {
                continue;
            }

            var purchased = completedOrders
                .Where(order => order.UserId == reviewer.Id && order.OrderItems.Any(item => item.FarmerName == praise.FarmName))
                .ToList();

            var alreadyReviewed = await db.Reviews
                .AnyAsync(review => review.UserId == reviewer.Id && review.FarmerProfileId == profile.Id, cancellationToken);

            if (purchased.Count == 0 || alreadyReviewed)
            {
                continue;
            }

            var created = purchased.Max(order => order.UpdatedAt).AddHours(9);

            db.Reviews.Add(new Review
            {
                UserId = reviewer.Id,
                FarmerProfileId = profile.Id,
                Rating = praise.Rating,
                Title = praise.Title,
                Comment = praise.Comment,
                VerifiedPurchase = true,
                HelpfulCount = 3 + (reviewer.Id.Length % 11),
                FarmerReply = praise.Reply,
                FarmerRepliedAt = praise.Reply is null ? null : created.AddHours(4),
                CreatedAt = created,
                UpdatedAt = created
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static Dictionary<string, ReviewSnippet> ReviewSnippets()
    {
        return new Dictionary<string, ReviewSnippet>(StringComparer.OrdinalIgnoreCase)
        {
            ["Heirloom Tomatoes"] = new(5, "Genuinely sun-ripened", "No watery supermarket flavour at all. These were still warm from the sun when I got them home.", 14),
            ["Rainbow Carrots"] = new(4, "Pretty and sweet", "Beautiful colour in the bunch and very crisp. A little smaller than I expected but that is normal for small farms.", 9),
            ["Baby Spinach"] = new(5, "Stays crisp for days", "Washed, dry and leafy. I used a whole bag through the week and it never went slimy.", 21),
            ["Tuscan Kale"] = new(4, "Sturdy leaves, no stems", "Perfect for a braise. Picked the same day and the leaves were still firm.", 7),
            ["Fresh Basil"] = new(5, "Fragrance fills the kitchen", "The smell when you open the bag is unreal. I made pesto two days later and it was still vivid.", 18),
            ["Sweet Corn"] = new(5, "Sugary and milky", "Best corn I have had outside a homegrown patch. We ate it within the hour.", 12),
            ["Honeycrisp Apples"] = new(5, "Explosive crunch", "Properly crisp with a sweet finish. My kids took them straight out of the bag.", 16),
            ["Bartlett Pears"] = new(4, "Ripe on arrival", "Baked the same evening and turned out beautifully. A softer texture than expected.", 6),
            ["Rainier Cherries"] = new(5, "Worth the price", "Small batch but every one was firm and juicy. There were some seconds in the bag for free, appreciated.", 11),
            ["Fresh Figs"] = new(5, "Soft and honeyed", "Eaten within an hour of getting home. You can tell they were not shipped.", 8),
            ["Whole Milk"] = new(5, "Thick, rich and cold", "Still cold from the stall. The cream line at the top tells you everything.", 10),
            ["Farmhouse Cheddar"] = new(5, "Proper mature cheddar", "Crumbly, sharp and aged properly. Made a very good sandwich.", 13),
            ["Greek Yogurt"] = new(4, "Thick without being tart", "Strained to a good texture. I preferred mine a little sweeter, but the quality is obvious.", 5),
            ["Pasture-Raised Eggs"] = new(5, "Orange yolks every time", "My benchmark for eggs. The shells were clean and the yolks stood up in the pan.", 24),
            ["Goat Cheese"] = new(5, "Soft and herby", "Rolled in herbs, so it looks as good as it tastes. Disappeared quickly.", 9),
            ["Sourdough Loaf"] = new(5, "Crackles when you cut it", "A genuinely long ferment. The crumb was open and the crust sang on the way home.", 26),
            ["Multigrain Bread"] = new(4, "Seedy and filling", "Good texture, keeps well for a few days in a bread bag.", 7),
            ["Cinnamon Rolls"] = new(5, "Cardamom is not a subtle flavour", "Still warm when I got to the car. My kids fought over the last one.", 15),
            ["Butter Croissants"] = new(5, "Laminated properly", "Shattering, hollow layers and a real butter taste. Worth the queue.", 17)
        };
    }

    private async Task SeedFavouritesAsync(List<ApplicationUser> customers, CancellationToken cancellationToken)
    {
        if (customers.Count == 0)
        {
            return;
        }

        if (await db.FavouriteFarmers.AnyAsync(cancellationToken) || await db.FavouriteProducts.AnyAsync(cancellationToken))
        {
            return;
        }

        var profiles = await db.FarmerProfiles.Where(item => item.Status == FarmerStatus.Active).ToListAsync(cancellationToken);
        var products = await db.Products
            .Include(item => item.FarmerProfile)
            .Where(item => item.FarmerProfile.Status == FarmerStatus.Active)
            .ToListAsync(cancellationToken);

        if (profiles.Count == 0 || products.Count == 0)
        {
            return;
        }

        for (var index = 0; index < customers.Count; index++)
        {
            var customer = customers[index];

            var favouriteFarm = profiles[index % profiles.Count];
            if (!await db.FavouriteFarmers.AnyAsync(item => item.UserId == customer.Id && item.FarmerProfileId == favouriteFarm.Id, cancellationToken))
            {
                db.FavouriteFarmers.Add(new FavouriteFarmer
                {
                    UserId = customer.Id,
                    FarmerProfileId = favouriteFarm.Id,
                    AddedAt = DateTime.UtcNow.AddDays(-30 + index)
                });
            }

            var chosen = products.Where(item => item.FarmerProfileId == favouriteFarm.Id).Take(2).ToList();
            foreach (var product in chosen)
            {
                if (!await db.FavouriteProducts.AnyAsync(item => item.UserId == customer.Id && item.ProductId == product.Id, cancellationToken))
                {
                    db.FavouriteProducts.Add(new FavouriteProduct
                    {
                        UserId = customer.Id,
                        ProductId = product.Id,
                        AddedAt = DateTime.UtcNow.AddDays(-25 + index)
                    });
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedAuditLogsAsync(List<ApplicationUser> customers, CancellationToken cancellationToken)
    {
        if (await db.AuditLogs.AnyAsync(cancellationToken))
        {
            return;
        }

        var adminEmail = configuration["Seed:AdminEmail"];
        var admin = string.IsNullOrWhiteSpace(adminEmail) ? null : await userManager.FindByEmailAsync(adminEmail);
        var adminId = admin?.Id;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = adminId,
            Action = "ApproveFarmer",
            EntityName = "FarmerProfile",
            Details = "Approved Greenwood Farm and linked the farmer to three markets.",
            Timestamp = DateTime.UtcNow.AddDays(-170)
        });

        db.AuditLogs.Add(new AuditLog
        {
            UserId = adminId,
            Action = "SuspendFarmer",
            EntityName = "FarmerProfile",
            Details = "Suspended Brooks Family Meats pending a food-safety document.",
            Timestamp = DateTime.UtcNow.AddDays(-24)
        });

        db.AuditLogs.Add(new AuditLog
        {
            UserId = adminId,
            Action = "UpdateOrder",
            EntityName = "Order",
            Details = "Marked a reservation as paid after the customer paid cash at the stall.",
            Timestamp = DateTime.UtcNow.AddDays(-9)
        });

        db.AuditLogs.Add(new AuditLog
        {
            UserId = adminId,
            Action = "CreateMarket",
            EntityName = "Market",
            Details = "Added Old Mill Saturday Market to the pickup network.",
            Timestamp = DateTime.UtcNow.AddDays(-60)
        });

        foreach (var customer in customers.Take(2))
        {
            db.Notifications.Add(new Notification
            {
                UserId = customer.Id,
                Type = NotificationType.System,
                Title = "Welcome to MarketLink",
                Message = "Browse seasonal product from local farmers and reserve it for pickup at a market near you.",
                ActionUrl = "/Products",
                IsRead = true,
                CreatedAt = customer.CreatedAt,
                ReadAt = customer.CreatedAt.AddHours(2)
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static ApplicationUser? MatchUser(List<ApplicationUser> users, string email)
    {
        return users.FirstOrDefault(user => string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildOrderNumber(DateTime orderedAt, HashSet<string> used)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var candidate = $"ML-{orderedAt:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
            if (used.Add(candidate))
            {
                return candidate;
            }
        }

        var fallback = $"ML-{orderedAt:yyyyMMdd}-{used.Count:D6}";
        used.Add(fallback);
        return fallback;
    }

    private static IEnumerable<FarmDefinition> FarmDefinitions()
    {
        yield return new FarmDefinition
        {
            Email = "maya@greenwoodfarm.test",
            FirstName = "Maya",
            LastName = "Thompson",
            Phone = "+1 555 010 2200",
            FarmName = "Greenwood Farm",
            Description = "Seasonal vegetables, leafy greens and herbs grown with care for the soil on twelve acres outside the city.",
            Address = "18 Willow Creek Road",
            City = "Northbridge",
            OperatingDays = "Saturday,Sunday",
            PickupWindows = "09:00 AM - 12:00 PM",
            OrderCutoffHours = 12,
            Latitude = 33.6772000m,
            Longitude = 73.0356000m,
            Status = FarmerStatus.Active,
            Markets = ["Downtown Farmers Market", "Community Green Square", "Old Mill Saturday Market"],
            Products =
            [
                Product("Heirloom Tomatoes", "Sun-ripened heirloom tomatoes picked the morning of pickup.", 12m, UnitType.Kg, "Vegetables", 40, 10, 30, 30, 25, 25, 20, 40, 35, true, "https://images.unsplash.com/photo-1546094096-0df4bcaaa337?auto=format&fit=crop&w=600&q=80"),
                Product("Rainbow Carrots", "Sweet, crisp carrots in a rainbow bunch with the tops on.", 6.5m, UnitType.Bundle, "Vegetables", 55, 15, 40, 40, 35, 35, 30, 50, 45, true, "https://images.unsplash.com/photo-1445282768818-728615cc910a?auto=format&fit=crop&w=600&q=80"),
                Product("Baby Spinach", "Tender baby spinach, triple washed and ready for the pan.", 4.25m, UnitType.Bundle, "Vegetables", 35, 10, 25, 25, 25, 25, 20, 30, 30, true, "https://images.unsplash.com/photo-1576045057995-568f588f82fb?auto=format&fit=crop&w=600&q=80"),
                Product("Tuscan Kale", "Dark Tuscan kale leaves, sturdy enough for soups and braises.", 3.75m, UnitType.Bundle, "Vegetables", 30, 10, 20, 20, 20, 20, 18, 28, 26, true, "https://images.unsplash.com/photo-1524179091875-bf99a9a6af57?auto=format&fit=crop&w=600&q=80"),
                Product("Fresh Basil", "Fragrant basil bunches cut to order.", 3m, UnitType.Bundle, "Vegetables", 24, 8, 18, 18, 15, 15, 12, 22, 20, true, "https://images.unsplash.com/photo-1618375569909-3c8616cf7733?auto=format&fit=crop&w=600&q=80"),
                Product("Sweet Corn", "Bi-colour sweet corn, sugar still in the kernels.", 5m, UnitType.Dozen, "Vegetables", 45, 12, 0, 0, 0, 0, 0, 45, 40, false, "https://images.unsplash.com/photo-1551754655-cd27e38d2076?auto=format&fit=crop&w=600&q=80")
            ]
        };

        yield return new FarmDefinition
        {
            Email = "bilal@razaorchard.test",
            FirstName = "Bilal",
            LastName = "Raza",
            Phone = "+1 555 010 2201",
            FarmName = "Raza Orchard",
            Description = "A third-generation orchard growing apples, pears, cherries and figs in the hills above Riverside.",
            Address = "77 Orchard Ridge Road",
            City = "Riverside",
            OperatingDays = "Wednesday,Friday,Saturday",
            PickupWindows = "07:00 AM - 01:00 PM",
            OrderCutoffHours = 18,
            Latitude = 33.6915000m,
            Longitude = 73.0598000m,
            Status = FarmerStatus.Active,
            Markets = ["Downtown Farmers Market", "Riverside Growers Market", "Harbor District Market"],
            Products =
            [
                Product("Honeycrisp Apples", "Crisp, sweet Honeycrisp apples from the north block.", 8m, UnitType.Kg, "Fruits", 60, 15, 40, 40, 40, 40, 35, 55, 50, true, "https://images.unsplash.com/photo-1560806887-1e4cd0b6cbd6?auto=format&fit=crop&w=600&q=80"),
                Product("Bartlett Pears", "Softening Bartlett pears, ideal for baking.", 7.25m, UnitType.Kg, "Fruits", 45, 12, 0, 0, 30, 30, 30, 42, 38, true, "https://images.unsplash.com/photo-1568702846914-96b305d2aaeb?auto=format&fit=crop&w=600&q=80"),
                Product("Rainier Cherries", "Deep-gold Rainier cherries, tree-ripened.", 14m, UnitType.Kg, "Fruits", 22, 8, 0, 0, 0, 0, 0, 22, 0, false, "https://images.unsplash.com/photo-1528821128474-27f963b062bf?auto=format&fit=crop&w=600&q=80"),
                Product("Fresh Figs", "Soft-skinned figs packed in shallow trays.", 12.5m, UnitType.Bundle, "Fruits", 18, 6, 0, 0, 0, 0, 0, 18, 0, false, "https://images.unsplash.com/photo-1601379329542-31c59347e2b7?auto=format&fit=crop&w=600&q=80")
            ]
        };

        yield return new FarmDefinition
        {
            Email = "sofia@sunrisedairy.test",
            FirstName = "Sofia",
            LastName = "Alvarez",
            Phone = "+1 555 010 2202",
            FarmName = "Sunrise Dairy",
            Description = "A forty-cow Jersey herd making milk, yoghurt and cheese delivered cold to the market every morning.",
            Address = "4 Meadow Rise",
            City = "Northbridge",
            OperatingDays = "Saturday,Sunday",
            PickupWindows = "08:00 AM - 11:00 AM",
            OrderCutoffHours = 10,
            Latitude = 33.6605000m,
            Longitude = 73.0752000m,
            Status = FarmerStatus.Active,
            Markets = ["Downtown Farmers Market", "Community Green Square"],
            Products =
            [
                Product("Whole Milk", "Jersey whole milk in returnable glass bottles.", 4.5m, UnitType.Litre, "Dairy", 80, 20, 50, 50, 50, 50, 45, 70, 65, false, "https://images.unsplash.com/photo-1550583724-b2692b85b150?auto=format&fit=crop&w=600&q=80"),
                Product("Farmhouse Cheddar", "Cloth-bound mature cheddar, aged eight weeks.", 11m, UnitType.Piece, "Dairy", 30, 8, 20, 20, 18, 18, 15, 28, 25, false, "https://images.unsplash.com/photo-1486297678162-eb2a19b0a32d?auto=format&fit=crop&w=600&q=80"),
                Product("Greek Yogurt", "Strained, high-protein Greek yogurt.", 6m, UnitType.Litre, "Dairy", 45, 12, 30, 30, 30, 30, 28, 40, 36, false, "https://images.unsplash.com/photo-1488477181946-6428a0291777?auto=format&fit=crop&w=600&q=80"),
                Product("Pasture-Raised Eggs", "Golden-yolk eggs from hens that range free daily.", 9m, UnitType.Dozen, "Dairy", 50, 12, 35, 35, 35, 35, 30, 45, 40, false, "https://images.unsplash.com/photo-1582722872445-44dc5f7e3c8f?auto=format&fit=crop&w=600&q=80"),
                Product("Goat Cheese", "Soft fresh goat cheese rolled in herbs.", 13m, UnitType.Piece, "Dairy", 20, 6, 12, 12, 12, 12, 10, 18, 16, false, "https://images.unsplash.com/photo-1486297678162-eb2a19b0a32d?auto=format&fit=crop&w=600&q=80")
            ]
        };

        yield return new FarmDefinition
        {
            Email = "james@millstreetbakery.test",
            FirstName = "James",
            LastName = "Okafor",
            Phone = "+1 555 010 2203",
            FarmName = "Mill Street Bakery",
            Description = "Long-fermentation sourdough and small-batch viennoiserie baked from scratch before every market.",
            Address = "12 Mill Street",
            City = "Downtown",
            OperatingDays = "Saturday,Sunday,Friday",
            PickupWindows = "01:00 PM - 05:00 PM",
            OrderCutoffHours = 8,
            Latitude = 33.7048000m,
            Longitude = 73.0312000m,
            Status = FarmerStatus.Active,
            Markets = ["Downtown Farmers Market", "Harbor District Market", "Old Mill Saturday Market"],
            Products =
            [
                Product("Sourdough Loaf", "Thirty-hour sourdough with a dark crackled crust.", 8m, UnitType.Piece, "Baked goods", 36, 10, 25, 25, 22, 22, 20, 34, 32, true, "https://images.unsplash.com/photo-1585478259715-876acc5be8eb?auto=format&fit=crop&w=600&q=80"),
                Product("Multigrain Bread", "Seeded multigrain loaf made with wholemeal flour.", 7m, UnitType.Piece, "Baked goods", 28, 8, 20, 20, 18, 18, 16, 26, 24, false, "https://images.unsplash.com/photo-1509440159596-0249088772ff?auto=format&fit=crop&w=600&q=80"),
                Product("Cinnamon Rolls", "Cardamom cinnamon spirals with cream-cheese glaze.", 12m, UnitType.Dozen, "Baked goods", 20, 6, 0, 0, 0, 0, 0, 20, 18, false, "https://images.unsplash.com/photo-1509365465985-25d11c17e812?auto=format&fit=crop&w=600&q=80"),
                Product("Butter Croissants", "Laminated butter croissants, twenty-four hour proof.", 10m, UnitType.Dozen, "Baked goods", 24, 6, 0, 0, 0, 0, 0, 24, 22, false, "https://images.unsplash.com/photo-1555507036-ab1f4038808a?auto=format&fit=crop&w=600&q=80")
            ]
        };

        yield return new FarmDefinition
        {
            Email = "emily@cedarhillapiary.test",
            FirstName = "Emily",
            LastName = "Chen",
            Phone = "+1 555 010 2204",
            FarmName = "Cedar Hill Apiary",
            Description = "Forty hives on the cedar hillside producing wildflower honey and beeswax goods.",
            Address = "9 Cedar Hill Lane",
            City = "Riverside",
            OperatingDays = "Wednesday,Saturday",
            PickupWindows = "04:00 PM - 07:00 PM",
            OrderCutoffHours = 20,
            Latitude = 33.7152000m,
            Longitude = 73.0801000m,
            Status = FarmerStatus.PendingApproval,
            Markets = ["Riverside Growers Market"],
            Products =
            [
                Product("Wildflower Honey", "Raw clover and wildflower honey, never heated.", 15m, UnitType.Piece, "Dairy", 30, 8, 0, 0, 20, 0, 0, 20, 18, false, "https://images.unsplash.com/photo-1587049352846-4a222e784d38?auto=format&fit=crop&w=600&q=80"),
                Product("Creamy Honey", "Slow-spun creamy honey with a fine crystal texture.", 16m, UnitType.Piece, "Dairy", 24, 6, 0, 0, 16, 0, 0, 16, 14, false, "https://images.unsplash.com/photo-1558642452-9d2a7deb7f62?auto=format&fit=crop&w=600&q=80"),
                Product("Beeswax Candles", "Hand-dipped pure beeswax candles.", 12m, UnitType.Piece, "Dairy", 18, 5, 0, 0, 12, 0, 0, 12, 10, false, "https://images.unsplash.com/photo-1603006905003-be475563bc59?auto=format&fit=crop&w=600&q=80")
            ]
        };

        yield return new FarmDefinition
        {
            Email = "daniel@brooksfamilymeats.test",
            FirstName = "Daniel",
            LastName = "Brooks",
            Phone = "+1 555 010 2205",
            FarmName = "Brooks Family Meats",
            Description = "Pasture-raised beef, lamb and poultry from a fourth-generation family farm.",
            Address = "51 Quarry Road",
            City = "Northbridge",
            OperatingDays = "Friday,Saturday",
            PickupWindows = "02:00 PM - 06:00 PM",
            OrderCutoffHours = 14,
            Latitude = 33.6678000m,
            Longitude = 73.0189000m,
            Status = FarmerStatus.Suspended,
            Markets = ["Harbor District Market"],
            Products =
            [
                Product("Free-Range Chicken", "Whole free-range chicken, air-chilled.", 18m, UnitType.Piece, "Dairy", 25, 8, 0, 0, 0, 0, 18, 20, 0, false, "https://images.unsplash.com/photo-1587593810167-a84920ea0781?auto=format&fit=crop&w=600&q=80"),
                Product("Lamb Chops", "French-trimmed lamb chops, three weeks matured.", 26m, UnitType.Kg, "Dairy", 18, 6, 0, 0, 0, 0, 14, 16, 0, false, "https://images.unsplash.com/photo-1603360946369-dc9bb6258143?auto=format&fit=crop&w=600&q=80"),
                Product("Beef Mince", "British-style beef mince with a 20% fat ratio.", 14m, UnitType.Kg, "Dairy", 30, 8, 0, 0, 0, 0, 22, 26, 0, false, "https://images.unsplash.com/photo-1607623814075-e51df1bdc82f?auto=format&fit=crop&w=600&q=80")
            ]
        };
    }

    private static IEnumerable<CustomerDefinition> CustomerDefinitions()
    {
        yield return new CustomerDefinition
        {
            Email = "jordan.lee@example.test",
            FirstName = "Jordan",
            LastName = "Lee",
            Phone = "+1 555 010 3301",
            Address = "218 Willow Creek Lane",
            City = "Northbridge",
            PostalCode = "97218"
        };

        yield return new CustomerDefinition
        {
            Email = "priya.sharma@example.test",
            FirstName = "Priya",
            LastName = "Sharma",
            Phone = "+1 555 010 3302",
            Address = "44 Harbour View Road",
            City = "Riverside",
            PostalCode = "97230"
        };

        yield return new CustomerDefinition
        {
            Email = "marcus.webb@example.test",
            FirstName = "Marcus",
            LastName = "Webb",
            Phone = "+1 555 010 3303",
            Address = "9 Larkspur Court",
            City = "Downtown",
            PostalCode = "97204"
        };

        yield return new CustomerDefinition
        {
            Email = "aisha.khan@example.test",
            FirstName = "Aisha",
            LastName = "Khan",
            Phone = "+1 555 010 3304",
            Address = "302 Cedar Street",
            City = "Northbridge",
            PostalCode = "97211"
        };
    }

    private static IEnumerable<OrderPlan> OrderPlans()
    {
        var now = DateTime.UtcNow;

        yield return new OrderPlan
        {
            CustomerEmail = "jordan.lee@example.test",
            Market = "Downtown Farmers Market",
            OrderedAt = now.AddHours(-2),
            PickupDate = NextDayOf(now, DayOfWeek.Saturday),
            TimeSlot = "09:00 AM - 10:00 AM",
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            Notes = "Please pack the tomatoes in a separate bag.",
            Items =
            [
                new OrderLine("Greenwood Farm", "Heirloom Tomatoes", 2),
                new OrderLine("Greenwood Farm", "Baby Spinach", 2),
                new OrderLine("Sunrise Dairy", "Pasture-Raised Eggs", 1)
            ]
        };

        yield return new OrderPlan
        {
            CustomerEmail = "priya.sharma@example.test",
            Market = "Riverside Growers Market",
            OrderedAt = now.AddHours(-5),
            PickupDate = NextDayOf(now, DayOfWeek.Wednesday),
            TimeSlot = "04:00 PM - 05:00 PM",
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            Notes = string.Empty,
            Items =
            [
                new OrderLine("Raza Orchard", "Honeycrisp Apples", 3),
                new OrderLine("Raza Orchard", "Bartlett Pears", 2)
            ]
        };

        yield return new OrderPlan
        {
            CustomerEmail = "marcus.webb@example.test",
            Market = "Downtown Farmers Market",
            OrderedAt = now.AddDays(-1).AddHours(-6),
            PickupDate = NextDayOf(now, DayOfWeek.Saturday),
            TimeSlot = "09:00 AM - 10:00 AM",
            Status = OrderStatus.Accepted,
            PaymentStatus = PaymentStatus.Pending,
            Notes = string.Empty,
            Items =
            [
                new OrderLine("Mill Street Bakery", "Sourdough Loaf", 2),
                new OrderLine("Mill Street Bakery", "Butter Croissants", 1),
                new OrderLine("Greenwood Farm", "Fresh Basil", 2)
            ]
        };

        yield return new OrderPlan
        {
            CustomerEmail = "aisha.khan@example.test",
            Market = "Downtown Farmers Market",
            OrderedAt = now.AddDays(-1).AddHours(-9),
            PickupDate = NextDayOf(now, DayOfWeek.Saturday),
            TimeSlot = "10:00 AM - 11:00 AM",
            Status = OrderStatus.Preparing,
            PaymentStatus = PaymentStatus.Pending,
            Notes = "Collecting on behalf of the office, please have the crates ready.",
            Items =
            [
                new OrderLine("Sunrise Dairy", "Whole Milk", 6),
                new OrderLine("Sunrise Dairy", "Greek Yogurt", 2),
                new OrderLine("Sunrise Dairy", "Farmhouse Cheddar", 1)
            ]
        };

        yield return new OrderPlan
        {
            CustomerEmail = "jordan.lee@example.test",
            Market = "Community Green Square",
            OrderedAt = now.AddDays(-2).AddHours(-4),
            PickupDate = NextDayOf(now, DayOfWeek.Sunday),
            TimeSlot = "08:00 AM - 09:00 AM",
            Status = OrderStatus.ReadyForPickup,
            PaymentStatus = PaymentStatus.Pending,
            Notes = string.Empty,
            Items =
            [
                new OrderLine("Greenwood Farm", "Rainbow Carrots", 2),
                new OrderLine("Greenwood Farm", "Tuscan Kale", 2),
                new OrderLine("Sunrise Dairy", "Goat Cheese", 1)
            ]
        };

        yield return new OrderPlan
        {
            CustomerEmail = "priya.sharma@example.test",
            Market = "Community Green Square",
            OrderedAt = now.AddDays(-2).AddHours(-7),
            PickupDate = NextDayOf(now, DayOfWeek.Sunday),
            TimeSlot = "08:00 AM - 09:00 AM",
            Status = OrderStatus.ReadyForPickup,
            PaymentStatus = PaymentStatus.Pending,
            Notes = string.Empty,
            Items =
            [
                new OrderLine("Sunrise Dairy", "Pasture-Raised Eggs", 2),
                new OrderLine("Raza Orchard", "Rainier Cherries", 1)
            ]
        };

        yield return new OrderPlan
        {
            CustomerEmail = "marcus.webb@example.test",
            Market = "Downtown Farmers Market",
            OrderedAt = now.AddDays(-9).AddHours(-3),
            PickupDate = now.AddDays(-7),
            TimeSlot = "09:00 AM - 10:00 AM",
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Paid,
            Notes = string.Empty,
            Items =
            [
                new OrderLine("Greenwood Farm", "Heirloom Tomatoes", 3),
                new OrderLine("Greenwood Farm", "Sweet Corn", 2),
                new OrderLine("Mill Street Bakery", "Multigrain Bread", 1)
            ]
        };

        yield return new OrderPlan
        {
            CustomerEmail = "aisha.khan@example.test",
            Market = "Community Green Square",
            OrderedAt = now.AddDays(-12).AddHours(-5),
            PickupDate = now.AddDays(-10),
            TimeSlot = "10:00 AM - 11:00 AM",
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Paid,
            Notes = "Please text when you are parked.",
            Items =
            [
                new OrderLine("Raza Orchard", "Honeycrisp Apples", 2),
                new OrderLine("Raza Orchard", "Fresh Figs", 1),
                new OrderLine("Sunrise Dairy", "Whole Milk", 3)
            ]
        };

        yield return new OrderPlan
        {
            CustomerEmail = "jordan.lee@example.test",
            Market = "Downtown Farmers Market",
            OrderedAt = now.AddDays(-16).AddHours(-8),
            PickupDate = now.AddDays(-14),
            TimeSlot = "08:00 AM - 09:00 AM",
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Paid,
            Notes = string.Empty,
            Items =
            [
                new OrderLine("Greenwood Farm", "Baby Spinach", 3),
                new OrderLine("Greenwood Farm", "Fresh Basil", 1),
                new OrderLine("Sunrise Dairy", "Pasture-Raised Eggs", 2)
            ]
        };

        yield return new OrderPlan
        {
            CustomerEmail = "priya.sharma@example.test",
            Market = "Riverside Growers Market",
            OrderedAt = now.AddDays(-4).AddHours(-10),
            PickupDate = now.AddDays(-2),
            TimeSlot = "04:00 PM - 05:00 PM",
            Status = OrderStatus.Cancelled,
            PaymentStatus = PaymentStatus.Refunded,
            Notes = string.Empty,
            Items =
            [
                new OrderLine("Raza Orchard", "Bartlett Pears", 2)
            ]
        };

        yield return new OrderPlan
        {
            CustomerEmail = "marcus.webb@example.test",
            Market = "Harbor District Market",
            OrderedAt = now.AddDays(-5).AddHours(-2),
            PickupDate = now.AddDays(-3),
            TimeSlot = "02:00 PM - 03:00 PM",
            Status = OrderStatus.Declined,
            PaymentStatus = PaymentStatus.Refunded,
            Notes = string.Empty,
            Items =
            [
                new OrderLine("Cedar Hill Apiary", "Wildflower Honey", 2)
            ]
        };

        yield return new OrderPlan
        {
            CustomerEmail = "aisha.khan@example.test",
            Market = "Downtown Farmers Market",
            OrderedAt = now.AddDays(-20).AddHours(-6),
            PickupDate = now.AddDays(-18),
            TimeSlot = "09:00 AM - 10:00 AM",
            Status = OrderStatus.Completed,
            PaymentStatus = PaymentStatus.Paid,
            Notes = string.Empty,
            Items =
            [
                new OrderLine("Sunrise Dairy", "Greek Yogurt", 2),
                new OrderLine("Sunrise Dairy", "Goat Cheese", 1)
            ]
        };
    }

    private static DateTime NextDayOf(DateTime from, DayOfWeek day)
    {
        var days = ((int)day - (int)from.DayOfWeek + 7) % 7;
        if (days == 0)
        {
            days = 7;
        }

        return from.Date.AddDays(days);
    }

    private static ProductDefinition Product(
        string name,
        string description,
        decimal price,
        UnitType unit,
        string category,
        int stock,
        int reorderThreshold,
        int monday,
        int tuesday,
        int wednesday,
        int thursday,
        int friday,
        int saturday,
        int sunday,
        bool isOrganic,
        string imageUrl)
    {
        return new ProductDefinition
        {
            Name = name,
            Description = description,
            Price = price,
            Unit = unit,
            Category = category,
            Stock = stock,
            ReorderThreshold = reorderThreshold,
            Monday = monday,
            Tuesday = tuesday,
            Wednesday = wednesday,
            Thursday = thursday,
            Friday = friday,
            Saturday = saturday,
            Sunday = sunday,
            IsOrganic = isOrganic,
            ImageUrl = imageUrl
        };
    }

    private sealed record ReviewSnippet(int Rating, string Title, string Comment, int HelpfulCount);

    private sealed class FarmDefinition
    {
        public string Email { get; init; } = string.Empty;
        public string FirstName { get; init; } = string.Empty;
        public string LastName { get; init; } = string.Empty;
        public string Phone { get; init; } = string.Empty;
        public string FarmName { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Address { get; init; } = string.Empty;
        public string City { get; init; } = string.Empty;
        public string OperatingDays { get; init; } = string.Empty;
        public string PickupWindows { get; init; } = string.Empty;
        public int OrderCutoffHours { get; init; }
        public decimal Latitude { get; init; }
        public decimal Longitude { get; init; }
        public FarmerStatus Status { get; init; }
        public string[] Markets { get; init; } = [];
        public ProductDefinition[] Products { get; init; } = [];
    }

    private sealed class ProductDefinition
    {
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public decimal Price { get; init; }
        public UnitType Unit { get; init; }
        public string Category { get; init; } = string.Empty;
        public int Stock { get; init; }
        public int ReorderThreshold { get; init; }
        public int Monday { get; init; }
        public int Tuesday { get; init; }
        public int Wednesday { get; init; }
        public int Thursday { get; init; }
        public int Friday { get; init; }
        public int Saturday { get; init; }
        public int Sunday { get; init; }
        public bool IsOrganic { get; init; }
        public string ImageUrl { get; init; } = string.Empty;
    }

    private sealed class CustomerDefinition
    {
        public string Email { get; init; } = string.Empty;
        public string FirstName { get; init; } = string.Empty;
        public string LastName { get; init; } = string.Empty;
        public string Phone { get; init; } = string.Empty;
        public string Address { get; init; } = string.Empty;
        public string City { get; init; } = string.Empty;
        public string PostalCode { get; init; } = string.Empty;
    }

    private sealed record OrderLine(string FarmName, string ProductName, int Quantity);

    private sealed class OrderPlan
    {
        public string CustomerEmail { get; init; } = string.Empty;
        public string Market { get; init; } = string.Empty;
        public DateTime OrderedAt { get; init; }
        public DateTime PickupDate { get; set; }
        public string TimeSlot { get; init; } = string.Empty;
        public OrderStatus Status { get; init; }
        public PaymentStatus PaymentStatus { get; init; }
        public string Notes { get; init; } = string.Empty;
        public OrderLine[] Items { get; init; } = [];

        public DateTime LatestUpdate => Status switch
        {
            OrderStatus.Pending => OrderedAt,
            OrderStatus.Accepted => OrderedAt.AddHours(3),
            OrderStatus.Preparing => OrderedAt.AddHours(8),
            OrderStatus.ReadyForPickup => OrderedAt.AddHours(26),
            OrderStatus.Completed => PickupDate.AddHours(1),
            OrderStatus.Declined => OrderedAt.AddHours(6),
            OrderStatus.Cancelled => OrderedAt.AddHours(6),
            _ => OrderedAt
        };
    }
}
