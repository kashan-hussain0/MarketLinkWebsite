using MarketLinkWebsite.Models.ViewModels;

namespace MarketLinkWebsite.Data;

public static class DemoCatalogData
{
    public static IReadOnlyList<ProductCardViewModel> Products { get; } =
    [
        new()
        {
            Id = 1,
            Name = "Honeycrisp Apples",
            ShortDescription = "Crisp, naturally sweet orchard apples harvested this week.",
            Description = "Small-batch apples grown without synthetic colourings. Each box is hand-selected for crispness, sweetness and a refreshing finish. Bring your own reusable bag and collect them directly from the grower.",
            Category = "Fruits",
            Price = 3.50m,
            Unit = "kg",
            QuantityAvailable = 15,
            ImageUrl = "https://images.unsplash.com/photo-1560806887-1e4cd0b6cbd6?auto=format&fit=crop&w=900&q=85",
            FarmerName = "Ayesha Khan",
            FarmName = "Green Valley Farm",
            MarketName = "Downtown Farmers Market",
            MarketDay = "Saturday",
            DistanceKm = 2.4,
            Rating = 4.9,
            ReviewCount = 86,
            IsOrganic = true,
            Badge = "Farmer favourite"
        },
        new()
        {
            Id = 2,
            Name = "Free-range Eggs",
            ShortDescription = "Large brown eggs from pasture-raised local hens.",
            Description = "Collected daily from a small free-range flock. The eggs have rich golden yolks, clean flavour and a dependable shelf life, making them suitable for everyday family cooking and baking.",
            Category = "Dairy",
            Price = 5.00m,
            Unit = "dozen",
            QuantityAvailable = 8,
            ImageUrl = "https://images.unsplash.com/photo-1582722872445-44dc5f7e3c8f?auto=format&fit=crop&w=900&q=85",
            FarmerName = "Daniel Brooks",
            FarmName = "Sunrise Poultry",
            MarketName = "Community Green Square",
            MarketDay = "Sunday",
            DistanceKm = 4.1,
            Rating = 4.8,
            ReviewCount = 64,
            IsOrganic = false,
            Badge = "Only 8 left"
        },
        new()
        {
            Id = 3,
            Name = "Country Sourdough",
            ShortDescription = "Slow-fermented loaf baked with locally milled flour.",
            Description = "A naturally leavened country loaf with a caramelised crust and soft open crumb. Baked early each market morning using stone-ground flour and a traditional long fermentation.",
            Category = "Baked goods",
            Price = 6.50m,
            Unit = "loaf",
            QuantityAvailable = 10,
            ImageUrl = "https://images.unsplash.com/photo-1509440159596-0249088772ff?auto=format&fit=crop&w=900&q=85",
            FarmerName = "Mariam Noor",
            FarmName = "Homestead Oven",
            MarketName = "Downtown Farmers Market",
            MarketDay = "Saturday",
            DistanceKm = 2.4,
            Rating = 4.9,
            ReviewCount = 112,
            IsOrganic = false,
            Badge = "Baked today"
        },
        new()
        {
            Id = 4,
            Name = "Raw Wildflower Honey",
            ShortDescription = "Unfiltered seasonal honey from neighbourhood hives.",
            Description = "Raw, unfiltered honey with floral notes and a naturally golden finish. Every jar is sourced from hives placed around the city edge and is never heated during processing.",
            Category = "Dairy",
            Price = 9.00m,
            Unit = "jar",
            QuantityAvailable = 5,
            ImageUrl = "https://images.unsplash.com/photo-1587049352847-4a222e784d38?auto=format&fit=crop&w=900&q=85",
            FarmerName = "Omar Sheikh",
            FarmName = "Meadow Apiaries",
            MarketName = "Riverside Growers Market",
            MarketDay = "Wednesday",
            DistanceKm = 6.8,
            Rating = 4.7,
            ReviewCount = 49,
            IsOrganic = false,
            Badge = "Small batch"
        },
        new()
        {
            Id = 5,
            Name = "Vine Tomatoes",
            ShortDescription = "Sweet field tomatoes with a rich, balanced flavour.",
            Description = "A colourful mix of vine and beefsteak tomatoes grown in open fields. Their firm texture makes them excellent for salads, sandwiches and everyday cooking.",
            Category = "Vegetables",
            Price = 4.25m,
            Unit = "kg",
            QuantityAvailable = 22,
            ImageUrl = "https://images.unsplash.com/photo-1546094096-0df4bcaaa337?auto=format&fit=crop&w=900&q=85",
            FarmerName = "Ayesha Khan",
            FarmName = "Green Valley Farm",
            MarketName = "Downtown Farmers Market",
            MarketDay = "Saturday",
            DistanceKm = 2.4,
            Rating = 4.8,
            ReviewCount = 73,
            IsOrganic = true,
            Badge = "Peak season"
        },
        new()
        {
            Id = 6,
            Name = "Fresh Carrot Bunch",
            ShortDescription = "Sweet, tender carrots with leafy tops attached.",
            Description = "A generous bunch of freshly lifted carrots. Their crisp texture works beautifully raw, roasted or added to soups and everyday family meals.",
            Category = "Vegetables",
            Price = 2.80m,
            Unit = "bunch",
            QuantityAvailable = 18,
            ImageUrl = "https://images.unsplash.com/photo-1447175008436-054170c2e979?auto=format&fit=crop&w=900&q=85",
            FarmerName = "Bilal Ahmed",
            FarmName = "Earthbound Organics",
            MarketName = "Community Green Square",
            MarketDay = "Sunday",
            DistanceKm = 4.1,
            Rating = 4.6,
            ReviewCount = 38,
            IsOrganic = true,
            Badge = "Organic"
        },
        new()
        {
            Id = 7,
            Name = "Creamy Farm Milk",
            ShortDescription = "Fresh whole milk from grass-fed local cows.",
            Description = "A clean, full-flavoured whole milk supplied in returnable glass bottles. Chilled and prepared for collection on the morning of your pickup.",
            Category = "Dairy",
            Price = 3.80m,
            Unit = "litre",
            QuantityAvailable = 30,
            ImageUrl = "https://images.unsplash.com/photo-1550583724-b2692b85b150?auto=format&fit=crop&w=900&q=85",
            FarmerName = "Daniel Brooks",
            FarmName = "Sunrise Dairy",
            MarketName = "Riverside Growers Market",
            MarketDay = "Wednesday",
            DistanceKm = 6.8,
            Rating = 4.7,
            ReviewCount = 91,
            IsOrganic = false,
            Badge = "Returnable bottle"
        },
        new()
        {
            Id = 8,
            Name = "Farmstead Cheese Selection",
            ShortDescription = "A balanced selection of mild artisan cheeses.",
            Description = "A rotating selection of mild cheddar-style, fresh and pressed cheeses made in small batches. Ask the grower about varieties and storage guidance at pickup.",
            Category = "Dairy",
            Price = 8.50m,
            Unit = "selection",
            QuantityAvailable = 6,
            ImageUrl = "https://images.unsplash.com/photo-1486297678162-eb2a19b0a32d?auto=format&fit=crop&w=900&q=85",
            FarmerName = "Daniel Brooks",
            FarmName = "Sunrise Dairy",
            MarketName = "Downtown Farmers Market",
            MarketDay = "Saturday",
            DistanceKm = 2.4,
            Rating = 4.8,
            ReviewCount = 44,
            IsOrganic = false,
            Badge = "New this week"
        }
    ];

