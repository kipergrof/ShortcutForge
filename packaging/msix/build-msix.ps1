<#
.SYNOPSIS
  Builds the Microsoft Store MSIX package of ShortcutForge.

.DESCRIPTION
  Publishes the app self-contained (not single-file: the package is the container), fills in
  AppxManifest.xml and runs makeappx. The output is unsigned: the Store signs packages on upload.
  For a local install test, pass -CertificatePfx to sign it with a test certificate.

.EXAMPLE
  ./packaging/msix/build-msix.ps1 -Output artifacts/ShortcutForge.msix
#>
param(
    [string] $Output = 'ShortcutForge.msix',
    [string] $IdentityName = 'kipergrof.ShortcutForge',
    [string] $Publisher = 'CN=4C083E5C-0222-4881-85C3-CD7C0DF59FA0',
    [string] $PublisherDisplayName = 'kipergrof',
    [string] $MakeAppx,
    [string] $SignTool,
    [string] $CertificatePfx,
    [string] $CertificatePassword
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
$work = Join-Path ([IO.Path]::GetTempPath()) "shortcutforge-msix-$([guid]::NewGuid().ToString('N'))"

function Find-SdkTool([string] $name) {
    $kits = "${env:ProgramFiles(x86)}\Windows Kits\10\bin"
    $found = Get-ChildItem $kits -Recurse -Filter $name -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $found) { throw "$name not found. Install the Windows SDK or pass -MakeAppx / -SignTool." }
    $found.FullName
}
if (-not $MakeAppx) { $MakeAppx = Find-SdkTool 'makeappx.exe' }

# Version: MSIX needs four parts and the Store requires the last one to be 0.
$props = [IO.File]::ReadAllText((Join-Path $root 'Directory.Build.props'))
$version = [regex]::Match($props, '<Version>([^<]+)</Version>').Groups[1].Value
$msixVersion = "$version.0"

try {
    dotnet publish (Join-Path $root 'src/ShortcutForge.App') -c Release -r win-x64 --self-contained `
        -o $work
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

    Copy-Item (Join-Path $PSScriptRoot 'Assets') (Join-Path $work 'Assets') -Recurse
    $manifest = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'AppxManifest.xml'))
    $manifest = $manifest.Replace('$IdentityName$', $IdentityName).Replace('$Publisher$', [Security.SecurityElement]::Escape($Publisher)).
        Replace('$PublisherDisplayName$', [Security.SecurityElement]::Escape($PublisherDisplayName)).Replace('$Version$', $msixVersion)
    [IO.File]::WriteAllText((Join-Path $work 'AppxManifest.xml'), $manifest, (New-Object Text.UTF8Encoding($false)))

    $outPath = [IO.Path]::GetFullPath($Output)
    New-Item -ItemType Directory -Force (Split-Path $outPath) | Out-Null
    & $MakeAppx pack /d $work /p $outPath /o /nv
    if ($LASTEXITCODE -ne 0) { throw 'makeappx failed' }

    if ($CertificatePfx) {
        if (-not $SignTool) { $SignTool = Find-SdkTool 'signtool.exe' }
        & $SignTool sign /fd SHA256 /f $CertificatePfx /p $CertificatePassword $outPath
        if ($LASTEXITCODE -ne 0) { throw 'signtool failed' }
    }
    Write-Host "MSIX $msixVersion -> $outPath"
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
