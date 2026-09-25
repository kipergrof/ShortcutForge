using System.Text;
using ShortcutForge.Core.Catalog;
using ShortcutForge.Core.Model;
using ShortcutForge.Core.Plist;

namespace ShortcutForge.Core.Tests;

public class PlistSerializerTests
{

    [Fact]
    public void Reads_sample()
    {
        var s = PlistSerializer.Read(Encoding.UTF8.GetBytes(PlistSamples.SampleXml), "Teszt");

        Assert.Equal("Teszt", s.Name);
        Assert.Equal(4282601983, s.Icon.StartColor);
        Assert.Equal(["WFStringContentItem"], s.InputClasses);
        Assert.Equal(["NCWidget"], s.Types);
        Assert.Equal(3, s.Actions.Count);

        var text = Assert.IsType<TokenString>(s.Actions[0].Parameters["WFTextActionText"]);
        Assert.Equal("Hello ￼!", text.Text);
        var att = Assert.Single(text.Attachments);
        Assert.Equal(6, att.Position);
        Assert.Equal(VariableKinds.ShortcutInput, att.Variable.Kind);

        var dict = Assert.IsType<DictionaryFieldValue>(s.Actions[1].Parameters["WFItems"]);
        Assert.Equal("name", Assert.Single(dict.Items).Key.Text);

        var msg = Assert.IsType<VariableValue>(s.Actions[2].Parameters["WFAlertActionMessage"]);
        Assert.Equal("AAAA-1", msg.Variable.OutputUuid);
        Assert.NotNull(msg.Variable.Extra?["Aggrandizements"]);
        Assert.Equal(new BoolValue(false), s.Actions[2].Parameters["WFAlertActionCancelButtonShown"]);
        Assert.Equal(new RealValue(1.5), s.Actions[2].Parameters["SomeNumber"]);
        Assert.True(s.Extra.ContainsKey("WFWorkflowImportQuestions"));
    }

    [Fact]
    public void Binary_round_trip_is_lossless()
    {
        var original = PlistSerializer.Read(Encoding.UTF8.GetBytes(PlistSamples.SampleXml));
        var copy = PlistSerializer.Read(PlistSerializer.WriteBinary(original));
        AssertSameShortcut(original, copy);
    }

    [Fact]
    public void Xml_round_trip_is_lossless()
    {
        var original = PlistSerializer.Read(Encoding.UTF8.GetBytes(PlistSamples.SampleXml));
        var copy = PlistSerializer.Read(Encoding.UTF8.GetBytes(PlistSerializer.WriteXml(original)));
        AssertSameShortcut(original, copy);
    }

    internal static void AssertSameShortcut(Shortcut expected, Shortcut actual)
    {
        Assert.Equal(expected.Icon, actual.Icon);
        Assert.Equal(expected.InputClasses, actual.InputClasses);
        Assert.Equal(expected.Types, actual.Types);
        Assert.Equal(expected.Actions.Count, actual.Actions.Count);
        for (var i = 0; i < expected.Actions.Count; i++)
        {
            Assert.Equal(expected.Actions[i].Identifier, actual.Actions[i].Identifier);
            Assert.Equal(expected.Actions[i].Parameters.Count, actual.Actions[i].Parameters.Count);
            foreach (var (key, value) in expected.Actions[i].Parameters)
                Assert.Equal(value, actual.Actions[i].Parameters[key]);
        }
    }

    [Fact]
    public void Catalog_loads_and_is_consistent()
    {
        var catalog = ActionCatalog.Default;
        Assert.True(catalog.Actions.Count > 150, $"Csak {catalog.Actions.Count} akció");
        Assert.NotNull(catalog.ById("is.workflow.actions.alert"));
        Assert.Equal("Alert", catalog.ById("is.workflow.actions.alert")!.Dsl);
        foreach (var action in catalog.Actions)
        {
            Assert.Equal(action.Params.Count, action.Params.Select(p => p.Name).Distinct().Count());
            Assert.Equal(action.Params.Count, action.Params.Select(p => p.Key).Distinct().Count());
            foreach (var p in action.Params.Where(p => p.Type == ParamKind.Enum))
                Assert.NotEmpty(p.Options!);
        }
    }

    [Fact]
    public void Control_flow_blocks_are_valid()
    {
        foreach (var def in ActionCatalog.Default.Actions.Where(a => a.Control != ControlFlowKind.None))
        {
            var block = ControlFlow.CreateBlock(def);
            Assert.Null(ControlFlow.Validate(block));
            Assert.Equal((0, block.Count - 1), ControlFlow.BlockRange(block, 0));
        }
    }
}
