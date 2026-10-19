# Audio Pitcher

Cambiá el tono (pitch) del audio de cualquier app de Windows en tiempo real: Spotify, YouTube, juegos, Discord, lo que sea.

*Change the pitch of any Windows app's audio in real time. Free and open source.*

## Descarga

**[Descargar para Windows](https://github.com/Bombaxz/audio-pitcher/releases/latest/download/AudioPitcher-Setup.exe)** (Windows 10/11, 64 bits)

El instalador también instala [VB-Cable](https://vb-audio.com/Cable/), un dispositivo de audio virtual gratuito que Audio Pitcher necesita para funcionar. Puede que haya que reiniciar la PC al terminar.

## Qué hace

- Pitch en semitonos (−12 a +12) y formant por separado.
- Elegís qué app procesar o todo el audio del sistema.
- Efectos: reverb, delay y chorus.
- Grabación del audio del sistema con recorte, preescucha y exportación a WAV.
- Temas, colores personalizables y modo "siempre al frente".
- Se actualiza solo cuando sale una versión nueva.

## Compilar desde el código

Requiere el [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
cd AudioPitcher
dotnet run
```

Para armar el instalador hace falta [Inno Setup 6](https://jrsoftware.org/isinfo.php):

```
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

## Estructura

| Carpeta | Qué hay |
|---|---|
| `AudioPitcher/` | La aplicación (WPF, .NET 8). `lib/` tiene las DLL nativas ya compiladas. |
| `native/signalsmith-wrapper/` | Código C++ de `signalsmith_stretch.dll`. |
| `installer/` | Script de Inno Setup y `build.ps1`. |
| `assets/` | Logo en alta resolución. |

## Soporte

Es un proyecto personal y gratuito. Podés reportar errores en [Issues](https://github.com/Bombaxz/audio-pitcher/issues), pero no hay soporte garantizado.

## Licencia

[GPL-3.0](LICENSE). Componentes de terceros en [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
