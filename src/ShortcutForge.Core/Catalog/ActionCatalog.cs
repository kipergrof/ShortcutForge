using System.Reflection;
using System.Text.Json;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.Core.Catalog;

/// <summary>
/// All known actions, loaded from the JSON files embedded under Catalog/Data.
/// Actions not in the catalog (e.g. third-party App Intents) still work as generic actions.
/// </summary>
public sealed class ActionCatalog
{
    private static readonly Lazy<ActionCatalog> DefaultInstance = new(LoadEmbedded);

    private readonly Dictionary<string, ActionDefinition> _byId;
    private readonly Dictionary<string, ActionDefinition> _byDsl;

    public ActionCatalog(IEnumerable<ActionDefinition> actions)
    {
        Actions = actions.ToList();
        _byId = new Dictionary<string, ActionDefinition>(StringComparer.OrdinalIgnoreCase);
        _byDsl = new Dictionary<string, ActionDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in Actions)
        {
            if (!_byId.TryAdd(a.Id, a))
                throw new InvalidOperationException(L.T($"Duplikált akció azonosító a katalógusban: {a.Id}", $"Duplicate action identifier in the catalog: {a.Id}"));
            if (!_byDsl.TryAdd(a.Dsl, a))
                throw new InvalidOperationException(L.T($"Duplikált DSL név a katalógusban: {a.Dsl}", $"Duplicate DSL name in the catalog: {a.Dsl}"));
        }
    }

    public static ActionCatalog Default => DefaultInstance.Value;

    public IReadOnlyList<ActionDefinition> Actions { get; }

    public IEnumerable<string> Categories => Actions.Select(a => a.Category).Distinct();

    public ActionDefinition? ById(string id) => _byId.GetValueOrDefault(id);

    public ActionDefinition? ByDslName(string name) => _byDsl.GetValueOrDefault(name);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ActionCatalog LoadEmbedded()
    {
        var assembly = typeof(ActionCatalog).Assembly;
        var all = new List<ActionDefinition>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".json")).Order())
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            var file = JsonSerializer.Deserialize<CatalogFile>(stream, JsonOptions)
                       ?? throw new InvalidDataException(L.T($"Üres katalógus fájl: {resource}", $"Empty catalog file: {resource}"));
            foreach (var action in file.Actions)
            {
                if (action.Category == "Egyéb") action.Category = file.Category;
                all.Add(action);
            }
        }
        return new ActionCatalog(all);
    }

    private sealed class CatalogFile
    {
        public string Category { get; init; } = "Egyéb";
        public List<ActionDefinition> Actions { get; init; } = [];
    }
}
