using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.Signing;

/// <summary>
/// Free signing via the public Shortcuty signing API
/// (https://github.com/Shortcuty/Signing-Server-API-Documentation): no API key,
/// <c>POST /api/v1/sign?response=json</c> with a multipart "file" part. Signs in "anyone" mode.
/// The shortcut's content is sent to Shortcuty.
/// </summary>
public sealed class ShortcutySigner(HttpClient? httpClient = null, string baseUrl = ShortcutySigner.DefaultBaseUrl) : ISigner
{
    public const string DefaultBaseUrl = "https://sign.shortcuty.app";

    /// <summary>Delay before the single retry when the signing queue is full.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(3);

    public string DisplayName => L.T("Ingyenes online aláírás (Shortcuty)", "Free online signing (Shortcuty)");

    public bool ProducesSignedFile => true;

    public async Task<byte[]> SignAsync(byte[] unsignedPlist, SigningMode mode, string shortcutName, CancellationToken cancellationToken = default)
    {
        var client = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                var (status, body) = await SendAsync(client, unsignedPlist, shortcutName, cancellationToken);
                if (status == HttpStatusCode.OK) return ReadSignedFile(body, unsignedPlist);

                var (code, message) = ReadError(body);
                if (status == HttpStatusCode.ServiceUnavailable && code == "signing_queue_full" && attempt == 1)
                {
                    await Task.Delay(RetryDelay, cancellationToken);
                    continue;
                }
                throw new SigningException(DescribeError((int)status, code, message));
            }
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SigningException(L.T("A Shortcuty aláíró nem válaszolt időben. Próbáld újra később.",
                "The Shortcuty signer did not respond in time. Try again later."), ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SigningException(L.T($"Nem sikerült elérni a Shortcuty aláírót: {ex.Message}",
                $"Could not reach the Shortcuty signer: {ex.Message}"), ex);
        }
        finally
        {
            if (httpClient is null) client.Dispose();
        }
    }

    private async Task<(HttpStatusCode Status, string Body)> SendAsync(HttpClient client, byte[] unsignedPlist, string shortcutName, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(unsignedPlist);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", FileName(shortcutName));

        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/v1/sign?response=json") { Content = form };
        request.Headers.UserAgent.ParseAdd(ShortcutForge.Core.AppInfo.UserAgent);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await client.SendAsync(request, cancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static string FileName(string shortcutName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string((string.IsNullOrWhiteSpace(shortcutName) ? "Shortcut" : shortcutName)
            .Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return name + ".shortcut";
    }

    /// <summary>Decodes the JSON response and checks both SHA-256 hashes it reports.</summary>
    private static byte[] ReadSignedFile(string body, byte[] unsignedPlist)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var file = json.RootElement.GetProperty("file");
            var signed = Convert.FromBase64String(file.GetProperty("content_base64").GetString() ?? "");

            if (file.TryGetProperty("signed_sha256", out var signedHash) &&
                !Hex(SHA256.HashData(signed)).Equals(signedHash.GetString(), StringComparison.OrdinalIgnoreCase))
                throw new SigningException(L.T("A letöltött aláírt fájl sérült (ellenőrzőösszeg eltér).",
                    "The downloaded signed file is corrupt (checksum mismatch)."));
            if (file.TryGetProperty("input_sha256", out var inputHash) &&
                !Hex(SHA256.HashData(unsignedPlist)).Equals(inputHash.GetString(), StringComparison.OrdinalIgnoreCase))
                throw new SigningException(L.T("A szerver más fájlt írt alá, mint amit küldtünk.",
                    "The server signed a different file than the one sent."));
            if (!SignedFile.IsSigned(signed))
                throw new SigningException(L.T("A Shortcuty válasza nem aláírt shortcut.", "The Shortcuty response is not a signed shortcut."));
            return signed;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            throw new SigningException(L.T("A Shortcuty válasza nem értelmezhető.", "The Shortcuty response could not be read."), ex);
        }
    }

    private static string Hex(byte[] hash) => Convert.ToHexString(hash);

    private static (string? Code, string? Message) ReadError(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.TryGetProperty("error", out var error))
                return (error.TryGetProperty("code", out var c) ? c.GetString() : null,
                        error.TryGetProperty("message", out var m) ? m.GetString() : null);
        }
        catch (JsonException)
        {
            // not JSON (e.g. a proxy error page)
        }
        return (null, null);
    }

    private static string DescribeError(int status, string? code, string? message)
    {
        var text = code switch
        {
            "invalid_workflow" => L.T("A Shortcuty szerint a fájl nem érvényes parancs.", "Shortcuty says the file is not a valid shortcut."),
            "already_signed" => L.T("A fájl már alá van írva.", "The file is already signed."),
            "signer_unavailable" => L.T("A Shortcuty aláíró most nem elérhető. Próbáld újra később.", "The Shortcuty signer is unavailable right now. Try again later."),
            "signing_queue_full" => L.T("A Shortcuty aláíró túlterhelt. Próbáld újra pár másodperc múlva.", "The Shortcuty signer is busy. Try again in a few seconds."),
            "missing_file" or "invalid_response_format" => L.T("Hibás kérés a Shortcuty felé.", "Invalid request to Shortcuty."),
            _ => L.T($"A Shortcuty aláíró hibát adott (HTTP {status}).", $"The Shortcuty signer returned an error (HTTP {status})."),
        };
        return message is null ? text : $"{text}\n({message})";
    }
}
