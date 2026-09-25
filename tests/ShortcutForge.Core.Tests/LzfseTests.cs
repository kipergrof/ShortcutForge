using ShortcutForge.Core.Import;

namespace ShortcutForge.Core.Tests;

public class LzfseTests
{
    private static string DataFile(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    [Theory]
    [InlineData("small")]  // bvxn (LZVN) block
    [InlineData("medium")] // single bvx2 block
    [InlineData("large")]  // many bvx2 blocks
    [InlineData("random")] // bvx- (stored) block
    public void Decodes_reference_output(string name)
    {
        var compressed = File.ReadAllBytes(DataFile(name + ".lzfse"));
        var expected = File.ReadAllBytes(DataFile(name + ".bin"));

        var actual = Lzfse.Decode(compressed, out var consumed);

        Assert.Equal(compressed.Length, consumed);
        Assert.Equal(expected.Length, actual.Length);
        Assert.True(expected.AsSpan().SequenceEqual(actual));
    }

    [Fact]
    public void Extracts_shortcut_from_signed_container()
    {
        // AEA1 header + auth data + filler + LZFSE-compressed Apple Archive with Shortcut.wflow.
        var shortcut = ShortcutFileReader.Read(File.ReadAllBytes(DataFile("synthetic-signed.shortcut")), "Aláírt");

        var action = Assert.Single(shortcut.Actions);
        Assert.Equal("is.workflow.actions.alert", action.Identifier);
        Assert.Equal(new Model.StringValue("Aláírt teszt"), action.Parameters["WFAlertActionMessage"]);
        Assert.Equal("Aláírt", shortcut.Name);
    }

    [Fact]
    public void Extracts_real_apple_signed_shortcut()
    {
        // Signed by Apple's signing (via a shortcut-signing-server) from a two-action test shortcut.
        var shortcut = ShortcutFileReader.Read(File.ReadAllBytes(DataFile("real-signed.shortcut")), "Valódi");

        Assert.Equal(["is.workflow.actions.gettext", "is.workflow.actions.showresult"], shortcut.Actions.Select(a => a.Identifier));
        var text = Assert.IsType<Model.TokenString>(shortcut.Actions[0].Parameters["WFTextActionText"]);
        Assert.Equal("Szia a ShortcutForge-bol!", text.Text);
    }

    [Fact]
    public void Rejects_garbage()
    {
        Assert.Throws<InvalidDataException>(() => Lzfse.Decode("bvx2garbage garbage garbage garbage"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => Lzfse.Decode("nope"u8.ToArray()));
    }
}
