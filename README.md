# ShortcutForge

Apple Parancsok (Shortcuts) készítése és szerkesztése **Windowson**.
*Create and edit Apple Shortcuts on Windows — English summary at the end.*

## Mit tud?

- **Vizuális szerkesztő**: akciókártyák a Parancsok apphoz hasonlóan, húzással rendezhetők. Vannak benne blokkok (Ha / Egyébként, Ismétlés, Menü), változóválasztó, feltételszerkesztő és visszavonás.
- **Változók részletei**: mint a Parancsok appban, a változó lekérhető típusként (`.as(Number)`), kérhető a tulajdonsága (`.get("File Size")`) vagy szótár-kulcsa (`["név"]`). A kimenetek átnevezhetők.
- **Szöveges nézet (DSL)**: ugyanaz a parancs programkódként. Van benne szintaxiskiemelés, automatikus kiegészítés (Ctrl+Szóköz) és hibajelzés a hiba helyén. Fülváltáskor a két nézet szinkronizálódik.
- **194 beépített akció** kategóriákba rendezve. Bármely más akció, például külső appok App Intentjei, általános blokként szerkeszthető, így importáláskor semmi sem vész el.
- **Megnyitás / import**: `.shortcut` (aláírt és aláíratlan), bináris és XML plist, iCloud megosztási link.
- **Export**: `.shortcut` fájl, opcionálisan **Macen, SSH-n keresztül automatikusan aláírva** (`shortcuts sign`).
- **Sablonok** (*Fájl › Új sablonból*): üdvözlés, JSON API lekérés, gyors menü, listafeldolgozás, akkumulátor-figyelő, megosztási lapos link-megnyitó.
- **Kényelem**: kategóriaikonok, összecsukható kártyák (egyenként vagy *Nézet › Összes kártya összecsukása*), vezetővonalak a beágyazott blokkoknál, Ctrl+F keresés (Enter: az első találat hozzáadása), összecsukható `{ }` blokkok a szöveges nézetben.
- **Sötét / világos téma** (a Windows beállítását követi), **magyar / angol** felület.

## Aláírás – fontos

iOS 15 óta iPhone-ra, iPadre és Macre csak **Apple által aláírt** parancs importálható. Aláírni csak macOS-en lehet:

```
shortcuts sign --mode anyone --input parancs.shortcut --output alairt.shortcut
```

Ha van elérhető Mac (saját vagy bérelt felhős Mac), az *Eszközök › Beállítások* menüben megadható SSH-n. Ekkor exportáláskor az alkalmazás automatikusan aláírja a fájlt. A Macen be kell kapcsolni a *Távoli bejelentkezést*, a „Bárki” módhoz pedig iCloud-fiók kell.

## A szöveges nyelv röviden

```
#name "Reggeli"
#icon color=blue glyph=59511

greeting = Text("Jó reggelt, {ShortcutInput}!")
if greeting contains "reggel" {
    Notification(greeting, title: "Hello")
} else {
    Alert("Nincs reggel")
}
fruits = List(["alma", "körte"])
foreach fruits {
    ShowResult(RepeatItem)
}
action "com.example.app.SomeIntent" (Param: "érték")

data = GetContentsOfUrl("https://example.com/api")
Alert(data.as(Dictionary)["name"])      // típus + szótár kulcs
```

A teljes leírás az alkalmazásban található: *Súgó › Szöveges nyelv (DSL) súgó*, vagy F1.

## Fordítás és futtatás

Szükséges: .NET 10 SDK (a könyvtárak .NET 8-at céloznak, az alkalmazás .NET 10-et a Fluent sötét téma miatt).

```
dotnet build
dotnet test
dotnet run --project src/ShortcutForge.App
```

## Felépítés

| Projekt | Tartalom |
|---|---|
| `ShortcutForge.Core` | Modell, plist olvasás/írás, akciókatalógus (`Catalog/Data/*.json`), import (aláírt AEA fájlok, LZFSE, iCloud), lokalizáció |
| `ShortcutForge.Dsl` | Lexer, parser, pretty-printer – veszteségmentes oda-vissza alakítás |
| `ShortcutForge.Signing` | `ISigner`: aláíratlan export és Mac SSH aláírás |
| `ShortcutForge.App` | WPF felület (MVVM, AvalonEdit) |
| `tests/*` | xUnit tesztek (plist és DSL oda-vissza alakítás, LZFSE referencia-vektorok, ViewModel) |

Új akció felvétele: egy bejegyzés a megfelelő `src/ShortcutForge.Core/Catalog/Data/*.json` fájlba (azonosító, név, DSL név, paraméterek).

## Ismert korlátok

- Az aláírt fájlok importja szintetikus tesztfájllal van ellenőrizve. Valódi, iPhone-ról exportált fájlon még érdemes kipróbálni.
- Az iCloud-link import az iCloud nem hivatalos API-ját használja.
- Az ikonnál csak a glyph száma adható meg, a szimbólum nem jelenik meg.
- A DSL-ben adott változónevek (`x = …`) nem tárolódnak a parancsban. Visszaolvasáskor a kimenet nevéből képzett név (pl. `text`) jelenik meg.

---

## English summary

ShortcutForge is a Windows desktop app (WPF, .NET) for building Apple Shortcuts:

- a visual card-based editor, including the tapped-variable options (get as type, property, dictionary key) and renaming outputs;
- a text language (DSL) kept in sync with the visual editor;
- 194 built-in actions, plus generic support for any other action;
- import of signed and unsigned `.shortcut` files, plists and iCloud links;
- export to `.shortcut`, with optional automatic signing on a Mac over SSH;
- light/dark theme and a Hungarian/English UI (*View* menu).

Since iOS 15, shortcuts must be signed on macOS (`shortcuts sign`) before they can be imported on a device.
