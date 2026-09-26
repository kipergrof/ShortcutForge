using ShortcutForge.App.ViewModels;
using ShortcutForge.Core.Catalog;

namespace ShortcutForge.App.Tests;

public class CommandPaletteTests
{
    private static PaletteItem Item(string title, string? keywords = null) => new(title, "", "", () => { }, keywords);

    [Theory]
    [InlineData("export", "Exportálás .shortcut fájlba", true)]   // "export" is a prefix of "exportalas"
    [InlineData("exp sh", "Export to .shortcut file", true)]      // fuzzy, in order
    [InlineData("sotet", "Téma: sötét", true)]                    // accents folded
    [InlineData("xyz", "Téma: sötét", false)]
    [InlineData("ts", "Téma: sötét", true)]
    [InlineData("st", "Téma", false)]                            // order matters
    public void Matches(string query, string text, bool expected) =>
        Assert.Equal(expected, PaletteMatcher.Score(query, text) is not null);

    [Fact]
    public void Ranks_prefix_and_word_matches_first()
    {
        var items = new[] { Item("Add action: Show Alert"), Item("Alert settings"), Item("Calculate"), Item("Wait to Return") };
        var result = PaletteMatcher.Filter(items, "alert");
        Assert.Equal("Alert settings", result[0].Title);
        Assert.Equal("Add action: Show Alert", result[1].Title);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Keywords_also_match()
    {
        var items = new[] { Item("Theme: dark", "sotet dark") };
        Assert.Single(PaletteMatcher.Filter(items, "sötét"));
    }

    [Fact]
    public void Palette_contains_commands_and_every_action_and_runs_them()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var vm = new MainViewModel(new PaletteDialogs());
                vm.NewCommand.Execute(null);
                var items = vm.BuildPaletteItems();
                Assert.True(items.Count > ActionCatalog.Default.Actions.Count);

                // Only relevant results: no scattered-letter matches like "Get Latest Screenshots".
                var alertResults = PaletteMatcher.Filter(items, "alert");
                Assert.All(alertResults, r => Assert.Contains("alert", PaletteMatcher.Fold(r.Title + " " + r.Keywords)));

                var alert = PaletteMatcher.Filter(items, "show alert")[0];
                alert.Execute();
                Assert.Equal("is.workflow.actions.alert", Assert.Single(vm.Current.Actions).Identifier);

                PaletteMatcher.Filter(items, "undo")[0].Execute();
                Assert.Empty(vm.Current.Actions);
            }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    private sealed class PaletteDialogs : IDialogService
    {
        public string? OpenFile(string filter) => null;
        public string? SaveFile(string filter, string fileName) => null;
        public string? Prompt(string title, string message, string initial = "") => null;
        public bool Confirm(string title, string message) => true;
        public bool? AskYesNoCancel(string title, string message) => false;
        public void Info(string title, string message) { }
        public void Error(string title, string message) { }
        public bool EditSettings(Services.AppSettings settings) => false;
        public void OpenUrl(string url) { }
        public void ShowShare(byte[] data, string fileName, bool isSigned) { }
    }
}
