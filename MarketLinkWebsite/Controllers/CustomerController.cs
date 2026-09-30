using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using MarketLinkWebsite.Services;
using MarketLinkWebsite.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Controllers
{
    [Authorize(Policy = "CustomerAccess")]
    public sealed class CustomerController : Controller
    {
        private readonly ApplicationDbContext db;
        private readonly UserManager<ApplicationUser> userManager;
        private readonly SignInManager<ApplicationUser> signInManager;
        private readonly MapService maps;

        public CustomerController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            MapService maps)
        {
            this.db = db;
            this.userManager = userManager;
            this.signInManager = signInManager;
            this.maps = maps;
        }

        [HttpGet]
        public async Task<IActionResult> Index(CancellationToken cancellationToken)
        {
            return await Dashboard(cancellationToken);
        }

        [HttpGet]
        public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
        {
            var customer = await GetActiveCustomerAsync(cancellationToken);
            if (customer is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            var orders = await db.Orders
                .AsNoTracking()
                .Where(order => order.UserId == customer.Id)
                .Include(order => order.Market)
                .Include(order => order.OrderItems)
                .ThenInclude(item => item.Product)
                .OrderByDescending(order => order.OrderDate)
                .ToListAsync(cancellationToken);

            var savedItemCount = await db.FavouriteProducts.CountAsync(item => item.UserId == customer.Id, cancellationToken);
            var favoriteFarmerCount = await db.FavouriteFarmers.CountAsync(item => item.UserId == customer.Id, cancellationToken);
            var seasonalProducts = await db.Products
                .AsNoTracking()
                .Where(product => product.IsAvailable)
                .OrderBy(product => product.Category.Name == "Fresh fruits" ? 0 : 1)
                .ThenByDescending(product => product.CreatedAt)
                .Select(product => new CustomerSeasonalPick
                {
                    ProductId = product.Id,
                    Name = product.Name,
                    FarmName = product.FarmerProfile.FarmName,
                    ImageUrl = product.ImageUrl,
                    Price = product.Price.ToString("C"),
                    Unit = $"per {UnitName(product.Unit)}",
                    Badge = product.Inventory != null && product.Inventory.QuantityAvailable <= 10 ? "Limited harvest" : "In season"
                })
                .Take(4)
                .ToListAsync(cancellationToken);

            var notifications = await db.Notifications
                .AsNoTracking()
                .Where(notification => notification.UserId == customer.Id)
                .OrderByDescending(notification => notification.CreatedAt)
                .Take(5)
                .ToListAsync(cancellationToken);

            var nextOrder = orders
                .Where(order => IsActiveOrder(order.Status))
                .OrderBy(order => order.PickupDate)
                .ThenBy(order => order.Id)
                .FirstOrDefault();

            var model = new CustomerDashboardViewModel
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Greeting = GetGreeting(),
                MemberSince = customer.CreatedAt.ToString("MMMM yyyy"),
                NextPickupDate = nextOrder?.PickupDate.ToString("dddd, MMMM d") ?? string.Empty,
                NextPickupWindow = nextOrder?.PickupTimeSlot ?? string.Empty,
                NextPickupLocation = nextOrder?.Market.Name ?? string.Empty,
                NextPickupStall = nextOrder?.Market.Address ?? string.Empty,
                ActiveOrderCount = orders.Count(order => IsActiveOrder(order.Status)),
                SavedItemCount = savedItemCount,
                FavoriteFarmerCount = favoriteFarmerCount,
                FeaturedOrder = nextOrder is null ? new CustomerOrderSummary() : MapOrder(nextOrder),
                RecentOrders = orders.Take(3).Select(MapOrder).ToList(),
                SeasonalPicks = seasonalProducts,
                Activity = notifications.Select(notification => new CustomerActivity
                {
                    Icon = NotificationIcon(notification.Type),
                    Title = notification.Title,
                    Detail = notification.Message,
                    Time = CreatedLabel(notification.CreatedAt),
                    Tone = NotificationTone(notification.Type)
                }).ToList()
            };

            SetCustomerView("Customer dashboard", "Dashboard");
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Orders(string? filter = null, string? status = null, CancellationToken cancellationToken = default)
        {
            var customer = await GetActiveCustomerAsync(cancellationToken);
            if (customer is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            var requestedFilter = string.IsNullOrWhiteSpace(filter) ? status : filter;
            var selectedFilter = requestedFilter?.ToLowerInvariant() switch
            {
                "active" => "Active",
                "completed" => "Completed",
                _ => "All"
            };

            var orders = await db.Orders
                .AsNoTracking()
                .Where(order => order.UserId == customer.Id)
                .Include(order => order.OrderItems)
                .ThenInclude(item => item.Product)
                .OrderByDescending(order => order.OrderDate)
                .ToListAsync(cancellationToken);

            var allOrders = orders.Select(MapOrder).ToList();
            var model = new CustomerOrdersViewModel
            {
                Orders = selectedFilter switch
                {
                    "Active" => allOrders.Where(order => order.IsActive).ToList(),
                    "Completed" => allOrders.Where(order => order.Status == nameof(OrderStatus.Completed)).ToList(),
                    _ => allOrders
                },
                TotalOrders = allOrders.Count,
                ActiveOrderCount = allOrders.Count(order => order.IsActive),
                PickupSiteCount = orders.Select(order => order.MarketId).Distinct().Count(),
                SelectedFilter = selectedFilter
            };
            model.FilteredCount = model.Orders.Count;
            SetCustomerView("Your orders", "Orders");
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> OrderDetails(int? id = null, CancellationToken cancellationToken = default)
        {
            var customer = await GetActiveCustomerAsync(cancellationToken);
            if (customer is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            if (id is null or <= 0)
            {
                return RedirectToAction(nameof(Orders));
            }

            var order = await db.Orders
                .AsNoTracking()
                // OrderItems and StatusHistory are both collections on the same
                // row, which a single query would cross-multiply.
                .AsSplitQuery()
                .Where(item => item.Id == id && item.UserId == customer.Id)
                .Include(item => item.Market)
                .Include(item => item.OrderItems)
                .ThenInclude(item => item.Product)
                .Include(item => item.StatusHistory)
                .FirstOrDefaultAsync(cancellationToken);

            if (order is null)
            {
                return NotFound();
            }

            var productIds = order.OrderItems
                .Where(item => item.ProductId.HasValue)
                .Select(item => item.ProductId!.Value)
                .ToList();
            var reviewedProductIds = await db.Reviews
                .Where(review => review.UserId == customer.Id && review.ProductId.HasValue && productIds.Contains(review.ProductId.Value))
                .Select(review => review.ProductId!.Value)
                .ToListAsync(cancellationToken);

            var subtotal = order.OrderItems.Sum(item => item.SubTotal);
            var model = new CustomerOrderDetailsViewModel
            {
                Order = MapOrder(order),
                Items = order.OrderItems.Select(item => new CustomerOrderLine
                {
                    ProductId = item.ProductId,
                    Name = item.ProductName,
                    FarmerName = item.FarmerName,
                    ImageUrl = item.Product?.ImageUrl ?? string.Empty,
                    Quantity = item.Quantity,
                    Unit = UnitName(item.Unit),
                    UnitPrice = item.UnitPrice,
                    LineTotal = item.SubTotal,
                    CanReview = order.Status == OrderStatus.Completed && item.ProductId.HasValue,
                    IsReviewed = item.ProductId.HasValue && reviewedProductIds.Contains(item.ProductId.Value)
                }).ToList(),
                Subtotal = subtotal,
                MarketFee = order.TotalAmount - subtotal,
                Total = order.TotalAmount,
                PickupAddress = new CustomerAddress
                {
                    Label = "Order pickup",
                    Recipient = $"{customer.FirstName} {customer.LastName}",
                    AddressLine = order.PickupAddress,
                    City = order.City,
                    Phone = order.Phone
                },
                PickupLocationName = order.Market.Name,
                MarketAddress = order.Market.Address,
                Notes = order.Notes,
                CancellationReason = order.CancellationReason ?? string.Empty,
                MarketDay = order.Market.OperatingDays,
                MarketMapUrl = $"https://www.openstreetmap.org/?mlat={order.Market.Latitude}&mlon={order.Market.Longitude}#map=16/{order.Market.Latitude}/{order.Market.Longitude}",
                MarketMapEmbedUrl = maps.EmbedUrlFor(order.Market.Latitude, order.Market.Longitude, 16),
                MarketDirectionsUrl = maps.DirectionsUrl(order.Market.Latitude, order.Market.Longitude),
            MarketLatitude = (double)order.Market.Latitude,
            MarketLongitude = (double)order.Market.Longitude,
                FarmNames = string.Join(", ", order.OrderItems
                    .Select(item => item.FarmerName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct()
                    .OrderBy(name => name)),
                Farms = await BuildOrderFarmsAsync(order, cancellationToken),
                Timeline = BuildTimeline(order)
            };

            SetCustomerView("Order details", "Orders");
            return View(model);
        }

        private async Task<List<CustomerOrderFarm>> BuildOrderFarmsAsync(Order order, CancellationToken cancellationToken)
        {
            var productIds = order.OrderItems
                .Where(item => item.ProductId.HasValue)
                .Select(item => item.ProductId!.Value)
                .Distinct()
                .ToList();

            if (productIds.Count == 0)
            {
                return [];
            }

            var rows = await db.Products
                .AsNoTracking()
                .Where(product => productIds.Contains(product.Id))
                .Select(product => new
                {
                    product.FarmerProfileId,
                    product.FarmerProfile.FarmName,
                    product.FarmerProfile.City,
                    product.FarmerProfile.Address,
                    product.FarmerProfile.OperatingDays,
                    product.FarmerProfile.PickupWindows,
                    product.FarmerProfile.Latitude,
                    product.FarmerProfile.Longitude,
                    profilePicture = product.FarmerProfile.User.ProfilePictureUrl,
                    fallbackImage = product.FarmerProfile.Products
                        .Where(item => item.IsAvailable && item.ImageUrl != string.Empty)
                        .OrderByDescending(item => item.UpdatedAt)
                        .Select(item => item.ImageUrl)
                        .FirstOrDefault(),
                    Stalls = product.FarmerProfile.MarketFarmers
                        .Where(link => link.Market.IsActive)
                        .Select(link => new CustomerOrderStall
                        {
                            MarketId = link.MarketId,
                            MarketName = link.Market.Name,
                            MarketAddress = link.Market.Address,
                            MarketDay = link.Market.OperatingDays
                                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .FirstOrDefault() ?? link.Market.OperatingDays,
                            OpenTime = link.Market.OpenTime,
                            CloseTime = link.Market.CloseTime,
                            StallNumber = link.StallNumber,
                            Latitude = (double)link.Market.Latitude,
                            Longitude = (double)link.Market.Longitude,
                            DirectionsUrl = maps.DirectionsUrl(link.Market.Latitude, link.Market.Longitude)
                        })
                        .ToList()
                })
                .ToListAsync(cancellationToken);

            return rows
                .GroupBy(row => row.FarmerProfileId)
                .Select(group =>
                {
                    var first = group.First();
                    var picture = string.IsNullOrWhiteSpace(first.profilePicture) ? first.fallbackImage : first.profilePicture;
                    return new CustomerOrderFarm
                    {
                        FarmerProfileId = first.FarmerProfileId,
                        FarmName = first.FarmName,
                        ImageUrl = picture ?? string.Empty,
                        Initials = string.IsNullOrWhiteSpace(first.FarmName)
                            ? "F"
                            : first.FarmName.Trim()[..1].ToUpperInvariant(),
                        City = first.City,
                        Address = first.Address,
                        OperatingDays = first.OperatingDays,
                        PickupWindows = first.PickupWindows,
                        MapUrl = maps.ViewUrl(first.Latitude, first.Longitude),
                        FarmUrl = $"/Farm/Index?id={first.FarmerProfileId}",
                        Latitude = (double)first.Latitude,
                        Longitude = (double)first.Longitude,
                        ItemCount = group.Count(),
                        Stalls = first.Stalls
                            .OrderBy(stall => stall.MarketName, StringComparer.OrdinalIgnoreCase)
                            .ToList()
                    };
                })
                .OrderBy(farm => farm.FarmName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        [HttpGet]
        public async Task<IActionResult> Favorites(CancellationToken cancellationToken)
        {
            var customer = await GetActiveCustomerAsync(cancellationToken);
            if (customer is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            var favoriteProducts = await db.FavouriteProducts
                .AsNoTracking()
                .Where(item => item.UserId == customer.Id)
                .Include(item => item.Product)
                .ThenInclude(product => product.FarmerProfile)
                .Include(item => item.Product)
                .ThenInclude(product => product.Category)
                .Include(item => item.Product)
                .ThenInclude(product => product.Inventory)
                .Include(item => item.Product)
                .ThenInclude(product => product.Reviews)
                .OrderByDescending(item => item.AddedAt)
                .ToListAsync(cancellationToken);

            var favoriteFarmerRows = await db.FavouriteFarmers
                .AsNoTracking()
                .Where(item => item.UserId == customer.Id)
                .OrderByDescending(item => item.AddedAt)
                .Select(item => new
                {
                    Id = item.FarmerProfile.Id,
                    Name = item.FarmerProfile.FarmName,
                    Location = item.FarmerProfile.City,
                    ImageUrl = item.FarmerProfile.User.ProfilePictureUrl,
                    Specialty = item.FarmerProfile.Description,
                    FollowerCount = item.FarmerProfile.FavoritedByUsers.Count(),
                    IsFeatured = item.FarmerProfile.Status == FarmerStatus.Active
                        && item.FarmerProfile.Products.Any(product => product.IsAvailable)
                })
                .ToListAsync(cancellationToken);

            var favoriteFarmers = favoriteFarmerRows
                .Select(item => new CustomerFavoriteFarmer
                {
                    Id = item.Id,
                    Name = item.Name,
                    Location = item.Location,
                    ImageUrl = item.ImageUrl,
                    Specialty = item.Specialty,
                    FollowerCount = $"{item.FollowerCount:N0} shoppers follow",
                    IsFeatured = item.IsFeatured
                })
                .ToList();

            var favoriteMarketRows = await db.FavouriteMarkets
                .AsNoTracking()
                .Where(item => item.UserId == customer.Id)
                .OrderByDescending(item => item.AddedAt)
                .Select(item => new
                {
                    Id = item.Market.Id,
                    Name = item.Market.Name,
                    City = item.Market.City,
                    Address = item.Market.Address,
                    OperatingDays = item.Market.OperatingDays,
                    OpenTime = item.Market.OpenTime,
                    CloseTime = item.Market.CloseTime,
                    ImageUrl = item.Market.ImageUrl,
                    FarmerCount = item.Market.MarketFarmers.Count,
                    ProductCount = item.Market.MarketFarmers.SelectMany(link => link.FarmerProfile.Products).Select(product => product.Id).Distinct().Count()
                })
                .ToListAsync(cancellationToken);

            var favoriteMarkets = favoriteMarketRows
                .Select(item => new CustomerFavoriteMarket
                {
                    Id = item.Id,
                    Name = item.Name,
                    City = item.City,
                    Address = item.Address,
                    OperatingDays = item.OperatingDays,
                    OpenTime = item.OpenTime,
                    CloseTime = item.CloseTime,
                    ImageUrl = item.ImageUrl,
                    FarmerCount = item.FarmerCount,
                    ProductCount = item.ProductCount
                })
                .ToList();

            var model = new CustomerFavoritesViewModel
            {
                Products = favoriteProducts.Select(item =>
                {
                    var product = item.Product;
                    var reviewCount = product.Reviews.Count;
                    var rating = reviewCount == 0 ? product.FarmerProfile.Rating : product.Reviews.Average(review => (decimal)review.Rating);
                    var availability = product.Inventory is null
                        ? product.IsAvailable ? "Available this week" : "Currently unavailable"
                        : product.Inventory.QuantityAvailable > 10
                            ? "In this week’s harvest"
                            : $"Only {product.Inventory.QuantityAvailable} left";
                    return new CustomerFavoriteProduct
                    {
                        Id = product.Id,
                        Name = product.Name,
                        FarmName = product.FarmerProfile.FarmName,
                        ImageUrl = product.ImageUrl,
                        Price = product.Price.ToString("C"),
                        Unit = $"per {UnitName(product.Unit)}",
                        Rating = rating,
                        ReviewCount = reviewCount,
                        Availability = availability,
                        Category = product.Category.Name
                    };
                }).ToList(),
                Farmers = favoriteFarmers,
                Markets = favoriteMarkets
            };

            SetCustomerView("Saved favorites", "Favorites");
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Reviews(CancellationToken cancellationToken)
        {
            var customer = await GetActiveCustomerAsync(cancellationToken);
            if (customer is null)
            {
                return Challenge();
            }

            var reviews = await db.Reviews
                .AsNoTracking()
                .Where(review => review.UserId == customer.Id)
                .Include(review => review.Product)
                .Include(review => review.FarmerProfile)
                .OrderByDescending(review => review.CreatedAt)
                .ToListAsync(cancellationToken);

            SetCustomerView("My reviews", "Reviews");

            return View(new CustomerReviewListViewModel
            {
                Reviews = reviews.Select(review => new CustomerReviewItem
                {
                    Id = review.Id,
                    Rating = review.Rating,
                    Title = review.Title,
                    Comment = review.Comment,
                    VerifiedPurchase = review.VerifiedPurchase,
                    HelpfulCount = review.HelpfulCount,
                    CreatedAt = review.CreatedAt,
                    FarmerReply = review.FarmerReply,
                    FarmerRepliedAt = review.FarmerRepliedAt,
                    Target = review.FarmerProfile?.FarmName ?? review.Product?.Name ?? "MarketLink",
                    TargetKind = review.FarmerProfile is not null ? "Farm" : "Product",
                    TargetId = review.FarmerProfile?.Id ?? review.Product?.Id ?? 0,
                    ImageUrl = review.Product?.ImageUrl ?? string.Empty
                }).ToList()
            });
        }

        [HttpGet]
        public async Task<IActionResult> Profile(CancellationToken cancellationToken)
        {
            var customer = await GetActiveCustomerAsync(cancellationToken);
            if (customer is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            SetCustomerView("Your profile", "Profile");
            return View(await BuildProfileViewModelAsync(customer, cancellationToken));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(CustomerProfileViewModel model, CancellationToken cancellationToken)
        {
            var customer = await GetActiveCustomerAsync(cancellationToken);
            if (customer is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            model ??= new CustomerProfileViewModel();
            model.Profile ??= new ProfileForm();

            if (!ModelState.IsValid)
            {
                SetCustomerView("Your profile", "Profile");
                ViewData["ErrorMessage"] = "Check the highlighted profile fields and try again.";
                var page = await BuildProfileViewModelAsync(customer, cancellationToken);
                page.Profile = model.Profile;
                return View(page);
            }

            var user = await userManager.FindByIdAsync(customer.Id);
            if (user is null)
            {
                await signInManager.SignOutAsync();
                return RedirectToAction(nameof(AccountController.Login));
            }

            var email = model.Profile.Email.Trim();
            var existing = await userManager.FindByEmailAsync(email);
            if (existing is not null && existing.Id != user.Id)
            {
                ModelState.AddModelError("Profile.Email", "Another account already uses this email address.");
            }
            else
            {
                user.FirstName = model.Profile.FirstName.Trim();
                user.LastName = model.Profile.LastName.Trim();
                user.Email = email;
                user.NormalizedEmail = userManager.NormalizeEmail(email);
                user.UserName = email;
                user.NormalizedUserName = userManager.NormalizeName(email);
                user.PhoneNumber = model.Profile.PhoneNumber.Trim();
                var result = await userManager.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    foreach (var error in result.Errors)
                    {
                        ModelState.AddModelError(string.Empty, error.Description);
                    }
                }
            }

            if (!ModelState.IsValid)
            {
                SetCustomerView("Your profile", "Profile");
                ViewData["ErrorMessage"] = "Check the highlighted profile fields and try again.";
                var page = await BuildProfileViewModelAsync(customer, cancellationToken);
                page.Profile = model.Profile;
                return View(page);
            }

            await signInManager.RefreshSignInAsync(user);
            TempData["SuccessMessage"] = "Your profile has been updated.";
            return RedirectToAction(nameof(Profile));
        }

        [HttpGet]
        public async Task<IActionResult> Addresses(CancellationToken cancellationToken)
        {
            var customer = await GetActiveCustomerAsync(cancellationToken);
            if (customer is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            SetCustomerView("Saved addresses", "Addresses");
            return View(await BuildAddressBookViewModelAsync(customer, cancellationToken));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddAddress(AddressBookViewModel model, CancellationToken cancellationToken)
        {
            var customer = await GetActiveCustomerAsync(cancellationToken);
            if (customer is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            model ??= new AddressBookViewModel();
            model.NewAddress ??= new AddressForm();

            if (!ModelState.IsValid)
            {
                SetCustomerView("Saved addresses", "Addresses");
                ViewData["ErrorMessage"] = "Check the highlighted address fields and try again.";
                var page = await BuildAddressBookViewModelAsync(customer, cancellationToken);
                page.NewAddress = model.NewAddress;
                return View(nameof(Addresses), page);
            }

            var existingAddresses = await db.Addresses
                .Where(address => address.UserId == customer.Id)
                .ToListAsync(cancellationToken);
            var makeDefault = model.NewAddress.IsDefault || existingAddresses.Count == 0;
            if (makeDefault)
            {
                foreach (var existing in existingAddresses.Where(address => address.IsDefault))
                {
                    existing.IsDefault = false;
                }
            }

            db.Addresses.Add(new Address
            {
                UserId = customer.Id,
                Label = model.NewAddress.Label.Trim(),
                RecipientName = model.NewAddress.Recipient.Trim(),
                Phone = model.NewAddress.Phone.Trim(),
                AddressLine = model.NewAddress.AddressLine.Trim(),
                City = model.NewAddress.City.Trim(),
                Region = model.NewAddress.Region.Trim(),
                PostalCode = model.NewAddress.PostalCode.Trim(),
                Latitude = model.NewAddress.Latitude,
                Longitude = model.NewAddress.Longitude,
                IsDefault = makeDefault,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken);
            TempData["SuccessMessage"] = "Address saved to your address book.";
            return RedirectToAction(nameof(Addresses));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAddress(int id, CancellationToken cancellationToken)
        {
            var customer = await GetActiveCustomerAsync(cancellationToken);
            if (customer is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            if (id <= 0)
            {
                TempData["ErrorMessage"] = "Choose a valid saved address.";
                return RedirectToAction(nameof(Addresses));
            }

            var addresses = await db.Addresses
                .Where(address => address.UserId == customer.Id)
                .OrderBy(address => address.CreatedAt)
                .ToListAsync(cancellationToken);
            var address = addresses.FirstOrDefault(item => item.Id == id);
            if (address is null)
            {
                TempData["ErrorMessage"] = "That address could not be found in your address book.";
                return RedirectToAction(nameof(Addresses));
            }

            var shouldReplaceDefault = address.IsDefault;
            db.Addresses.Remove(address);
            if (shouldReplaceDefault)
            {
                var replacement = addresses.FirstOrDefault(item => item.Id != id);
                if (replacement is not null)
                {
                    replacement.IsDefault = true;
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            TempData["SuccessMessage"] = "The address has been removed from your address book.";
            return RedirectToAction(nameof(Addresses));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MakeDefaultAddress(int id, CancellationToken cancellationToken)
        {
            var customer = await GetActiveCustomerAsync(cancellationToken);
            if (customer is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            var addresses = await db.Addresses
                .Where(address => address.UserId == customer.Id)
                .ToListAsync(cancellationToken);
            if (id <= 0 || addresses.All(address => address.Id != id))
            {
                TempData["ErrorMessage"] = "Choose a valid saved address.";
                return RedirectToAction(nameof(Addresses));
            }

            foreach (var address in addresses)
            {
                address.IsDefault = address.Id == id;
            }

            await db.SaveChangesAsync(cancellationToken);
            TempData["SuccessMessage"] = "Your default pickup address has been updated.";
            return RedirectToAction(nameof(Addresses));
        }

        [HttpGet]
        public async Task<IActionResult> Notifications(string? filter = null, CancellationToken cancellationToken = default)
        {
            var customer = await GetActiveCustomerAsync(cancellationToken);
            if (customer is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            var selectedFilter = filter?.ToLowerInvariant() switch
            {
                "unread" => "Unread",
                "orders" => "Orders",
                _ => "All"
            };

            var notifications = await db.Notifications
                .AsNoTracking()
                .Where(notification => notification.UserId == customer.Id)
                .OrderByDescending(notification => notification.CreatedAt)
                .ToListAsync(cancellationToken);
            var model = new CustomerNotificationsViewModel
            {
                UnreadCount = notifications.Count(notification => !notification.IsRead),
                TotalCount = notifications.Count,
                SelectedFilter = selectedFilter,
                Notifications = notifications.Select(MapNotification).ToList()
            };

            model.Notifications = selectedFilter switch
            {
                "Unread" => model.Notifications.Where(notification => !notification.IsRead).ToList(),
                "Orders" => model.Notifications.Where(notification => notification.Category == "Orders").ToList(),
                _ => model.Notifications
            };
            model.FilteredCount = model.Notifications.Count;
            ViewData["UnreadNotificationCount"] = model.UnreadCount;
            SetCustomerView("Notifications", "Notifications");
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkNotificationRead(int id, CancellationToken cancellationToken)
        {
            var customer = await GetActiveCustomerAsync(cancellationToken);
            if (customer is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            if (id <= 0)
            {
                TempData["ErrorMessage"] = "Choose a valid notification.";
                return RedirectToAction(nameof(Notifications));
            }

            var notification = await db.Notifications
                .FirstOrDefaultAsync(item => item.Id == id && item.UserId == customer.Id, cancellationToken);
            if (notification is null)
            {
                TempData["ErrorMessage"] = "That notification could not be found.";
                return RedirectToAction(nameof(Notifications));
            }

            if (!notification.IsRead)
            {
                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            TempData["SuccessMessage"] = "Notification marked as read.";
            return RedirectToAction(nameof(Notifications));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllNotificationsRead(CancellationToken cancellationToken)
        {
            var customer = await GetActiveCustomerAsync(cancellationToken);
            if (customer is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            var unread = await db.Notifications
                .Where(notification => notification.UserId == customer.Id && !notification.IsRead)
                .ToListAsync(cancellationToken);
            var readAt = DateTime.UtcNow;
            foreach (var notification in unread)
            {
                notification.IsRead = true;
                notification.ReadAt = readAt;
            }

            await db.SaveChangesAsync(cancellationToken);
            TempData["SuccessMessage"] = unread.Count == 0 ? "All notifications are already read." : "All notifications marked as read.";
            return RedirectToAction(nameof(Notifications));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveFavorite(int id, string kind = "product", CancellationToken cancellationToken = default)
        {
            var customer = await GetActiveCustomerAsync(cancellationToken);
            if (customer is null)
            {
                return RedirectToAction(nameof(AccountController.Login));
            }

            if (id <= 0)
            {
                TempData["ErrorMessage"] = "Choose a valid favorite.";
                return RedirectToAction(nameof(Favorites));
            }

            var isFarmer = kind.Equals("farmer", StringComparison.OrdinalIgnoreCase);
            var isMarket = kind.Equals("market", StringComparison.OrdinalIgnoreCase);
            if (isFarmer)
            {
                var favorite = await db.FavouriteFarmers
                    .FirstOrDefaultAsync(item => item.UserId == customer.Id && item.FarmerProfileId == id, cancellationToken);
                if (favorite is null)
                {
                    TempData["ErrorMessage"] = "That saved farm could not be found.";
                    return RedirectToAction(nameof(Favorites));
                }

                db.FavouriteFarmers.Remove(favorite);
            }
            else if (isMarket)
            {
                var favorite = await db.FavouriteMarkets
                    .FirstOrDefaultAsync(item => item.UserId == customer.Id && item.MarketId == id, cancellationToken);
                if (favorite is null)
                {
                    TempData["ErrorMessage"] = "That saved market could not be found.";
                    return RedirectToAction(nameof(Favorites));
                }

                db.FavouriteMarkets.Remove(favorite);
            }
            else
            {
                var favorite = await db.FavouriteProducts
                    .FirstOrDefaultAsync(item => item.UserId == customer.Id && item.ProductId == id, cancellationToken);
                if (favorite is null)
                {
                    TempData["ErrorMessage"] = "That saved item could not be found.";
                    return RedirectToAction(nameof(Favorites));
                }

                db.FavouriteProducts.Remove(favorite);
            }

            await db.SaveChangesAsync(cancellationToken);
            TempData["SuccessMessage"] = isFarmer ? "Farm removed from your saved list." : isMarket ? "Market removed from your saved list." : "Item removed from your saved list.";
            return RedirectToAction(nameof(Favorites));
        }

        private async Task<ApplicationUser?> GetActiveCustomerAsync(CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
            {
                await signInManager.SignOutAsync();
                return null;
            }

            var customer = await db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);
            if (customer is null || !customer.IsActive)
            {
                await signInManager.SignOutAsync();
                TempData["ErrorMessage"] = "Your account is inactive. Contact MarketLink support for help.";
                return null;
            }

            ViewData["CustomerDisplayName"] = $"{customer.FirstName} {customer.LastName}".Trim();
            ViewData["CustomerInitials"] = GetInitials(customer.FirstName, customer.LastName);
            return customer;
        }

        private void SetCustomerView(string title, string activeNav)
        {
            ViewData["Title"] = title;
            ViewData["ActiveNav"] = activeNav;
        }

        private async Task<CustomerProfileViewModel> BuildProfileViewModelAsync(ApplicationUser customer, CancellationToken cancellationToken)
        {
            var totalOrders = await db.Orders.CountAsync(order => order.UserId == customer.Id, cancellationToken);
            var savedItems = await db.FavouriteProducts.CountAsync(item => item.UserId == customer.Id, cancellationToken);
            var favoriteFarmers = await db.FavouriteFarmers.CountAsync(item => item.UserId == customer.Id, cancellationToken);
            var addresses = await LoadAddressesAsync(customer.Id, cancellationToken);
            return new CustomerProfileViewModel
            {
                AvatarInitials = GetInitials(customer.FirstName, customer.LastName),
                MemberSince = $"Member since {customer.CreatedAt.ToString("MMMM yyyy")}",
                Profile = new ProfileForm
                {
                    FirstName = customer.FirstName,
                    LastName = customer.LastName,
                    Email = customer.Email ?? string.Empty,
                    PhoneNumber = customer.PhoneNumber ?? string.Empty
                },
                Addresses = addresses.Select(MapAddress).ToList(),
                TotalOrders = totalOrders,
                SavedItems = savedItems,
                FavoriteFarmers = favoriteFarmers
            };
        }

        private async Task<AddressBookViewModel> BuildAddressBookViewModelAsync(ApplicationUser customer, CancellationToken cancellationToken)
        {
            var addresses = await LoadAddressesAsync(customer.Id, cancellationToken);
            return new AddressBookViewModel
            {
                Addresses = addresses.Select(MapAddress).ToList(),
                NewAddress = new AddressForm
                {
                    Recipient = $"{customer.FirstName} {customer.LastName}".Trim(),
                    Phone = customer.PhoneNumber ?? string.Empty
                }
            };
        }

        private async Task<List<Address>> LoadAddressesAsync(string userId, CancellationToken cancellationToken)
        {
            return await db.Addresses
                .AsNoTracking()
                .Where(address => address.UserId == userId)
                .OrderByDescending(address => address.IsDefault)
                .ThenBy(address => address.Label)
                .ToListAsync(cancellationToken);
        }

        private static CustomerAddress MapAddress(Address address)
        {
            return new CustomerAddress
            {
                Id = address.Id,
                Label = address.Label,
                Recipient = address.RecipientName,
                AddressLine = address.AddressLine,
                City = address.City,
                Region = address.Region,
                PostalCode = address.PostalCode,
                Phone = address.Phone,
                IsDefault = address.IsDefault
            };
        }

        private static CustomerOrderSummary MapOrder(Order order)
        {
            return new CustomerOrderSummary
            {
                Id = order.Id,
                OrderNumber = order.OrderNumber,
                OrderDate = order.OrderDate,
                Status = FormatStatus(order.Status),
                StatusClass = StatusClass(order.Status),
                StatusIcon = StatusIcon(order.Status),
                IsActive = IsActiveOrder(order.Status),
                IsCompleted = order.Status == OrderStatus.Completed,
                CanEdit = order.Status is OrderStatus.Pending or OrderStatus.Accepted,
                CanCancel = order.Status == OrderStatus.Pending,
                PickupDate = order.PickupDate.ToString("ddd, MMM d"),
                PickupWindow = order.PickupTimeSlot,
                MarketId = order.MarketId,
                ItemSummary = ItemSummary(order.OrderItems),
                ItemCount = order.OrderItems.Sum(item => item.Quantity),
                Total = order.TotalAmount,
                PaymentLabel = PaymentLabel(order),
                ImageUrl = order.OrderItems.Select(item => item.Product?.ImageUrl).FirstOrDefault(image => !string.IsNullOrWhiteSpace(image)) ?? string.Empty
            };
        }

        private static List<CustomerTimelineStep> BuildTimeline(Order order)
        {
            var timeline = order.StatusHistory
                .OrderBy(history => history.ChangedAt)
                .Select(history => new CustomerTimelineStep
                {
                    Title = StatusTitle(history.Status),
                    Detail = StatusDetail(history.Status),
                    Time = history.ChangedAt.ToString("MMM d · h:mm tt"),
                    State = history.Status == order.Status ? "current" : "complete",
                    Icon = StatusIcon(history.Status)
                })
                .ToList();

            if (timeline.Count == 0)
            {
                timeline.Add(new CustomerTimelineStep
                {
                    Title = StatusTitle(order.Status),
                    Detail = StatusDetail(order.Status),
                    Time = order.UpdatedAt.ToString("MMM d · h:mm tt"),
                    State = "current",
                    Icon = StatusIcon(order.Status)
                });
            }

            return timeline;
        }

        private static CustomerNotification MapNotification(Notification notification)
        {
            var action = ResolveNotificationAction(notification);
            return new CustomerNotification
            {
                Id = notification.Id,
                Title = notification.Title,
                Message = notification.Message,
                CreatedLabel = CreatedLabel(notification.CreatedAt),
                Category = NotificationCategory(notification.Type),
                Icon = NotificationIcon(notification.Type),
                Tone = NotificationTone(notification.Type),
                IsRead = notification.IsRead,
                ActionController = action.Controller,
                ActionName = action.Action,
                ActionId = action.Id
            };
        }

        private static (string Controller, string Action, int Id) ResolveNotificationAction(Notification notification)
        {
            var path = notification.ActionUrl?.Split('?', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim('/');
            var segments = path?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            if (segments.Length < 2)
            {
                return notification.Type switch
                {
                    NotificationType.Order => ("Customer", "Orders", 0),
                    NotificationType.Review => ("Review", "Index", 0),
                    NotificationType.Account => ("Customer", "Profile", 0),
                    _ => ("Customer", "Notifications", 0)
                };
            }

            var controller = segments[0];
            var action = segments[1];
            var id = segments.Length > 2 && int.TryParse(segments[2], out var parsedId) ? parsedId : 0;
            var allowed = controller switch
            {
                "Customer" => action is "Orders" or "OrderDetails" or "Favorites" or "Profile" or "Notifications",
                "Review" => action == "Index",
                "Product" => action is "Index" or "Details",
                "Account" => action == "Login",
                _ => false
            };
            return allowed ? (controller, action, id) : ("Customer", "Notifications", 0);
        }

        private static string ItemSummary(IEnumerable<OrderItem> items)
        {
            var names = items.Select(item => item.ProductName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (names.Count == 0)
            {
                return "No item details";
            }

            return names.Count <= 2
                ? string.Join(", ", names)
                : $"{names[0]}, {names[1]} + {names.Count - 2} more";
        }

        private static bool IsActiveOrder(OrderStatus status)
        {
            return status is OrderStatus.Pending or OrderStatus.Accepted or OrderStatus.Preparing or OrderStatus.ReadyForPickup;
        }

        private static string FormatStatus(OrderStatus status)
        {
            return status switch
            {
                OrderStatus.ReadyForPickup => "Ready for pickup",
                _ => status.ToString()
            };
        }

        private static string StatusClass(OrderStatus status)
        {
            return status switch
            {
                OrderStatus.Pending => "warning",
                OrderStatus.Accepted => "primary",
                OrderStatus.Preparing => "info",
                OrderStatus.ReadyForPickup => "success",
                OrderStatus.Completed => "secondary",
                OrderStatus.Declined => "danger",
                OrderStatus.Cancelled => "dark",
                _ => "secondary"
            };
        }

        private static string StatusIcon(OrderStatus status)
        {
            return status switch
            {
                OrderStatus.Pending => "bi-hourglass-split",
                OrderStatus.Accepted => "bi-check2-circle",
                OrderStatus.Preparing => "bi-basket2",
                OrderStatus.ReadyForPickup => "bi-shop-window",
                OrderStatus.Completed => "bi-check2-all",
                OrderStatus.Declined => "bi-x-circle",
                OrderStatus.Cancelled => "bi-slash-circle",
                _ => "bi-check2-circle"
            };
        }

        private static string StatusTitle(OrderStatus status)
        {
            return status switch
            {
                OrderStatus.Pending => "Order placed",
                OrderStatus.Accepted => "Order accepted",
                OrderStatus.Preparing => "Farmers preparing your order",
                OrderStatus.ReadyForPickup => "Ready for pickup",
                OrderStatus.Completed => "Order completed",
                OrderStatus.Declined => "Order declined",
                OrderStatus.Cancelled => "Order cancelled",
                _ => "Order updated"
            };
        }

        private static string StatusDetail(OrderStatus status)
        {
            return status switch
            {
                OrderStatus.Pending => "Your pre-order was received and is awaiting confirmation.",
                OrderStatus.Accepted => "The participating farmers confirmed your order.",
                OrderStatus.Preparing => "Your market items are being gathered for pickup.",
                OrderStatus.ReadyForPickup => "Your order is packed and waiting at the market.",
                OrderStatus.Completed => "Your order has been picked up. Thank you for shopping local.",
                OrderStatus.Declined => "The participating farmers could not fulfill this order.",
                OrderStatus.Cancelled => "This order was cancelled.",
                _ => "Your order status was updated."
            };
        }

        private static string PaymentLabel(Order order)
        {
            return order.PaymentStatus switch
            {
                PaymentStatus.Paid => "Paid",
                PaymentStatus.Failed => "Payment failed",
                PaymentStatus.Refunded => "Refunded",
                _ => string.IsNullOrWhiteSpace(order.PaymentMethod) ? "Payment pending" : order.PaymentMethod
            };
        }

        private static string UnitName(UnitType unit)
        {
            return unit switch
            {
                UnitType.Kg => "kg",
                UnitType.Gram => "g",
                UnitType.Litre => "litre",
                UnitType.Piece => "piece",
                UnitType.Dozen => "dozen",
                UnitType.Bundle => "bundle",
                _ => "unit"
            };
        }

        private static string GetInitials(string firstName, string lastName)
        {
            var first = string.IsNullOrWhiteSpace(firstName) ? string.Empty : firstName.Trim()[0].ToString();
            var last = string.IsNullOrWhiteSpace(lastName) ? string.Empty : lastName.Trim()[0].ToString();
            return $"{first}{last}".ToUpperInvariant();
        }

        private static string GetGreeting()
        {
            return DateTime.Now.Hour switch
            {
                < 12 => "Good morning",
                < 17 => "Good afternoon",
                _ => "Good evening"
            };
        }

        private static string NotificationCategory(NotificationType type)
        {
            return type switch
            {
                NotificationType.Order => "Orders",
                NotificationType.Account => "Account",
                NotificationType.Review => "Reviews",
                NotificationType.Announcement => "Market",
                _ => "MarketLink"
            };
        }

        private static string NotificationIcon(NotificationType type)
        {
            return type switch
            {
                NotificationType.Order => "bi-bag-check",
                NotificationType.Account => "bi-person-check",
                NotificationType.Review => "bi-chat-heart",
                NotificationType.Announcement => "bi-basket2",
                _ => "bi-flower1"
            };
        }

        private static string NotificationTone(NotificationType type)
        {
            return type switch
            {
                NotificationType.Order => "success",
                NotificationType.Account => "primary",
                NotificationType.Review => "danger",
                NotificationType.Announcement => "warning",
                _ => "success"
            };
        }

        private static string CreatedLabel(DateTime createdAt)
        {
            var local = createdAt.ToLocalTime();
            if (local.Date == DateTime.Today)
            {
                return $"Today, {local:h:mm tt}";
            }

            if (local.Date == DateTime.Today.AddDays(-1))
            {
                return "Yesterday";
            }

            return local.ToString("MMM d, yyyy");
        }
    }

    public class CustomerDashboardViewModel
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Greeting { get; set; } = string.Empty;
        public string MemberSince { get; set; } = string.Empty;
        public string NextPickupDate { get; set; } = string.Empty;
        public string NextPickupWindow { get; set; } = string.Empty;
        public string NextPickupLocation { get; set; } = string.Empty;
        public string NextPickupStall { get; set; } = string.Empty;
        public int ActiveOrderCount { get; set; }
        public int SavedItemCount { get; set; }
        public int FavoriteFarmerCount { get; set; }
        public CustomerOrderSummary FeaturedOrder { get; set; } = new();
        public List<CustomerOrderSummary> RecentOrders { get; set; } = new();
        public List<CustomerSeasonalPick> SeasonalPicks { get; set; } = new();
        public List<CustomerActivity> Activity { get; set; } = new();
    }

    public class CustomerOrdersViewModel
    {
        public List<CustomerOrderSummary> Orders { get; set; } = new();
        public int TotalOrders { get; set; }
        public int ActiveOrderCount { get; set; }
        public int FilteredCount { get; set; }
        public int PickupSiteCount { get; set; }
        public string SelectedFilter { get; set; } = "All";
    }

    public class CustomerOrderSummary
    {
        public int Id { get; set; }
        public string OrderNumber { get; set; } = string.Empty;
        public DateTime OrderDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public string StatusClass { get; set; } = "secondary";
        public string StatusIcon { get; set; } = "bi-check2-circle";
        public bool IsActive { get; set; }
        public bool IsCompleted { get; set; }
        public bool CanEdit { get; set; }
        public bool CanCancel { get; set; }
        public string PickupDate { get; set; } = string.Empty;
        public string PickupWindow { get; set; } = string.Empty;
    public int MarketId { get; set; }
        public string ItemSummary { get; set; } = string.Empty;
        public int ItemCount { get; set; }
        public decimal Total { get; set; }
        public string PaymentLabel { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
    }

    public class CustomerOrderDetailsViewModel
    {
        public CustomerOrderSummary Order { get; set; } = new();
        public List<CustomerOrderLine> Items { get; set; } = new();
        public decimal Subtotal { get; set; }
        public decimal MarketFee { get; set; }
        public decimal Total { get; set; }
        public CustomerAddress PickupAddress { get; set; } = new();
        public string PickupLocationName { get; set; } = string.Empty;
        public string MarketAddress { get; set; } = string.Empty;
        public string MarketDay { get; set; } = string.Empty;
        public string MarketMapUrl { get; set; } = string.Empty;
        public string MarketMapEmbedUrl { get; set; } = string.Empty;
        public string MarketDirectionsUrl { get; set; } = string.Empty;
        public double MarketLatitude { get; set; }
        public double MarketLongitude { get; set; }
        public List<CustomerOrderFarm> Farms { get; set; } = new();
        public string Notes { get; set; } = string.Empty;
        public string CancellationReason { get; set; } = string.Empty;
        public string FarmNames { get; set; } = string.Empty;
        public List<CustomerTimelineStep> Timeline { get; set; } = new();
    }

    public class CustomerOrderFarm
    {
        public int FarmerProfileId { get; set; }
        public string FarmName { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string Initials { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string OperatingDays { get; set; } = string.Empty;
        public string PickupWindows { get; set; } = string.Empty;
        public string MapUrl { get; set; } = string.Empty;
        public int ItemCount { get; set; }
        public string FarmUrl { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public List<CustomerOrderStall> Stalls { get; set; } = new();
    }

    public class CustomerOrderStall
    {
        public int MarketId { get; set; }
        public string MarketName { get; set; } = string.Empty;
        public string MarketAddress { get; set; } = string.Empty;
        public string MarketDay { get; set; } = string.Empty;
        public string OpenTime { get; set; } = string.Empty;
        public string CloseTime { get; set; } = string.Empty;
        public string StallNumber { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string DirectionsUrl { get; set; } = string.Empty;
    }

    public class CustomerOrderLine
    {
        public int? ProductId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string FarmerName { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string Unit { get; set; } = string.Empty;
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
        public bool CanReview { get; set; }
        public bool IsReviewed { get; set; }
    }

    public class CustomerTimelineStep
    {
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
    }

    public class CustomerSeasonalPick
    {
        public int ProductId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string FarmName { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string Price { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string Badge { get; set; } = string.Empty;
    }

    public class CustomerActivity
    {
        public string Icon { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string Time { get; set; } = string.Empty;
        public string Tone { get; set; } = "success";
    }

    public class CustomerFavoritesViewModel
    {
        public List<CustomerFavoriteProduct> Products { get; set; } = new();
        public List<CustomerFavoriteFarmer> Farmers { get; set; } = new();
        public List<CustomerFavoriteMarket> Markets { get; set; } = new();
    }

    public class CustomerFavoriteProduct
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string FarmName { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string Price { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public decimal Rating { get; set; }
        public int ReviewCount { get; set; }
        public string Availability { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
    }

    public class CustomerFavoriteFarmer
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string Specialty { get; set; } = string.Empty;
        public string FollowerCount { get; set; } = string.Empty;
        public bool IsFeatured { get; set; }
    }

    public class CustomerFavoriteMarket
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string OperatingDays { get; set; } = string.Empty;
        public string OpenTime { get; set; } = string.Empty;
        public string CloseTime { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public int FarmerCount { get; set; }
        public int ProductCount { get; set; }
    }

    public class CustomerProfileViewModel
    {
        public string AvatarInitials { get; set; } = string.Empty;
        public string MemberSince { get; set; } = string.Empty;
        public ProfileForm Profile { get; set; } = new();
        public List<CustomerAddress> Addresses { get; set; } = new();
        public int TotalOrders { get; set; }
        public int SavedItems { get; set; }
        public int FavoriteFarmers { get; set; }
    }

    public class ProfileForm
    {
        [Required(ErrorMessage = "Enter your first name.")]
        [StringLength(50, MinimumLength = 2, ErrorMessage = "First name must be 2–50 characters.")]
        [PersonName]
        [Display(Name = "First name")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your last name.")]
        [StringLength(50, MinimumLength = 2, ErrorMessage = "Last name must be 2–50 characters.")]
        [PersonName]
        [Display(Name = "Last name")]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [StringLength(256, ErrorMessage = "Email address must be 256 characters or fewer.")]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your phone number.")]
        [Phone(ErrorMessage = "Enter a valid phone number.")]
        [StringLength(30, ErrorMessage = "Phone number must be 30 characters or fewer.")]
        [PhoneNumber]
        [Display(Name = "Phone number")]
        public string PhoneNumber { get; set; } = string.Empty;
    }

    public class AddressBookViewModel
    {
        public List<CustomerAddress> Addresses { get; set; } = new();
        public AddressForm NewAddress { get; set; } = new();
    }

    public class AddressForm
    {
        [Required(ErrorMessage = "Give this address a label.")]
        [StringLength(40, MinimumLength = 2, ErrorMessage = "Label must be 2–40 characters.")]
        [BusinessName]
        [Display(Name = "Address label")]
        public string Label { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter the recipient name.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Recipient must be 2–100 characters.")]
        [PersonName]
        [Display(Name = "Recipient name")]
        public string Recipient { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter a street address.")]
        [StringLength(250, MinimumLength = 5, ErrorMessage = "Enter a complete street address.")]
        [StreetAddress]
        [Display(Name = "Street address")]
        public string AddressLine { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter a city.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Enter a valid city.")]
        [BusinessName]
        [Display(Name = "City")]
        public string City { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter a state or region.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Enter a state or region.")]
        [BusinessName]
        [Display(Name = "State / region")]
        public string Region { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter a postal code.")]
        [StringLength(20, MinimumLength = 3, ErrorMessage = "Enter a valid postal code.")]
        [PostalCode]
        [Display(Name = "Postal code")]
        public string PostalCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter a phone number.")]
        [Phone(ErrorMessage = "Enter a valid phone number.")]
        [StringLength(30, ErrorMessage = "Phone number must be 30 characters or fewer.")]
        [PhoneNumber]
        [Display(Name = "Phone number")]
        public string Phone { get; set; } = string.Empty;

        [Coordinate]
        [Range(typeof(decimal), "-90", "90", ErrorMessage = "Latitude must be between -90 and 90.")]
        [Display(Name = "Latitude")]
        public decimal? Latitude { get; set; }

        [Coordinate]
        [Range(typeof(decimal), "-180", "180", ErrorMessage = "Longitude must be between -180 and 180.")]
        [Display(Name = "Longitude")]
        public decimal? Longitude { get; set; }

        [Display(Name = "Make this my default pickup address")]
        public bool IsDefault { get; set; }
    }

    public class CustomerAddress
    {
        public int Id { get; set; }
        public string Label { get; set; } = string.Empty;
        public string Recipient { get; set; } = string.Empty;
        public string AddressLine { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string PostalCode { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
    }

    public class CustomerNotificationsViewModel
    {
        public int UnreadCount { get; set; }
        public int TotalCount { get; set; }
        public int FilteredCount { get; set; }
        public string SelectedFilter { get; set; } = "All";
        public List<CustomerNotification> Notifications { get; set; } = new();
    }

    public class CustomerNotification
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string CreatedLabel { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string Tone { get; set; } = "success";
        public bool IsRead { get; set; }
        public string ActionController { get; set; } = string.Empty;
        public string ActionName { get; set; } = string.Empty;
        public int ActionId { get; set; }
    }
}
