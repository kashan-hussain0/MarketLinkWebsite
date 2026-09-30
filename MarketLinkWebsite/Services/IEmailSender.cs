namespace MarketLinkWebsite.Services;

public interface IEmailSender
{
    /// <summary>
    /// True when a mail server is actually configured. Callers use this to keep
    /// quiet about delivery instead of pretending a message went out.
    /// </summary>
    bool IsConfigured { get; }

    Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default);
}
