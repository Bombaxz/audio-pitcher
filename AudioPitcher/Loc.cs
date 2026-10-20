using System;
using System.Collections.Generic;
using System.Globalization;

namespace AudioPitcher
{
    // Textos de la UI. XAML: {DynamicResource S.Clave}; código: Loc.T("Clave").
    internal static class Loc
    {
        public static string Current { get; private set; } = "es";
        public static event Action? Changed;

        // "" = nunca elegido → idioma de Windows (español si es es-*, si no inglés)
        public static void Init(string saved) =>
            Apply(saved is "es" or "en" ? saved
                : CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en");

        public static void Set(string lang)
        {
            if (lang == Current) return;
            Apply(lang);
            Changed?.Invoke();
        }

        private static void Apply(string lang)
        {
            Current = lang;
            var res = System.Windows.Application.Current.Resources;
            foreach (var (k, v) in _strings) res["S." + k] = lang == "en" ? v.En : v.Es;
        }

        public static string T(string key, params object[] args) => Get(key, Current, args);
        public static string Es(string key, params object[] args) => Get(key, "es", args);

        private static string Get(string key, string lang, object[] args)
        {
            if (!_strings.TryGetValue(key, out var v)) return key;
            string s = lang == "en" ? v.En : v.Es;
            return args.Length > 0 ? string.Format(s, args) : s;
        }

        private static readonly Dictionary<string, (string Es, string En)> _strings = new()
        {
            // Encabezado
            ["RecTip"]          = ("Grabar audio del sistema", "Record system audio"),
            ["SettingsTip"]     = ("Configuración", "Settings"),
            ["PinnedTip"]       = ("Fijada al frente (clic para soltar)", "Kept on top (click to release)"),
            ["UnpinnedTip"]     = ("No está fijada (clic para fijar)", "Not on top (click to pin)"),
            ["MinimizeTip"]     = ("Minimizar", "Minimize"),
            ["HideToTrayTip"]   = ("Esconder en la bandeja", "Hide to tray"),
            ["UpdateTo"]        = ("Actualizar a {0}", "Update to {0}"),

            // Panel principal
            ["AppTip"]          = ("Elegí de qué programa capturar el audio", "Choose which program to capture audio from"),
            ["NoPerAppRouting"] = ("El ruteo por app no está disponible en esta versión de Windows.",
                                   "Per-app routing isn't available on this version of Windows."),
            ["FavTip"]          = ("Fijar arriba de la lista", "Pin to the top of the list"),
            ["SystemSource"]    = ("Sistema (VB-Cable)", "System (VB-Cable)"),
            ["MoreOptions"]     = ("▾  Más opciones…", "▾  More options…"),
            ["ValueTip"]        = ("Arrastrá para cambiar · Click para editar · Click derecho para color",
                                   "Drag to change · Click to edit · Right-click for color"),
            ["ResetTip"]        = ("Resetear", "Reset"),
            ["NeedsRubberBand"] = (" (requiere rubberband.dll)", " (requires rubberband.dll)"),
            ["KnobTip"]         = ("Clic para cambiar color del arco", "Click to change the arc color"),
            ["DragToPick"]      = ("Arrastrá para elegir", "Drag to pick"),

            // Estado
            ["StatusNoAudio"]       = ("Sin audio. Revisá el enrutamiento.", "No audio. Check the routing."),
            ["StatusProcessing"]    = ("Procesando audio en vivo.", "Processing audio live."),
            ["StatusProcessingApp"] = ("Procesando {0} en vivo.", "Processing {0} live."),
            ["StatusUpdated"]       = ("Listo: actualizado a la versión {0}.", "Done: updated to version {0}."),
            ["StatusError"]         = ("Error: {0}", "Error: {0}"),
            ["Raw"]                 = ("{0}", "{0}"),
            ["CableNotFound"]       = ("VB-CABLE no encontrado. Instalá desde vb-audio.com/Cable",
                                       "VB-CABLE not found. Install it from vb-audio.com/Cable"),
            ["RouteFailed"]         = ("No se pudo rutear la app: {0}", "Couldn't route the app: {0}"),
            ["DownloadingCable"]    = ("Descargando VB-Cable…", "Downloading VB-Cable…"),

            // Configuración
            ["SettingsTitle"]   = ("CONFIGURACIÓN", "SETTINGS"),
            ["AudioOutput"]     = ("SALIDA DE AUDIO", "AUDIO OUTPUT"),
            ["DefaultDevice"]   = ("Dispositivo predeterminado", "Default device"),
            ["OutputHint"]      = ("Cambiar mientras corre reinicia el audio.", "Changing it while running restarts the audio."),
            ["PitchEngine"]     = ("MOTOR PITCH", "PITCH ENGINE"),
            ["RbNA"]            = ("RB (N/D)", "RB (N/A)"),
            ["SsNA"]            = ("SS (N/D)", "SS (N/A)"),
            ["OnMinimize"]      = ("AL MINIMIZAR", "WHEN MINIMIZED"),
            ["Taskbar"]         = ("Barra de tareas", "Taskbar"),
            ["TaskbarTip"]      = ("Queda en la barra de tareas, como cualquier programa",
                                   "Stays in the taskbar, like any other program"),
            ["Tray"]            = ("Bandeja", "Tray"),
            ["TrayTip"]         = ("Se esconde junto al reloj; doble clic en el ícono para volver",
                                   "Hides next to the clock; double-click the icon to bring it back"),
            ["Language"]        = ("IDIOMA", "LANGUAGE"),
            ["Appearance"]      = ("APARIENCIA", "APPEARANCE"),
            ["ResetColorsTip"]  = ("Restablecer colores del tema actual", "Reset the current theme's colors"),
            ["Text"]            = ("TEXTO", "TEXT"),
            ["Contrast"]        = ("Contraste", "Contrast"),
            ["Background"]      = ("FONDO", "BACKGROUND"),
            ["SourceCode"]      = ("Código fuente", "Source code"),
            ["Licenses"]        = ("Licencias", "Licenses"),
            ["Report"]          = ("Reportar un problema", "Report a problem"),
            ["ReportTip"]       = ("Guarda un reporte en el Escritorio para enviarlo",
                                   "Saves a report to your Desktop so you can send it"),
            ["ReportReady"]     = ("Reporte listo", "Report ready"),
            ["ReportReadyMsg"]  = ("Se guardó un reporte en tu Escritorio:\n{0}\n\n" +
                                   "Mandáselo a quien te pasó el programa, o adjuntalo en GitHub (Issues).\n" +
                                   "Tiene datos del programa y de tus dispositivos de audio, nada personal.",
                                   "A report was saved to your Desktop:\n{0}\n\n" +
                                   "Send it to whoever gave you the program, or attach it on GitHub (Issues).\n" +
                                   "It has data about the program and your audio devices, nothing personal."),
            ["ReportFailed"]    = ("No se pudo crear el reporte: {0}", "Couldn't create the report: {0}"),

            // Grabación
            ["PreviewTip"]      = ("Escuchar lo que se va a guardar", "Listen to what will be saved"),
            ["PauseTip"]        = ("Pausar / Reanudar", "Pause / Resume"),
            ["StopTip"]         = ("Detener", "Stop"),
            ["NormTip"]         = ("Normalizar al guardar", "Normalize on save"),
            ["SaveWav"]         = ("Guardar WAV", "Save WAV"),
            ["SaveRecording"]   = ("Guardar grabación", "Save recording"),
            ["RecordingFile"]   = ("grabacion", "recording"),

            // Bandeja
            ["TrayShow"]        = ("Mostrar", "Show"),
            ["TrayClose"]       = ("Cerrar", "Close"),

            // VB-CABLE
            ["CableRestartTitle"]  = ("Falta reiniciar", "Restart needed"),
            ["CableRestartMsg"]    = ("VB-CABLE está instalado, pero Windows todavía no lo activó.\n\n" +
                                      "Hay que reiniciar la PC una vez para que funcione. ¿Reiniciar ahora?",
                                      "VB-CABLE is installed, but Windows hasn't activated it yet.\n\n" +
                                      "The PC needs to restart once for it to work. Restart now?"),
            ["CableDisabledTitle"] = ("VB-CABLE desactivado", "VB-CABLE disabled"),
            ["CableDisabledMsg"]   = ("VB-CABLE está desactivado en Windows.\n\n" +
                                      "Se va a abrir la configuración de sonido: en las pestañas Reproducción y Grabación, " +
                                      "hacé clic derecho en \"CABLE\" y elegí \"Habilitar\".",
                                      "VB-CABLE is disabled in Windows.\n\n" +
                                      "The sound settings will open: on the Playback and Recording tabs, " +
                                      "right-click \"CABLE\" and choose \"Enable\"."),
            ["CableMissingTitle"]  = ("VB-Cable no encontrado", "VB-Cable not found"),
            ["CableMissingMsg"]    = ("VB-Cable no está instalado.\n\n" +
                                      "Audio Pitcher necesita VB-Cable para funcionar como dispositivo virtual de audio. " +
                                      "¿Querés instalarlo ahora?\n\n" +
                                      "(Se descarga de vb-audio.com y requiere permisos de administrador)",
                                      "VB-Cable isn't installed.\n\n" +
                                      "Audio Pitcher needs VB-Cable to work as a virtual audio device. " +
                                      "Do you want to install it now?\n\n" +
                                      "(It downloads from vb-audio.com and needs administrator permissions)"),
            ["CableInstalledTitle"] = ("VB-Cable instalado", "VB-Cable installed"),
            ["CableInstalledMsg"]   = ("Instalación completada.\n\n" +
                                       "Cerrá y volvé a abrir Audio Pitcher. Si el cable no aparece, reiniciá Windows.",
                                       "Installation complete.\n\n" +
                                       "Close and reopen Audio Pitcher. If the cable doesn't show up, restart Windows."),
            ["CableDownloadTitle"]  = ("Error de descarga", "Download error"),
            ["CableDownloadMsg"]    = ("No se pudo descargar VB-Cable automáticamente.\n\n" +
                                       "Se va a abrir la página oficial para que lo descargues manualmente:\n" +
                                       "vb-audio.com/Cable",
                                       "VB-Cable couldn't be downloaded automatically.\n\n" +
                                       "The official page will open so you can download it manually:\n" +
                                       "vb-audio.com/Cable"),

            // Ventana de actualización
            ["NewVersion"]      = ("Hay una nueva versión disponible.", "A new version is available."),
            ["NotNow"]          = ("Ahora no", "Not now"),
            ["Update"]          = ("Actualizar", "Update"),
            ["Cancel"]          = ("Cancelar", "Cancel"),
            ["Close"]           = ("Cerrar", "Close"),
            ["DownloadFailed"]  = ("No se pudo descargar.", "Download failed."),
            ["Installing"]      = ("Instalando… se vuelve a abrir sola", "Installing… it will reopen on its own"),
        };
    }
}
