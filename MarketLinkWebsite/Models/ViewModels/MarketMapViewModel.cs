using System.ComponentModel.DataAnnotations;
using MarketLinkWebsite.Models.Entities;

namespace MarketLinkWebsite.Models.ViewModels;

public sealed class MarketMapMarker
{
    [Required]
    public string Kind { get; set; } = "market";

    [Required]
    public double Lat { get; set; }

    [Required]
    public double Lon { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Subtitle { get; set; }

    public string? Image { get; set; }

    public string? Stall { get; set; }

    public string? Href { get; set; }

    public List<MarketMapFact> Meta { get; set; } = new();
}

public sealed class MarketMapFact
{
    public string Label { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

public sealed class MarketMapViewModel
{
    public string Title { get; set; } = "Markets and farms near you";

    public string Subtitle { get; set; } = "Every market and farmer stall with a pinned location.";

    public string HeightClass { get; set; } = "market-map-canvas-tall";

    public List<MarketMapMarker> Markers { get; set; } = new();

    public static MarketMapViewModel ForMarkets(IEnumerable<Market> markets)
    {
        var model = new MarketMapViewModel();

        foreach (var market in markets ?? Enumerable.Empty<Market>())
        {
            if (market.Latitude == 0m && market.Longitude == 0m)
            {
                continue;
            }

            model.Markers.Add(new MarketMapMarker
            {
                Kind = "market",
                Lat = (double)market.Latitude,
                Lon = (double)market.Longitude,
                Title = market.Name,
                Subtitle = Join(market.Address, market.City),
                Image = market.ImageUrl,
                Href = $"/Market/Details/{market.Id}",
                Meta = new List<MarketMapFact>
                {
                    new() { Label = "Open", Value = Join(market.OperatingDays, $"{market.OpenTime} to {market.CloseTime}") ?? "Not published" }
                }
            });
        }

        return model;
    }

    public static MarketMapViewModel ForStalls(
        IEnumerable<MarketFarmer> links,
        Func<int, Market?> marketOf,
        Func<int, FarmerProfile?> farmerOf)
    {
        var model = new MarketMapViewModel
        {
            Title = "Farmer stalls",
            Subtitle = "Tap a stall for its pickup point and directions."
        };

        foreach (var link in links ?? Enumerable.Empty<MarketFarmer>())
        {
            var farmer = farmerOf(link.FarmerProfileId);
            if (farmer is null)
            {
                continue;
            }

            if (farmer.Latitude == 0m && farmer.Longitude == 0m)
            {
                continue;
            }

            var market = marketOf(link.MarketId);
            var facts = new List<MarketMapFact>
            {
                new() { Label = "Open", Value = farmer.OperatingDays },
                new() { Label = "Pickup", Value = farmer.PickupWindows }
            };

            if (market is not null)
            {
                facts.Add(new MarketMapFact { Label = "Market", Value = market.Name });
            }

            model.Markers.Add(new MarketMapMarker
            {
                Kind = "farmer",
                Lat = (double)farmer.Latitude,
                Lon = (double)farmer.Longitude,
                Title = farmer.FarmName,
                Subtitle = Join(farmer.Address, farmer.City),
                Image = farmer.User?.ProfilePictureUrl,
                Stall = string.IsNullOrWhiteSpace(link.StallNumber) ? null : link.StallNumber,
                Href = $"/Farm/Index?id={farmer.Id}",
                Meta = facts
            });
        }

        return model;
    }

    public static MarketMapMarker? ForDetail(MarketCardViewModel market)
    {
        if (market is null || (market.Latitude == 0d && market.Longitude == 0d))
        {
            return null;
        }

        return new MarketMapMarker
        {
            Kind = "market",
            Lat = market.Latitude,
            Lon = market.Longitude,
            Title = market.Name,
            Subtitle = Join(market.Address, market.City),
            Image = market.ImageUrl,
            Meta = new List<MarketMapFact>
            {
                new() { Label = "Open", Value = Join(market.Day, $"{market.OpenTime} to {market.CloseTime}") ?? "Not published" }
            }
        };
    }

    private static string? Join(string? first, string? second)
    {
        var a = (first ?? string.Empty).Trim();
        var b = (second ?? string.Empty).Trim();

        if (a.Length == 0) return string.IsNullOrWhiteSpace(b) ? null : b;
        if (b.Length == 0) return a;
        return a + ", " + b;
    }
}
