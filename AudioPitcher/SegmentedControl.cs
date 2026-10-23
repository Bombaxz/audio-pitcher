using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using Panel = System.Windows.Controls.Panel;

namespace AudioPitcher;

/// Selector de opciones con una pastilla que se desliza hasta la opción elegida (diseño de Figma).
/// Cada opción define su texto y el relleno de la pastilla cuando está elegida.
/// Si la ventana es angosta, las opciones se achican y el texto se recorta con "…" (no se encima).
internal sealed class SegmentedControl : Border
{
    public sealed class Option
    {
        public required Func<string> Label { get; init; }
        /// Recurso del tema (ej. "BrushAccentStrong") o un pincel fijo para la pastilla elegida.
        public string? PillResource { get; init; }
        public Func<Brush>? PillBrush { get; init; }
        /// Texto de la opción elegida: recurso del tema o color fijo (por defecto, blanco).
        public string? SelectedTextResource { get; init; }
        public Color SelectedText { get; init; } = Colors.White;
        public bool Enabled { get; init; } = true;
    }

    private const double Pad = 2, OptionPad = 14, Duration = 180;
    private readonly List<Option> _options;
    private readonly bool _stretch;
    private readonly Grid _grid = new();
    private readonly Border _pill = new() { CornerRadius = new CornerRadius(6), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly List<(Border cell, TextBlock text)> _cells = new();
    private double _cellWidth;   // ancho natural de cada opción (sin estirar)
    private int _selected = -1;

    public event Action<int>? SelectionChanged;

    public SegmentedControl(IEnumerable<Option> options, bool stretch)
    {
        _options = options.ToList();
        _stretch = stretch;
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(Pad);
        HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
        SetResourceReference(BackgroundProperty, "BrushCtl2");

        // Canto de vidrio (solo con imagen de fondo): encima del fondo, cubriendo también el relleno
        var glass = new GlassEdge { CornerRadius = new CornerRadius(8), Margin = new Thickness(-Pad) };
        _grid.Children.Add(glass);

        _pill.Effect = new System.Windows.Media.Effects.DropShadowEffect
            { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Direction = 270, Opacity = 0.3 };
        _pill.Opacity = 0;
        _grid.Children.Add(new Canvas { IsHitTestVisible = false, Children = { _pill } });
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var uniform = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1 };
        Panel host = stretch ? uniform : row;

        for (int i = 0; i < _options.Count; i++)
        {
            var o = _options[i];
            var text = new TextBlock { FontSize = 10, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                                       TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(stretch ? 1 : 4, 0, stretch ? 1 : 4, 0) };
            var cell = new Border { Height = 24, Background = Brushes.Transparent, Child = text,
                                    Cursor = o.Enabled ? Cursors.Hand : Cursors.Arrow, Opacity = o.Enabled ? 1 : 0.4 };
            int idx = i;
            cell.MouseLeftButtonDown += (_, e) => { if (_options[idx].Enabled) { Select(idx, animate: true); SelectionChanged?.Invoke(idx); } e.Handled = true; };
            host.Children.Add(cell);
            _cells.Add((cell, text));
        }
        _grid.Children.Add(host);
        Child = _grid;
        RefreshTexts();
        SizeChanged += (_, _) => PlacePill(animate: false);
    }

    public int SelectedIndex => _selected;

    /// Textos y anchos (al cambiar de idioma). Sin estirar: todas las opciones del ancho de la más larga.
    public void RefreshTexts()
    {
        double max = 0;
        for (int i = 0; i < _cells.Count; i++)
        {
            var text = _cells[i].text;
            text.Text = _options[i].Label();
            text.FontWeight = FontWeights.Bold;   // medir con la versión más ancha
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            max = Math.Max(max, text.DesiredSize.Width);
        }
        _cellWidth = Math.Ceiling(max + OptionPad * 2);
        InvalidateMeasure();
        UpdateStyles();
        Dispatcher.BeginInvoke(new Action(() => PlacePill(animate: false)), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // Sin estirar: si no entran a su ancho natural, se reparten el lugar que hay
    protected override Size MeasureOverride(Size constraint)
    {
        if (!_stretch && _cells.Count > 0)
        {
            double w = _cellWidth;
            if (!double.IsInfinity(constraint.Width))
                w = Math.Min(w, Math.Floor((constraint.Width - Pad * 2) / _cells.Count));
            foreach (var (cell, _) in _cells)
                if (cell.Width != w) cell.Width = Math.Max(w, 0);
        }
        return base.MeasureOverride(constraint);
    }

    /// Elige una opción (-1 = ninguna, p. ej. Fondo en modo imagen).
    public void Select(int index, bool animate = false)
    {
        if (index == _selected && _pill.Opacity > 0) return;
        _selected = index;
        UpdateStyles();
        PlacePill(animate && IsLoaded);
    }

    private void UpdateStyles()
    {
        for (int i = 0; i < _cells.Count; i++)
        {
            var text = _cells[i].text;
            var o = _options[i];
            bool sel = i == _selected;
            text.FontWeight = sel ? FontWeights.Bold : FontWeights.Normal;
            if (sel)
            {
                if (o.SelectedTextResource != null) text.SetResourceReference(TextBlock.ForegroundProperty, o.SelectedTextResource);
                else text.Foreground = new SolidColorBrush(o.SelectedText);
                text.Opacity = 1;
            }
            else
            {
                text.SetResourceReference(TextBlock.ForegroundProperty, "BrushSubtext");
                text.Opacity = 0.7;
            }
        }
        if (_selected >= 0)
        {
            var o = _options[_selected];
            if (o.PillResource != null) _pill.SetResourceReference(Border.BackgroundProperty, o.PillResource);
            else _pill.Background = o.PillBrush?.Invoke() ?? Brushes.Transparent;
        }
    }

    // La pastilla se desliza (posición y ancho) hasta la opción elegida
    private void PlacePill(bool animate)
    {
        if (_selected < 0 || _selected >= _cells.Count)
        {
            _pill.BeginAnimation(OpacityProperty, animate ? new DoubleAnimation(0, TimeSpan.FromMilliseconds(Duration)) : null);
            if (!animate) _pill.Opacity = 0;
            return;
        }
        var cell = _cells[_selected].cell;
        if (cell.ActualWidth <= 0) return;
        double x = cell.TranslatePoint(new Point(0, 0), _grid).X;
        _pill.Height = cell.ActualHeight;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur = TimeSpan.FromMilliseconds(Duration);
        if (animate && _pill.Opacity > 0)
        {
            _pill.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(x, dur) { EasingFunction = ease });
            _pill.BeginAnimation(WidthProperty, new DoubleAnimation(cell.ActualWidth, dur) { EasingFunction = ease });
        }
        else
        {
            _pill.BeginAnimation(Canvas.LeftProperty, null); _pill.BeginAnimation(WidthProperty, null);
            Canvas.SetLeft(_pill, x); _pill.Width = cell.ActualWidth;
        }
        _pill.BeginAnimation(OpacityProperty, animate ? new DoubleAnimation(1, dur) : null);
        if (!animate) _pill.Opacity = 1;
    }

    /// Para recolorear las pastillas fijas cuando cambia el tema (Hue, etc.)
    public void RefreshColors() => UpdateStyles();
}
