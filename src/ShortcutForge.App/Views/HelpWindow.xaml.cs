using System.Windows;
using ShortcutForge.App.Editor;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.App.Views;

public partial class HelpWindow : Window
{
    private const string Help = """
        // ===== Fejléc (direktívák) =====
        #name "Parancs neve"
        #icon color=blue glyph=59511        // színek: red darkorange orange gold yellow green teal blue darkblue violet purple pink gray slate
        #input WFStringContentItem, WFURLContentItem   // elfogadott bemenetek
        #type NCWidget, ActionExtension                // widget, megosztási lap, ...

        // ===== Akciók =====
        // Egy sor = egy akció. A név a bal oldali listában látható (pl. Alert, Text, GetContentsOfUrl).
        Alert("Üzenet", title: "Cím")        // az első paraméter megadható név nélkül
        Vibrate()

        // Egy akció kimenete elnevezhető, és később változóként használható:
        greeting = Text("Szia!")
        Notification(greeting, title: "Hello")

        // Szövegben a változók kapcsos zárójelbe kerülnek:
        ShowResult("Üdv, {greeting} – bemenet: {ShortcutInput}")
        // Kapcsos zárójel szövegként: \{ és \}   Idézőjel: \"   Új sor: \n

        // ===== Beépített változók =====
        // ShortcutInput  Clipboard  CurrentDate  Ask  DeviceDetails  RepeatItem  RepeatIndex

        // ===== Elnevezett változók (Set Variable / Add to Variable) =====
        var counter = 0
        var items += "új elem"               // hozzáfűzés
        ShowResult("{counter}")
        // Szóközös név: var("Saját változó")

        // ===== Változó típusa, tulajdonsága, szótár kulcsa =====
        // (mint amikor a Parancsok appban rákoppintasz egy változóra)
        szam = AskForInput(prompt: "Szám?")
        ShowResult("{szam.as(Number)}")
        f = GetFile()
        ShowResult("Méret: {f.get("File Size")}")
        d = GetContentsOfUrl("https://example.com/api")
        Alert(d.as(Dictionary)["name"])
        // Típusok: Text Number Boolean Dictionary Date URL RichText File Image PDF Media
        //          Contact Location Email Phone Article SafariWebPage App
        // Vizuális szerkesztőben: a változómező melletti ⋯ gomb, szövegben a {x} gomb.
        // Kimenet átnevezése: kattints a kártyán a →név jelvényre.

        // ===== Feltétel =====
        if greeting contains "Szia" {
            Alert("benne van")
        } else {
            Alert("nincs benne")
        }
        // Operátorok: ==  !=  <  <=  >  >=  contains  !contains  beginsWith  endsWith
        //             hasValue  !hasValue  between 1 and 10
        // A feltétel eredménye is elnevezhető:  if ... { ... } -> eredmeny

        // ===== Ismétlés =====
        repeat 3 {
            ShowResult("{RepeatIndex}. kör")
        }
        list = List(["alma", "körte", "szilva"])
        foreach list {
            ShowResult(RepeatItem)
        }

        // ===== Menü =====
        menu "Mit szeretnél?" {
            case "Kávé" {
                Alert("☕")
            }
            case "Tea" {
                Alert("🍵")
            }
        } -> valasztas

        // ===== Értékek =====
        // szám: 42  3.14  -1      logikai: true false
        // szöveg: "..."           lista: ["a", "b", 3]
        // szótár: { "kulcs": "érték", "szám": 5, "igaz": true, "belső": { "a": "b" } }
        GetContentsOfUrl("https://example.com/api", method: "POST", bodyType: "JSON",
                         json: { "name": greeting, "count": 3 })

        // ===== Bármilyen (akár külső app) akció =====
        // Ismeretlen akciók az azonosítójukkal adhatók meg, a paraméterek nyers plist kulcsokkal:
        action "com.example.app.SomeIntent" (Parameter: "érték", Count: 3)

        // ===== Haladó / nyers formák (importált parancsoknál jelenhetnek meg) =====
        // text("...")            szöveges token (WFTextTokenString)
        // fields({...})          Dictionary akció mező formátum
        // wrap("Típus", érték)   egyéb WFSerializationType burok
        // raw(érték)             típusfüggetlen nyers érték
        // data("base64")  date("2024-01-01T00:00:00Z")
        // output("UUID", "Név")  hivatkozás egy akció kimenetére azonosító alapján
        // global("Típus")        egyéb speciális változó
        // valtozo.with({ "Aggrandizements": [...] })   tulajdonság / típuskényszerítés
        // Blokkok extra paraméterei:  if x hasValue with (Kulcs: érték) { ... }
        """;

