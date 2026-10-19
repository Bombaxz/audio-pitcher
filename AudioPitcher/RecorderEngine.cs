using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioPitcher
{
    internal sealed class RecorderEngine : IDisposable
    {
        private WasapiLoopbackCapture? _capture;
        private readonly List<float>  _samples = new();
        private readonly object       _lock    = new();
        private bool                  _paused;

        public enum RecordState { Idle, Recording, Paused, Stopped }
        public RecordState State  { get; private set; } = RecordState.Idle;
        public WaveFormat? Format { get; private set; }

        public TimeSpan Duration
        {
            get
            {
                if (Format == null) return TimeSpan.Zero;
                lock (_lock) return TimeSpan.FromSeconds((double)_samples.Count / Format.SampleRate / Format.Channels);
            }
        }

        // fires on audio thread — caller must dispatch to UI before touching controls
        public event EventHandler<float[]>? SamplesAvailable;

        public void Start()
        {
            if (State == RecordState.Recording) return;
            lock (_lock) _samples.Clear();

            _capture = new WasapiLoopbackCapture();
            Format   = _capture.WaveFormat;
            _paused  = false;

            _capture.DataAvailable += OnData;
            _capture.StartRecording();
            State = RecordState.Recording;
        }

        private void OnData(object? sender, WaveInEventArgs e)
        {
            if (_paused || e.BytesRecorded == 0) return;

            float[] chunk;
            if (Format!.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                chunk = new float[e.BytesRecorded / 4];
                Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);
            }
            else if (Format.BitsPerSample == 16)
            {
                chunk = new float[e.BytesRecorded / 2];
                for (int i = 0; i < chunk.Length; i++)
                    chunk[i] = BitConverter.ToInt16(e.Buffer, i * 2) / 32768f;
            }
            else return;

            lock (_lock) _samples.AddRange(chunk);
            SamplesAvailable?.Invoke(this, chunk);
        }

        public void Pause()
        {
            if (State != RecordState.Recording) return;
            _paused = true;
            State   = RecordState.Paused;
        }

        public void Resume()
        {
            if (State != RecordState.Paused) return;
            _paused = false;
            State   = RecordState.Recording;
        }

        public void Stop()
        {
            _capture?.StopRecording();
            _capture?.Dispose();
            _capture = null;
            if (State != RecordState.Idle) State = RecordState.Stopped;
        }

        public float[] GetAllSamples()
        {
            lock (_lock) return _samples.ToArray();
        }

        private bool GetTrimmed(double trimStartRatio, double trimEndRatio, bool normalize,
                                out float[] all, out int start, out int end, out float gain)
        {
            all = Array.Empty<float>(); start = end = 0; gain = 1f;
            if (Format == null) return false;
            all = GetAllSamples();
            if (all.Length == 0) return false;

            int ch = Format.Channels;
            start  = ((int)(all.Length * Math.Clamp(trimStartRatio, 0, 1)) / ch) * ch;
            end    = ((int)(all.Length * Math.Clamp(trimEndRatio,   0, 1)) / ch) * ch;
            if (end <= start) return false;

            if (normalize)
            {
                float peak = 0f;
                for (int i = start; i < end; i++) peak = Math.Max(peak, Math.Abs(all[i]));
                if (peak > 0.001f) gain = 0.95f / peak;
            }
            return true;
        }

        public void SaveWav(string path, double trimStartRatio, double trimEndRatio, bool normalize)
        {
            if (!GetTrimmed(trimStartRatio, trimEndRatio, normalize, out var all, out int start, out int end, out float gain))
                return;

            var fmt = WaveFormat.CreateIeeeFloatWaveFormat(Format!.SampleRate, Format.Channels);
            using var writer = new WaveFileWriter(path, fmt);
            for (int i = start; i < end; i++)
                writer.WriteSample(all[i] * gain);
        }

        public PreviewSampleProvider? CreatePreview(double trimStartRatio, double trimEndRatio, bool normalize)
        {
            if (!GetTrimmed(trimStartRatio, trimEndRatio, normalize, out var all, out int start, out int end, out float gain))
                return null;
            return new PreviewSampleProvider(all, start, end, gain,
                WaveFormat.CreateIeeeFloatWaveFormat(Format!.SampleRate, Format.Channels));
        }

        public void Reset()
        {
            Stop();
            lock (_lock) _samples.Clear();
            State  = RecordState.Idle;
            Format = null;
        }

        public void Dispose() => Stop();
    }

    internal sealed class PreviewSampleProvider : ISampleProvider
    {
        private readonly float[] _data;
        private readonly int     _end;
        private readonly float   _gain;
        private volatile int     _pos;

        public WaveFormat WaveFormat { get; }
        public double PositionRatio => (double)_pos / _data.Length;

        public PreviewSampleProvider(float[] data, int start, int end, float gain, WaveFormat format)
        {
            _data = data; _pos = start; _end = end; _gain = gain; WaveFormat = format;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, _end - _pos);
            if (n <= 0) return 0;
            for (int i = 0; i < n; i++) buffer[offset + i] = _data[_pos + i] * _gain;
            _pos += n;
            return n;
        }
    }
}
