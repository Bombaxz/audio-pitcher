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
        public int    TextBrightness   { get; set; } = 50;
        public bool   MinimizeToTray   { get; set; } = false;
        public string SkippedUpdateVersion { get; set; } = "";
        public string LastRealOutputId { get; set; } = "";   // última salida real (no VB-CABLE)
        public string OutputDeviceName { get; set; } = "";
        public string TargetProcessName { get; set; } = "";   // "" = VB-Cable
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
