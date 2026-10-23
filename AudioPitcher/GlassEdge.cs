using System;
using System.Windows;
using System.Windows.Controls;

namespace AudioPitcher;

/// Canto de vidrio (modo imagen, diseño Figma GLASS): un filo de luz fino, fuerte arriba (luz a −45°),
/// más suave abajo y apenas a los costados. Sin brillos difusos hacia adentro (se ven como un bulto).
/// Va encima del fondo del control, sin recibir clicks; sin imagen de fondo no se ve.
internal sealed class GlassEdge : Border
{
    public GlassEdge()
    {
        IsHitTestVisible = false;
        BorderThickness = new Thickness(1);
        SetResourceReference(BorderBrushProperty, "BrushGlassRim");
        Loaded   += (_, _) => { ThemeService.ThemeChanged += OnTheme; Update(); };
        Unloaded += (_, _) => ThemeService.ThemeChanged -= OnTheme;
        Update();
    }

    private void OnTheme(object? sender, EventArgs e) => Update();

    private void Update() => Visibility = ThemeService.Glass ? Visibility.Visible : Visibility.Collapsed;
}
