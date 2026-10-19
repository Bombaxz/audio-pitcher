using System.Configuration;
using System.Data;
using System.Windows;

namespace AudioPitcher;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    // Diálogos nativos de Windows (TaskDialog) con el estilo moderno.
    public App() => System.Windows.Forms.Application.EnableVisualStyles();
}

