namespace MarketLinkWebsite.Areas.Farmer.Controllers;

/// <summary>
/// Decides whether an uploaded file really is an image by reading its file signature,
/// so the browser cannot block a valid picture with a slightly wrong content type and
/// a non-image cannot slip through by claiming to be one.
/// </summary>
internal static class ImageUpload
{
    public const long MaxBytes = 8 * 1024 * 1024;

    public static string? ExtensionFor(string fileName, string? contentType, byte[] header)
    {
        var byContent = FromSignature(header);
        if (byContent is not null)
        {
            return byContent;
        }

        var nameExtension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        var byName = nameExtension switch
        {
            ".jpg" or ".jpeg" or ".jpe" => ".jpg",
            ".png" => ".png",
            ".gif" => ".gif",
            ".webp" => ".webp",
            ".bmp" => ".bmp",
            ".tif" or ".tiff" => ".tiff",
            ".avif" => ".avif",
            ".svg" => ".svg",
            ".ico" => ".ico",
            ".heic" or ".heif" => ".heic",
            _ => null
        };

        if (byName is not null)
        {
            return byName;
        }

        var normalized = (contentType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "image/jpeg" or "image/jpg" or "image/pjpeg" => ".jpg",
            "image/png" or "image/x-png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/bmp" or "image/x-ms-bmp" => ".bmp",
            "image/tiff" => ".tiff",
            "image/avif" => ".avif",
            "image/svg+xml" => ".svg",
            "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
            "image/heic" or "image/heif" => ".heic",
            _ => null
        };
    }

    public static bool IsInlineSafe(string extension) => extension is ".svg";

    private static string? FromSignature(byte[] header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ".jpg";
        }

        if (header.Length >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return ".png";
        }

        if (header.Length >= 6
            && header[0] == 'G' && header[1] == 'I' && header[2] == 'F' && header[3] == '8')
        {
            return ".gif";
        }

        if (header.Length >= 12
            && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
            && header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P')
        {
            return ".webp";
        }

        if (header.Length >= 2 && header[0] == 'B' && header[1] == 'M')
        {
            return ".bmp";
        }

        if (header.Length >= 4
            && header[0] == 'I' && header[1] == 'I' && header[2] == 0x2A && header[3] == 0x00)
        {
            return ".tiff";
        }

        if (header.Length >= 4
            && header[0] == 'M' && header[1] == 'M' && header[2] == 0x00 && header[3] == 0x2A)
        {
            return ".tiff";
        }

        if (header.Length >= 12
            && header[4] == 'f' && header[5] == 't' && header[6] == 'y' && header[7] == 'p'
            && header[8] == 'a' && header[9] == 'v' && header[10] == 'i' && header[11] == 'f')
        {
            return ".avif";
        }

        if (header.Length >= 4 && header[0] == 0x00 && header[1] == 0x00 && header[2] == 0x01 && header[3] == 0x00)
        {
            return ".ico";
        }

        if (header.Length >= 4
            && header[0] == 0x66 && header[1] == 0x74 && header[2] == 0x79 && header[3] == 0x70)
        {
            return ".heic";
        }

        var text = System.Text.Encoding.UTF8.GetString(header, 0, Math.Min(header.Length, 200)).TrimStart();
        if (text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
            || (text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
                && text.Contains("<svg", StringComparison.OrdinalIgnoreCase)))
        {
            return ".svg";
        }

        return null;
    }

    public static string Describe() =>
        "JPG, PNG, GIF, WEBP, BMP, TIFF, AVIF, HEIC or SVG";
}
