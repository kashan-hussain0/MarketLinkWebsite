using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace MarketLinkWebsite.Validation;

/// <summary>
/// Requires a checkbox to be ticked, such as the terms and conditions box.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class MustBeTrueAttribute : ValidationAttribute
{
    public MustBeTrueAttribute(string errorMessage) => ErrorMessage = errorMessage;

    public override bool IsValid(object? value) => value is true;
}

/// <summary>
/// Requires a date to fall on or after a given day. Used for pickup dates, which
/// can never be set in the past.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class NotBeforeTodayAttribute : ValidationAttribute
{
    private const int MaxDaysAhead = 90;

    public NotBeforeTodayAttribute(string errorMessage) => ErrorMessage = errorMessage;

    public override bool IsValid(object? value)
    {
        if (value is not DateTime date)
        {
            return true;
        }

        var day = date.Date;
        return day >= DateTime.Today && day <= DateTime.Today.AddDays(MaxDaysAhead);
    }
}

/// <summary>
/// Checks an uploaded picture: an allowed extension, an allowed content type and a
/// size ceiling. The bytes are checked again when the file is stored.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ImageUploadAttribute : ValidationAttribute
{
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };

    public ImageUploadAttribute(int maxMegabytes, string errorMessage)
    {
        MaxMegabytes = maxMegabytes;
        ErrorMessage = errorMessage;
    }

    public int MaxMegabytes { get; }

    public override bool IsValid(object? value)
    {
        // An optional picture is fine when nothing was chosen.
        if (value is not IFormFile file || file.Length == 0)
        {
            return true;
        }

        if (file.Length > MaxMegabytes * 1024L * 1024L)
        {
            return false;
        }

        var extension = Path.GetExtension(file.FileName);
        return !string.IsNullOrWhiteSpace(extension)
            && AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Checks a clock time written in the 24 hour form the market and pickup slot
/// fields use, for example 08:30.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class ClockTimeAttribute : ValidationAttribute
{
    public ClockTimeAttribute(string errorMessage) => ErrorMessage = errorMessage;

    public override bool IsValid(object? value)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return TimeOnly.TryParse(text.Trim(), System.Globalization.CultureInfo.InvariantCulture, out _);
    }
}
