using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Services;

public sealed class AssistantService
{
    private readonly ApplicationDbContext db;

    public AssistantService(ApplicationDbContext db)
    {
        this.db = db;
    }

    public async Task<List<AssistantReply>> AnswerAsync(string question, CancellationToken cancellationToken = default)
    {
        var text = question?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return new List<AssistantReply>
            {
                AssistantReply.Ask("Tell me what you are looking for, for example “open Saturday”, “eggs” or “Cedar Hill Apiary”.")
            };
        }

        var term = text.ToLowerInvariant();
        var replies = new List<AssistantReply>();

        if (Mentions(term, "hour", "open", "close", "timing", "time", "schedule", "kab"))
        {
            replies.Add(await MarketTimingsAsync(cancellationToken));
        }

        if (Mentions(term, "pickup", "collect", "window", "slot", "cutoff", "cut-off", "uthana", "lene"))
        {
            replies.Add(await PickupWindowsAsync(cancellationToken));
        }

        if (Mentions(term, "available", "availability", "trading", "which farm", "who is", "saturday", "sunday", "friday", "wednesday", "monday", "tuesday", "thursday"))
        {
            replies.Add(await FarmerAvailabilityAsync(term, cancellationToken));
        }

        if (Mentions(term, "direction", "map", "where", "location", "address", "kahan", "route"))
        {
            replies.Add(await DirectionsAsync(cancellationToken));
        }

        if (Mentions(term, "review", "rating", "rated", "rate", "feedback", "star", "opinion", "best", "top"))
        {
            replies.Add(await RatingsAsync(cancellationToken));
        }

        if (Mentions(term, "payment", "pay", "card", "cash", "price", "free", "fee"))
        {
            replies.Add(AssistantReply.Ask("Payment is taken at the stall when you collect your pre-order. MarketLink does not charge online, so there are no card fees and you only pay for what you reserved."));
        }

        if (Mentions(term, "cancel", "return", "refund", "change", "modify", "edit"))
        {
            replies.Add(AssistantReply.Ask("You can edit or cancel a reservation yourself from My orders, but only until the farmer accepts it. After acceptance the farmer prepares your items, so contact the market if you need help."));
        }

        if (Mentions(term, "sell", "farmer", "farm", "grow", "stall", "vendor", "kisan"))
        {
            replies.Add(await FarmersAsync(term, cancellationToken));
        }

        if (replies.Count == 0 || Mentions(term, "product", "item", "find", "search", "looking", "want", "available", "stock", "chahiye", "organic", "koi"))
        {
            replies.Add(await ProductsAsync(term, cancellationToken));
        }

        replies.Add(AssistantReply.Ask("Ask me about market days, pickup directions, farmer ratings, what is in season, or how pre-orders and payments work."));

