# Microsoft Store listing – English (en-us)

Text for Partner Center (*Store listings › English (United States)*). The Store shows plain text, so the long texts are in code blocks: copy the contents as they are.

- Product name: **ShortcutForge**
- Privacy policy URL: https://github.com/kipergrof/ShortcutForge/blob/main/PRIVACY.md
- Website: https://github.com/kipergrof/ShortcutForge
- Support contact: https://github.com/kipergrof/ShortcutForge/issues
- Category: Developer tools (alternative: Productivity)

## Short description

Max 100 characters (this one: 96).

```text
Create and edit Apple Shortcuts on Windows: visual editor, code view, free signing, QR transfer.
```

## Description

Max 10,000 characters (this one: about 2,600).

```text
ShortcutForge lets you build shortcuts for Apple's Shortcuts app (iPhone, iPad, Mac) on your Windows PC. Build them visually with action cards, much like in the Shortcuts app, or write them as code. Open existing shortcuts, edit them, and send them straight to your iPhone.

WHAT YOU CAN DO
• Visual editor: action cards with drag and drop, blocks (If / Otherwise, Repeat, Repeat with Each, Choose from Menu), a condition editor, collapsible cards, and undo / redo.
• Variables like on the iPhone: use any action's output or a named variable, get it as a type, read a property or a dictionary key, and rename outputs.
• Text view: the same shortcut as code, with syntax highlighting, completion, folding and inline error markers. It stays in sync with the visual editor.
• 194 built-in actions in categories. Any other action, including third-party app actions, is kept and stays editable, so nothing is lost when you import a shortcut.
• Open signed and unsigned .shortcut files, plist files and iCloud share links.
• Free online signing (Shortcuty), or sign on your own Mac over SSH or with a signing server.
• Send to iPhone with a QR code: scan it with the iPhone camera on the same Wi-Fi and the shortcut downloads straight into Shortcuts. No AirDrop, cable or cloud needed.
• Command palette (Ctrl+K): type a few letters to run any command, open a template or add an action.
• Find and replace (Ctrl+H) in the visual editor, including renaming a variable everywhere.
• Templates and an online template gallery that keeps growing without app updates.
• Dry run (F5): run the shortcut step by step on Windows and see what each action produces. Your files, the network and the clipboard are never touched.
• Shortcut from a description: describe what you want in plain words, in any language, and Claude writes the shortcut for you to review. This needs your own Claude API key from Anthropic.
• Light and dark theme (follows Windows), English and Hungarian interface.

GOOD TO KNOW
Since iOS 15, iPhone, iPad and Mac only import shortcuts that are signed by Apple, and Apple's signing tool only runs on a Mac. ShortcutForge handles this for you with free online signing by Shortcuty: turn it on once in Tools › Settings, and every export and QR transfer is signed automatically. Online signing sends the shortcut to Shortcuty's server, so for shortcuts with passwords or personal data use your own Mac or signing server instead.

No account, no ads, no telemetry. ShortcutForge is free and open source (MIT license).

ShortcutForge is not affiliated with Apple. Apple, iPhone, iPad, Mac and Shortcuts are trademarks of Apple Inc.
```

## What's new in this version

For 1.1.x. Max 1,500 characters.

```text
• Send to iPhone with a QR code (Ctrl+Shift+E): scan it with the iPhone camera and the shortcut downloads straight into Shortcuts.
• Command palette (Ctrl+K): run any command, open a template or add an action with a few keystrokes.
• Find and replace (Ctrl+H) in the visual editor, and rename a variable everywhere.
• Online template gallery with 10 new templates, such as pomodoro, translate, resize photos and QR code.
• Shortcut from a description: Claude writes a shortcut from plain words (needs your own Claude API key).
• Dry run (F5): run a shortcut step by step on Windows and see what each action produces.
```

## Search keywords

Up to 7.

1. Apple Shortcuts
2. Shortcuts editor
3. iPhone shortcuts
4. iOS automation
5. shortcut file
6. Siri shortcuts
7. automation

## Product features

Up to 20, max 200 characters each.

