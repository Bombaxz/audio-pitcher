using System;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;

namespace AudioPitcher
{
    internal static class ThemeService
    {
        public static event EventHandler? ThemeChanged;

        // Backgrounds — preset-controlled, hue only in Hue mode (3)
        private static readonly (string Key, byte R, byte G, byte B)[] _bgPalette =
        {
            ("BrushBg",       0x1A, 0x1B, 0x2E),
            ("BrushSurface",  0x2A, 0x2A, 0x3D),
            ("BrushSurface2", 0x24, 0x24, 0x38),
            ("BrushOverlay",  0x2E, 0x2E, 0x45),
        };

        // Borders/strokes — always hue-shifted
        private static readonly (string Key, byte R, byte G, byte B)[] _borderPalette =
        {
            ("BrushBorder",    0x3D, 0x3D, 0x56),
            ("BrushBorderHov", 0x58, 0x5B, 0x70),
            ("BrushSelected",  0x31, 0x32, 0x44),
            ("BrushSelItem",   0x31, 0x3D, 0x7A),
            ("BrushHover",     0x45, 0x47, 0x5A),
            ("BrushMuted2",    0x4A, 0x4A, 0x6A),
        };

        // Accents — always hue-shifted
        private static readonly (string Key, byte R, byte G, byte B)[] _accentPalette =
        {
            ("BrushBlue",   0x89, 0xB4, 0xFA),
            ("BrushGreen",  0xA6, 0xE3, 0xA1),
            ("BrushYellow", 0xF9, 0xE2, 0xAF),
            ("BrushMauve",  0xCB, 0xA6, 0xF7),
            ("BrushRed",    0xF3, 0x8B, 0xA8),
        };

        // Text base values
        private static readonly (string Key, byte R, byte G, byte B)[] _textPalette =
        {
            ("BrushMuted",   0x6C, 0x70, 0x86),
            ("BrushSubtext", 0xA6, 0xAD, 0xC8),
            ("BrushText",    0xCD, 0xD6, 0xF4),
        };

        // bg presets: 0=Dark  1=Black  2=Deep  3=Hue  4=White
        // text modes: 0=Blancas  1=Negras  2=Hue
        private static float _hueShift      = 0f;
        private static int   _bgPreset      = 0;
        private static int   _textColorMode  = 0;
        private static int   _textBrightness = 100; // 0–100
        private static bool  _glass;                // modo imagen: controles de vidrio

        // Custom colors per preset — store BASE color (pre-hue); hue applied on read
        private static readonly System.Collections.Generic.Dictionary<int,
            System.Collections.Generic.Dictionary<string, WpfColor>> _customColors = new();

        public static float HueShift       => _hueShift;
        public static int   BgPreset       => _bgPreset;
        public static int   TextColorMode  => _textColorMode;
        public static int   TextBrightness => _textBrightness;
        public static bool  Glass          => _glass;

        public static void ApplyHueShift(float degrees)    { _hueShift = degrees;      UpdateResources(); }
        public static void ApplyBgPreset(int preset)       { _bgPreset = preset;       UpdateResources(); }
        public static void ApplyTextColorMode(int mode)    { _textColorMode = mode is 1 or 2 ? mode : 0; UpdateResources(); }   // 0 normal, 1 negro, 2 color
        public static void ApplyTextBrightness(int value)  { _textBrightness = value;  UpdateResources(); }
        public static void ApplyImageMode(bool on, int preset) { _glass = on; _bgPreset = preset; UpdateResources(); }

        public static void SetCustomColor(string key, WpfColor baseColor)
        {
            if (!_customColors.ContainsKey(_bgPreset))
                _customColors[_bgPreset] = new();
            _customColors[_bgPreset][key] = baseColor;
            UpdateResources(); // also fires ThemeChanged
        }

        public static void ResetCustomColors()
        {
            if (_customColors.ContainsKey(_bgPreset))
                _customColors[_bgPreset].Clear();
            UpdateResources();
        }

        public static WpfColor? GetCustomColorBase(string key) =>
            _customColors.TryGetValue(_bgPreset, out var d) && d.TryGetValue(key, out var c) ? c : null;

        public static WpfColor? GetCustomColor(string key)
        {
            if (_customColors.TryGetValue(_bgPreset, out var d) && d.TryGetValue(key, out var c))
                return ShiftHue(c, _hueShift);
            return null;
        }

        public static System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> GetCustomColorsForSave()
        {
            var result = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>();
            foreach (var (preset, dict) in _customColors)
            {
                var inner = new System.Collections.Generic.Dictionary<string, string>();
                foreach (var (key2, color) in dict)
                    inner[key2] = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                result[preset.ToString()] = inner;
            }
            return result;
        }

