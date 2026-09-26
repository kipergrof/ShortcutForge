# Changelog

All notable changes to ShortcutForge are listed here.
Versions follow [Semantic Versioning](https://semver.org/): MAJOR.MINOR.PATCH.

## Releasing a new version

1. Update `<Version>` in `Directory.Build.props` (e.g. `1.1.0`).
2. Add a section for it below.
3. Commit, then tag and push:
   ```
   git tag v1.1.0
   git push origin main v1.1.0
   ```
   The *Release* workflow checks that the tag matches `Directory.Build.props`, runs the tests,
   builds `ShortcutForge-1.1.0-win-x64.exe` and publishes the GitHub release.

## [Unreleased]

- CI: build and test on every push; automated releases from version tags
- Issue templates for bug reports and feature requests

## [1.0.0] – 2026-09-26

First public release.

- Visual card editor kept in sync with a text language (DSL)
- 194 built-in actions; any other action kept as a generic block
- Tapped-variable options (get as type, property, dictionary key), output renaming
- Open signed and unsigned `.shortcut` files, plists and iCloud links
- Export signed `.shortcut` files: free online signing (Shortcuty), Mac over SSH, signing server
- Templates, light/dark theme, English/Hungarian UI
