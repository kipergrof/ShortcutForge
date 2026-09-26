using System.Text;
using System.Text.RegularExpressions;
using ShortcutForge.Core.Catalog;

namespace ShortcutForge.Dsl;

/// <summary>
/// Builds the (static, cacheable) system prompt that teaches a language model ShortcutForge's
/// text language, and extracts the generated code from its reply.
/// </summary>
public static partial class AiPrompt
{
    private const string LanguageReference = """
        You write Apple Shortcuts in ShortcutForge's text language (a DSL). The user describes what the
        shortcut should do; you reply with the complete shortcut as code.

        Language reference
        ------------------
        #name "Shortcut name"                    header directives come first
        #icon color=blue glyph=59511             colors: red darkorange orange gold yellow green teal blue darkblue violet purple pink gray slate
        #input WFStringContentItem, WFURLContentItem   accepted input (share sheet / other shortcuts)
        #type NCWidget, ActionExtension          NCWidget = widget, ActionExtension = share sheet, WatchKit = Apple Watch

        One statement per line. An action is called by its DSL name with arguments:
            Alert("Message", title: "Title")      first argument may be positional, the rest are named
        Bind an action's output to a name and use it later:
            greeting = Text("Hi!")
            Notification(greeting, title: "Hello")
        Variables inside text go in braces: "Hello {greeting}". Literal braces: \{ \}. Quote: \". New line: \n.
        Built-in variables: ShortcutInput, Clipboard, CurrentDate, Ask, DeviceDetails, RepeatItem, RepeatIndex.
        Named variables: var total = 0   /   var items += value   (Set Variable / Add to Variable)
        Get a variable as a type, a property or a dictionary key:
            n.as(Number)    file.get("File Size")    data.as(Dictionary)["name"]
            types: Text Number Boolean Dictionary Date URL RichText File Image PDF Media Contact Location Email Phone Article SafariWebPage App
        Condition:
            if value contains "x" { ... } else { ... }
            operators: == != < <= > >= contains !contains beginsWith endsWith hasValue !hasValue  between 1 and 10
        Loops:  repeat 3 { ... }      foreach list { ... RepeatItem ... }
        Menu:   menu "Question?" { case "A" { ... } case "B" { ... } }
        Values: numbers 42 3.5, true/false, "text", lists ["a", "b"], dictionaries { "key": "value", "n": 2, "ok": true }
        Comment line (becomes a Comment action): // text
        Enum parameters take exactly one of the listed options, as a string.

        Rules
        -----
        - Use only the actions listed below, with their DSL names and parameter names exactly as listed.
        - Prefer the smallest shortcut that does the job; add a short // comment line where it helps.
        - Write user-visible texts (alerts, notifications, prompts, #name) in the language of the user's description.
        - Never put passwords, API keys or personal data into the shortcut; ask for them with AskForInput if needed.
        - If something cannot be done with the available actions, do the closest reasonable thing and explain it
          in a // comment at the top.
        - Reply with exactly one fenced code block marked sfdsl containing the whole shortcut, and nothing after it.
          You may write one or two sentences before the block.

        Available actions (DSL name(parameter: type) — Shortcuts name — description)
        ---------------------------------------------------------------------------
        """;

    private static readonly Lazy<string> SystemPromptText = new(Build);

    /// <summary>The full system prompt. Deterministic, so it can be cached by the API.</summary>
    public static string SystemPrompt => SystemPromptText.Value;

    private static string Build()
    {
        var sb = new StringBuilder(LanguageReference);
        foreach (var group in ActionCatalog.Default.Actions.GroupBy(a => a.Category))
        {
            sb.Append('\n').Append("## ").Append(ActionDefinition.CategoryName(group.Key) is var en && en != group.Key ? en : group.Key).Append('\n');
            foreach (var a in group)
            {
                if (a.Control != ControlFlowKind.None) continue; // written with if / repeat / foreach / menu
                sb.Append(a.Dsl).Append('(');
                sb.AppendJoin(", ", a.Params.Select(p =>
                    p.Type == ParamKind.Enum && p.Options is { Count: > 0 }
                        ? $"{p.Name}: {string.Join("|", p.Options.Select(o => '"' + o + '"'))}"
                        : $"{p.Name}: {TypeName(p.Type)}"));
                sb.Append(')');
                if (a.Output is not null) sb.Append(" -> output");
                sb.Append(" — ").Append(a.Name);
                if (a.DescriptionEn is { } d) sb.Append(" — ").Append(d);
                if (a.MacOnly) sb.Append(" (Mac only)");
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    private static string TypeName(ParamKind kind) => kind switch
    {
        ParamKind.Text => "text",
        ParamKind.String => "plain text",
        ParamKind.Variable => "variable",
        ParamKind.Number or ParamKind.Integer => "number",
        ParamKind.Bool => "true|false",
        ParamKind.Dictionary => "dictionary",
        ParamKind.List => "list",
        _ => "raw",
    };

    [GeneratedRegex(@"```[ \t]*(?:sfdsl|dsl|shortcut|text)?[ \t]*\r?\n(?<code>.*?)```", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CodeBlock();

    /// <summary>The code from the model's reply: the last fenced block, or the whole reply if there is none.</summary>
    public static string ExtractCode(string reply)
    {
        var matches = CodeBlock().Matches(reply);
        return (matches.Count > 0 ? matches[^1].Groups["code"].Value : reply).Trim() + "\n";
    }

    /// <summary>User message asking to fix code that did not parse.</summary>
    public static string RepairRequest(string description, string code, string error) =>
        $"""
        The user asked for: {description}

        This shortcut code has an error: {error}

        ```sfdsl
        {code.TrimEnd()}
        ```

        Fix the error and reply with the complete corrected shortcut in one sfdsl code block.
        """;
}
