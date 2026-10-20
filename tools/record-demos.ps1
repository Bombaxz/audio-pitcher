# Graba los clips del programa real para la web (necesita ffmpeg).
# Uso: powershell -ExecutionPolicy Bypass -File tools\record-demos.ps1 [clip ...]
param([string[]]$Clips = @('pitch', 'formant', 'apps', 'themes', 'fx', 'rec'))
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$app  = "$root\AudioPitcher"
$out  = "$root\web\img"

dotnet build "$app\AudioPitcher.csproj" -nologo -v q
if ($LASTEXITCODE) { throw 'build fallo' }
# Spotify de la Microsoft Store: la app no puede listar WindowsApps, se le pasa la ruta
$sp = Get-AppxPackage SpotifyAB.SpotifyMusic -ErrorAction SilentlyContinue
if ($sp) { $env:AP_SPOTIFY_EXE = Join-Path $sp.InstallLocation 'Spotify.exe' }
$exe = Get-ChildItem "$app\bin\Debug" -Recurse -Filter AudioPitcher.exe | Select-Object -First 1

foreach ($clip in $Clips) {
    $dir = Join-Path $env:TEMP "ap-clip-$clip"
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    Write-Host "Grabando $clip..."
    Start-Process $exe.FullName -ArgumentList '--record', $clip, $dir -Wait
    $fps = Get-Content "$dir\fps.txt"
    # El hero va sobre el fondo de la app; los recuadros sobre el gris de la sección
    $bg = if ($clip -eq 'pitch') { '0x0d0c16' } else { '0x181818' }
    $size = (& ffprobe -v error -select_streams v:0 -show_entries stream=width,height -of csv=p=0:s=x "$dir\f0000.png")
    ffmpeg -y -loglevel error -framerate $fps -i "$dir\f%04d.png" `
        -filter_complex "color=c=$($bg):s=$($size):r=30[bg];[bg][0:v]overlay=shortest=1,fps=30,format=yuv420p" `
        -c:v libx264 -preset slow -crf 22 -movflags +faststart -an "$out\demo-$clip.mp4"
    ffmpeg -y -loglevel error -i "$dir\f0000.png" -vf "color=c=$($bg):s=$($size)[bg];[bg][0:v]overlay" -frames:v 1 "$out\demo-$clip.jpg" 2>$null
    Remove-Item $dir -Recurse -Force
    Write-Host "  -> demo-$clip.mp4"
}
