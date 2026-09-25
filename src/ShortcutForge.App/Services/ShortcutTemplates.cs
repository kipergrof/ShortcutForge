using ShortcutForge.Core.Localization;

namespace ShortcutForge.App.Services;

/// <summary>A ready-made starting point for a new shortcut (File › New from template).</summary>
public sealed record ShortcutTemplate(string NameHu, string NameEn, string SourceHu, string SourceEn)
{
    public string Name => L.T(NameHu, NameEn);
    public string Source => L.T(SourceHu, SourceEn);
    public override string ToString() => Name;
}

public static class ShortcutTemplates
{
    public static IReadOnlyList<ShortcutTemplate> All { get; } =
    [
        new("Üres parancs", "Blank shortcut",
            """
            #name "Új parancs"
            #icon color=blue glyph=59511

            """,
            """
            #name "New shortcut"
            #icon color=blue glyph=59511

            """),

        new("Üdvözlés névvel", "Greeting by name",
            """
            #name "Üdvözlés"
            #icon color=blue glyph=59511

            // Megkérdezi a nevet és értesítést küld
            name = AskForInput(prompt: "Hogy hívnak?", type: "Text")
            greeting = Text("Szia, {name}! Jó, hogy itt vagy.")
            if name hasValue {
                Notification(greeting, title: "Üdvözlés")
            } else {
                Alert("Nem adtál meg nevet.", title: "Hoppá")
            }
            """,
            """
            #name "Greeting"
            #icon color=blue glyph=59511

            // Asks for a name and sends a notification
            name = AskForInput(prompt: "What's your name?", type: "Text")
            greeting = Text("Hi, {name}! Nice to see you.")
            if name hasValue {
                Notification(greeting, title: "Greeting")
            } else {
                Alert("You didn't enter a name.", title: "Oops")
            }
            """),

        new("Időjárás JSON API-ból", "Weather from a JSON API",
            """
            #name "Időjárás most"
            #icon color=teal glyph=59511

            // Letölt egy JSON választ, és kiolvas belőle egy értéket
            response = GetContentsOfUrl("https://api.open-meteo.com/v1/forecast?latitude=47.5&longitude=19.04&current_weather=true", method: "GET")
            weather = GetDictionaryValue(response, get: "Value", key: "current_weather")
            ShowResult("Hőmérséklet Budapesten: {weather.as(Dictionary)["temperature"]} °C")
            """,
            """
            #name "Weather now"
            #icon color=teal glyph=59511

            // Downloads a JSON response and reads a value from it
            response = GetContentsOfUrl("https://api.open-meteo.com/v1/forecast?latitude=51.5&longitude=-0.12&current_weather=true", method: "GET")
            weather = GetDictionaryValue(response, get: "Value", key: "current_weather")
            ShowResult("Temperature in London: {weather.as(Dictionary)["temperature"]} °C")
            """),

        new("Gyors menü", "Quick menu",
            """
            #name "Gyors menü"
            #icon color=orange glyph=59511

            menu "Mit csináljunk?" {
                case "Zseblámpa" {
                    Flashlight(state: "Toggle")
                }
                case "Vágólap megjelenítése" {
                    clip = GetClipboard()
                    ShowResult(clip)
                }
                case "Akkumulátor" {
                    battery = GetBatteryLevel()
                    Alert("Töltöttség: {battery}%")
                }
            }
            """,
            """
            #name "Quick menu"
            #icon color=orange glyph=59511

            menu "What should we do?" {
                case "Flashlight" {
                    Flashlight(state: "Toggle")
                }
                case "Show clipboard" {
                    clip = GetClipboard()
                    ShowResult(clip)
                }
                case "Battery" {
                    battery = GetBatteryLevel()
                    Alert("Battery: {battery}%")
                }
            }
            """),

        new("Listafeldolgozás", "Process a list",
            """
            #name "Bevásárlólista"
            #icon color=green glyph=59511

            // Minden elemet nagybetűvel kezd, majd soronként kiírja
            items = List(["tej", "kenyér", "tojás", "alma"])
            foreach items {
                item = ChangeCase(RepeatItem, case: "Capitalize Every Word")
                var result += item
            }
            joined = CombineText(result, separator: "New Lines")
            ShowResult(joined)
            """,
            """
            #name "Shopping list"
            #icon color=green glyph=59511

            // Capitalizes every item, then shows them line by line
            items = List(["milk", "bread", "eggs", "apples"])
            foreach items {
                item = ChangeCase(RepeatItem, case: "Capitalize Every Word")
                var result += item
            }
            joined = CombineText(result, separator: "New Lines")
            ShowResult(joined)
            """),

        new("Akkumulátor-figyelő", "Battery watcher",
            """
            #name "Akkumulátor"
            #icon color=red glyph=59511
            #type NCWidget

            level = GetBatteryLevel()
            if level < 20 {
                Notification("Csak {level}% maradt, töltsd fel!", title: "Alacsony töltöttség", sound: true)
                SetLowPowerMode(on: true)
            } else {
                ShowResult("Töltöttség: {level}%")
            }
            """,
            """
            #name "Battery"
            #icon color=red glyph=59511
            #type NCWidget

            level = GetBatteryLevel()
            if level < 20 {
                Notification("Only {level}% left, please charge!", title: "Low battery", sound: true)
                SetLowPowerMode(on: true)
            } else {
                ShowResult("Battery: {level}%")
            }
            """),

        new("Link megnyitása olvasó módban (megosztási lap)", "Open link in Reader (share sheet)",
            """
            #name "Olvasó mód"
            #icon color=darkblue glyph=59511
            #input WFURLContentItem, WFSafariWebPageContentItem
            #type ActionExtension

            // A megosztási lapról kapott linket olvasó módban nyitja meg
            ShowWebPage(ShortcutInput, reader: true)
            """,
            """
            #name "Reader mode"
            #icon color=darkblue glyph=59511
            #input WFURLContentItem, WFSafariWebPageContentItem
            #type ActionExtension

            // Opens the link received from the share sheet in Reader mode
            ShowWebPage(ShortcutInput, reader: true)
            """),
    ];
}