        public static void RestoreCustomColors(System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>> saved)
        {
            _customColors.Clear();
            foreach (var (presetStr, dict) in saved)
            {
                if (!int.TryParse(presetStr, out int preset)) continue;
                _customColors[preset] = new();
                foreach (var (key2, hex) in dict)
                {
                    try
                    {
                        var c = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                        _customColors[preset][key2] = c;
                    }
                    catch { }
                }
            }
        }

        private static void UpdateResources()
        {
            var res = System.Windows.Application.Current.Resources;

            // ── Backgrounds ──────────────────────────────────────────────────
            switch (_bgPreset)
            {
                case 1: // Black
                    Set(res, "BrushBg",       0x00, 0x00, 0x00);
                    Set(res, "BrushSurface",  0x10, 0x10, 0x10);
                    Set(res, "BrushSurface2", 0x0C, 0x0C, 0x0C);
                    Set(res, "BrushOverlay",  0x18, 0x18, 0x18);
                    break;
                case 2: // Deep
                    Set(res, "BrushBg",       0x0D, 0x0D, 0x18);
                    Set(res, "BrushSurface",  0x18, 0x18, 0x28);
                    Set(res, "BrushSurface2", 0x14, 0x14, 0x22);
                    Set(res, "BrushOverlay",  0x1E, 0x1E, 0x2E);
                    break;
                case 3: // Hue — shift bg with hue
                    foreach (var (key, r, g, b) in _bgPalette)
                        SetShifted(res, key, r, g, b, _hueShift);
                    break;
                case 4: // White (diseño): ventana blanca, desplegables y superficies gris claro
                    Set(res, "BrushBg",       0xFF, 0xFF, 0xFF);
                    Set(res, "BrushSurface",  0xF4, 0xF4, 0xF6);
                    Set(res, "BrushSurface2", 0xF9, 0xF9, 0xFB);
                    Set(res, "BrushOverlay",  0xE8, 0xE8, 0xEC);
                    break;
                default: // Dark (0)
                    foreach (var (key, r, g, b) in _bgPalette)
                        Set(res, key, r, g, b);
                    break;
            }

            res["ShadowDropdownOpacity"] = _bgPreset == 4 ? 0.05 : 0.15;   // sombra interior de los desplegables

            // ── Borders/strokes — always hue ─────────────────────────────────
            if (_bgPreset == 4) // White — light neutral borders
            {
                Set(res, "BrushBorder",    0xD8, 0xD8, 0xDE);
                Set(res, "BrushBorderHov", 0xBC, 0xBC, 0xC6);
                Set(res, "BrushSelected",  0xEC, 0xEC, 0xF0);
                Set(res, "BrushSelItem",   0xD2, 0xE2, 0xFB);
                Set(res, "BrushHover",     0xEE, 0xEE, 0xF2);
            }
            else
            {
                foreach (var (key, r, g, b) in _borderPalette)
                    SetShifted(res, key, r, g, b, _hueShift);
            }

            // ── Accents — always hue ──────────────────────────────────────────
            if (_bgPreset == 4) // White — Catppuccin Latte (saturated for light bg)
            {
                SetShifted(res, "BrushBlue",   0x1E, 0x66, 0xF5, _hueShift);
                SetShifted(res, "BrushGreen",  0x40, 0xA0, 0x2B, _hueShift);
                SetShifted(res, "BrushYellow", 0xDF, 0x8E, 0x1D, _hueShift);
                SetShifted(res, "BrushMauve",  0x88, 0x39, 0xEF, _hueShift);
                SetShifted(res, "BrushRed",    0xD2, 0x0F, 0x39, _hueShift);
                SetShifted(res, "BrushAccentStrong", 0x1E, 0x66, 0xF5, _hueShift);
            }
            else
            {
                foreach (var (key, r, g, b) in _accentPalette)
                    SetShifted(res, key, r, g, b, _hueShift);
                SetShifted(res, "BrushAccentStrong", 0x63, 0x9E, 0xFE, _hueShift);   // pastillas elegidas (diseño)
            }

            // ── Text colors (all 4 brushes) ───────────────────────────────────
            // Contraste: 0 = suave, 100 = máximo, en cualquier tema.
            // Normal = gris neutro; Color = teñido con el hue (base azul-lavanda).
            float t = _textBrightness / 100f;
            bool light = _bgPreset == 4;
            bool tint  = _textColorMode == 2;
            float hue  = ((230f + _hueShift) / 360f % 1f + 1f) % 1f;

            void SetText(string key, float lSoft, float lStrong, float sat)
            {
                float l = lSoft + (lStrong - lSoft) * t;
                HslToRgb(hue, tint ? sat : 0f, l, out byte r, out byte g, out byte b);
                res[key] = Frozen(WpfColor.FromRgb(r, g, b));
            }

            // Color: el máximo se queda en un L donde el tinte todavía se ve.
            if (_textColorMode == 1)   // Negro: letras negras en cualquier tema
            {
                SetText("BrushText",    0.16f, 0.00f, 0f);
                SetText("BrushSubtext", 0.24f, 0.06f, 0f);
                SetText("BrushMuted",   0.32f, 0.14f, 0f);
                SetText("BrushMuted2",  0.36f, 0.18f, 0f);
            }
            else if (light && tint)
            {
                SetText("BrushText",    0.42f, 0.24f, 0.75f);
                SetText("BrushSubtext", 0.48f, 0.30f, 0.65f);
                SetText("BrushMuted",   0.58f, 0.38f, 0.50f);
                SetText("BrushMuted2",  0.64f, 0.44f, 0.45f);
            }
            else if (light)
            {
                SetText("BrushText",    0.30f, 0.00f, 0f);
                SetText("BrushSubtext", 0.42f, 0.00f, 0f);
                SetText("BrushMuted",   0.56f, 0.12f, 0f);
                SetText("BrushMuted2",  0.62f, 0.16f, 0f);
            }
            else if (tint)
            {
                SetText("BrushText",    0.66f, 0.82f, 0.95f);
                SetText("BrushSubtext", 0.60f, 0.78f, 0.80f);
                SetText("BrushMuted",   0.48f, 0.70f, 0.60f);
                SetText("BrushMuted2",  0.40f, 0.64f, 0.50f);
            }
            else
            {
                SetText("BrushText",    0.76f, 1.00f, 0f);
                SetText("BrushSubtext", 0.62f, 1.00f, 0f);
                SetText("BrushMuted",   0.46f, 0.90f, 0f);
                SetText("BrushMuted2",  0.38f, 0.86f, 0f);
            }

            // Hint: textos chicos de apoyo (escalas, aclaraciones) — siempre algo teñidos y atenuados
            {
                float hl = light ? 0.52f - 0.14f * t : 0.42f + 0.16f * t;
                HslToRgb(hue, light ? 0.18f : 0.22f, hl, out byte hr, out byte hg, out byte hb);
                res["BrushHint"] = Frozen(WpfColor.FromRgb(hr, hg, hb));
            }

            // ── Pitch/Formant fill — isolated resources, don't touch BrushBlue/BrushMauve ─
            var sliderCustom  = GetCustomColor("sliderAccent");
            res["BrushPitchFill"]   = sliderCustom.HasValue
                ? Frozen(sliderCustom.Value)
                : res["BrushBlue"]  as SolidColorBrush ?? Frozen(WpfColor.FromRgb(0x89, 0xB4, 0xFA));

            var formantCustom = GetCustomColor("formantAccent");
            res["BrushFormantFill"] = formantCustom.HasValue
                ? Frozen(formantCustom.Value)
                : res["BrushMauve"] as SolidColorBrush ?? Frozen(WpfColor.FromRgb(0xCB, 0xA6, 0xF7));

            // ── Modo imagen (diseño): controles de vidrio = su color al 20 % + un filo de luz ──
            var bgColor = ((SolidColorBrush)res["BrushBg"]).Color;
            var veil    = light ? WpfColor.FromRgb(0xF4, 0xF4, 0xF6) : bgColor;
            res["BrushImageVeil"] = Frozen(veil);
            res["BrushCtl"]       = Glassy("BrushSurface");
            res["BrushCtl2"]      = Glassy("BrushSurface2");
            res["BrushCtlHover"]  = Glassy("BrushSelected", 0.35);
            res["BrushTrack"]     = Glassy("BrushOverlay");
            res["BrushTrackMain"] = _glass ? res["BrushTrack"] : res["BrushOverlay"];
            res["BrushRowHover"]  = Glassy("BrushHover", light ? 0.45 : 0.3);
            res["BrushWave"]      = _glass ? Frozen(WithAlpha(veil, 0.2)) : Frozen(WpfColor.FromRgb(0x1A, 0x1B, 0x2E));
            res["BrushGlassRim"]  = _glass ? GlassRim(light) : Frozen(Colors.Transparent);
            res["BrushCtlBorder"] = _glass ? Frozen(Colors.Transparent) : res["BrushBorder"];
            if (_glass) res["ShadowDropdownOpacity"] = 0.0;
            res["ThumbShadowOpacity"] = _glass ? 0.12 : 0.25;   // perillas de las barras: más sutil sobre la imagen

            SolidColorBrush Glassy(string key, double alpha = 0.2)
            {
                var c = ((SolidColorBrush)res[key]).Color;
                return _glass ? Frozen(WithAlpha(c, alpha)) : Frozen(c);
            }

            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static void Set(System.Windows.ResourceDictionary res, string key, byte r, byte g, byte b)
            => res[key] = Frozen(WpfColor.FromRgb(r, g, b));

        private static void SetShifted(System.Windows.ResourceDictionary res, string key,
                                        byte r, byte g, byte b, float deg)
            => res[key] = Frozen(ShiftHue(WpfColor.FromRgb(r, g, b), deg));

        private static WpfColor WithAlpha(WpfColor c, double a) => WpfColor.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B);

