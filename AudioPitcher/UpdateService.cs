using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace AudioPitcher
{
    internal sealed record UpdateInfo(Version Version, string Notes, string InstallerUrl);

    // Consulta la última Release de GitHub y, si es más nueva, baja y corre el instalador.
    internal static class UpdateService
    {
        public const string Repo = "Bombaxz/audio-pitcher";

        private static readonly HttpClient _http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("AudioPitcher-Updater");
            return c;
        }

        public static Version CurrentVersion =>
            Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

        public static async Task<UpdateInfo?> CheckAsync()
        {
#if DEBUG
            // Para ver el aviso sin publicar nada: set AP_FAKE_UPDATE=1 en run.bat
            if (Environment.GetEnvironmentVariable("AP_FAKE_UPDATE") == "1")
                return new UpdateInfo(new Version(9, 9, 9),
                    "• Arreglos de prueba\n• Esto es un simulacro, no se instala nada", FakeUrl);
#endif
            try
            {
                var json = await _http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
                if (!Version.TryParse(tag, out var latest)) return null;
                if (Normalize(latest) <= Normalize(CurrentVersion)) return null;

                string? url = null;
                foreach (var asset in root.GetProperty("assets").EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        url = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
                if (url == null) return null;

                var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                return new UpdateInfo(latest, notes, url);
            }
            catch
            {
                return null;
            }
        }

        public const string FakeUrl = "fake";

        public static async Task<bool> DownloadAndRunAsync(UpdateInfo info, IProgress<int> progress,
                                                           System.Threading.CancellationToken ct = default)
        {
            if (info.InstallerUrl == FakeUrl)
            {
                for (int p = 0; p <= 100; p += 2) { progress.Report(p); await Task.Delay(60, ct); }
                return true;
            }
            if (string.IsNullOrEmpty(info.InstallerUrl)) return false;
            try
            {
                var path = Path.Combine(Path.GetTempPath(), $"AudioPitcher-Setup-{info.Version}.exe");
                using (var resp = await _http.GetAsync(info.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? -1;
                    await using var src = await resp.Content.ReadAsStreamAsync(ct);
                    await using var dst = File.Create(path);
                    var buf = new byte[81920];
                    long done = 0;
                    int read;
                    while ((read = await src.ReadAsync(buf, ct)) > 0)
                    {
                        await dst.WriteAsync(buf.AsMemory(0, read), ct);
                        done += read;
                        if (total > 0) progress.Report((int)(done * 100 / total));
                    }
                }

                Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART")
                    { UseShellExecute = true });
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                return false;
            }
        }

        private static Version Normalize(Version v) =>
            new(v.Major, v.Minor, Math.Max(v.Build, 0));
    }
}
