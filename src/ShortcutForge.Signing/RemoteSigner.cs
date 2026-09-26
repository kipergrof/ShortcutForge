using System.Net.Http.Json;
using Claunia.PropertyList;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.Signing;

/// <summary>
/// Signs shortcuts with a remote signing server implementing the API of
/// https://github.com/scaxyz/shortcut-signing-server (runs on a Mac; can be self-hosted).
/// The request is <c>POST {"shortcutName": …, "shortcut": "&lt;XML plist&gt;"}</c>; the response is the signed file.
/// The shortcut's content is sent to that server. The client identifies itself honestly as ShortcutForge.
/// </summary>
public sealed class RemoteSigner(string displayName, string url, HttpClient? httpClient = null) : ISigner
{
    private static readonly string[] AcceptedContentTypes =
        ["application/octet-stream", "application/x-plist", "application/x-apple-shortcut"];

    public string DisplayName { get; } = displayName;

    public string Url { get; } = url;

    public bool ProducesSignedFile => true;

    public async Task<byte[]> SignAsync(byte[] unsignedPlist, SigningMode mode, string shortcutName, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new SigningException(L.T($"Érvénytelen aláíró szerver cím: {Url}", $"Invalid signing server address: {Url}"));

        // The service expects the XML form of the plist.
        var xml = PropertyListParser.Parse(unsignedPlist).ToXmlPropertyList();
        var client = httpClient ?? new HttpClient();
        try
        {
            client.Timeout = httpClient is null ? TimeSpan.FromSeconds(60) : client.Timeout;
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = JsonContent.Create(new { shortcutName = string.IsNullOrWhiteSpace(shortcutName) ? "Shortcut" : shortcutName, shortcut = xml }),
            };
            request.Headers.UserAgent.ParseAdd(ShortcutForge.Core.AppInfo.UserAgent);

            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var isHtml = response.Content.Headers.ContentType?.MediaType == "text/html";
            if ((int)response.StatusCode is 403 or 302 or 401 || (isHtml && !SignedFile.IsSigned(body)))
                throw new SigningException(L.T(
                    $"Az aláíró szolgáltatás elutasította a kérést (HTTP {(int)response.StatusCode}). Lehet, hogy csak engedélyezett klienseket fogad, vagy bejelentkezést kér.",
                    $"The signing service refused the request (HTTP {(int)response.StatusCode}). It may only accept approved clients or require a login."));
            if (!response.IsSuccessStatusCode)
            {
                var detail = System.Text.Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 300)).Trim();
                throw new SigningException(L.T(
                    $"Az aláíró szolgáltatás hibát adott (HTTP {(int)response.StatusCode}). {detail}",
                    $"The signing service returned an error (HTTP {(int)response.StatusCode}). {detail}"));
            }

            var contentType = response.Content.Headers.ContentType?.MediaType;
            if (!SignedFile.IsSigned(body))
                throw new SigningException(L.T(
                    $"Az aláíró szolgáltatás válasza nem aláírt shortcut ({contentType ?? "ismeretlen típus"}).",
                    $"The signing service's response is not a signed shortcut ({contentType ?? "unknown type"})."));
            if (contentType is not null && !AcceptedContentTypes.Contains(contentType))
                throw new SigningException(L.T($"Váratlan választípus: {contentType}", $"Unexpected response type: {contentType}"));
            return body;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SigningException(L.T("Az aláíró szolgáltatás nem válaszolt időben. Próbáld újra később.",
                "The signing service did not respond in time. Try again later."), ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SigningException(L.T($"Nem sikerült elérni az aláíró szolgáltatást: {ex.Message}",
                $"Could not reach the signing service: {ex.Message}"), ex);
        }
        finally
        {
            if (httpClient is null) client.Dispose();
        }
    }
}
