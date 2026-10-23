using System.Configuration;
using System.Data;
using System.Windows;

namespace AudioPitcher;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    public App()
    {
        // Diálogos nativos de Windows (TaskDialog) con el estilo moderno.
        System.Windows.Forms.Application.EnableVisualStyles();

        // Cualquier error inesperado queda en el log para poder diagnosticarlo
        DispatcherUnhandledException += (_, e) => Log.Error("Error no controlado (UI)", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Error no controlado", e.ExceptionObject as Exception);
            if (e.IsTerminating)
                try { Dispatcher.Invoke(() => (MainWindow as AudioPitcher.MainWindow)?.EmergencyRestore()); } catch { }
        };
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Error("Error en tarea", e.Exception);
    }

    public const string UiFont = "Segoe UI Variable Small, Segoe UI";

    protected override void OnStartup(StartupEventArgs e)
    {
        Loc.Init(SettingsStore.Load().Language);
        // Tipografía de toda la app (también popups creados en código): Segoe UI Variable en
        // Windows 11, más nítida en tamaños chicos; en Windows 10 cae a la Segoe UI de siempre.
        var font = new System.Windows.Media.FontFamily(UiFont);
        System.Windows.Documents.TextElement.FontFamilyProperty.OverrideMetadata(
            typeof(System.Windows.Documents.TextElement), new FrameworkPropertyMetadata(font));
        System.Windows.Controls.TextBlock.FontFamilyProperty.OverrideMetadata(
            typeof(System.Windows.Controls.TextBlock), new FrameworkPropertyMetadata(font));
        // Las ventanas pasan la tipografía a todo lo de adentro (se hereda)
        Window.FontFamilyProperty.OverrideMetadata(typeof(Window), new FrameworkPropertyMetadata(font));
        base.OnStartup(e);
    }
}
