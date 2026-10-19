using System;
using System.Collections.Generic;
using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioPitcher
{
    public class AudioSessionInfo
    {
        public string Name { get; set; } = "";
        public string ProcessName { get; set; } = "";
        public string ExePath { get; set; } = "";
        public int ProcessId { get; set; }
        public AudioSessionControl Session { get; set; } = null!;
        public System.Windows.Media.ImageSource? Icon { get; set; }
        public bool IsFavorite { get; set; }
        public bool IsApp => ProcessId > 0;
        public override string ToString() => Name;
    }

    public class AudioEngine : IDisposable
    {
        // ── Capture / output ──────────────────────────────────────────────────
        private IWaveIn? _capture;
        private WaveOutEvent? _output;
        private BufferedWaveProvider? _buffer;

        // ── Pitch processors ─────────────────────────────────────────────────
        private RubberBandProcessor?  _rbPitch;      // standard pitch (RubberBand mode)
        private RubberBandProcessor?  _rbFormant;    // formant-preserved pitch
        private SignalsmithProcessor? _signalsmith;  // standard pitch (Signalsmith mode)
        private float _standardPitch = 0f;
        private float _formantPitch  = 0f;

        // ── Effects ──────────────────────────────────────────────────────────
        private ReverbEffect? _reverb;
        private DelayEffect?  _delay;
        private ChorusEffect? _chorus;

        // ── Format info ──────────────────────────────────────────────────────
        private int  _captureChannels = 2;
        private bool _captureIsFloat  = true;

        // ── Public ───────────────────────────────────────────────────────────
        public bool IsRunning { get; private set; }
        public static string RubberBandError { get; private set; } = "";
        public static bool IsRubberBandAvailable  { get; } = CheckRubberBand();
        public static bool IsSignalsmithAvailable { get; } = SignalsmithProcessor.IsAvailable;

        public enum PitchEngineMode { RubberBand = 0, Signalsmith = 1 }
        public PitchEngineMode PitchEngine { get; set; } = PitchEngineMode.Signalsmith;

        // Standard pitch
        public float PitchSemitones
        {
            get => _standardPitch;
            set { _standardPitch = value; _rbPitch?.SetPitch(value); _signalsmith?.SetPitch(value); }
        }

        // Formant-preserved pitch (RubberBand)
        public float FormantPitchSemitones
        {
            get => _formantPitch;
            set { _formantPitch = value; _rbFormant?.SetPitch(value); }
        }

        // Effects — setters forward to live instances
        public float ReverbWet      { set { if (_reverb != null) _reverb.Wet      = value; } }
        public float ReverbRoomSize { set { if (_reverb != null) _reverb.RoomSize = value; } }
        public float ReverbDamping  { set { if (_reverb != null) _reverb.Damping  = value; } }
        public float DelayWet       { set { if (_delay  != null) _delay.Wet       = value; } }
        public float DelayTimeMs    { set { if (_delay  != null) _delay.TimeMs    = value; } }
        public float DelayFeedback  { set { if (_delay  != null) _delay.Feedback  = value; } }
        public float ChorusWet      { set { if (_chorus != null) _chorus.Wet      = value; } }
        public float ChorusRate     { set { if (_chorus != null) _chorus.Rate     = value; } }
        public float ChorusDepth    { set { if (_chorus != null) _chorus.Depth    = value; } }

        // ── Static helpers ───────────────────────────────────────────────────
        private static bool CheckRubberBand()
        {
            try
            {
                bool ok = RubberBandProcessor.IsAvailable();
                if (!ok) RubberBandError = "RB_New returned null (DLL loaded but rubberband_new failed)";
                return ok;
            }
            catch (Exception ex)
            {
                RubberBandError = ex.ToString();
                try { System.IO.File.WriteAllText("rb_error.txt", ex.ToString()); } catch { }
                return false;
            }
        }

        public static List<AudioSessionInfo> GetActiveSessions()
        {
            var list = new List<AudioSessionInfo>();
            // Dedup por nombre de proceso: apps multiproceso (Discord, msedgewebview2)
            // abren varias sesiones y aparecerían repetidas.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int self = Environment.ProcessId;
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                // Todos los endpoints render, no solo el default: una app ruteada
                // a CABLE Input tiene su sesión en ese endpoint y debe seguir listada.
                foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    if (sessions == null) continue;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var s = sessions[i];
                        try
                        {
                            int pid = (int)s.GetProcessID;
                            if (pid == 0 || pid == self) continue;
                            var proc = Process.GetProcessById(pid);
                            if (!seen.Add(proc.ProcessName)) continue;
                            string name = string.IsNullOrEmpty(s.DisplayName) ? proc.ProcessName : s.DisplayName;
                            string exePath = "";
                            try { exePath = proc.MainModule?.FileName ?? ""; } catch { }
                            list.Add(new AudioSessionInfo
                            {
                                Name        = name,
                                ProcessName = proc.ProcessName,
                                ExePath     = exePath,
                                ProcessId   = pid,
                                Session     = s
                            });
                        }
                        catch { }
                    }
                }
            }
            catch { }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public static List<string> GetOutputDeviceNames()
        {
            var list = new List<string>();
            for (int i = 0; i < WaveOut.DeviceCount; i++)
                list.Add(WaveOut.GetCapabilities(i).ProductName);
            return list;
        }

        public static bool IsCableInstalled()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                    if (dev.FriendlyName.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase))
                        return true;
            }
            catch { }
            return false;
        }

        private static MMDevice? FindCableOutputDevice()
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                if (dev.FriendlyName.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase))
                    return dev;
            return null;
        }

        /// <summary>Endpoint de render del cable ("CABLE Input"), destino del ruteo por app.</summary>
        public static MMDevice? FindCableRenderDevice()
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                if (dev.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase))
                    return dev;
            return null;
        }

        // ── Start / Stop ─────────────────────────────────────────────────────
        public bool FxEnabled { get; set; } = true;

        public void Start(int outputDeviceIndex)
        {
            Stop();

            var cable = FindCableOutputDevice()
                ?? throw new InvalidOperationException("VB-CABLE no encontrado. Instalá desde vb-audio.com/Cable");
            var wasapi = new WasapiCapture(cable, false, 50);
            _capture = wasapi;
            WaveFormat fmt = wasapi.WaveFormat;

            _captureChannels = fmt.Channels;
            _captureIsFloat  = fmt.Encoding == WaveFormatEncoding.IeeeFloat;

            // Standard pitch
            if (PitchEngine == PitchEngineMode.RubberBand && IsRubberBandAvailable)
            {
                // R3 + smoothing + accumulator
                _rbPitch = new RubberBandProcessor(fmt.SampleRate, fmt.Channels,
                    preserveFormants: false, finerEngine: true, smoothing: true, useAccumulator: true);
                _rbPitch.SetPitch(_standardPitch);
            }
            else if (PitchEngine == PitchEngineMode.Signalsmith && IsSignalsmithAvailable)
            {
                _signalsmith = new SignalsmithProcessor(fmt.SampleRate, fmt.Channels);
                _signalsmith.SetPitch(_standardPitch);
            }

            // Formant pitch (RubberBand)
            if (IsRubberBandAvailable)
            {
                _rbFormant = new RubberBandProcessor(fmt.SampleRate, fmt.Channels, preserveFormants: true);
                _rbFormant.SetPitch(_formantPitch);
            }

            // Effects — create with current sample rate
            _reverb = new ReverbEffect(fmt.SampleRate, fmt.Channels);
            _delay  = new DelayEffect(fmt.SampleRate, fmt.Channels);
            _chorus = new ChorusEffect(fmt.SampleRate, fmt.Channels);

            var floatFmt = WaveFormat.CreateIeeeFloatWaveFormat(fmt.SampleRate, fmt.Channels);
            _buffer = new BufferedWaveProvider(floatFmt)
            {
                BufferLength = floatFmt.AverageBytesPerSecond / 2,
                DiscardOnBufferOverflow = true
            };

            _output = new WaveOutEvent { DeviceNumber = outputDeviceIndex, DesiredLatency = 150 };
            _output.Init(_buffer);
            _output.Play();

            _capture.DataAvailable += OnData;
            if (_capture is WasapiCapture w) w.StartRecording();

            IsRunning = true;
        }


        private void OnData(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0 || _buffer == null) return;

            // Convert to float[]
            float[] samples;
            int sampleCount;
            if (_captureIsFloat)
            {
                int n = e.BytesRecorded / 4;
                samples = new float[n];
                Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);
                sampleCount = n / _captureChannels;
            }
            else
            {
                int n = e.BytesRecorded / 2;
                samples = new float[n];
                for (int i = 0; i < n; i++)
                    samples[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
                sampleCount = n / _captureChannels;
            }

            // Standard pitch — skip if zero
            if (_standardPitch != 0f)
            {
                if (PitchEngine == PitchEngineMode.RubberBand && _rbPitch != null)
                {
                    float[] rb = _rbPitch.Process(samples, sampleCount);
                    if (rb.Length == 0) return;
                    samples     = rb;
                    sampleCount = samples.Length / _captureChannels;
                }
                else if (PitchEngine == PitchEngineMode.Signalsmith && _signalsmith != null)
                {
                    samples     = _signalsmith.Process(samples, sampleCount);
                    sampleCount = samples.Length / _captureChannels;
                }
            }

            // Formant pitch — skip if zero
            if (_formantPitch != 0f && _rbFormant != null)
            {
                float[] rb = _rbFormant.Process(samples, sampleCount);
                if (rb.Length == 0) return;
                samples = rb;
                sampleCount = samples.Length / _captureChannels;
            }

            // Effects (in-place)
            if (FxEnabled)
            {
                _reverb?.ProcessInPlace(samples, sampleCount);
                _delay?.ProcessInPlace(samples, sampleCount);
                _chorus?.ProcessInPlace(samples, sampleCount);
            }

            // Output
            int byteLen = sampleCount * _captureChannels * 4;
            byte[] bytes = new byte[byteLen];
            Buffer.BlockCopy(samples, 0, bytes, 0, byteLen);
            _buffer.AddSamples(bytes, 0, byteLen);
        }

        public void Stop()
        {
            // Sin guarda por IsRunning: si Start() falló a mitad hay que soltar la captura igual.
            _capture?.StopRecording();
            _capture?.Dispose();
            _output?.Stop();
            _output?.Dispose();
            _rbPitch?.Dispose();
            _rbFormant?.Dispose();
            _signalsmith?.Dispose();
            _capture     = null;
            _output      = null;
            _buffer      = null;
            _rbPitch     = null;
            _rbFormant   = null;
            _signalsmith = null;
            _reverb    = null;
            _delay     = null;
            _chorus    = null;
            IsRunning   = false;
        }

        public void Dispose() => Stop();
    }
}
