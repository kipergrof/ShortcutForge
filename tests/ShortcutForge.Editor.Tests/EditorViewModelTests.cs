using ShortcutForge.Core.Catalog;
using ShortcutForge.Core.Localization;
using ShortcutForge.Core.Model;
using ShortcutForge.Core.Plist;
using ShortcutForge.Dsl;
using ShortcutForge.Editor;

// The UI language is a static setting; keep tests that depend on it from running in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ShortcutForge.Editor.Tests;

public sealed class EditorViewModelTests
{
    private static ActionDefinition Def(string dsl) => ActionCatalog.Default.ByDslName(dsl)!;

    private static EditorViewModel Load(string source)
    {
        var vm = new EditorViewModel();
        vm.LoadShortcut(DslParser.Parse(source), null);
        return vm;
    }

    [Fact]
    public void Starts_with_the_example_shortcut_and_indents_nested_actions()
    {
        var vm = new EditorViewModel();

        Assert.Equal(vm.Current.Actions.Count, vm.Cards.Count);
        Assert.Equal(ControlFlow.ComputeIndent(vm.Current.Actions), vm.Cards.Select(c => c.Indent).ToArray());
        Assert.Contains(vm.Cards, c => c.Indent == 1);
        Assert.False(vm.IsDirty);
        Assert.Null(vm.StructureWarning);
    }

    [Fact]
    public void Adding_an_if_block_inserts_start_otherwise_and_end_after_the_selection()
    {
        var vm = Load("""Comment("a")""");
        vm.SelectedCard = vm.Cards[0];

        vm.AddAction(Def("If"));

        Assert.Equal(4, vm.Cards.Count);
        Assert.Equal([null, ControlFlowMode.Start, ControlFlowMode.Middle, ControlFlowMode.End],
            vm.Cards.Select(c => c.Mode).ToArray());
        Assert.True(vm.Cards[1].IsIfStart);
        Assert.Same(vm.Cards[1], vm.SelectedCard);
        Assert.True(vm.IsDirty);
        Assert.Null(vm.StructureWarning);
    }

    [Fact]
    public void Actions_inside_a_block_are_indented()
    {
        var vm = Load("""
            if ShortcutInput hasValue {
                Comment("inside")
            }
            Comment("after")
            """);

        Assert.Equal([0, 1, 0, 0], vm.Cards.Select(c => c.Indent).ToArray());
        Assert.True(vm.Cards[1].IsIndented);
    }

    [Fact]
    public void Deleting_a_block_start_removes_the_whole_block()
    {
        var vm = Load("""
            Comment("before")
            if ShortcutInput hasValue {
                Comment("inside")
            }
            Comment("after")
            """);

        vm.DeleteCardCommand.Execute(vm.Cards[1]);

        Assert.Equal(2, vm.Current.Actions.Count);
        Assert.All(vm.Current.Actions, a => Assert.Equal("is.workflow.actions.comment", a.Identifier));
    }

    [Fact]
    public void Moving_down_jumps_over_a_whole_block()
    {
        var vm = Load("""
            Comment("first")
            if ShortcutInput hasValue {
                Comment("inside")
            }
            """);

        vm.MoveDownCommand.Execute(vm.Cards[0]);

        Assert.Equal(ControlFlowMode.Start, vm.Current.Actions[0].ControlFlow);
        Assert.Equal("first", CommentText(vm.Current.Actions[^1]));
        Assert.Null(ControlFlow.Validate(vm.Current.Actions));

        // Moving up one step from below a block enters the block (like in the Windows app).
        vm.MoveUpCommand.Execute(vm.Cards[^1]);
        Assert.Equal("first", CommentText(vm.Current.Actions[2]));
        Assert.Equal(1, vm.Cards[2].Indent);
    }

    [Fact]
    public void Duplicating_a_block_gives_it_a_new_group()
    {
        var vm = Load("""
            repeat 2 {
                Comment("x")
            }
            """);

        vm.DuplicateCardCommand.Execute(vm.Cards[0]);

        Assert.Equal(6, vm.Current.Actions.Count);
        Assert.NotEqual(vm.Current.Actions[0].GroupingIdentifier, vm.Current.Actions[3].GroupingIdentifier);
        Assert.Null(ControlFlow.Validate(vm.Current.Actions));
    }

    [Fact]
    public void Adding_menu_items_keeps_the_menu_titles_in_sync()
    {
        var vm = Load("");
        vm.AddAction(Def("Menu"));
        var start = vm.Cards[0];

        vm.AddMenuCaseCommand.Execute(start);

        var items = (ArrayValue)vm.Current.Actions[0].Get("WFMenuItems")!;
        Assert.Equal(3, items.Items.Count);
        Assert.Equal(3, vm.Cards.Count(c => c.IsMenuCase));
    }

    [Fact]
    public void Editing_a_parameter_updates_the_model_and_can_be_undone()
    {
        var vm = Load("""Comment("old")""");
        var param = vm.Cards[0].Params.Single();

        param.Text = "new";

        Assert.Equal("new", CommentText(vm.Current.Actions[0]));
        Assert.True(vm.IsDirty);
        Assert.True(vm.UndoCommand.CanExecute(null));

        vm.UndoCommand.Execute(null);
        Assert.Equal("old", CommentText(vm.Current.Actions[0]));
        vm.RedoCommand.Execute(null);
        Assert.Equal("new", CommentText(vm.Current.Actions[0]));
    }

