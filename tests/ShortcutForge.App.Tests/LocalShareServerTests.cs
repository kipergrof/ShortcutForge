using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using ShortcutForge.App.Services;

namespace ShortcutForge.App.Tests;

public class LocalShareServerTests
{
    private static readonly byte[] Content = "AEA1 signed shortcut bytes"u8.ToArray();

    [Fact]
    public async Task Serves_the_file_only_at_its_random_path()
    {
        using var server = LocalShareServer.Start(Content, "Reggeli parancs.shortcut");
        using var http = new HttpClient();
        var url = server.UrlFor(IPAddress.Loopback);

        var downloaded = new TaskCompletionSource();
        server.Downloaded += (_, _) => downloaded.TrySetResult();

        using var ok = await http.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(Content, await ok.Content.ReadAsByteArrayAsync());
        Assert.Equal("attachment", ok.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("Reggeli parancs.shortcut", ok.Content.Headers.ContentDisposition.FileNameStar);
        await downloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, server.Downloads);

        // Anything else is not served.
        using var root = await http.GetAsync($"http://127.0.0.1:{server.Port}/");
        Assert.Equal(HttpStatusCode.NotFound, root.StatusCode);
        using var wrongToken = await http.GetAsync($"http://127.0.0.1:{server.Port}/0000/Reggeli%20parancs.shortcut");
        Assert.Equal(HttpStatusCode.NotFound, wrongToken.StatusCode);
        using var post = await http.PostAsync(url, new StringContent("x"));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);

        // HEAD returns headers without counting a download.
        using var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, url));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(Content.Length, head.Content.Headers.ContentLength);
        Assert.Equal(1, server.Downloads);
    }

    // The QR code points at the landing page. Scanning a link that answers with an octet-stream
    // attachment leaves Safari on a blank page with no download prompt, so the phone needs a real
    // page with a Download button on it.
    [Fact]
    public async Task Landing_page_offers_the_file_and_reports_the_visit()
    {
        using var server = LocalShareServer.Start(Content, "Reggeli parancs.shortcut");
        using var http = new HttpClient();
        var pageUrl = server.PageUrlFor(IPAddress.Loopback);

        var opened = new TaskCompletionSource();
        server.PageOpened += (_, _) => opened.TrySetResult();

        using var page = await http.GetAsync(pageUrl);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType!.MediaType);
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, server.Downloads); // opening the page is not a download

        // The download button must resolve to the file, from the page URL with and without its
        // trailing slash (a relative href would break on the second one).
        var html = await page.Content.ReadAsStringAsync();
        var href = WebUtility.HtmlDecode(html.Split("href=\"")[1].Split('"')[0]);
        foreach (var basePage in new[] { pageUrl, pageUrl.TrimEnd('/') })
        {
            using var file = await http.GetAsync(new Uri(new Uri(basePage), href));
            Assert.Equal(HttpStatusCode.OK, file.StatusCode);
            Assert.Equal(Content, await file.Content.ReadAsByteArrayAsync());
        }

        // The page is served without the trailing slash too, so a trimmed link still works.
        using var noSlash = await http.GetAsync(pageUrl.TrimEnd('/'));
        Assert.Equal(HttpStatusCode.OK, noSlash.StatusCode);
    }

    [Fact]
    public async Task Landing_page_is_behind_the_random_token()
    {
        using var server = LocalShareServer.Start(Content, "x.shortcut");
        using var http = new HttpClient();
        using var wrongToken = await http.GetAsync($"http://127.0.0.1:{server.Port}/0000/");
        Assert.Equal(HttpStatusCode.NotFound, wrongToken.StatusCode);
    }

    [Fact]
    public void Paths_are_unguessable_and_different_per_share()
    {
        using var a = LocalShareServer.Start(Content, "x.shortcut");
        using var b = LocalShareServer.Start(Content, "x.shortcut");
        Assert.NotEqual(a.Path, b.Path);
        Assert.True(a.Path.Split('/')[1].Length >= 24);
    }

    [Fact]
    public async Task Stops_serving_after_dispose()
    {
        var server = LocalShareServer.Start(Content, "x.shortcut");
        var url = server.UrlFor(IPAddress.Loopback);
        server.Dispose();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        await Assert.ThrowsAnyAsync<Exception>(() => http.GetAsync(url));
    }

    [Fact]
    public void Real_home_network_ranks_above_virtual_adapters()
    {
        var wifi = LocalShareServer.RankAddress(true, false, NetworkInterfaceType.Wireless80211, [192, 168, 1, 23]);
        var vmware = LocalShareServer.RankAddress(false, true, NetworkInterfaceType.Ethernet, [192, 168, 56, 1]);
        var docker = LocalShareServer.RankAddress(true, true, NetworkInterfaceType.Ethernet, [172, 17, 0, 1]);
        Assert.True(wifi > vmware);
        Assert.True(wifi > docker);
    }
}
