# Template gallery

Templates shown in ShortcutForge under *File › Template gallery*. The app downloads
`index.json` and the `.sfdsl` files from this folder on GitHub, so new templates reach
users without a new release.

To add one: write `<id>.en.sfdsl` (and optionally `<id>.hu.sfdsl`) in the text language,
add an entry to `index.json`, and run `dotnet test` — a test checks that every template
parses and only uses known actions and parameters.
