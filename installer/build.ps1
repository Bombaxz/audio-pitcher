# Compila Audio Pitcher y arma installer\Output\AudioPitcher-Setup.exe
# Uso: powershell -ExecutionPolicy Bypass -File installer\build.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$version = ([xml](Get-Content "$root\AudioPitcher\AudioPitcher.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Write-Host "Audio Pitcher $version"

$vb = "$PSScriptRoot\vbcable"
if (-not (Test-Path "$vb\VBCABLE_Setup_x64.exe")) {
    Write-Host 'Descargando VB-CABLE...'
    $zip = Join-Path $env:TEMP 'VBCABLE_Driver_Pack45.zip'
    Invoke-WebRequest 'https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip' -OutFile $zip -UseBasicParsing
    Expand-Archive $zip -DestinationPath $vb -Force
}

$pub = "$root\AudioPitcher\bin\Publish\win-x64"
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
dotnet publish "$root\AudioPitcher\AudioPitcher.csproj" -p:PublishProfile=win-x64 -nologo
if ($LASTEXITCODE) { throw 'dotnet publish fallo' }

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Falta Inno Setup 6 (winget install JRSoftware.InnoSetup)' }
& $iscc /Q "/DAppVersion=$version" "$PSScriptRoot\AudioPitcher.iss"
if ($LASTEXITCODE) { throw 'Inno Setup fallo' }

# Copia idéntica que baja el actualizador: así las descargas de Setup.exe son solo de la web
Copy-Item "$PSScriptRoot\Output\AudioPitcher-Setup.exe" "$PSScriptRoot\Output\AudioPitcher-Update.exe" -Force
Write-Host "Listo: $PSScriptRoot\Output\AudioPitcher-Setup.exe (+ AudioPitcher-Update.exe)"
