# winget package

Manifests for [winget](https://learn.microsoft.com/windows/package-manager/) (`winget install kipergrof.ShortcutForge`),
in the layout used by [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs).

## First submission (once)

The package has to be accepted into `microsoft/winget-pkgs` once:

```
winget validate --manifest winget/manifests/k/kipergrof/ShortcutForge/1.0.0
wingetcreate submit winget/manifests/k/kipergrof/ShortcutForge/1.0.0 --token <github-token>
```

This opens a pull request in `microsoft/winget-pkgs` from a fork in your account.
Microsoft's automated checks and a moderator review it, usually within a few days.

## Later releases (automatic)

Save a classic GitHub token with the `public_repo` scope as the repository secret
`WINGET_TOKEN`. The *Release* workflow then runs `wingetcreate update` after each
release and opens the update pull request in `microsoft/winget-pkgs` by itself.
Without the secret this step is skipped.
