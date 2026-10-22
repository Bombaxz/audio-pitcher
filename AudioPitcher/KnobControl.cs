using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using WpfColor       = System.Windows.Media.Color;
using WpfSize        = System.Windows.Size;
using WpfPoint       = System.Windows.Point;
using WpfPen         = System.Windows.Media.Pen;
using WpfBrush       = System.Windows.Media.Brush;
using WpfBrushes     = System.Windows.Media.Brushes;
using WpfCursors     = System.Windows.Input.Cursors;
using WpfFontFamily  = System.Windows.Media.FontFamily;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace AudioPitcher
{
    internal sealed class KnobControl : FrameworkElement
    {
        private const double W = 54, KnobH = 50, TotalH = 70;
        private const double Cx = W / 2, Cy = KnobH / 2;
        private const double OuterR = 22, TrackR = 16;
        private const double StartDeg = -135, EndDeg = 135;

        // ── Dependency properties ──────────────────────────────────────────

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(KnobControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender,
                    (d, e) => ((KnobControl)d).RaiseValueChanged((double)e.OldValue, (double)e.NewValue)));

        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(KnobControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(KnobControl),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LabelProperty =
            DependencyProperty.Register(nameof(Label), typeof(string), typeof(KnobControl),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty KnobColorProperty =
            DependencyProperty.Register(nameof(KnobColor), typeof(WpfColor), typeof(KnobControl),
                new FrameworkPropertyMetadata(WpfColor.FromRgb(0x89, 0xB4, 0xFA), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TickValuesProperty =
            DependencyProperty.Register(nameof(TickValues), typeof(double[]), typeof(KnobControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SuffixProperty =
            DependencyProperty.Register(nameof(Suffix), typeof(string), typeof(KnobControl),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ScaleProperty =
            DependencyProperty.Register(nameof(Scale), typeof(double), typeof(KnobControl),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty DecimalPlacesProperty =
            DependencyProperty.Register(nameof(DecimalPlaces), typeof(int), typeof(KnobControl),
                new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double    Value         { get => (double)   GetValue(ValueProperty);         set => SetValue(ValueProperty, value); }
        public double    Minimum       { get => (double)   GetValue(MinimumProperty);        set => SetValue(MinimumProperty, value); }
        public double    Maximum       { get => (double)   GetValue(MaximumProperty);        set => SetValue(MaximumProperty, value); }
        public string    Label         { get => (string)   GetValue(LabelProperty);          set => SetValue(LabelProperty, value); }
        public WpfColor  KnobColor     { get => (WpfColor) GetValue(KnobColorProperty);      set => SetValue(KnobColorProperty, value); }
        public double[]? TickValues    { get => (double[]?)GetValue(TickValuesProperty);     set => SetValue(TickValuesProperty, value); }
        public string    Suffix        { get => (string)   GetValue(SuffixProperty);         set => SetValue(SuffixProperty, value); }
        public double    Scale         { get => (double)   GetValue(ScaleProperty);          set => SetValue(ScaleProperty, value); }
        public int       DecimalPlaces { get => (int)      GetValue(DecimalPlacesProperty);  set => SetValue(DecimalPlacesProperty, value); }

        // ── ValueChanged event ─────────────────────────────────────────────

        public static readonly RoutedEvent ValueChangedEvent =
            EventManager.RegisterRoutedEvent(nameof(ValueChanged), RoutingStrategy.Bubble,
                typeof(RoutedPropertyChangedEventHandler<double>), typeof(KnobControl));

        public event RoutedPropertyChangedEventHandler<double> ValueChanged
        {
            add    => AddHandler(ValueChangedEvent, value);
            remove => RemoveHandler(ValueChangedEvent, value);
        }

        private void RaiseValueChanged(double o, double n) =>
            RaiseEvent(new RoutedPropertyChangedEventArgs<double>(o, n, ValueChangedEvent));

        // ── Layout ────────────────────────────────────────────────────────

        protected override WpfSize MeasureOverride(WpfSize _) => new(W, TotalH);
        protected override WpfSize ArrangeOverride(WpfSize _) => new(W, TotalH);

        // ── Rendering ────────────────────────────────────────────────────

        protected override void OnRender(DrawingContext dc)
        {
            double t = Maximum > Minimum
                ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1)
                : 0;
            double valDeg = StartDeg + t * (EndDeg - StartDeg);

            var bgColor     = GetTheme("BrushSurface", WpfColor.FromRgb(0x2A, 0x2A, 0x3D));
            var borderColor = GetTheme("BrushHover",   WpfColor.FromRgb(0x45, 0x47, 0x5A));
            var trackColor  = GetTheme("BrushOverlay", WpfColor.FromRgb(0x2E, 0x2E, 0x45));
            var mutedColor  = GetTheme("BrushMuted",   WpfColor.FromRgb(0x6C, 0x70, 0x86));
            var textColor   = GetTheme("BrushText",    WpfColor.FromRgb(0xCD, 0xD6, 0xF4));
            var labelColor  = GetTheme("BrushMuted2",  WpfColor.FromRgb(0x4A, 0x4A, 0x6A));

            bool isWhite = ThemeService.BgPreset == 4;
            var knobPen = isWhite
                ? null
                : new WpfPen(new SolidColorBrush(borderColor), 1.5);
            dc.DrawEllipse(new SolidColorBrush(bgColor), knobPen,
                new WpfPoint(Cx, Cy), OuterR, OuterR);

            DrawArc(dc, TrackR, StartDeg, EndDeg, RoundPen(trackColor, 2.5));

            var shiftedKnob = ThemeService.ApplyThemeKnobColor(ThemeService.ShiftHue(KnobColor, ThemeService.HueShift));
            if (t > 0.005)
                DrawArc(dc, TrackR, StartDeg, valDeg, RoundPen(shiftedKnob, 2.5));

            double iRad = valDeg * Math.PI / 180;
            double ir = TrackR - 4;
            dc.DrawEllipse(WpfBrushes.White, null,
                new WpfPoint(Cx + ir * Math.Sin(iRad), Cy - ir * Math.Cos(iRad)), 2.5, 2.5);

            if (TickValues != null && Maximum > Minimum)
            {
                double range = Maximum - Minimum;
                double tickR = OuterR + 3;
                foreach (double tv in TickValues)
                {
                    double tt  = Math.Clamp((tv - Minimum) / range, 0, 1);
                    double tRad = (StartDeg + tt * (EndDeg - StartDeg)) * Math.PI / 180;
                    bool lit = Math.Abs(Value - tv) < range * 0.025;
                    var tickColor  = lit ? shiftedKnob : mutedColor;
                    float tickSize = lit ? 2.2f : 1.5f;
                    dc.DrawEllipse(new SolidColorBrush(tickColor), null,
                        new WpfPoint(Cx + tickR * Math.Sin(tRad), Cy - tickR * Math.Cos(tRad)),
                        tickSize, tickSize);
                }
            }

            const double dpi = 1.0;
            var lft = MakeFt(Label ?? "", 8.5, new SolidColorBrush(labelColor), dpi, bold: false);
            dc.DrawText(lft, new WpfPoint(Cx - lft.Width / 2, KnobH + 2));

            var vft = MakeFt(FormatValue(), 8.0, new SolidColorBrush(textColor), dpi, bold: true);
            dc.DrawText(vft, new WpfPoint(Cx - vft.Width / 2, KnobH + 14));
        }

        private static WpfPen RoundPen(WpfColor c, double thickness) =>
            new(new SolidColorBrush(c), thickness)
            { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        private static FormattedText MakeFt(string text, double size, WpfBrush brush, double dpi, bool bold) =>
            new(text, CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
                new Typeface(new WpfFontFamily("Segoe UI"), FontStyles.Normal,
                    bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
                size, brush, dpi);

        private static void DrawArc(DrawingContext dc, double r, double a0, double a1, WpfPen pen)
        {
            if (Math.Abs(a1 - a0) < 0.1) return;
            double r0 = a0 * Math.PI / 180, r1 = a1 * Math.PI / 180;
            var p0 = new WpfPoint(Cx + r * Math.Sin(r0), Cy - r * Math.Cos(r0));
            var p1 = new WpfPoint(Cx + r * Math.Sin(r1), Cy - r * Math.Cos(r1));
            double sweep = a1 - a0;
            var fig = new PathFigure { StartPoint = p0, IsFilled = false };
            fig.Segments.Add(new ArcSegment(p1, new WpfSize(r, r), 0,
                Math.Abs(sweep) > 180,
                sweep >= 0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true));
            dc.DrawGeometry(null, pen, new PathGeometry(new[] { fig }));
        }

        private string FormatValue()
        {
            double disp = Value * Scale;
            return $"{disp.ToString($"F{DecimalPlaces}")}{Suffix}";
        }

        // ── Win32 cursor anchor ───────────────────────────────────────────

        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT pt);
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

        // ── Mouse ────────────────────────────────────────────────────────

        private bool _drag;
        private int  _anchorX, _anchorY;   // screen pixels — cursor is locked here
        private double _accumulated;        // accumulated drag distance
        private double _val0;

        public KnobControl()
        {
            Cursor = WpfCursors.Hand;
            Loaded += (_, _) =>
            {
                ThemeService.ThemeChanged += OnThemeChanged;
                UpdateShadow();
                InvalidateVisual();
            };
            Unloaded += (_, _) => ThemeService.ThemeChanged -= OnThemeChanged;
        }

        private void OnThemeChanged(object? sender, EventArgs e)
        {
            UpdateShadow();
            InvalidateVisual();
        }

        private void UpdateShadow()
        {
            Effect = ThemeService.BgPreset == 4
                ? new System.Windows.Media.Effects.DropShadowEffect
                  { BlurRadius = 10, Color = WpfColor.FromRgb(0, 0, 0), Opacity = 0.13,
                    ShadowDepth = 3, Direction = 270 }
                : null;
        }

        private static WpfColor GetTheme(string key, WpfColor fallback)
        {
            if (System.Windows.Application.Current?.Resources[key] is SolidColorBrush b) return b.Color;
            return fallback;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            _drag = true;
            _val0 = Value;
            _accumulated = 0;
            GetCursorPos(out var pt);
            _anchorX = pt.X; _anchorY = pt.Y;
            CaptureMouse();
            System.Windows.Input.Mouse.OverrideCursor = WpfCursors.None;
            e.Handled = true;
        }

        protected override void OnMouseMove(WpfMouseEventArgs e)
        {
            if (!_drag) return;
            GetCursorPos(out var pt);
            double dy = _anchorY - pt.Y;
            SetCursorPos(_anchorX, _anchorY);   // lock cursor in place
            _accumulated += dy / 120.0 * (Maximum - Minimum);
            double raw = _val0 + _accumulated;

            if (TickValues != null)
            {
                double range     = Maximum - Minimum;
                double snapRange = range * 0.07;
                foreach (double tick in TickValues)
                {
                    double dist = Math.Abs(raw - tick);
                    if (dist < snapRange)
                    {
                        double t = dist / snapRange;
                        raw = tick + (raw - tick) * t * t * t;
                        break;
                    }
                }
            }

            Value = Math.Clamp(raw, Minimum, Maximum);
        }

        // Click derecho: la ventana abre el mismo selector de color que los sliders y lo guarda
        public event Action<KnobControl>? ColorPickRequested;

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            ColorPickRequested?.Invoke(this);
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            _drag = false; ReleaseMouseCapture(); System.Windows.Input.Mouse.OverrideCursor = null;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            double step = (Maximum - Minimum) / 20.0;
            Value = Math.Clamp(Value + (e.Delta > 0 ? step : -step), Minimum, Maximum);
            e.Handled = true;
        }
    }
}
