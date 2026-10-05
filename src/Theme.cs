using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MonitorPower
{
    public enum ThemeMode { System, Dark, Light }

    // The RemoteAccessHub palette: charcoal surfaces with an olive-green accent, and the same family in light.
    public sealed class Palette
    {
        public readonly bool IsDark;
        public readonly Color Background, Surface, SurfaceAlt, Border, Text, SubText, Muted, Accent, AccentHover, AccentPressed, OnAccent, Success, Warning, Danger, Info, LogBackground, LogText;
        public Palette(bool isDark, string background, string surface, string surfaceAlt, string border, string text, string subText, string muted,
            string accent, string accentHover, string accentPressed, string onAccent, string success, string warning, string danger, string info, string logBackground, string logText)
        {
            IsDark = isDark;
            Background = Theme.Hex(background); Surface = Theme.Hex(surface); SurfaceAlt = Theme.Hex(surfaceAlt); Border = Theme.Hex(border);
            Text = Theme.Hex(text); SubText = Theme.Hex(subText); Muted = Theme.Hex(muted);
            Accent = Theme.Hex(accent); AccentHover = Theme.Hex(accentHover); AccentPressed = Theme.Hex(accentPressed); OnAccent = Theme.Hex(onAccent);
            Success = Theme.Hex(success); Warning = Theme.Hex(warning); Danger = Theme.Hex(danger); Info = Theme.Hex(info);
            LogBackground = Theme.Hex(logBackground); LogText = Theme.Hex(logText);
        }
    }

    public static class Theme
    {
        public static readonly Palette Dark = new Palette(true, "#16181A", "#202326", "#2A2E32", "#3A3F44", "#E9ECEF", "#B3BAC1", "#7E868E",
            "#7CB342", "#8BC34A", "#689F38", "#0E1A04", "#81C784", "#FFB74D", "#EF7B7B", "#64B5F6", "#121416", "#C9CFD5");
        public static readonly Palette Light = new Palette(false, "#F3F4F6", "#FFFFFF", "#EEF0F3", "#D5D9DE", "#1D2125", "#525A63", "#9AA1A9",
            "#3F7D20", "#4A8F27", "#336A19", "#FFFFFF", "#2E7D32", "#9A5B00", "#C62828", "#1565C0", "#FAFBFC", "#2B3137");

        static Palette current;
        static ThemeMode mode = ThemeMode.System;
        public static Palette Current { get { return current ?? Dark; } }
        public static ThemeMode Mode { get { return mode; } }
        public static event Action Changed;

        public static ThemeMode ParseMode(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "dark": return ThemeMode.Dark;
                case "light": return ThemeMode.Light;
                default: return ThemeMode.System;
            }
        }
        public static string ToSetting(ThemeMode value) { return value == ThemeMode.Dark ? "dark" : value == ThemeMode.Light ? "light" : "system"; }
        public static Palette Resolve(ThemeMode value) { return value == ThemeMode.Dark ? Dark : value == ThemeMode.Light ? Light : SystemPrefersLight() ? Light : Dark; }

        public static void Apply(ThemeMode value)
        {
            mode = value;
            Palette next = Resolve(value);
            bool changed = !ReferenceEquals(next, Current);
            current = next;
            if (changed && Changed != null) Changed();
        }

        // Re-reads the Windows app mode when the theme follows the system.
        public static void RefreshFromSystem() { if (mode == ThemeMode.System) Apply(ThemeMode.System); }

        // Windows "app mode" (Settings > Personalisation > Colours). Unreadable means dark.
        public static bool SystemPrefersLight()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return value is int && (int)value == 1;
                }
            }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------ fonts

        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        static string iconFamily;

        public static Font UiFont(float size) { return UiFont(size, FontStyle.Regular); }
        public static Font UiFont(float size, FontStyle style) { return CachedFont("Malgun Gothic", size, style); }

        // Windows 11 "Segoe Fluent Icons", or Windows 10 "Segoe MDL2 Assets"; the glyph codes are shared.
        public static Font IconFont(float size)
        {
            if (iconFamily == null) iconFamily = FindIconFamily();
            return CachedFont(iconFamily, size, FontStyle.Regular);
        }

        static Font CachedFont(string family, float size, FontStyle style)
        {
            string key = family + "|" + size + "|" + style;
            lock (fonts)
            {
                Font font;
                if (!fonts.TryGetValue(key, out font)) { font = new Font(family, size, style); fonts[key] = font; }
                return font;
            }
        }

        static string FindIconFamily()
        {
            try
            {
                using (var installed = new InstalledFontCollection())
                    foreach (string name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
                        foreach (FontFamily family in installed.Families)
                            if (family.Name == name) return name;
            }
            catch (Exception) { }
            return "Segoe UI Symbol";
        }

        // Icon glyphs (Segoe Fluent Icons / MDL2 code points), the same set RemoteAccessHub uses plus a few for monitors.
        public static class Glyph
        {
            public const string Power = "", Moon = "", Sync = "", Refresh = "", Settings = "", More = "";
            public const string ChevronDown = "", Cancel = "", Check = "", Warning = "", Error = "", Info = "";
            public const string Monitor = "", Remote = "", Log = "", Help = "", Copy = "", Delete = "", Exit = "";
        }

        // ------------------------------------------------------------ window

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        // Dark or light title bar to match (Windows 10 20H1 and later). Failure is ignored.
        public static void ApplyTitleBar(Form form)
        {
            if (!form.IsHandleCreated) return;
            try
            {
                int value = Current.IsDark ? 1 : 0;
                if (DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int)) != 0) DwmSetWindowAttribute(form.Handle, 19, ref value, sizeof(int));
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------ colour maths

        public static Color Hex(string hex)
        {
            string h = hex.TrimStart('#');
            return Color.FromArgb(Convert.ToInt32(h.Substring(0, 2), 16), Convert.ToInt32(h.Substring(2, 2), 16), Convert.ToInt32(h.Substring(4, 2), 16));
        }

        public static Color Blend(Color a, Color b, double t)
        {
            return Color.FromArgb((int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        // WCAG 2.x contrast ratio.
        public static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }
        static double Luminance(Color c) { return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B); }
        static double Channel(int v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
    }

    // Context menus (tray, more, auto refresh) in theme colours.
    public sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
    {
        public ThemedMenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Current.Text : Theme.Current.Muted;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) { e.ArrowColor = Theme.Current.SubText; base.OnRenderArrow(e); }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var box = new Rectangle(e.ImageRectangle.X - 2, e.ImageRectangle.Y - 2, e.ImageRectangle.Width + 4, e.ImageRectangle.Height + 4);
            TextRenderer.DrawText(e.Graphics, Theme.Glyph.Check, Theme.IconFont(9f), box, Theme.Current.Accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        sealed class MenuColors : ProfessionalColorTable
        {
            static Palette P { get { return Theme.Current; } }
            public override Color ToolStripDropDownBackground { get { return P.Surface; } }
            public override Color ImageMarginGradientBegin { get { return P.Surface; } }
            public override Color ImageMarginGradientMiddle { get { return P.Surface; } }
            public override Color ImageMarginGradientEnd { get { return P.Surface; } }
            public override Color MenuBorder { get { return P.Border; } }
            public override Color MenuItemBorder { get { return P.Border; } }
            public override Color MenuItemSelected { get { return P.SurfaceAlt; } }
            public override Color MenuItemSelectedGradientBegin { get { return P.SurfaceAlt; } }
            public override Color MenuItemSelectedGradientEnd { get { return P.SurfaceAlt; } }
            public override Color MenuItemPressedGradientBegin { get { return P.Border; } }
            public override Color MenuItemPressedGradientEnd { get { return P.Border; } }
            public override Color SeparatorDark { get { return P.Border; } }
            public override Color SeparatorLight { get { return P.Border; } }
            public override Color CheckBackground { get { return P.SurfaceAlt; } }
            public override Color CheckSelectedBackground { get { return P.SurfaceAlt; } }
            public override Color CheckPressedBackground { get { return P.SurfaceAlt; } }
        }
    }
}
