using System.IO;
using System.Text;
using NAudio.CoreAudioApi;

namespace AudioPitcher
{
    /// <summary>
    /// Registro local para diagnosticar problemas. Queda en %LOCALAPPDATA%\AudioPitcher\logs
    /// y solo sale de la PC si el usuario lo envía (botón "Reportar un problema").
    /// </summary>
    internal static class Log
    {
        private const long MaxBytes = 512 * 1024;
        private static readonly object _lock = new();

        public static string Folder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AudioPitcher", "logs");
        public static string FilePath { get; } = Path.Combine(Folder, "audiopitcher.log");

        public static void Info(string msg) => Write("INFO ", msg);
        public static void Warn(string msg) => Write("WARN ", msg);
        public static void Error(string msg, Exception? ex = null) =>
            Write("ERROR", ex == null ? msg : $"{msg}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

        private static void Write(string level, string msg)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(Folder);
                    // Rotación simple: el archivo anterior queda como .old
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > MaxBytes)
                        File.Move(FilePath, FilePath + ".old", overwrite: true);
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {msg}{Environment.NewLine}");
                }
            }
            catch { }
        }

        /// <summary>Datos del sistema y del audio al arrancar.</summary>
        public static void Startup()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"── Audio Pitcher {UpdateService.CurrentVersion.ToString(3)} ──");
            sb.AppendLine($"Windows {Environment.OSVersion.Version} {(Environment.Is64BitOperatingSystem ? "x64" : "x86")}");
            sb.AppendLine(DescribeAudio());
            Info(sb.ToString().TrimEnd());
        }

        public static string DescribeAudio()
        {
            var sb = new StringBuilder();
            try
            {
                using var en = new MMDeviceEnumerator();
                string Def(DataFlow f, Role r)
                {
                    try { return en.GetDefaultAudioEndpoint(f, r).FriendlyName; } catch { return "(ninguno)"; }
                }
                sb.AppendLine($"Salida predeterminada: {Def(DataFlow.Render, Role.Multimedia)} | comunicaciones: {Def(DataFlow.Render, Role.Communications)}");
                sb.AppendLine($"Entrada predeterminada: {Def(DataFlow.Capture, Role.Multimedia)}");
                sb.AppendLine($"Estado VB-CABLE: {AudioEngine.GetCableStatus()}");
                // Solo activos y desactivados: los desconectados son ruido
                foreach (var dev in en.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active | DeviceState.Disabled))
                {
                    string name;
                    try { name = dev.FriendlyName; } catch { name = "(sin nombre)"; }
                    sb.AppendLine($"  [{dev.DataFlow}/{dev.State}] {name}");
                }
            }
            catch (Exception ex) { sb.AppendLine($"No se pudieron listar dispositivos: {ex.Message}"); }
            return sb.ToString().TrimEnd();
        }

        /// <summary>Copia el log al Escritorio con un resumen actual y devuelve la ruta.</summary>
        public static string ExportReport()
        {
            Info("Reporte solicitado\n" + DescribeAudio());
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string dest = Path.Combine(desktop, $"AudioPitcher-reporte-{DateTime.Now:yyyyMMdd-HHmm}.txt");
            lock (_lock)
            {
                var sb = new StringBuilder();
                if (File.Exists(FilePath + ".old")) sb.Append(File.ReadAllText(FilePath + ".old"));
                if (File.Exists(FilePath)) sb.Append(File.ReadAllText(FilePath));
                File.WriteAllText(dest, sb.ToString());
            }
            return dest;
        }
    }
}
