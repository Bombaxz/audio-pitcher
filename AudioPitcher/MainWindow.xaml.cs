using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WpfColor          = System.Windows.Media.Color;
using WpfPoint          = System.Windows.Point;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfKeyEventArgs   = System.Windows.Input.KeyEventArgs;

namespace AudioPitcher;

public partial class MainWindow : Window
{
    private readonly AudioEngine    _engine   = new();
    private readonly RecorderEngine _recorder = new();
    private int  _outputDeviceIndex = -1;
    private bool _settingsOpen = false;
    private bool _recPanelOpen = false;
    private bool _fxOpen       = false;
    private bool _fxEnabled    = true;
    private bool _isPinned      = true;
    private System.Windows.Forms.NotifyIcon _trayIcon = null!;

    private readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _recentApps   = new();
    private List<string> _favoriteApps = new();
    private bool _showAllApps, _expandingApps;

    // recorder timer + peak accumulator
    private System.Windows.Threading.DispatcherTimer? _recTimer;
    private float        _recPeakMax  = 0f;
    private readonly object _recPeakLock = new();
    private bool         _recDotFlash = true;
    private bool         _normEnabled = false;

    private bool      _pitchDragging,   _formantDragging;
    private bool      _pitchDragMoved,  _formantDragMoved;
    private WpfPoint  _pitchDragOrigin, _formantDragOrigin;
    private double    _pitchDragVal0,   _formantDragVal0;

    private bool   _appComboUpdating = false;
    private string _targetProcessName = "";
    private string _selectedAppName   = "";
    private int    _routedPid         = 0;   // app actualmente ruteada a CABLE Input

    private DefaultDeviceWatcher? _deviceWatcher;
    private System.Windows.Threading.DispatcherTimer? _defaultDeviceDebounce;

