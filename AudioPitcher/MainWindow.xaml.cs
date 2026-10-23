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
using System.Windows.Media.Imaging;
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
        RootBorder.CornerRadius = new CornerRadius(WindowCorner);
        RestoreWindowPlacement();
        Loaded += OnLoaded;
        Closing += (_, _) => CaptureWindowPlacement();
        PreviewKeyDown += HotkeyCapture_PreviewKeyDown;
        // Click en cualquier otro lado mientras se espera una combinación → cancelar
        PreviewMouseDown += (_, _) =>
        {
            if (_capturingHotkey != null && !_hotkeyBoxes[_capturingHotkey].box.IsMouseOver) CancelHotkeyCapture();
        };
        Closed += (_, _) =>
        {
            UnregisterAllHotkeys();
            RestoreAllRoutings("cierre");
            FixDefaultOutputIfCable(onExit: true);
            SaveSettings();
            Log.Info("Cierre");
            _deviceWatcher?.Dispose();
            _trayIcon?.Dispose(); StopPreview(); _recorder.Dispose(); _engine.Dispose();
        };
        InitTray();
        // Apagado o cierre de sesión de Windows con la app abierta
        System.Windows.Application.Current.SessionEnding += (_, _) =>
        {
            RestoreAllRoutings("Windows se apaga");
            FixDefaultOutputIfCable(onExit: true);
        };
    }

    // ── Posición y tamaño de la ventana ─────────────────────────────────────────

    private double? _winLeft, _winTop, _winWidth, _winHeight;

    private void RestoreWindowPlacement()
    {
        var s = SettingsStore.Load();
        if (s.WindowWidth is not double w || s.WindowHeight is not double h || w <= 0 || h <= 0) return;
        _winWidth  = Width = Math.Max(w, MinWidth);
        _winHeight = h;   // se aplica al medir la altura natural (OnLoaded)

        // Solo si sigue dentro de una pantalla (p. ej. monitor desconectado → centrada)
        if (s.WindowLeft is double l && s.WindowTop is double t && IsOnScreen(l, t, Width))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            _winLeft = Left = l;
            _winTop  = Top  = t;
        }
    }

    // La franja superior de la ventana (por donde se arrastra) tiene que verse en algún monitor
    private static bool IsOnScreen(double left, double top, double width)
    {
        var primary = System.Windows.Forms.Screen.PrimaryScreen;
        double scale = primary != null && SystemParameters.PrimaryScreenWidth > 0
            ? primary.Bounds.Width / SystemParameters.PrimaryScreenWidth : 1.0;
        var strip = new System.Drawing.Rectangle((int)(left * scale), (int)(top * scale),
                                                 (int)(width * scale), (int)(40 * scale));
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var hit = System.Drawing.Rectangle.Intersect(screen.WorkingArea, strip);
            if (hit.Width >= 60 * scale && hit.Height >= 20 * scale) return true;
        }
        return false;
    }

    private void CaptureWindowPlacement()
    {
        if (!IsLoaded || ActualWidth <= 0) return;
        Rect b = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (b.IsEmpty || b.Width <= 0) return;
        // Con configuración o efectos abiertos, se guarda la altura de antes de abrirlos
        double h = _settingsOpen && _heightBeforeSettings > 0 ? _heightBeforeSettings
                 : _fxOpen && _heightBeforeFx > 0 ? _heightBeforeFx
                 : b.Height;
        (_winLeft, _winTop, _winWidth, _winHeight) = (b.Left, b.Top, b.Width, h);
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
        UpdateBtn.ToolTip = Loc.T("UpdateTo", info.Version.ToString(3));

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

    // ── Salida de Windows atrapada en VB-CABLE ───────────────────────────────
    // Si Windows quedó mandando todo al cable (lo hace a veces el instalador de VB-CABLE),
    // al cerrar Audio Pitcher no se escucharía nada. Se vuelve a la salida real:
    // al abrir (salvo en modo Sistema, que necesita el cable) y siempre al cerrar.
    private string _lastRealOutputId = "";

    private void FixDefaultOutputIfCable(bool onExit)
    {
        try
        {
            using var def = AudioEngine.GetDefaultRender();
            if (def == null) return;
            if (!AudioEngine.IsVbCable(def)) { _lastRealOutputId = def.ID; return; }
            if (!onExit && _routedPid == 0 && string.IsNullOrEmpty(_targetProcessName)) return; // modo Sistema

            using var real = AudioEngine.FindRealRenderDevice(_lastRealOutputId);
            if (real == null) { Log.Warn("La salida de Windows es VB-CABLE y no hay otra salida activa"); return; }
            AudioPolicyConfig.SetSystemDefaultRender(real.ID);
            _lastRealOutputId = real.ID;
            Log.Info($"Salida de Windows cambiada de '{def.FriendlyName}' a '{real.FriendlyName}' ({(onExit ? "al cerrar" : "al abrir")})");
        }
        catch (Exception ex) { Log.Error("No se pudo corregir la salida predeterminada", ex); }
    }

    private void ReportLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string path = Log.ExportReport();
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            System.Windows.MessageBox.Show(
                Loc.T("ReportReadyMsg", System.IO.Path.GetFileName(path)),
                Loc.T("ReportReady"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(Loc.T("ReportFailed", ex.Message), "Audio Pitcher",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
#if DEBUG
        RunDemoToolsIfRequested();
#endif
        VersionRun.Text = "v" + UpdateService.CurrentVersion.ToString(3);
        CheckForUpdates();
        _loading = true;   // prevent SaveSettings during initial device population
        LoadOutputDevices();

        DelayTimeKnob.TickValues = new[] { 100.0, 200.0, 300.0, 400.0, 500.0, 600.0, 700.0, 800.0 };

        if (!AudioEngine.IsRubberBandAvailable)
        {
            FormantSlider.IsEnabled        = false;
            FormantUnavailLabel.Visibility = Visibility.Visible;
        }
        if (!AudioEngine.IsSignalsmithAvailable)
            { }
        if (!AudioPolicyConfig.IsSupported)
            AppCombo.IsEnabled = false;
        ApplyLanguageTexts();
        Loc.Changed += OnLanguageChanged;

        // Set mutable brushes so animations can target them
        FxPill.Background = new SolidColorBrush(WpfColor.FromRgb(0x89, 0xB4, 0xFA));
        FxPillDot.Fill    = new SolidColorBrush(Colors.White);

        InitKnobColors();
        InitSegmentedControls();
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
        Log.Startup();
        FixDefaultOutputIfCable(onExit: false);
        StartEngine();
        if (SettingsStore.TakeUpdatedVersion() is string updated)
        {
            Log.Info($"Actualizado a {updated}");
            // Aviso temporal: a los segundos vuelve el estado que había
            var previous = _statusText;
            Func<string> notice = () => Loc.T("StatusUpdated", updated);
            SetStatus(notice);
            var hide = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            hide.Tick += (_, _) =>
            {
                hide.Stop();
                if (_statusText != notice) return;   // ya lo reemplazó otro estado
                if (previous != null) SetStatus(previous);
                else { _statusText = null; StatusLabel.Text = ""; }
            };
            hide.Start();
        }

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
            if (_winHeight is double savedH)   // altura con la que se cerró (p. ej. achicada)
                Height = Math.Clamp(savedH, MinHeight, _naturalHeight);
            // Measure the title label natural width (works even if later collapsed)
            TitleLabel.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            _titleLabelNaturalWidth = TitleLabel.DesiredSize.Width;
            UpdateTitleVisibility(animate: false);   // abrió angosta: que no choque con los botones
        }));
    }

    // ── VB-Cable check ───────────────────────────────────────────────────────

    private async void CheckVBCable()
    {
        var status = AudioEngine.GetCableStatus();
        if (status == AudioEngine.CableStatus.Ok) return;
        Log.Warn($"VB-CABLE no disponible: {status}");

        if (status == AudioEngine.CableStatus.NeedsRestart)
        {
            var r = System.Windows.MessageBox.Show(
                Loc.T("CableRestartMsg"),
                Loc.T("CableRestartTitle"), MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (r == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo("shutdown", "/r /t 5") { CreateNoWindow = true, UseShellExecute = false });
            return;
        }
        if (status == AudioEngine.CableStatus.Disabled)
        {
            System.Windows.MessageBox.Show(
                Loc.T("CableDisabledMsg"),
                Loc.T("CableDisabledTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            try { Process.Start(new ProcessStartInfo("control", "mmsys.cpl") { UseShellExecute = true }); } catch { }
            return;
        }

        var result = System.Windows.MessageBox.Show(
            Loc.T("CableMissingMsg"),
            Loc.T("CableMissingTitle"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        string installerPath = System.IO.Path.Combine(AppContext.BaseDirectory, "VBCABLE_Setup_x64.exe");
        try
        {
            if (!System.IO.File.Exists(installerPath))
            {
                SetError("DownloadingCable");
                installerPath = await DownloadVBCableSetup();
            }

            var proc = Process.Start(new ProcessStartInfo(installerPath)
            {
                Verb = "runas",
                UseShellExecute = true
            });
            if (proc != null) await Task.Run(proc.WaitForExit);

            System.Windows.MessageBox.Show(
                Loc.T("CableInstalledMsg"),
                Loc.T("CableInstalledTitle"),
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
                Loc.T("CableDownloadMsg"),
                Loc.T("CableDownloadTitle"),
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
        _trayShowItem  = menu.Items.Add(Loc.T("TrayShow"), null, (_, _) => RestoreFromTray());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _trayCloseItem = menu.Items.Add(Loc.T("TrayClose"), null, (_, _) => Dispatcher.Invoke(Close));

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Audio Pitcher",
            Icon = trayIco,
            Visible = false,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private System.Windows.Forms.ToolStripItem? _trayShowItem, _trayCloseItem;

    private void HideToTray()
    {
        _trayIcon.Visible = true;
        ShowInTaskbar = false;
        Hide();
    }

    private bool _minimizeToTray;

    private void SetMinimizeMode(bool tray)
    {
        _minimizeToTray = tray;
        UpdateMinimizeButtons();
        SaveSettings();
    }

    private void UpdateMinimizeButtons()
    {
        _minimizeSeg?.Select(_minimizeToTray ? 1 : 0);
        MinimizeBtn.ToolTip = Loc.T(_minimizeToTray ? "HideToTrayTip" : "MinimizeTip");
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
        PinBtn.ToolTip = Loc.T(_isPinned ? "PinnedTip" : "UnpinnedTip");
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (_minimizeToTray) HideToTray();
        else WindowState = WindowState.Minimized;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        CancelHotkeyCapture();
        if (!_isPinned && WindowState == WindowState.Normal)
            WindowState = WindowState.Minimized;
    }

    // ── Devices ───────────────────────────────────────────────────────────────

    private const string DefaultDeviceSentinel = "<default>";

    private void LoadOutputDevices()
    {
        var devices = AudioEngine.GetOutputDeviceNames();
        var items = new List<string> { Loc.T("DefaultDevice") };
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
                    items.Add(new AudioSessionInfo { Name = Loc.T("SystemSource"), ProcessId = 0 });
                items.Add(new AudioSessionInfo { Name = Loc.T("MoreOptions"), ProcessId = -1 });
            }
        }
        if (!collapsed)
        {
            items.AddRange(sorted);
            items.Add(new AudioSessionInfo { Name = Loc.T("SystemSource"), ProcessId = 0 });
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
        SwitchPitchToApp(_targetProcessName);

        if (sel.ProcessId == _routedPid) return;   // sin cambios (incluye Sistema↔Sistema)

        RestoreRouting();
        if (sel.ProcessId == 0) return;

        try
        {
            using var cable = AudioEngine.FindCableRenderDevice();
            if (cable == null)
            {
                SetError("CableNotFound");
                return;
            }
            AudioPolicyConfig.SetDefaultEndpoint(sel.ProcessId, cable.ID);
            _routedPid = sel.ProcessId;
            FixDefaultOutputIfCable(onExit: false);

            _recentApps.RemoveAll(n => n.Equals(sel.ProcessName, StringComparison.OrdinalIgnoreCase));
            _recentApps.Insert(0, sel.ProcessName);
            if (_recentApps.Count > 8) _recentApps.RemoveRange(8, _recentApps.Count - 8);
        }
        catch (Exception ex) { SetError("RouteFailed", ex.Message); }
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
        try
        {
            AudioPolicyConfig.SetDefaultEndpoint(_routedPid, "");
            Log.Info($"Ruteo devuelto: pid {_routedPid} ({_selectedAppName})");
        }
        catch (Exception ex) { Log.Error($"No se pudo devolver el ruteo de pid {_routedPid} ({_selectedAppName})", ex); }
        _routedPid = 0;
    }

    /// <summary>
    /// Devuelve TODAS las apps que estén mandando audio a VB-CABLE, no solo la actual
    /// (cubre apps reiniciadas con otro PID y cierres sucios anteriores).
    /// </summary>
    public void RestoreAllRoutings(string reason)
    {
        Log.Info($"Devolviendo ruteos ({reason})");
        RestoreRouting();
        SweepOrphanRoutings(AudioEngine.GetActiveSessions());
    }

    /// <summary>Último intento si la app se cae: que nadie quede sin sonido.</summary>
    public void EmergencyRestore()
    {
        try { RestoreAllRoutings("error inesperado"); FixDefaultOutputIfCable(onExit: true); } catch { }
    }

    /// <summary>Limpia ruteos al cable que quedaron de sesiones anteriores (cierre sucio).</summary>
    private void SweepOrphanRoutings(List<AudioSessionInfo> items)
    {
        try
        {
            var cableIds = AudioEngine.GetCableRenderIds();
            if (cableIds.Count == 0) return;
            foreach (var it in items)
            {
                if (it.ProcessId == 0 || it.ProcessId == _routedPid) continue;
                if (!cableIds.Contains(AudioPolicyConfig.GetDefaultEndpoint(it.ProcessId))) continue;
                try
                {
                    AudioPolicyConfig.SetDefaultEndpoint(it.ProcessId, "");
                    Log.Info($"Ruteo huérfano devuelto: {it.ProcessName} (pid {it.ProcessId})");
                }
                catch (Exception ex) { Log.Error($"No se pudo devolver {it.ProcessName} (pid {it.ProcessId})", ex); }
            }
        }
        catch (Exception ex) { Log.Error("SweepOrphanRoutings", ex); }
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
        catch (Exception ex) { SetError("Raw", ex.Message); }
    }

    private static float GetSliderVal(Slider s) =>
        (float)(Math.Round(s.Value * 10) / 10.0);

    // ── Pitch sliders ─────────────────────────────────────────────────────────

    private bool _snapGuard;

    private void PitchSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PitchLabel == null) return;
        if (SnapToSemitone(PitchSlider)) return;
        if (SnapNearZero(PitchSlider, 0.25)) return;
        float val = GetSliderVal(PitchSlider);
        PitchLabel.Text = val >= 0 ? $"+{val:F1}" : $"{val:F1}";
        _engine.PitchSemitones = val;
        ForgetResetIfManual(ref _pitchBeforeReset);
        StoreAppPitch();
    }

    private void FormantSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FormantLabel == null) return;
        if (SnapToSemitone(FormantSlider)) return;
        if (SnapNearZero(FormantSlider, 0.25)) return;
        float val = GetSliderVal(FormantSlider);
        FormantLabel.Text = val >= 0 ? $"+{val:F1}" : $"{val:F1}";
        _engine.FormantPitchSemitones = val;
        ForgetResetIfManual(ref _formantBeforeReset);
        StoreAppPitch();
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

    // ── Tono por app: cada app vuelve a su pitch/formant ──────────────────────

    private Dictionary<string, double[]> _appPitch = new();
    private string? _pitchAppKey;   // app a la que corresponden los valores actuales de los sliders
    private bool _switchingApp;

    private static string PitchKey(string processName) =>
        string.IsNullOrEmpty(processName) ? "*" : processName.ToLowerInvariant();

    private void SwitchPitchToApp(string processName)
    {
        string key = PitchKey(processName);
        if (key == _pitchAppKey) return;
        _pitchAppKey = key;
        double[] v = _appPitch.TryGetValue(key, out var saved) && saved.Length == 2 ? saved : new[] { 0.0, 0.0 };
        _switchingApp = _snapGuard = true;
        PitchSlider.Value   = v[0];
        FormantSlider.Value = v[1];
        _switchingApp = _snapGuard = false;
        _pitchBeforeReset = _formantBeforeReset = null;
        UpdateResetButtons();
    }

    private void StoreAppPitch()
    {
        if (_pitchAppKey == null || _switchingApp) return;
        double p = Math.Round(PitchSlider.Value, 1), f = Math.Round(FormantSlider.Value, 1);
        if (p == 0 && f == 0) _appPitch.Remove(_pitchAppKey);
        else _appPitch[_pitchAppKey] = new[] { p, f };
    }

    // ── Ruedita del mouse y Shift (semitonos exactos) ─────────────────────────

    private void PitchWheel(object sender, MouseWheelEventArgs e)   => WheelStep(PitchSlider, e);
    private void FormantWheel(object sender, MouseWheelEventArgs e) => WheelStep(FormantSlider, e);

    // Paso 0.1; con Shift, al semitono entero siguiente
    private void WheelStep(Slider slider, MouseWheelEventArgs e)
    {
        if (!slider.IsEnabled) return;
        int dir = Math.Sign(e.Delta);
        double v = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            ? (dir > 0 ? Math.Floor(slider.Value + 1e-6) + 1 : Math.Ceiling(slider.Value - 1e-6) - 1)
            : Math.Round(slider.Value * 10) / 10.0 + 0.1 * dir;
        _snapGuard = true;   // sin el imán del 0: con la ruedita se puede ir a ±0.1
        slider.Value = Math.Clamp(Math.Round(v, 1), slider.Minimum, slider.Maximum);
        _snapGuard = false;
        e.Handled = true;
    }

    // Arrastrando con Shift: redondea a semitonos enteros
    private bool SnapToSemitone(Slider s)
    {
        if (_snapGuard || _resetting || _switchingApp) return false;
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) || Mouse.LeftButton != MouseButtonState.Pressed) return false;
        double r = Math.Round(s.Value);
        if (Math.Abs(r - s.Value) < 1e-6) return false;
        _snapGuard = true;
        s.Value = r;
        _snapGuard = false;
        return true;
    }

    // Reset de ida y vuelta: primer click → 0 (recuerda el valor); segundo click → vuelve al valor
    private double? _pitchBeforeReset, _formantBeforeReset;
    private bool _resetting;

    private void ResetPitch_Click(object sender, RoutedEventArgs e)   => ToggleReset(PitchSlider, ref _pitchBeforeReset);
    private void ResetFormant_Click(object sender, RoutedEventArgs e) => ToggleReset(FormantSlider, ref _formantBeforeReset);

    private void ToggleReset(Slider slider, ref double? before)
    {
        _resetting = true;
        if (Math.Abs(slider.Value) > 0.001) { before = slider.Value; slider.Value = 0; }
        else if (before is double v)        { before = null;         slider.Value = v; }
        _resetting = false;
        UpdateResetButtons();
    }

    // Moverlo a mano olvida el valor guardado
    private void ForgetResetIfManual(ref double? before)
    {
        if (_resetting || before == null) return;
        before = null;
        UpdateResetButtons();
    }

    private void UpdateResetButtons()
    {
        UpdateResetButton(PitchResetBtn, _pitchBeforeReset, "BrushPitchFill");
        UpdateResetButton(FormantResetBtn, _formantBeforeReset, "BrushFormantFill");
    }

    // Con valor guardado: flecha espejada y en color, tooltip "Volver a +1.7"
    private static void UpdateResetButton(System.Windows.Controls.Button btn, double? before, string accentKey)
    {
        if (btn?.Content is not TextBlock icon) return;
        if (before is double v)
        {
            icon.RenderTransform = new ScaleTransform(-1, 1);
            icon.SetResourceReference(TextBlock.ForegroundProperty, accentKey);
            btn.ToolTip = Loc.T("RestoreTip", v >= 0 ? $"+{v:F1}" : $"{v:F1}");
        }
        else
        {
            icon.RenderTransform = Transform.Identity;
            icon.ClearValue(TextBlock.ForegroundProperty);
            btn.SetResourceReference(FrameworkElement.ToolTipProperty, "S.ResetTip");
        }
    }

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

        var targetPill = _fxEnabled ? FxAccent() : WpfColor.FromRgb(0x2E, 0x2E, 0x45);
        var targetDot  = _fxEnabled ? Colors.White : WpfColor.FromRgb(0x4A, 0x4A, 0x6A);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var dotPos = new ThicknessAnimation(
            new Thickness(_fxEnabled ? 14 : 1, 0, 0, 0), TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };
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

    private void SetTextColorMode(int mode)
    {
        ThemeService.ApplyTextColorMode(mode);
        HighlightTextBtns();
    }

    private void HighlightTextBtns() => _textSeg?.Select(ThemeService.TextColorMode);

    // Fondo claro → letras negras; fondo oscuro → blancas (Color se respeta)
    private void MatchTextToBackground(bool lightBg)
    {
        int mode = ThemeService.TextColorMode;
        if (lightBg && mode == 0) SetTextColorMode(1);
        else if (!lightBg && mode == 1) SetTextColorMode(0);
    }

    private void HighlightBgBtns(int preset) => _bgSeg?.Select(preset);   // -1 = ninguno (modo imagen)

    private void SetBgPreset(int preset)
    {
        if (_imageMode) { _bgImageFile = ""; ApplyBgImage(); }   // un fondo de la app reemplaza a la imagen
        ExitImagePalette(preset);
        MatchTextToBackground(preset == 4);
        SaveSettings();
    }

    // ── Controles segmentados (Motor, Al minimizar, Texto, Fondo) ─────────────

    private SegmentedControl? _engineSeg, _minimizeSeg, _textSeg, _bgSeg, _toneSeg, _vividSeg;

    private static readonly (string name, byte r, byte g, byte b)[] BgPresetColors =
    {
        ("Dark", 0x1A, 0x1B, 0x2E), ("Black", 0x00, 0x00, 0x00), ("Deep", 0x0D, 0x0D, 0x18),
        ("Hue", 0x1A, 0x1B, 0x2E), ("White", 0xFF, 0xFF, 0xFF),
    };

    // Color real de cada fondo (Hue = el de Dark corrido por el slider de HUE)
    private static System.Windows.Media.Brush PresetBrush(int i)
    {
        var (_, r, g, b) = BgPresetColors[i];
        var c = WpfColor.FromRgb(r, g, b);
        if (i == 3) c = ThemeService.ShiftHue(c, ThemeService.HueShift);
        var br = new SolidColorBrush(c); br.Freeze(); return br;
    }

    private static System.Windows.Media.Brush Gradient(params string[] hex)
    {
        var g = new LinearGradientBrush { StartPoint = new WpfPoint(0, 0.5), EndPoint = new WpfPoint(1, 0.5) };
        for (int i = 0; i < hex.Length; i++)
            g.GradientStops.Add(new GradientStop((WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(hex[i]), hex.Length == 1 ? 0 : (double)i / (hex.Length - 1)));
        g.Freeze(); return g;
    }

    private void InitSegmentedControls()
    {
        _engineSeg = new SegmentedControl(new[]
        {
            new SegmentedControl.Option { Label = () => AudioEngine.IsRubberBandAvailable ? "RubberBand" : Loc.T("RbNA"),
                                          PillResource = "BrushAccentStrong", Enabled = AudioEngine.IsRubberBandAvailable },
            new SegmentedControl.Option { Label = () => AudioEngine.IsSignalsmithAvailable ? "Signalsmith" : Loc.T("SsNA"),
                                          PillResource = "BrushAccentStrong", Enabled = AudioEngine.IsSignalsmithAvailable },
        }, stretch: false);
        _engineSeg.SelectionChanged += SetPitchEngine;
        EngineSegHost.Content = _engineSeg;

        _minimizeSeg = new SegmentedControl(new[]
        {
            new SegmentedControl.Option { Label = () => Loc.T("Taskbar"), PillResource = "BrushAccentStrong" },
            new SegmentedControl.Option { Label = () => Loc.T("Tray"),    PillResource = "BrushAccentStrong" },
        }, stretch: false);
        _minimizeSeg.SelectionChanged += i => SetMinimizeMode(i == 1);
        MinimizeSegHost.Content = _minimizeSeg;

        _textSeg = new SegmentedControl(new[]
        {
            new SegmentedControl.Option { Label = () => Loc.T("TxtNormal"), PillResource = "BrushOverlay", SelectedTextResource = "BrushText" },
            new SegmentedControl.Option { Label = () => Loc.T("TxtBlack"), PillBrush = () => System.Windows.Media.Brushes.Black },
            new SegmentedControl.Option { Label = () => "Color", PillBrush = () => Gradient("#F38BA8", "#89B4FA") },
        }, stretch: false);
        _textSeg.SelectionChanged += SetTextColorMode;
        TextSegHost.Content = _textSeg;

        var bgOptions = new SegmentedControl.Option[5];
        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            bgOptions[i] = new SegmentedControl.Option
            {
                Label = () => BgPresetColors[idx].name, PillBrush = () => PresetBrush(idx),
                SelectedText = idx == 4 ? WpfColor.FromRgb(0x1A, 0x1B, 0x2E) : Colors.White,
            };
        }
        _bgSeg = new SegmentedControl(bgOptions, stretch: true);
        _bgSeg.SelectionChanged += SetBgPreset;
        BgSegHost.Content = _bgSeg;

        _toneSeg = new SegmentedControl(new[]
        {
            new SegmentedControl.Option { Label = () => Loc.T("BgToneAuto"),  PillResource = "BrushAccentStrong" },
            new SegmentedControl.Option { Label = () => Loc.T("BgToneLight"), PillResource = "BrushAccentStrong" },
            new SegmentedControl.Option { Label = () => Loc.T("BgToneDark"),  PillResource = "BrushAccentStrong" },
        }, stretch: false);
        _toneSeg.Select(_bgImageTone);
        _toneSeg.SelectionChanged += SetImageTone;
        ToneSegHost.Content = _toneSeg;

        // Color: Suave · Vivo (se recuerda aparte para fondos oscuros y claros); la pastilla muestra
        // el azul que resulta en el modo actual, corrido por el HUE
        static System.Windows.Media.Brush AccentPill(WpfColor c) { var b = new SolidColorBrush(ThemeService.ShiftHue(c, ThemeService.HueShift)); b.Freeze(); return b; }
        static bool Light() => ThemeService.BgPreset == 4;
        _vividSeg = new SegmentedControl(new[]
        {
            new SegmentedControl.Option { Label = () => Loc.T("AccentSoft"),
                PillBrush = () => AccentPill(Light() ? ThemeService.SoftAccent(0x1E, 0x66, 0xF5) : WpfColor.FromRgb(0x89, 0xB4, 0xFA)) },
            new SegmentedControl.Option { Label = () => Loc.T("AccentVivid"),
                PillBrush = () => AccentPill(Light() ? WpfColor.FromRgb(0x1E, 0x66, 0xF5) : ThemeService.VividAccent(0x1E, 0x66, 0xF5)) },
        }, stretch: false);
        _vividSeg.SelectionChanged += i => { ThemeService.ApplyAccentVivid(i == 1); SaveSettings(); };
        VividSegHost.Content = _vividSeg;

        // El Hue cambia el color real del fondo "Hue" y de las pastillas Suave/Vivo; al cambiar de
        // modo, el selector muestra la elección de ese modo
        ThemeService.ThemeChanged += (_, _) =>
        {
            _bgSeg.RefreshColors(); _vividSeg.RefreshColors();
            _vividSeg.Select(ThemeService.AccentVivid ? 1 : 0);
        };
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
            ThemeService.SetAccentVivid(s.AccentVivid, s.AccentVividLight);

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
            _lastRealOutputId = s.LastRealOutputId;
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

            // Imagen de fondo
            BgImageAmountSlider.Value = s.BgImageAmount;
            BgImageBlurSlider.Value   = s.BgImageBlur;
            _bgImageFile       = s.BgImageFile ?? "";
            _bgImageTone       = s.BgImageTone is >= 0 and <= 2 ? s.BgImageTone : 0;
            _presetBeforeImage = s.BgPresetBeforeImage is >= 0 and <= 4 ? s.BgPresetBeforeImage : 1;
            MigrateOldBackground(_bgImageFile);
            if (ApplyBgImage()) EnterImagePalette(remember: false);

            // Restore capture source (app)
            _hotkeys = s.Hotkeys ?? new();
            RegisterAllHotkeys();

            _appPitch          = s.AppPitch ?? new();
            _targetProcessName = s.TargetProcessName;
            _recentApps        = s.RecentApps ?? new();
            _favoriteApps      = s.FavoriteApps ?? new();
            LoadAppSessions();
        }
        finally { _loading = false; }
    }

    private void SaveSettings()
    {
        if (_loading || _demoMode) return;
        CaptureWindowPlacement();
        var devices = AudioEngine.GetOutputDeviceNames();
        string devName = OutputCombo.SelectedIndex == 0
            ? DefaultDeviceSentinel
            : OutputCombo.SelectedIndex >= 1 && OutputCombo.SelectedIndex - 1 < devices.Count
                ? devices[OutputCombo.SelectedIndex - 1] : "";

        SettingsStore.Save(new AppSettings
        {
            PitchEngine      = (int)_engine.PitchEngine,
            MinimizeToTray   = _minimizeToTray,
            Language         = Loc.Current,
            LastRealOutputId = _lastRealOutputId,
            SkippedUpdateVersion = _skippedUpdate,
            HueShift         = ThemeService.HueShift,
            BgPreset         = ThemeService.BgPreset,
            TextColorMode    = ThemeService.TextColorMode,
            TextBrightness   = ThemeService.TextBrightness,
            AccentVivid      = ThemeService.AccentVividDark,
            AccentVividLight = ThemeService.AccentVividLight,
            OutputDeviceName = devName,
            TargetProcessName = _targetProcessName,
            RecentApps       = _recentApps,
            FavoriteApps     = _favoriteApps,
            BgImageFile      = _bgImageFile,
            BgImageAmount    = (int)BgImageAmountSlider.Value,
            BgImageBlur      = (int)BgImageBlurSlider.Value,
            BgImageTone      = _bgImageTone,
            BgPresetBeforeImage = _presetBeforeImage,
            AppPitch         = _appPitch,
            Hotkeys          = _hotkeys,
            WindowLeft       = _winLeft,
            WindowTop        = _winTop,
            WindowWidth      = _winWidth,
            WindowHeight     = _winHeight,
            CustomColors     = ThemeService.GetCustomColorsForSave(),
        });
    }

    // ── Pitch engine toggle ───────────────────────────────────────────────────

    private void SetPitchEngine(int mode)
    {
        _engine.PitchEngine = (AudioEngine.PitchEngineMode)mode;
        UpdateEngineButtons(mode);
        if (_engine.IsRunning) StartEngine();
        SaveSettings();
    }

    private void UpdateEngineButtons(int mode) => _engineSeg?.Select(mode);

    // ── Settings panel toggle ─────────────────────────────────────────────────

    private void ToggleSettings_Click(object sender, RoutedEventArgs e)
    {
        CancelHotkeyCapture();
        _settingsOpen = !_settingsOpen;
        if (_settingsOpen) ShowSettingsPage(0);   // siempre abre en el inicio
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
        string app = _selectedAppName;
        SetStatus(_routedPid > 0
            ? () => Loc.T("StatusProcessingApp", app)
            : () => Loc.T("StatusProcessing"));
    }

    private void SetStopped()
    {
        StatusLogo.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "BrushMuted");
        StatusLabel.SetResourceReference(TextBlock.ForegroundProperty, "BrushMuted");
        SetStatus(() => Loc.T("StatusNoAudio"));
    }

    // key de Loc; el log queda siempre en español
    private void SetError(string key, params object[] args)
    {
        Log.Error(Loc.Es(key, args));
        StatusLogo.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "BrushRed");
        StatusLabel.Foreground = new SolidColorBrush(WpfColor.FromRgb(0xF3, 0x8B, 0xA8));
        SetStatus(() => Loc.T("StatusError", Loc.T(key, args)));
    }

    // El texto se guarda como función para regenerarlo al cambiar de idioma
    private Func<string>? _statusText;

    // Texto nítido (ClearType) dentro de popups con sombra; requiere fondo opaco
    private static T ClearTypeOn<T>(T el) where T : UIElement
    {
        RenderOptions.SetClearTypeHint(el, ClearTypeHint.Enabled);
        return el;
    }

    private void SetStatus(Func<string> text)
    {
        _statusText = text;
        StatusLabel.Text = text();
    }

    // ── Idioma ────────────────────────────────────────────────────────────────

    // ── Atajos globales ───────────────────────────────────────────────────────
    // RegisterHotKey: la combinación es exclusiva de Audio Pitcher mientras está abierto.
    // Si otro programa ya la tiene como atajo global, Windows la rechaza y se avisa.

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int  WM_HOTKEY  = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    private static readonly string[] HotkeyIds   = { "toggle", "up", "down", "show" };
    private static readonly string[] HotkeyNames = { "HkToggle", "HkUp", "HkDown", "HkShow" };

    private Dictionary<string, int[]> _hotkeys = new();      // id → [modificadores, tecla virtual]
    private readonly HashSet<string> _hotkeysInUse = new();  // rechazados por Windows
    private string? _capturingHotkey;
    private readonly Dictionary<string, (Border box, TextBlock text)> _hotkeyBoxes = new();

    private IntPtr Hwnd => new System.Windows.Interop.WindowInteropHelper(this).Handle;

    private void RegisterAllHotkeys()
    {
        _hotkeysInUse.Clear();
        for (int i = 0; i < HotkeyIds.Length; i++)
        {
            UnregisterHotKey(Hwnd, i + 1);
            if (_hotkeys.TryGetValue(HotkeyIds[i], out var k) && k.Length == 2 &&
                !RegisterHotKey(Hwnd, i + 1, (uint)k[0] | MOD_NOREPEAT, (uint)k[1]))
                _hotkeysInUse.Add(HotkeyIds[i]);
        }
        UpdateHotkeyRows();
    }

    private void UnregisterAllHotkeys()
    {
        for (int i = 0; i < HotkeyIds.Length; i++) UnregisterHotKey(Hwnd, i + 1);
    }

    private void OnHotkey(int id)
    {
        if (_capturingHotkey != null) return;
        switch (id)
        {
            case 1: ToggleOriginal(); break;
            case 2: StepPitch(+1); break;
            case 3: StepPitch(-1); break;
            case 4: ToggleWindowVisible(); break;
        }
    }

    // Original ↔ cambiado: pitch y formant juntos (mismo comportamiento que los botones de reset)
    private void ToggleOriginal()
    {
        bool shifted = Math.Abs(PitchSlider.Value) > 0.001 || Math.Abs(FormantSlider.Value) > 0.001;
        if (shifted)
        {
            if (Math.Abs(PitchSlider.Value)   > 0.001) ToggleReset(PitchSlider,   ref _pitchBeforeReset);
            if (Math.Abs(FormantSlider.Value) > 0.001) ToggleReset(FormantSlider, ref _formantBeforeReset);
        }
        else
        {
            if (_pitchBeforeReset   != null) ToggleReset(PitchSlider,   ref _pitchBeforeReset);
            if (_formantBeforeReset != null) ToggleReset(FormantSlider, ref _formantBeforeReset);
        }
    }

    private void StepPitch(int dir)
    {
        _snapGuard = true;   // sin el imán del 0 (p. ej. -0.8 + 1 = +0.2)
        PitchSlider.Value = Math.Clamp(Math.Round(PitchSlider.Value + dir, 1), PitchSlider.Minimum, PitchSlider.Maximum);
        _snapGuard = false;
    }

    private void ToggleWindowVisible()
    {
        if (!IsVisible) { RestoreFromTray(); return; }
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
            Activate();
            return;
        }
        if (_minimizeToTray) HideToTray();
        else WindowState = WindowState.Minimized;
    }

    // Filas de la sección ATAJOS (se arman una vez; los textos se actualizan con el idioma)
    private void UpdateHotkeyRows()
    {
        if (_hotkeyBoxes.Count == 0)
        {
            for (int i = 0; i < HotkeyIds.Length; i++)
            {
                string id = HotkeyIds[i];
                var label = new TextBlock { FontSize = 10, VerticalAlignment = VerticalAlignment.Center,
                                            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
                label.SetResourceReference(TextBlock.TextProperty, "S." + HotkeyNames[i]);
                label.SetResourceReference(TextBlock.ForegroundProperty, "BrushMuted");
                var text = new TextBlock { FontSize = 9, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                                           HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
                // Caja "hundida" (diseño): sin borde, con sombra interior arriba
                var inset = new InnerShadow { CornerRadius = 5, OffsetY = 1, Blur = 4, ShadowOpacity = 0.55 };   // diseño: hundida
                var box = new Border
                {
                    Width = 118, Height = 22, CornerRadius = new CornerRadius(5),
                    BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    Child = new Grid { Children = { inset, text } },
                };
                box.SetResourceReference(Border.BackgroundProperty, "BrushSurface2");
                box.SetResourceReference(FrameworkElement.ToolTipProperty, "S.HkTip");
                box.MouseLeftButtonDown += (_, ev) => { StartHotkeyCapture(id); ev.Handled = true; };
                // Nombre a la izquierda (se recorta con "…"), caja a la derecha (se achica si es angosta)
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(box, 1);
                row.Children.Add(label);
                row.Children.Add(box);
                HotkeyRows.Children.Add(row);
                _hotkeyBoxes[id] = (box, text);
            }
        }

        foreach (var id in HotkeyIds)
        {
            var (box, text) = _hotkeyBoxes[id];
            bool capturing = _capturingHotkey == id;
            bool inUse     = _hotkeysInUse.Contains(id);
            bool set       = _hotkeys.TryGetValue(id, out var k) && k.Length == 2;
            text.Text = capturing ? Loc.T("HkPress") : set ? HotkeyText(k![0], k[1]) : Loc.T("HkNone");
            text.SetResourceReference(TextBlock.ForegroundProperty,
                inUse ? "BrushRed" : capturing || set ? "BrushText" : "BrushMuted");
            if (capturing || inUse) box.SetResourceReference(Border.BorderBrushProperty, capturing ? "BrushBlue" : "BrushRed");
            else box.BorderBrush = System.Windows.Media.Brushes.Transparent;   // sin borde en reposo
            if (inUse) box.ToolTip = Loc.T("HkInUse");
            else box.SetResourceReference(FrameworkElement.ToolTipProperty, "S.HkTip");
        }
    }

    // Pie en una línea: si no entra, se saca la versión; si sigue sin entrar, también Licencias
    private bool _fitFooterBusy;
    private void FooterText_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || _fitFooterBusy) return;
        _fitFooterBusy = true;
        var lines = FooterText.Inlines;
        if (!lines.Contains(VersionRun)) { lines.InsertBefore(lines.FirstInline, VersionSep); lines.InsertBefore(VersionSep, VersionRun); }
        if (!lines.Contains(LicensesLink)) { lines.InsertBefore(ReportLink, LicensesSep); lines.InsertBefore(LicensesSep, LicensesLink); }
        bool Fits() { FooterText.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity)); return FooterText.DesiredSize.Width <= e.NewSize.Width + 0.5; }
        if (!Fits()) { lines.Remove(VersionRun); lines.Remove(VersionSep); }
        if (!Fits()) { lines.Remove(LicensesLink); lines.Remove(LicensesSep); }
        _fitFooterBusy = false;
    }

    // Ventana angosta: la caja del atajo ocupa como mucho la mitad de la fila
    private void HotkeyRows_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        foreach (var (box, _) in _hotkeyBoxes.Values) box.Width = Math.Clamp(e.NewSize.Width * 0.55, 76, 118);
    }

    private static string HotkeyText(int mods, int vk)
    {
        var parts = new List<string>();
        if ((mods & MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mods & MOD_ALT)     != 0) parts.Add("Alt");
        if ((mods & MOD_SHIFT)   != 0) parts.Add("Shift");
        if ((mods & MOD_WIN)     != 0) parts.Add("Win");
        var key = KeyInterop.KeyFromVirtualKey(vk);
        string name = key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Num " + (int)(key - Key.NumPad0),
            Key.OemPlus => "+", Key.OemMinus => "-", Key.OemComma => ",", Key.OemPeriod => ".",
            Key.Prior => "PageUp", Key.Next => "PageDown",
            _ => key.ToString(),
        };
        parts.Add(name);
        return string.Join("+", parts);
    }

    private void StartHotkeyCapture(string id)
    {
        _capturingHotkey = id;
        UnregisterAllHotkeys();   // para que las combinaciones actuales lleguen como teclas normales
        UpdateHotkeyRows();
        Focus();
    }

    private void CancelHotkeyCapture()
    {
        if (_capturingHotkey == null) return;
        _capturingHotkey = null;
        RegisterAllHotkeys();
    }

    // Mientras se espera una combinación, todas las teclas van acá
    private void HotkeyCapture_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (_capturingHotkey == null)
        {
            if (e.Key == Key.Escape && _settingsOpen && _settingsPage != 0) { ShowSettingsPage(0); e.Handled = true; }
            return;
        }
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        string id = _capturingHotkey;

        if (key == Key.Escape) { CancelHotkeyCapture(); return; }
        if (key is Key.Back or Key.Delete)
        {
            _hotkeys.Remove(id);
            _capturingHotkey = null;
            RegisterAllHotkeys();
            SaveSettings();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
                   Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;   // esperar la tecla

        uint mods = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= MOD_CONTROL;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))     mods |= MOD_ALT;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))   mods |= MOD_SHIFT;
        if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= MOD_WIN;

        _hotkeys[id] = new[] { (int)mods, KeyInterop.VirtualKeyFromKey(key) };
        _capturingHotkey = null;
        RegisterAllHotkeys();   // si Windows la rechaza, queda marcada "en uso"
        SaveSettings();
    }

    private void OnLanguageChanged()
    {
        ApplyLanguageTexts();
        UpdatePinBtn();
        UpdateMinimizeButtons();
        UpdateResetButtons();
        if (_statusText != null) StatusLabel.Text = _statusText();

        bool loading = _loading;
        _loading = true;
        int outIdx = OutputCombo.SelectedIndex;
        LoadOutputDevices();
        OutputCombo.SelectedIndex = outIdx;
        _loading = loading;
        LoadAppSessions();

        SaveSettings();
    }

    // Textos que pone el código (los del XAML se actualizan solos por DynamicResource)
    private void ApplyLanguageTexts()
    {
        _engineSeg?.RefreshTexts(); _minimizeSeg?.RefreshTexts(); _textSeg?.RefreshTexts(); _bgSeg?.RefreshTexts(); _toneSeg?.RefreshTexts(); _vividSeg?.RefreshTexts();
        if (!AudioPolicyConfig.IsSupported)      AppCombo.ToolTip = Loc.T("NoPerAppRouting");
        if (_pendingUpdate != null) UpdateBtn.ToolTip = Loc.T("UpdateTo", _pendingUpdate.Version.ToString(3));
        if (_trayShowItem  != null) _trayShowItem.Text  = Loc.T("TrayShow");
        if (_trayCloseItem != null) _trayCloseItem.Text = Loc.T("TrayClose");

        // Idioma: lista desplegable (los nombres van siempre en su propio idioma)
        _langComboUpdating = true;
        if (LangCombo.Items.Count == 0) { LangCombo.Items.Add("Español"); LangCombo.Items.Add("English"); }
        LangCombo.SelectedIndex = Loc.Current == "en" ? 1 : 0;
        _langComboUpdating = false;
        UpdateHotkeyRows();
    }

    private bool _langComboUpdating;

    private void LangCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_langComboUpdating || LangCombo.SelectedIndex < 0) return;
        Loc.Set(LangCombo.SelectedIndex == 1 ? "en" : "es");
    }

    // ── Configuración: inicio (menú + salida) → General / Apariencia ──────────

    private int _settingsPage;   // 0 = inicio, 1 = General, 2 = Apariencia

    private void SettingsMenu_Click(object sender, MouseButtonEventArgs e)
    {
        // Respuesta al click: la fila se marca un instante y recién ahí cambia la página
        var row = (Border)sender;
        int page = int.Parse(row.Tag!.ToString()!);
        row.SetResourceReference(Border.BackgroundProperty, "BrushRowHover");
        e.Handled = true;
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            row.Background = System.Windows.Media.Brushes.Transparent;
            ShowSettingsPage(page);
        };
        t.Start();
    }

    private void SettingsBack_Click(object sender, MouseButtonEventArgs e)
    {
        if (_settingsPage == 0) return;
        ShowSettingsPage(0);
        e.Handled = true;
    }

    private void ShowSettingsPage(int page)
    {
        CancelHotkeyCapture();
        _settingsPage = page;
        SettingsHome.Visibility  = page == 0 ? Visibility.Visible : Visibility.Collapsed;
        GeneralTab.Visibility    = page == 1 ? Visibility.Visible : Visibility.Collapsed;
        AppearanceTab.Visibility = page == 2 ? Visibility.Visible : Visibility.Collapsed;
        SettingsBackArrow.Visibility = page == 0 ? Visibility.Collapsed : Visibility.Visible;
        SettingsHeader.Cursor = page == 0 ? null : System.Windows.Input.Cursors.Hand;
        SettingsTitleText.SetResourceReference(TextBlock.TextProperty,
            page == 1 ? "S.GeneralTitle" : page == 2 ? "S.Appearance" : "S.SettingsTitle");
        if (_settingsOpen)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render,
                new Action(() => UpdateNaturalHeight(autoExpand: true)));
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
        UpdateTitleVisibility(animate: true);
    }

    // El título se esconde si choca con los botones de la derecha (al achicar o al abrir angosta)
    private void UpdateTitleVisibility(bool animate)
    {
        const double leftMargin  = 16;
        const double rightMargin = 12;
        const double ellipseW    = 7 + 8;
        const double buffer      = 12;
        double titleRight = leftMargin + ellipseW + _titleLabelNaturalWidth;
        double btnsLeft   = ActualWidth - rightMargin - HeaderBtns.ActualWidth;
        bool shouldShow = titleRight + buffer <= btnsLeft;
        if (shouldShow == _titleVisible) return;
        _titleVisible = shouldShow;

        if (!animate)
        {
            TitleLabel.BeginAnimation(OpacityProperty, null);
            TitleLabel.Opacity = 1;
            TitleLabel.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
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
        if (msg == WM_HOTKEY)
        {
            OnHotkey(wParam.ToInt32());
            handled = true;
            return IntPtr.Zero;
        }
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

        if (FxBtn.Template?.FindName("FxArrow", FxBtn) is TextBlock fxArrow)
            UpdateLabelColor("label:FX", fxArrow, "BrushBlue");

        // El interruptor de FX sigue el color de FX (o el acento del tema)
        if (FxPill.Background is SolidColorBrush pillBrush && !pillBrush.IsFrozen && _fxEnabled)
            pillBrush.Color = FxAccent();

        ApplyKnobColors();
    }

    private static WpfColor FxAccent() =>
        ThemeService.GetCustomColor("label:FX")
        ?? (System.Windows.Application.Current.Resources["BrushBlue"] as SolidColorBrush)?.Color
        ?? WpfColor.FromRgb(0x89, 0xB4, 0xFA);

    // ── Perillas: color propio (click derecho), guardado como los demás colores ──

    private readonly Dictionary<KnobControl, WpfColor> _knobDefaults = new();

    private KnobControl[] AllKnobs => new[]
    {
        ReverbWetKnob, ReverbRoomKnob, ReverbDampKnob,
        DelayWetKnob, DelayTimeKnob, DelayFbKnob,
        ChorusWetKnob, ChorusRateKnob, ChorusDepthKnob,
    };

    private void InitKnobColors()
    {
        foreach (var knob in AllKnobs)
        {
            _knobDefaults[knob] = knob.KnobColor;   // el color de fábrica (XAML)
            knob.ColorPickRequested += k =>
                ShowColorPickerPopup(k, c => ThemeService.SetCustomColor("knob:" + k.Name, c));
        }
        ApplyKnobColors();
    }

    private void ApplyKnobColors()
    {
        foreach (var (knob, def) in _knobDefaults)
            knob.KnobColor = ThemeService.GetCustomColorBase("knob:" + knob.Name) ?? def;
    }

    private void FxBtn_ColorPick(object sender, MouseButtonEventArgs e)
    {
        ShowColorPickerPopup((UIElement)sender, c => ThemeService.SetCustomColor("label:FX", c));
        e.Handled = true;
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
        // Solo colores: el contraste queda como está
        HueSlider.Value = 0;
        ThemeService.ResetCustomColors();
        ThemeService.ApplyTextColorMode(0);
        ApplyCustomLabelColors();
        SaveSettings();
        HighlightTextBtns();
    }

    // ── Imagen de fondo: galería ──────────────────────────────────────────────
    // Cada imagen elegida se copia a %APPDATA%\AudioPitcher\backgrounds (nombre = hash del contenido,
    // así no se duplica). Queda disponible aunque se borre el original; se elimina desde la galería.

    private string _bgImageFile = "";   // archivo aplicado dentro de backgrounds ("" = ninguna)

    private static string BgFolder => System.IO.Path.Combine(SettingsStore.Folder, "backgrounds");

    private static string[] SavedBackgrounds()
    {
        try
        {
            if (!System.IO.Directory.Exists(BgFolder)) return Array.Empty<string>();
            return System.IO.Directory.GetFiles(BgFolder)
                .OrderBy(System.IO.File.GetCreationTimeUtc)
                .Select(System.IO.Path.GetFileName).OfType<string>().ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    // Copia a la galería y devuelve el nombre guardado
    private static string AddToGallery(string sourcePath)
    {
        LoadBgBitmap(sourcePath, 64);   // valida que sea una imagen legible
        System.IO.Directory.CreateDirectory(BgFolder);
        string hash;
        using (var sha = System.Security.Cryptography.SHA1.Create())
        using (var fs = System.IO.File.OpenRead(sourcePath))
            hash = Convert.ToHexString(sha.ComputeHash(fs))[..12].ToLowerInvariant();
        string name = hash + System.IO.Path.GetExtension(sourcePath).ToLowerInvariant();
        string dest = System.IO.Path.Combine(BgFolder, name);
        if (!System.IO.File.Exists(dest)) System.IO.File.Copy(sourcePath, dest);
        return name;
    }

    // La versión de prueba guardaba una sola copia en la raíz ("background.jpg"): pasarla a la galería
    private void MigrateOldBackground(string oldFile)
    {
        if (string.IsNullOrEmpty(oldFile) || oldFile.Contains('/') || oldFile.Contains('\\')) return;
        string old = System.IO.Path.Combine(SettingsStore.Folder, oldFile);
        if (!System.IO.File.Exists(old) || System.IO.File.Exists(System.IO.Path.Combine(BgFolder, oldFile))) return;
        try
        {
            _bgImageFile = AddToGallery(old);
            System.IO.File.Delete(old);
        }
        catch (Exception ex) { Log.Error("Migrar imagen de fondo", ex); }
    }

    private void AddBgImages()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title       = Loc.T("BgImagePick"),
            Filter      = Loc.T("BgImageFiles") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != true) return;
        string? last = null;
        foreach (var file in dlg.FileNames)
        {
            try { last = AddToGallery(file); }
            catch (Exception ex)
            {
                Log.Error("Imagen de fondo", ex);
                System.Windows.MessageBox.Show(Loc.T("BgImageError") + "\n\n" + System.IO.Path.GetFileName(file),
                    "Audio Pitcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        if (last == null) return;
        SelectBgImage(last);   // se aplica la última agregada
    }

    private void SelectBgImage(string name)
    {
        bool wasImage = _imageMode;
        _bgImageFile = name;
        if (ApplyBgImage()) EnterImagePalette(remember: !wasImage);
        else if (wasImage) ExitImagePalette(_presetBeforeImage);
        SaveSettings();
    }

    private void DeleteBgImage(string name)
    {
        bool current = name == _bgImageFile;
        if (current) BgImageRect.Fill = BgPreview.Background = null;
        try { System.IO.File.Delete(System.IO.Path.Combine(BgFolder, name)); }
        catch (Exception ex) { Log.Error("Borrar imagen de fondo", ex); }
        if (current) SelectBgImage("");
        else { RefreshBgGallery(); SaveSettings(); }
    }

    // "Quitar" (vista previa): saca la imagen puesta de la galería y vuelve al fondo de antes
    private void BgRemove_Click(object sender, MouseButtonEventArgs e)
    {
        if (_bgImageFile != "") DeleteBgImage(_bgImageFile);
        e.Handled = true;
    }

    // ── Modo imagen: la imagen reemplaza a los fondos de la app (nunca los dos a la vez) ──
    // Imagen oscura → colores de Dark; imagen clara → colores de White. Los controles pasan a ser
    // de vidrio (ThemeService). Tema: Auto (según el brillo de la imagen) · Claro · Oscuro.

    private bool _imageMode, _bgImageIsLight;
    private int  _bgImageTone;   // 0 auto, 1 claro, 2 oscuro
    private int  _presetBeforeImage = 1;

    private void EnterImagePalette(bool remember)
    {
        if (remember && !_imageMode) _presetBeforeImage = ThemeService.BgPreset;
        _imageMode = true;
        bool light = _bgImageTone == 1 || (_bgImageTone == 0 && _bgImageIsLight);
        ThemeService.ApplyImageMode(true, light ? 4 : 0);
        HeaderSeparator.Visibility = RecordSeparator.Visibility = Visibility.Collapsed;   // con imagen, sin líneas separadoras
        MenuDivider.Height = 2;                              // divisor del menú: barrita de vidrio
        if (!_loading) MatchTextToBackground(light);
        HighlightBgBtns(-1);
        _toneSeg?.Select(_bgImageTone);
    }

    private void ExitImagePalette(int preset)
    {
        _imageMode = false;
        ThemeService.ApplyImageMode(false, preset);
        HeaderSeparator.Visibility = RecordSeparator.Visibility = Visibility.Visible;
        MenuDivider.Height = 1;
        HighlightBgBtns(preset);
    }

    private void SetImageTone(int tone)
    {
        _bgImageTone = tone;
        if (_imageMode) EnterImagePalette(remember: false);
        SaveSettings();
    }

    // Clara si el brillo promedio (miniatura de 32 px) pasa la mitad
    private static bool IsLightImage(string path)
    {
        try
        {
            var bmp = new FormatConvertedBitmap(LoadBgBitmap(path, 32), PixelFormats.Bgra32, null, 0);
            int w = bmp.PixelWidth, h = bmp.PixelHeight;
            var px = new byte[w * h * 4];
            bmp.CopyPixels(px, w * 4, 0);
            double sum = 0;
            for (int i = 0; i < px.Length; i += 4) sum += 0.0722 * px[i] + 0.7152 * px[i + 1] + 0.2126 * px[i + 2];
            return sum / (w * h) / 255 > 0.5;
        }
        catch { return false; }
    }

    // OnLoad + reducido: no deja el archivo bloqueado y no gasta memoria de más
    private static BitmapImage LoadBgBitmap(string path, int width = 900)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource        = new Uri(path);
        bmp.CacheOption      = BitmapCacheOption.OnLoad;
        bmp.CreateOptions    = BitmapCreateOptions.IgnoreImageCache;
        bmp.DecodePixelWidth = width;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private bool ApplyBgImage()
    {
        string path = string.IsNullOrEmpty(_bgImageFile) ? "" : System.IO.Path.Combine(BgFolder, _bgImageFile);
        bool has = path != "" && System.IO.File.Exists(path);
        if (has)
        {
            try
            {
                var brush = new ImageBrush(LoadBgBitmap(path)) { Stretch = Stretch.UniformToFill };
                brush.Freeze();
                BgImageRect.Fill = BgPreview.Background = brush;
                _bgImageIsLight = IsLightImage(path);
            }
            catch (Exception ex) { Log.Error("Imagen de fondo", ex); has = false; }
        }
        if (!has) { BgImageRect.Fill = BgPreview.Background = null; _bgImageFile = ""; }

        var vis = has ? Visibility.Visible : Visibility.Collapsed;
        BgImageHost.Visibility = BgPreview.Visibility = BgImageOptions.Visibility = vis;
        RefreshBgGallery();
        if (_settingsOpen)   // la galería y los controles cambian de alto dentro de Configuración
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render,
                new Action(() => UpdateNaturalHeight(autoExpand: true)));
        return has;
    }

    // Tira de miniaturas (diseño opción B): [agregar] [más nuevas … más viejas].
    // Click aplica; click en la que está puesta la saca. La elegida lleva aro y tilde.
    private void RefreshBgGallery()
    {
        BgGallery.Children.Clear();

        Border Tile() => new()
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 6, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
        };

        // Agregar: ícono de "imagen con +" sobre el gris de los controles
        var add = Tile();
        add.SetResourceReference(Border.BackgroundProperty, "BrushCtl2");
        var rim = new GlassEdge { CornerRadius = new CornerRadius(6) };
        var icon = new System.Windows.Shapes.Path
        {
            Data = (Geometry)FindResource("AddImageGeometry"), Stretch = Stretch.Uniform,
            Width = 14, Height = 14, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        icon.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "BrushSubtext");
        add.Child = new Grid { Children = { rim, icon } };
        add.MouseEnter += (_, _) => icon.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "BrushText");
        add.MouseLeave += (_, _) => icon.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "BrushSubtext");
        add.MouseLeftButtonDown += (_, e) => { AddBgImages(); e.Handled = true; };
        BgGallery.Children.Add(add);

        foreach (var name in SavedBackgrounds().Reverse())
        {
            var tile = Tile();
            try
            {
                tile.Background = new ImageBrush(LoadBgBitmap(System.IO.Path.Combine(BgFolder, name), 96))
                    { Stretch = Stretch.UniformToFill };
            }
            catch { continue; }   // archivo dañado: no se muestra

            if (name == _bgImageFile)
            {
                var ring = new Border { CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(2) };
                ring.SetResourceReference(Border.BorderBrushProperty, "BrushAccentStrong");
                var dot = new System.Windows.Shapes.Ellipse();
                dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "BrushAccentStrong");
                var check = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M4,7 L6,9 L10,5"), Stroke = System.Windows.Media.Brushes.White, StrokeThickness = 1.6,
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                };
                var badge = new Grid
                {
                    Width = 14, Height = 14, Margin = new Thickness(0, 4, 4, 0),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Children = { dot, check },
                };
                tile.Child = new Grid { Children = { ring, badge } };
            }
            string n = name;
            tile.MouseLeftButtonDown += (_, e) => { SelectBgImage(n == _bgImageFile ? "" : n); e.Handled = true; };
            BgGallery.Children.Add(tile);
        }
        if (BgGallery.Children[^1] is Border last) last.Margin = new Thickness(0);
    }

    // La rueda del mouse corre la tira de costado
    private void BgStrip_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (BgStripScroll.ScrollableWidth <= 0) return;
        BgStripScroll.ScrollToHorizontalOffset(BgStripScroll.HorizontalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    private void BgImageAmount_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BgImageVeil == null) return;
        BgImageVeil.Opacity     = 1 - e.NewValue / 100.0;
        BgImageAmountLabel.Text = $"{(int)e.NewValue}%";
        if (IsLoaded) SaveSettings();   // el XAML fija el valor inicial antes de que exista todo
    }

    private void BgImageBlur_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BgImageBlurFx == null) return;
        BgImageBlurFx.Radius  = e.NewValue;
        BgImageBlurLabel.Text = $"{(int)e.NewValue}";
        if (IsLoaded) SaveSettings();
    }

    // Recorte con las esquinas redondeadas de la ventana
    private void BgImageHost_SizeChanged(object sender, SizeChangedEventArgs e)
        => BgImageHost.Clip = new RectangleGeometry(new Rect(e.NewSize), WindowCorner - 1, WindowCorner - 1);

    // Windows 11 recorta la ventana con esquinas de 8 px: el borde propio tiene que coincidir,
    // si no queda una franja del color de fondo en cada esquina. Windows 10 usa la región de 14 px.
    private static readonly double WindowCorner = Environment.OSVersion.Version.Build >= 22000 ? 8 : 14;

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
            RecPauseIcon.Data    = (Geometry)FindResource("PauseGeometry");
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
            RecBtnIcon.ClearValue(ForegroundProperty);
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

        RecPlayIcon.Data = (Geometry)FindResource("StopGeometry");
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
        RecPlayIcon.Data = (Geometry)FindResource("PlayGeometry");
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
            RecPauseIcon.Data = (Geometry)FindResource("PlayGeometry");
        }
        else if (_recorder.State == RecorderEngine.RecordState.Paused)
        {
            _recorder.Resume();
            RecPauseIcon.Data = (Geometry)FindResource("PauseGeometry");
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
            Title      = Loc.T("SaveRecording"),
            Filter     = "WAV audio (*.wav)|*.wav",
            DefaultExt = ".wav",
            FileName   = $"{Loc.T("RecordingFile")}_{DateTime.Now:yyyyMMdd_HHmmss}",
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
            FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Text = Loc.T("DragToPick")
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
                Child = ClearTypeOn(new System.Windows.Controls.StackPanel
                    { Margin = new Thickness(8), Children = { spectrum, info } }),
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
