param([string]$Version = "0.1.0")
# Publishes a self-contained single-file exe, then compiles the Inno Setup installer.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$pub = Join-Path $root "artifacts\publish"
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }

dotnet publish "$root\src\WClean.App" -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:Version=$Version -o $pub
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
Get-ChildItem $pub -Filter *.pdb | Remove-Item

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
          "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
          "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found" }

& $iscc "/DAppVersion=$Version" "/DSourceDir=$pub" "$root\installer\WClean.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
Get-Item "$root\artifacts\WClean-$Version-win-x64-setup.exe" | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}}
