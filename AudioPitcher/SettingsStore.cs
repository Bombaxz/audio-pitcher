using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AudioPitcher
{
    internal sealed class AppSettings
    {
        public int    PitchEngine      { get; set; } = 1;  // 0=RubberBand  1=Signalsmith
        public float  HueShift         { get; set; } = 0f;
        public int    BgPreset         { get; set; } = 0;
        public int    TextColorMode    { get; set; } = 0;
        public int    TextBrightness   { get; set; } = 100;
        public bool   MinimizeToTray   { get; set; } = false;
        public string Language         { get; set; } = "";   // "" = todavía no elegido → idioma de Windows
        public string SkippedUpdateVersion { get; set; } = "";
        public string LastRealOutputId { get; set; } = "";   // última salida real (no VB-CABLE)
        public string OutputDeviceName { get; set; } = "";
        public string TargetProcessName { get; set; } = "";   // "" = VB-Cable
        // Posición y tamaño de la ventana al cerrar (null = nunca guardado → centrada)
        public double? WindowLeft   { get; set; }
        public double? WindowTop    { get; set; }
        public double? WindowWidth  { get; set; }
        public double? WindowHeight { get; set; }
        // Imagen de fondo: archivo copiado a la carpeta de ajustes ("" = sin imagen)
        public string BgImageFile   { get; set; } = "";
        public int    BgImageAmount { get; set; } = 35;   // 0-100: cuánto se ve la imagen
        public int    BgImageBlur   { get; set; } = 10;   // radio de desenfoque
        public int    BgImageTone   { get; set; } = 0;   // 0 auto (según la imagen), 1 claro, 2 oscuro
        public int    BgPresetBeforeImage { get; set; } = 1;   // fondo al que se vuelve al sacar la imagen
        // Tono por app: proceso (minúsculas, "*" = sistema) → [pitch, formant]
        public Dictionary<string, double[]> AppPitch { get; set; } = new();
        // Atajos globales: id ("toggle", "up", "down", "show") → [modificadores, tecla virtual]
        public Dictionary<string, int[]> Hotkeys { get; set; } = new();
        public List<string> RecentApps { get; set; } = new(); // últimas apps ruteadas, más reciente primero
        public List<string> FavoriteApps { get; set; } = new(); // apps con estrella, fijas arriba
        // preset index (as string) → (colorKey → "#RRGGBB")
        public Dictionary<string, Dictionary<string, string>> CustomColors { get; set; } = new();
    }

    internal static class SettingsStore
    {
        private static readonly string _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioPitcher", "settings.json");

        public static string Folder => Path.GetDirectoryName(_path)!;

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(_path))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new();
            }
            catch { }
            return new();
        }

        // Versión recién instalada por el actualizador: al reabrir se avisa una vez
        private static readonly string _updatedFlag = Path.Combine(Path.GetDirectoryName(_path)!, "updated.txt");

        public static void MarkUpdatedTo(string version)
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); File.WriteAllText(_updatedFlag, version); } catch { }
        }

        public static string? TakeUpdatedVersion()
        {
            try
            {
                if (!File.Exists(_updatedFlag)) return null;
                string v = File.ReadAllText(_updatedFlag).Trim();
                File.Delete(_updatedFlag);
                return v;
            }
            catch { return null; }
        }

        public static void Save(AppSettings s)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(s,
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
