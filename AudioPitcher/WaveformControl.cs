using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WpfColor      = System.Windows.Media.Color;
using WpfPen        = System.Windows.Media.Pen;
using WpfMouseEvent = System.Windows.Input.MouseEventArgs;
using WpfPoint      = System.Windows.Point;
using WpfCursors    = System.Windows.Input.Cursors;

namespace AudioPitcher
{
    internal sealed class WaveformControl : FrameworkElement
    {
        // ── Static brushes/pens (frozen, reused per render) ───────────────────
        private static readonly WpfPen GreenPen = MakePen(WpfColor.FromRgb(0xA6, 0xE3, 0xA1), 1.5);
        private static readonly WpfPen BluePen  = MakePen(WpfColor.FromRgb(0x89, 0xB4, 0xFA), 1.5);
        private static readonly WpfPen RedPen   = MakePen(WpfColor.FromRgb(0xF3, 0x8B, 0xA8), 2.0);
        private static readonly WpfPen PlayheadPen = MakePen(WpfColor.FromRgb(0xFF, 0xFF, 0xFF), 1.5);
        private static readonly SolidColorBrush BgBrush  = MakeBrush(WpfColor.FromRgb(0x1A, 0x1B, 0x2E));
        private static readonly SolidColorBrush DimBrush = MakeBrush(WpfColor.FromArgb(0xCC, 0x1A, 0x1B, 0x2E));

