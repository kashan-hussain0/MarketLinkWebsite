using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MarketLinkWebsite.Services;

public sealed class AiAssistantService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext db;
    private readonly IConfiguration configuration;
    private readonly HttpClient httpClient;
    private readonly ILogger<AiAssistantService> logger;

    public AiAssistantService(
        ApplicationDbContext db,
        IConfiguration configuration,
        HttpClient httpClient,
        ILogger<AiAssistantService> logger)
    {
        this.db = db;
        this.configuration = configuration;
        this.httpClient = httpClient;
        this.logger = logger;
    }

    public bool HostedModelEnabled =>
        string.Equals(configuration["Ai:Provider"], "gemini", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(configuration["Ai:ApiKey"]);

    public string ModeLabel => HostedModelEnabled ? "Smart assistant" : "Local catalogue";

    public async Task<AssistantAnswer> AnswerAsync(string question, CancellationToken cancellationToken = default)
    {
        var text = question?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return AssistantAnswer.Local("Tell me what you are looking for. Try \"what is open on Saturday\", \"organic apples\" or \"how do I pay\".");
        }

        if (HostedModelEnabled)
        {
            try
            {
                var reply = await AskHostedModelAsync(text, cancellationToken);
                if (!string.IsNullOrWhiteSpace(reply))
                {
                    return AssistantAnswer.Hosted(reply);
                }

                logger.LogWarning("The hosted assistant returned nothing usable, falling back to the local catalogue.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Hosted assistant call failed, falling back to the local catalogue.");
            }
        }

        return AssistantAnswer.Local(await LocalAnswerAsync(text, cancellationToken));
    }

    private IReadOnlyList<string> ResolveModels()
    {
        string[] configured = (configuration["Ai:Models"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (configured.Length == 0)
        {
            var single = (configuration["Ai:Model"] ?? string.Empty).Trim();
            if (single.Length > 0)
            {
                configured = new[] { single };
            }
        }

        if (configured.Length == 0)
        {
            configured = new[] { "gemini-3.8-flash", "gemini-3.7-flash", "gemini-3.6-flash" };
        }

        return configured;
    }

    private async Task<string> AskHostedModelAsync(string question, CancellationToken cancellationToken)
    {
        var context = await BuildContextAsync(cancellationToken);
        var models = ResolveModels();
        var totalBudget = TimeSpan.FromSeconds(ReadBudget("Ai:TotalTimeoutSeconds", 20));
        var attemptBudget = TimeSpan.FromSeconds(ReadBudget("Ai:AttemptTimeoutSeconds", 10));

        using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overall.CancelAfter(totalBudget);

        // Google answers most calls quickly but returns 503 "high demand" often
        // enough that a single shot is a coin toss. Two rounds of retries across
        // the model list make a served answer the likely outcome.
        for (var round = 1; round <= 2 && !overall.IsCancellationRequested; round++)
        {
            var attempts = models.Select(model => Attempt(model, context, question, attemptBudget, overall.Token)).ToArray();
            var answers = await Task.WhenAll(attempts);

            foreach (var answer in answers)
            {
                if (!string.IsNullOrWhiteSpace(answer))
                {
                    return answer;
                }
            }

            if (round < 2)
            {
                logger.LogInformation("No model answered on round {Round}. Trying again.", round);
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(600), overall.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        return string.Empty;
    }

    private async Task<string> Attempt(string model, string context, string question, TimeSpan budget, CancellationToken cancellationToken)
    {
        try
        {
            using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attempt.CancelAfter(budget);
            return await AskModelAsync(model, context, question, attempt.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Model {Model} did not answer within {Seconds} seconds.", model, budget.TotalSeconds);
            return string.Empty;
        }
        catch (Exception exception)
        {
            logger.LogInformation(exception, "Model {Model} could not be reached.", model);
            return string.Empty;
        }
    }

    private int ReadBudget(string key, int fallback) =>
        int.TryParse(configuration[key], out var seconds) && seconds > 0 ? Math.Min(seconds, 60) : fallback;

    private async Task<string> AskModelAsync(string model, string context, string question, CancellationToken cancellationToken)
    {
        var apiKey = configuration["Ai:ApiKey"]!.Trim();
        var endpoint = (configuration["Ai:Endpoint"] ?? "https://generativelanguage.googleapis.com/v1beta/models").TrimEnd('/');

        var payload = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = SystemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = $"Live MarketLink data:\n{context}\n\nShopper question: {question}" } }
                }
            },
            generationConfig = new
            {
                temperature = 0.2,
                maxOutputTokens = 1024
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}");
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;

            // 429 is a quota pause and 503 is Google's own "busy right now". Both
            // clear on their own, so the detail is logged and the caller retries.
            if (status is 429 or 500 or 502 or 503 or 504)
            {
                logger.LogInformation("Model {Model} is temporarily unavailable ({Status}).", model, status);
            }
            else
            {
                logger.LogWarning("Model {Model} returned {Status}.", model, status);
            }

            return string.Empty;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("candidates", out var candidates)
            || candidates.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var first = candidates[0];
        if (!first.TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts)
            || parts.GetArrayLength() == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var piece) && piece.GetString() is { Length: > 0 } text)
            {
                builder.Append(text);
            }
        }

        return builder.ToString().Trim();
    }

    private const string SystemPrompt = "You are the MarketLink market assistant for a farmers market pre-order website. "
        + "Answer only from the live data supplied in the user message. Never invent product, timings, prices or farmers. "
        + "If the data does not cover the question, say so plainly and point to the catalogue. "
        + "Payment is always made at the pickup stall, never online. There is no delivery, only pickup. "
        + "Formatting rules, follow these exactly: reply in plain text only. "
        + "Never use markdown. No asterisks, no hash signs, no bold or italic markers, no code blocks, no links. "
        + "When you need to list more than one thing, put each one on its own line starting with a hyphen and a space. "
        + "Keep the whole reply under 80 words and do not repeat the question back.";

    private async Task<string> BuildContextAsync(CancellationToken cancellationToken)
    {
        var markets = await db.Markets
            .AsNoTracking()
            .Where(market => market.IsActive)
            .OrderBy(market => market.Name)
            .Select(market => new
            {
                market.Name,
                market.OperatingDays,
                market.Address,
                market.City,
                Latitude = (double)market.Latitude,
                Longitude = (double)market.Longitude
            })
            .ToListAsync(cancellationToken);

        var farms = await db.FarmerProfiles
            .AsNoTracking()
            .Where(profile => profile.Status == FarmerStatus.Active && profile.User.IsActive)
            .OrderByDescending(profile => profile.Rating)
            .Select(profile => new
            {
                profile.FarmName,
                profile.City,
                profile.Rating,
                profile.ReviewCount,
                profile.OperatingDays,
                profile.PickupWindows,
                Live = profile.Products.Count(product => product.IsAvailable)
            })
            .ToListAsync(cancellationToken);

        var product = await db.Products
            .AsNoTracking()
            .Where(product => product.IsAvailable
                && product.Category.IsActive
                && product.FarmerProfile.Status == FarmerStatus.Active
                && product.FarmerProfile.User.IsActive)
            .OrderBy(product => product.Category.Name)
            .ThenBy(product => product.Name)
            .Select(product => new
            {
                product.Name,
                product.Price,
                product.IsOrganic,
                Category = product.Category.Name,
                Farm = product.FarmerProfile.FarmName,
                Available = product.Inventory != null ? product.Inventory.QuantityAvailable : 0,
                ProductId = product.Id
            })
            .ToListAsync(cancellationToken);

        var builder = new StringBuilder();
        builder.AppendLine("Markets:");
        foreach (var market in markets)
        {
            builder.AppendLine($"- {market.Name}, {market.Address}, {market.City}. Open {market.OperatingDays}. Map {market.Latitude:0.0000},{market.Longitude:0.0000}");
        }

        builder.AppendLine();
        builder.AppendLine("Farms:");
        foreach (var farm in farms)
        {
            builder.AppendLine($"- {farm.FarmName} in {farm.City}. Rated {farm.Rating:0.0} from {farm.ReviewCount} reviews. Open {farm.OperatingDays}. Pickup {farm.PickupWindows}. {farm.Live} live listings.");
        }

        builder.AppendLine();
        builder.AppendLine("Product:");
        foreach (var item in product)
        {
            builder.AppendLine($"- {item.Name} ({item.Category}) from {item.Farm}: {item.Price:0.00} each, {item.Available} available{(item.IsOrganic ? ", organic" : string.Empty)}. Page /Product/Details/{item.ProductId}");
        }

        return builder.ToString();
    }

    private async Task<string> LocalAnswerAsync(string term, CancellationToken cancellationToken)
    {
        var service = new AssistantService(db);
        var replies = await service.AnswerAsync(term, cancellationToken);
        return string.Join(" ", replies.Select(reply => reply.Message));
    }
}

public sealed class AssistantAnswer
{
    public string Message { get; private init; } = string.Empty;
    public bool FromHostedModel { get; private init; }

    public static AssistantAnswer Hosted(string message) => new() { Message = message, FromHostedModel = true };

    public static AssistantAnswer Local(string message) => new() { Message = message, FromHostedModel = false };
}
