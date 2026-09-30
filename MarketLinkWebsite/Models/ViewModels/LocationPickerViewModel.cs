using System.ComponentModel.DataAnnotations;

namespace MarketLinkWebsite.Models.ViewModels;

public sealed class LocationPickerViewModel
{
    public string Suffix { get; set; } = "default";

    public string Title { get; set; } = "Your location";

    public string Hint { get; set; } = "We use this to show markets and farmers near you.";

    public decimal? Latitude { get; set; }

    public decimal? Longitude { get; set; }
}

public static class LocationInput
{
    public static decimal? ReadLatitude(string? raw)
    {
        return Coordinate.Read(raw);
    }

    public static decimal? ReadLongitude(string? raw)
    {
        return Coordinate.Read(raw);
    }

    public static bool LooksLikeAPair(decimal? latitude, decimal? longitude)
    {
        if (!latitude.HasValue || !longitude.HasValue)
        {
            return false;
        }

        return Math.Abs(latitude.Value) <= 90m && Math.Abs(longitude.Value) <= 180m;
    }
}

internal static class Coordinate
{
    public static decimal? Read(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!decimal.TryParse(raw.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return Math.Round(value, 7, MidpointRounding.AwayFromZero);
    }
}
