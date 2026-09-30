using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;

namespace MarketLinkWebsite.Services;

/// <summary>
/// Sends mail through whatever SMTP server the host is configured with. Delivery
/// problems are logged and swallowed on purpose: a shopper placing a pre-order
/// must not lose their basket because a mail server is having a bad day.
/// </summary>
public sealed class EmailSender : IEmailSender
{
    private const int MaxBodyLength = 20000;

    private readonly IConfiguration configuration;
    private readonly IHostEnvironment environment;
    private readonly ILogger<EmailSender> logger;

    public EmailSender(
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<EmailSender> logger)
    {
        this.configuration = configuration;
        this.environment = environment;
        this.logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["Email:SmtpHost"]);

    public async Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipient))
        {
            logger.LogWarning("An email was skipped because it had no recipient.");
            return;
        }

        if (!IsConfigured)
        {
            if (environment.IsDevelopment())
            {
                // Writing the body to the log keeps the feature testable on a
                // machine with no mail server attached.
                logger.LogInformation(
                    "Email is not configured, so this was not delivered to {Recipient}. Subject: {Subject} Body: {Body}",
                    recipient,
                    subject,
                    body);
            }
            else
            {
                logger.LogError("Email delivery is not configured, so {Recipient} was not notified.", recipient);
            }

            return;
        }

        var message = BuildMessage(recipient, subject, body);
        if (message is null)
        {
            return;
        }

        try
        {
            using var client = BuildClient();
            using (message)
            {
                await client.SendMailAsync(message, cancellationToken);
            }

            logger.LogInformation("Sent \"{Subject}\" to {Recipient}.", subject, recipient);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A bad address, a refused login and a full mailbox all land here. None
            // of them should surface to the person waiting on the page.
            logger.LogError(exception, "Could not send \"{Subject}\" to {Recipient}.", subject, recipient);
        }
    }

    private MailMessage? BuildMessage(string recipient, string subject, string body)
    {
        var fromAddress = configuration["Email:FromAddress"];
        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            logger.LogError("Email:FromAddress is not set, so no message was built for {Recipient}.", recipient);
            return null;
        }

        try
        {
            // Subjects and addresses come from user input in places, so a stray line
            // break is stripped before the message is built.
            var safeSubject = Flatten(subject, 200);
            var safeBody = body.Length > MaxBodyLength ? body[..MaxBodyLength] : body;

            var message = new MailMessage
            {
                From = new MailAddress(fromAddress),
                Subject = safeSubject,
                Body = safeBody,
                IsBodyHtml = false
            };

            message.To.Add(recipient);
            return message;
        }
        catch (FormatException exception)
        {
            logger.LogError(exception, "The from address is not a valid email, so nothing was sent to {Recipient}.", recipient);
            return null;
        }
        catch (ArgumentException exception)
        {
            logger.LogError(exception, "The recipient address was rejected, so nothing was sent.");
            return null;
        }
    }

    private SmtpClient BuildClient()
    {
        var host = configuration["Email:SmtpHost"]!;
        var port = configuration.GetValue<int?>("Email:SmtpPort") ?? 587;
        var useSsl = configuration.GetValue("Email:UseSsl", true);
        var username = configuration["Email:SmtpUsername"];

        var client = new SmtpClient(host, port)
        {
            EnableSsl = useSsl
        };

        if (!string.IsNullOrWhiteSpace(username))
        {
            client.UseDefaultCredentials = false;
            client.Credentials = new NetworkCredential(username, configuration["Email:SmtpPassword"]);
        }

        return client;
    }

    private static string Flatten(string value, int maxLength)
    {
        var cleaned = value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return cleaned.Length > maxLength ? cleaned[..maxLength] : cleaned;
    }
}
