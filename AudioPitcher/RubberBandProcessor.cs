using System;
using System.Collections.Generic;
using RubberBand;

namespace AudioPitcher
{
    internal sealed class RubberBandProcessor : IDisposable
    {
        private RubberBandStretcher? _rb;
        private readonly int  _channels;
        private readonly bool _useAccumulator;
        private float[][] _inCh;
        private float[][] _outCh;

        // Fixed-chunk accumulator (used by R3 engines for quality)
        private float[] _accum = new float[32768];
        private int     _accumLen = 0;
        private const int ChunkSamples = 4096;

        private const RubberBandStretcher.Options EngineFiner = (RubberBandStretcher.Options)0x20000000;

        public static bool IsAvailable()
        {
            var s = new RubberBandStretcher(44100, 1,
                RubberBandStretcher.Options.ProcessRealTime | EngineFiner, 1.0, 1.0);
            s.Dispose();
            return true;
        }

        // finerEngine = R3 spectral analysis; smoothing = reduces transient ghosting; useAccumulator = fixed-chunk feeding
        public RubberBandProcessor(int sampleRate, int channels, bool preserveFormants,
                                   bool finerEngine = true, bool smoothing = false, bool useAccumulator = true)
        {
            _channels       = channels;
            _useAccumulator = useAccumulator;

            var opts = RubberBandStretcher.Options.ProcessRealTime;
            if (finerEngine) opts |= EngineFiner;
            if (smoothing)
            {
                opts |= RubberBandStretcher.Options.SmoothingOn;
                opts |= RubberBandStretcher.Options.TransientsSmooth;
            }
            if (preserveFormants) opts |= RubberBandStretcher.Options.FormantPreserved;

            _rb = new RubberBandStretcher(sampleRate, channels, opts, 1.0, 1.0);
            _rb.SetMaxProcessSize(useAccumulator ? ChunkSamples : 8192);

            int prealloc = useAccumulator ? ChunkSamples : 8192;
            _inCh  = Alloc(channels, prealloc);
            _outCh = Alloc(channels, prealloc * 2);
        }

        public void SetPitch(float semitones) =>
            _rb!.SetPitchScale(Math.Pow(2.0, semitones / 12.0));

        public float[] Process(float[] input, int sampleCount) =>
            _useAccumulator ? ProcessAccumulated(input, sampleCount) : ProcessDirect(input, sampleCount);

        private float[] ProcessDirect(float[] input, int sampleCount)
        {
            Grow(ref _inCh, sampleCount);
            for (int i = 0; i < sampleCount; i++)
                for (int ch = 0; ch < _channels; ch++)
                    _inCh[ch][i] = input[i * _channels + ch];

            _rb!.Process(_inCh, sampleCount, false);

            int avail = _rb.Available();
            if (avail <= 0) return Array.Empty<float>();

            Grow(ref _outCh, avail);
            long got = _rb.Retrieve(_outCh, avail);

            var result = new float[(int)got * _channels];
            for (int i = 0; i < (int)got; i++)
                for (int ch = 0; ch < _channels; ch++)
                    result[i * _channels + ch] = _outCh[ch][i];
            return result;
        }

        private float[] ProcessAccumulated(float[] input, int sampleCount)
        {
            int inFloats    = sampleCount * _channels;
            int chunkFloats = ChunkSamples * _channels;
            EnsureAccum(inFloats);
            Array.Copy(input, 0, _accum, _accumLen, inFloats);
            _accumLen += inFloats;

            if (_accumLen < chunkFloats) return Array.Empty<float>();

            var results = new List<float>(_accumLen);
            while (_accumLen >= chunkFloats)
            {
                for (int i = 0; i < ChunkSamples; i++)
                    for (int ch = 0; ch < _channels; ch++)
                        _inCh[ch][i] = _accum[i * _channels + ch];

                _rb!.Process(_inCh, ChunkSamples, false);

                int avail = _rb.Available();
                if (avail > 0)
                {
                    Grow(ref _outCh, avail);
                    long got = _rb.Retrieve(_outCh, avail);
                    for (int i = 0; i < (int)got; i++)
                        for (int ch = 0; ch < _channels; ch++)
                            results.Add(_outCh[ch][i]);
                }

                _accumLen -= chunkFloats;
                if (_accumLen > 0)
                    Array.Copy(_accum, chunkFloats, _accum, 0, _accumLen);
            }

            return results.Count > 0 ? results.ToArray() : Array.Empty<float>();
        }

        public void Dispose()
        {
            _rb?.Dispose();
            _rb = null;
        }

        private static float[][] Alloc(int ch, int n)
        {
            var a = new float[ch][];
            for (int i = 0; i < ch; i++) a[i] = new float[n];
            return a;
        }

        private void EnsureAccum(int needed)
        {
            if (_accumLen + needed <= _accum.Length) return;
            var bigger = new float[Math.Max(_accum.Length * 2, _accumLen + needed)];
            Array.Copy(_accum, bigger, _accumLen);
            _accum = bigger;
        }

        private void Grow(ref float[][] arr, int n)
        {
            if (arr[0].Length >= n) return;
            arr = Alloc(_channels, n * 2);
        }
    }
}
