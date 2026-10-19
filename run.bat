@echo off
title Audio Pitcher (watch)
cd /d "%~dp0AudioPitcher"
set DOTNET_WATCH_RESTART_ON_RUDE_EDIT=1
dotnet watch run --project AudioPitcher.csproj
if errorlevel 1 (
    echo.
    echo Error al iniciar. Asegurate de tener .NET 8 instalado.
    pause
)
