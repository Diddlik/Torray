# Builds the Velopack installer into artifacts/releases. The version comes from Directory.Build.props.
# -KeepReleases keeps previously downloaded releases in artifacts/releases so vpk can build delta updates (CI).
param([switch]$KeepReleases)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-Location (Split-Path $PSScriptRoot)

$version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version
Remove-Item artifacts/publish -Recurse -Force -ErrorAction Ignore
if (-not $KeepReleases) {
    # Local builds start clean; vpk refuses to pack a version that already exists in the output folder.
    Remove-Item artifacts/releases -Recurse -Force -ErrorAction Ignore
}

dotnet tool restore
dotnet publish src/Torray.App -c Release -r win-x64 --self-contained -o artifacts/publish

# The pack id names the install folder (%LOCALAPPDATA%\TorrayApp). It must differ from the data folder
# %LOCALAPPDATA%\Torray, because uninstalling deletes the whole install folder.
dotnet vpk pack `
    --packId TorrayApp `
    --packVersion $version `
    --runtime win-x64 `
    --packTitle Torray `
    --packAuthors Diddlik `
    --packDir artifacts/publish `
    --mainExe Torray.exe `
    --icon src/Torray.App/Assets/Torray.ico `
    --outputDir artifacts/releases
