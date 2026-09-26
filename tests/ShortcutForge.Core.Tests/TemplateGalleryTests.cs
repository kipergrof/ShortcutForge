using ShortcutForge.Core.Localization;

namespace ShortcutForge.Core.Tests;

public class TemplateGalleryTests
{
    private const string Index = """
        {
          "version": 1,
          "templates": [
            { "id": "a", "category": "fun", "name": { "hu": "Kocka", "en": "Dice" },
              "description": { "en": "Rolls a die" }, "files": { "hu": "a.hu.sfdsl", "en": "a.en.sfdsl" } },
            { "id": "", "name": { "en": "no id" }, "files": { "en": "x.sfdsl" } },
            { "id": "nofiles", "name": { "en": "No files" }, "files": {} }
          ]
        }
        """;

    [Fact]
    public void Parses_valid_entries_and_localizes()
    {
        var templates = TemplateGallery.ParseIndex(Index);
        var t = Assert.Single(templates);
        try
        {
            L.Language = "hu";
            Assert.Equal("Kocka", t.DisplayName);
            Assert.Equal("Rolls a die", t.DisplayDescription); // falls back to English
            Assert.Equal("a.hu.sfdsl", t.FileName);
            L.Language = "en";
            Assert.Equal("Dice", t.DisplayName);
            Assert.Equal("a.en.sfdsl", t.FileName);
        }
        finally
        {
            L.Language = "hu";
        }
    }

    [Fact]
    public async Task Rejects_file_names_with_paths()
    {
        var evil = TemplateGallery.ParseIndex("""{ "templates": [ { "id": "x", "name": {"en":"x"}, "files": { "en": "../secret" } } ] }""")[0];
        await Assert.ThrowsAsync<InvalidOperationException>(() => TemplateGallery.GetSourceAsync(evil));
    }
}
