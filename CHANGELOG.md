# Changelog

All notable changes to ShortcutForge. Versions follow [Semantic Versioning](https://semver.org/) (MAJOR.MINOR.PATCH).

New entries are written automatically by the *Release* workflow (GitHub › Actions › Release › Run workflow)
from the commit messages since the previous release — no manual editing needed.

<!-- releases -->

## [1.1.1] – 2026-09-26

- Microsoft Store listing and privacy policy (#13)
- Fix Send to iPhone: landing page and firewall help (#12)
- Release: skip the winget update until the package is in the winget catalog (#11)

## [1.1.0] – 2026-09-26

- Dry run: run a shortcut step by step on Windows (#10)
- Generate a shortcut from a plain-language description with Claude (#8)
- Online template gallery with 10 new templates (#7)
- Command palette (Ctrl+K) (#6)
- Find and replace (#5)
- winget package (#4)
- Check for updates on GitHub (#3)
- Send to iPhone with a QR code (#2)

## [1.0.0] – 2026-09-26

First public release.

- Visual card editor kept in sync with a text language (DSL)
- 194 built-in actions; any other action kept as a generic block
- Tapped-variable options (get as type, property, dictionary key), output renaming
- Open signed and unsigned `.shortcut` files, plists and iCloud links
- Export signed `.shortcut` files: free online signing (Shortcuty), Mac over SSH, signing server
- Templates, light/dark theme, English/Hungarian UI
