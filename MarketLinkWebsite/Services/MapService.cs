using System.Globalization;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace MarketLinkWebsite.Services;

public enum MapProvider
{
    OpenStreetMap,
    Google
}

public sealed class MapService
{
    private const string Separator = "%2C";
    private const double DefaultLatitude = 31.5204;
    private const double DefaultLongitude = 74.3587;

    private readonly IConfiguration configuration;

    public MapService(IConfiguration configuration)
    {
        this.configuration = configuration;
    }

    public MapProvider Provider =>
        string.Equals(configuration["Maps:Provider"], "google", StringComparison.OrdinalIgnoreCase)
            ? MapProvider.Google
            : MapProvider.OpenStreetMap;

    public string GoogleApiKey => configuration["Maps:GoogleApiKey"]?.Trim() ?? string.Empty;

    public bool GoogleReady => Provider == MapProvider.Google && GoogleApiKey.Length > 0;

    public string ProviderLabel => GoogleReady ? "Google Maps" : "OpenStreetMap";

    public double PlatformLatitude => Coordinate("Platform:Latitude", DefaultLatitude);
    public double PlatformLongitude => Coordinate("Platform:Longitude", DefaultLongitude);

    public string PlatformAddress =>
        configuration["Platform:SupportAddress"]?.Trim() is { Length: > 0 } address
            ? address
            : "Central Square, Northbridge";

    public string PlatformEmail => configuration["Platform:SupportEmail"] ?? "admin@marketlink.com";

    public string PlatformPhone => configuration["Platform:SupportPhone"] ?? string.Empty;

    public double CenterLatitude(decimal latitude) => latitude == 0m ? PlatformLatitude : (double)latitude;

    public double CenterLongitude(decimal longitude) => longitude == 0m ? PlatformLongitude : (double)longitude;

    // Directions follow the active provider so a shopper is handed a link that
    // matches the map they are already looking at.
    public string DirectionsUrl(decimal latitude, decimal longitude)
    {
        var lat = Format(CenterLatitude(latitude));
        var lon = Format(CenterLongitude(longitude));

        return GoogleReady
            ? "https://www.google.com/maps/dir/?api=1&destination=" + lat + "," + lon
            : "https://www.openstreetmap.org/directions?to=" + lat + "%2C" + lon;
    }

    public string ViewUrl(decimal latitude, decimal longitude)
    {
        var lat = Format(CenterLatitude(latitude));
        var lon = Format(CenterLongitude(longitude));

        return GoogleReady
            ? "https://www.google.com/maps/search/?api=1&query=" + lat + "," + lon
            : "https://www.openstreetmap.org/?mlat=" + lat + "&mlon=" + lon + "#map=16/" + lat + "/" + lon;
    }

    public string SearchUrl(string address) =>
        "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(address);

    public string? EmbeddedFrameUrl(IReadOnlyList<MapPin> pins, int zoom = 13)
    {
        if (pins.Count == 0)
        {
            return null;
        }

        return GoogleReady ? GoogleEmbed(pins, zoom) : OpenStreetMapEmbed(pins, zoom);
    }

    public string? SinglePinUrl(MapPin pin, int zoom = 15) => EmbeddedFrameUrl(new[] { pin }, zoom);

    private string GoogleEmbed(IReadOnlyList<MapPin> pins, int zoom)
    {
        // Average of the pins, so a group of markets is centred rather than
        // cropped onto whichever one happened to be first.
        var centerLat = pins.Average(pin => pin.Latitude);
        var centerLon = pins.Average(pin => pin.Longitude);

        var url = "https://www.google.com/maps/embed/v1/view?key=" + Uri.EscapeDataString(GoogleApiKey)
            + "&center=" + Format(centerLat) + "," + Format(centerLon)
            + "&zoom=" + zoom + "&maptype=roadmap";

        foreach (var pin in pins.Take(20))
        {
            url += "&markers=color:0x4f772d|label:" + Uri.EscapeDataString(Trim(pin.Label, 12))
                + "|" + Format(pin.Latitude) + "," + Format(pin.Longitude);
        }

        return url;
    }

    private static string OpenStreetMapEmbed(IReadOnlyList<MapPin> pins, int zoom)
    {
        var maxLat = pins[0].Latitude;
        var minLat = pins[0].Latitude;
        var maxLon = pins[0].Longitude;
        var minLon = pins[0].Longitude;

        foreach (var pin in pins)
        {
            maxLat = Math.Max(maxLat, pin.Latitude);
            minLat = Math.Min(minLat, pin.Latitude);
            maxLon = Math.Max(maxLon, pin.Longitude);
            minLon = Math.Min(minLon, pin.Longitude);
        }

        // A single pin leaves the box with no width or height, and OpenStreetMap
        // then renders nothing at all. Give every box a visible span.
        const double MinimumSpan = 0.01;
        if (maxLon - minLon < MinimumSpan)
        {
            var halfLon = (MinimumSpan - (maxLon - minLon)) / 2;
            minLon -= halfLon;
            maxLon += halfLon;
        }

        if (maxLat - minLat < MinimumSpan)
        {
            var halfLat = (MinimumSpan - (maxLat - minLat)) / 2;
            minLat -= halfLat;
            maxLat += halfLat;
        }

        return "https://www.openstreetmap.org/export/embed.html?bbox="
            + Format(minLon) + Separator
            + Format(minLat) + Separator
            + Format(maxLon) + Separator
            + Format(maxLat)
            + "&layer=mapnik&marker="
            + Format(pins[0].Latitude) + Separator + Format(pins[0].Longitude)
            + "&z=" + Math.Clamp(zoom, 3, 17);
    }

    /// <summary>
    /// An embeddable frame around a single location. The plain view URL is a full
    /// page and refuses to load inside an iframe, so the two must not be mixed up.
    /// </summary>
    public string EmbedUrlFor(decimal latitude, decimal longitude, int zoom = 15) =>
        SinglePinUrl(new MapPin
        {
            Latitude = CenterLatitude(latitude),
            Longitude = CenterLongitude(longitude)
        }, zoom) ?? string.Empty;

    private string CoordinatePair(decimal latitude, decimal longitude) =>
        Format(CenterLatitude(latitude)) + Separator + Format(CenterLongitude(longitude));

    private static string Format(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Trim(string value, int length) => value.Length <= length ? value : value[..length];

    private double Coordinate(string key, double fallback)
    {
        var raw = configuration[key];
        return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }
}

public sealed class MapPin
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string Label { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}
