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
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Error no controlado", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Error("Error en tarea", e.Exception);
    }
}
