using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;
using ShortcutForge.Core.Localization;
using ShortcutForge.Dsl;

namespace ShortcutForge.App.Services;

/// <summary>Result of generating a shortcut from a description.</summary>
public sealed record AiGenerationResult(string Code, string? Explanation, bool Valid, string? Error);

public sealed class AiGenerationException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Turns a plain-language description into ShortcutForge DSL with Claude (official Anthropic C# SDK).
/// The large system prompt (language reference + action catalog) is cached; the result is checked
/// with the DSL parser and repaired once automatically if it does not parse.
/// </summary>
public sealed class ShortcutAiService(string apiKey)
{
    public const string ModelId = "claude-opus-5";

    public async Task<AiGenerationResult> GenerateAsync(string description, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new AiGenerationException(L.T("Adj meg egy Claude API-kulcsot az Eszközök › Beállítások menüben.",
                "Enter a Claude API key in Tools › Settings."));

        var client = new AnthropicClient { ApiKey = apiKey };
        var reply = await AskAsync(client, description, cancellationToken);
        var code = AiPrompt.ExtractCode(reply);
        var explanation = ExplanationOf(reply);

        if (TryParse(code, out var error)) return new AiGenerationResult(code, explanation, true, null);

        // One automatic repair round, as a fresh request (no thinking replay needed).
        var repaired = AiPrompt.ExtractCode(await AskAsync(client, AiPrompt.RepairRequest(description, code, error!), cancellationToken));
        return TryParse(repaired, out var secondError)
            ? new AiGenerationResult(repaired, explanation, true, null)
            : new AiGenerationResult(repaired, explanation, false, secondError);
    }

    private static bool TryParse(string code, out string? error)
    {
        try
        {
            DslParser.Parse(code);
            error = null;
            return true;
        }
        catch (DslException ex)
        {
            error = ex.ToString();
            return false;
        }
    }

    private static string? ExplanationOf(string reply)
    {
        var fence = reply.IndexOf("```", StringComparison.Ordinal);
        var text = (fence > 0 ? reply[..fence] : "").Trim();
        return text.Length > 0 ? text : null;
    }

    private static async Task<string> AskAsync(AnthropicClient client, string userMessage, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = ModelId,
                MaxTokens = 16000,
                // Server-side fallback ("default" mode): a refused request is re-served in the same call by a
                // fallback model chosen by the API for the refusal category — no model list to maintain.
                Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
                Fallbacks = new Default(),
                System = new List<BetaTextBlockParam>
                {
                    new() { Text = AiPrompt.SystemPrompt, CacheControl = new BetaCacheControlEphemeral() },
                },
                Messages = [new() { Role = Role.User, Content = userMessage }],
            }, cancellationToken);

            if (response.StopReason == "refusal")
                throw new AiGenerationException(L.T("A modell ezt a kérést nem teljesítette. Fogalmazd át a leírást.",
                    "The model declined this request. Try rephrasing the description."));

            var text = string.Concat(response.Content
                .Select(b => b.TryPickText(out BetaTextBlock? t) ? t.Text : "")).Trim();
            if (text.Length == 0)
                throw new AiGenerationException(L.T("Üres válasz érkezett. Próbáld újra.", "The reply was empty. Try again."));
            return text;
        }
        catch (AnthropicUnauthorizedException ex)
        {
            throw new AiGenerationException(L.T("Érvénytelen API-kulcs. Ellenőrizd az Eszközök › Beállítások menüben.",
                "Invalid API key. Check it in Tools › Settings."), ex);
        }
        catch (AnthropicRateLimitException ex)
        {
            throw new AiGenerationException(L.T("Túl sok kérés. Várj egy kicsit, és próbáld újra.",
                "Too many requests. Wait a moment and try again."), ex);
        }
        catch (Anthropic5xxException ex)
        {
            throw new AiGenerationException(L.T("A Claude szolgáltatás átmenetileg nem elérhető. Próbáld újra később.",
                "The Claude service is temporarily unavailable. Try again later."), ex);
        }
        catch (AnthropicIOException ex)
        {
            throw new AiGenerationException(L.T($"Nem sikerült csatlakozni: {ex.Message}", $"Could not connect: {ex.Message}"), ex);
        }
        catch (AnthropicApiException ex)
        {
            throw new AiGenerationException(L.T($"A Claude API hibát adott: {ex.Message}", $"The Claude API returned an error: {ex.Message}"), ex);
        }
    }
}