    private const string HelpEn = """
        // ===== Header (directives) =====
        #name "Shortcut name"
        #icon color=blue glyph=59511        // colors: red darkorange orange gold yellow green teal blue darkblue violet purple pink gray slate
        #input WFStringContentItem, WFURLContentItem   // accepted input types
        #type NCWidget, ActionExtension                // widget, share sheet, ...

        // ===== Actions =====
        // One line = one action. The names are shown in the list on the left (e.g. Alert, Text, GetContentsOfUrl).
        Alert("Message", title: "Title")      // the first parameter can be given without a name
        Vibrate()

        // An action's output can be named and used later as a variable:
        greeting = Text("Hi!")
        Notification(greeting, title: "Hello")

        // In text, variables go inside braces:
        ShowResult("Welcome, {greeting} – input: {ShortcutInput}")
        // Literal braces: \{ and \}   Quote: \"   New line: \n

        // ===== Built-in variables =====
        // ShortcutInput  Clipboard  CurrentDate  Ask  DeviceDetails  RepeatItem  RepeatIndex

        // ===== Named variables (Set Variable / Add to Variable) =====
        var counter = 0
        var items += "new item"              // append
        ShowResult("{counter}")
        // Names with spaces: var("My variable")

        // ===== Variable type, property, dictionary key =====
        // (like tapping a variable in the Shortcuts app)
        num = AskForInput(prompt: "Number?")
        ShowResult("{num.as(Number)}")
        f = GetFile()
        ShowResult("Size: {f.get("File Size")}")
        d = GetContentsOfUrl("https://example.com/api")
        Alert(d.as(Dictionary)["name"])
        // Types: Text Number Boolean Dictionary Date URL RichText File Image PDF Media
        //        Contact Location Email Phone Article SafariWebPage App
        // In the visual editor: the ⋯ button next to variable fields, {x} in text fields.
        // Rename an output: click the →name badge on the card.

        // ===== Conditions =====
        if greeting contains "Hi" {
            Alert("found")
        } else {
            Alert("not found")
        }
        // Operators: ==  !=  <  <=  >  >=  contains  !contains  beginsWith  endsWith
        //            hasValue  !hasValue  between 1 and 10
        // The result of an If can be named too:  if ... { ... } -> result

        // ===== Repeat =====
        repeat 3 {
            ShowResult("Round {RepeatIndex}")
        }
        list = List(["apple", "pear", "plum"])
        foreach list {
            ShowResult(RepeatItem)
        }

        // ===== Menu =====
        menu "What would you like?" {
            case "Coffee" {
                Alert("☕")
            }
            case "Tea" {
                Alert("🍵")
            }
        } -> choice

        // ===== Values =====
        // number: 42  3.14  -1      boolean: true false
        // text: "..."               list: ["a", "b", 3]
        // dictionary: { "key": "value", "number": 5, "flag": true, "inner": { "a": "b" } }
        GetContentsOfUrl("https://example.com/api", method: "POST", bodyType: "JSON",
                         json: { "name": greeting, "count": 3 })

        // ===== Any action (including third-party apps) =====
        // Unknown actions are written with their identifier and raw plist keys:
        action "com.example.app.SomeIntent" (Parameter: "value", Count: 3)

        // ===== Advanced / raw forms (may appear in imported shortcuts) =====
        // text("...")            text token (WFTextTokenString)
        // fields({...})          Dictionary action field format
        // wrap("Type", value)    any other WFSerializationType wrapper
        // raw(value)             kind-independent raw value
        // data("base64")  date("2024-01-01T00:00:00Z")
        // output("UUID", "Name") reference to an action output by identifier
        // global("Type")         other special variable
        // variable.with({ "Aggrandizements": [...] })   property access / type coercion
        // Extra parameters of blocks:  if x hasValue with (Key: value) { ... }
        """;

    public HelpWindow()
    {
        InitializeComponent();
        DslEditorSupport.Attach(HelpText, () => []);
        ShowText();
        EventHandler onLanguage = (_, _) => ShowText();
        L.Changed += onLanguage;
        Closed += (_, _) => L.Changed -= onLanguage;
    }

    private void ShowText() => HelpText.Text = L.IsEnglish ? HelpEn : Help;
}
