using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace MarketLinkWebsite.Validation;

/// <summary>
/// Field level validation rules shared by the customer, farmer and admin forms.
/// Each attribute only accepts the characters that genuinely belong in that
/// field, so a name can never be a number and a phone number can never be a word.
/// </summary>
public static partial class FieldRules
{
    // Letters, spaces, apostrophes, hyphens and periods. A name may start with a
    // digit only when a farmer uses a trading name such as "3 Sons Farm", so the
    // rule allows a short leading number but rejects names that are mostly digits.
    [GeneratedRegex(@"^(?=.{2,80}$)(?!.*\d{4})[A-Za-z][A-Za-z\s'.\-]*(?:\s\d{1,2}[A-Za-z\s'.\-]*)?$", RegexOptions.CultureInvariant)]
    private static partial Regex PersonName();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9\s&'.\-]{1,60}$", RegexOptions.CultureInvariant)]
    private static partial Regex BusinessName();

    [GeneratedRegex(@"^[A-Za-z0-9\s,.\-'#]{5,150}$", RegexOptions.CultureInvariant)]
    private static partial Regex AddressLine();

    [GeneratedRegex(@"^[+]?[0-9][0-9\s\-()]{6,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneNumber();

    [GeneratedRegex(@"^\d{3,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex WholeNumber();

    // Postal codes vary by country, so digits and a small set of letters are both
    // accepted. Spaces, hyphens and dots are allowed as separators.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9\s\-]{2,9}$", RegexOptions.CultureInvariant)]
    private static partial Regex PostalCode();

    [GeneratedRegex(@"^-?\d{1,3}(?:\.\d{1,7})?$", RegexOptions.CultureInvariant)]
    private static partial Regex Coordinate();

    public static bool IsPersonName(string? value) =>
        string.IsNullOrWhiteSpace(value) || PersonName().IsMatch(value.Trim());

    public static bool IsBusinessName(string? value) =>
        string.IsNullOrWhiteSpace(value) || BusinessName().IsMatch(value.Trim());

    public static bool IsAddress(string? value) =>
        string.IsNullOrWhiteSpace(value) || AddressLine().IsMatch(value.Trim());

    public static bool IsPhone(string? value) =>
        string.IsNullOrWhiteSpace(value) || PhoneNumber().IsMatch(value.Trim());

    public static bool IsWholeNumber(string? value) =>
        string.IsNullOrWhiteSpace(value) || WholeNumber().IsMatch(value.Trim());

    public static bool IsPostalCode(string? value) =>
        string.IsNullOrWhiteSpace(value) || PostalCode().IsMatch(value.Trim());

    public static bool IsCoordinate(string? value) =>
        string.IsNullOrWhiteSpace(value) || Coordinate().IsMatch(value.Trim());
}

/// <summary>Accepts only a person's name, never a number or symbol run.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class PersonNameAttribute : ValidationAttribute
{
    public PersonNameAttribute() => ErrorMessage = "Enter a valid name using letters only.";

    public override bool IsValid(object? value) => FieldRules.IsPersonName(value as string);
}

/// <summary>Accepts a stall, farm or business name.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class BusinessNameAttribute : ValidationAttribute
{
    public BusinessNameAttribute() => ErrorMessage = "Enter a valid stall or business name.";

    public override bool IsValid(object? value) => FieldRules.IsBusinessName(value as string);
}

/// <summary>Accepts a street address, including house numbers.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class StreetAddressAttribute : ValidationAttribute
{
    public StreetAddressAttribute() => ErrorMessage = "Enter a valid address.";

    public override bool IsValid(object? value) => FieldRules.IsAddress(value as string);
}

/// <summary>Accepts digits, spaces and the usual phone separators only.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class PhoneNumberAttribute : ValidationAttribute
{
    public PhoneNumberAttribute() => ErrorMessage = "Enter a valid contact number.";

    public override bool IsValid(object? value) => FieldRules.IsPhone(value as string);
}

/// <summary>Accepts a whole number and nothing else.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class WholeNumberAttribute : ValidationAttribute
{
    public WholeNumberAttribute() => ErrorMessage = "Enter a whole number.";

    public override bool IsValid(object? value) => FieldRules.IsWholeNumber(value as string);
}

/// <summary>Accepts a postal code made of letters, digits and separators.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class PostalCodeAttribute : ValidationAttribute
{
    public PostalCodeAttribute() => ErrorMessage = "Enter a valid postal code.";

    public override bool IsValid(object? value) => FieldRules.IsPostalCode(value as string);
}

/// <summary>Accepts a latitude or longitude value.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class CoordinateAttribute : ValidationAttribute
{
    public CoordinateAttribute() => ErrorMessage = "Enter a valid coordinate, for example 33.6844.";

    public override bool IsValid(object? value) => FieldRules.IsCoordinate(value as string);
}
