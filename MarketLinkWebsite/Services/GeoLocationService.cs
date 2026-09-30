using System.Globalization;
using System.Security.Claims;
using MarketLinkWebsite.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Services;

public sealed class GeoLocationService
{
    public const string CookieName = "ml-geo";
    private const int MaxAgeDays = 90;

    private readonly IHttpContextAccessor accessor;
    private readonly ApplicationDbContext db;

    public GeoLocationService(IHttpContextAccessor accessor, ApplicationDbContext db)
    {
        this.accessor = accessor;
        this.db = db;
    }

    public bool HasLocation { get; private set; }

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    /// <summary>The place the distance was taken from, for the wording on screen.</summary>
    public string SourceLabel { get; private set; } = string.Empty;

    /// <summary>
    /// Reads the visitor's position. The cookie set by the browser is used first
    /// because it is the most recent. When there is no cookie, the position saved
    /// on the account or on the default pickup address is used instead, so a
    /// signed in customer still sees distances after switching device.
    /// </summary>
    public GeoLocationService Read()
    {
        HasLocation = false;
        Latitude = 0;
        Longitude = 0;
        SourceLabel = string.Empty;

        var context = accessor.HttpContext;
        if (context is null)
        {
            return this;
        }

        if (TryApply(context.Request.Cookies[CookieName], "your device"))
        {
            return this;
        }

        ApplySavedLocation(context);
        return this;
    }

    public void Write(double latitude, double longitude)
    {
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            return;
        }

        var context = accessor.HttpContext;
        if (context is null)
        {
            return;
        }

        var value = string.Create(
            CultureInfo.InvariantCulture,
            $"{Math.Round(latitude, 5)},{Math.Round(longitude, 5)}");

        context.Response.Cookies.Append(CookieName, value, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddDays(MaxAgeDays),
            HttpOnly = false,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Path = "/"
        });

        Latitude = Math.Round(latitude, 5);
        Longitude = Math.Round(longitude, 5);
        HasLocation = true;
        SourceLabel = "your device";
    }

    public double DistanceTo(double latitude, double longitude) =>
        HasLocation ? GeoDistance.HaversineKm(Latitude, Longitude, latitude, longitude) : 0;

    private bool TryApply(string? raw, string label)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
        {
            return false;
        }

        return Apply(latitude, longitude, label);
    }

    private bool Apply(double latitude, double longitude, string label)
    {
        if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            return false;
        }

        if (latitude == 0 && longitude == 0)
        {
            return false;
        }

        Latitude = Math.Round(latitude, 5);
        Longitude = Math.Round(longitude, 5);
        HasLocation = true;
        SourceLabel = label;
        return true;
    }

    private void ApplySavedLocation(HttpContext context)
    {
        var userId = context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var account = db.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.Latitude, user.Longitude })
            .FirstOrDefault();

        if (account?.Latitude is decimal savedLat && account.Longitude is decimal savedLon
            && Apply((double)savedLat, (double)savedLon, "your saved location"))
        {
            return;
        }

        var address = db.Addresses
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.Latitude != null && item.Longitude != null)
            .OrderByDescending(item => item.IsDefault)
            .ThenByDescending(item => item.CreatedAt)
            .Select(item => new { item.Latitude, item.Longitude })
            .FirstOrDefault();

        if (address?.Latitude is decimal addressLat && address.Longitude is decimal addressLon)
        {
            Apply((double)addressLat, (double)addressLon, "your saved address");
        }
    }
}
