using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace RecRoomLauncher
{
    internal sealed class LauncherSettings
    {
        public string ClientDirectory { get; set; }
        public string ServerDirectory { get; set; }
        public string DiscoveryUrl { get; set; }
        public string ServiceDomain { get; set; }
        public string TachyonHost { get; set; }
        public string TachyonServerId { get; set; }
        public string VoiceKeyXml { get; set; }
    }

    internal enum ServerState { Unknown, Online, Degraded, Offline }

    internal sealed class ProbeResult
    {
        public readonly ServerState State;
        public readonly string Summary;
        public readonly int? Count;

        public ProbeResult(ServerState state, string summary, int? count)
        {
            State = state;
            Summary = summary;
            Count = count;
        }
    }

    internal enum Weight { Regular, Medium, Bold }

    // Palette and type scale from the "RecToon Launcher v3" design.
    internal static class Theme
    {
        public static readonly Color Page = Hex(0xF7F6EF);
        public static readonly Color Deep = Hex(0x0F2420);
        public static readonly Color Ink = Hex(0x17302A);
        public static readonly Color Body = Hex(0x55665F);
        public static readonly Color CaptionInk = Hex(0x6B8A7E);
        public static readonly Color Subtle = Hex(0x7B8A84);
        public static readonly Color LabelInk = Hex(0x3D4F48);
        public static readonly Color CardEdge = Hex(0xE7E8E1);
        public static readonly Color Green = Hex(0x1F7A5E);
        public static readonly Color GreenIcon = Hex(0x2E8B6E);
        public static readonly Color GreenTint = Hex(0xE6F2EC);
        public static readonly Color GreenTintHover = Hex(0xD6EBE1);
        public static readonly Color Red = Hex(0xB4442E);
        public static readonly Color RedTint = Hex(0xFBE7E2);
        public static readonly Color RedTintHover = Hex(0xF6D8D0);
        public static readonly Color Gold = Hex(0xA87F12);
        public static readonly Color GoldTint = Hex(0xFBF3D9);
        public static readonly Color Neutral = Hex(0xF3F5F0);
        public static readonly Color NeutralHover = Hex(0xE9EDE6);
        public static readonly Color NeutralEdge = Hex(0xE2E6E4);
        public static readonly Color InputEdge = Hex(0xE3E8E1);
        public static readonly Color Mint = Hex(0x5BB89A);
        public static readonly Color Lime = Hex(0xA9C774);
        public static readonly Color Sun = Hex(0xEFC84C);
        public static readonly Color PressShadow = Hex(0x3E8F74);
        public static readonly Color LogText = Hex(0xD5E3DC);
        public static readonly Color BannerText = Hex(0xB9D3C8);
        public static readonly Color OnDark = Hex(0xE6EFE9);
        public static readonly Color DotOnline = Hex(0x2EAE7E);
        public static readonly Color DotOffline = Hex(0xE0604A);
        public static readonly Color DotWarning = Hex(0xE0A21A);
        public static readonly Color DotIdle = Hex(0x9AA9A2);

        public static Color Hex(int rgb) { return Color.FromArgb((rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

        private static float _scale;
        public static float Scale
        {
            get
            {
                if (_scale <= 0)
                    using (var g = Graphics.FromHwnd(IntPtr.Zero)) _scale = g.DpiX / 96f;
                return _scale;
            }
        }

        public static float S(float value) { return value * Scale; }

        public static Color Dot(ServerState state)
        {
            switch (state)
            {
                case ServerState.Online: return DotOnline;
                case ServerState.Degraded: return DotWarning;
                case ServerState.Offline: return DotOffline;
                default: return DotIdle;
            }
        }

        public static Color Blend(Color top, Color bottom, float amount)
        {
            return Color.FromArgb(
                (int)(bottom.R + (top.R - bottom.R) * amount),
                (int)(bottom.G + (top.G - bottom.G) * amount),
                (int)(bottom.B + (top.B - bottom.B) * amount));
        }

        public static Color Lighten(Color color, float amount)
        {
            return Blend(Color.White, color, amount);
        }

        // Design sizes are CSS pixels; WinForms fonts are points.
        public static Font F(float px, Weight weight)
        {
            var pt = px * 0.75f;
            if (weight == Weight.Medium)
            {
                var medium = EmbeddedFonts.Family("Ubuntu Medium");
                if (medium != null) return new Font(medium, pt);
            }
            else
            {
                var ubuntu = EmbeddedFonts.Family("Ubuntu");
                if (ubuntu != null) return new Font(ubuntu, pt, weight == Weight.Bold ? FontStyle.Bold : FontStyle.Regular);
            }
            switch (weight)
            {
                case Weight.Medium: return new Font("Segoe UI Semibold", pt);
                case Weight.Bold: return new Font("Segoe UI", pt, FontStyle.Bold);
                default: return new Font("Segoe UI", pt);
            }
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static LinearGradientBrush BrandGradient(RectangleF rect, float lighten, bool limeAt55)
        {
            var brush = new LinearGradientBrush(new RectangleF(rect.X - 1, rect.Y, rect.Width + 2, rect.Height), Color.Black, Color.Black, 0f);
            brush.InterpolationColors = new ColorBlend {
                Colors = new[] { Lighten(Mint, lighten), Lighten(Lime, lighten), Lighten(Sun, lighten) },
                Positions = new[] { 0f, limeAt55 ? 0.55f : 0.5f, 1f }
            };
            return brush;
        }

        public static Label Label(string text, Font font, Color color, Padding margin)
        {
            return new Label {
                Text = text, AutoSize = true, UseMnemonic = false, ForeColor = color, Margin = margin,
                Anchor = AnchorStyles.Left | AnchorStyles.Right, Font = font
            };
        }

        public static Label Caption(string text, Padding margin)
        {
            return Label(text.ToUpperInvariant(), F(12, Weight.Bold), CaptionInk, margin);
        }

        public static TableLayoutPanel Stack()
        {
            var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return stack;
        }

        public static void Add(TableLayoutPanel stack, Control control)
        {
            stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            stack.Controls.Add(control, 0, stack.RowStyles.Count - 1);
        }

        public static void AddFill(TableLayoutPanel stack, Control control)
        {
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            stack.Controls.Add(control, 0, stack.RowStyles.Count - 1);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        private static void Dwm(Form form, int attribute, int value)
        {
            try { DwmSetWindowAttribute(form.Handle, attribute, ref value, sizeof(int)); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        private static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        // Windows 11 draws the native caption in the design's deep green; older builds ignore these attributes.
        public static void UseBrandTitleBar(Form form)
        {
            form.HandleCreated += (s, e) => {
                Dwm(form, 20, 1);
                Dwm(form, 34, ColorRef(Deep));
                Dwm(form, 35, ColorRef(Deep));
                Dwm(form, 36, ColorRef(OnDark));
            };
        }

        public static void UseRoundCorners(Form form)
        {
            form.HandleCreated += (s, e) => Dwm(form, 33, 2);
        }

        public static void UseDarkScrollBars(Control control)
        {
            control.HandleCreated += (s, e) => {
                try { SetWindowTheme(control.Handle, "DarkMode_Explorer", null); }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
            };
        }
    }

    // Loads any fonts/*.ttf compiled into the exe (e.g. Ubuntu) for both GDI+ and GDI text rendering.
    internal static class EmbeddedFonts
    {
        [DllImport("gdi32.dll")]
        private static extern IntPtr AddFontMemResourceEx(IntPtr font, uint length, IntPtr reserved, ref uint count);

        private static readonly PrivateFontCollection Collection = new PrivateFontCollection();
        private static readonly Dictionary<string, FontFamily> Cache = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;

        public static FontFamily Family(string name)
        {
            Load();
            FontFamily family;
            if (Cache.TryGetValue(name, out family)) return family;
            family = Collection.Families.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (family == null)
            {
                try { family = new FontFamily(name); }
                catch (ArgumentException) { family = null; }
            }
            Cache[name] = family;
            return family;
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            var assembly = Assembly.GetExecutingAssembly();
            foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
            {
                using (var stream = assembly.GetManifestResourceStream(resource))
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    var data = buffer.ToArray();
                    // The font memory must outlive the collection, so it is intentionally never freed.
                    var pointer = Marshal.AllocCoTaskMem(data.Length);
                    Marshal.Copy(data, 0, pointer, data.Length);
                    Collection.AddMemoryFont(pointer, data.Length);
                    uint count = 0;
                    AddFontMemResourceEx(pointer, (uint)data.Length, IntPtr.Zero, ref count);
                }
            }
        }
    }

    internal static class Assets
    {
        public static Image Image(string name)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RecRoomLauncher." + name))
            {
                if (stream == null) return null;
                using (var image = System.Drawing.Image.FromStream(stream)) return new Bitmap(image);
            }
        }

        public static Icon AppIcon()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RecRoomLauncher.launcher.ico"))
                return stream == null ? null : new Icon(stream);
        }
    }

    internal enum Glyph { None, Play, Stop, Refresh, Arrow, Gear, Folder, Home, Globe, Check, Cross, Alert, Close }

    // Vector icons traced from the design's 24x24 SVGs.
    internal static class Icons
    {
        public static void Draw(Graphics g, Glyph glyph, RectangleF box, Color color)
        {
            if (glyph == Glyph.None) return;
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(box.X, box.Y);
            g.ScaleTransform(box.Width / 24f, box.Height / 24f);
            using (var pen = new Pen(color, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            {
                switch (glyph)
                {
                    case Glyph.Play:
                        var triangle = new[] { new PointF(7, 4), new PointF(20, 12), new PointF(7, 20) };
                        pen.Width = 2;
                        g.FillPolygon(brush, triangle);
                        g.DrawPolygon(pen, triangle);
                        break;
                    case Glyph.Stop:
                        using (var square = Theme.Rounded(new RectangleF(5, 5, 14, 14), 2)) g.FillPath(brush, square);
                        break;
                    case Glyph.Refresh:
                        pen.Width = 2;
                        g.DrawArc(pen, 4, 4, 16, 16, 0, 315);
                        g.DrawLines(pen, new[] { new PointF(20, 4), new PointF(20, 8), new PointF(16, 8) });
                        break;
                    case Glyph.Arrow:
                        pen.Width = 2.2f;
                        g.DrawLine(pen, 4, 12, 20, 12);
                        g.DrawLines(pen, new[] { new PointF(14, 6), new PointF(20, 12), new PointF(14, 18) });
                        break;
                    case Glyph.Gear:
                        var teeth = new List<PointF>();
                        for (var k = 0; k < 8; k++)
                        {
                            var a = k * 45.0;
                            foreach (var p in new[] { new[] { -14.0, 7.2 }, new[] { -8.0, 10.0 }, new[] { 8.0, 10.0 }, new[] { 14.0, 7.2 } })
                            {
                                var rad = (a + p[0]) * Math.PI / 180;
                                teeth.Add(new PointF((float)(12 + p[1] * Math.Cos(rad)), (float)(12 + p[1] * Math.Sin(rad))));
                            }
                        }
                        g.DrawPolygon(pen, teeth.ToArray());
                        g.DrawEllipse(pen, 9, 9, 6, 6);
                        break;
                    case Glyph.Folder:
                        using (var folder = new GraphicsPath())
                        {
                            folder.AddArc(3, 5, 4, 4, 180, 90);
                            folder.AddLine(5, 5, 9, 5);
                            folder.AddLine(9, 5, 11, 7);
                            folder.AddLine(11, 7, 19, 7);
                            folder.AddArc(17, 7, 4, 4, 270, 90);
                            folder.AddLine(21, 9, 21, 17);
                            folder.AddArc(17, 15, 4, 4, 0, 90);
                            folder.AddLine(19, 19, 5, 19);
                            folder.AddArc(3, 15, 4, 4, 90, 90);
                            folder.CloseFigure();
                            g.DrawPath(pen, folder);
                        }
                        break;
                    case Glyph.Home:
                        g.DrawPolygon(pen, new[] {
                            new PointF(3, 11), new PointF(12, 4), new PointF(21, 11), new PointF(21, 20), new PointF(20, 21),
                            new PointF(15, 21), new PointF(15, 15), new PointF(9, 15), new PointF(9, 21), new PointF(4, 21), new PointF(3, 20)
                        });
                        break;
                    case Glyph.Globe:
                        g.DrawEllipse(pen, 3, 3, 18, 18);
                        g.DrawLine(pen, 3, 12, 21, 12);
                        using (var meridian = new GraphicsPath())
                        {
                            meridian.AddBezier(12, 3, 14.5f, 5.6f, 15.8f, 8.6f, 15.8f, 12);
                            meridian.AddBezier(15.8f, 12, 15.8f, 15.4f, 14.5f, 18.4f, 12, 21);
                            meridian.AddBezier(12, 21, 9.5f, 18.4f, 8.2f, 15.4f, 8.2f, 12);
                            meridian.AddBezier(8.2f, 12, 8.2f, 8.6f, 9.5f, 5.6f, 12, 3);
                            g.DrawPath(pen, meridian);
                        }
                        break;
                    case Glyph.Check:
                        pen.Width = 3;
                        g.DrawLines(pen, new[] { new PointF(5, 12), new PointF(10, 17), new PointF(19, 7) });
                        break;
                    case Glyph.Cross:
                        pen.Width = 3;
                        g.DrawLine(pen, 7, 7, 17, 17);
                        g.DrawLine(pen, 17, 7, 7, 17);
                        break;
                    case Glyph.Alert:
                        pen.Width = 3;
                        g.DrawLine(pen, 12, 5, 12, 13);
                        g.FillEllipse(brush, 10.2f, 16.2f, 3.6f, 3.6f);
                        break;
                    case Glyph.Close:
                        pen.Width = 2;
                        g.DrawLine(pen, 7, 7, 17, 17);
                        g.DrawLine(pen, 17, 7, 7, 17);
                        break;
                }
            }
            g.Restore(state);
        }
    }

    internal class RoundedPanel : Panel
    {
        public float Radius = 18;
        public float BorderWidth = 1;
        public Color BorderColor = Color.Empty;

        public RoundedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent != null ? Parent.BackColor : BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var inset = BorderColor == Color.Empty ? 0 : Theme.S(BorderWidth) / 2;
            var rect = new RectangleF(inset, inset, Width - 1 - inset * 2, Height - 1 - inset * 2);
            using (var path = Theme.Rounded(rect, Theme.S(Radius)))
            using (var fill = new SolidBrush(BackColor))
            {
                e.Graphics.FillPath(fill, path);
                if (BorderColor != Color.Empty)
                    using (var pen = new Pen(BorderColor, Theme.S(BorderWidth))) e.Graphics.DrawPath(pen, path);
            }
        }
    }

    // One button class covers the design's tinted, outlined, glass (on the banner) and 3D gradient buttons.
    internal sealed class PillButton : Button
    {
        public Color Fill = Theme.Neutral;
        public Color HoverFill = Theme.NeutralHover;
        public Color Ink = Theme.Ink;
        public Color HoverInk = Color.Empty;
        public Color Edge = Color.Empty;
        public bool Gradient;
        public int Depth;
        public float Radius = 12;
        public float IconPx = 16;
        public float Gap = 10;
        public float SidePadding = 18;
        public int BaseHeight = 44;
        public Glyph Icon;
        private bool _hover;
        private bool _pressed;

        public PillButton(string text, Glyph icon, Font font)
        {
            Text = text;
            Icon = icon;
            Font = font;
            UseMnemonic = false;
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Margin = new Padding(0, 0, 10, 0);
            Height = 44;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        }

        public static PillButton Tinted(string text, Glyph icon, Color fill, Color hover, Color ink)
        {
            return new PillButton(text, icon, Theme.F(15, Weight.Medium)) { Fill = fill, HoverFill = hover, Ink = ink, AutoSize = true };
        }

        public static PillButton Outlined(string text, Glyph icon)
        {
            return new PillButton(text, icon, Theme.F(15, Weight.Medium)) { Edge = Theme.NeutralEdge, AutoSize = true };
        }

        public static PillButton Primary(string text, Glyph icon, float fontPx, int height, int depth)
        {
            return new PillButton(text, icon, Theme.F(fontPx, Weight.Bold)) {
                Gradient = true, Depth = depth, Height = height + depth, BaseHeight = height + depth, Radius = 14, Ink = Theme.Deep, IconPx = 18, Gap = 12
            };
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var content = MeasureContent();
            return new Size(content.Width + (int)Theme.S(SidePadding * 2), (int)Theme.S(BaseHeight));
        }

        private Size MeasureContent()
        {
            var text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            if (Icon == Glyph.None) return text;
            if (String.IsNullOrEmpty(Text)) return new Size((int)Theme.S(IconPx), text.Height);
            return new Size((int)Theme.S(IconPx + Gap) + text.Width, text.Height);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            PaintBackdrop(g);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var depth = Theme.S(Depth);
            var pressedShift = Gradient && _pressed && Enabled ? Theme.S(Depth - 2) : 0;
            var face = new RectangleF(0.5f, 0.5f + pressedShift, Width - 1.5f, Height - 1.5f - depth);
            var radius = Theme.S(Radius);

            if (Gradient)
            {
                var shadow = face;
                shadow.Offset(0, _pressed && Enabled ? Theme.S(2) : depth);
                using (var path = Theme.Rounded(shadow, radius))
                using (var brush = new SolidBrush(Enabled ? Theme.PressShadow : Theme.Blend(Theme.PressShadow, Theme.Page, 0.4f)))
                    g.FillPath(brush, path);
                using (var path = Theme.Rounded(face, radius))
                using (var brush = Theme.BrandGradient(face, !Enabled ? 0.45f : _hover ? 0.06f : 0f, true))
                    g.FillPath(brush, path);
            }
            else
            {
                var fill = _hover && Enabled ? HoverFill : Fill;
                if (!Enabled) fill = Color.FromArgb(fill.A / 2, fill);
                using (var path = Theme.Rounded(face, radius))
                {
                    using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                    if (Edge != Color.Empty)
                        using (var pen = new Pen(Edge, Theme.S(1))) g.DrawPath(pen, path);
                }
            }

            var ink = _hover && HoverInk != Color.Empty ? HoverInk : Ink;
            if (!Enabled) ink = Color.FromArgb(140, ink);
            if (Focused && ShowFocusCues)
            {
                var inset = Theme.S(3);
                var ring = new RectangleF(face.X + inset, face.Y + inset, face.Width - inset * 2, face.Height - inset * 2);
                using (var path = Theme.Rounded(ring, Math.Max(2, radius - inset)))
                using (var pen = new Pen(Color.FromArgb(150, ink), Theme.S(1.5f)) { DashStyle = DashStyle.Dot })
                    g.DrawPath(pen, path);
            }

            var content = MeasureContent();
            var x = face.X + (face.Width - content.Width) / 2f;
            var midY = face.Y + face.Height / 2f;
            if (Icon != Glyph.None)
            {
                var size = Theme.S(IconPx);
                Icons.Draw(g, Icon, new RectangleF(x, midY - size / 2, size, size), ink);
                x += Theme.S(IconPx + Gap);
            }
            if (!String.IsNullOrEmpty(Text))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (var brush = new SolidBrush(ink))
                using (var format = new StringFormat(StringFormat.GenericTypographic) { LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap })
                    g.DrawString(Text, Font, brush, new RectangleF(x, face.Y, face.Right - x, face.Height), format);
            }
        }

        // Paint whatever is behind the button so rounded corners and translucent fills sit on the real surface.
        private void PaintBackdrop(Graphics g)
        {
            if (Parent == null) return;
            var state = g.Save();
            g.TranslateTransform(-Left, -Top);
            var area = new Rectangle(Left, Top, Width, Height);
            using (var args = new PaintEventArgs(g, area))
            {
                InvokePaintBackground(Parent, args);
                InvokePaint(Parent, args);
            }
            g.Restore(state);
        }
    }

    internal sealed class StatusDot : Control
    {
        private Color _color = Theme.DotIdle;

        public StatusDot()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        public void Set(Color color) { _color = color; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var background = Parent != null ? Parent.BackColor : Color.White;
            g.Clear(background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var size = Math.Min(Width, Height) - 1;
            var core = size / 2f;
            using (var halo = new SolidBrush(Theme.Blend(_color, background, 0.18f)))
                g.FillEllipse(halo, (Width - size) / 2f, (Height - size) / 2f, size, size);
            using (var dot = new SolidBrush(_color))
                g.FillEllipse(dot, (Width - core) / 2f, (Height - core) / 2f, core, core);
        }
    }

    // Tinted rounded tile or circle with a vector icon (join badges and checklist marks).
    internal sealed class IconTile : Control
    {
        public Color Tint;
        public Color Ink;
        public Glyph Icon;
        public float IconFraction = 26f / 60f;
        public bool Circle;

        public IconTile(Glyph icon, Color tint, Color ink)
        {
            Icon = icon;
            Tint = tint;
            Ink = ink;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        public void Set(Glyph icon, Color tint, Color ink) { Icon = icon; Tint = tint; Ink = ink; Invalidate(); }

        // Tiles are always square; without this the stack layout collapses their width.
        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            if (Circle) base.SetBoundsCore(x, y, width, height, specified);
            else base.SetBoundsCore(x, y, height, height, specified | BoundsSpecified.Width);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var brush = new SolidBrush(Tint))
            {
                if (Circle) g.FillEllipse(brush, rect);
                else using (var path = Theme.Rounded(rect, Width * 16f / 60f)) g.FillPath(brush, path);
            }
            var size = Width * IconFraction;
            Icons.Draw(g, Icon, new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size), Ink);
        }
    }

    // Single-line text box that flattens multi-line pastes (e.g. RSA key XML) instead of truncating them.
    internal sealed class FlatPasteTextBox : TextBox
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x302 && !Multiline && Clipboard.ContainsText())
            {
                SelectedText = Regex.Replace(Clipboard.GetText(), @"\s*[\r\n]+\s*", "");
                return;
            }
            base.WndProc(ref m);
        }
    }

    internal sealed class InputField : RoundedPanel
    {
        public readonly FlatPasteTextBox Box = new FlatPasteTextBox();
        private readonly Color _rest;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public InputField(string cue, float fontPx, int height, Color rest)
        {
            _rest = rest;
            Radius = height >= 46 ? 12 : 10;
            BorderWidth = 2;
            BackColor = rest;
            BorderColor = Theme.InputEdge;
            Anchor = AnchorStyles.Left | AnchorStyles.Right;
            Margin = Padding.Empty;
            Padding = new Padding(height >= 46 ? 14 : 12, 0, 12, 0);
            Height = height;
            Cursor = Cursors.IBeam;

            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = rest;
            Box.ForeColor = Theme.Ink;
            Box.Font = Theme.F(fontPx, Weight.Regular);
            Box.Enter += (s, e) => SetFocused(true);
            Box.Leave += (s, e) => SetFocused(false);
            if (!String.IsNullOrEmpty(cue))
                Box.HandleCreated += (s, e) => SendMessage(Box.Handle, 0x1501, (IntPtr)1, cue);
            Click += (s, e) => Box.Focus();
            Controls.Add(Box);
        }

        public string Value { get { return Box.Text.Trim(); } set { Box.Text = value ?? ""; } }

        private void SetFocused(bool focused)
        {
            BackColor = focused ? Color.White : _rest;
            Box.BackColor = BackColor;
            BorderColor = focused ? Theme.Mint : Theme.InputEdge;
            Invalidate();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            Box.SetBounds(Padding.Left, (Height - Box.Height) / 2, Math.Max(10, Width - Padding.Horizontal), Box.Height);
        }
    }

    internal enum CaptionKind { Minimize, Maximize, Close }

    // Window buttons drawn over the banner art, replacing the native title bar.
    internal sealed class CaptionButton : Control
    {
        private readonly CaptionKind _kind;
        private bool _hover;

        public CaptionButton(CaptionKind kind)
        {
            _kind = kind;
            Size = new Size(46, 40);
            TabStop = false;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = kind == CaptionKind.Minimize ? "Minimize" : kind == CaptionKind.Maximize ? "Maximize" : "Close";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !ClientRectangle.Contains(e.Location)) return;
            var form = FindForm();
            if (form == null) return;
            switch (_kind)
            {
                case CaptionKind.Minimize: form.WindowState = FormWindowState.Minimized; break;
                case CaptionKind.Maximize:
                    form.WindowState = form.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                    break;
                default: form.Close(); break;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var state = g.Save();
            g.TranslateTransform(-Left, -Top);
            using (var args = new PaintEventArgs(g, Bounds)) InvokePaintBackground(Parent, args);
            g.Restore(state);

            var close = _kind == CaptionKind.Close;
            if (_hover)
                using (var hover = new SolidBrush(close ? Theme.Hex(0xC42B1C) : Color.FromArgb(20, Color.White)))
                    g.FillRectangle(hover, ClientRectangle);

            var ink = _hover && close ? Color.White : Theme.Hex(0xC9D6CF);
            var cx = Width / 2f;
            var cy = Height / 2f;
            var half = Theme.S(5);
            using (var pen = new Pen(ink, Theme.S(1)))
            {
                switch (_kind)
                {
                    case CaptionKind.Minimize:
                        g.DrawLine(pen, cx - half, cy, cx + half, cy);
                        break;
                    case CaptionKind.Maximize:
                        var box = Theme.S(9);
                        var form = FindForm();
                        if (form != null && form.WindowState == FormWindowState.Maximized)
                        {
                            var inner = box - Theme.S(2);
                            g.DrawRectangle(pen, cx - box / 2 + Theme.S(2), cy - box / 2, inner, inner);
                            g.DrawRectangle(pen, cx - box / 2, cy - box / 2 + Theme.S(2), inner, inner);
                        }
                        else g.DrawRectangle(pen, cx - box / 2, cy - box / 2, box, box);
                        break;
                    default:
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.DrawLine(pen, cx - half, cy - half, cx + half, cy + half);
                        g.DrawLine(pen, cx + half, cy - half, cx - half, cy + half);
                        break;
                }
            }
        }
    }

    // Banner from the design: game art, a left-to-right shade, the RecToon logo, and the brand strip.
    // It also stands in for the title bar: the top CaptionHeight carries the window buttons, and the
    // whole banner drags the window.
    internal sealed class Banner : Control
    {
        public const int CaptionHeight = 40;
        private readonly CaptionButton _minimize = new CaptionButton(CaptionKind.Minimize);
        private readonly CaptionButton _maximize = new CaptionButton(CaptionKind.Maximize);
        private readonly CaptionButton _close = new CaptionButton(CaptionKind.Close);
        private readonly Image _art = Assets.Image("banner.jpg");
        private readonly Image _logo = Assets.Image("logo-full.png");
        private readonly Font _titleFont = Theme.F(28, Weight.Bold);
        private readonly Font _subtitleFont = Theme.F(16, Weight.Regular);
        private Bitmap _cache;
        public Control Action;

        public Banner()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Deep;
            Controls.Add(_minimize);
            Controls.Add(_maximize);
            Controls.Add(_close);
        }

        public void RefreshCaption() { _maximize.Invalidate(); }

        // Let clicks on the art fall through to the form, which reports them as caption hits.
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x84) { m.Result = (IntPtr)(-1); return; }
            base.WndProc(ref m);
        }

        protected override void OnResize(EventArgs e)
        {
            if (_cache != null) { _cache.Dispose(); _cache = null; }
            base.OnResize(e);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            _close.Location = new Point(Width - _close.Width, 0);
            _maximize.Location = new Point(_close.Left - _maximize.Width, 0);
            _minimize.Location = new Point(_maximize.Left - _minimize.Width, 0);
            var caption = (int)Theme.S(CaptionHeight);
            if (Action != null)
                Action.Location = new Point(Width - (int)Theme.S(28) - Action.Width, caption + (Height - caption - (int)Theme.S(4) - Action.Height) / 2);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (Width <= 0 || Height <= 0) return;
            if (_cache == null) _cache = Render();
            e.Graphics.DrawImageUnscaled(_cache, 0, 0);
        }

        protected override void OnPaint(PaintEventArgs e) { }

        private Bitmap Render()
        {
            var bitmap = new Bitmap(Width, Height);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Theme.Deep);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var strip = Theme.S(4);
                if (_art != null)
                {
                    var scale = Math.Max((float)Width / _art.Width, (float)Height / _art.Height);
                    var w = _art.Width * scale;
                    var h = _art.Height * scale;
                    g.DrawImage(_art, (Width - w) / 2f, (Height - h) * 0.55f, w, h);
                }
                var all = new RectangleF(0, 0, Width, Height);
                using (var shade = new LinearGradientBrush(new RectangleF(-1, 0, Width + 2, Height), Color.Black, Color.Black, 0f))
                {
                    shade.InterpolationColors = new ColorBlend {
                        Colors = new[] { Color.FromArgb(224, Theme.Deep), Color.FromArgb(153, Theme.Deep), Color.FromArgb(38, Theme.Deep) },
                        Positions = new[] { 0f, 0.45f, 1f }
                    };
                    g.FillRectangle(shade, all);
                }
                // Darken the top edge so the window buttons stay legible over bright foliage.
                var caption = Theme.S(CaptionHeight);
                using (var fade = new LinearGradientBrush(new RectangleF(0, -1, Width, caption * 1.8f + 2), Color.FromArgb(150, Theme.Deep), Color.FromArgb(0, Theme.Deep), 90f))
                    g.FillRectangle(fade, 0, 0, Width, caption * 1.8f);
                var stripRect = new RectangleF(0, Height - strip, Width, strip);
                using (var brand = Theme.BrandGradient(stripRect, 0, false)) g.FillRectangle(brand, stripRect);

                var body = Height - strip - caption;
                var logoSize = body * 200f / 252f;
                // Line the logo's visible edge (14/512 of the image is transparent margin) up with the cards' content below.
                var logoX = Theme.S(28 + 26) - logoSize * 14f / 512f;
                if (_logo != null) g.DrawImage(_logo, logoX, caption + (body - logoSize) / 2f, logoSize, logoSize);

                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                var textX = logoX + logoSize + Theme.S(14);
                var titleHeight = g.MeasureString("RecToon Launcher", _titleFont).Height;
                var subtitleHeight = g.MeasureString("Ag", _subtitleFont).Height;
                var textTop = caption + (body - titleHeight - Theme.S(6) - subtitleHeight) / 2f;
                using (var white = new SolidBrush(Color.White))
                    g.DrawString("RecToon Launcher", _titleFont, white, textX, textTop);
                using (var soft = new SolidBrush(Theme.BannerText))
                    g.DrawString("2026 patch · host your own server or join a friend's", _subtitleFont, soft, textX, textTop + titleHeight + Theme.S(6));
            }
            return bitmap;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_cache != null) _cache.Dispose();
                if (_art != null) _art.Dispose();
                if (_logo != null) _logo.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // Dark header with the brand strip, used by the Settings modal.
    internal sealed class ModalHeader : Control
    {
        private readonly string _title;
        private readonly string _subtitle;
        private readonly Font _titleFont = Theme.F(24, Weight.Bold);
        private readonly Font _subtitleFont = Theme.F(15, Weight.Regular);

        public ModalHeader(string title, string subtitle)
        {
            _title = title;
            _subtitle = subtitle;
            BackColor = Theme.Deep;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Deep);
            var strip = new RectangleF(0, Height - Theme.S(4), Width, Theme.S(4));
            using (var brand = Theme.BrandGradient(strip, 0, false)) g.FillRectangle(brand, strip);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var left = (int)Theme.S(28);
            var titleHeight = TextRenderer.MeasureText(_title, _titleFont).Height;
            TextRenderer.DrawText(e.Graphics, _title, _titleFont, new Point(left, (int)Theme.S(26)), Color.White, Theme.Deep, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(e.Graphics, _subtitle, _subtitleFont, new Point(left, (int)Theme.S(32) + titleHeight), Theme.BannerText, Theme.Deep, TextFormatFlags.NoPadding);
        }
    }

    internal sealed class SettingsDialog : Form
    {
        public readonly InputField Game = new InputField(null, 15, 46, Color.White);
        public readonly InputField Server = new InputField(null, 15, 46, Color.White);

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public SettingsDialog(string game, string server, Func<string, string, string> browse)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Settings";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = Theme.Page;
            Font = Theme.F(15, Weight.Regular);
            ClientSize = new Size(640, 392);
            Theme.UseRoundCorners(this);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(28, 24, 28, 26) };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(body);

            var header = new ModalHeader("Settings", "Where the patched Rec Room client and your RecToon server are installed.") { Dock = DockStyle.Top, Height = 106 };
            header.MouseDown += (s, e) => {
                if (e.Button != MouseButtons.Left) return;
                ReleaseCapture();
                SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
            };
            var close = new PillButton("", Glyph.Close, Theme.F(14, Weight.Regular)) {
                Size = new Size(34, 34), Radius = 10, IconPx = 22, SidePadding = 0, Gap = 0,
                Fill = Color.FromArgb(20, Color.White), HoverFill = Color.FromArgb(41, Color.White), Ink = Hex(0xC9D6CF), HoverInk = Color.White,
                DialogResult = DialogResult.Cancel, TabStop = false
            };
            close.AccessibleName = "Close";
            header.Controls.Add(close);
            header.Resize += (s, e) => close.Location = new Point(header.Width - close.Width - (int)Theme.S(18), (int)Theme.S(18));
            Controls.Add(header);

            Theme.Add(body, FolderRow("Game folder", "(the patched folder that contains RecRoom.exe)", Game, game, current => browse("Select the patched Rec Room folder (contains RecRoom.exe)", current)));
            var serverRow = FolderRow("Server folder", "(contains server.js)", Server, server, current => browse("Select the folder containing server.js", current));
            serverRow.Margin = new Padding(0, 18, 0, 0);
            Theme.Add(body, serverRow);
            Theme.AddFill(body, new Panel { Margin = Padding.Empty });

            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
            var save = PillButton.Primary("Save", Glyph.None, 15, 46, 4);
            save.Radius = 12;
            save.SidePadding = 26;
            save.AutoSize = true;
            save.DialogResult = DialogResult.OK;
            save.Margin = Padding.Empty;
            var cancel = new PillButton("Cancel", Glyph.None, Theme.F(15, Weight.Medium)) {
                Fill = Color.White, HoverFill = Theme.Neutral, Edge = Theme.NeutralEdge, Height = 46, BaseHeight = 46, SidePadding = 22,
                AutoSize = true, DialogResult = DialogResult.Cancel, Margin = new Padding(0, 0, 10, 4)
            };
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            var rerun = new LinkLabel {
                Text = "Run setup again…", AutoSize = true, Font = Theme.F(14, Weight.Medium), LinkColor = Theme.Green,
                ActiveLinkColor = Theme.Hex(0x145B45), LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 14, 18, 0)
            };
            rerun.LinkClicked += (s, e) => { DialogResult = DialogResult.Retry; Close(); };
            buttons.Controls.Add(rerun);
            Theme.Add(body, buttons);
            AcceptButton = save;
            CancelButton = cancel;
            ActiveControl = save;
            ResumeLayout(false);
            PerformLayout();
        }

        private static Color Hex(int rgb) { return Theme.Hex(rgb); }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        private static Control FolderRow(string label, string hint, InputField field, string value, Func<string, string> browse)
        {
            var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var caption = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 8) };
            caption.Controls.Add(Theme.Label(label, Theme.F(14, Weight.Medium), Theme.LabelInk, Padding.Empty));
            caption.Controls.Add(Theme.Label(hint, Theme.F(14, Weight.Regular), Theme.Subtle, new Padding(2, 0, 0, 0)));
            row.Controls.Add(caption, 0, 0);
            row.SetColumnSpan(caption, 2);
            field.Value = value;
            field.Margin = new Padding(0, 0, 8, 0);
            row.Controls.Add(field, 0, 1);
            var pick = PillButton.Tinted("Browse", Glyph.Folder, Theme.GreenTint, Theme.GreenTintHover, Theme.Green);
            pick.Font = Theme.F(14, Weight.Medium);
            pick.Height = 46;
            pick.BaseHeight = 46;
            pick.Radius = 10;
            pick.Gap = 8;
            pick.SidePadding = 16;
            pick.Margin = Padding.Empty;
            pick.Click += (s, e) => { var chosen = browse(field.Value); if (chosen != null) field.Value = chosen; };
            row.Controls.Add(pick, 1, 1);
            return row;
        }
    }

    // Finds the process listening on a local TCP port (used to stop the local RecToon server).
    internal static class PortOwner
    {
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);

        public static List<int> ListeningProcessIds(int port)
        {
            var ids = new List<int>();
            Collect(2, port, 24, 8, 20, ids);   // MIB_TCPROW_OWNER_PID
            Collect(23, port, 56, 20, 52, ids); // MIB_TCP6ROW_OWNER_PID
            return ids.Distinct().ToList();
        }

        private static void Collect(int family, int port, int rowSize, int portOffset, int pidOffset, List<int> ids)
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                var size = 0;
                GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 3, 0);
                if (size <= 0) return;
                var buffer = Marshal.AllocHGlobal(size);
                try
                {
                    var result = GetExtendedTcpTable(buffer, ref size, false, family, 3, 0);
                    if (result == 122) continue;
                    if (result != 0) return;
                    var count = Marshal.ReadInt32(buffer);
                    for (var i = 0; i < count; i++)
                    {
                        var row = IntPtr.Add(buffer, 4 + i * rowSize);
                        var raw = Marshal.ReadInt32(row, portOffset);
                        var localPort = ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);
                        if (localPort == port) ids.Add(Marshal.ReadInt32(row, pidOffset));
                    }
                    return;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private const string LocalOrigin = "https://localhost:10443";
        private const int LocalPort = 10443;
        private const int DefaultRemotePort = 10443;
        private const int BannerHeight = 256;
        private const int SetupBannerHeight = 150;

        private readonly string _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RecRoomCommunityLauncher", "settings.json");

        private string _clientDirectory;
        private string _serverDirectory;

        // Server status panel
        private readonly StatusDot _statusDot = new StatusDot();
        private readonly Label _statusTitle = Theme.Label("Checking…", Theme.F(26, Weight.Medium), Theme.Ink, new Padding(8, 0, 0, 0));
        private readonly Label _statusDetail = Theme.Label("Looking for a RecToon server on this PC.", Theme.F(15, Weight.Regular), Theme.Body, new Padding(0, 12, 0, 0));
        private readonly Label _factAddress = new Label();
        private readonly Label _factItems = new Label();
        private readonly Label _factCdn = new Label();
        private readonly Label _factChecked = new Label();
        private readonly PillButton _startServer = PillButton.Tinted("Start server", Glyph.Play, Theme.GreenTint, Theme.GreenTintHover, Theme.Green);
        private readonly PillButton _stopServer = PillButton.Tinted("Stop server", Glyph.Stop, Theme.RedTint, Theme.RedTintHover, Theme.Red);
        private readonly PillButton _refresh = PillButton.Outlined("Refresh", Glyph.Refresh);
        private readonly PillButton _serverSettings = PillButton.Outlined("Server settings", Glyph.Gear);
        private readonly RichTextBox _activity = new RichTextBox();

        // Join panel
        private readonly Panel _checkingView = new Panel { Dock = DockStyle.Fill };
        private readonly TableLayoutPanel _hostView = Theme.Stack();
        private readonly TableLayoutPanel _joinView = Theme.Stack();
        private readonly PillButton _joinOwn = PillButton.Primary("Start game & join", Glyph.Play, 18, 60, 5);
        private readonly PillButton _joinRemote = PillButton.Primary("Join server", Glyph.Arrow, 18, 60, 5);
        private readonly InputField _serverAddress = new InputField("play.example.com", 16, 50, Theme.Neutral);
        private readonly InputField _port = new InputField("10443", 14, 40, Theme.Neutral);
        private readonly InputField _voiceServer = new InputField("Optional", 14, 40, Theme.Neutral);
        private readonly InputField _voiceServerId = new InputField("Optional", 14, 40, Theme.Neutral);
        private readonly InputField _serviceDomain = new InputField("Same as address", 14, 40, Theme.Neutral);
        private readonly InputField _voiceKey = new InputField("<RSAKeyValue>…</RSAKeyValue>", 14, 40, Theme.Neutral);
        private readonly Label _remoteStatus = Theme.Label("", Theme.F(14, Weight.Regular), Theme.Body, new Padding(0, 8, 0, 0));
        private readonly LinkLabel _optionsToggle = new LinkLabel();
        private readonly TableLayoutPanel _optionsGrid = new TableLayoutPanel();
        private readonly List<Control> _joinIntro = new List<Control>();
        private readonly Label[] _checkTexts = new Label[3];
        private readonly IconTile[] _checkMarks = new IconTile[3];

        private readonly Timer _timer = new Timer();
        private Banner _banner;
        private Control _content;
        private PillButton _settingsButton;
        private SetupView _setup;
        private readonly bool _forceSetup;
        private ServerState _localState = ServerState.Unknown;
        private bool _checking;
        private bool _busy;
        private bool _optionsOpen;

        private static readonly string DefaultClientDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Steam", "steamapps", "content", "app_471710", "depot_471711");
        private static readonly string DefaultServerDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "RecToon 2026", "server");

        public MainForm(bool forceSetup)
        {
            _forceSetup = forceSetup;
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "Rec Room Community Launcher v7";
            Icon = Assets.AppIcon() ?? Icon;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1052, 920);
            MinimumSize = new Size(940, 760);
            Font = Theme.F(15, Weight.Regular);
            BackColor = Theme.Page;
            ForeColor = Theme.Ink;
            Theme.UseBrandTitleBar(this);

            BuildLayout();
            ResumeLayout(false);
            PerformLayout();

            LoadSettings();
            ReadClientIniDefaults();
            UpdateCdnFact();
            ShowJoinView();
            var setupReason = SetupReason();
            if (setupReason != null) ShowSetup(setupReason);

            _timer.Interval = 5000;
            // Start/stop/join report their own progress; a background check would overwrite it.
            _timer.Tick += async (s, e) => { if (!_busy) await RefreshLocalStatus(false); };
            Load += (s, e) => { SizeClientTo(1052, 920); FitToScreen(); };
            Resize += (s, e) => { if (_banner != null) _banner.RefreshCaption(); };
            Shown += async (s, e) => { if (_setup == null) await RefreshLocalStatus(false); };
            FormClosing += (s, e) => SaveSettings();
        }

        // The native caption is removed in WM_NCCALCSIZE, so size by the real non-client frame instead of ClientSize.
        private void SizeClientTo(int width, int height)
        {
            var frameWidth = Width - ClientSize.Width;
            var frameHeight = Height - ClientSize.Height;
            Size = new Size((int)Theme.S(width) + frameWidth, (int)Theme.S(height) + frameHeight);
            CenterToScreen();
        }

        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);

        protected override void WndProc(ref Message m)
        {
            const int WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84;
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                // Keep the default side and bottom resize frame, but give the caption area to the client.
                var top = Marshal.ReadInt32(m.LParam, 4);
                base.WndProc(ref m);
                if (IsZoomed(m.HWnd)) top += GetSystemMetrics(33) + GetSystemMetrics(92);
                Marshal.WriteInt32(m.LParam, 4, top);
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
            if (m.Msg == WM_NCHITTEST && (int)m.Result == 1)
            {
                var raw = m.LParam.ToInt64();
                var point = PointToClient(new Point((short)(raw & 0xFFFF), (short)((raw >> 16) & 0xFFFF)));
                var edge = (int)Theme.S(6);
                if (WindowState != FormWindowState.Maximized && point.Y < edge)
                    m.Result = (IntPtr)(point.X < edge * 2 ? 13 : point.X > ClientSize.Width - edge * 2 ? 14 : 12);
                else if (_banner != null && point.Y < _banner.Bottom)
                    m.Result = (IntPtr)2;
            }
        }

        private void FitToScreen()
        {
            var area = Screen.FromControl(this).WorkingArea;
            if (Height <= area.Height && Width <= area.Width) return;
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        }

        private void BuildLayout()
        {
            var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(28, 24, 28, 28) };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 5f / 11f * 100));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 6f / 11f * 100));
            content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            content.Controls.Add(BuildStatusCard(), 0, 0);
            content.Controls.Add(BuildJoinCard(), 1, 0);
            Controls.Add(content);
            _content = content;

            var banner = new Banner { Dock = DockStyle.Top, Height = BannerHeight + Banner.CaptionHeight };
            _banner = banner;
            var settings = new PillButton("Settings", Glyph.Gear, Theme.F(15, Weight.Medium)) {
                Fill = Color.FromArgb(20, Color.White), HoverFill = Color.FromArgb(41, Color.White),
                Edge = Color.FromArgb(41, Color.White), Ink = Theme.OnDark, IconPx = 18, AutoSize = true, Margin = Padding.Empty
            };
            settings.Click += (s, e) => EditSettings();
            _settingsButton = settings;
            banner.Controls.Add(settings);
            banner.Action = settings;
            Controls.Add(banner);
        }

        private static RoundedPanel Card(Padding padding, Padding margin)
        {
            return new RoundedPanel { Dock = DockStyle.Fill, BackColor = Color.White, BorderColor = Theme.CardEdge, Radius = 18, Padding = padding, Margin = margin };
        }

        private Control BuildStatusCard()
        {
            var card = Card(new Padding(26), new Padding(0, 0, 10, 0));
            var stack = Theme.Stack();
            card.Controls.Add(stack);

            Theme.Add(stack, Theme.Caption("Server status", Padding.Empty));

            var statusRow = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 14, 0, 0) };
            statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _statusDot.Size = new Size(24, 24);
            _statusDot.Margin = new Padding(0, 2, 0, 0);
            _statusDot.Anchor = AnchorStyles.Left;
            _statusTitle.Anchor = AnchorStyles.Left;
            statusRow.Controls.Add(_statusDot, 0, 0);
            statusRow.Controls.Add(_statusTitle, 1, 0);
            Theme.Add(stack, statusRow);
            Theme.Add(stack, _statusDetail);

            var facts = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 20, 0, 0) };
            facts.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            facts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddFact(facts, "Address", _factAddress, "localhost:10443");
            AddFact(facts, "Avatar items", _factItems, "—");
            AddFact(facts, "Clothing CDN", _factCdn, "—");
            AddFact(facts, "Last checked", _factChecked, "—");
            Theme.Add(stack, facts);

            var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 8, 0, 0) };
            _startServer.Visible = false;
            _startServer.Click += async (s, e) => await RunBusy(_startServer, "Starting…", StartLocalServer);
            _stopServer.Visible = false;
            _stopServer.Click += async (s, e) => await RunBusy(_stopServer, "Stopping…", StopLocalServer);
            _refresh.Click += async (s, e) => await RunBusy(_refresh, "Checking…", () => RefreshLocalStatus(true));
            actions.Controls.Add(_startServer);
            actions.Controls.Add(_stopServer);
            actions.Controls.Add(_refresh);
            _serverSettings.Margin = new Padding(0, 0, 10, 0);
            _serverSettings.Click += async (s, e) => await EditServerSettings();
            actions.Controls.Add(_serverSettings);
            Theme.Add(stack, actions);

            Theme.Add(stack, Theme.Caption("Activity", new Padding(0, 26, 0, 10)));
            var log = new RoundedPanel { Dock = DockStyle.Fill, BackColor = Theme.Deep, Radius = 12, Padding = new Padding(14, 12, 6, 12), Margin = Padding.Empty, MinimumSize = new Size(0, 96) };
            _activity.Dock = DockStyle.Fill;
            _activity.ReadOnly = true;
            _activity.BorderStyle = BorderStyle.None;
            _activity.ScrollBars = RichTextBoxScrollBars.Vertical;
            _activity.BackColor = Theme.Deep;
            _activity.ForeColor = Theme.LogText;
            _activity.Font = Theme.F(14, Weight.Regular);
            _activity.DetectUrls = false;
            _activity.TabStop = false;
            _activity.Cursor = Cursors.Arrow;
            Theme.UseDarkScrollBars(_activity);
            log.Controls.Add(_activity);
            Theme.AddFill(stack, log);
            return card;
        }

        private static void AddFact(TableLayoutPanel facts, string name, Label value, string initial)
        {
            var row = facts.RowStyles.Count;
            facts.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = Theme.Label(name, Theme.F(15, Weight.Regular), Theme.Subtle, new Padding(0, 0, 0, 14));
            label.AutoSize = false;
            label.Size = new Size(128, 22);
            label.TextAlign = ContentAlignment.MiddleLeft;
            facts.Controls.Add(label, 0, row);
            value.Text = initial;
            value.AutoSize = false;
            value.Height = 22;
            value.UseMnemonic = false;
            value.TextAlign = ContentAlignment.MiddleLeft;
            value.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            value.Font = Theme.F(15, Weight.Medium);
            value.ForeColor = Theme.Ink;
            value.Margin = new Padding(0, 0, 0, 14);
            facts.Controls.Add(value, 1, row);
        }

        private Control BuildJoinCard()
        {
            var card = Card(new Padding(30, 26, 30, 26), new Padding(10, 0, 0, 0));

            var checking = Theme.Label("Looking for a server on this PC…", Theme.F(16, Weight.Regular), Theme.Body, Padding.Empty);
            checking.Dock = DockStyle.Fill;
            checking.AutoSize = false;
            checking.TextAlign = ContentAlignment.MiddleCenter;
            _checkingView.Controls.Add(checking);

            // Hosting: the local server is up, so the only action is to launch and join it.
            Theme.Add(_hostView, Theme.Caption("Join", Padding.Empty));
            Theme.Add(_hostView, Badge(Glyph.Home, Theme.GreenTint, Theme.GreenIcon));
            Theme.Add(_hostView, Theme.Label("Play on your server", Theme.F(30, Weight.Medium), Theme.Ink, new Padding(0, 18, 0, 0)));
            Theme.Add(_hostView, Theme.Label("Your server is running on this PC. Start Rec Room and you'll join it as soon as the game loads.", Theme.F(16, Weight.Regular), Theme.Body, new Padding(0, 10, 0, 0)));
            Theme.Add(_hostView, BuildChecklist());
            Theme.AddFill(_hostView, new Panel { Margin = Padding.Empty });
            _joinOwn.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _joinOwn.Margin = new Padding(0, 16, 0, 0);
            _joinOwn.Click += async (s, e) => await RunBusy(_joinOwn, "Starting Rec Room…", JoinOwnServer);
            Theme.Add(_hostView, _joinOwn);
            Theme.Add(_hostView, Theme.Label("The first launch may ask for administrator access to set up the local clothing CDN. Friends can only join if your server is reachable from their network.", Theme.F(13, Weight.Regular), Theme.Subtle, new Padding(0, 14, 0, 0)));

            // Not hosting: ask for someone else's server instead.
            Theme.Add(_joinView, Theme.Caption("Join", Padding.Empty));
            var globe = Badge(Glyph.Globe, Theme.GoldTint, Theme.Gold);
            Theme.Add(_joinView, globe);
            Theme.Add(_joinView, Theme.Label("Join a server", Theme.F(30, Weight.Medium), Theme.Ink, new Padding(0, 18, 0, 0)));
            var intro = Theme.Label("No server is running on this PC. Enter the address of the server you want to play on.", Theme.F(16, Weight.Regular), Theme.Body, new Padding(0, 10, 0, 0));
            Theme.Add(_joinView, intro);
            _joinIntro.Add(globe);
            _joinIntro.Add(intro);
            Theme.Add(_joinView, Theme.Label("Server address", Theme.F(14, Weight.Medium), Theme.LabelInk, new Padding(0, 22, 0, 8)));
            Theme.Add(_joinView, _serverAddress);
            _serverAddress.Box.TextChanged += (s, e) => SetRemoteStatus(ServerState.Unknown, "");
            _remoteStatus.Visible = false;
            Theme.Add(_joinView, _remoteStatus);

            _optionsToggle.AutoSize = true;
            _optionsToggle.Margin = new Padding(0, 16, 0, 0);
            _optionsToggle.Font = Theme.F(14, Weight.Medium);
            _optionsToggle.LinkColor = Theme.Green;
            _optionsToggle.ActiveLinkColor = Theme.Hex(0x145B45);
            _optionsToggle.VisitedLinkColor = Theme.Green;
            _optionsToggle.LinkBehavior = LinkBehavior.AlwaysUnderline;
            _optionsToggle.LinkClicked += (s, e) => SetOptionsOpen(!_optionsOpen);
            Theme.Add(_joinView, _optionsToggle);

            _optionsGrid.AutoSize = true;
            _optionsGrid.ColumnCount = 3;
            _optionsGrid.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _optionsGrid.Margin = new Padding(0, 12, 0, 0);
            _optionsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            _optionsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            _optionsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            AddOption("Port", _port, 0, 0, 1);
            AddOption("Voice server", _voiceServer, 1, 0, 1);
            AddOption("Voice server ID", _voiceServerId, 2, 0, 1);
            AddOption("Service domain", _serviceDomain, 0, 1, 1);
            AddOption("Voice public key", _voiceKey, 1, 1, 2);
            Theme.Add(_joinView, _optionsGrid);

            Theme.AddFill(_joinView, new Panel { Margin = Padding.Empty });
            _joinRemote.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _joinRemote.Margin = new Padding(0, 16, 0, 0);
            _joinRemote.Click += async (s, e) => await RunBusy(_joinRemote, "Connecting…", JoinRemoteServer);
            Theme.Add(_joinView, _joinRemote);
            var hostHint = Theme.Label("Want to host instead? Start your server from the status panel.", Theme.F(13, Weight.Regular), Theme.Subtle, new Padding(0, 14, 0, 0));
            Theme.Add(_joinView, hostHint);
            _joinIntro.Add(hostHint);
            _joinView.AutoScroll = true;
            SetOptionsOpen(false);

            card.Controls.Add(_checkingView);
            card.Controls.Add(_hostView);
            card.Controls.Add(_joinView);
            return card;
        }

        private void AddOption(string label, InputField field, int column, int row, int span)
        {
            var last = column + span == _optionsGrid.ColumnCount;
            var cell = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(column == 0 ? 0 : 5, row == 0 ? 0 : 10, last ? 0 : 5, 0) };
            cell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Theme.Add(cell, Theme.Label(label, Theme.F(13, Weight.Medium), Theme.LabelInk, new Padding(0, 0, 0, 6)));
            Theme.Add(cell, field);
            _optionsGrid.Controls.Add(cell, column, row);
            if (span > 1) _optionsGrid.SetColumnSpan(cell, span);
        }

        private void SetOptionsOpen(bool open)
        {
            _optionsOpen = open;
            _optionsToggle.Text = open ? "Hide connection options" : "Connection options…";
            _optionsGrid.Visible = open;
            // The design's card has room for the options only once the intro copy steps aside.
            foreach (var control in _joinIntro) control.Visible = !open;
        }

        private Control BuildChecklist()
        {
            var list = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 22, 0, 0) };
            list.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var i = 0; i < _checkTexts.Length; i++)
            {
                list.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                var gap = i == _checkTexts.Length - 1 ? 0 : 12;
                _checkMarks[i] = new IconTile(Glyph.Check, Theme.GreenTint, Theme.GreenIcon) {
                    Circle = true, IconFraction = 13f / 24f, Size = new Size(24, 24), Margin = new Padding(0, 0, 14, gap), Anchor = AnchorStyles.Left
                };
                _checkTexts[i] = Theme.Label("", Theme.F(15, Weight.Regular), Theme.Ink, new Padding(0, 0, 0, gap));
                _checkTexts[i].Anchor = AnchorStyles.Left | AnchorStyles.Right;
                list.Controls.Add(_checkMarks[i], 0, i);
                list.Controls.Add(_checkTexts[i], 1, i);
            }
            return list;
        }

        private void UpdateChecklist()
        {
            var serverReady = _localState == ServerState.Online;
            SetCheck(0, serverReady ? Glyph.Check : Glyph.Cross, serverReady ? "Server is answering on localhost:10443" : "Server is not answering yet");
            var dir = (_clientDirectory ?? "").Trim();
            var mode = PatchLayout.Detect(dir);
            if (mode == PatchMode.Referee) SetCheck(1, Glyph.Check, "Patched game folder found");
            else if (mode == PatchMode.Legacy) SetCheck(1, Glyph.Alert, "Game uses the older injector patch — Settings → Run setup again switches it");
            else SetCheck(1, Glyph.Cross, File.Exists(Path.Combine(dir, "RecRoom.exe")) ? "Game folder is not patched — Settings → Run setup again" : "Game folder not found — choose it in Settings");
            var steam = Process.GetProcessesByName("steam");
            var steamRunning = steam.Length > 0;
            foreach (var process in steam) process.Dispose();
            SetCheck(2, steamRunning ? Glyph.Check : Glyph.Alert, steamRunning ? "Steam is running" : "Steam is closed — it will be opened for you");
        }

        private void SetCheck(int index, Glyph glyph, string text)
        {
            switch (glyph)
            {
                case Glyph.Check: _checkMarks[index].Set(glyph, Theme.GreenTint, Theme.GreenIcon); _checkTexts[index].ForeColor = Theme.Ink; break;
                case Glyph.Alert: _checkMarks[index].Set(glyph, Theme.GoldTint, Theme.Gold); _checkTexts[index].ForeColor = Theme.Ink; break;
                default: _checkMarks[index].Set(glyph, Theme.RedTint, Theme.Red); _checkTexts[index].ForeColor = Theme.Red; break;
            }
            _checkTexts[index].Text = text;
        }

        private static IconTile Badge(Glyph glyph, Color tint, Color ink)
        {
            return new IconTile(glyph, tint, ink) { Size = new Size(60, 60), Margin = new Padding(0, 20, 0, 0), Anchor = AnchorStyles.Left };
        }

        private void ShowJoinView()
        {
            if (_busy) return;
            var view = _localState == ServerState.Unknown ? (Control)_checkingView
                : _localState == ServerState.Online ? _hostView : _joinView;
            foreach (var candidate in new Control[] { _checkingView, _hostView, _joinView })
                candidate.Visible = candidate == view;
            AcceptButton = view == _hostView ? _joinOwn : view == _joinView ? _joinRemote : null;
        }

        private async Task RunBusy(PillButton button, string busyText, Func<Task> work)
        {
            if (_busy) return;
            _busy = true;
            var text = button.Text;
            button.Text = busyText;
            button.Enabled = false;
            try { await work(); }
            catch (Exception ex) { ShowError(ex.Message); }
            finally
            {
                button.Text = text;
                button.Enabled = true;
                _busy = false;
                ShowJoinView();
            }
        }

        // A server package may bundle node.exe beside "Start Server.cmd"; otherwise use PATH.
        private string NodeExe() { return ServerSetup.NodeFor(_serverDirectory) ?? "node.exe"; }

        // ---------- First-run setup ----------

        // null = no setup needed; "" = fresh setup; other text = why setup is shown again.
        private string SetupReason()
        {
            if (_forceSetup) return "";
            var state = InstallState.Load();
            if (state == null)
            {
                // Launchers from before setup existed: keep a working patched folder, never re-download it.
                if (InstallState.IsPatchedGame(_clientDirectory))
                {
                    var migrated = InstallState.ForExistingGame(InstallPaths.Normalize(_clientDirectory), "existing");
                    var serverReady = ServerSetup.ValidateServerFolder(_serverDirectory) == null;
                    migrated.Server = new ServerStateRecord {
                        Choice = serverReady ? "existing" : "skipped", Path = serverReady ? _serverDirectory : null,
                        Status = serverReady ? "installed" : "notInstalled"
                    };
                    migrated.SetupComplete = true;
                    try { migrated.Save(); }
                    catch (Exception ex) { Log("Could not save the install record: " + ex.Message); }
                    return null;
                }
                return "";
            }
            var game = state.Game != null ? state.Game.Path : null;
            if (!state.SetupComplete) return "Setup was not finished last time. You can pick up where you left off.";
            if (String.IsNullOrEmpty(game) || !File.Exists(Path.Combine(game, "RecRoom.exe")))
                return "Your Rec Room folder" + (String.IsNullOrEmpty(game) ? "" : " (" + game + ")") + " could not be found. Install it again, or choose where it is now.";
            return null;
        }

        private void ShowSetup(string reason)
        {
            if (_setup != null) return;
            _timer.Stop();
            var state = InstallState.Load();
            string resumeGame = null;
            if (!_forceSetup && state != null && !state.SetupComplete && state.Game != null && state.Game.Status == "ready" && InstallState.IsPatchedGame(state.Game.Path))
                resumeGame = state.Game.Path;
            _setup = new SetupView(reason, _serverDirectory, ChooseFolder, Log, FinishSetup, resumeGame);
            _content.Visible = false;
            if (_settingsButton != null) _settingsButton.Visible = false;
            // A shorter banner leaves room for the setup steps.
            _banner.Height = (int)Theme.S(SetupBannerHeight + Banner.CaptionHeight);
            Controls.Add(_setup);
            Controls.SetChildIndex(_setup, Controls.GetChildIndex(_content));
            Log("Setup started.");
        }

        private async void FinishSetup(string gamePath, string serverPath)
        {
            if (!String.IsNullOrEmpty(gamePath)) _clientDirectory = gamePath;
            if (!String.IsNullOrEmpty(serverPath)) _serverDirectory = serverPath;
            SaveSettings();
            if (_setup != null)
            {
                Controls.Remove(_setup);
                _setup.Dispose();
                _setup = null;
            }
            _banner.Height = (int)Theme.S(BannerHeight + Banner.CaptionHeight);
            _content.Visible = true;
            if (_settingsButton != null) _settingsButton.Visible = true;
            Log("Setup complete. Game: " + _clientDirectory + (String.IsNullOrEmpty(serverPath) ? "" : " · Server: " + serverPath));
            UpdateChecklist();
            ShowJoinView();
            await RefreshLocalStatus(false);
        }

        // ---------- Settings ----------

        private void EditSettings()
        {
            using (var dim = new Form {
                FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                BackColor = Theme.Deep, Opacity = 0.45, Bounds = Bounds
            })
            using (var dialog = new SettingsDialog(_clientDirectory, _serverDirectory, ChooseFolder))
            {
                Theme.UseRoundCorners(dim);
                dim.Show(this);
                var result = dialog.ShowDialog(dim);
                dim.Close();
                if (result == DialogResult.Retry) { ShowSetup(""); return; }
                if (result != DialogResult.OK) return;
                _clientDirectory = dialog.Game.Value;
                _serverDirectory = dialog.Server.Value;
                SaveSettings();
                UpdateChecklist();
                Log("Settings saved.");
            }
        }

        private string ChooseFolder(string description, string current)
        {
            using (var dialog = new FolderBrowserDialog { Description = description, SelectedPath = ExistingOrHome(current) })
                return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
        }

        private static string ExistingOrHome(string path) { return Directory.Exists(path) ? path : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }

        private void LoadSettings()
        {
            var settings = new LauncherSettings { ClientDirectory = DefaultClientDirectory, ServerDirectory = DefaultServerDirectory };
            try
            {
                if (File.Exists(_settingsPath)) settings = new JavaScriptSerializer().Deserialize<LauncherSettings>(File.ReadAllText(_settingsPath));
            }
            catch { Log("Saved launcher settings could not be read; using defaults."); }
            _clientDirectory = StringOr(settings.ClientDirectory, DefaultClientDirectory);
            _serverDirectory = StringOr(settings.ServerDirectory, DefaultServerDirectory);
            ApplyRemoteValues(settings.DiscoveryUrl, settings.ServiceDomain, settings.TachyonHost, settings.TachyonServerId, settings.VoiceKeyXml);
        }

        private static string StringOr(string value, string fallback) { return String.IsNullOrWhiteSpace(value) ? fallback : value; }

        private void ReadClientIniDefaults()
        {
            var ini = Path.Combine(_clientDirectory, "2026patch.ini");
            if (!File.Exists(ini)) return;
            var values = ReadIni(ini);
            string url;
            if (!values.TryGetValue("ServerUrl", out url)) return;
            string domain, tachyon, serverId, key;
            values.TryGetValue("ServerDomain", out domain);
            values.TryGetValue("TachyonHost", out tachyon);
            values.TryGetValue("TachyonServerId", out serverId);
            values.TryGetValue("VoiceKeyXml", out key);
            ApplyRemoteValues(url, domain, tachyon, serverId, key);
        }

        // The join panel only remembers other people's servers; a localhost profile is the host flow.
        private void ApplyRemoteValues(string url, string domain, string tachyon, string serverId, string key)
        {
            Uri uri;
            var text = (url ?? "").Trim();
            if (text.Length == 0) return;
            if (text.IndexOf("://", StringComparison.Ordinal) < 0) text = "https://" + text;
            if (!TryServerUri(text, out uri) || IsLoopback(uri.Host)) return;
            _serverAddress.Value = uri.Scheme == Uri.UriSchemeHttps ? uri.Host : uri.Scheme + "://" + uri.Host;
            _port.Value = uri.Port == DefaultRemotePort ? "" : uri.Port.ToString();
            domain = (domain ?? "").Trim().TrimEnd('.');
            _serviceDomain.Value = domain.Equals(uri.Host, StringComparison.OrdinalIgnoreCase) ? "" : domain;
            _voiceServer.Value = tachyon;
            _voiceServerId.Value = serverId;
            _voiceKey.Value = key;
            if (_port.Value.Length > 0 || _voiceServer.Value.Length > 0 || _serviceDomain.Value.Length > 0) SetOptionsOpen(true);
        }

        private void SaveSettings()
        {
            try
            {
                Uri uri;
                string ignored;
                var settings = new LauncherSettings {
                    ClientDirectory = _clientDirectory, ServerDirectory = _serverDirectory,
                    DiscoveryUrl = TryResolveRemote(out uri, out ignored) ? uri.GetLeftPart(UriPartial.Authority) : _serverAddress.Value,
                    ServiceDomain = _serviceDomain.Value.TrimEnd('.'), TachyonHost = _voiceServer.Value,
                    TachyonServerId = _voiceServerId.Value, VoiceKeyXml = _voiceKey.Value
                };
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath));
                File.WriteAllText(_settingsPath, new JavaScriptSerializer().Serialize(settings), new UTF8Encoding(false));
            }
            catch (Exception ex) { Log("Could not save launcher preferences: " + ex.Message); }
        }

        // ---------- Server settings (add-ons) ----------

        private async Task EditServerSettings()
        {
            if (_busy) return;
            var serverDir = (_serverDirectory ?? "").Trim().TrimEnd('\\', '/');
            var problem = ServerSetup.ValidateServerFolder(serverDir);
            if (problem != null)
            {
                ShowError("Server settings need an installed server. " + problem + " Choose your server folder in Settings, or install one with Settings → Run setup again.");
                return;
            }
            bool saved, restart;
            using (var dim = new Form {
                FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                BackColor = Theme.Deep, Opacity = 0.45, Bounds = Bounds
            })
            using (var dialog = new ServerSettingsDialog(serverDir, Log))
            {
                Theme.UseRoundCorners(dim);
                dim.Show(this);
                saved = dialog.ShowDialog(dim) == DialogResult.OK;
                restart = dialog.RestartNeeded;
                dim.Close();
            }
            if (!saved) return;
            Log("Server settings saved.");
            if (!restart) return;
            if (_localState != ServerState.Online)
            {
                Log("Room changes apply the next time the server starts.");
                return;
            }
            var answer = MessageBox.Show(this, "Room changes apply when the server restarts. Restart the server now?\n\nPlayers on it will be disconnected.",
                "Restart server", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) { Log("Restart the server later to apply the room changes."); return; }
            await RunBusy(_serverSettings, "Restarting…", async () => { await StopLocalServer(); await StartLocalServer(); });
        }

        // ---------- Server status ----------

        private async Task RefreshLocalStatus(bool manual)
        {
            if (_checking) return;
            _checking = true;
            _factChecked.Text = "Checking…";
            try { ApplyLocalStatus(await ProbeServer(LocalOrigin), manual); }
            finally
            {
                _checking = false;
                if (!_timer.Enabled) _timer.Start();
            }
        }

        private void ApplyLocalStatus(ProbeResult result, bool announce = false)
        {
            var changed = result.State != _localState;
            _localState = result.State;
            _statusDot.Set(Theme.Dot(result.State));
            switch (result.State)
            {
                case ServerState.Online:
                    _statusTitle.Text = "Server online";
                    _statusDetail.Text = "Your RecToon server is running on this PC and answering requests.";
                    break;
                case ServerState.Degraded:
                    _statusTitle.Text = "Server not ready";
                    _statusDetail.Text = "Something is answering on port 10443, but the RecToon API check failed. " + result.Summary;
                    break;
                default:
                    _statusTitle.Text = "No server running";
                    _statusDetail.Text = "Start your RecToon server to host on this PC, or join someone else's server.";
                    break;
            }
            _factItems.Text = result.Count.HasValue ? result.Count.Value.ToString("N0") : "—";
            _factChecked.Text = DateTime.Now.ToString("HH:mm:ss");
            UpdateCdnFact();
            _startServer.Visible = result.State == ServerState.Offline;
            _stopServer.Visible = result.State == ServerState.Online || result.State == ServerState.Degraded;
            UpdateChecklist();
            if (changed || announce)
            {
                if (result.State == ServerState.Online) Log("Local server is online.");
                else if (result.State == ServerState.Offline) Log("No local server found on port 10443.");
                else Log("Port 10443 is answering, but the API check failed.");
            }
            ShowJoinView();
        }

        private void UpdateCdnFact()
        {
            try { _factCdn.Text = LocalCdnHosts.IsReady() ? "Ready" : "Set up on first join"; }
            catch (Exception ex) { _factCdn.Text = ex is InvalidOperationException ? "Hosts file conflict" : "Unknown"; }
        }

        private async Task<ProbeResult> ProbeServer(string origin)
        {
            Uri serverUri;
            if (!Uri.TryCreate(origin, UriKind.Absolute, out serverUri)) return new ProbeResult(ServerState.Offline, "Invalid server address", null);
            Exception failure;
            try
            {
                var helper = EnsureProbeHelper();
                var arguments = "\"" + helper.Replace("\"", "\\\"") + "\" \"" + origin.TrimEnd('/') + "\"";
                var start = new ProcessStartInfo(NodeExe(), arguments) {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (var process = Process.Start(start))
                {
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();
                    var exited = await Task.Run(() => process.WaitForExit(7000));
                    if (!exited)
                    {
                        try { process.Kill(); } catch (InvalidOperationException) { }
                        return new ProbeResult(ServerState.Offline, "The server check timed out.", null);
                    }
                    var output = await outputTask;
                    var error = await errorTask;
                    if (String.IsNullOrWhiteSpace(output)) return new ProbeResult(ServerState.Degraded, "Server check failed: " + error.Trim(), null);
                    var result = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(output.Trim());
                    if (result.ContainsKey("ok") && Convert.ToBoolean(result["ok"]))
                    {
                        int? count = null;
                        if (result.ContainsKey("count") && result["count"] != null) count = Convert.ToInt32(result["count"]);
                        return new ProbeResult(ServerState.Online, "Online", count);
                    }
                    var status = result.ContainsKey("status") ? Convert.ToString(result["status"]) : "";
                    var detail = result.ContainsKey("error") ? Convert.ToString(result["error"]) : "API response was not valid";
                    if (!String.IsNullOrEmpty(status)) return new ProbeResult(ServerState.Degraded, "The API returned HTTP " + status + ".", null);
                    if (IsUnreachable(detail)) return new ProbeResult(ServerState.Offline, "Nothing is answering at " + serverUri.Authority + ".", null);
                    return new ProbeResult(ServerState.Degraded, detail, null);
                }
            }
            catch (Exception ex) { failure = ex; }
            return await ProbeTcpFallback(serverUri, failure);
        }

        private static bool IsUnreachable(string detail)
        {
            return new[] { "ECONNREFUSED", "ENOTFOUND", "EHOSTUNREACH", "ENETUNREACH", "EAI_AGAIN", "ETIMEDOUT", "timed out" }
                .Any(code => detail.IndexOf(code, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static async Task<ProbeResult> ProbeTcpFallback(Uri serverUri, Exception checkerError)
        {
            var port = serverUri.IsDefaultPort ? (serverUri.Scheme == Uri.UriSchemeHttps ? 443 : 80) : serverUri.Port;
            try
            {
                using (var tcp = new TcpClient())
                {
                    var connect = tcp.ConnectAsync(serverUri.Host, port);
                    if (await Task.WhenAny(connect, Task.Delay(1500)) != connect)
                        return new ProbeResult(ServerState.Offline, "Nothing is answering at " + serverUri.Host + ":" + port + ".", null);
                    await connect;
                    return new ProbeResult(ServerState.Degraded, "Port is open, but the server checker is unavailable (" + checkerError.GetBaseException().Message + ").", null);
                }
            }
            catch (Exception tcpError)
            {
                return new ProbeResult(ServerState.Offline, "Nothing is answering at " + serverUri.Host + ":" + port + " (" + tcpError.GetBaseException().Message + ").", null);
            }
        }

        private string EnsureProbeHelper()
        {
            var path = Path.Combine(Path.GetDirectoryName(_settingsPath), "probe-server.js");
            if (!File.Exists(path))
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RecRoomLauncher.probe-server.js"))
                {
                    if (stream == null) throw new InvalidOperationException("The server check helper is missing from the launcher.");
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (var file = File.Create(path)) stream.CopyTo(file);
                }
            }
            return path;
        }

        private async Task StartLocalServer()
        {
            var serverDir = (_serverDirectory ?? "").Trim().TrimEnd('\\', '/');
            if (!File.Exists(Path.Combine(serverDir, "server.js")))
                throw new InvalidOperationException("The server folder must contain server.js. Choose it in Settings.");
            var parent = Path.GetDirectoryName(serverDir);
            var serverCommand = Path.Combine(parent, "Start Server.cmd");
            if (!File.Exists(serverCommand)) throw new InvalidOperationException("Start Server.cmd was not found beside the selected server folder.");
            Process.Start(new ProcessStartInfo(serverCommand) { WorkingDirectory = parent, UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal });
            Log("Opened Start Server.cmd in a console window.");

            _statusDot.Set(Theme.DotWarning);
            _statusTitle.Text = "Starting server…";
            _statusDetail.Text = "Waiting for the RecToon API to answer on port 10443.";
            // The server verifies every archived clothing bundle before it listens, which can
            // take well over a minute with a large archive.
            var waited = Stopwatch.StartNew();
            ProbeResult status;
            do
            {
                await Task.Delay(1000);
                _statusDetail.Text = "Waiting for the RecToon API to answer on port 10443 (" + (int)waited.Elapsed.TotalSeconds +
                    "s). A large clothing archive makes the first start slow.";
                status = await ProbeServer(LocalOrigin);
            } while (status.State != ServerState.Online && waited.Elapsed < TimeSpan.FromMinutes(3));
            ApplyLocalStatus(status);
            if (status.State != ServerState.Online)
                throw new InvalidOperationException("The server did not answer within 3 minutes. Check its console window for errors. " + status.Summary);
        }

        private async Task StopLocalServer()
        {
            var ids = PortOwner.ListeningProcessIds(LocalPort);
            if (ids.Count == 0)
            {
                ApplyLocalStatus(await ProbeServer(LocalOrigin));
                return;
            }
            var targets = new List<Process>();
            try
            {
                foreach (var id in ids)
                {
                    Process process;
                    try { process = Process.GetProcessById(id); }
                    catch (ArgumentException) { continue; }
                    targets.Add(process);
                    if (!process.ProcessName.Equals("node", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Port 10443 is held by " + process.ProcessName + ".exe (PID " + id + "), not the RecToon server. Close that program yourself.");
                }
                foreach (var process in targets)
                {
                    try
                    {
                        process.Kill();
                        await Task.Run(() => process.WaitForExit(5000));
                    }
                    catch (Win32Exception ex)
                    {
                        throw new InvalidOperationException("Windows would not let the launcher stop the server (PID " + process.Id + "): " + ex.Message + ". Close its console window instead.");
                    }
                    catch (InvalidOperationException) { }
                }
            }
            finally { foreach (var process in targets) process.Dispose(); }

            ProbeResult status = null;
            for (var i = 0; i < 10; i++)
            {
                status = await ProbeServer(LocalOrigin);
                if (status.State == ServerState.Offline) break;
                await Task.Delay(300);
            }
            var wasOnline = _localState != ServerState.Offline;
            ApplyLocalStatus(status);
            if (status.State == ServerState.Offline && wasOnline) Log("Local server stopped.");
        }

        // ---------- Joining ----------

        private async Task JoinOwnServer()
        {
            var status = await ProbeServer(LocalOrigin);
            ApplyLocalStatus(status);
            if (status.State != ServerState.Online)
                throw new InvalidOperationException("Your server stopped answering. " + status.Summary);
            await EnsureLocalCdn();
            SaveNetworkSettings(LocalOrigin, "localhost", "", "", LocalRoomServerKey());
            if (LaunchGame()) Log("Joining localhost:10443.");
        }

        // The local server's room transport (Tachyon) keeps its RSA public key in
        // data/tachyon-public.xml; the client must encrypt its approval keys with it.
        private string LocalRoomServerKey()
        {
            var file = Path.Combine((_serverDirectory ?? "").Trim(), "data", "tachyon-public.xml");
            if (!File.Exists(file)) return "";
            var key = File.ReadAllText(file).Trim();
            try { ValidatePublicKey(key); }
            catch (InvalidOperationException ex)
            {
                Log("Ignoring the local room server key: " + ex.Message);
                return "";
            }
            Log("Using the local room server's public key for voice/room connections.");
            return key;
        }

        private async Task EnsureLocalCdn()
        {
            if (!LocalCdnHosts.IsReady())
            {
                Log("Setting up the local clothing CDN. Windows will ask for administrator access to add cdn.localhost.");
                var setup = new ProcessStartInfo(Application.ExecutablePath, "--setup-local-cdn") {
                    UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var helper = Process.Start(setup))
                {
                    await Task.Run(() => helper.WaitForExit());
                    if (helper.ExitCode != 0) throw new InvalidOperationException("The local CDN hostname setup did not finish.");
                }
                if (!LocalCdnHosts.IsReady()) throw new InvalidOperationException("cdn.localhost is not resolving to this PC after setup.");
                UpdateCdnFact();
            }
            await CheckLocalCdn();
        }

        private async Task CheckLocalCdn()
        {
            string directory = Path.GetDirectoryName(_settingsPath);
            Directory.CreateDirectory(directory);
            string script = Path.Combine(directory, "probe-cdn-v4.js");
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RecRoomLauncher.probe-cdn.js"))
            {
                if (stream == null) throw new InvalidOperationException("The CDN checker is missing from the launcher.");
                using (var file = File.Create(script)) stream.CopyTo(file);
            }
            var start = new ProcessStartInfo(NodeExe(), "\"" + script + "\"") {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var process = Process.Start(start))
            {
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                bool exited = await Task.Run(() => process.WaitForExit(12000));
                if (!exited) { process.Kill(); throw new InvalidOperationException("The local clothing CDN check timed out."); }
                string output = await outputTask;
                string error = await errorTask;
                if (process.ExitCode != 0) throw new InvalidOperationException("Local clothing CDN is not ready. " + output.Trim() + " " + error.Trim());
                Log("Local HTTPS clothing CDN is ready.");
            }
        }

        private async Task JoinRemoteServer()
        {
            Uri uri;
            string problem;
            if (!TryResolveRemote(out uri, out problem))
            {
                SetRemoteStatus(ServerState.Offline, problem);
                Log(problem);
                if (_serverAddress.Value.Length == 0) _serverAddress.Box.Focus();
                return;
            }
            if (IsLoopback(uri.Host))
            {
                SetRemoteStatus(ServerState.Offline, "That address is this PC. Start your server from the status panel to host and join it.");
                return;
            }
            var origin = uri.GetLeftPart(UriPartial.Authority);
            var domain = _serviceDomain.Value.Length == 0 ? uri.Host : _serviceDomain.Value.TrimEnd('.');
            ValidateRemoteSettings(uri, domain);

            SetRemoteStatus(ServerState.Unknown, "Checking " + uri.Authority + "…");
            var result = await ProbeServer(origin);
            if (result.State == ServerState.Online)
                SetRemoteStatus(ServerState.Online, "Server online" + (result.Count.HasValue ? " · " + result.Count.Value.ToString("N0") + " avatar items" : ""));
            else
            {
                SetRemoteStatus(result.State, result.Summary);
                var answer = MessageBox.Show(this, uri.Authority + " didn't pass the server check.\r\n\r\n" + result.Summary + "\r\n\r\nLaunch Rec Room and try to join anyway?",
                    "Join server", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
                if (answer != DialogResult.Yes) return;
            }

            SaveNetworkSettings(origin, domain, _voiceServer.Value, _voiceServerId.Value, _voiceKey.Value);
            if (LaunchGame()) Log("Joining " + uri.Authority + ".");
        }

        // Address wins if it names a port; otherwise the Port option, otherwise RecToon's default 10443.
        private bool TryResolveRemote(out Uri uri, out string problem)
        {
            uri = null;
            problem = null;
            var text = _serverAddress.Value;
            if (text.Length == 0) { problem = "Enter a server address to join."; return false; }
            if (text.IndexOf("://", StringComparison.Ordinal) < 0) text = "https://" + text;
            Uri parsed;
            if (!TryServerUri(text, out parsed)) { problem = "Enter a server address like play.example.com."; return false; }
            var authority = text.Substring(text.IndexOf("://", StringComparison.Ordinal) + 3).TrimEnd('/');
            var explicitPort = Regex.IsMatch(authority, @"^(\[[^\]]+\]|[^:\[\]]+):\d+$");
            int port = DefaultRemotePort;
            if (explicitPort) port = parsed.Port;
            else if (_port.Value.Length > 0 && (!Int32.TryParse(_port.Value, out port) || port < 1 || port > 65535))
            {
                problem = "Port must be a number from 1 to 65535.";
                return false;
            }
            uri = new UriBuilder(parsed.Scheme, parsed.Host, port).Uri;
            return true;
        }

        private void SetRemoteStatus(ServerState state, string text)
        {
            _remoteStatus.ForeColor = state == ServerState.Online ? Theme.Green : state == ServerState.Unknown ? Theme.Body : Theme.Red;
            _remoteStatus.Text = text ?? "";
            _remoteStatus.Visible = !String.IsNullOrEmpty(text);
        }

        private static bool IsLoopback(string host)
        {
            return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host == "127.0.0.1" || host == "[::1]";
        }

        private void ValidateRemoteSettings(Uri uri, string domain)
        {
            if (uri.Host != domain && !uri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The service domain in Connection options must match the server address or be its parent domain.");
            if (_voiceServer.Value.Length > 0 && _voiceKey.Value.Length == 0)
                throw new InvalidOperationException("A voice server requires the operator's RSA public key XML. Add it under Connection options.");
            if (_voiceKey.Value.Length > 0) ValidatePublicKey(_voiceKey.Value);
        }

        private static bool TryServerUri(string text, out Uri uri)
        {
            return Uri.TryCreate(text.Trim(), UriKind.Absolute, out uri) &&
                (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
                uri.AbsolutePath == "/" && String.IsNullOrEmpty(uri.Query) && String.IsNullOrEmpty(uri.Fragment) && String.IsNullOrEmpty(uri.UserInfo);
        }

        private static void ValidatePublicKey(string xmlText)
        {
            var doc = new System.Xml.XmlDocument();
            doc.XmlResolver = null;
            try { doc.LoadXml(xmlText); }
            catch (System.Xml.XmlException) { throw new InvalidOperationException("The voice public key is not valid XML."); }
            var root = doc.DocumentElement;
            if (root == null || root.Name != "RSAKeyValue" || root.Attributes.Count != 0 || root.ChildNodes.Count != 2 ||
                root["Modulus"] == null || root["Exponent"] == null || root["Modulus"].Attributes.Count != 0 || root["Exponent"].Attributes.Count != 0)
                throw new InvalidOperationException("Voice key must be public RSA XML containing only Modulus and Exponent.");
            try
            {
                Convert.FromBase64String(root["Modulus"].InnerText);
                Convert.FromBase64String(root["Exponent"].InnerText);
            }
            catch (FormatException) { throw new InvalidOperationException("The voice key's Modulus and Exponent must be base64."); }
        }

        private void SaveNetworkSettings(string serverUrl, string domain, string tachyonHost, string tachyonServerId, string voiceKey)
        {
            var clientDir = (_clientDirectory ?? "").Trim();
            var ini = Path.Combine(clientDir, "2026patch.ini");
            if (PatchLayout.Detect(clientDir) == PatchMode.None)
                throw new InvalidOperationException("The game folder is not patched. Choose your patched Rec Room folder in Settings, or patch it with Settings → Run setup again.");
            if (!File.Exists(ini)) throw new InvalidOperationException("2026patch.ini is missing from the selected game folder.");
            SetIniValues(ini, new Dictionary<string, string> {
                { "ServerUrl", serverUrl }, { "ServerDomain", domain },
                { "TachyonHost", tachyonHost }, { "TachyonServerId", tachyonServerId },
                { "VoiceKeyXml", voiceKey }
            });
            SaveSettings();
        }

        private bool LaunchGame()
        {
            var steamProcesses = Process.GetProcessesByName("steam");
            var steamRunning = steamProcesses.Length > 0;
            foreach (var process in steamProcesses) process.Dispose();
            if (!steamRunning)
            {
                var steamExe = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamExe", null) as string;
                if (String.IsNullOrEmpty(steamExe) || !File.Exists(steamExe))
                    steamExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe");
                if (!File.Exists(steamExe))
                    throw new InvalidOperationException("Open Steam and sign in before starting Rec Room. Steam.exe could not be located.");
                Process.Start(new ProcessStartInfo(steamExe) {
                    WorkingDirectory = Path.GetDirectoryName(steamExe),
                    UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal
                });
                Log("Opened Steam. Finish signing in, then press the join button again.");
                MessageBox.Show(this, "Steam was closed, so it has been opened. Wait for Steam to finish loading and sign in if prompted, then press the join button again.",
                    "Steam is starting", MessageBoxButtons.OK, MessageBoxIcon.Information);
                UpdateChecklist();
                return false;
            }
            var dir = _clientDirectory.Trim();
            if (PatchLayout.Detect(dir) == PatchMode.Legacy)
            {
                // Older injector installs start through the patch's own script.
                Process.Start(new ProcessStartInfo(Path.Combine(dir, "RecRoomScreen.bat")) { WorkingDirectory = dir, UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal });
                Log("Started Rec Room with the older injector patch (RecRoomScreen.bat).");
                return true;
            }
            // Referee layout: Windows loads the community Referee.dll with the game; no injector or script.
            Process.Start(new ProcessStartInfo(Path.Combine(dir, "RecRoom.exe"), PatchPackage.LaunchArguments) { WorkingDirectory = dir, UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal });
            Log("Started RecRoom.exe. The first window can take a while to appear.");
            return true;
        }

        private static Dictionary<string, string> ReadIni(string path)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var section = "";
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) section = line.Substring(1, line.Length - 2);
                else if (section.Equals("Network", StringComparison.OrdinalIgnoreCase) && !line.StartsWith(";") && line.Contains("="))
                {
                    var split = line.IndexOf('=');
                    values[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
                }
            }
            return values;
        }

        private static void SetIniValues(string path, Dictionary<string, string> values)
        {
            var lines = File.ReadAllLines(path).ToList();
            var section = "";
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var insertAt = -1;
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    if (section.Equals("Network", StringComparison.OrdinalIgnoreCase) && insertAt < 0) insertAt = i;
                    section = line.Substring(1, line.Length - 2);
                    continue;
                }
                if (!section.Equals("Network", StringComparison.OrdinalIgnoreCase) || line.StartsWith(";") || !line.Contains("=")) continue;
                var split = line.IndexOf('=');
                var key = line.Substring(0, split).Trim();
                if (values.ContainsKey(key)) { lines[i] = key + "=" + values[key]; seen.Add(key); }
            }
            if (section.Equals("Network", StringComparison.OrdinalIgnoreCase) && insertAt < 0) insertAt = lines.Count;
            var missing = values.Where(kv => !seen.Contains(kv.Key)).Select(kv => kv.Key + "=" + kv.Value).ToList();
            if (insertAt < 0) { lines.Add(""); lines.Add("[Network]"); lines.AddRange(missing); }
            else lines.InsertRange(insertAt, missing);
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }

        private void Log(string message)
        {
            _activity.SelectionStart = _activity.TextLength;
            _activity.SelectionLength = 0;
            _activity.SelectionColor = Theme.Mint;
            _activity.AppendText((_activity.TextLength > 0 ? "\n" : "") + DateTime.Now.ToString("HH:mm:ss") + "   ");
            _activity.SelectionColor = Theme.LogText;
            _activity.AppendText(message);
            _activity.SelectionStart = _activity.TextLength;
            _activity.ScrollToCaret();
        }

        private void ShowError(string message)
        {
            Log("Error: " + message);
            MessageBox.Show(this, message, "Rec Room Community Launcher", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--setup-local-cdn")
            {
                try { LocalCdnHosts.Install(); return 0; }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Local clothing CDN setup", MessageBoxButtons.OK, MessageBoxIcon.Warning); return 1; }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args.Any(a => a.Equals("--setup", StringComparison.OrdinalIgnoreCase))));
            return 0;
        }
    }
}
