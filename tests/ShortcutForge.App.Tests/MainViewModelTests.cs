using System.IO;
using ShortcutForge.App.Services;
using ShortcutForge.App.ViewModels;
using ShortcutForge.Core.Catalog;
using ShortcutForge.Core.Import;
using ShortcutForge.Core.Model;

namespace ShortcutForge.App.Tests;

/// <summary>Drives the main view model without a window, like a user would through the UI.</summary>
public class MainViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sf-tests-" + Guid.NewGuid().ToString("N"));

    public MainViewModelTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("SHORTCUTFORGE_SETTINGS_DIR", _dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private sealed class FakeDialogs : IDialogService
    {
        public string? NextSavePath;
        public bool ConfirmAnswer = true;
        public string? NextOpenPath;
        public readonly List<string> Messages = [];

        public string? OpenFile(string filter) => NextOpenPath;
        public string? SaveFile(string filter, string fileName) => NextSavePath;
        public string? Prompt(string title, string message, string initial = "") => null;
        public bool Confirm(string title, string message) { Messages.Add(message); return ConfirmAnswer; }
        public bool? AskYesNoCancel(string title, string message) => false;
        public void Info(string title, string message) => Messages.Add(message);
        public void Error(string title, string message) => Messages.Add("ERROR: " + message);
        public bool EditSettings(AppSettings settings) => false;
        public string? NextGallerySource;
        public string? PickGalleryTemplate() => NextGallerySource;
    }

    /// <summary>WPF objects need an STA thread.</summary>
    private static void Sta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    private static ActionDefinition Def(string dsl) => ActionCatalog.Default.ByDslName(dsl)!;

    [Fact]
    public void Starts_with_example_and_valid_structure() => Sta(() =>
    {
        var vm = new MainViewModel(new FakeDialogs());
        Assert.NotEmpty(vm.Cards);
        Assert.Null(vm.StructureWarning);
        Assert.Contains("providedInput", vm.VariableSuggestions);
    });

    [Fact]
    public void Editing_moving_duplicating_deleting_and_undo() => Sta(() =>
    {
        var vm = new MainViewModel(new FakeDialogs());
        vm.NewCommand.Execute(null);
        Assert.Empty(vm.Cards);

        // Text action, then an alert that references it.
        vm.AddAction(Def("Text"));
        var textCard = vm.Cards[0];
        textCard.Params.Single(p => p.Key == "WFTextActionText").Text = "Helló világ";
        Assert.Equal(TokenString.Plain("Helló világ"), textCard.Action.Parameters["WFTextActionText"]);
        Assert.Equal("text", textCard.OutputName);

        vm.SelectedCard = null;
        vm.AddAction(Def("Alert"));
        var alertCard = vm.Cards[1];
        var message = alertCard.Params.Single(p => p.Key == "WFAlertActionMessage");
        message.Text = "Üzenet: {text}";
        var token = Assert.IsType<TokenString>(alertCard.Action.Parameters["WFAlertActionMessage"]);
        Assert.Equal(textCard.Action.Uuid, Assert.Single(token.Attachments).Variable.OutputUuid);

        // Invalid expression shows an error and keeps the model unchanged.
        message.Text = "Hiányzó {nincsilyen}";
        Assert.NotNull(message.Error);
        Assert.Equal(token, alertCard.Action.Parameters["WFAlertActionMessage"]);

        // If block with condition.
        vm.SelectedCard = null;
        vm.AddAction(Def("If"));
        Assert.Equal(5, vm.Current.Actions.Count); // text, alert, if, otherwise, end
        var ifCard = vm.Cards[2];
        ifCard.ConditionInput = "text";
        ifCard.ConditionOperator = ActionCardViewModel.ConditionOptions.Single(o => o.Code == Conditions.Contains);
        ifCard.ConditionValue = "világ";
        Assert.Null(ifCard.ConditionError);
        var start = vm.Current.Actions[2];
        Assert.Equal(new IntegerValue(Conditions.Contains), start.Parameters["WFCondition"]);
        Assert.Equal(TokenString.Plain("világ"), start.Parameters["WFConditionalActionString"]);

        // Move the whole If block above the alert.
        vm.MoveUpCommand.Execute(vm.Cards[2]);
        Assert.Equal(ControlFlow.IfId, vm.Current.Actions[1].Identifier);
        Assert.Equal("is.workflow.actions.alert", vm.Current.Actions[4].Identifier);
        Assert.Null(ControlFlow.Validate(vm.Current.Actions));

        // Drag the alert into the If body (before "Otherwise").
        vm.MoveBlock(4, 2);
        Assert.Equal("is.workflow.actions.alert", vm.Current.Actions[2].Identifier);
        Assert.Equal(1, vm.Cards[2].Indent);

        // Duplicate the block: new grouping id, still valid.
        vm.DuplicateCardCommand.Execute(vm.Cards[1]);
        Assert.Equal(9, vm.Current.Actions.Count);
        Assert.Null(ControlFlow.Validate(vm.Current.Actions));
        Assert.NotEqual(vm.Current.Actions[1].GroupingIdentifier, vm.Current.Actions[5].GroupingIdentifier);

        // Delete the copy (whole block), then undo / redo.
        vm.DeleteCardCommand.Execute(vm.Cards[5]);
        Assert.Equal(5, vm.Current.Actions.Count);
        vm.UndoCommand.Execute(null);
        Assert.Equal(9, vm.Current.Actions.Count);
        vm.RedoCommand.Execute(null);
        Assert.Equal(5, vm.Current.Actions.Count);
    });

    [Fact]
    public void Opens_a_template_from_the_gallery() => Sta(() =>
    {
        var dialogs = new FakeDialogs { NextGallerySource = "#name \"Kocka\"\nroll = RandomNumber(min: 1, max: 6)\nAlert(\"{roll}\")" };
        var vm = new MainViewModel(dialogs);
        vm.OpenGalleryCommand.Execute(null);
        Assert.Equal("Kocka", vm.ShortcutName);
        Assert.Equal(2, vm.Cards.Count);

        dialogs.NextGallerySource = null; // cancelled: nothing changes
        vm.OpenGalleryCommand.Execute(null);
        Assert.Equal("Kocka", vm.ShortcutName);
    });

    [Fact]
    public void All_templates_parse_in_both_languages()
    {
        foreach (var template in ShortcutTemplates.All)
        {
            foreach (var source in new[] { template.SourceHu, template.SourceEn })
            {
                var shortcut = Dsl.DslParser.Parse(source);
                Assert.Null(ControlFlow.Validate(shortcut.Actions));
                // Everything used in templates must be a known catalog action.
                Assert.All(shortcut.Actions, a => Assert.NotNull(ActionCatalog.Default.ById(a.Identifier)));
            }
        }
    }

    [Fact]
    public void Renaming_an_output_updates_references() => Sta(() =>
    {
        var vm = new MainViewModel(new FakeDialogs());
        vm.OnDslEdited("t = Text(\"a\")\nAlert(\"Érték: {t}\")");
        Assert.True(vm.ApplyDslIfNeeded());

        vm.RenameOutput(vm.Cards[0], "Saját szöveg");
        Assert.Equal(new StringValue("Saját szöveg"), vm.Current.Actions[0].Parameters["CustomOutputName"]);
        var token = Assert.IsType<TokenString>(vm.Current.Actions[1].Parameters["WFAlertActionMessage"]);
        Assert.Equal("Saját szöveg", token.Attachments[0].Variable.OutputName);
        Assert.Equal("sajátSzöveg", vm.Cards[0].OutputName);
        Assert.Equal("Érték: {sajátSzöveg}", vm.Cards[1].Params.Single(p => p.Key == "WFAlertActionMessage").Text);
        Assert.DoesNotContain(vm.Cards[0].Params, p => p.Key == "CustomOutputName");

        // Survives the text view.
        var dsl = vm.CurrentDsl;
        vm.OnDslEdited(dsl);
        Assert.True(vm.ApplyDslIfNeeded());
        Assert.Equal("sajátSzöveg", vm.Cards[0].OutputName);

        // Empty name restores the default.
        vm.RenameOutput(vm.Cards[0], "");
        Assert.False(vm.Current.Actions[0].Parameters.ContainsKey("CustomOutputName"));
        Assert.Equal("text", vm.Cards[0].OutputName);
    });

    [Fact]
    public void Menu_cases_stay_in_sync() => Sta(() =>
    {
        var vm = new MainViewModel(new FakeDialogs());
        vm.NewCommand.Execute(null);
        vm.AddAction(Def("Menu"));
        var start = vm.Current.Actions[0];
        vm.AddMenuCaseCommand.Execute(vm.Cards[0]);
        var cases = vm.Cards.Where(c => c.IsMenuCase).ToList();
        Assert.Equal(3, cases.Count);
        cases[0].CaseTitle = "Kávé";
        var items = Assert.IsType<ArrayValue>(start.Parameters["WFMenuItems"]);
        Assert.Equal(new StringValue("Kávé"), items.Items[0]);
        Assert.Equal(3, items.Items.Count);

        vm.DeleteCardCommand.Execute(vm.Cards.First(c => c.IsMenuCase));
        items = Assert.IsType<ArrayValue>(start.Parameters["WFMenuItems"]);
        Assert.Equal(2, items.Items.Count);
        Assert.Null(ControlFlow.Validate(vm.Current.Actions));
    });

    [Fact]
    public void Block_editing_keeps_structure_valid() => Sta(() =>
    {
        var vm = new MainViewModel(new FakeDialogs());
        vm.OnDslEdited("""
            menu "M" {
                case "A" {
                    Vibrate()
                }
                case "B" {
                }
            }
            Alert("vége")
            """);
        Assert.True(vm.ApplyDslIfNeeded());

        // Duplicating a menu item keeps it inside its menu.
        var caseA = vm.Cards.First(c => c.IsMenuCase);
        vm.DuplicateCardCommand.Execute(caseA);
        Assert.Null(ControlFlow.Validate(vm.Current.Actions));
        var items = Assert.IsType<ArrayValue>(vm.Current.Actions[0].Parameters["WFMenuItems"]);
        Assert.Equal(3, items.Items.Count);

        // Dragging a menu item out of its menu is refused.
        var count = vm.Current.Actions.Count;
        var firstCase = vm.Cards.First(c => c.IsMenuCase);
        vm.MoveBlock(firstCase.Index, vm.Cards.Count);
        Assert.Null(ControlFlow.Validate(vm.Current.Actions));
        Assert.Equal(count, vm.Current.Actions.Count);
        Assert.True(vm.Current.Actions[1].ControlFlow == ControlFlowMode.Middle);

        // With a block start selected, a new action goes inside the block.
        vm.SelectedCard = vm.Cards[0];
        vm.AddAction(Def("ShowResult"));
        Assert.Equal("is.workflow.actions.showresult", vm.Current.Actions[1].Identifier);
        Assert.Equal(1, vm.Cards[1].Indent);
    });

    [Fact]
    public void Dsl_edits_apply_only_when_valid() => Sta(() =>
    {
        var vm = new MainViewModel(new FakeDialogs());
        vm.OnDslEdited("x = Text(\"a\")\nAlert(nincs)");
        Assert.NotNull(vm.DslError);
        Assert.Equal(2, vm.DslErrorLine);
        Assert.False(vm.ApplyDslIfNeeded());

        vm.OnDslEdited("#name \"DSL-ből\"\nx = Text(\"a\")\nAlert(x)");
        Assert.Null(vm.DslError);
        Assert.True(vm.ApplyDslIfNeeded());
        Assert.Equal("DSL-ből", vm.ShortcutName);
        Assert.Equal(2, vm.Cards.Count);
        // Names are derived from the output name (DSL binding names are not stored in the shortcut).
        Assert.Equal("{text}", vm.Cards[1].Params.Single(p => p.Key == "WFAlertActionMessage").Text);

        vm.UndoCommand.Execute(null);
        Assert.Equal("Első parancsom", vm.ShortcutName);
    });

    [Fact]
    public async Task Save_open_and_export_round_trip()
    {
        Shortcut? original = null;
        string? projectPath = null, exportPath = null;
        FakeDialogs? dialogs = null;
        MainViewModel? vm = null;

        Sta(() =>
        {
            dialogs = new FakeDialogs();
            vm = new MainViewModel(dialogs);
            original = vm.Current;
            projectPath = dialogs.NextSavePath = Path.Combine(_dir, "próba.sfdsl");
            Assert.True(vm.SaveAs());
            Assert.False(vm.IsDirty);
            Assert.True(File.Exists(projectPath));
        });

        // Export runs asynchronously; the unsigned exporter completes synchronously.
        exportPath = Path.Combine(_dir, "próba.shortcut");
        Sta(() =>
        {
            dialogs!.NextSavePath = exportPath;
            // Decline the one-time online signing offer: tests must not call the network.
            dialogs.ConfirmAnswer = false;
            vm!.ExportCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.Equal(SignerKind.Unsigned, vm.Settings.Signer);
            Assert.True(vm.Settings.ShortcutyOfferShown);
            static bool IsOffer(string m) => m.Contains("Aláírassam most ingyen");
            Assert.Single(dialogs.Messages, IsOffer);

            // Not asked again.
            vm.ExportCommand.ExecuteAsync(null).GetAwaiter().GetResult();
            Assert.Single(dialogs.Messages, IsOffer);
        });
        Assert.True(File.Exists(exportPath));
        Assert.Contains(dialogs!.Messages, m => m.Contains("shortcuts sign"));

        var exported = ShortcutFileReader.ReadFile(exportPath);
        Assert.Equal(original!.Actions.Count, exported.Actions.Count);
        Assert.Equal(original.Actions.Select(a => a.Identifier), exported.Actions.Select(a => a.Identifier));

        Sta(() =>
        {
            var vm2 = new MainViewModel(new FakeDialogs());
            vm2.OpenPath(projectPath!);
            Assert.Equal(original.Name, vm2.Current.Name);
            Assert.Equal(original.Actions.Count, vm2.Current.Actions.Count);
            Assert.Contains(projectPath!, vm2.RecentFiles);

            vm2.OpenPath(exportPath);
            Assert.Equal("próba", vm2.Current.Name);
            Assert.Equal(original.Actions.Count, vm2.Current.Actions.Count);
        });
        await Task.CompletedTask;
    }
}
