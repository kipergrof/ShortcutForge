# Privacy Policy

*[Magyar változat lent](#adatvédelmi-tájékoztató)*

**Effective date: 2026-09-26**

This policy covers ShortcutForge, a Windows desktop app for creating and editing Apple Shortcuts, in every form it is distributed: the Microsoft Store version, the `.exe` from GitHub Releases, the winget package, and builds from source. ShortcutForge is made by Krisztián Szilvágyi ([@kipergrof](https://github.com/kipergrof)) and is open source, so you can check everything below in the [source code](https://github.com/kipergrof/ShortcutForge).

## In short

- **No telemetry, no analytics, no crash reporting.** The app does not collect usage data and does not "phone home".
- **No accounts and no ads.**
- **Your settings stay on your PC.** The author never receives them.
- **The app only goes online when you use a feature that needs it.** Each such feature is listed below with what it sends and where.

## Data stored on your PC

ShortcutForge saves its settings in `%APPDATA%\ShortcutForge\settings.json`. This file contains:

- your preferences (theme, language, signing method, update-check setting),
- the list of recently opened files (their paths on your PC),
- the connection details you enter for signing: signing server URL, Mac host name, port, user name and the path of your SSH key file,
- your Mac password and your Claude API key, if you enter them. These two secrets are **encrypted with Windows DPAPI** for your Windows user account, so they are not stored in plain text and cannot be decrypted by another user.

The shortcuts you create are saved only where you choose to save them. To remove all app data, delete the `%APPDATA%\ShortcutForge` folder (uninstalling the Store version also removes the app's own package data).

## When the app uses the network

None of the connections below happen in the background without a reason. Each one starts only when you use the related feature, and the data goes straight from your PC to the service named. The author of ShortcutForge does not run any of these services and receives none of this data.

| Feature | When | What is sent | Where |
|---|---|---|---|
| **Free online signing (Shortcuty)** | When you export or send a shortcut with "Free online signing – Shortcuty" selected | The full shortcut file | `sign.shortcuty.app` ([Shortcuty](https://github.com/Shortcuty/Signing-Server-API-Documentation), a third-party service) |
| **Shortcut from a description (AI)** | When you click Generate | Your description, together with the app's fixed instructions (the text language reference and the action list). If the result needs fixing, a second automatic request sends the generated code. Your open shortcut and your files are not sent. Authenticated with **your own** Claude API key | The Anthropic Claude API (`api.anthropic.com`) |
| **Template gallery** | When you open *File › Template gallery* or pick a template | Nothing but a normal download request | `raw.githubusercontent.com` (GitHub) |
| **iCloud link import** | When you import a shortcut from an iCloud link | The shortcut ID from the link you pasted | Apple's iCloud web API (`www.icloud.com`) |
| **Update check** | At most once a day at startup, and when you choose *Help › Check for updates*. It can be turned off in *Tools › Settings*. **Not present in the Microsoft Store version**, which is updated by the Store | Nothing but a normal request for the latest release | The GitHub releases API (`api.github.com`) |
| **Signing on your Mac over SSH** | When you export with this method | The shortcut file, and your Mac user name and password or SSH key for logging in | The Mac you configure |
| **Signing server** | When you export with this method | The full shortcut file | The server URL you configure |
| **Send to iPhone (QR code)** | While the Send to iPhone window is open | The app runs a small web server on your **local network only**. It serves the one shortcut at a random, unguessable address and stops when you close the window | Devices on your local network that open that address (normally your iPhone after scanning the QR code) |

Requests to online services identify the app with a `ShortcutForge/<version>` user agent, and, like any internet connection, they reveal your IP address to the service you connect to. Send to iPhone signs the shortcut first with the signing method you selected, so the matching row above applies to that step too.

**Please note:** online signing sends the whole shortcut to a third party. Do not sign shortcuts that contain passwords, API keys or personal data that way. Use your own Mac or signing server for those. What happens to data you send to Shortcuty, Anthropic, GitHub or Apple is governed by their own privacy policies.

Links in the app, for example to the RoutineHub Shortcut Source Helper or to the GitHub page, open in your web browser.

## Children

ShortcutForge does not knowingly collect any personal data from anyone, including children.

## Changes to this policy

If the app's data handling changes, this file will be updated in the repository and the effective date above will change. The history of the file is public on GitHub.

## Contact

Questions or concerns: open an issue at <https://github.com/kipergrof/ShortcutForge/issues>.

---

# Adatvédelmi tájékoztató

**Hatályos: 2026. szeptember 26-tól**

Ez a tájékoztató a ShortcutForge-ra vonatkozik. A ShortcutForge Windowsos asztali alkalmazás Apple Parancsok (Shortcuts) készítéséhez és szerkesztéséhez, és a tájékoztató minden terjesztési formájára érvényes: a Microsoft Store-verzióra, a GitHub Releases oldalról letölthető `.exe`-re, a winget-csomagra és a forráskódból fordított változatra is. A ShortcutForge-ot Szilvágyi Krisztián ([@kipergrof](https://github.com/kipergrof)) készíti. Nyílt forráskódú, így minden alábbi állítás ellenőrizhető a [forráskódban](https://github.com/kipergrof/ShortcutForge).

## Röviden

- **Nincs telemetria, analitika vagy hibajelentés-küldés.** Az alkalmazás nem gyűjt használati adatokat, és nem „telefonál haza”.
- **Nincs fiók és nincs reklám.**
- **A beállításaid a gépeden maradnak.** A fejlesztőhöz nem jutnak el.
- **Az alkalmazás csak akkor használ internetet, ha olyan funkciót indítasz, amelyhez ez kell.** Az ilyen funkciókat lent sorra vesszük: mit küldenek és hová.

## A gépeden tárolt adatok

A ShortcutForge a beállításait a `%APPDATA%\ShortcutForge\settings.json` fájlba menti. Ebben a következők vannak:

- a beállításaid (téma, nyelv, aláírási mód, frissítéskeresés),
- a legutóbb megnyitott fájlok listája (az elérési útjuk a gépeden),
- az aláíráshoz megadott kapcsolódási adatok: az aláíró szerver címe, a Mac gépneve, portja, a felhasználónév és az SSH-kulcsfájl elérési útja,
- a Mac jelszava és a Claude API-kulcs, ha megadod őket. Ezt a két titkos adatot a Windows **DPAPI titkosítással** védi a Windows-felhasználói fiókodhoz kötve, így nem egyszerű szövegként tárolódnak, és más felhasználó nem tudja visszafejteni őket.

Az elkészített parancsok csak oda kerülnek, ahová te mented őket. Az alkalmazás összes adatát a `%APPDATA%\ShortcutForge` mappa törlésével távolíthatod el (a Store-verzió eltávolításakor a csomag saját adatai is törlődnek).

## Mikor használ hálózatot az alkalmazás?

Az alábbi kapcsolatok egyike sem indul el ok nélkül a háttérben. Mindegyik csak akkor jön létre, amikor a hozzá tartozó funkciót használod, és az adat közvetlenül a gépedről a megnevezett szolgáltatáshoz kerül. A ShortcutForge fejlesztője egyiket sem üzemelteti, és ezekből az adatokból semmit sem kap meg.

| Funkció | Mikor? | Mit küld? | Hová? |
|---|---|---|---|
| **Ingyenes online aláírás (Shortcuty)** | Exportáláskor vagy iPhone-ra küldéskor, ha az „Ingyenes online aláírás – Shortcuty” mód van kiválasztva | A teljes parancsfájlt | `sign.shortcuty.app` ([Shortcuty](https://github.com/Shortcuty/Signing-Server-API-Documentation), külső szolgáltatás) |
| **Parancs leírásból (AI)** | Amikor a Létrehozás gombra kattintasz | A leírásodat az alkalmazás rögzített utasításaival együtt (a szöveges nyelv leírása és az akciólista). Ha az eredményt javítani kell, egy második, automatikus kérés a generált kódot küldi el. A megnyitott parancsodat és a fájljaidat nem küldi el. A hitelesítés a **saját** Claude API-kulcsoddal történik | Az Anthropic Claude API (`api.anthropic.com`) |
| **Sablongaléria** | Amikor megnyitod a *Fájl › Sablongaléria* ablakot vagy választasz egy sablont | Csak egy szokásos letöltési kérést | `raw.githubusercontent.com` (GitHub) |
| **Import iCloud-linkről** | Amikor iCloud-linkről importálsz egy parancsot | A beillesztett linkben szereplő parancsazonosítót | Az Apple iCloud webes API-ja (`www.icloud.com`) |
| **Frissítéskeresés** | Indításkor legfeljebb naponta egyszer, illetve a *Súgó › Frissítések keresése* menüponttal. Az *Eszközök › Beállítások* menüben kikapcsolható. **A Microsoft Store-verzióban nincs benne**, azt a Store frissíti | Csak egy szokásos kérést a legújabb kiadásról | A GitHub kiadási API-ja (`api.github.com`) |
| **Aláírás saját Macen SSH-n** | Ha ezzel a móddal exportálsz | A parancsfájlt, valamint a bejelentkezéshez a Mac felhasználónevét és jelszavát vagy SSH-kulcsát | Az általad megadott Macre |
| **Aláíró szerver** | Ha ezzel a móddal exportálsz | A teljes parancsfájlt | Az általad megadott szervercímre |
| **Küldés iPhone-ra (QR-kód)** | Amíg a Küldés iPhone-ra ablak nyitva van | Az alkalmazás egy kis webszervert futtat, **csak a helyi hálózaton**. Ez egyetlen parancsot szolgál ki egy véletlenszerű, kitalálhatatlan címen, és az ablak bezárásakor leáll | A helyi hálózaton lévő eszközök, amelyek megnyitják ezt a címet (általában az iPhone-od a QR-kód beolvasása után) |

Az online szolgáltatások felé küldött kérések `ShortcutForge/<verzió>` felhasználói ügynökkel (user agent) azonosítják az alkalmazást, és mint minden internetkapcsolatnál, a szolgáltatás látja az IP-címedet. A Küldés iPhone-ra előbb aláírja a parancsot a kiválasztott aláírási móddal, így erre a lépésre a táblázat megfelelő sora is vonatkozik.

**Fontos:** az online aláírás a teljes parancsot egy külső szolgáltatónak küldi el. Jelszót, API-kulcsot vagy személyes adatot tartalmazó parancsot ne írass alá így, ezekhez használd a saját Macedet vagy aláíró szerveredet. A Shortcutynak, az Anthropicnak, a GitHubnak vagy az Apple-nek küldött adatok kezelésére az ő saját adatvédelmi szabályzatuk vonatkozik.

Az alkalmazásban lévő linkek (például a RoutineHub Shortcut Source Helper vagy a GitHub-oldal) a webböngésződben nyílnak meg.

## Gyermekek

A ShortcutForge senkitől, így gyermekektől sem gyűjt tudatosan személyes adatot.

## A tájékoztató módosítása

Ha az alkalmazás adatkezelése megváltozik, ez a fájl frissül a tárolóban, és a fenti hatálybalépési dátum is módosul. A fájl előzményei nyilvánosak a GitHubon.

## Kapcsolat

Kérdés vagy észrevétel esetén nyiss egy hibajegyet (issue-t): <https://github.com/kipergrof/ShortcutForge/issues>.
