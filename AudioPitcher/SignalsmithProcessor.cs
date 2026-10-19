using System;
using System.Runtime.InteropServices;

namespace AudioPitcher
{
    internal sealed class SignalsmithProcessor : IDisposable
    {
        private const string Dll = "signalsmith_stretch.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ss_create(int channels, float sampleRate);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ss_destroy(IntPtr handle);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ss_set_pitch(IntPtr handle, float semitones);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ss_process(IntPtr handle,
            [In]  float[] input,  int inputSamples,
            [Out] float[] output);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ss_input_latency(IntPtr handle);

        // ── State ─────────────────────────────────────────────────────────────
        private IntPtr _handle;
        private readonly int _channels;
        private float[] _outBuf = Array.Empty<float>();

        public static bool IsAvailable { get; } = Check();

        private static bool Check()
        {
            try
            {
                var h = ss_create(1, 44100f);
                if (h == IntPtr.Zero) return false;
                ss_destroy(h);
                return true;
            }
            catch { return false; }
        }

        public SignalsmithProcessor(int sampleRate, int channels)
        {
            _channels = channels;
            _handle   = ss_create(channels, (float)sampleRate);
            if (_handle == IntPtr.Zero)
                throw new InvalidOperationException("ss_create returned null");
        }

        public void SetPitch(float semitones)
        {
            if (_handle != IntPtr.Zero)
                ss_set_pitch(_handle, semitones);
        }

        public float[] Process(float[] input, int sampleCount)
        {
            if (_handle == IntPtr.Zero) return Array.Empty<float>();

            int total = sampleCount * _channels;
            if (_outBuf.Length < total) _outBuf = new float[total];

            ss_process(_handle, input, sampleCount, _outBuf);

            var result = new float[total];
            Array.Copy(_outBuf, result, total);
            return result;
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                ss_destroy(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
