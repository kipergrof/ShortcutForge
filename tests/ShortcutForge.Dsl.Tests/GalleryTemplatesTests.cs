using ShortcutForge.Core;
using ShortcutForge.Core.Catalog;
using ShortcutForge.Core.Model;

namespace ShortcutForge.Dsl.Tests;

/// <summary>Checks the online gallery (templates/ in the repository) the same way CI does.</summary>
public class GalleryTemplatesTests
{
    private static readonly HashSet<string> StructuralKeys =
        [ActionInstance.UuidKey, ActionInstance.GroupingKey, ActionInstance.ControlFlowKey, ActionInstance.CustomOutputNameKey];

    private static string TemplatesFolder()
    {
        var root = typeof(GalleryTemplatesTests).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .Cast<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepositoryRoot")?.Value;
        if (root is not null && File.Exists(Path.Combine(root, "templates", "index.json")))
            return Path.Combine(root, "templates");
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "templates", "index.json");
            if (File.Exists(candidate)) return Path.GetDirectoryName(candidate)!;
        }
        throw new DirectoryNotFoundException("templates/index.json not found above the test directory.");
    }

    [Fact]
    public void Index_lists_existing_files_in_both_languages()
    {
        var folder = TemplatesFolder();
        var templates = TemplateGallery.ParseIndex(File.ReadAllText(Path.Combine(folder, "index.json")));
        Assert.True(templates.Count >= 10);
        Assert.Equal(templates.Count, templates.Select(t => t.Id).Distinct().Count());
        foreach (var t in templates)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Name.GetValueOrDefault("en")), t.Id);
            Assert.False(string.IsNullOrWhiteSpace(t.Description.GetValueOrDefault("en")), t.Id);
            Assert.True(t.Files.ContainsKey("en"), t.Id);
            foreach (var file in t.Files.Values)
                Assert.True(File.Exists(Path.Combine(folder, file)), $"{t.Id}: missing {file}");
        }
    }

    [Fact]
    public void Every_template_parses_and_uses_only_known_actions_and_parameters()
    {
        var folder = TemplatesFolder();
        foreach (var path in Directory.GetFiles(folder, "*.sfdsl"))
        {
            var shortcut = DslParser.Parse(File.ReadAllText(path));
            Assert.NotEmpty(shortcut.Actions);
            Assert.Null(ControlFlow.Validate(shortcut.Actions));
            foreach (var action in shortcut.Actions)
            {
                var definition = ActionCatalog.Default.ById(action.Identifier);
                Assert.True(definition is not null, $"{Path.GetFileName(path)}: unknown action {action.Identifier}");
                foreach (var key in action.Parameters.Keys.Where(k => !StructuralKeys.Contains(k)))
                    Assert.True(definition!.ParamByKey(key) is not null,
                        $"{Path.GetFileName(path)}: {definition.Dsl} has no parameter '{key}' (typo?)");
            }
        }
    }
}
