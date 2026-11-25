# ZG2 – Build und Paket
# Aufruf im Ordner neben ZG2.sln:   .\package.ps1            (baut, packt, installiert in den Mods-Ordner)
#                                    .\package.ps1 -NoInstall  (nur bauen und packen)
param(
    [switch]$NoInstall
)

$ErrorActionPreference = 'Stop'
$root    = $PSScriptRoot
# Derail Valley: Umgebungsvariable DV_INSTALL_DIR oder erster vorhandener Ordner (gleiche Liste wie Directory.Build.targets)
$dvCandidates = @(
    $env:DV_INSTALL_DIR,
    'C:\GAMES\Steam\steamapps\common\Derail Valley',
    'S:\SteamLibrary\steamapps\common\Derail Valley',
    'D:\SteamLibrary\steamapps\common\Derail Valley',
    'E:\SteamLibrary\steamapps\common\Derail Valley',
    'F:\SteamLibrary\steamapps\common\Derail Valley',
    'C:\Program Files (x86)\Steam\steamapps\common\Derail Valley',
    'C:\Program Files\Steam\steamapps\common\Derail Valley'
)
$dvDir = $dvCandidates | Where-Object { $_ -and (Test-Path (Join-Path $_ 'DerailValley_Data\Managed')) } | Select-Object -First 1
if (-not $dvDir) { throw 'Derail Valley nicht gefunden. Pfad in package.ps1 ergaenzen oder DV_INSTALL_DIR setzen.' }
Write-Host "Derail Valley: $dvDir" -ForegroundColor Cyan
$modsDir = Join-Path $dvDir 'Mods'
$utf8    = New-Object System.Text.UTF8Encoding($false)   # UTF-8 OHNE BOM

function Read-TextNoBom([string]$path) {
    $text = [System.IO.File]::ReadAllText($path)
    if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) { $text = $text.Substring(1) }
    return $text
}

# ---------- Version ----------
$infoPath = Join-Path $root 'Info.json'
$infoText = Read-TextNoBom $infoPath
$version  = ([regex]::Match($infoText, '"Version"\s*:\s*"([^"]+)"')).Groups[1].Value
if (-not $version) { throw 'Version in Info.json nicht gefunden.' }

$distDir = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

# Existiert das ZIP dieser Version schon, Hotfix-Teil hochzaehlen (1.1.0 -> 1.1.1)
while (Test-Path (Join-Path $distDir "ZG2_$version.zip")) {
    $parts = $version.Split('.')
    $last  = $parts.Length - 1
    $width = $parts[$last].Length
    $parts[$last] = ([int]$parts[$last] + 1).ToString().PadLeft($width, '0')
    $newVersion = $parts -join '.'
    Write-Host "ZIP fuer $version existiert bereits -> Version $newVersion" -ForegroundColor Yellow
    $infoText = $infoText -replace ('"Version"\s*:\s*"' + [regex]::Escape($version) + '"'), ('"Version": "' + $newVersion + '"')
    $version  = $newVersion
}
[System.IO.File]::WriteAllText($infoPath, $infoText, $utf8)

# Alte Pack-Regel im MP-Projekt wuerde beim Release-Build selbst ein (unvollstaendiges) ZIP erzeugen
$mpProj = Join-Path $root 'ZG2.MP\ZG2.MP.csproj'
if ((Get-Content $mpProj -Raw) -match 'CopyAndPack') {
    throw "ZG2.MP\ZG2.MP.csproj enthaelt noch die alte Pack-Regel (CopyAndPack). Bitte die neue ZG2.MP.csproj einsetzen."
}

# ---------- Build ----------
Write-Host "Baue ZG2 $version (Release) ..." -ForegroundColor Cyan
& dotnet build (Join-Path $root 'ZG2.sln') -c Release -nologo -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Build fehlgeschlagen.' }

$mainDll = Join-Path $root 'ZG2.Game\bin\Release\ZG2.dll'
$mpDll   = Join-Path $root 'ZG2.MP\bin\Release\ZG2.MP.dll'
foreach ($f in @($mainDll, $mpDll)) {
    if (-not (Test-Path $f)) { throw "Nicht gefunden: $f" }
}

# ---------- Asset-Bundle ----------
$bundleCandidates = @(
    (Join-Path $root 'ZG2.UnityProject\Assets\AssetBundles\zg2boards'),
    (Join-Path $root 'zg2boards'),
    (Join-Path $modsDir 'ZG2\zg2boards'),
    (Join-Path $dvDir 'Mods.inactive\ZG2\zg2boards')
)
$bundle = $bundleCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $bundle) {
    throw "Asset-Bundle 'zg2boards' nicht gefunden. Lege es neben ZG2.sln (aus dem Repo: ZG2.UnityProject\Assets\AssetBundles\zg2boards oder aus der installierten Mod Mods\ZG2\zg2boards)."
}

# ---------- Textur-Bundle (eigene Texturen) ----------
# Bevorzugt das in Unity gebaute Bundle "zg2textures"; sonst die Bilddateien aus
# ZG2.UnityProject\Assets\CustomTextures lose in den Ordner Textures\ des Pakets.
$texBundle = @(
    (Join-Path $root 'ZG2.UnityProject\Assets\AssetBundles\zg2textures'),
    (Join-Path $root 'zg2textures')
) | Where-Object { Test-Path $_ } | Select-Object -First 1
$texSourceDir = Join-Path $root 'ZG2.UnityProject\Assets\CustomTextures'

# ---------- Paket zusammenstellen ----------
$stage = Join-Path $distDir 'ZG2'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item $mainDll $stage
Copy-Item $mpDll   $stage
Copy-Item $bundle  (Join-Path $stage 'zg2boards')
if ($texBundle) {
    Copy-Item $texBundle (Join-Path $stage 'zg2textures')
    Write-Host "Textur-Bundle: $texBundle" -ForegroundColor Cyan
} elseif (Test-Path $texSourceDir) {
    $texFiles = Get-ChildItem $texSourceDir -File | Where-Object { $_.Extension -match '^\.(png|jpg|jpeg|tga)$' }
    if ($texFiles) {
        $texOut = Join-Path $stage 'Textures'
        New-Item -ItemType Directory -Force -Path $texOut | Out-Null
        $texFiles | ForEach-Object { Copy-Item $_.FullName $texOut }
        Write-Host "Kein Textur-Bundle gebaut - $($texFiles.Count) Texturdatei(en) lose nach Textures\" -ForegroundColor Yellow
    }
}
# info.json OHNE BOM ins Paket
[System.IO.File]::WriteAllText((Join-Path $stage 'Info.json'), $infoText, $utf8)

$zip = Join-Path $distDir "ZG2_$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $true)
Write-Host "Paket: $zip" -ForegroundColor Green

# ---------- In den Mods-Ordner installieren ----------
if (-not $NoInstall) {
    $target = Join-Path $modsDir 'ZG2'
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    # Settings.xml und andere vorhandene Dateien bleiben erhalten
    Copy-Item (Join-Path $stage '*') $target -Recurse -Force
    Write-Host "Installiert nach: $target" -ForegroundColor Green
}