        private static WpfPen MakePen(WpfColor c, double w)
        {
            var p = new WpfPen(new SolidColorBrush(c), w);
            p.Freeze();
            return p;
        }
        private static SolidColorBrush MakeBrush(WpfColor c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        // ── Dependency properties for trim ────────────────────────────────────
        public static readonly DependencyProperty TrimStartRatioProperty =
            DependencyProperty.Register(nameof(TrimStartRatio), typeof(double), typeof(WaveformControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TrimEndRatioProperty =
            DependencyProperty.Register(nameof(TrimEndRatio), typeof(double), typeof(WaveformControl),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty PlayheadRatioProperty =
            DependencyProperty.Register(nameof(PlayheadRatio), typeof(double), typeof(WaveformControl),
                new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        // < 0 = oculto
        public double PlayheadRatio
        {
            get => (double)GetValue(PlayheadRatioProperty);
            set => SetValue(PlayheadRatioProperty, value);
        }

        public double TrimStartRatio
        {
            get => (double)GetValue(TrimStartRatioProperty);
            set => SetValue(TrimStartRatioProperty, value);
        }
        public double TrimEndRatio
        {
            get => (double)GetValue(TrimEndRatioProperty);
            set => SetValue(TrimEndRatioProperty, value);
        }

        // ── State ─────────────────────────────────────────────────────────────
        public enum DisplayMode { Scrolling, Review }
        public DisplayMode Mode { get; private set; } = DisplayMode.Scrolling;

        private float _normalizeGain = 1f;
        public float NormalizeGain
        {
            get => _normalizeGain;
            set { _normalizeGain = value; if (Mode == DisplayMode.Review) InvalidateVisual(); }
        }

        private const int    MaxPeaks      = 400;
        private const double HandleHitZone = 8.0;

        // Ring buffer for scrolling waveform
        private readonly float[] _peaks = new float[MaxPeaks];
        private int _peakHead  = 0;
        private int _peakCount = 0;

        // Downsampled peaks for review mode
        private float[] _reviewPeaks = Array.Empty<float>();

        // Drag state for trim handles
        private bool   _draggingStart, _draggingEnd;
        private double _dragOffset;

        // ── Data API ──────────────────────────────────────────────────────────

        public void AddPeak(float peak)
        {
            if (_peakCount < MaxPeaks)
            {
                _peaks[(_peakHead + _peakCount) % MaxPeaks] = peak;
                _peakCount++;
            }
            else
            {
                _peaks[_peakHead] = peak;
                _peakHead = (_peakHead + 1) % MaxPeaks;
            }
            InvalidateVisual();
        }

        public void SetReviewData(float[] allSamples)
        {
            Mode           = DisplayMode.Review;
            TrimStartRatio = 0.0;
            TrimEndRatio   = 1.0;

            if (allSamples.Length == 0) { _reviewPeaks = Array.Empty<float>(); InvalidateVisual(); return; }

            int n = Math.Min(MaxPeaks, allSamples.Length);
            _reviewPeaks   = new float[n];
            double blockF  = (double)allSamples.Length / n;
            for (int i = 0; i < n; i++)
            {
                int   start = (int)(i * blockF);
                int   end   = Math.Min((int)((i + 1) * blockF), allSamples.Length);
                float max   = 0f;
                for (int j = start; j < end; j++) max = Math.Max(max, Math.Abs(allSamples[j]));
                _reviewPeaks[i] = max;
            }
            InvalidateVisual();
        }

        public void ResetScrolling()
        {
            Mode       = DisplayMode.Scrolling;
            _peakHead  = 0;
            _peakCount = 0;
            InvalidateVisual();
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            dc.DrawRectangle(BgBrush, null, new Rect(0, 0, w, h));
            if (Mode == DisplayMode.Scrolling) RenderScrolling(dc, w, h);
            else                               RenderReview(dc, w, h);
        }

        private void RenderScrolling(DrawingContext dc, double w, double h)
        {
            if (_peakCount == 0) return;
            double mid  = h / 2;
            double colW = w / MaxPeaks;
            for (int i = 0; i < _peakCount; i++)
            {
                float  peak = _peaks[(_peakHead + i) % MaxPeaks];
                double x    = i * colW + colW / 2;
                double amp  = Math.Max(2.0, peak * mid * 0.9);
                dc.DrawLine(GreenPen, new WpfPoint(x, mid - amp), new WpfPoint(x, mid + amp));
            }
        }

        private void RenderReview(DrawingContext dc, double w, double h)
        {
            int    n      = _reviewPeaks.Length;
            double mid    = h / 2;
            double colW   = n > 0 ? w / n : w;
            double startX = TrimStartRatio * w;
            double endX   = TrimEndRatio   * w;

            if (n > 0)
            {
                for (int i = 0; i < n; i++)
                {
                    double x   = i * colW + colW / 2;
                    double amp = Math.Max(2.0, Math.Min(_reviewPeaks[i] * _normalizeGain * mid * 0.9, mid * 0.97));
                    dc.DrawLine(BluePen, new WpfPoint(x, mid - amp), new WpfPoint(x, mid + amp));
                }
            }

            // dim outside trim
            if (startX > 0) dc.DrawRectangle(DimBrush, null, new Rect(0, 0, startX, h));
            if (endX   < w) dc.DrawRectangle(DimBrush, null, new Rect(endX, 0, w - endX, h));

            DrawHandle(dc, startX, h, isStart: true);
            DrawHandle(dc, endX,   h, isStart: false);

            if (PlayheadRatio >= 0)
            {
                double px = PlayheadRatio * w;
                dc.DrawLine(PlayheadPen, new WpfPoint(px, 0), new WpfPoint(px, h));
            }
        }

        private static void DrawHandle(DrawingContext dc, double x, double h, bool isStart)
        {
            double arm = 6.0;
            dc.DrawLine(RedPen, new WpfPoint(x, 0), new WpfPoint(x, h));
            if (isStart)
            {
                dc.DrawLine(RedPen, new WpfPoint(x, 2),     new WpfPoint(x + arm, 2));
                dc.DrawLine(RedPen, new WpfPoint(x, h - 2), new WpfPoint(x + arm, h - 2));
            }
            else
            {
                dc.DrawLine(RedPen, new WpfPoint(x - arm, 2),     new WpfPoint(x, 2));
                dc.DrawLine(RedPen, new WpfPoint(x - arm, h - 2), new WpfPoint(x, h - 2));
            }
        }

        // ── Mouse drag for trim handles ────────────────────────────────────────

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (Mode != DisplayMode.Review) return;
            double x      = e.GetPosition(this).X;
            double w      = ActualWidth;
            double startX = TrimStartRatio * w;
            double endX   = TrimEndRatio   * w;

            if (Math.Abs(x - startX) <= HandleHitZone)
            {
                _draggingStart = true;
                _dragOffset    = x - startX;
                CaptureMouse();
                Cursor    = WpfCursors.SizeWE;
                e.Handled = true;
            }
            else if (Math.Abs(x - endX) <= HandleHitZone)
            {
                _draggingEnd = true;
                _dragOffset  = x - endX;
                CaptureMouse();
                Cursor    = WpfCursors.SizeWE;
                e.Handled = true;
            }
        }

        protected override void OnMouseMove(WpfMouseEvent e)
        {
            double x = e.GetPosition(this).X;
            double w = ActualWidth;

            if (_draggingStart)
            {
                TrimStartRatio = Math.Clamp((x - _dragOffset) / w, 0.0, TrimEndRatio - 0.01);
                e.Handled = true;
            }
            else if (_draggingEnd)
            {
                TrimEndRatio = Math.Clamp((x - _dragOffset) / w, TrimStartRatio + 0.01, 1.0);
                e.Handled = true;
            }
            else if (Mode == DisplayMode.Review)
            {
                double startX = TrimStartRatio * w;
                double endX   = TrimEndRatio   * w;
                Cursor = (Math.Abs(x - startX) <= HandleHitZone || Math.Abs(x - endX) <= HandleHitZone)
                    ? WpfCursors.SizeWE : WpfCursors.Arrow;
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (_draggingStart || _draggingEnd)
            {
                _draggingStart = _draggingEnd = false;
                ReleaseMouseCapture();
                Cursor    = WpfCursors.Arrow;
                e.Handled = true;
            }
        }
    }
}
