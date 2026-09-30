using System.Globalization;

namespace MarketLinkWebsite.Services;

public static class GeoDistance
{
    private const double EarthRadiusKm = 6371.0088;

    public static double HaversineKm(double latitudeA, double longitudeA, double latitudeB, double longitudeB)
    {
        if (latitudeA == 0 && longitudeA == 0)
        {
            return 0;
        }

        if (latitudeB == 0 && longitudeB == 0)
        {
            return 0;
        }

        var dLat = ToRadians(latitudeB - latitudeA);
        var dLon = ToRadians(longitudeB - longitudeA);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(ToRadians(latitudeA)) * Math.Cos(ToRadians(latitudeB))
            * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return Math.Round(EarthRadiusKm * c, 1);
    }

    public static string Label(double kilometres) => kilometres <= 0
        ? string.Empty
        : kilometres < 1
            ? $"{Math.Round(kilometres * 1000 / 50) * 50} m away"
            : $"{kilometres.ToString("0.0", CultureInfo.InvariantCulture)} km away";

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
