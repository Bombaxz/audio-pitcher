# Audio Pitcher

Change the pitch of any Windows app's audio in real time: Spotify, YouTube, games, Discord, anything. Free and open source.

*Cambiá el tono del audio de cualquier app de Windows en tiempo real. Gratis y de código abierto. [En español →](https://audiopitcher.app/es/)*

## Download

**[audiopitcher.app](https://audiopitcher.app)** · **[Download for Windows](https://github.com/Bombaxz/audio-pitcher/releases/latest/download/AudioPitcher-Setup.exe)** (Windows 10/11, 64-bit)

The installer also installs [VB-CABLE](https://vb-audio.com/Cable/), a free virtual audio device Audio Pitcher needs to work. You may need to restart your PC after the first install.

## Features

- Pitch in semitones (−12 to +12) and formant, separately.
- Process one app or the whole system audio.
- Effects: reverb, delay and chorus.
- Record system audio with trimming, preview and WAV export.
- Themes, image backgrounds with a frosted-glass look, custom colors and "always on top".
- English and Spanish interface (follows the Windows language).
- Updates itself silently when a new version is out.

## Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
cd AudioPitcher
dotnet run
```

To build the installer you need [Inno Setup 6](https://jrsoftware.org/isinfo.php):

```
powershell -ExecutionPolicy Bypass -File installer\build.ps1
```

## Structure

| Folder | Contents |
|---|---|
| `AudioPitcher/` | The app (WPF, .NET 8). `lib/` holds the prebuilt native DLLs. |
| `native/signalsmith-wrapper/` | C++ source of `signalsmith_stretch.dll`. |
| `installer/` | Inno Setup script and `build.ps1`. |
| `web/` | The website (audiopitcher.app), deployed on Cloudflare. |
| `tools/` | Local site preview, guide generator, demo recording. |
| `assets/` | High-resolution logo. |

## Support the project

Audio Pitcher is free. If it's useful to you, you can support it on [Ko-fi](https://ko-fi.com/labalseraa) or, from Argentina, with a [cafecito](https://cafecito.app/labalaseraa) ☕.

## Help

This is a personal, free project. You can report bugs in [Issues](https://github.com/Bombaxz/audio-pitcher/issues) (the app's "Report a problem" button saves a report you can attach), but support isn't guaranteed.

## License

[GPL-3.0](LICENSE). Third-party components in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
