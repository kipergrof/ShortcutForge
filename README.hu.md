# ShortcutForge

Apple Parancsok (Shortcuts) készítése és szerkesztése **Windowson**.
*[English README](README.md)*

## Mit tud?

- **Vizuális szerkesztő**: akciókártyák a Parancsok apphoz hasonlóan, húzással rendezhetők. Vannak benne blokkok (Ha / Egyébként, Ismétlés, Menü), változóválasztó, feltételszerkesztő és visszavonás.
- **Változók részletei**: mint a Parancsok appban, a változó lekérhető típusként (`.as(Number)`), kérhető a tulajdonsága (`.get("File Size")`) vagy szótár-kulcsa (`["név"]`). A kimenetek átnevezhetők.
- **Szöveges nézet (DSL)**: ugyanaz a parancs programkódként. Van benne szintaxiskiemelés, automatikus kiegészítés (Ctrl+Szóköz) és hibajelzés a hiba helyén. Fülváltáskor a két nézet szinkronizálódik.
- **194 beépített akció** kategóriákba rendezve. Bármely más akció, például külső appok App Intentjei, általános blokként szerkeszthető, így importáláskor semmi sem vész el.
- **Megnyitás / import**: `.shortcut` (aláírt és aláíratlan), bináris és XML plist, iCloud megosztási link.
- **Export**: `.shortcut` fájl, **ingyenes online aláírással** (Shortcuty), saját Macen SSH-n keresztül vagy aláíró szerverrel.
- **Küldés iPhone-ra QR-kóddal** (Ctrl+Shift+E): aláírja a parancsot és QR-kódot mutat. Ugyanazon a Wi-Fi-n az iPhone kamerájával beolvasva egy kis letöltőoldal nyílik meg a telefonon – koppints a Letöltés gombra, és a Parancsok importálja. AirDrop, kábel vagy felhő nélkül.
- **Sablonok** (*Fájl › Új sablonból*) és **online sablongaléria** (*Fájl › Sablongaléria*), ami a GitHubon bővül, app-frissítés nélkül: pomodoro, vágólap jegyzetbe, fordítás, fotó-átméretezés, véletlen választó, QR-kód, szószámláló, éjszakai mód és még több.
- **Parancs leírásból (AI)** (*Fájl › Parancs leírásból*, Ctrl+Shift+N): írd le a saját szavaiddal, bármilyen nyelven, mit csináljon a parancs, és a Claude megírja. Megnyitás előtt átnézheted. Saját Claude API-kulcs kell hozzá (*Eszközök › Beállítások*, vagy az `ANTHROPIC_API_KEY` környezeti változó). A leírásod az Anthropichoz kerül.
- **Próbafuttatás** (*Eszközök › Próbafuttatás*, F5): a parancs lépésenként lefut Windowson, és minden akciónál látszik az eredmény. A szöveg, a számok, a változók, a listák, a szótárak, a dátumok és a Ha / Ismétlés / Menü blokkok valóban lefutnak, a kérdések és a menük ablakként jelennek meg. Az iPhone-hoz kötött hatások csak megjelennek, az eszközt igénylő akciók kimaradnak. Fájl, hálózat és a valódi vágólap érintetlen marad.
- **Kényelem**: kategóriaikonok, összecsukható kártyák (egyenként vagy *Nézet › Összes kártya összecsukása*), vezetővonalak a beágyazott blokkoknál, Ctrl+F keresés (Enter: az első találat hozzáadása), összecsukható `{ }` blokkok a szöveges nézetben.
- **Parancspaletta** (Ctrl+K): pár betűvel bármely parancs, sablon vagy akció elérhető; elgépelést tűrő, ékezetfüggetlen keresés.

- **Frissítés-értesítés**: naponta egyszer megnézi a GitHubon, van-e újabb kiadás (kikapcsolható; kézzel: *Súgó › Frissítések keresése*).
- **Sötét / világos téma** (a Windows beállítását követi), **magyar / angol** felület.

