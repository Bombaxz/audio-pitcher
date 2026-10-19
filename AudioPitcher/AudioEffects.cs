using System;

namespace AudioPitcher
{
    // ── FreeVerb-style algorithmic reverb ─────────────────────────────────────
    internal sealed class ReverbEffect
    {
        private static readonly int[] CombTunings   = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
        private static readonly int[] ApTunings     = { 556, 441, 341, 225 };
        private const int StereoSpread = 23;
        private const float FixedGain  = 0.015f;
        private const float ScaleWet   = 3f;
        private const float ScaleDry   = 2f;

        private readonly CombFilter[][]    _combs;
        private readonly AllPassFilter[][] _aps;
        private readonly int _channels;

        private float _wet1, _wet2, _dry = 1f, _roomFb, _damp1, _damp2;

        public float Wet      { set { _wet1 = value * ScaleWet * (1f + _width) / 2f; _wet2 = value * ScaleWet * (1f - _width) / 2f; } }
        public float RoomSize { set { _roomFb = value * 0.28f + 0.7f; ApplyToCombs(); } }
        public float Damping  { set { _damp1 = value * 0.4f; _damp2 = 1f - _damp1; ApplyToCombs(); } }

        private float _width = 1f;

        public ReverbEffect(int sampleRate, int channels)
        {
            _channels = channels;
            float scale = sampleRate / 44100f;

            _combs = new CombFilter[channels][];
            _aps   = new AllPassFilter[channels][];
            for (int ch = 0; ch < channels; ch++)
            {
                int spread = ch > 0 ? StereoSpread : 0;
                _combs[ch] = new CombFilter[CombTunings.Length];
                for (int i = 0; i < CombTunings.Length; i++)
                    _combs[ch][i] = new CombFilter((int)((CombTunings[i] + spread) * scale));

                _aps[ch] = new AllPassFilter[ApTunings.Length];
                for (int i = 0; i < ApTunings.Length; i++)
                    _aps[ch][i] = new AllPassFilter((int)((ApTunings[i] + spread) * scale));
            }

            RoomSize = 0.5f;
            Damping  = 0.5f;
            Wet      = 0f;
        }

        public void ProcessInPlace(float[] samples, int sampleCount)
        {
            if (_wet1 <= 0) return;
            for (int i = 0; i < sampleCount; i++)
            {
                for (int ch = 0; ch < _channels; ch++)
                {
                    int idx = i * _channels + ch;
                    float inp = samples[idx] * FixedGain;
                    float outVal = 0f;
                    foreach (var c in _combs[ch]) outVal += c.Process(inp);
                    foreach (var a in _aps[ch])   outVal  = a.Process(outVal);
                    samples[idx] = samples[idx] * _dry + outVal * _wet1;
                }
            }
        }

        private void ApplyToCombs()
        {
            for (int ch = 0; ch < _channels; ch++)
                foreach (var c in _combs[ch]) { c.Feedback = _roomFb; c.Damp1 = _damp1; c.Damp2 = _damp2; }
        }

        private sealed class CombFilter
        {
            private readonly float[] _buf;
            private int _pos;
            private float _store;
            public float Feedback, Damp1, Damp2;
            public CombFilter(int size) { _buf = new float[Math.Max(size, 1)]; }
            public float Process(float inp)
            {
                float output = _buf[_pos];
                _store = output * Damp2 + _store * Damp1;
                _buf[_pos] = inp + _store * Feedback;
                if (++_pos >= _buf.Length) _pos = 0;
                return output;
            }
        }

        private sealed class AllPassFilter
        {
            private readonly float[] _buf;
            private int _pos;
            public AllPassFilter(int size) { _buf = new float[Math.Max(size, 1)]; }
            public float Process(float inp)
            {
                float bufout = _buf[_pos];
                _buf[_pos] = inp + bufout * 0.5f;
                if (++_pos >= _buf.Length) _pos = 0;
                return bufout - inp;
            }
        }
    }

    // ── Feedback delay ─────────────────────────────────────────────────────────
    internal sealed class DelayEffect
    {
        private readonly float[][] _lines;
        private readonly int[]     _pos;
        private readonly int _channels;
        private readonly int _sampleRate;
        private int   _delaySamples;
        private float _feedback;
        private float _wet, _dry;

