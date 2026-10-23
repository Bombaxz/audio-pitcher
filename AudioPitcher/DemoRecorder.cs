using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfColor = System.Windows.Media.Color;

namespace AudioPitcher;

// Herramientas para generar capturas y videos de la web (solo en Debug):
//   dotnet run -- --screenshot salida.png
//   dotnet run -- --record <pitch|formant|apps|themes|fx|rec> carpeta
// En modo demo no se guarda la configuración del usuario.
public partial class MainWindow
{
    private bool _demoMode;

#if DEBUG
    private readonly List<BitmapSource> _demoFrames = new();
    private Stopwatch? _demoClock;

    private async void RunDemoToolsIfRequested()
    {
        try { await RunDemoTools(); }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "ap-demo-error.txt"), ex.ToString());
            Close();
        }
    }

    // Experimento: ¿la captura por proceso sigue recibiendo audio si la app está silenciada?
    // dotnet run -- --probe-mute zen   → resultado en %TEMP%\ap-probe.txt
    private async Task ProbeMute(string processName)
    {
        var sb = new System.Text.StringBuilder();
        var info = AudioEngine.GetActiveSessions().FirstOrDefault(s => s.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
        if (info == null) { sb.AppendLine("No hay sesión de audio para " + processName); }
        else
        {
            float peak = 0, speakers = 0;
            using var cap = new ProcessLoopbackCapture(info.ProcessId);
            cap.DataAvailable += (_, e) =>
            {
                for (int i = 0; i + 4 <= e.BytesRecorded; i += 4)
                    peak = Math.Max(peak, Math.Abs(BitConverter.ToSingle(e.Buffer, i)));
            };
            // Lo que realmente sale por los parlantes (loopback del dispositivo de salida)
            // Solo la energía en la frecuencia del tono de prueba (Goertzel), para ignorar el resto del audio
            using var spk = new NAudio.Wave.WasapiLoopbackCapture();
            double freq = 6789, sr = spk.WaveFormat.SampleRate; int ch = spk.WaveFormat.Channels;
            double coeff = 2 * Math.Cos(2 * Math.PI * freq / sr);
            spk.DataAvailable += (_, e) =>
            {
                double s1 = 0, s2 = 0; int n = 0;
                for (int i = 0; i + 4 * ch <= e.BytesRecorded; i += 4 * ch, n++)
                {
                    double x = BitConverter.ToSingle(e.Buffer, i);
                    double s0 = x + coeff * s1 - s2; s2 = s1; s1 = s0;
                }
                if (n == 0) return;
                double mag = Math.Sqrt(s1 * s1 + s2 * s2 - coeff * s1 * s2) * 2 / n;
                speakers = Math.Max(speakers, (float)mag);
            };
            cap.StartRecording();
            spk.StartRecording();
            async Task<string> Measure() { peak = 0; speakers = 0; await Task.Delay(2500); return $"captura {peak:0.000} | parlantes {speakers:0.000}"; }

            sb.AppendLine($"Formato: {cap.WaveFormat}");
            sb.AppendLine($"Normal:            {await Measure()}");
            info.Session.SimpleAudioVolume.Mute = true;
            sb.AppendLine($"App silenciada:    {await Measure()}");
            info.Session.SimpleAudioVolume.Mute = false;
            sb.AppendLine($"Restaurado:        {await Measure()}");
            spk.StopRecording();
            cap.StopRecording();
        }
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "ap-probe.txt"), sb.ToString());
    }

    private async Task RunDemoTools()
    {
        var args = Environment.GetCommandLineArgs();
        int pm = Array.IndexOf(args, "--probe-mute");
        if (pm >= 0 && pm + 1 < args.Length)
        {
            _demoMode = true;
            await Task.Delay(2000);
            await ProbeMute(args[pm + 1]);
            Close();
            return;
        }
        int s = Array.IndexOf(args, "--screenshot");
        int r = Array.IndexOf(args, "--record");
        if (s < 0 && r < 0) return;
        _demoMode = true;
        await Task.Delay(3000);

        if (s >= 0 && s + 1 < args.Length)
        {
            SaveFrame(CaptureFrame(), args[s + 1]);
            Close();
            return;
        }
        if (r + 2 >= args.Length) { Close(); return; }

        string clip = args[r + 1], dir = args[r + 2];
        Directory.CreateDirectory(dir);
        StartCapture();
        await (clip switch
        {
            "pitch"   => ClipPitch(),
            "formant" => ClipFormant(),
            "apps"    => ClipApps(),
            "themes"  => ClipThemes(),
            "fx"      => ClipFx(),
            "rec"     => ClipRec(),
            _         => Task.CompletedTask,
        });
        StopCaptureAndSave(dir);
        Close();
    }

    // ── Captura ──────────────────────────────────────────────────────────────

    private BitmapSource CaptureFrame()
    {
        RootBorder.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)(RootBorder.ActualWidth * 2), (int)(RootBorder.ActualHeight * 2),
                                         192, 192, PixelFormats.Pbgra32);
        bmp.Render(RootBorder);
        bmp.Freeze();
        return bmp;
    }

    private static void SaveFrame(BitmapSource bmp, string path)
    {
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    private double _demoNextMs;

    private void StartCapture()
    {
        _demoClock = Stopwatch.StartNew();
        CompositionTarget.Rendering += CaptureOnRender;
    }

    // Un cuadro cada ~33 ms, sincronizado con el render de WPF
    private void CaptureOnRender(object? sender, EventArgs e)
    {
        if (_demoClock!.Elapsed.TotalMilliseconds < _demoNextMs) return;
        _demoNextMs += 1000.0 / 30;
        _demoFrames.Add(CaptureFrame());
    }

    // Todos los cuadros al tamaño máximo (la ventana crece hacia abajo) + fps real medido.
    private void StopCaptureAndSave(string dir)
    {
        CompositionTarget.Rendering -= CaptureOnRender;
        double secs = _demoClock!.Elapsed.TotalSeconds;
        int w = _demoFrames.Max(f => f.PixelWidth), h = _demoFrames.Max(f => f.PixelHeight);
        w += w % 2; h += h % 2;
        for (int i = 0; i < _demoFrames.Count; i++)
        {
            var f = _demoFrames[i];
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
                dc.DrawImage(f, new Rect(0, 0, f.PixelWidth, f.PixelHeight));
            var canvas = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            canvas.Render(dv);
            SaveFrame(canvas, Path.Combine(dir, $"f{i:D4}.png"));
        }
        File.WriteAllText(Path.Combine(dir, "fps.txt"),
            (_demoFrames.Count / secs).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
    }

    // ── Utilidades de animación ─────────────────────────────────────────────

    private static Task Wait(int ms) => Task.Delay(ms);

    private static async Task Tween(Action<double> set, double from, double to, int ms)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            double x = Math.Min(1, sw.ElapsedMilliseconds / (double)ms);
            double e = x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2;
            set(from + (to - from) * e);
            if (x >= 1) break;
            await Task.Delay(15);
        }
    }

    private Task Pitch(double to, int ms = 900) => Tween(v => PitchSlider.Value = Math.Round(v, 1), PitchSlider.Value, to, ms);
    private Task Formant(double to, int ms = 900) => Tween(v => FormantSlider.Value = Math.Round(v, 1), FormantSlider.Value, to, ms);

    // ── Clips ───────────────────────────────────────────────────────────────

    private async Task ClipPitch()
    {
        await Wait(600);
        await Pitch(5);   await Wait(800);
        await Pitch(-4, 1100); await Wait(800);
        await Pitch(0);   await Wait(700);
    }

    private async Task ClipFormant()
    {
        await Wait(600);
        await Formant(4);  await Wait(800);
        await Formant(-3, 1100); await Wait(800);
        await Formant(0);  await Wait(700);
    }

    private async Task ClipApps()
    {
        // Apps instaladas típicas (no hace falta que estén abiertas: solo se usa el ícono)
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string FirstExisting(params string[] paths) => paths.FirstOrDefault(File.Exists) ?? "";
        string Newest(string dir, string pattern, string exe) => Directory.Exists(dir)
            ? Directory.GetDirectories(dir, pattern).OrderByDescending(d => d).Select(d => Path.Combine(d, exe)).FirstOrDefault(File.Exists) ?? ""
            : "";
        string StoreExe(string package, string exe)
        {
            try
            {
                var dir = Directory.GetDirectories(Path.Combine(pf, "WindowsApps"), package + "_*_x64_*").FirstOrDefault();
                return dir != null && File.Exists(Path.Combine(dir, exe)) ? Path.Combine(dir, exe) : "";
            }
            catch { return ""; }
        }
        var candidates = new (string Name, string Path)[]
        {
            ("Spotify",       FirstExisting(Path.Combine(roaming, @"Spotify\Spotify.exe"), StoreExe("SpotifyAB.SpotifyMusic", "Spotify.exe"),
                                            Environment.GetEnvironmentVariable("AP_SPOTIFY_EXE") ?? "",
                                            Path.Combine(local, @"Microsoft\WindowsApps\Spotify.exe"))),
            ("Discord",       Newest(Path.Combine(local, "Discord"), "app-*", "Discord.exe") is { Length: > 0 } dc ? dc
                              : FirstExisting(Path.Combine(local, @"Discord\Discord.exe"))),
            ("Google Chrome", FirstExisting(Path.Combine(pf, @"Google\Chrome\Application\chrome.exe"))),
            ("Zen Browser",   FirstExisting(Path.Combine(pf, @"Zen Browser\zen.exe"))),
            ("Microsoft Edge", FirstExisting(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"))),
        };
        var items = new List<AudioSessionInfo>();
        int fakePid = 900000;
        foreach (var (name, path) in candidates)
        {
            if (path.Length == 0) continue;
            items.Add(new AudioSessionInfo { Name = name, ProcessName = Path.GetFileNameWithoutExtension(path),
                                             ExePath = path, ProcessId = fakePid++, Icon = GetAppIcon(path) });
        }
        if (items.Count == 0) return;

        double[] pitches = { 3, -2, 5, -4, 2 };
        _appComboUpdating = true;
        AppCombo.ItemsSource = items;
        await Wait(500);
        for (int i = 0; i < items.Count && i < pitches.Length; i++)
        {
            AppCombo.SelectedIndex = i;
            StatusLabel.Text = $"Procesando {items[i].Name} en vivo.";
            await Wait(500);
            await Pitch(pitches[i], 700);
            await Wait(900);
        }
        await Pitch(0, 700);
        await Wait(600);
    }

    private async Task ClipThemes()
    {
        ToggleSettings_Click(this, new RoutedEventArgs());
        await Wait(1000);
        foreach (int preset in new[] { 1, 2, 4, 3 })
        {
            ThemeService.ApplyBgPreset(preset);
            HighlightBgBtns(preset);
            await Wait(1100);
        }
        await Tween(v => HueSlider.Value = v, HueSlider.Value, 140, 1600);
        await Wait(500);
        ThemeService.ApplyTextColorMode(2);
        HighlightTextBtns();
        await Wait(900);
        await Tween(v => HueSlider.Value = v, HueSlider.Value, -90, 1600);
        await Wait(900);
    }

    private async Task ClipFx()
    {
        ToggleFx_Click(this, new RoutedEventArgs());
        await Wait(900);
        await Tween(v => ReverbWetKnob.Value = v, ReverbWetKnob.Value, 0.6, 800);
        await Tween(v => ReverbRoomKnob.Value = v, ReverbRoomKnob.Value, 0.85, 700);
        await Wait(300);
        await Tween(v => DelayWetKnob.Value = v, DelayWetKnob.Value, 0.35, 800);
        await Tween(v => DelayTimeKnob.Value = v, DelayTimeKnob.Value, 480, 700);
        await Wait(300);
        await Tween(v => ChorusWetKnob.Value = v, ChorusWetKnob.Value, 0.5, 800);
        await Tween(v => ChorusRateKnob.Value = v, ChorusRateKnob.Value, 2.2, 700);
        await Wait(900);
    }

    private async Task ClipRec()
    {
        var red = new SolidColorBrush(WpfColor.FromRgb(0xF0, 0x47, 0x5A));
        RecBtnIcon.Foreground = red;
        Waveform.ResetScrolling();
        RecTimerLabel.Text = "0:00.0";
        AnimateMaxHeight(RecordPanel, 175, 220, () => UpdateNaturalHeight(autoExpand: true));
        await Wait(500);

        // Envolvente tipo música: golpes cada 0,5 s que decaen + variación aleatoria
        var rnd = new Random(7);
        double Env(double t) => 0.18 + 0.6 * Math.Exp(-((t % 0.5) * 7)) * (0.7 + 0.3 * Math.Sin(t * 1.3)) + 0.12 * rnd.NextDouble();
        var sw = Stopwatch.StartNew();
        for (int n = 0; n < 40; n++)
        {
            double t = n / 10.0;
            for (int k = 0; k < 10; k++)
                Waveform.AddPeak((float)Math.Min(0.95, Env(t + k / 100.0)));
            RecDot.Fill = red;
            RecDot.Opacity = n % 2 == 0 ? 1.0 : 0.3;
            RecTimerLabel.Text = $"0:{(int)t:D2}.{(int)(t * 10) % 10}";
            await Task.Delay(Math.Max(0, (int)((n + 1) * 100 - sw.ElapsedMilliseconds)));
        }

        // Stop → modo revisión con recorte y preescucha
        RecDot.Opacity = 1;
        RecDot.Fill = new SolidColorBrush(WpfColor.FromRgb(0x3D, 0x3D, 0x56));
        var samples = new float[48000 * 4];
        double amp = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            if (i % 1200 == 0) amp = Math.Min(0.95, Env(i / 48000.0));
            samples[i] = (float)((rnd.NextDouble() * 2 - 1) * amp);
        }
        Waveform.SetReviewData(samples);
        RecSaveBtn.IsEnabled = true;
        RecPauseBtn.Visibility = Visibility.Collapsed;
        RecPlayBtn.Visibility = Visibility.Visible;
        await Wait(700);
        await Tween(v => Waveform.TrimStartRatio = v, 0, 0.14, 700);
        await Tween(v => Waveform.TrimEndRatio = v, 1, 0.82, 700);
        await Wait(400);
        RecPlayIcon.Data = (Geometry)FindResource("StopGeometry");
        await Tween(v => Waveform.PlayheadRatio = v, 0.14, 0.82, 2200);
        Waveform.PlayheadRatio = -1;
        RecPlayIcon.Data = (Geometry)FindResource("PlayGeometry");
        await Wait(800);
    }
#endif
}
