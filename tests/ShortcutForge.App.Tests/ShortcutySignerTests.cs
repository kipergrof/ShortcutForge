using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShortcutForge.Core.Plist;
using ShortcutForge.Signing;

namespace ShortcutForge.App.Tests;

public class ShortcutySignerTests
{
    private sealed class FakeHandler(Func<int, HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return await respond(++Calls, request);
        }
    }

    private static readonly byte[] Unsigned = PlistSerializer.WriteBinary(Dsl.DslParser.Parse("x = Text(\"a\")\nShowResult(x)"));
    private static readonly byte[] Signed = Encoding.ASCII.GetBytes("AEA1 fake signed shortcut");

    private static string Hex(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Success(byte[]? signed = null, byte[]? input = null) => Json(HttpStatusCode.OK, new
    {
        success = true,
        file = new
        {
            filename = "x — Signed.shortcut",
            content_base64 = Convert.ToBase64String(signed ?? Signed),
            format = "AEA1",
            input_sha256 = Hex(input ?? Unsigned),
            signed_sha256 = Hex(signed ?? Signed),
        },
    });

    private static HttpResponseMessage Error(HttpStatusCode status, string code) =>
        Json(status, new { success = false, error = new { code, message = "details" } });

    [Fact]
    public async Task Uploads_multipart_file_and_returns_verified_signed_file()
    {
        string? body = null;
        var handler = new FakeHandler(async (_, request) =>
        {
            body = await request.Content!.ReadAsStringAsync();
            return Success();
        });

        var result = await new ShortcutySigner(new HttpClient(handler)).SignAsync(Unsigned, SigningMode.Anyone, "Próba");

        Assert.Equal(Signed, result);
        Assert.Equal("https://sign.shortcuty.app/api/v1/sign?response=json", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("multipart/form-data", handler.LastRequest.Content!.Headers.ContentType!.MediaType);
        Assert.Contains("name=file", body);
        Assert.Contains(".shortcut", body);
        Assert.Contains("ShortcutForge", handler.LastRequest.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Rejects_checksum_mismatch()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Success(input: [1, 2, 3])));
        var ex = await Assert.ThrowsAsync<SigningException>(() =>
            new ShortcutySigner(new HttpClient(handler)).SignAsync(Unsigned, SigningMode.Anyone, "x"));
        Assert.Contains("más fájlt", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "invalid_workflow", "nem érvényes")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "already_signed", "már alá van írva")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "signer_unavailable", "nem elérhető")]
    public async Task Reports_api_errors(HttpStatusCode status, string code, string expected)
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(Error(status, code)));
        var ex = await Assert.ThrowsAsync<SigningException>(() =>
            new ShortcutySigner(new HttpClient(handler)).SignAsync(Unsigned, SigningMode.Anyone, "x"));
        Assert.Contains(expected, ex.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Retries_once_when_the_queue_is_full()
    {
        var handler = new FakeHandler((call, _) => Task.FromResult(call == 1
            ? Error(HttpStatusCode.ServiceUnavailable, "signing_queue_full")
            : Success()));
        var signer = new ShortcutySigner(new HttpClient(handler)) { RetryDelay = TimeSpan.Zero };

        Assert.Equal(Signed, await signer.SignAsync(Unsigned, SigningMode.Anyone, "x"));
        Assert.Equal(2, handler.Calls);
    }
}