    public static IReadOnlyList<MarketCardViewModel> Markets { get; } =
    [
        new()
        {
            Id = 1,
            Name = "Downtown Farmers Market",
            Description = "The city's best-known weekend market, bringing together product growers, bakers and small food productrs.",
            Address = "Central Square, Main Avenue",
            City = "Downtown",
            Day = "Saturday",
            OpenTime = "7:00 AM",
            CloseTime = "1:00 PM",
            FarmerCount = 24,
            ProductCount = 86,
            DistanceKm = 2.4,
            Latitude = 33.6844000,
            Longitude = 73.0479000,
            IsOpenThisWeek = true,
            ImageUrl = "https://images.unsplash.com/photo-1488459716781-31db52582fe9?auto=format&fit=crop&w=1200&q=85",
            EmbedUrl = "https://www.openstreetmap.org/export/embed.html?bbox=73.0279%2C33.6744%2C73.0679%2C33.6944&layer=mapnik&marker=33.6844%2C73.0479"
        },
        new()
        {
            Id = 2,
            Name = "Community Green Square",
            Description = "A relaxed neighbourhood market focused on organic product, pantry staples and family-friendly makers.",
            Address = "East Park Boulevard",
            City = "Riverside",
            Day = "Sunday",
            OpenTime = "8:00 AM",
            CloseTime = "2:00 PM",
            FarmerCount = 18,
            ProductCount = 64,
            DistanceKm = 4.1,
            Latitude = 33.6987000,
            Longitude = 73.0632000,
            IsOpenThisWeek = true,
            ImageUrl = "https://images.unsplash.com/photo-1472653431158-6364773b2a56?auto=format&fit=crop&w=1200&q=85",
            EmbedUrl = "https://www.openstreetmap.org/export/embed.html?bbox=73.0458%2C33.6932%2C73.0858%2C33.7132&layer=mapnik&marker=33.7032%2C73.0658"
        },
        new()
        {
            Id = 3,
            Name = "Riverside Growers Market",
            Description = "A mid-week market for convenient pickup, featuring fresh dairy, honey and everyday family essentials.",
            Address = "Canal Walk, Warehouse District",
            City = "Riverside",
            Day = "Wednesday",
            OpenTime = "3:00 PM",
            CloseTime = "7:00 PM",
            FarmerCount = 14,
            ProductCount = 42,
            DistanceKm = 6.8,
            Latitude = 33.6548000,
            Longitude = 73.0701000,
            IsOpenThisWeek = true,
            ImageUrl = "https://images.unsplash.com/photo-1533900298318-6b8da08a523e?auto=format&fit=crop&w=1200&q=85",
            EmbedUrl = "https://www.openstreetmap.org/export/embed.html?bbox=73.0414%2C33.6521%2C73.0814%2C33.6721&layer=mapnik&marker=33.6621%2C73.0614"
        }
    ];
}
