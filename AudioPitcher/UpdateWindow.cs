using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Brushes             = System.Windows.Media.Brushes;
using Cursors             = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation         = System.Windows.Controls.Orientation;
using Rectangle           = System.Windows.Shapes.Rectangle;

namespace AudioPitcher
{
    // Ventanita de actualización: pregunta, descarga con barra y lanza el instalador.
    internal sealed class UpdateWindow : Window
    {
        private readonly UpdateInfo _info;
        private readonly CancellationTokenSource _cts = new();
        private readonly Border     _fill;
        private readonly Border     _track;
        private readonly TextBlock  _message;
        private readonly TextBlock  _status;
        private readonly StackPanel _buttons;
        private bool _downloading;

        public UpdateWindow(UpdateInfo info)
        {
            _info = info;

            Title                 = "Audio Pitcher";
            Width                 = 300;
            SizeToContent         = SizeToContent.Height;
            WindowStyle           = WindowStyle.None;
            ResizeMode            = ResizeMode.NoResize;
            ShowInTaskbar         = false;
            Topmost               = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            UseLayoutRounding     = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            SetResourceReference(BackgroundProperty, "BrushBg");
            MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
            KeyDown += (_, e) => { if (e.Key == Key.Escape) Cancel(); };

            var logo = new Rectangle
            {
                Width = 17, Height = 18, Margin = new Thickness(0, 0, 8, 0),
                OpacityMask = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/logo.png")))
                    { Stretch = Stretch.Uniform }
            };
            RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
            logo.SetResourceReference(Shape.FillProperty, "BrushBlue");

            var title = Text("Audio Pitcher", 12, "BrushText", bold: true);
            var ver   = Text(info.Version.ToString(3), 10, "BrushMuted");
            ver.Margin = new Thickness(6, 1, 0, 0);
            var header = new StackPanel { Orientation = Orientation.Horizontal, Children = { logo, title, ver },
                                          Margin = new Thickness(0, 0, 0, 12) };

            _message = Text("Hay una nueva versión disponible.", 11, "BrushSubtext");

            _fill = new Border { Width = 0, CornerRadius = new CornerRadius(3),
                                 HorizontalAlignment = HorizontalAlignment.Left };
            _fill.SetResourceReference(Border.BackgroundProperty, "BrushGreen");
            _track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Child = _fill,
                                  Visibility = Visibility.Collapsed };
            _track.SetResourceReference(Border.BackgroundProperty, "BrushSurface");

            _status = Text("", 10, "BrushSubtext");

            _buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            ShowAskButtons();

            var bottom = new Grid { Margin = new Thickness(0, 14, 0, 0), Children = { _status, _buttons } };

            var frame = new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 16, 18, 16),
                Child = new StackPanel { Children = { header, _message, _track, bottom } }
            };
            frame.SetResourceReference(Border.BorderBrushProperty, "BrushBorder");
            Content = frame;
        }

        private static TextBlock Text(string s, double size, string brushKey, bool bold = false)
        {
            var t = new TextBlock { Text = s, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                                    VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
            return t;
        }

        private static Border MakeButton(string label, bool primary, Action onClick)
        {
            var text = Text(label, 10, primary ? "BrushText" : "BrushMuted", bold: primary);
            var b = new Border
            {
                Padding = new Thickness(12, 5, 12, 5), CornerRadius = new CornerRadius(5),
                BorderThickness = new Thickness(primary ? 1.5 : 1), Cursor = Cursors.Hand,
                Margin = new Thickness(6, 0, 0, 0), Child = text, Background = Brushes.Transparent
            };
            string normal = primary ? "BrushGreen" : "BrushBorder";
            b.SetResourceReference(Border.BorderBrushProperty, normal);
            b.MouseEnter += (_, _) => b.SetResourceReference(Border.BorderBrushProperty, primary ? "BrushGreen" : "BrushBorderHov");
            b.MouseEnter += (_, _) => { if (primary) b.SetResourceReference(Border.BackgroundProperty, "BrushSurface"); };
            b.MouseLeave += (_, _) => { b.SetResourceReference(Border.BorderBrushProperty, normal); b.Background = Brushes.Transparent; };
            b.MouseLeftButtonDown += (_, e) => { e.Handled = true; onClick(); };
            return b;
        }

        private void ShowAskButtons()
        {
            _buttons.Children.Clear();
            _buttons.Children.Add(MakeButton("Ahora no", false, () => DialogResult = false));
            _buttons.Children.Add(MakeButton("Actualizar", true, async () => await DownloadAsync()));
        }

        private void SetProgress(int percent)
        {
            double target = _track.ActualWidth * Math.Clamp(percent, 0, 100) / 100.0;
            _fill.BeginAnimation(WidthProperty,
                new DoubleAnimation(target, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase() });
            _status.Text = $"{percent}%";
        }

        private async Task DownloadAsync()
        {
            if (_downloading) return;
            _downloading = true;
            _message.Visibility = Visibility.Collapsed;
            _track.Visibility   = Visibility.Visible;
            _status.Text        = "0%";
            _buttons.Children.Clear();
            _buttons.Children.Add(MakeButton("Cancelar", false, Cancel));

            bool ok;
            try
            {
                ok = await UpdateService.DownloadAndRunAsync(_info, new Progress<int>(SetProgress), _cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!ok)
            {
                _status.Text = "No se pudo descargar.";
                _status.SetResourceReference(TextBlock.ForegroundProperty, "BrushRed");
                _buttons.Children.Clear();
                _buttons.Children.Add(MakeButton("Cerrar", false, () => DialogResult = false));
                return;
            }

            SetProgress(100);
            _buttons.Children.Clear();
            await Task.Delay(500);
            DialogResult = true;
        }

        private void Cancel()
        {
            if (_downloading && _buttons.Children.Count == 0) return; // ya instalando
            _cts.Cancel();
            DialogResult = false;
        }
    }
}