1. Build Apple Shortcuts on Windows with action cards, much like in the Shortcuts app
2. Drag and drop, If / Otherwise, Repeat, Repeat with Each and Choose from Menu blocks
3. Text view: write shortcuts as code with syntax highlighting, completion and error markers
4. The visual editor and the text view stay in sync
5. 194 built-in actions in categories; other actions are kept and stay editable
6. Variables like on the iPhone: get as a type, read a property or a dictionary key, rename outputs
7. Open signed and unsigned .shortcut files, plist files and iCloud share links
8. Free online signing with Shortcuty, so exported shortcuts import on iPhone, iPad and Mac
9. Also signs on your own Mac over SSH or with a signing server
10. Send to iPhone with a QR code over your local Wi-Fi, without AirDrop, cable or cloud
11. Command palette (Ctrl+K) with typo-tolerant search for commands, templates and actions
12. Find and replace, including renaming a variable everywhere
13. Built-in templates and an online template gallery that grows without app updates
14. Dry run: run a shortcut step by step on Windows and see every action's result
15. Shortcut from a plain-language description with Claude, using your own API key
16. Undo / redo and collapsible cards
17. Light and dark theme that follows Windows
18. English and Hungarian interface
19. No account, no ads, no telemetry
20. Free and open source (MIT)

## Screenshots to take

Take them on Windows at 1920×1080 (the Store accepts 1366×768 or larger, PNG). Use a clean, realistic example shortcut such as "Good morning" or one from the gallery. Captions can be added in Partner Center.

1. **Visual editor** (light theme): a shortcut with an If / Otherwise block and a Repeat block, the action list on the left. Caption: "Build shortcuts with action cards".
2. **Text view**: the same shortcut as code, with syntax highlighting and the completion list open. Caption: "Or write them as code".
3. **Send to iPhone**: the QR code window. Caption: "Send to your iPhone with a QR code".
4. **Command palette** (Ctrl+K) open with a few letters typed. Caption: "Everything is a few keystrokes away".
5. **Template gallery** window with the template list. Caption: "Start from a template".
6. **Dry run** (F5) window partway through a shortcut. Caption: "Test it step by step on Windows".
7. **Shortcut from a description** window with an example description and the generated result. Caption: "Describe it, and Claude writes it".
8. **Dark theme**: the visual editor in dark mode. Caption: "Light and dark theme".
9. Optional: **Settings** with "Free online signing – Shortcuty" selected. Caption: "Free signing, no Mac needed".

Do not show a real API key, Mac password or personal data in any screenshot.

## Notes for certification

Paste into *Submission options › Notes for certification*.

```text
ShortcutForge is a classic WPF desktop app (.NET 10) packaged as MSIX with the Desktop Bridge.

runFullTrust: required because the app is a full-trust Win32/WPF desktop application, not a UWP app. It reads and writes files the user opens or saves, and stores its settings in %APPDATA%\ShortcutForge.

privateNetworkClientServer: used only by the "Send to iPhone" feature (File menu, Ctrl+Shift+E). While that window is open, the app runs a small HTTP server on the local network that serves the one selected shortcut at a random, unguessable URL, shown as a QR code, so an iPhone on the same Wi-Fi can download it. The server stops when the window is closed. Windows Firewall may ask to allow it on private networks.

internetClient: used only for features the user starts: free online signing (sign.shortcuty.app), importing from an iCloud link, the online template gallery (raw.githubusercontent.com) and the optional AI generation with the user's own Claude API key (Anthropic API). The GitHub update check is disabled in the Store version.

How to test: open the app, pick File › New from template, and you have a sample shortcut in the visual editor. Switch to the text view to see it as code. Press F5 for a dry run. No account or sign-in is needed. The AI feature needs a Claude API key and can be skipped.

No telemetry, no accounts, no ads. Privacy policy: https://github.com/kipergrof/ShortcutForge/blob/main/PRIVACY.md

ShortcutForge is not affiliated with Apple. Apple, iPhone, iPad, Mac and Shortcuts are trademarks of Apple Inc.
```
