# Builds the plugin and zips a Thunderstore-ready package into dist/.
#
#   powershell -ExecutionPolicy Bypass -File tools\pack.ps1
#
# Thunderstore wants manifest.json, icon.png and README.md at the zip root, so the
# package/ folder is zipped by its contents, not as a folder.
#
# Unlike the other Cartur mods this one ships art, and AssetLoader looks for it in an
# assets/ folder beside the DLL. So the DLL cannot sit loose in plugins/ - it goes in
# plugins/CarturWasteManagement/ with assets/ next to it, and that shape has to survive the zip.

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$pkg = Join-Path $root "package"
$dist = Join-Path $root "dist"

$manifest = Get-Content (Join-Path $pkg "manifest.json") -Raw | ConvertFrom-Json
$version = $manifest.version_number

# The plugin version and the package version are read by different people in different
# places; if they drift, the Thunderstore listing lies about what is in the DLL.
$plugin = Get-Content (Join-Path $root "src\Plugin.cs") -Raw
if ($plugin -notmatch 'PluginVersion\s*=\s*"([^"]+)"') { throw "Could not read the plugin version from src\Plugin.cs" }
if ($Matches[1] -ne $version) { throw "manifest.json is $version but the plugin says $($Matches[1])" }

dotnet build (Join-Path $root "src\CarturWasteManagement.csproj") -c Release
if (-not $?) { throw "build failed" }

# Cleared, not just overwritten: a DLL or a renamed PNG left behind from an earlier build
# would otherwise ride along in the zip.
$modDir = Join-Path $pkg "plugins\CarturWasteManagement"
if (Test-Path (Join-Path $pkg "plugins")) { Remove-Item (Join-Path $pkg "plugins") -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $modDir "assets") | Out-Null
Copy-Item (Join-Path $root "src\bin\Release\net472\CarturWasteManagement.dll") $modDir -Force
Copy-Item (Join-Path $root "src\Assets\*.png") (Join-Path $modDir "assets") -Force

$shipped = Get-ChildItem (Join-Path $modDir "assets") -File
Write-Host "assets: $($shipped.Count) - $($shipped.Name -join ', ')"

New-Item -ItemType Directory -Force -Path $dist | Out-Null
$zip = Join-Path $dist "$($manifest.name)-$version.zip"
if (Test-Path $zip) { Remove-Item $zip }

# Written entry by entry rather than with Compress-Archive: that cmdlet stores Windows
# backslashes in the entry names, and an installer reading the zip then creates a single
# file literally named "plugins\CarturWasteManagement\CarturWasteManagement.dll" instead of the folders.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, "Create")
try {
    foreach ($file in Get-ChildItem $pkg -Recurse -File) {
        $name = $file.FullName.Substring($pkg.Length + 1).Replace("\", "/")
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $name) | Out-Null
    }
}
finally { $archive.Dispose() }

Write-Host "packed $zip"

# Nexus installs by unpacking into the game folder rather than reading a manifest, so its
# zip is the plugin folder at its real path and nothing else - no manifest, icon or README,
# which would land in the Valheim root as loose files.
$nexus = Join-Path $dist "$($manifest.name)-$version-Nexus.zip"
if (Test-Path $nexus) { Remove-Item $nexus }
$archive = [System.IO.Compression.ZipFile]::Open($nexus, "Create")
try {
    foreach ($file in Get-ChildItem $modDir -Recurse -File) {
        $rel = $file.FullName.Substring($modDir.Length + 1).Replace("\", "/")
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $file.FullName, "BepInEx/plugins/CarturWasteManagement/$rel") | Out-Null
    }
}
finally { $archive.Dispose() }

Write-Host "packed $nexus"
