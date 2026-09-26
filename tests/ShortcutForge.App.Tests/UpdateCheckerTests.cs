using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ShortcutForge.App.Services;
using ShortcutForge.App.ViewModels;
using ShortcutForge.Core;

namespace ShortcutForge.App.Tests;

public class UpdateCheckerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sf-upd-" + Guid.NewGuid().ToString("N"));

    public UpdateCheckerTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("SHORTCUTFORGE_SETTINGS_DIR", _dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private sealed class FakeHandler(HttpStatusCode status, object? body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var content = body is string s ? s : JsonSerializer.Serialize(body);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") });
        }
    }

    private static object Release(string tag, bool prerelease = false) => new
    {
        tag_name = tag,
        html_url = $"https://github.com/kipergrof/ShortcutForge/releases/tag/{tag}",
        draft = false,
        prerelease,
        body = "- Something new",
        assets = new[]
        {
            new { name = $"ShortcutForge-{tag.TrimStart('v')}-win-x64.exe", browser_download_url = $"https://example.test/{tag}.exe" },
        },
    };

    [Theory]
    [InlineData("1.1.0", "1.0.0", true)]
    [InlineData("v1.0.1", "1.0.0", true)]
    [InlineData("2.0", "1.9.9", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.2.0", false)]
    [InlineData("nonsense", "1.0.0", false)]
    public void Compares_versions(string latest, string current, bool newer) =>
        Assert.Equal(newer, UpdateChecker.IsNewer(latest, current));

    [Fact]
    public async Task Reports_a_newer_release_with_its_download()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, Release("v1.2.0"));
        var result = await UpdateChecker.CheckAsync("1.0.0", new HttpClient(handler));

        var update = Assert.IsType<UpdateInfo>(result.Update);
        Assert.Equal("1.2.0", update.Version);
        Assert.Equal("https://example.test/v1.2.0.exe", update.DownloadUrl);
        Assert.Contains("ShortcutForge", handler.LastRequest!.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Same_version_prerelease_and_missing_releases_are_up_to_date()
    {
        Assert.Null((await UpdateChecker.CheckAsync("1.2.0", new HttpClient(new FakeHandler(HttpStatusCode.OK, Release("v1.2.0"))))).Update);
        Assert.Null((await UpdateChecker.CheckAsync("1.0.0", new HttpClient(new FakeHandler(HttpStatusCode.OK, Release("v2.0.0", prerelease: true))))).Update);
        var none = await UpdateChecker.CheckAsync("1.0.0", new HttpClient(new FakeHandler(HttpStatusCode.NotFound, "{}")));
        Assert.Null(none.Update);
        Assert.Null(none.Error);
    }

    [Fact]
    public async Task Errors_are_reported_not_thrown()
    {
        var result = await UpdateChecker.CheckAsync("1.0.0", new HttpClient(new FakeHandler(HttpStatusCode.InternalServerError, "oops")));
        Assert.Null(result.Update);
        Assert.NotNull(result.Error);
        var broken = await UpdateChecker.CheckAsync("1.0.0", new HttpClient(new FakeHandler(HttpStatusCode.OK, "not json")));
        Assert.NotNull(broken.Error);
    }

    private sealed class Dialogs : IDialogService
    {
        public readonly List<string> Opened = [];
        public string? OpenFile(string filter) => null;
        public string? SaveFile(string filter, string fileName) => null;
        public string? Prompt(string title, string message, string initial = "") => null;
        public bool Confirm(string title, string message) => true;
        public bool? AskYesNoCancel(string title, string message) => false;
        public void Info(string title, string message) { }
        public void Error(string title, string message) { }
        public bool EditSettings(AppSettings settings) => false;
        public void OpenUrl(string url) => Opened.Add(url);
    }

    private static void Sta(Func<Task> body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { body().GetAwaiter().GetResult(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    [Fact]
    public void Background_check_is_daily_and_respects_skip_and_opt_out() => Sta(async () =>
    {
        var dialogs = new Dialogs();
        var vm = new MainViewModel(dialogs);
        var calls = 0;
        var update = new UpdateInfo("9.9.9", "https://example.test/page", "https://example.test/new.exe", null);
        vm.UpdateCheck = _ => { calls++; return Task.FromResult(new UpdateCheckResult(update)); };

        await vm.CheckForUpdatesInBackgroundAsync();
        Assert.Equal(update, vm.AvailableUpdate);
        Assert.Contains("9.9.9", vm.UpdateBannerText);

        vm.DownloadUpdateCommand.Execute(null);
        Assert.Equal("https://example.test/new.exe", Assert.Single(dialogs.Opened));

        // Skipped versions are not offered again, even after the daily interval.
        vm.SkipUpdateCommand.Execute(null);
        Assert.Null(vm.AvailableUpdate);
        vm.Settings.LastUpdateCheck = DateTime.UtcNow.AddDays(-2);
        await vm.CheckForUpdatesInBackgroundAsync();
        Assert.Null(vm.AvailableUpdate);
        Assert.Equal(2, calls);

        // At most once a day.
        await vm.CheckForUpdatesInBackgroundAsync();
        Assert.Equal(2, calls);

        // Opt-out.
        vm.Settings.CheckForUpdates = false;
        vm.Settings.LastUpdateCheck = null;
        await vm.CheckForUpdatesInBackgroundAsync();
        Assert.Equal(2, calls);
    });
}
