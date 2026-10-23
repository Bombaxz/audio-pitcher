using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Brushes = System.Windows.Media.Brushes;

namespace AudioPitcher;

/// Sombra interior como la de Figma (INNER_SHADOW): un aro negro por fuera del borde proyecta su
/// sombra hacia adentro, desplazada OffsetY y desenfocada Blur; el recorte redondeado oculta el aro.
/// Va como primera capa dentro del control (debajo del contenido), sin recibir clicks.
internal sealed class InnerShadow : Grid
{
    private const double Ring = 16;   // grosor del aro (mayor que desenfoque + desplazamiento)
    private const double Gap = 1.5;   // el aro queda apenas afuera del recorte: sin píxeles negros en el borde
    private readonly Border _ring = new() { BorderBrush = Brushes.Black, BorderThickness = new Thickness(Ring), Margin = new Thickness(-(Ring + Gap)) };
    private readonly DropShadowEffect _shadow = new() { Color = Colors.Black, Direction = 270 };

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(InnerShadow),
        new PropertyMetadata(8.0, (d, _) => ((InnerShadow)d).Update()));
    public static readonly DependencyProperty OffsetYProperty = DependencyProperty.Register(nameof(OffsetY), typeof(double), typeof(InnerShadow),
        new PropertyMetadata(4.0, (d, _) => ((InnerShadow)d).Update()));
    public static readonly DependencyProperty BlurProperty = DependencyProperty.Register(nameof(Blur), typeof(double), typeof(InnerShadow),
        new PropertyMetadata(4.0, (d, _) => ((InnerShadow)d).Update()));
    public static readonly DependencyProperty ShadowOpacityProperty = DependencyProperty.Register(nameof(ShadowOpacity), typeof(double), typeof(InnerShadow),
        new PropertyMetadata(0.15, (d, _) => ((InnerShadow)d).Update()));

    public double CornerRadius  { get => (double)GetValue(CornerRadiusProperty);  set => SetValue(CornerRadiusProperty, value); }
    public double OffsetY       { get => (double)GetValue(OffsetYProperty);       set => SetValue(OffsetYProperty, value); }
    public double Blur          { get => (double)GetValue(BlurProperty);          set => SetValue(BlurProperty, value); }
    public double ShadowOpacity { get => (double)GetValue(ShadowOpacityProperty); set => SetValue(ShadowOpacityProperty, value); }
    public InnerShadow()
    {
        IsHitTestVisible = false;
        _ring.Effect = _shadow;
        Children.Add(_ring);
        SizeChanged += (_, _) => Update();
        Update();
    }

    private void Update()
    {
        // WPF redondea el borde interior como (radio - grosor/2): así el hueco coincide con el control
        _ring.CornerRadius = new System.Windows.CornerRadius(CornerRadius + Gap + Ring / 2);
        _shadow.BlurRadius = Blur;
        _shadow.ShadowDepth = OffsetY;
        _shadow.Opacity = ShadowOpacity;
        Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), CornerRadius, CornerRadius);
    }
}