        return replies.Where(reply => reply != null).Select(reply => reply!).ToList();
    }

    private static bool Mentions(string term, params string[] needles) => needles.Any(needle => term.Contains(needle, StringComparison.Ordinal));

    private static readonly string[] Days =
    [
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"
    ];

    private async Task<AssistantReply> MarketTimingsAsync(CancellationToken cancellationToken)
    {
        var markets = await db.Markets
            .AsNoTracking()
            .Where(market => market.IsActive)
            .OrderBy(market => market.Name)
            .Select(market => new { market.Name, market.OperatingDays, market.Address, market.City })
            .ToListAsync(cancellationToken);

        if (markets.Count == 0)
        {
            return AssistantReply.Ask("No markets are published right now. Please check back soon.");
        }

        var lines = markets
            .Select(market => $"{market.Name} is open on {market.OperatingDays} at {market.Address}, {market.City}.")
            .Take(4);

        return AssistantReply.Ask("Here is the market calendar: " + string.Join(" ", lines));
    }

    /// <summary>
    /// Answers with the pickup window each farm publishes, plus when it stops
    /// taking pre-orders for the next market day.
    /// </summary>
    private async Task<AssistantReply> PickupWindowsAsync(CancellationToken cancellationToken)
    {
        var profiles = await db.FarmerProfiles
            .AsNoTracking()
            .Where(profile => profile.Status == FarmerStatus.Active
                && profile.User.IsActive
                && profile.PickupWindows != string.Empty)
            .OrderBy(profile => profile.FarmName)
            .Select(profile => new
            {
                profile.FarmName,
                profile.PickupWindows,
                profile.OrderCutoffHours,
                profile.OperatingDays
            })
            .Take(5)
            .ToListAsync(cancellationToken);

        if (profiles.Count == 0)
        {
            return AssistantReply.Ask("Farmers have not published pickup windows yet. You will always see the exact slot at checkout.");
        }

        var lines = profiles.Select(profile =>
        {
            var cutoff = profile.OrderCutoffHours > 0
                ? $" Stops taking pre-orders {profile.OrderCutoffHours} hours before pickup."
                : string.Empty;
            return $"{profile.FarmName} collects on {profile.OperatingDays} between {profile.PickupWindows}.{cutoff}";
        });

        return AssistantReply.Ask("Pickup windows: " + string.Join(" ", lines));
    }

    /// <summary>
    /// Answers which farms trade on a named day, so a shopper can plan a single trip.
    /// </summary>
    private async Task<AssistantReply> FarmerAvailabilityAsync(string term, CancellationToken cancellationToken)
    {
        var day = Days.FirstOrDefault(candidate => term.Contains(candidate, StringComparison.OrdinalIgnoreCase));

        if (day is null)
        {
            return AssistantReply.Ask("Tell me a day such as Saturday or Friday and I will list the farms trading that day.");
        }

        var farms = await db.FarmerProfiles
            .AsNoTracking()
            .Where(profile => profile.Status == FarmerStatus.Active
                && profile.User.IsActive
                && profile.OperatingDays.Contains(day))
            .OrderBy(profile => profile.FarmName)
            .Select(profile => new
            {
                profile.FarmName,
                profile.City,
                LiveItems = profile.Products.Count(product => product.IsAvailable)
            })
            .Take(5)
            .ToListAsync(cancellationToken);

        if (farms.Count == 0)
        {
            return AssistantReply.Ask($"No farm is trading on {day} right now. Try another day and I will check again.");
        }

        var lines = farms.Select(farm => $"{farm.FarmName} in {farm.City} with {farm.LiveItems} live items.");

        return AssistantReply.Ask($"Farms trading on {day}: " + string.Join(" ", lines));
    }

    private async Task<AssistantReply> DirectionsAsync(CancellationToken cancellationToken)
    {
        var markets = await db.Markets
            .AsNoTracking()
            .Where(market => market.IsActive && market.Latitude != 0 && market.Longitude != 0)
            .OrderBy(market => market.Name)
            .Select(market => new { market.Name, market.Address, market.City, market.Latitude, market.Longitude })
            .ToListAsync(cancellationToken);

        if (markets.Count == 0)
        {
            return AssistantReply.Ask("No market has a map pin yet. Open Markets and map to see addresses.");
        }

        var links = markets
            .Take(3)
            .Select(market => $"{market.Name}: <a href=\"https://www.openstreetmap.org/directions?to={market.Latitude}%2C{market.Longitude}\" target=\"_blank\" rel=\"noopener\">open directions</a>");

        return AssistantReply.Ask("You can plan a route from any market page: " + string.Join(" · ", links));
    }

    private async Task<AssistantReply> RatingsAsync(CancellationToken cancellationToken)
    {
        var farms = await db.FarmerProfiles
            .AsNoTracking()
            .Where(profile => profile.Status == FarmerStatus.Active && profile.ReviewCount > 0)
            .OrderByDescending(profile => profile.Rating)
            .Select(profile => new { profile.FarmName, profile.Rating, profile.ReviewCount })
            .Take(3)
            .ToListAsync(cancellationToken);

        if (farms.Count == 0)
        {
            return AssistantReply.Ask("No farm has been rated yet. Once customers leave reviews they appear on the farm profile page.");
        }

        var lines = farms.Select(farm => $"{farm.FarmName} is rated {farm.Rating:0.0} out of 5 from {farm.ReviewCount} review(s).");
        return AssistantReply.Ask("Highest rated farms right now: " + string.Join(" ", lines));
    }

    private async Task<AssistantReply> FarmersAsync(string term, CancellationToken cancellationToken)
    {
        var query = db.FarmerProfiles
            .AsNoTracking()
            .Where(profile => profile.Status == FarmerStatus.Active && profile.User.IsActive);

        if (term.Length > 3)
        {
            query = query.Where(profile => profile.FarmName.Contains(term) || profile.City.Contains(term));
        }

        var farms = await query
            .OrderByDescending(profile => profile.Rating)
            .Take(4)
            .Select(profile => new
            {
                profile.Id,
                profile.FarmName,
                profile.City,
                profile.Rating,
                profile.ReviewCount,
                Products = profile.Products.Count(item => item.IsAvailable)
            })
            .ToListAsync(cancellationToken);

        if (farms.Count == 0)
        {
            return AssistantReply.Ask("I could not find a matching farm. Try the farm name or browse Explore product.");
        }

        var lines = farms.Select(farm => $"<a href=\"/Farm/{farm.Id}\">{farm.FarmName}</a> in {farm.City} has {farm.Products} live listing(s) and is rated {farm.Rating:0.0}.");
        return AssistantReply.Ask("Here is what I found: " + string.Join(" ", lines));
    }

    private static readonly string[] SearchFiller =
    {
        "show", "find", "search", "looking", "look", "for", "want", "need", "please", "give",
        "me", "any", "some", "there", "have", "has", "get", "buy", "order", "chahiye", "koi",
        "this", "week", "currently", "available", "list", "kya", "what", "which", "where",
        "product", "goods", "stuff", "thing", "things", "organic", "local", "fresh", "near",
        "around", "nearby", "market", "markets", "farms", "farmers", "seller", "sellers",
        "suggest", "recommend", "options", "option", "today", "tomorrow", "weekend"
    };

    private async Task<AssistantReply> ProductsAsync(string term, CancellationToken cancellationToken)
    {
        var wantsOrganic = term.Contains("organic", StringComparison.Ordinal);
        var tokens = term
            .Split(new[] { ' ', ',', '.', '?', '!' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length > 1)
            .Where(token => !SearchFiller.Contains(token, StringComparer.OrdinalIgnoreCase))
            .Select(token => token.ToLowerInvariant())
            .Distinct()
            .Take(4)
            .ToList();

        var candidates = await db.Products
            .AsNoTracking()
            .Where(product => product.IsAvailable
                && product.Category.IsActive
                && product.FarmerProfile.Status == FarmerStatus.Active
                && product.FarmerProfile.User.IsActive)
            .Where(product => !wantsOrganic || product.IsOrganic)
            .Select(product => new
            {
                product.Id,
                product.Name,
                product.Price,
                product.Unit,
                product.IsOrganic,
                Category = product.Category.Name,
                Farm = product.FarmerProfile.FarmName,
                Available = product.Inventory != null ? product.Inventory.QuantityAvailable : 0
            })
            .ToListAsync(cancellationToken);

        var matches = candidates
            .Select(product => new
            {
                Product = product,
                Score = tokens.Count(token =>
                    product.Name.ToLowerInvariant().Contains(token, StringComparison.Ordinal)
                    || product.Category.ToLowerInvariant().Contains(token, StringComparison.Ordinal)
                    || product.Farm.ToLowerInvariant().Contains(token, StringComparison.Ordinal))
            })
            .Where(row => tokens.Count == 0 || row.Score > 0)
            .OrderByDescending(row => row.Score)
            .ThenByDescending(row => row.Product.Available)
            .Take(5)
            .ToList();

        if (matches.Count == 0)
        {
            var suggestion = tokens.Count > 0 ? " “" + string.Join(" ", tokens) + "”" : string.Empty;
            return AssistantReply.Ask($"I could not find{suggestion} in the catalogue this week. Try a broader word such as “apples”, “eggs” or “bread”.");
        }

        var qualifier = wantsOrganic ? " organic" : string.Empty;
        var lines = matches.Select(row =>
            $"<a href=\"/Product/Details/{row.Product.Id}\">{row.Product.Name}</a> from {row.Product.Farm} at {row.Product.Price:0.00} ({row.Product.Available} available).");

        return AssistantReply.Html($"Here is the{qualifier} product available this week: " + string.Join(" ", lines));
    }
}

public sealed class AssistantReply
{
    public string Message { get; private init; } = string.Empty;
    public bool IsHtml { get; private init; }

    public static AssistantReply Ask(string message) => new() { Message = message, IsHtml = false };

    public static AssistantReply Html(string message) => new() { Message = message, IsHtml = true };

    public override string ToString() => Message;
}