        // Filo de luz del vidrio (como el GLASS de Figma, luz a −45°): fuerte arriba, sobre todo a la
        // izquierda; más suave abajo; casi nada a los costados
        private static LinearGradientBrush GlassRim(bool light)
        {
            double k = light ? 0.95 : 0.55;
            var b = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0.25, 1) };
            b.GradientStops.Add(new GradientStop(WithAlpha(Colors.White, k), 0));
            b.GradientStops.Add(new GradientStop(WithAlpha(Colors.White, k * 0.2), 0.35));
            b.GradientStops.Add(new GradientStop(WithAlpha(Colors.White, k * 0.15), 0.65));
            b.GradientStops.Add(new GradientStop(WithAlpha(Colors.White, k * 0.6), 1));
            b.Freeze();
            return b;
        }

        private static SolidColorBrush Frozen(WpfColor c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private static WpfColor Lerp(WpfColor a, WpfColor b, float t) =>
            WpfColor.FromRgb(
                (byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));

        public static WpfColor ApplyThemeKnobColor(WpfColor c)
        {
            if (_bgPreset != 4) return c;
            RgbToHsl(c.R, c.G, c.B, out float h, out float s, out float l);
            float newL = Math.Clamp(l * 0.50f, 0.15f, 0.46f);
            float newS = Math.Min(s * 1.15f, 1f);
            HslToRgb(h, newS, newL, out byte r, out byte g, out byte b);
            return WpfColor.FromRgb(r, g, b);
        }

        public static WpfColor ShiftHue(WpfColor c, float degrees)
        {
            if (Math.Abs(degrees) < 0.5f) return c;
            RgbToHsl(c.R, c.G, c.B, out float h, out float s, out float l);
            h = ((h + degrees / 360f) % 1f + 1f) % 1f;
            HslToRgb(h, s, l, out byte nr, out byte ng, out byte nb);
            return WpfColor.FromRgb(nr, ng, nb);
        }

        public static void HslToColor(float h, float s, float l, out WpfColor color)
        {
            HslToRgb(h, s, l, out byte r, out byte g, out byte b);
            color = WpfColor.FromRgb(r, g, b);
        }

        private static void RgbToHsl(byte rb, byte gb, byte bb,
            out float h, out float s, out float l)
        {
            float r = rb / 255f, g = gb / 255f, b = bb / 255f;
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2f;
            if (max == min) { h = 0; s = 0; return; }
            float d = max - min;
            s = l > 0.5f ? d / (2f - max - min) : d / (max + min);
            if      (max == r) h = (g - b) / d + (g < b ? 6f : 0f);
            else if (max == g) h = (b - r) / d + 2f;
            else               h = (r - g) / d + 4f;
            h /= 6f;
        }

        private static void HslToRgb(float h, float s, float l,
            out byte r, out byte g, out byte b)
        {
            float fr, fg, fb;
            if (s == 0) { fr = fg = fb = l; }
            else
            {
                float q = l < 0.5f ? l * (1f + s) : l + s - l * s;
                float p = 2f * l - q;
                fr = Hue2Rgb(p, q, h + 1f / 3f);
                fg = Hue2Rgb(p, q, h);
                fb = Hue2Rgb(p, q, h - 1f / 3f);
            }
            r = (byte)(fr * 255);
            g = (byte)(fg * 255);
            b = (byte)(fb * 255);
        }

        private static float Hue2Rgb(float p, float q, float t)
        {
            t = ((t % 1f) + 1f) % 1f;
            if (t < 1f / 6f) return p + (q - p) * 6f * t;
            if (t < 0.5f)    return q;
            if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6f;
            return p;
        }
    }
}