    private bool _reverbOpen = true, _delayOpen = true, _chorusOpen = true;
    private bool _loading = false;
    private double _headerHeight, _naturalHeight;
    private double _titleLabelNaturalWidth;
    private bool   _titleVisible = true;
    private double _heightBeforeSettings = 0;
    private double _heightBeforeFx = 0;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            SaveSettings();
            RestoreRouting();
            _deviceWatcher?.Dispose();
            _trayIcon?.Dispose(); StopPreview(); _recorder.Dispose(); _engine.Dispose();
        };
        InitTray();
    }

    // ── Actualizaciones ───────────────────────────────────────────────────────

    private UpdateInfo? _pendingUpdate;
    private bool _updating;

    private string _skippedUpdate = "";

    private async void CheckForUpdates()
    {
        _skippedUpdate = SettingsStore.Load().SkippedUpdateVersion;
        var info = await UpdateService.CheckAsync();
        if (info == null) return;
        _pendingUpdate = info;
        UpdateBtn.ToolTip = $"Actualizar a {info.Version.ToString(3)}";

        // Se pregunta una sola vez por versión; si dice "Ahora no", queda el botón en el encabezado.
        string ver = info.Version.ToString(3);
        if (_skippedUpdate != ver)
        {
            await Task.Delay(1200);
            if (ShowUpdateDialog()) return;
            _skippedUpdate = ver;
            SaveSettings();
        }
        UpdateBtn.Visibility = Visibility.Visible;
    }

    private void UpdateBtn_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        UpdPill.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));

        var hop = new System.Windows.Media.Animation.DoubleAnimation(0, -1.8, TimeSpan.FromMilliseconds(130))
        {
            AutoReverse = true,
            RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(2),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase()
        };
        UpdHop.BeginAnimation(TranslateTransform.YProperty, hop);
    }

    private void UpdateBtn_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        UpdPill.BeginAnimation(OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0.55, TimeSpan.FromMilliseconds(200)));
    }

    private void UpdateBtn_Click(object sender, RoutedEventArgs e) => ShowUpdateDialog();

    // Devuelve true si se actualizó.
    private bool ShowUpdateDialog()
    {
        if (_pendingUpdate == null || _updating) return false;
        _updating = true;
        var info = _pendingUpdate;
        bool ok = new UpdateWindow(info) { Owner = this }.ShowDialog() == true;
        _updating = false;
        if (!ok) return false;

        if (info.InstallerUrl == UpdateService.FakeUrl)
        {
            UpdateBtn.Visibility = Visibility.Collapsed;
            _pendingUpdate = null;
            return true;
        }
        System.Windows.Application.Current.Shutdown();
        return true;
    }

    private static void OpenShell(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch { }
    }

    private void SourceLink_Click(object sender, RoutedEventArgs e) =>
        OpenShell($"https://github.com/{UpdateService.Repo}");

    private void LicensesLink_Click(object sender, RoutedEventArgs e)
    {
        var local = System.IO.Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
        OpenShell(System.IO.File.Exists(local)
            ? local
            : $"https://github.com/{UpdateService.Repo}/blob/main/THIRD-PARTY-NOTICES.txt");
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        VersionRun.Text = "v" + UpdateService.CurrentVersion.ToString(3);
        CheckForUpdates();
        _loading = true;   // prevent SaveSettings during initial device population
        LoadOutputDevices();

        DelayTimeKnob.TickValues = new[] { 100.0, 200.0, 300.0, 400.0, 500.0, 600.0, 700.0, 800.0 };

        if (!AudioEngine.IsRubberBandAvailable)
        {
            FormantSlider.IsEnabled        = false;
            FormantUnavailLabel.Visibility = Visibility.Visible;
            EngBtn0.IsEnabled              = false;
            EngTxt0.Text                   = "RB (N/D)";
        }
        if (!AudioEngine.IsSignalsmithAvailable)
        {
            EngBtn1.IsEnabled = false;
            EngTxt1.Text      = "SS (N/D)";
        }
        if (!AudioPolicyConfig.IsSupported)
        {
            AppCombo.IsEnabled = false;
            AppCombo.ToolTip   = "El ruteo por app no está disponible en esta versión de Windows.";
        }

        // Set mutable brushes so animations can target them
        FxPill.Background = new SolidColorBrush(WpfColor.FromRgb(0x89, 0xB4, 0xFA));
        FxPillDot.Fill    = new SolidColorBrush(Colors.White);

        ThemeService.ThemeChanged += (_, _) => { ApplyCustomLabelColors(); ApplyThemeChrome(); SaveSettings(); };

        // Hide cursor while dragging any slider thumb
        foreach (var sl in new[] { PitchSlider, FormantSlider, HueSlider })
        {
            sl.AddHandler(System.Windows.Controls.Primitives.Thumb.DragStartedEvent,
                new System.Windows.Controls.Primitives.DragStartedEventHandler((_, _) => Mouse.OverrideCursor = System.Windows.Input.Cursors.None));
            sl.AddHandler(System.Windows.Controls.Primitives.Thumb.DragCompletedEvent,
                new System.Windows.Controls.Primitives.DragCompletedEventHandler((_, _) => Mouse.OverrideCursor = null));
        }

        LoadSettings();
        StartEngine();

        // Seguir en vivo el dispositivo predeterminado de Windows
        _defaultDeviceDebounce = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _defaultDeviceDebounce.Tick += (_, _) =>
        {
            _defaultDeviceDebounce.Stop();
            if (_outputDeviceIndex == -1 && _engine.IsRunning) StartEngine();
        };
        _deviceWatcher = new DefaultDeviceWatcher();
        _deviceWatcher.DefaultRenderChanged += () => Dispatcher.BeginInvoke(new Action(() =>
        {
            _defaultDeviceDebounce.Stop();
            _defaultDeviceDebounce.Start();
        }));

        SizeChanged += Window_SizeChanged;

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(CheckVBCable));

        // Freeze height, capture natural height so MaxHeight blocks over-extending
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            _headerHeight  = ActualHeight - ShadeContent.ActualHeight;
            _naturalHeight = ActualHeight;
            MaxHeight      = _naturalHeight;
            SizeToContent  = SizeToContent.Manual;
            // Measure the title label natural width (works even if later collapsed)
            TitleLabel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            _titleLabelNaturalWidth = TitleLabel.DesiredSize.Width;
        }));
    }

    // ── VB-Cable check ───────────────────────────────────────────────────────

    private static bool IsVBCableInstalled()
    {
        for (int i = 0; i < NAudio.Wave.WaveOut.DeviceCount; i++)
        {
            var cap = NAudio.Wave.WaveOut.GetCapabilities(i);
            if (cap.ProductName.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        for (int i = 0; i < NAudio.Wave.WaveIn.DeviceCount; i++)
        {
            var cap = NAudio.Wave.WaveIn.GetCapabilities(i);
            if (cap.ProductName.Contains("CABLE", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private async void CheckVBCable()
    {
        if (IsVBCableInstalled()) return;

        var result = System.Windows.MessageBox.Show(
            "VB-Cable no está instalado.\n\n" +
            "Audio Pitcher necesita VB-Cable para funcionar como dispositivo virtual de audio. " +
            "¿Querés instalarlo ahora?\n\n" +
            "(Se descarga de vb-audio.com y requiere permisos de administrador)",
            "VB-Cable no encontrado",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        string installerPath = System.IO.Path.Combine(AppContext.BaseDirectory, "VBCABLE_Setup_x64.exe");
        try
        {
            if (!System.IO.File.Exists(installerPath))
            {
                SetError("Descargando VB-Cable…");
                installerPath = await DownloadVBCableSetup();
            }

            var proc = Process.Start(new ProcessStartInfo(installerPath)
            {
                Verb = "runas",
                UseShellExecute = true
            });
            if (proc != null) await Task.Run(proc.WaitForExit);

            System.Windows.MessageBox.Show(
                "Instalación completada.\n\n" +
                "Cerrá y volvé a abrir Audio Pitcher. Si el cable no aparece, reiniciá Windows.",
                "VB-Cable instalado",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Usuario canceló el UAC
        }
        catch
        {
            System.Windows.MessageBox.Show(
                "No se pudo descargar VB-Cable automáticamente.\n\n" +
                "Se va a abrir la página oficial para que lo descargues manualmente:\n" +
                "vb-audio.com/Cable",
                "Error de descarga",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            try { Process.Start(new ProcessStartInfo("https://vb-audio.com/Cable/") { UseShellExecute = true }); }
            catch { }
        }
    }

    private static async Task<string> DownloadVBCableSetup()
    {
        string zipPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VBCABLE_Driver_Pack.zip");
        string dir     = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VBCABLE_Driver_Pack");

        // Nombre del pack oficial vigente primero, versión anterior como respaldo
        string[] urls =
        {
            "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip",
            "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack43.zip",
        };

        using var http = new HttpClient();
        byte[]? data = null;
        foreach (var url in urls)
        {
            try { data = await http.GetByteArrayAsync(url); break; }
            catch { }
        }
        if (data == null) throw new InvalidOperationException("No se pudo descargar el driver.");

        await System.IO.File.WriteAllBytesAsync(zipPath, data);
        if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true);
        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, dir);

        string setup = System.IO.Path.Combine(dir, "VBCABLE_Setup_x64.exe");
        if (!System.IO.File.Exists(setup))
            throw new InvalidOperationException("El zip no contiene VBCABLE_Setup_x64.exe.");
        return setup;
    }

    // ── Tray ─────────────────────────────────────────────────────────────────

    private void InitTray()
    {
        // Ícono de la app (embebido en el exe); si falla, circulito de respaldo
        System.Drawing.Icon? trayIco = null;
        try { trayIco = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!); } catch { }
        if (trayIco == null)
        {
            var bmp = new System.Drawing.Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);
                g.FillEllipse(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(137, 180, 250)), 1, 1, 13, 13);
            }
            trayIco = System.Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Mostrar", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Cerrar", null, (_, _) => Dispatcher.Invoke(Close));

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Audio Pitcher",
            Icon = trayIco,
            Visible = false,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void HideToTray()
    {
        _trayIcon.Visible = true;
        ShowInTaskbar = false;
        Hide();
    }

    private bool _minimizeToTray;

    private void MinimizeMode_Click(object sender, MouseButtonEventArgs e)
    {
        _minimizeToTray = ((FrameworkElement)sender).Tag?.ToString() == "1";
        UpdateMinimizeButtons();
        SaveSettings();
        e.Handled = true;
    }

    private void UpdateMinimizeButtons()
    {
        var btns = new[] { MinBtn0, MinBtn1 };
        var txts = new[] { MinTxt0, MinTxt1 };
        for (int i = 0; i < 2; i++)
        {
            bool active = (i == 1) == _minimizeToTray;
            btns[i].SetResourceReference(Border.BorderBrushProperty, active ? "BrushBlue" : "BrushBorder");
            btns[i].BorderThickness = new Thickness(active ? 2 : 1);
            txts[i].SetResourceReference(TextBlock.ForegroundProperty, active ? "BrushText" : "BrushMuted");
        }
        MinimizeBtn.ToolTip = _minimizeToTray ? "Esconder en la bandeja" : "Minimizar";
    }

    private void RestoreFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            _trayIcon.Visible = false;
            ShowInTaskbar = true;
            Show();
            WindowState = WindowState.Normal;
            Activate();
        });
    }

    // ── Pin / Unpin ───────────────────────────────────────────────────────────

    private void PinBtn_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = !_isPinned;
        Topmost = _isPinned;
        UpdatePinBtn();
    }

    private void UpdatePinBtn()
    {
        PinBtn.SetResourceReference(ForegroundProperty, _isPinned ? "BrushBlue" : "BrushMuted");
        PinBtn.ToolTip = _isPinned ? "Fijada al frente (clic para soltar)" : "No está fijada (clic para fijar)";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (_minimizeToTray) HideToTray();
        else WindowState = WindowState.Minimized;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (!_isPinned && WindowState == WindowState.Normal)
            WindowState = WindowState.Minimized;
    }

    // ── Devices ───────────────────────────────────────────────────────────────

    private const string DefaultDeviceItem     = "Dispositivo predeterminado";
    private const string DefaultDeviceSentinel = "<default>";

    private void LoadOutputDevices()
    {
        var devices = AudioEngine.GetOutputDeviceNames();
        var items = new List<string> { DefaultDeviceItem };
        items.AddRange(devices);
        OutputCombo.ItemsSource = items;
        // Item 0 = default de Windows (índice -1 = wave mapper); selección inicial:
        // primer dispositivo concreto que no sea el cable, para evitar feedback.
        int idx = 0;
        for (int i = 0; i < devices.Count; i++)
            if (!devices[i].Contains("CABLE", StringComparison.OrdinalIgnoreCase)) { idx = i + 1; break; }
        OutputCombo.SelectedIndex = idx;
    }

    // ── Selección de app (ruteo automático a VB-Cable) ────────────────────────

    private const string SystemSourceName = "Sistema (VB-Cable)";

    private void LoadAppSessions()
    {
        var sessions = AudioEngine.GetActiveSessions();
        foreach (var s in sessions)
        {
            s.Icon       = GetAppIcon(s.ExePath);
            s.IsFavorite = _favoriteApps.Any(n => n.Equals(s.ProcessName, StringComparison.OrdinalIgnoreCase));
        }
        SweepOrphanRoutings(sessions);

        var sorted = sessions.OrderBy(SessionRank).ToList();
        var items  = new List<AudioSessionInfo>();

        // Colapsado: favoritas + últimas 2 usadas + la activa; el resto (y "Sistema")
        // bajo "Más opciones…", que va siempre al fondo.
        bool collapsed = !_showAllApps;
        if (collapsed)
        {
            var recent2 = _recentApps.Take(2).ToList();
            var visible = sorted.Where(s => s.IsFavorite || s.ProcessId == _routedPid
                || recent2.Any(n => n.Equals(s.ProcessName, StringComparison.OrdinalIgnoreCase))).ToList();
            if (visible.Count == 0 || visible.Count == sorted.Count)
            {
                collapsed = false;   // nada que priorizar o nada que esconder
            }
            else
            {
                items.AddRange(visible);
                if (_routedPid == 0 || !visible.Any(v => v.ProcessId == _routedPid))
                    items.Add(new AudioSessionInfo { Name = SystemSourceName, ProcessId = 0 });
                items.Add(new AudioSessionInfo { Name = "▾  Más opciones…", ProcessId = -1 });
            }
        }
        if (!collapsed)
        {
            items.AddRange(sorted);
            items.Add(new AudioSessionInfo { Name = SystemSourceName, ProcessId = 0 });
        }

        int idx = items.FindIndex(i => i.ProcessId > 0 && i.ProcessId == _routedPid);
        if (idx < 0 && !string.IsNullOrEmpty(_targetProcessName))
            idx = items.FindIndex(i => i.ProcessId > 0 &&
                i.ProcessName.Equals(_targetProcessName, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
            idx = items.FindIndex(i => i.ProcessId == 0);   // Sistema

        _appComboUpdating = true;
        AppCombo.ItemsSource   = items;
        AppCombo.SelectedIndex = idx >= 0 ? idx : 0;
        _appComboUpdating = false;

        ApplySelectedApp();
    }

    private void ApplySelectedApp()
    {
        if (AppCombo.SelectedItem is not AudioSessionInfo sel || sel.ProcessId < 0) return;
        _selectedAppName   = sel.Name;
        _targetProcessName = sel.ProcessId == 0 ? "" : sel.ProcessName;

        if (sel.ProcessId == _routedPid) return;   // sin cambios (incluye Sistema↔Sistema)

        RestoreRouting();
        if (sel.ProcessId == 0) return;

        try
        {
            using var cable = AudioEngine.FindCableRenderDevice();
            if (cable == null)
            {
                SetError("VB-CABLE no encontrado. Instalá desde vb-audio.com/Cable");
                return;
            }
            AudioPolicyConfig.SetDefaultEndpoint(sel.ProcessId, cable.ID);
            _routedPid = sel.ProcessId;

            _recentApps.RemoveAll(n => n.Equals(sel.ProcessName, StringComparison.OrdinalIgnoreCase));
            _recentApps.Insert(0, sel.ProcessName);
            if (_recentApps.Count > 8) _recentApps.RemoveRange(8, _recentApps.Count - 8);
        }
        catch (Exception ex) { SetError($"No se pudo rutear la app: {ex.Message}"); }
    }

    // Apps típicas de audio: van arriba en el combo cuando no hay historial
    private static readonly string[] MediaApps =
    {
        "spotify", "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc",
        "discord", "vlc", "itunes", "music", "tidal", "deezer", "foobar2000",
        "winamp", "musicbee", "aimp"
    };

    // Orden: favoritas → usadas recientemente (en orden) → apps de audio conocidas → el resto
    private int SessionRank(AudioSessionInfo a)
    {
        if (a.IsFavorite) return 0;
        int r = _recentApps.FindIndex(n => n.Equals(a.ProcessName, StringComparison.OrdinalIgnoreCase));
        if (r >= 0) return 10 + r;
        string p = a.ProcessName.ToLowerInvariant();
        return MediaApps.Any(m => p.Contains(m)) ? 100 : 200;
    }

    private ImageSource? GetAppIcon(string exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        if (_iconCache.TryGetValue(exePath, out var cached)) return cached;
        ImageSource? img = null;
        try
        {
            using var ico = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (ico != null)
            {
                var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    ico.Handle, System.Windows.Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                if (bmp.CanFreeze) bmp.Freeze();
                img = bmp;
            }
        }
        catch { }
        _iconCache[exePath] = img;
        return img;
    }

    /// <summary>Devuelve la app ruteada al default del sistema.</summary>
    private void RestoreRouting()
    {
        if (_routedPid == 0) return;
        try { AudioPolicyConfig.SetDefaultEndpoint(_routedPid, ""); } catch { }
        _routedPid = 0;
    }

    /// <summary>Limpia ruteos al cable que quedaron de sesiones anteriores (cierre sucio).</summary>
    private void SweepOrphanRoutings(List<AudioSessionInfo> items)
    {
        try
        {
            using var cable = AudioEngine.FindCableRenderDevice();
            if (cable == null) return;
            foreach (var it in items)
            {
                if (it.ProcessId == 0 || it.ProcessId == _routedPid) continue;
                if (string.Equals(AudioPolicyConfig.GetDefaultEndpoint(it.ProcessId), cable.ID,
                                  StringComparison.OrdinalIgnoreCase))
                    try { AudioPolicyConfig.SetDefaultEndpoint(it.ProcessId, ""); } catch { }
            }
        }
        catch { }
    }

    private void AppCombo_DropDownOpened(object sender, EventArgs e)
    {
        if (_expandingApps) { _expandingApps = false; return; }
        _showAllApps = false;
        LoadAppSessions();
    }

    private void AppCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_appComboUpdating || _loading) return;
        if (AppCombo.SelectedItem is AudioSessionInfo s && s.ProcessId < 0)
        {
            // "Más opciones…" seleccionado por teclado: expandir y reabrir
            _showAllApps = true;
            LoadAppSessions();
            if (!AppCombo.IsDropDownOpen)
            {
                _expandingApps = true;
                Dispatcher.BeginInvoke(new Action(() => AppCombo.IsDropDownOpen = true));
            }
            return;
        }
        ApplySelectedApp();
        StartEngine();
        SaveSettings();
    }

    // "Más opciones…": bloquear el down y expandir en el up, sin cerrar el dropdown.
    private void AppItem_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AudioSessionInfo info && info.ProcessId < 0)
            e.Handled = true;
    }

    private void AppItem_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not AudioSessionInfo info || info.ProcessId >= 0) return;
        e.Handled = true;
        _showAllApps = true;
        LoadAppSessions();
    }

    // La selección del ComboBox ocurre en el mouse-UP, así que hay que bloquear
    // ambos eventos y recién reconstruir la lista en el up (si se reconstruye en
    // el down, el up cae sobre otro item ya desplazado y lo selecciona).
    private void FavStar_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void FavStar_MouseUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is not AudioSessionInfo info || info.ProcessId <= 0) return;

        if (_favoriteApps.RemoveAll(n => n.Equals(info.ProcessName, StringComparison.OrdinalIgnoreCase)) == 0)
            _favoriteApps.Add(info.ProcessName);

        LoadAppSessions();
        SaveSettings();
    }


    private void OutputCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OutputCombo.SelectedIndex >= 0)
            _outputDeviceIndex = OutputCombo.SelectedIndex - 1;   // item 0 → -1 (wave mapper)
        if (_loading) return;
        if (_engine.IsRunning) StartEngine();
        SaveSettings();
    }

    // ── Engine ────────────────────────────────────────────────────────────────

    private void StartEngine()
    {
        try
        {
            _engine.PitchSemitones        = GetSliderVal(PitchSlider);
            _engine.FormantPitchSemitones = GetSliderVal(FormantSlider);
            _engine.FxEnabled             = _fxEnabled;
            _engine.Start(_outputDeviceIndex);

            _engine.ReverbWet      = (float)ReverbWetKnob.Value;
            _engine.ReverbRoomSize = (float)ReverbRoomKnob.Value;
            _engine.ReverbDamping  = (float)ReverbDampKnob.Value;
            _engine.DelayWet       = (float)DelayWetKnob.Value;
            _engine.DelayTimeMs    = (float)DelayTimeKnob.Value;
            _engine.DelayFeedback  = (float)DelayFbKnob.Value;
            _engine.ChorusWet      = (float)ChorusWetKnob.Value;
            _engine.ChorusRate     = (float)ChorusRateKnob.Value;
            _engine.ChorusDepth    = (float)ChorusDepthKnob.Value;

            SetRunning();
        }
        catch (Exception ex) { SetError(ex.Message); }
    }

    private static float GetSliderVal(Slider s) =>
        (float)(Math.Round(s.Value * 10) / 10.0);

    // ── Pitch sliders ─────────────────────────────────────────────────────────

    private bool _snapGuard;

    private void PitchSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PitchLabel == null) return;
        if (SnapNearZero(PitchSlider, 0.25)) return;
        float val = GetSliderVal(PitchSlider);
        PitchLabel.Text = val >= 0 ? $"+{val:F1}" : $"{val:F1}";
        _engine.PitchSemitones = val;
    }

    private void FormantSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FormantLabel == null) return;
        if (SnapNearZero(FormantSlider, 0.25)) return;
        float val = GetSliderVal(FormantSlider);
        FormantLabel.Text = val >= 0 ? $"+{val:F1}" : $"{val:F1}";
        _engine.FormantPitchSemitones = val;
    }

    private bool SnapNearZero(Slider s, double snapRange)
    {
        if (_snapGuard || s.Value == 0 || Math.Abs(s.Value) >= snapRange) return false;
        _snapGuard = true;
        s.Value = 0;
        _snapGuard = false;
        return true;
    }

    // Quadratic slowdown as value approaches 0 — gives a magnetic "pull" feel
    private static double MagneticZero(double raw, double snapRange)
    {
        if (Math.Abs(raw) >= snapRange) return raw;
        double t = Math.Abs(raw) / snapRange;
        return Math.Sign(raw) * snapRange * t * t;
    }

    private void ResetPitch_Click(object sender, RoutedEventArgs e)   => PitchSlider.Value   = 0;
    private void ResetFormant_Click(object sender, RoutedEventArgs e) => FormantSlider.Value = 0;

    // ── Slider jump-to-click ──────────────────────────────────────────────────

    private void PitchSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e) =>
        JumpSliderToClick((Slider)sender, e);

    private void FormantSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e) =>
        JumpSliderToClick((Slider)sender, e);

    private static void JumpSliderToClick(Slider slider, MouseButtonEventArgs e)
    {
        const double margin = 8.0;
        double pos   = e.GetPosition(slider).X;
        double ratio = Math.Clamp((pos - margin) / (slider.ActualWidth - margin * 2), 0, 1);
        slider.Value = slider.Minimum + ratio * (slider.Maximum - slider.Minimum);
    }

    // ── Pitch label drag & edit ───────────────────────────────────────────────

    private void PitchLabel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _pitchDragging   = true;
        _pitchDragMoved  = false;
        _pitchDragOrigin = e.GetPosition(this);
        _pitchDragVal0   = PitchSlider.Value;
        PitchLabel.CaptureMouse();
        Mouse.OverrideCursor = System.Windows.Input.Cursors.None;
        e.Handled = true;
    }

    private void PitchLabel_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_pitchDragging) return;
        double dx = e.GetPosition(this).X - _pitchDragOrigin.X;
        if (Math.Abs(dx) > 3) _pitchDragMoved = true;
        PitchSlider.Value = Math.Clamp(MagneticZero(_pitchDragVal0 + dx / 10.0, 0.4), PitchSlider.Minimum, PitchSlider.Maximum);
    }

    private void PitchLabel_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pitchDragging) return;
        _pitchDragging = false;
        PitchLabel.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        if (!_pitchDragMoved)
        {
            PitchEditBox.Text = PitchSlider.Value.ToString("F1");
            PitchLabel.Visibility   = Visibility.Collapsed;
            PitchEditBox.Visibility = Visibility.Visible;
            PitchEditBox.SelectAll();
            PitchEditBox.Focus();
        }
        e.Handled = true;
    }

    private void PitchEditBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Enter)  { CommitPitchEdit();  e.Handled = true; }
        if (e.Key == Key.Escape) { CancelPitchEdit();  e.Handled = true; }
    }

    private void PitchEditBox_LostFocus(object sender, RoutedEventArgs e) => CommitPitchEdit();

    private void CommitPitchEdit()
    {
        if (PitchEditBox.Visibility != Visibility.Visible) return;
        if (double.TryParse(PitchEditBox.Text,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v))
            PitchSlider.Value = Math.Clamp(v, PitchSlider.Minimum, PitchSlider.Maximum);
        CancelPitchEdit();
    }

    private void CancelPitchEdit()
    {
        PitchEditBox.Visibility = Visibility.Collapsed;
        PitchLabel.Visibility   = Visibility.Visible;
    }

    // ── Formant label drag & edit ─────────────────────────────────────────────

    private void FormantLabel_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _formantDragging   = true;
        _formantDragMoved  = false;
        _formantDragOrigin = e.GetPosition(this);
        _formantDragVal0   = FormantSlider.Value;
        FormantLabel.CaptureMouse();
        Mouse.OverrideCursor = System.Windows.Input.Cursors.None;
        e.Handled = true;
    }

    private void FormantLabel_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_formantDragging) return;
        double dx = e.GetPosition(this).X - _formantDragOrigin.X;
        if (Math.Abs(dx) > 3) _formantDragMoved = true;
        FormantSlider.Value = Math.Clamp(MagneticZero(_formantDragVal0 + dx / 10.0, 0.4), FormantSlider.Minimum, FormantSlider.Maximum);
    }

    private void FormantLabel_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_formantDragging) return;
        _formantDragging = false;
        FormantLabel.ReleaseMouseCapture();
        Mouse.OverrideCursor = null;
        if (!_formantDragMoved)
        {
            FormantEditBox.Text = FormantSlider.Value.ToString("F1");
            FormantLabel.Visibility   = Visibility.Collapsed;
            FormantEditBox.Visibility = Visibility.Visible;
            FormantEditBox.SelectAll();
            FormantEditBox.Focus();
        }
        e.Handled = true;
    }

    private void FormantEditBox_KeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Enter)  { CommitFormantEdit(); e.Handled = true; }
        if (e.Key == Key.Escape) { CancelFormantEdit(); e.Handled = true; }
    }

    private void FormantEditBox_LostFocus(object sender, RoutedEventArgs e) => CommitFormantEdit();

    private void CommitFormantEdit()
    {
        if (FormantEditBox.Visibility != Visibility.Visible) return;
        if (double.TryParse(FormantEditBox.Text,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v))
            FormantSlider.Value = Math.Clamp(v, FormantSlider.Minimum, FormantSlider.Maximum);
        CancelFormantEdit();
    }

    private void CancelFormantEdit()
    {
        FormantEditBox.Visibility = Visibility.Collapsed;
        FormantLabel.Visibility   = Visibility.Visible;
    }

    // ── Animations ────────────────────────────────────────────────────────────

    private static void AnimateMaxHeight(FrameworkElement el, double to, double ms = 220,
                                          Action? onCompleted = null, IEasingFunction? easing = null)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms))
            { EasingFunction = easing ?? new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (onCompleted != null) anim.Completed += (_, _) => onCompleted();
        el.BeginAnimation(FrameworkElement.MaxHeightProperty, anim);
    }

    private void UpdateNaturalHeight(bool autoExpand = false, double measureWidth = 0)
    {
        double w = measureWidth > 0 ? measureWidth
                 : (ShadeContent.ActualWidth > 0 ? ShadeContent.ActualWidth : 290);
        ShadeContent.Measure(new System.Windows.Size(w, double.PositiveInfinity));
        _naturalHeight = _headerHeight + ShadeContent.DesiredSize.Height;
        MaxHeight      = _naturalHeight;
        if (autoExpand && ActualHeight < _naturalHeight)
            Height = _naturalHeight;
    }

    // ── FX panel toggle & enable ──────────────────────────────────────────────

    private void ToggleFx_Click(object sender, RoutedEventArgs e)
    {
        _fxOpen = !_fxOpen;
        bool open = _fxOpen;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        double mw = ShadeContent.ActualWidth > 0 ? ShadeContent.ActualWidth : ActualWidth;
        double fxH = 0;
        if (FxPanel.Child is FrameworkElement fxChild)
        {
            fxChild.Measure(new System.Windows.Size(mw, double.PositiveInfinity));
            fxH = fxChild.DesiredSize.Height;
        }

        if (open)
        {
            FxPanel.Opacity = 0;
            _heightBeforeFx = ActualHeight;
            double targetH = ActualHeight + fxH;
            _naturalHeight = targetH;
            MaxHeight = targetH;

            AnimateMaxHeight(FxPanel, 500, 260);
            double fromH = ActualHeight;
            var ha = new DoubleAnimation(fromH, targetH, TimeSpan.FromMilliseconds(260))
                { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
            ha.Completed += (_, _) => { Height = targetH; BeginAnimation(HeightProperty, null); };
            BeginAnimation(HeightProperty, ha);
        }
        else
        {
            double targetH = Math.Clamp(_heightBeforeFx > 0 ? _heightBeforeFx : ActualHeight - fxH,
                                        _headerHeight + 20, ActualHeight);
            _heightBeforeFx = 0;

            AnimateMaxHeight(FxPanel, 0, 220);
            double fromH = ActualHeight;
            var ha = new DoubleAnimation(fromH, targetH, TimeSpan.FromMilliseconds(220))
                { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
            ha.Completed += (_, _) => { Height = targetH; BeginAnimation(HeightProperty, null); UpdateNaturalHeight(); };
            BeginAnimation(HeightProperty, ha);
        }

        FxPanel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(open ? 0 : 1, open ? 1 : 0,
                TimeSpan.FromMilliseconds(open ? 280 : 160)) { EasingFunction = ease });
        if (FxBtn.Template.FindName("FxArrow", FxBtn) is TextBlock arrow)
            arrow.Text = open ? " ▾" : " ▸";
    }

    private void FxOnOff_Click(object sender, MouseButtonEventArgs e)
    {
        _fxEnabled = !_fxEnabled;
        _engine.FxEnabled = _fxEnabled;

        var blue = (System.Windows.Application.Current.Resources["BrushBlue"] as SolidColorBrush)?.Color
                   ?? WpfColor.FromRgb(0x89, 0xB4, 0xFA);
        var targetPill = _fxEnabled ? blue : WpfColor.FromRgb(0x2E, 0x2E, 0x45);
        var targetDot  = _fxEnabled ? Colors.White : WpfColor.FromRgb(0x4A, 0x4A, 0x6A);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var dotPos = new ThicknessAnimation(
            new Thickness(_fxEnabled ? 15 : 2, 0, 0, 0), TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };
        FxPillDot.BeginAnimation(FrameworkElement.MarginProperty, dotPos);

        if (FxPill.Background is SolidColorBrush pillBg)
            pillBg.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(targetPill, TimeSpan.FromMilliseconds(180)));
        if (FxPillDot.Fill is SolidColorBrush dotFill)
            dotFill.BeginAnimation(SolidColorBrush.ColorProperty,
                new ColorAnimation(targetDot, TimeSpan.FromMilliseconds(180)));

        e.Handled = true;
    }

    // ── FX section toggles ────────────────────────────────────────────────────

    private void ReverbToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _reverbOpen = !_reverbOpen;
        AnimateSectionToggle(ReverbSection, _reverbOpen, ReverbArrow);
    }

    private void DelayToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _delayOpen = !_delayOpen;
        AnimateSectionToggle(DelaySection, _delayOpen, DelayArrow);
    }

    private void ChorusToggle_Click(object sender, MouseButtonEventArgs e)
    {
        _chorusOpen = !_chorusOpen;
        AnimateSectionToggle(ChorusSection, _chorusOpen, ChorusArrow);
    }

    private void AnimateSectionToggle(Border section, bool open, TextBlock arrow)
    {
        arrow.Text = open ? "▾" : "▸";

        double mw = ShadeContent.ActualWidth > 0 ? ShadeContent.ActualWidth : ActualWidth;
        double sectionH = 0;
        if (section.Child is FrameworkElement content)
        {
            content.Measure(new System.Windows.Size(mw, double.PositiveInfinity));
            sectionH = content.DesiredSize.Height;
        }

        double fromH  = ActualHeight;
        double targetH = open
            ? ActualHeight + sectionH
            : Math.Max(ActualHeight - sectionH, _headerHeight + 20);

        if (open) { _naturalHeight = targetH; MaxHeight = targetH; }

        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        AnimateMaxHeight(section, open ? 90 : 0, 200, easing: ease);
        var ha = new DoubleAnimation(fromH, targetH, TimeSpan.FromMilliseconds(200))
            { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
        ha.Completed += (_, _) => { Height = targetH; BeginAnimation(HeightProperty, null); UpdateNaturalHeight(); };
        BeginAnimation(HeightProperty, ha);
    }

    // ── Theme ─────────────────────────────────────────────────────────────────

    private void HueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (HueLabel == null) return;
        if (SnapNearZero(HueSlider, 5.0)) return;
        float shift = (float)e.NewValue;
        HueLabel.Text = shift >= 0 ? $"+{shift:F0}°" : $"{shift:F0}°";
        ThemeService.ApplyHueShift(shift);
    }

    private void BrilloSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BrilloLabel == null) return;
        int val = (int)e.NewValue;
        BrilloLabel.Text = $"{val}%";
        ThemeService.ApplyTextBrightness(val);
    }

    private void TextColor_Click(object sender, MouseButtonEventArgs e)
    {
        int mode = int.Parse(((FrameworkElement)sender).Tag!.ToString()!);
        ThemeService.ApplyTextColorMode(mode);
        HighlightTextBtns();
    }

    private void HighlightTextBtns()
    {
        bool color = ThemeService.TextColorMode == 2;
        TxtBtn0.SetResourceReference(Border.BorderBrushProperty, color ? "BrushBorder" : "BrushBlue");
        TxtBtn0.BorderThickness = new Thickness(color ? 1 : 2);
        TxtBtn2.SetResourceReference(Border.BorderBrushProperty, color ? "BrushBlue" : "BrushBorder");
        TxtBtn2.BorderThickness = new Thickness(color ? 2 : 1);
    }

    private void HighlightBgBtns(int preset)
    {
        var btns = new[] { BgBtn0, BgBtn1, BgBtn2, BgBtn3, BgBtn4 };
        for (int i = 0; i < btns.Length; i++)
        {
            btns[i].SetResourceReference(Border.BorderBrushProperty, i == preset ? "BrushBlue" : "BrushBorder");
            btns[i].BorderThickness = new Thickness(i == preset ? 2 : 1);
        }
    }

    private void BgPreset_Click(object sender, MouseButtonEventArgs e)
    {
        int preset = int.Parse(((FrameworkElement)sender).Tag!.ToString()!);
        ThemeService.ApplyBgPreset(preset);
        HighlightBgBtns(preset);
        e.Handled = true;
    }

    // ── FX knob handlers ──────────────────────────────────────────────────────

    private void ReverbWetKnob_Changed(object s, RoutedPropertyChangedEventArgs<double> e)  => _engine.ReverbWet      = (float)e.NewValue;
    private void ReverbRoomKnob_Changed(object s, RoutedPropertyChangedEventArgs<double> e) => _engine.ReverbRoomSize = (float)e.NewValue;
    private void ReverbDampKnob_Changed(object s, RoutedPropertyChangedEventArgs<double> e) => _engine.ReverbDamping  = (float)e.NewValue;
    private void DelayWetKnob_Changed(object s, RoutedPropertyChangedEventArgs<double> e)   => _engine.DelayWet       = (float)e.NewValue;
    private void DelayTimeKnob_Changed(object s, RoutedPropertyChangedEventArgs<double> e)  => _engine.DelayTimeMs    = (float)e.NewValue;
    private void DelayFbKnob_Changed(object s, RoutedPropertyChangedEventArgs<double> e)    => _engine.DelayFeedback  = (float)e.NewValue;
    private void ChorusWetKnob_Changed(object s, RoutedPropertyChangedEventArgs<double> e)  => _engine.ChorusWet      = (float)e.NewValue;
    private void ChorusRateKnob_Changed(object s, RoutedPropertyChangedEventArgs<double> e) => _engine.ChorusRate     = (float)e.NewValue;
    private void ChorusDepthKnob_Changed(object s, RoutedPropertyChangedEventArgs<double> e)=> _engine.ChorusDepth    = (float)e.NewValue;

    // ── Settings persistence ──────────────────────────────────────────────────

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            var s = SettingsStore.Load();

            // Custom colors must be restored before applying theme state
            ThemeService.RestoreCustomColors(s.CustomColors);
            ThemeService.ApplyBgPreset(s.BgPreset);
            ThemeService.ApplyHueShift(s.HueShift);
            ThemeService.ApplyTextColorMode(s.TextColorMode);
            ThemeService.ApplyTextBrightness(s.TextBrightness);

            // Sync UI sliders (suppress save via _loading flag)
            HueSlider.Value    = s.HueShift;
            BrilloSlider.Value = s.TextBrightness;

            HighlightBgBtns(s.BgPreset);

            HighlightTextBtns();
            UpdatePinBtn();

            // Restore pitch engine
            _engine.PitchEngine = (AudioEngine.PitchEngineMode)s.PitchEngine;
            UpdateEngineButtons(s.PitchEngine);
            _minimizeToTray = s.MinimizeToTray;
            UpdateMinimizeButtons();

            // Restore output device by name
            if (s.OutputDeviceName == DefaultDeviceSentinel)
            {
                OutputCombo.SelectedIndex = 0;
            }
            else if (!string.IsNullOrEmpty(s.OutputDeviceName))
            {
                var devices = AudioEngine.GetOutputDeviceNames();
                int idx = devices.FindIndex(d => d == s.OutputDeviceName);
                if (idx >= 0) OutputCombo.SelectedIndex = idx + 1;
            }

            // Restore capture source (app)
            _targetProcessName = s.TargetProcessName;
            _recentApps        = s.RecentApps ?? new();
            _favoriteApps      = s.FavoriteApps ?? new();
            LoadAppSessions();
        }
        finally { _loading = false; }
    }

    private void SaveSettings()
    {
        if (_loading) return;
        var devices = AudioEngine.GetOutputDeviceNames();
        string devName = OutputCombo.SelectedIndex == 0
            ? DefaultDeviceSentinel
            : OutputCombo.SelectedIndex >= 1 && OutputCombo.SelectedIndex - 1 < devices.Count
                ? devices[OutputCombo.SelectedIndex - 1] : "";

        SettingsStore.Save(new AppSettings
        {
            PitchEngine      = (int)_engine.PitchEngine,
            MinimizeToTray   = _minimizeToTray,
            SkippedUpdateVersion = _skippedUpdate,
            HueShift         = ThemeService.HueShift,
            BgPreset         = ThemeService.BgPreset,
            TextColorMode    = ThemeService.TextColorMode,
            TextBrightness   = ThemeService.TextBrightness,
            OutputDeviceName = devName,
            TargetProcessName = _targetProcessName,
            RecentApps       = _recentApps,
            FavoriteApps     = _favoriteApps,
            CustomColors     = ThemeService.GetCustomColorsForSave(),
        });
    }

    // ── Pitch engine toggle ───────────────────────────────────────────────────

    private void PitchEngine_Click(object sender, MouseButtonEventArgs e)
    {
        int mode = int.Parse(((FrameworkElement)sender).Tag!.ToString()!);
        _engine.PitchEngine = (AudioEngine.PitchEngineMode)mode;
        UpdateEngineButtons(mode);
        if (_engine.IsRunning) StartEngine();
        SaveSettings();
    }

    private void UpdateEngineButtons(int mode)
    {
        var btns = new[] { EngBtn0, EngBtn1 };
        var dots = new[] { EngDot0, EngDot1 };
        var txts = new[] { EngTxt0, EngTxt1 };
        for (int i = 0; i < btns.Length; i++)
        {
            bool active = i == mode;
            btns[i].SetResourceReference(Border.BorderBrushProperty, active ? "BrushBlue" : "BrushBorder");
            btns[i].BorderThickness = new Thickness(active ? 2 : 1);
            dots[i].SetResourceReference(System.Windows.Shapes.Shape.FillProperty, active ? "BrushBlue" : "BrushBorder");
            txts[i].SetResourceReference(TextBlock.ForegroundProperty, active ? "BrushText" : "BrushMuted");
        }
    }

    // ── Settings panel toggle ─────────────────────────────────────────────────

    private void ToggleSettings_Click(object sender, RoutedEventArgs e)
    {
        _settingsOpen = !_settingsOpen;
        if (_settingsOpen)
            _heightBeforeSettings = ActualHeight;   // save before expanding

        SettingsPanel.Visibility = _settingsOpen ? Visibility.Visible : Visibility.Collapsed;
        MainPanel.Visibility     = _settingsOpen ? Visibility.Collapsed : Visibility.Visible;
        SettingsBtn.Foreground   = new SolidColorBrush(_settingsOpen
            ? WpfColor.FromRgb(0x89, 0xB4, 0xFA)
            : WpfColor.FromRgb(0x6C, 0x70, 0x86));
        bool open = _settingsOpen;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            UpdateNaturalHeight(autoExpand: open);
            if (!open && _heightBeforeSettings > 0)
                Height = Math.Min(_heightBeforeSettings, _naturalHeight); // restore, but never exceed content
        }));
    }

    // ── Status helpers ────────────────────────────────────────────────────────

    private void SetRunning()
    {
        StatusLogo.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "BrushBlue");
        StatusLabel.SetResourceReference(TextBlock.ForegroundProperty, "BrushMuted");
        StatusLabel.Text = _routedPid > 0
            ? $"Procesando {_selectedAppName} en vivo."
            : "Procesando audio en vivo.";
    }

    private void SetStopped()
    {
        StatusLogo.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "BrushMuted");
        StatusLabel.SetResourceReference(TextBlock.ForegroundProperty, "BrushMuted");
        StatusLabel.Text = "Sin audio. Revisá el enrutamiento.";
    }

    private void SetError(string msg)
    {
        StatusLogo.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "BrushRed");
        StatusLabel.Foreground = new SolidColorBrush(WpfColor.FromRgb(0xF3, 0x8B, 0xA8));
        StatusLabel.Text = $"Error: {msg}";
    }

    // ── Window chrome ─────────────────────────────────────────────────────────

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (PitchEditBox.Visibility == Visibility.Visible && !PitchEditBox.IsMouseOver)
            CommitPitchEdit();
        if (FormantEditBox.Visibility == Visibility.Visible && !FormantEditBox.IsMouseOver)
            CommitFormantEdit();
        base.OnPreviewMouseLeftButtonDown(e);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyRoundedRegion();
        if (!IsLoaded || _titleLabelNaturalWidth == 0) return;

        if (e.WidthChanged)
        {
            double oldNaturalH = _naturalHeight;
            UpdateNaturalHeight(measureWidth: e.NewSize.Width);
            double delta = _naturalHeight - oldNaturalH;
            if (Math.Abs(delta) > 0.5)
                Height = Math.Clamp(ActualHeight + delta, MinHeight, _naturalHeight);
        }

        const double leftMargin  = 16;
        const double rightMargin = 12;
        const double ellipseW    = 7 + 8;
        const double buffer      = 12;
        double titleRight = leftMargin + ellipseW + _titleLabelNaturalWidth;
        double btnsLeft   = ActualWidth - rightMargin - HeaderBtns.ActualWidth;
        bool shouldShow = titleRight + buffer <= btnsLeft;
        if (shouldShow == _titleVisible) return;
        _titleVisible = shouldShow;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        if (shouldShow)
        {
            TitleLabel.Visibility = Visibility.Visible;
            TitleLabel.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        }
        else
        {
            var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease };
            anim.Completed += (_, _) => { TitleLabel.Visibility = Visibility.Collapsed; TitleLabel.Opacity = 1; };
            TitleLabel.BeginAnimation(OpacityProperty, anim);
        }
    }

    // ── Window chrome ─────────────────────────────────────────────────────────

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);
    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint crColor);

    private const int DWMWA_NCRENDERING_POLICY       = 2;
    private const int DWMNCRP_ENABLED                = 2;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE  = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND                   = 2;
    private const int GCLP_HBRBACKGROUND             = -10;
    private const int WM_NCLBUTTONDBLCLK = 0x00A3;
    private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14,
                      HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource src)
        {
            int ncrp = DWMNCRP_ENABLED;
            DwmSetWindowAttribute(src.Handle, DWMWA_NCRENDERING_POLICY, ref ncrp, sizeof(int));

            // Frame DWM oscuro + fondo de clase oscuro: sin franjas blancas al redimensionar
            int dark = 1;
            DwmSetWindowAttribute(src.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            var bg = System.Windows.Application.Current.Resources["BrushBg"] as SolidColorBrush;
            var c  = bg?.Color ?? WpfColor.FromRgb(0x1E, 0x1E, 0x2E);
            SetClassLongPtr(src.Handle, GCLP_HBRBACKGROUND,
                CreateSolidBrush((uint)(c.R | c.G << 8 | c.B << 16)));

            if (Environment.OSVersion.Version.Build >= 22000)
            {
                int pref = DWMWCP_ROUND;
                DwmSetWindowAttribute(src.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
            src.AddHook(WndProc);
        }
        ApplyRoundedRegion();
    }

    private void ApplyRoundedRegion()
    {
        if (Environment.OSVersion.Version.Build >= 22000) return; // Win11: DWM handles corners natively
        if (!(PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource src)) return;
        var m = src.CompositionTarget?.TransformToDevice;
        double dpiX = m?.M11 ?? 1.0;
        double dpiY = m?.M22 ?? 1.0;
        int w = (int)Math.Ceiling(ActualWidth  * dpiX);
        int h = (int)Math.Ceiling(ActualHeight * dpiY);
        int r = (int)Math.Round(14 * dpiX * 2); // ellipse diameter = radius * 2
        SetWindowRgn(src.Handle, CreateRoundRectRgn(0, 0, w, h, r, r), true);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCLBUTTONDBLCLK)
        {
            int ht = wParam.ToInt32();
            bool isEdge = ht is HTLEFT or HTRIGHT or HTTOP or HTBOTTOM
                              or HTTOPLEFT or HTTOPRIGHT or HTBOTTOMLEFT or HTBOTTOMRIGHT;
            if (isEdge)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateNaturalHeight();
                    if (ActualHeight < _naturalHeight - 1)
                    {
                        // Contraída → expandir, recordando la altura previa para poder volver
                        _heightBeforeEdgeExpand = ActualHeight;
                        AnimateHeightTo(_naturalHeight);
                    }
                    else if (_heightBeforeEdgeExpand > 0 && _heightBeforeEdgeExpand < _naturalHeight - 1)
                    {
                        // Ya expandida → volver a como estaba antes del doble click
                        AnimateHeightTo(_heightBeforeEdgeExpand);
                    }
                }));
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private double _heightBeforeEdgeExpand = 0;

    private void AnimateHeightTo(double target)
    {
        var anim = new DoubleAnimation(ActualHeight, target, TimeSpan.FromMilliseconds(200))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        anim.Completed += (_, _) => { Height = target; BeginAnimation(HeightProperty, null); };
        BeginAnimation(HeightProperty, anim);
    }

    // ── Custom label / slider colors ──────────────────────────────────────────

    private void ApplyThemeChrome()
    {
        bool isWhite = ThemeService.BgPreset == 4;
        RootBorder.BorderThickness = isWhite ? new Thickness(0) : new Thickness(1);
    }

    private void ApplyCustomLabelColors()
    {
        UpdateLabelColor("label:PITCH",   PitchHeader);
        UpdateLabelColor("label:FORMANT", FormantHeader);
        UpdateLabelColor("label:REVERB",  ReverbHeader);
        UpdateLabelColor("label:DELAY",   DelayHeader);
        UpdateLabelColor("label:CHORUS",  ChorusHeader);

        // FX label is inside a ControlTemplate — access via FindName
        if (FxBtn.Template?.FindName("FxTextLabel", FxBtn) is TextBlock fxTb)
            UpdateLabelColor("label:FX", fxTb, "BrushBlue");

        // Pitch/Formant value labels — use isolated fill resources, not BrushBlue/BrushMauve
        UpdateLabelColor("sliderAccent",  PitchLabel,   "BrushPitchFill");
        UpdateLabelColor("formantAccent", FormantLabel, "BrushFormantFill");

        // FX pill follows accent color (only update if not frozen / animation-safe)
        var blue = (System.Windows.Application.Current.Resources["BrushBlue"] as SolidColorBrush)?.Color
                   ?? WpfColor.FromRgb(0x89, 0xB4, 0xFA);
        if (FxPill.Background is SolidColorBrush pillBrush && !pillBrush.IsFrozen && _fxEnabled)
            pillBrush.Color = blue;
    }

    private static void UpdateLabelColor(string key, TextBlock label, string fallbackResource = "BrushMuted2")
    {
        var c = ThemeService.GetCustomColor(key);
        if (c.HasValue)
        {
            var b = new SolidColorBrush(c.Value);
            b.Freeze();
            label.Foreground = b;
        }
        else
            label.SetResourceReference(TextBlock.ForegroundProperty, fallbackResource);
    }

    private void SectionLabel_ColorPick(object sender, MouseButtonEventArgs e)
    {
        var tb  = (TextBlock)sender;
        string key = tb.Tag?.ToString() ?? "";
        ShowColorPickerPopup(tb, c => ThemeService.SetCustomColor(key, c));
        e.Handled = true;
    }

    private void Slider_ColorPick(object sender, MouseButtonEventArgs e)
    {
        ShowColorPickerPopup((UIElement)sender, c => ThemeService.SetCustomColor("sliderAccent", c));
        e.Handled = true;
    }

    private void FormantLabel_ColorPick(object sender, MouseButtonEventArgs e)
    {
        ShowColorPickerPopup((UIElement)sender, c => ThemeService.SetCustomColor("formantAccent", c));
        e.Handled = true;
    }

    private void ResetColors_Click(object sender, RoutedEventArgs e)
    {
        HueSlider.Value = 0;
        ThemeService.ResetCustomColors();
        ThemeService.ApplyTextColorMode(0);
        BrilloSlider.Value = 50;
        ApplyCustomLabelColors();
        SaveSettings();
        HighlightTextBtns();
    }

    // ── Recorder ──────────────────────────────────────────────────────────────

    private void RecBtn_Click(object sender, RoutedEventArgs e)
    {
        _recPanelOpen = !_recPanelOpen;
        bool open = _recPanelOpen;
        AnimateMaxHeight(RecordPanel, open ? 175 : 0, 220, () => UpdateNaturalHeight(autoExpand: open));

        if (_recPanelOpen)
        {
            StopPreview();
            RecPlayBtn.Visibility  = Visibility.Collapsed;
            RecPauseBtn.Visibility = Visibility.Visible;
            _recorder.Reset();
            Waveform.ResetScrolling();
            RecSaveBtn.IsEnabled = false;
            RecPauseIcon.Text    = "⏸";
            RecTimerLabel.Text   = "0:00.0";
            SetRecDot();

            _recorder.SamplesAvailable += OnRecorderSamples;
            _recorder.Start();
            SetRecDot();

            _recTimer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromMilliseconds(100) };
            _recTimer.Tick += RecTimer_Tick;
            _recTimer.Start();

            RecBtnIcon.Foreground = new SolidColorBrush(WpfColor.FromRgb(0xF0, 0x47, 0x5A));
        }
        else
        {
            StopRecorderCleanup();
            RecBtnIcon.ClearValue(TextBlock.ForegroundProperty);
        }
    }

    // ── Preview de la grabación ──────────────────────────────────────────────

    private NAudio.Wave.WaveOutEvent? _previewOut;
    private PreviewSampleProvider?    _previewSrc;
    private System.Windows.Threading.DispatcherTimer? _previewTimer;

    private void RecPlay_Click(object sender, RoutedEventArgs e)
    {
        if (_previewOut != null) { StopPreview(); return; }

        var src = _recorder.CreatePreview(Waveform.TrimStartRatio, Waveform.TrimEndRatio, _normEnabled);
        if (src == null) return;

        var output = new NAudio.Wave.WaveOutEvent { DesiredLatency = 100 };
        try { output.Init(new NAudio.Wave.SampleProviders.SampleToWaveProvider(src)); }
        catch { output.Dispose(); return; }

        _previewSrc = src;
        _previewOut = output;
        output.PlaybackStopped += (_, _) => Dispatcher.BeginInvoke(() => { if (_previewOut == output) StopPreview(); });
        output.Play();

        RecPlayIcon.Text = "■";
        _previewTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _previewTimer.Tick += (_, _) => { if (_previewSrc != null) Waveform.PlayheadRatio = _previewSrc.PositionRatio; };
        _previewTimer.Start();
    }

    private void StopPreview()
    {
        _previewTimer?.Stop();
        _previewTimer = null;
        var output = _previewOut;
        _previewOut = null;
        _previewSrc = null;
        output?.Stop();
        output?.Dispose();
        Waveform.PlayheadRatio = -1;
        RecPlayIcon.Text = "▶";
    }

    private void StopRecorderCleanup()
    {
        StopPreview();
        _recTimer?.Stop();
        _recTimer = null;
        _recorder.SamplesAvailable -= OnRecorderSamples;
        if (_recorder.State == RecorderEngine.RecordState.Recording ||
            _recorder.State == RecorderEngine.RecordState.Paused)
            _recorder.Stop();
    }

    private void RecPause_Click(object sender, RoutedEventArgs e)
    {
        if (_recorder.State == RecorderEngine.RecordState.Recording)
        {
            _recorder.Pause();
            RecPauseIcon.Text = "▶";
        }
        else if (_recorder.State == RecorderEngine.RecordState.Paused)
        {
            _recorder.Resume();
            RecPauseIcon.Text = "⏸";
        }
        SetRecDot();
    }

    private void RecStop_Click(object sender, RoutedEventArgs e)
    {
        _recTimer?.Stop();
        _recTimer = null;
        _recorder.SamplesAvailable -= OnRecorderSamples;
        _recorder.Stop();
        SetRecDot();
        RecTimerLabel.Text = FormatDuration(_recorder.Duration);

        float[] all = _recorder.GetAllSamples();
        if (all.Length > 0)
        {
            Waveform.SetReviewData(all);
            RecSaveBtn.IsEnabled = true;
            RecPauseBtn.Visibility = Visibility.Collapsed;
            RecPlayBtn.Visibility  = Visibility.Visible;
            UpdateNormVisual();
        }
    }

    private void RecSave_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title      = "Guardar grabación",
            Filter     = "WAV audio (*.wav)|*.wav",
            DefaultExt = ".wav",
            FileName   = $"grabacion_{DateTime.Now:yyyyMMdd_HHmmss}",
        };
        if (dlg.ShowDialog() != true) return;
        _recorder.SaveWav(dlg.FileName, Waveform.TrimStartRatio, Waveform.TrimEndRatio, _normEnabled);
    }

    private void NormBtn_Click(object sender, MouseButtonEventArgs e)
    {
        _normEnabled = !_normEnabled;
        var accent = (System.Windows.Media.Brush?)System.Windows.Application.Current.Resources["BrushGreen"]
                     ?? new SolidColorBrush(WpfColor.FromRgb(0xA6, 0xE3, 0xA1));
        var border = (System.Windows.Media.Brush?)System.Windows.Application.Current.Resources["BrushBorder"]
                     ?? new SolidColorBrush(WpfColor.FromRgb(0x3D, 0x3D, 0x56));
        NormBtn.BorderBrush     = _normEnabled ? accent : border;
        NormBtn.BorderThickness = new Thickness(_normEnabled ? 2 : 1);
        NormDot.Fill            = _normEnabled ? accent : border;
        if (NormBtn.Child is StackPanel sp && sp.Children[1] is TextBlock tb)
            tb.Foreground = _normEnabled
                ? (System.Windows.Media.Brush?)System.Windows.Application.Current.Resources["BrushText"] ?? new SolidColorBrush(Colors.White)
                : (System.Windows.Media.Brush?)System.Windows.Application.Current.Resources["BrushMuted"] ?? new SolidColorBrush(WpfColor.FromRgb(0x6C, 0x70, 0x86));

        UpdateNormVisual();
    }

    private void UpdateNormVisual()
    {
        if (!_normEnabled || Waveform.Mode != WaveformControl.DisplayMode.Review)
        {
            Waveform.NormalizeGain   = 1f;
            NormGainLabel.Visibility = Visibility.Collapsed;
            return;
        }

        float[] all = _recorder.GetAllSamples();
        float peak = 0f;
        foreach (float s in all) { float a = Math.Abs(s); if (a > peak) peak = a; }

        if (peak <= 0.001f)
        {
            Waveform.NormalizeGain   = 1f;
            NormGainLabel.Visibility = Visibility.Collapsed;
            return;
        }

        float gain = 0.95f / peak;
        double db  = 20.0 * Math.Log10(gain);
        Waveform.NormalizeGain    = gain;
        NormGainLabel.Text        = db >= 0 ? $"+{db:F1} dB" : $"{db:F1} dB";
        NormGainLabel.Foreground  = gain >= 1f
            ? new SolidColorBrush(WpfColor.FromRgb(0xA6, 0xE3, 0xA1))   // verde — sube
            : new SolidColorBrush(WpfColor.FromRgb(0xF9, 0xE2, 0xAF));  // amarillo — baja
        NormGainLabel.Visibility  = Visibility.Visible;
    }

    private void OnRecorderSamples(object? sender, float[] chunk)
    {
        // audio thread — accumulate peak only
        float max = 0f;
        foreach (float s in chunk) { float a = Math.Abs(s); if (a > max) max = a; }
        lock (_recPeakLock) { if (max > _recPeakMax) _recPeakMax = max; }
    }

    private void RecTimer_Tick(object? sender, EventArgs e)
    {
        RecTimerLabel.Text = FormatDuration(_recorder.Duration);

        if (_recorder.State == RecorderEngine.RecordState.Recording)
        {
            float peak;
            lock (_recPeakLock) { peak = _recPeakMax; _recPeakMax = 0f; }
            Waveform.AddPeak(peak);

            _recDotFlash = !_recDotFlash;
            RecDot.Opacity = _recDotFlash ? 1.0 : 0.3;
        }
    }

    private void SetRecDot()
    {
        var state = _recorder.State;
        RecDot.Opacity = 1.0;
        RecDot.Fill = new SolidColorBrush(state switch
        {
            RecorderEngine.RecordState.Recording => WpfColor.FromRgb(0xF3, 0x8B, 0xA8),
            RecorderEngine.RecordState.Paused    => WpfColor.FromRgb(0xF9, 0xE2, 0xAF),
            _                                    => WpfColor.FromRgb(0x3D, 0x3D, 0x56),
        });
    }

    private static string FormatDuration(TimeSpan t) =>
        $"{(int)t.TotalMinutes}:{t.Seconds:D2}.{t.Milliseconds / 100}";

    private void ShowColorPickerPopup(UIElement target, Action<WpfColor> onSelect)
    {
        var grad = new System.Windows.Media.LinearGradientBrush
            { StartPoint = new WpfPoint(0, 0), EndPoint = new WpfPoint(1, 0) };
        for (int i = 0; i <= 12; i++)
        {
            ThemeService.HslToColor(i / 12f, 1f, 0.65f, out var c);
            grad.GradientStops.Add(new System.Windows.Media.GradientStop(
                ThemeService.ShiftHue(c, ThemeService.HueShift), i / 12.0));
        }

        var spectrum = new System.Windows.Controls.Border
        {
            Width = 160, Height = 22,
            CornerRadius = new CornerRadius(5),
            Background = grad,
            Cursor = System.Windows.Input.Cursors.Hand,
            ClipToBounds = true
        };

        var surfaceColor = (System.Windows.Application.Current.Resources["BrushSurface"] as SolidColorBrush)?.Color
                           ?? WpfColor.FromRgb(0x2A, 0x2A, 0x3D);
        var borderColor  = (System.Windows.Application.Current.Resources["BrushBorder"] as SolidColorBrush)?.Color
                           ?? WpfColor.FromRgb(0x3D, 0x3D, 0x56);

        var marker = new System.Windows.Shapes.Rectangle
        {
            Width = 3, Height = 22, Fill = System.Windows.Media.Brushes.White,
            Stroke = System.Windows.Media.Brushes.Black, StrokeThickness = 0.5,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            IsHitTestVisible = false, Visibility = Visibility.Hidden
        };
        spectrum.Child = new System.Windows.Controls.Grid { Children = { marker } };

        var swatch = new System.Windows.Controls.Border
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(4),
            BorderBrush = new SolidColorBrush(borderColor), BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 6, 0)
        };
        var hexLabel = new System.Windows.Controls.TextBlock
        {
            FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Text = "Arrastrá para elegir"
        };
        hexLabel.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "BrushSubtext");
        var info = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 7, 0, 0),
            Children = { swatch, hexLabel }
        };

        WpfColor ColorAt(double x, out double clampedX)
        {
            clampedX = Math.Clamp(x, 0, spectrum.ActualWidth);
            ThemeService.HslToColor((float)(clampedX / spectrum.ActualWidth), 1f, 0.65f, out var c);
            return c;
        }

        void Preview(double x, out WpfColor baseColor)
        {
            baseColor = ColorAt(x, out double cx);
            var shown = ThemeService.ShiftHue(baseColor, ThemeService.HueShift);
            swatch.Background = new SolidColorBrush(shown);
            hexLabel.Text = $"#{shown.R:X2}{shown.G:X2}{shown.B:X2}";
            marker.Margin = new Thickness(Math.Clamp(cx - 1.5, 0, spectrum.ActualWidth - 3), 0, 0, 0);
            marker.Visibility = Visibility.Visible;
        }

        var popup = new System.Windows.Controls.Primitives.Popup
        {
            Child = new System.Windows.Controls.Border
            {
                Child = new System.Windows.Controls.StackPanel
                    { Margin = new Thickness(8), Children = { spectrum, info } },
                Background = new SolidColorBrush(surfaceColor),
                BorderBrush = new SolidColorBrush(borderColor),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                    { BlurRadius = 14, Color = WpfColor.FromRgb(0,0,0), Opacity = 0.5, ShadowDepth = 3 }
            },
            PlacementTarget = target,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse,
            StaysOpen = false,
            AllowsTransparency = true
        };

        // Hover: solo muestra el color. Clic/arrastre: lo aplica en vivo. Soltar: cierra.
        spectrum.MouseMove += (s, ev) =>
        {
            Preview(ev.GetPosition(spectrum).X, out var c);
            if (spectrum.IsMouseCaptured) onSelect(c);
        };
        spectrum.MouseLeftButtonDown += (s, ev) =>
        {
            spectrum.CaptureMouse();
            Preview(ev.GetPosition(spectrum).X, out var c);
            onSelect(c);
            ev.Handled = true;
        };
        spectrum.MouseLeftButtonUp += (s, ev) =>
        {
            if (!spectrum.IsMouseCaptured) return;
            spectrum.ReleaseMouseCapture();
            popup.IsOpen = false;
            ev.Handled = true;
        };

        popup.IsOpen = true;
    }
}
