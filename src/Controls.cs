using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MonitorPower
{
    static class Draw
    {
        public static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Max(0.1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Smooth(Graphics g) { g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality; }

        public static Size MeasureText(string text, Font font) { return TextRenderer.MeasureText(text ?? "", font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine); }

        // The nearest opaque background behind a control, used to fill rounded corners.
        public static Color ParentBack(Control c)
        {
            for (Control p = c.Parent; p != null; p = p.Parent) if (p.BackColor.A == 255) return p.BackColor;
            return Theme.Current.Background;
        }

        public static void Text(Graphics g, string text, Font font, Rectangle box, Color color, TextFormatFlags flags) { TextRenderer.DrawText(g, text ?? "", font, box, color, flags | TextFormatFlags.NoPadding); }
    }

    // Rounded card: surface colour and a hairline border; children sit inside Padding.
    public sealed class CardPanel : Panel
    {
        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Current.Surface;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Palette p = Theme.Current;
            e.Graphics.Clear(Draw.ParentBack(this));
            Draw.Smooth(e.Graphics);
            using (GraphicsPath path = Draw.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8))
            using (var fill = new SolidBrush(p.Surface))
            using (var pen = new Pen(p.Border))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        public void ApplyTheme() { BackColor = Theme.Current.Surface; Invalidate(true); }
    }

    public enum ButtonVariant { Primary, Secondary, Ghost, Danger }

    // Theme-aware flat button (RemoteAccessHub's FlatButton). It stays a Button, so Enter/Space, focus and the accessible name keep working.
    // With SplitWidth > 0 the right part is a drop-down area that raises DropDownClick instead of Click.
    public sealed class FlatButton : Button
    {
        const int FadeMs = 120, Frame = 15;
        readonly Timer fade = new Timer { Interval = Frame };
        float hot;
        bool hover, pressed, isChecked, clickFromMouse;
        Point mouseUp;
        ButtonVariant variant = ButtonVariant.Secondary;
        string glyph = "";
        int splitWidth;

        public event EventHandler DropDownClick;

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            Font = Theme.UiFont(9.5f, FontStyle.Bold);
            Height = 36;
            fade.Tick += (s, e) =>
            {
                float target = hover ? 1 : 0, step = (float)Frame / FadeMs;
                hot = target > hot ? Math.Min(target, hot + step) : Math.Max(target, hot - step);
                if (hot == target) fade.Stop();
                Invalidate();
            };
        }

        public ButtonVariant Variant { get { return variant; } set { variant = value; Invalidate(); } }
        // The suggested next action is drawn as the primary button; the others are secondary.
        public bool Primary { get { return variant == ButtonVariant.Primary; } set { Variant = value ? ButtonVariant.Primary : ButtonVariant.Secondary; } }
        public string Glyph { get { return glyph; } set { glyph = value ?? ""; Invalidate(); } }
        public bool IconOnly { get; set; }
        public bool ShowArrow { get; set; }
        public int SplitWidth { get { return splitWidth; } set { splitWidth = Math.Max(0, value); Invalidate(); } }
        // A toggle that is on: accent-tinted fill, accent border and an accent icon.
        public bool Checked { get { return isChecked; } set { if (isChecked == value) return; isChecked = value; Invalidate(); } }

        public bool IsInSplitArea(Point p) { return splitWidth > 0 && p.X >= Width - splitWidth; }

        void Fade() { if (Motion.Enabled) { fade.Start(); return; } fade.Stop(); hot = hover ? 1 : 0; Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Fade(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Fade(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) { hover = pressed = false; hot = 0; fade.Stop(); } Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        // Only a mouse click tells the drop-down area apart; Enter and Space always run the main action.
        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false; clickFromMouse = true; mouseUp = e.Location; Invalidate();
            base.OnMouseUp(e); // OnClick runs synchronously in here
            clickFromMouse = false;
        }
        protected override void OnClick(EventArgs e)
        {
            if (splitWidth > 0 && clickFromMouse && IsInSplitArea(mouseUp)) { if (DropDownClick != null) DropDownClick(this, EventArgs.Empty); return; }
            base.OnClick(e);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (splitWidth > 0 && (e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Down))) { e.Handled = true; if (DropDownClick != null) DropDownClick(this, EventArgs.Empty); return; }
            base.OnKeyDown(e);
        }

        internal Color FillColor { get { Color fill, border, text, icon; Colors(out fill, out border, out text, out icon); return fill; } }

        void Colors(out Color fill, out Color border, out Color text, out Color icon)
        {
            Palette p = Theme.Current;
            float h = 1 - (1 - hot) * (1 - hot); // ease-out
            Color back = Draw.ParentBack(this);
            switch (variant)
            {
                case ButtonVariant.Primary:
                    fill = !Enabled ? p.SurfaceAlt : pressed ? p.AccentPressed : Theme.Blend(p.Accent, p.AccentHover, h);
                    border = Enabled ? fill : p.Border;
                    text = Enabled ? p.OnAccent : p.Muted;
                    break;
                case ButtonVariant.Danger:
                    fill = !Enabled ? p.SurfaceAlt : pressed ? Theme.Blend(p.Surface, p.Danger, 0.35) : Theme.Blend(p.Surface, Theme.Blend(p.Surface, p.Danger, 0.22), h);
                    border = Enabled ? p.Danger : p.Border;
                    text = Enabled ? p.Danger : p.Muted;
                    break;
                case ButtonVariant.Ghost:
                    fill = !Enabled ? back : pressed ? p.Border : Theme.Blend(back, p.SurfaceAlt, h);
                    border = fill;
                    text = Enabled ? p.Text : p.Muted;
                    break;
                default:
                    fill = !Enabled ? p.SurfaceAlt : pressed ? p.Border : Theme.Blend(p.SurfaceAlt, Theme.Blend(p.SurfaceAlt, p.Border, 0.5), h);
                    border = p.Border;
                    text = Enabled ? p.Text : p.Muted;
                    break;
            }
            icon = text;
            if (isChecked && Enabled && variant != ButtonVariant.Primary)
            {
                fill = Theme.Blend(fill, p.Accent, pressed ? 0.28 : 0.16 + 0.06 * h);
                border = p.Accent;
                icon = p.Accent;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Palette p = Theme.Current;
            Graphics g = e.Graphics;
            g.Clear(Draw.ParentBack(this));
            Draw.Smooth(g);
            Color fill, border, text, icon;
            Colors(out fill, out border, out text, out icon);

            using (GraphicsPath path = Draw.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 6))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            Rectangle content = ClientRectangle;
            if (splitWidth > 0)
            {
                int sx = Width - splitWidth;
                using (var sep = new Pen(Theme.Blend(fill, text, 0.35))) g.DrawLine(sep, sx, 8, sx, Height - 8);
                Draw.Text(g, Theme.Glyph.ChevronDown, Theme.IconFont(8f), new Rectangle(sx, 0, splitWidth, Height), text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                content = new Rectangle(0, 0, sx, Height);
            }

            if (IconOnly) Draw.Text(g, glyph, Theme.IconFont(11f), content, icon, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            else
            {
                Size textSize = Draw.MeasureText(Text, Font);
                int iconW = glyph.Length > 0 ? 22 : 0, arrowW = ShowArrow ? 18 : 0;
                int x = content.X + Math.Max(10, (content.Width - (iconW + textSize.Width + arrowW)) / 2);
                if (iconW > 0) Draw.Text(g, glyph, Theme.IconFont(10.5f), new Rectangle(x, 0, iconW, Height), icon, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                Draw.Text(g, Text, Font, new Rectangle(x + iconW, 0, Math.Max(0, content.Right - x - iconW - arrowW), Height), text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
                if (arrowW > 0) Draw.Text(g, Theme.Glyph.ChevronDown, Theme.IconFont(8f), new Rectangle(x + iconW + textSize.Width + 6, 0, arrowW, Height), text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }

            if (Focused && ShowFocusCues)
                using (GraphicsPath path = Draw.RoundedRect(new RectangleF(2.5f, 2.5f, Width - 5.5f, Height - 5.5f), 4))
                using (var pen = new Pen(variant == ButtonVariant.Primary ? p.OnAccent : p.Accent, 1.5f) { DashStyle = DashStyle.Dot })
                    g.DrawPath(pen, path);
        }

        // Width that fits the label, icon and drop-down area.
        public int PreferredWidth()
        {
            if (IconOnly) return 38;
            int w = Draw.MeasureText(Text, Font).Width + 28;
            if (glyph.Length > 0) w += 22;
            if (ShowArrow) w += 20;
            return w + splitWidth;
        }

        protected override void Dispose(bool disposing) { if (disposing) fade.Dispose(); base.Dispose(disposing); }
    }

    // Header badge: an icon, a coloured dot and a short label in a pill.
    public sealed class StatusPill : Control
    {
        Color dot;
        string glyph = "";

        public StatusPill()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.UiFont(9f, FontStyle.Bold);
            TabStop = false;
            AccessibleRole = AccessibleRole.StaticText;
            Height = 30;
        }

        public Color Dot { get { return dot; } }

        public void Set(string text, Color dotColor, string icon)
        {
            Text = text; AccessibleName = text; dot = dotColor; glyph = icon ?? "";
            Width = Draw.MeasureText(Text, Font).Width + (glyph.Length > 0 ? 50 : 34);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Palette p = Theme.Current;
            Graphics g = e.Graphics;
            g.Clear(Draw.ParentBack(this));
            Draw.Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath path = Draw.RoundedRect(r, r.Height / 2))
            using (var fill = new SolidBrush(p.SurfaceAlt))
            using (var pen = new Pen(p.Border))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            int x = 12;
            if (glyph.Length > 0) { Draw.Text(g, glyph, Theme.IconFont(9f), new Rectangle(x, 0, 16, Height), p.SubText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter); x += 18; }
            using (var brush = new SolidBrush(dot)) g.FillEllipse(brush, x, (Height - 8) / 2f, 8, 8);
            Draw.Text(g, Text, Font, new Rectangle(x + 14, 0, Width, Height), p.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    public enum BannerKind { Info, Progress, Success, Warning, Error }

    // The line that narrates what is happening: a coloured stripe, an icon and up to two lines of text.
    public sealed class StatusBanner : Control
    {
        BannerKind kind = BannerKind.Info;

        public StatusBanner()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Font = Theme.UiFont(10f);
            TabStop = false;
            AccessibleRole = AccessibleRole.Alert;
        }

        public BannerKind Kind { get { return kind; } }

        public void Set(string text, BannerKind value) { Text = text; AccessibleName = text; kind = value; Invalidate(); }

        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        public static Color ColorOf(BannerKind value)
        {
            Palette p = Theme.Current;
            switch (value)
            {
                case BannerKind.Success: return p.Success;
                case BannerKind.Warning: return p.Warning;
                case BannerKind.Error: return p.Danger;
                case BannerKind.Progress: return p.Info;
                default: return p.SubText;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Palette p = Theme.Current;
            Graphics g = e.Graphics;
            g.Clear(Draw.ParentBack(this));
            Draw.Smooth(g);
            Color color = ColorOf(kind);
            string icon = kind == BannerKind.Success ? Theme.Glyph.Check : kind == BannerKind.Warning ? Theme.Glyph.Warning : kind == BannerKind.Error ? Theme.Glyph.Error : kind == BannerKind.Progress ? Theme.Glyph.Sync : Theme.Glyph.Info;
            bool tinted = kind != BannerKind.Info;
            using (GraphicsPath path = Draw.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8))
            using (var fill = new SolidBrush(Theme.Blend(p.Surface, color, tinted ? 0.10 : 0.0)))
            using (var pen = new Pen(Theme.Blend(p.Border, color, tinted ? 0.45 : 0.0)))
            {
                g.FillPath(fill, path);
                g.DrawPath(pen, path);
            }
            using (GraphicsPath bar = Draw.RoundedRect(new RectangleF(1, 8, 4, Height - 16), 2))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, bar);
            Draw.Text(g, icon, Theme.IconFont(13f), new Rectangle(16, 0, 24, Height), color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Draw.Text(g, Text, Font, new Rectangle(50, 4, Width - 62, Height - 8), p.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }

    // The large round state mark in the monitor card, drawn like RemoteAccessHub's step circles.
    public sealed class StateBadge : Control
    {
        Color color = Color.Gray;
        string glyph = Theme.Glyph.Monitor;
        bool filled;

        public StateBadge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            AccessibleRole = AccessibleRole.Graphic;
            Size = new Size(48, 48);
        }

        public Color Color { get { return color; } }

        // Filled for a confirmed good state, a tinted ring for everything else.
        public void Set(Color value, string icon, bool solid) { color = value; glyph = icon; filled = solid; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Palette p = Theme.Current;
            Graphics g = e.Graphics;
            g.Clear(Draw.ParentBack(this));
            Draw.Smooth(g);
            var circle = new RectangleF(1, 1, Width - 3, Height - 3);
            if (filled) using (var brush = new SolidBrush(color)) g.FillEllipse(brush, circle);
            else
            {
                using (var brush = new SolidBrush(Theme.Blend(p.Surface, color, 0.18))) g.FillEllipse(brush, circle);
                using (var pen = new Pen(color, 2)) g.DrawEllipse(pen, circle);
            }
            Draw.Text(g, glyph, Theme.IconFont(16f), ClientRectangle, filled ? (p.IsDark ? p.Background : Color.White) : color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // Thin progress bar: Value 0..1 is determinate, a negative Value is a moving segment (a still bar when Windows animations are off).
    public sealed class ProgressLine : Control
    {
        readonly Timer timer = new Timer { Interval = 30 };
        double value = -1;
        int phase;

        public ProgressLine()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            AccessibleRole = AccessibleRole.ProgressBar;
            Height = 4;
            timer.Tick += (s, e) => { phase += Math.Max(4, Width / 60); Invalidate(); };
        }

        public double Value { get { return value; } set { this.value = value; phase = 0; Animate(); Invalidate(); } }

        void Animate() { timer.Enabled = Visible && value < 0 && Motion.Enabled; }
        protected override void OnVisibleChanged(EventArgs e) { Animate(); base.OnVisibleChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Palette p = Theme.Current;
            Graphics g = e.Graphics;
            g.Clear(Draw.ParentBack(this));
            Draw.Smooth(g);
            var track = new RectangleF(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Draw.RoundedRect(track, Height / 2f))
            using (var brush = new SolidBrush(p.SurfaceAlt))
                g.FillPath(brush, path);
            RectangleF bar;
            if (value >= 0) bar = new RectangleF(0, 0, Math.Max(Height, (float)(track.Width * Math.Min(1, value))), track.Height);
            else if (timer.Enabled) { float w = Math.Max(40, Width / 3f); bar = new RectangleF(phase % (Width + w) - w, 0, w, track.Height); }
            else bar = track;
            using (GraphicsPath clip = Draw.RoundedRect(track, Height / 2f))
            using (GraphicsPath path = Draw.RoundedRect(bar, Height / 2f))
            using (var brush = new SolidBrush(timer.Enabled || value >= 0 ? p.Info : Theme.Blend(p.SurfaceAlt, p.Info, 0.6)))
            {
                g.SetClip(clip);
                g.FillPath(brush, path);
                g.ResetClip();
            }
        }

        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }

    // Drop-down list in theme colours. Items are owner-drawn (PaintItem draws custom content, otherwise the item text is used);
    // the frame and arrow are repainted after the system paints so the closed box follows the theme too.
    public sealed class ThemedComboBox : ComboBox
    {
        bool hover;

        public event DrawItemEventHandler PaintItem;

        public ThemedComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            ItemHeight = 26;
            IntegralHeight = false;
            Cursor = Cursors.Hand;
            ApplyTheme();
        }

        public void ApplyTheme() { BackColor = Theme.Current.SurfaceAlt; ForeColor = Theme.Current.Text; Invalidate(); }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            Palette p = Theme.Current;
            bool face = (e.State & DrawItemState.ComboBoxEdit) != 0, selected = !face && (e.State & DrawItemState.Selected) != 0;
            Color back = face ? (Enabled ? p.SurfaceAlt : p.Surface) : selected ? Theme.Blend(p.SurfaceAlt, p.Accent, 0.24) : p.SurfaceAlt;
            using (var brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);
            if (e.Index < 0) return;
            if (PaintItem != null) PaintItem(this, e);
            else Draw.Text(e.Graphics, GetItemText(Items[e.Index]), Font, new Rectangle(e.Bounds.X + (face ? 6 : 10), e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height), Enabled ? p.Text : p.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x000F && IsHandleCreated) PaintFrame(); // WM_PAINT
        }

        void PaintFrame()
        {
            Palette p = Theme.Current;
            Rectangle r = ClientRectangle;
            int button = SystemInformation.VerticalScrollBarWidth + 4;
            Color back = Enabled ? p.SurfaceAlt : p.Surface;
            Color border = !Enabled ? p.Border : Focused || DroppedDown ? p.Accent : hover ? Theme.Blend(p.Border, p.SubText, 0.4) : p.Border;
            using (Graphics g = Graphics.FromHwnd(Handle))
            {
                using (var brush = new SolidBrush(back)) g.FillRectangle(brush, r.Right - button - 1, 1, button, r.Height - 2);
                using (var pen = new Pen(back, 2)) g.DrawRectangle(pen, 2, 2, r.Width - 4, r.Height - 4);
                using (var pen = new Pen(border)) g.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
                if (Focused && Enabled) using (var pen = new Pen(p.Accent)) g.DrawRectangle(pen, 1, 1, r.Width - 3, r.Height - 3);
                Draw.Text(g, Theme.Glyph.ChevronDown, Theme.IconFont(8f), new Rectangle(r.Right - button - 1, 0, button, r.Height), Enabled ? p.SubText : p.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
