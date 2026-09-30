using System.Security.Cryptography;
using System.Text;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Services;

public enum ResetCodeOutcome
{
    Accepted,
    NoCodeFound,
    Expired,
    AlreadyUsed,
    TooManyAttempts,
    WrongCode
}

/// <summary>
/// Issues and checks the six digit code a customer types back in after asking to
/// reset a forgotten password.
///
/// The code itself is never stored. Only a SHA-256 hash of it goes in the table, so
/// a copy of the database still cannot be turned into a working login. A code is
/// good for ten minutes, dies after five wrong guesses, and cannot be redeemed
/// twice. Asking for a new code retires the previous one straight away.
/// </summary>
public sealed class PasswordResetCodeService
{
    public const int CodeLength = 6;
    public const string CodeLengthError = "Enter the 6 digit code from the email.";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public const int MaxAttempts = 5;
    private static readonly TimeSpan ResendGap = TimeSpan.FromSeconds(60);

    private readonly ApplicationDbContext db;
    private readonly IEmailSender emailSender;
    private readonly IConfiguration configuration;
    private readonly IHostEnvironment environment;
    private readonly ILogger<PasswordResetCodeService> logger;

    public PasswordResetCodeService(
        ApplicationDbContext db,
        IEmailSender emailSender,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<PasswordResetCodeService> logger)
    {
        this.db = db;
        this.emailSender = emailSender;
        this.configuration = configuration;
        this.environment = environment;
        this.logger = logger;
    }

    /// <summary>
    /// A phrase the developer sets in configuration so the reset can be walked
    /// through without a mail server. It is only honoured while running in
    /// Development, so a build that reaches a real server cannot be unlocked with
    /// it even if the setting were copied by accident.
    /// </summary>
    public string? DevelopmentCode =>
        environment.IsDevelopment() ? ConfigureDevelopmentCode() : null;

    /// <summary>How many characters a code may have: six digits, or a set phrase.</summary>
    public int MaxCodeLength => DevelopmentCode is null ? CodeLength : Math.Max(CodeLength, DevelopmentCode.Length + 8);

    /// <summary>True when the typed text is the configured development phrase.</summary>
    public bool IsDevelopmentCode(string? typed) =>
        DevelopmentCode is not null
        && string.Equals(Normalise(typed), DevelopmentCode, StringComparison.OrdinalIgnoreCase);