## Aláírás – fontos

iOS 15 óta iPhone-ra, iPadre és Macre csak **Apple által aláírt** parancs importálható. Aláírni csak macOS-en lehet:

```
shortcuts sign --mode anyone --input parancs.shortcut --output alairt.shortcut
```

Lehetőségek:

1. **Ingyen, online, Mac nélkül – Shortcuty (ajánlott):** az *Eszközök › Beállítások* menüben válaszd az „Ingyenes online aláírás – Shortcuty” módot. Az első exportnál az app egyszer rá is kérdez. Innentől az *Exportálás .shortcut* azonnal aláírt fájlt ment, ami AirDroppal vagy iCloud Drive-on át rögtön importálható. Ehhez a [Shortcuty nyilvános aláíró API-ját](https://github.com/Shortcuty/Signing-Server-API-Documentation) használja, „bárki” módban, API-kulcs nélkül. Figyelem: a parancs teljes tartalma a Shortcuty szerverére kerül.
2. **Ingyen, Mac nélkül (iPhone-on):** *Fájl › Exportálás iPhone-ra*. Ez egy `Név.plist` fájlt ment. Ezt a telefonon a RoutineHub ingyenes [Shortcut Source Helper](https://routinehub.co/shortcut/10060/) parancsával kell megnyitni, amely távoli aláírással (Remote Sign) aláírja és importálja. Az alkalmazás lépésről lépésre végigvezet rajta.
3. **Mac SSH-n:** az *Eszközök › Beállítások* menüben megadható egy Mac (saját vagy bérelt felhős), és az alkalmazás exportáláskor automatikusan aláír vele. A Macen be kell kapcsolni a *Távoli bejelentkezést*, a „Bárki” módhoz pedig iCloud-fiók kell.
4. **Aláíró szerver:** egy [shortcut-signing-server](https://github.com/scaxyz/shortcut-signing-server) kompatibilis URL, például saját Macen futtatva. A parancs tartalma erre a szerverre kerül.

A RoutineHub HubSign szolgáltatása csak engedélyezett klienseket fogad, ezért közvetlenül nem használható. Az alkalmazás nem álcázza magát más kliensnek.

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

## Letöltés

A legegyszerűbb: töltsd le a `ShortcutForge-<verzió>-win-x64.exe` fájlt a [legfrissebb kiadásból](https://github.com/kipergrof/ShortcutForge/releases/latest), és indítsd el. Egyetlen fájl, Windows 10/11 (x64) rendszeren .NET telepítése nélkül fut. A SmartScreen figyelmeztethet, mert a fájl nincs kódaláírva: *További információ › Futtatás mindenképp*.

**winget-tel** (miután a csomagot elfogadták a winget tárolóba):

```
winget install kipergrof.ShortcutForge
```

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
| `ShortcutForge.Signing` | `ISigner`: aláíratlan export, Shortcuty, Mac SSH, aláíró szerver |
| `ShortcutForge.App` | WPF felület (MVVM, AvalonEdit) |
| `tests/*` | xUnit tesztek (plist és DSL oda-vissza alakítás, LZFSE referencia-vektorok, valódi Apple-aláírt fájl, ViewModel, aláírók) |

Új akció felvétele: egy bejegyzés a megfelelő `src/ShortcutForge.Core/Catalog/Data/*.json` fájlba (azonosító, név, DSL név, paraméterek).

## Ismert korlátok

- Az iCloud-link import az iCloud nem hivatalos API-ját használja.
- Az ikonnál csak a glyph száma adható meg, a szimbólum nem jelenik meg.
- A DSL-ben adott változónevek (`x = …`) nem tárolódnak a parancsban. Visszaolvasáskor a kimenet nevéből képzett név (pl. `text`) jelenik meg.


## Szerző és licenc

A ShortcutForge-ot **Szilvágyi Krisztián** ([@kipergrof](https://github.com/kipergrof)) készítette.

[MIT](LICENSE). Harmadik féltől származó komponensek: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