        public float Wet      { set { _wet = value; _dry = 1f; } }
        public float TimeMs   { set { _delaySamples = Math.Max(1, (int)(value / 1000f * _sampleRate)); } }
        public float Feedback { set { _feedback = Math.Clamp(value, 0f, 0.93f); } }

        public DelayEffect(int sampleRate, int channels)
        {
            _sampleRate = sampleRate;
            _channels   = channels;
            int maxSamples = sampleRate; // 1 second max
            _lines = new float[channels][];
            _pos   = new int[channels];
            for (int ch = 0; ch < channels; ch++)
                _lines[ch] = new float[maxSamples];

            TimeMs   = 300f;
            Feedback = 0.45f;
            Wet      = 0f;
        }

        public void ProcessInPlace(float[] samples, int sampleCount)
        {
            if (_wet <= 0) return;
            int len = _lines[0].Length;
            for (int i = 0; i < sampleCount; i++)
            {
                for (int ch = 0; ch < _channels; ch++)
                {
                    int idx  = i * _channels + ch;
                    int rPos = (_pos[ch] - _delaySamples + len) % len;
                    float delayed = _lines[ch][rPos];
                    _lines[ch][_pos[ch]] = samples[idx] + delayed * _feedback;
                    samples[idx] = samples[idx] * _dry + delayed * _wet;
                    if (++_pos[ch] >= len) _pos[ch] = 0;
                }
            }
        }
    }

    // ── Chorus (2 modulated delay lines) ──────────────────────────────────────
    internal sealed class ChorusEffect
    {
        private const int NumVoices = 2;
        private readonly float[][] _lines;
        private readonly int[]     _pos;
        private readonly int   _channels;
        private readonly float _sampleRate;
        private readonly float[] _lfoPhase;
        private readonly float[] _lfoOffset; // per-voice phase offset

        private float _wet, _dry;
        private float _rateRadians;  // LFO rad/sample
        private float _depthSamples;
        private readonly float _baseDelaySamples;

        public float Wet   { set { _wet = value; _dry = 1f; } }
        public float Rate  { set { _rateRadians = (float)(2.0 * Math.PI * value / _sampleRate); } }
        public float Depth { set { _depthSamples = value / 1000f * _sampleRate; } }

        public ChorusEffect(int sampleRate, int channels)
        {
            _sampleRate = sampleRate;
            _channels   = channels;
            _baseDelaySamples = 0.015f * sampleRate; // 15ms base delay
            int bufSize = sampleRate / 5; // 200ms max depth

            _lines    = new float[channels * NumVoices][];
            _pos      = new int[channels * NumVoices];
            _lfoPhase = new float[NumVoices];
            _lfoOffset = new float[NumVoices];
            for (int v = 0; v < NumVoices; v++)
                _lfoOffset[v] = (float)(v * Math.PI); // voices 180° apart

            for (int i = 0; i < channels * NumVoices; i++)
                _lines[i] = new float[bufSize];

            Rate  = 0.8f;
            Depth = 5f;
            Wet   = 0f;
        }

        public void ProcessInPlace(float[] samples, int sampleCount)
        {
            if (_wet <= 0) return;
            int bufSize = _lines[0].Length;

            for (int i = 0; i < sampleCount; i++)
            {
                // Advance LFO
                for (int v = 0; v < NumVoices; v++)
                    _lfoPhase[v] += _rateRadians;

                for (int ch = 0; ch < _channels; ch++)
                {
                    int idx  = i * _channels + ch;
                    float wet = 0f;

                    for (int v = 0; v < NumVoices; v++)
                    {
                        int lineIdx = v * _channels + ch;
                        float modulation = (float)Math.Sin(_lfoPhase[v] + _lfoOffset[v]) * _depthSamples;
                        float delayF = _baseDelaySamples + modulation;
                        if (delayF < 1f) delayF = 1f;

                        int d0 = (int)delayF;
                        float frac = delayF - d0;
                        int r0 = (_pos[lineIdx] - d0 + bufSize) % bufSize;
                        int r1 = (_pos[lineIdx] - d0 - 1 + bufSize) % bufSize;
                        wet += _lines[lineIdx][r0] * (1f - frac) + _lines[lineIdx][r1] * frac;

                        _lines[lineIdx][_pos[lineIdx]] = samples[idx];
                        if (++_pos[lineIdx] >= bufSize) _pos[lineIdx] = 0;
                    }

                    samples[idx] = samples[idx] * _dry + (wet / NumVoices) * _wet;
                }
            }
        }
    }
}
