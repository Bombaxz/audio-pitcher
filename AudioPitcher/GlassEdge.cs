using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace AudioPitcher;

/// Vidrio del modo imagen, copiado del efecto GLASS de Figma (luz a −45°, medido en sus renders):
///  · una banda oscura suave bajo el borde de arriba y el de la izquierda (la refracción del canto);
///  · una luz interior muy leve que aclara hacia abajo a la derecha;
///  · un filo de 1 px: fuerte arriba y abajo, tenue a los costados, con las esquinas
///    arriba-izquierda y abajo-derecha más brillantes.
/// Solo degradados (sin efectos ni desenfoques). Va encima del fondo del control, sin recibir
/// clicks; sin imagen de fondo no se ve.
internal sealed class GlassEdge : Grid
{
    private readonly Rectangle _bandTop  = new() { VerticalAlignment = VerticalAlignment.Top };
    private readonly Rectangle _bandLeft = new() { HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
    private readonly Rectangle _light    = new();
    private readonly Border _rimH = new() { BorderThickness = new Thickness(0, 1, 0, 1) };
    private readonly Border _rimV = new() { BorderThickness = new Thickness(1, 0, 1, 0) };

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(GlassEdge),
        new PropertyMetadata(new CornerRadius(8), (d, _) => ((GlassEdge)d).Update()));

    public CornerRadius CornerRadius { get => (CornerRadius)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }

    public GlassEdge()
    {
        IsHitTestVisible = false;
        Children.Add(_bandTop);
        Children.Add(_bandLeft);
        Children.Add(_light);
        Children.Add(_rimH);
        Children.Add(_rimV);
        Loaded   += (_, _) => { ThemeService.ThemeChanged += OnTheme; Update(); };
        Unloaded += (_, _) => ThemeService.ThemeChanged -= OnTheme;
        SizeChanged += (_, _) => Update();
        Update();
    }

    private void OnTheme(object? sender, EventArgs e) => Update();

    private static Color White(double a) => Color.FromArgb((byte)Math.Round(a * 255), 255, 255, 255);
    private static Color Black(double a) => Color.FromArgb((byte)Math.Round(a * 255), 0, 0, 0);

    private static LinearGradientBrush Gradient(Point from, Point to, params (double pos, Color c)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = from, EndPoint = to };
        foreach (var (pos, c) in stops) b.GradientStops.Add(new GradientStop(c, pos));
        b.Freeze();
        return b;
    }

    private void Update()
    {
        if (!ThemeService.Glass) { Visibility = Visibility.Collapsed; return; }
        Visibility = Visibility.Visible;
        bool light = ThemeService.BgPreset == 4;
        double r = CornerRadius.TopLeft;
        _rimH.CornerRadius = _rimV.CornerRadius = CornerRadius;
        Clip = ActualWidth > 0 ? new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), r, r) : null;

        // Valores medidos en los renders de Figma (fondo violeta / fondo claro)
        // Banda de refracción: ~10 px (la mitad en controles bajos, como las barras)
        double band = Math.Min(10, Math.Max(2, ActualHeight * 0.5));
        double shade = light ? 0.035 : 0.12;
        _bandTop.Height = band;
        _bandTop.Fill   = Gradient(new Point(0, 0), new Point(0, 1), (0, Black(shade)), (1, Black(0)));
        _bandLeft.Width = band;
        _bandLeft.Fill  = Gradient(new Point(0, 0), new Point(1, 0), (0, Black(shade * 0.7)), (1, Black(0)));

        // Luz interior hacia abajo a la derecha (solo se nota sobre fondo claro)
        double glow = light ? 0.07 : 0.0;
        _light.Fill = Gradient(new Point(0, 0), new Point(1, 1), (0.35, White(0)), (1, White(glow)));

        // Filo: oscuro → arriba 30 %, abajo 18 %, costados 15–24 % (más en las esquinas TL y BR);
        //       claro → ~90 % parejo
        double k = light ? 0.9 : 0.3;
        double bottom = light ? 0.95 : 0.6, sideA = light ? 0.95 : 0.8, sideMid = light ? 0.9 : 0.5, sideB = light ? 0.95 : 0.55;
        _rimH.BorderBrush = Gradient(new Point(0, 0), new Point(0, 1), (0, White(k)), (1, White(k * bottom)));
        _rimV.BorderBrush = Gradient(new Point(0, 0), new Point(1, 1), (0, White(k * sideA)), (0.5, White(k * sideMid)), (1, White(k * sideB)));

        // Una barrita (divisor de 2 px): solo la línea de luz de arriba, sin bandas
        bool thin = ActualHeight > 0 && ActualHeight <= 3;
        _rimH.BorderThickness = thin ? new Thickness(0, 1, 0, 0) : new Thickness(0, 1, 0, 1);
        _bandTop.Visibility = _bandLeft.Visibility = _light.Visibility = _rimV.Visibility = thin ? Visibility.Collapsed : Visibility.Visible;
    }
}