    private string? ConfigureDevelopmentCode()
    {
        var configured = configuration["Security:DevelopmentResetCode"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        return Normalise(configured);
    }

    /// <summary>
    /// Mails a fresh code to <paramref name="user"/>. Older codes for the same
    /// account are consumed first so only the newest one can work.
    /// </summary>
    public async Task<ResetCodeOutcome> SendAsync(ApplicationUser user, string? requestedFromIp, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var outstanding = await db.PasswordResetCodes
            .Where(code => code.UserId == user.Id && code.ConsumedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var stale in outstanding)
        {
            stale.ConsumedAt = now;
        }

        // Two codes a minute is already generous, and the cap stops someone using
        // the form to flood a mailbox.
        if (outstanding.Count > 0 && now - outstanding.Max(code => code.CreatedAt) < ResendGap)
        {
            await db.SaveChangesAsync(cancellationToken);
            return ResetCodeOutcome.TooManyAttempts;
        }

        var code = GenerateCode();
        var platform = configuration["Platform:Name"] ?? "MarketLink";

        db.PasswordResetCodes.Add(new PasswordResetCode
        {
            UserId = user.Id,
            CodeHash = Hash(code),
            CreatedAt = now,
            ExpiresAt = now.Add(Lifetime),
            RequestedFromIp = requestedFromIp
        });

        await db.SaveChangesAsync(cancellationToken);

        var minutes = (int)Lifetime.TotalMinutes;
        var body = new StringBuilder()
            .AppendLine($"Hello {user.FirstName},")
            .AppendLine()
            .AppendLine($"Your {platform} verification code is:")
            .AppendLine()
            .AppendLine($"    {code}")
            .AppendLine()
            .AppendLine($"The code is good for {minutes} minutes and can only be used once.")
            .AppendLine("If you did not ask to reset your password you can ignore this message.")
            .ToString();

        await emailSender.SendAsync(user.Email!, $"{code} is your {platform} verification code", body, cancellationToken);

        // Without a mail server the code would be unreachable, so in development it
        // is written to the log instead. Nothing is shown on the page itself.
        if (!emailSender.IsConfigured)
        {
            logger.LogWarning(
                "Email is not configured, so the reset code for {UserId} could not be delivered. Code on file: {Code}",
                user.Id,
                code);
        }

        return ResetCodeOutcome.Accepted;
    }

    /// <summary>
    /// Checks a typed code. A wrong guess is counted, and once the allowance is
    /// gone the code is burnt so brute force cannot simply keep trying.
    /// </summary>
    public async Task<ResetCodeOutcome> CheckAsync(string userId, string? code, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // The set phrase opens the door on its own, so the flow can be demonstrated
        // on a machine with no mail server attached.
        if (IsDevelopmentCode(code))
        {
            return ResetCodeOutcome.Accepted;
        }

        var row = await db.PasswordResetCodes
            .Where(item => item.UserId == userId && item.ConsumedAt == null)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return ResetCodeOutcome.NoCodeFound;
        }

        if (row.ExpiresAt <= now)
        {
            return ResetCodeOutcome.Expired;
        }

        if (row.AttemptCount >= MaxAttempts)
        {
            row.ConsumedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return ResetCodeOutcome.TooManyAttempts;
        }

        if (FixedTimeEquals(row.CodeHash, Hash(Normalise(code))))
        {
            return ResetCodeOutcome.Accepted;
        }

        row.AttemptCount++;
        if (row.AttemptCount >= MaxAttempts)
        {
            row.ConsumedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return ResetCodeOutcome.WrongCode;
    }

    /// <summary>Retires the code once the new password has been saved.</summary>
    public async Task ConsumeAsync(string userId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var rows = await db.PasswordResetCodes
            .Where(code => code.UserId == userId && code.ConsumedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            row.ConsumedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Clears codes and expired rows. Runs on a timer so the table does not grow
    /// without bound.
    /// </summary>
    public async Task<int> PurgeAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(-1);
        var expired = await db.PasswordResetCodes
            .Where(code => code.ExpiresAt < cutoff)
            .ToListAsync(cancellationToken);

        db.PasswordResetCodes.RemoveRange(expired);
        await db.SaveChangesAsync(cancellationToken);
        return expired.Count;
    }

    private static string GenerateCode()
    {
        // RandomNumberGenerator over the full int range then modulo would bias the
        // result towards the low digits, so the digits are taken one at a time.
        var digits = new char[CodeLength];
        for (var index = 0; index < CodeLength; index++)
        {
            digits[index] = (char)('0' + RandomNumberGenerator.GetInt32(10));
        }

        return new string(digits);
    }

    private static string Normalise(string? code) =>
        // Phrases are typed with stray spacing, so runs of whitespace are squeezed
        // to a single space before anything is compared.
        string.Join(' ', (code ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Hash(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexString(bytes);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(left),
            Encoding.ASCII.GetBytes(right));
    }
}

public sealed class PasswordResetCodeCleanupService : BackgroundService
{
    private readonly IServiceProvider services;
    private readonly ILogger<PasswordResetCodeCleanupService> logger;

    public PasswordResetCodeCleanupService(IServiceProvider services, ILogger<PasswordResetCodeCleanupService> logger)
    {
        this.services = services;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<PasswordResetCodeService>();
                var removed = await service.PurgeAsync(stoppingToken);

                if (removed > 0)
                {
                    logger.LogInformation("Removed {Count} expired password reset codes.", removed);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Password reset code cleanup failed.");
            }
        }
    }
}
