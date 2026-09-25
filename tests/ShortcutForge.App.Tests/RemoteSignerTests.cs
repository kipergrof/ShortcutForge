using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ShortcutForge.Core.Plist;
using ShortcutForge.Signing;

namespace ShortcutForge.App.Tests;

public class RemoteSignerTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return respond(request, LastBody);
        }
    }

    private static byte[] UnsignedSample() =>
        PlistSerializer.WriteBinary(Dsl.DslParser.Parse("x = Text(\"a\")\nShowResult(x)"));

    [Fact]
    public async Task Sends_name_and_xml_plist_and_returns_signed_file()
    {
        var signed = Encoding.ASCII.GetBytes("AEA1-signed-bytes");
        var handler = new FakeHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(signed) { Headers = { ContentType = new("application/octet-stream") } },
        });
        var signer = new RemoteSigner("Test", "https://sign.example/sign", new HttpClient(handler));

        var result = await signer.SignAsync(UnsignedSample(), SigningMode.Anyone, "Próba");

        Assert.Equal(signed, result);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Contains("ShortcutForge", handler.LastRequest.Headers.UserAgent.ToString());
        using var json = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("Próba", json.RootElement.GetProperty("shortcutName").GetString());
        var xml = json.RootElement.GetProperty("shortcut").GetString()!;
        Assert.Contains("<plist", xml);
        Assert.Contains("is.workflow.actions.showresult", xml);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "text/html", "<!DOCTYPE html><title>Just a moment...</title>", "elutasította")]
    [InlineData(HttpStatusCode.OK, "application/octet-stream", "not signed", "nem aláírt")]
    [InlineData(HttpStatusCode.InternalServerError, "text/plain", "boom", "HTTP 500")]
    public async Task Reports_failures(HttpStatusCode status, string contentType, string body, string expected)
    {
        var handler = new FakeHandler((_, _) => new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType),
        });
        var signer = new RemoteSigner("Test", "https://sign.example/sign", new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<SigningException>(() => signer.SignAsync(UnsignedSample(), SigningMode.Anyone, "x"));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Rejects_invalid_url()
    {
        var signer = new RemoteSigner("Test", "nem url");
        await Assert.ThrowsAsync<SigningException>(() => signer.SignAsync(UnsignedSample(), SigningMode.Anyone, "x"));
    }
}
