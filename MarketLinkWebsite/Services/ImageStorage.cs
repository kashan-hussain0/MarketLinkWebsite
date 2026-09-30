using System.Security.Cryptography;

namespace MarketLinkWebsite.Services;

public sealed record StoredImage(string FileName, string PublicPath);

public interface IImageStorage
{
    bool TryStore(IFormFile? file, string folder, out StoredImage stored, out string failure);
}

public sealed class ImageStorage : IImageStorage
{
    private const long MaxBytes = 8L * 1024 * 1024;
    private const int MinBytes = 64;

    private static readonly (string Extension, byte[] Magic)[] Formats =
    {
        (".jpg", new byte[] { 0xFF, 0xD8, 0xFF }),
        (".png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        (".gif", new byte[] { 0x47, 0x49, 0x46, 0x38 }),
        (".bmp", new byte[] { 0x42, 0x4D }),
        (".webp", new byte[] { 0x52, 0x49, 0x46, 0x46 })
    };

    private readonly IWebHostEnvironment environment;
    private readonly ILogger<ImageStorage> logger;

    public ImageStorage(IWebHostEnvironment environment, ILogger<ImageStorage> logger)
    {
        this.environment = environment;
        this.logger = logger;
    }

    public bool TryStore(IFormFile? file, string folder, out StoredImage stored, out string failure)
    {
        stored = new StoredImage(string.Empty, string.Empty);
        failure = string.Empty;

        if (file is null || file.Length == 0)
        {
            failure = "No picture was selected.";
            return false;
        }

        if (file.Length > MaxBytes)
        {
            failure = "That picture is larger than 8 MB. Please choose a smaller one.";
            return false;
        }

        if (file.Length < MinBytes)
        {
            failure = "That file is too small to be a picture.";
            return false;
        }

        // The signature is only a dozen bytes long. Streams are allowed to
        // return fewer bytes than were asked for, so keep reading until the
        // buffer is full or the file ends.
        var head = new byte[12];
        var read = 0;
        using (var source = file.OpenReadStream())
        {
            while (read < head.Length)
            {
                var got = source.Read(head, read, head.Length - read);
                if (got <= 0)
                {
                    break;
                }

                read += got;
            }
        }

        if (read < 8)
        {
            failure = "That file could not be read as a picture.";
            return false;
        }

        var extension = Match(head, read);
        if (extension is null)
        {
            failure = "Only JPG, PNG, GIF, BMP and WEBP pictures can be uploaded.";
            return false;
        }

        var name = Guid.NewGuid().ToString("N") + extension;
        var relative = $"/uploads/{folder}/{name}";
        var absolute = Path.Combine(environment.WebRootPath ?? string.Empty, "uploads", folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);

        using (var target = new FileStream(absolute, FileMode.Create, FileAccess.Write))
        {
            using var source = file.OpenReadStream();
            source.CopyTo(target);
        }

        logger.LogInformation("Stored {FileName} for the {Folder} folder", name, folder);
        stored = new StoredImage(name, relative);
        return true;
    }

    private static string? Match(byte[] head, int length)
    {
        foreach (var (extension, magic) in Formats)
        {
            if (length < magic.Length)
            {
                continue;
            }

            var matched = true;
            for (int i = 0; i < magic.Length; i++)
            {
                if (head[i] != magic[i])
                {
                    matched = false;
                    break;
                }
            }

            if (!matched)
            {
                continue;
            }

            if (extension == ".webp")
            {
                var hasWebpTag = length >= 12
                    && head[8] == (byte)'W' && head[9] == (byte)'E' && head[10] == (byte)'B' && head[11] == (byte)'P';
                if (!hasWebpTag)
                {
                    continue;
                }
            }

            return extension;
        }

        return null;
    }
}
