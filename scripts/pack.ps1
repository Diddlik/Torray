# Builds the Velopack installer into artifacts/releases. The version comes from Directory.Build.props.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-Location (Split-Path $PSScriptRoot)

$version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version
# Local builds start clean; vpk refuses to pack a version that already exists in the output folder.
Remove-Item artifacts -Recurse -Force -ErrorAction Ignore

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