    [Fact]
    public void An_invalid_parameter_value_shows_an_error_and_keeps_the_model()
    {
        var vm = Load("""Wait(3)""");
        var param = vm.Cards[0].Params.First(p => p.Kind is ParamKind.Number or ParamKind.Integer);

        param.Text = "(((";

        Assert.NotNull(param.Error);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Invalid_dsl_text_is_reported_and_not_applied()
    {
        var vm = Load("""Comment("a")""");

        vm.OnDslEdited("Comment(");

        Assert.NotNull(vm.DslError);
        Assert.True(vm.DslErrorLine > 0);
        Assert.False(vm.ApplyDslIfNeeded());
        Assert.Single(vm.Current.Actions);

        vm.DiscardDslEdits();
        Assert.Null(vm.DslError);
        Assert.True(vm.ApplyDslIfNeeded());
    }

    [Fact]
    public void Valid_dsl_text_replaces_the_cards_and_is_undoable()
    {
        var vm = Load("""Comment("a")""");
        var changed = 0;
        vm.DslSourceChanged += (_, _) => changed++;

        vm.OnDslEdited("""
            #name "Renamed"
            Comment("a")
            Comment("b")
            """);
        Assert.Null(vm.DslError);
        Assert.True(vm.ApplyDslIfNeeded());

        Assert.Equal(2, vm.Cards.Count);
        Assert.Equal("Renamed", vm.ShortcutName);
        Assert.Null(vm.PendingDsl);

        vm.UndoCommand.Execute(null);
        Assert.Single(vm.Cards);
        Assert.True(changed > 0);
    }

    [Fact]
    public void The_printed_text_parses_back_to_the_same_actions()
    {
        var vm = new EditorViewModel();
        var reparsed = DslParser.Parse(vm.CurrentDsl);
        Assert.Equal(vm.Current.Actions.Select(a => a.Identifier), reparsed.Actions.Select(a => a.Identifier));
    }

    [Fact]
    public void Shortcut_settings_change_the_model()
    {
        var vm = Load("""Comment("a")""");

        vm.ShortcutName = "My name";
        vm.SelectedColor = vm.ColorOptions.First(c => c.Name == "red");
        vm.TypeOptions.First(o => o.Key == "WatchKit").IsChecked = true;

        Assert.Equal("My name", vm.Current.Name);
        Assert.Equal(ShortcutIcon.ColorByName("red"), vm.Current.Icon.StartColor);
        Assert.Contains("WatchKit", vm.Current.Types);
        Assert.Contains("My name", vm.WindowTitle);
        Assert.Equal("#FF4351", vm.SelectedColor.Hex);
    }

    [Fact]
    public void Library_is_grouped_by_category_and_filtered_by_search()
    {
        var vm = new EditorViewModel();
        Assert.Equal(ActionCatalog.Default.Actions.Count, vm.Library.Sum(g => g.Items.Count));
        Assert.Equal(vm.Library.Count, vm.Library.Select(g => g.Name).Distinct().Count());

        vm.SearchText = "Alert";
        Assert.Contains(vm.Library.SelectMany(g => g.Items), i => i.Dsl == "Alert");
        Assert.True(vm.Library.Sum(g => g.Items.Count) < ActionCatalog.Default.Actions.Count);

        vm.SearchText = "zzzz-no-such-action";
        Assert.True(vm.LibraryIsEmpty);
    }

    [Fact]
    public void Enter_in_the_search_adds_the_first_match()
    {
        var vm = Load("");
        vm.SearchText = "Comment";

        vm.AddFirstMatchCommand.Execute(null);

        Assert.Single(vm.Current.Actions);
    }

    [Fact]
    public void Switching_the_language_relabels_the_library()
    {
        var before = L.Language;
        try
        {
            L.Language = L.English;
            var vm = new EditorViewModel();
            Assert.Contains(vm.Library, g => g.Name == "Scripting");

            L.Language = L.Hungarian;
            Assert.Contains(vm.Library, g => g.Name == "Vezérlés");
            Assert.Equal("Nyelv: magyar", vm.Status);
        }
        finally
        {
            L.Language = before;
        }
    }

    private static string? CommentText(ActionInstance action) =>
        action.Get("WFCommentActionText") switch
        {
            StringValue s => s.Value,
            TokenString t => t.Text,
            _ => null,
        };
}

public sealed class ShortcutDocumentTests
{
    [Fact]
    public void Project_files_round_trip()
    {
        var shortcut = DslParser.Parse("""
            #name "Round trip"
            Comment("hello")
            """);
        var path = Path.Combine(Path.GetTempPath(), $"sf-{Guid.NewGuid():N}.sfdsl");
        try
        {
            ShortcutDocument.SaveProject(shortcut, path);
            var loaded = ShortcutDocument.Open(path);
            Assert.Equal("Round trip", loaded.Name);
            Assert.Single(loaded.Actions);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Unsigned_shortcut_bytes_open_again()
    {
        var shortcut = DslParser.Parse("""Comment("x")""");
        var bytes = ShortcutDocument.UnsignedBytes(shortcut);

        var loaded = ShortcutDocument.Open(bytes, "Test.shortcut");

        Assert.Equal("Test", loaded.Name);
        Assert.Single(loaded.Actions);
    }

    [Fact]
    public void Xml_plist_opens()
    {
        var xml = System.Text.Encoding.UTF8.GetBytes(PlistSerializer.WriteXml(DslParser.Parse("""Comment("x")""")));
        Assert.Single(ShortcutDocument.Open(xml, "X.plist").Actions);
    }

    [Theory]
    [InlineData("a/b:c", "a_b_c")]
    [InlineData("   ", "Shortcut")]
    [InlineData("Plain name", "Plain name")]
    public void Safe_file_names(string input, string expected) =>
        Assert.Equal(expected, ShortcutDocument.SafeFileName(input));
}
