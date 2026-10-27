using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PaintDark
{
    /// <summary>The dark palette. Every surface of the app pulls its colors from here.</summary>
    public static class Theme
    {
        public static readonly Color Caption = C(0x202020);
        public static readonly Color CaptionInactive = C(0x2B2B2B);
        public static readonly Color CaptionText = C(0xE8E8E8);
        public static readonly Color CaptionTextInactive = C(0x8A8A8A);
        public static readonly Color CloseHover = C(0xC42B1C);
        public static readonly Color CloseDown = C(0x9E2418);
        public static readonly Color CaptionBtnHover = C(0x383838);
        public static readonly Color CaptionBtnDown = C(0x4A4A4A);

        public static readonly Color TabRow = C(0x202020);
        public static readonly Color Ribbon = C(0x2B2B2B);
        public static readonly Color RibbonBorder = C(0x3C3C3C);
        public static readonly Color GroupSep = C(0x434343);
        public static readonly Color GroupLabel = C(0x9C9C9C);
        public static readonly Color Text = C(0xE6E6E6);
        public static readonly Color TextDim = C(0xA6A6A6);
        public static readonly Color TextDisabled = C(0x6A6A6A);

        public static readonly Color Hover = C(0x3A3A3A);
        public static readonly Color HoverBorder = C(0x505050);
        public static readonly Color Pressed = C(0x474747);
        public static readonly Color Checked = C(0x1E3A5A);
        public static readonly Color CheckedBorder = C(0x3B7BC4);
        public static readonly Color CheckedHover = C(0x25476D);

        public static readonly Color Accent = C(0x2B6FC2);
        public static readonly Color AccentHover = C(0x3A80D4);
        public static readonly Color AccentDown = C(0x1F5BA3);
        public static readonly Color AccentText = C(0xFFFFFF);
        public static readonly Color Focus = C(0x4C9AEE);

        public static readonly Color Workspace = C(0x1B1B1B);
        public static readonly Color CanvasBorder = C(0x3E3E3E);
        public static readonly Color CanvasShadow = C(0x0E0E0E);
        public static readonly Color Handle = C(0xDADADA);
        public static readonly Color HandleBorder = C(0x202020);
        public static readonly Color Ruler = C(0x252525);
        public static readonly Color RulerTick = C(0x8C8C8C);
        public static readonly Color RulerText = C(0xA8A8A8);
        public static readonly Color RulerMark = C(0x4C9AEE);

        public static readonly Color Status = C(0x202020);
        public static readonly Color StatusBorder = C(0x343434);
        public static readonly Color StatusText = C(0xCFCFCF);

        public static readonly Color Popup = C(0x2B2B2B);
        public static readonly Color PopupBorder = C(0x4A4A4A);
        public static readonly Color PopupHover = C(0x3D3D3D);
        public static readonly Color PopupHeader = C(0x333333);
        public static readonly Color MenuSep = C(0x444444);

        public static readonly Color Dialog = C(0x202020);
        public static readonly Color DialogPanel = C(0x2B2B2B);
        public static readonly Color Input = C(0x161616);
        public static readonly Color InputBorder = C(0x555555);
        public static readonly Color InputBorderHover = C(0x707070);
        public static readonly Color Button = C(0x333333);
        public static readonly Color ButtonBorder = C(0x4D4D4D);

        public static readonly Color ScrollTrack = C(0x1E1E1E);
        public static readonly Color ScrollThumb = C(0x4A4A4A);
        public static readonly Color ScrollThumbHover = C(0x666666);
        public static readonly Color ScrollThumbDown = C(0x7A7A7A);
        public static readonly Color ScrollArrow = C(0x9A9A9A);

        public static readonly Color Tooltip = C(0x2B2B2B);
        public static readonly Color TooltipBorder = C(0x555555);

        public static readonly Color ContextTab = C(0x6B5A1E);
        public static readonly Color ContextTabText = C(0xF2D27A);

        public static Color C(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

        // ---------- fonts (pixel based so they are DPI independent) ----------
        static readonly Dictionary<long, Font> _fonts = new Dictionary<long, Font>();
        public static Font UiFont(int dpi) => GetFont(dpi, 12f, FontStyle.Regular);
        public static Font UiFontBold(int dpi) => GetFont(dpi, 12f, FontStyle.Bold);
        public static Font SmallFont(int dpi) => GetFont(dpi, 11f, FontStyle.Regular);
        public static Font TitleFont(int dpi) => GetFont(dpi, 12f, FontStyle.Regular);
        public static Font BigFont(int dpi) => GetFont(dpi, 20f, FontStyle.Regular);
        public static Font GetFont(int dpi, float px, FontStyle style)
        {
            long key = ((long)dpi << 32) | ((long)(px * 10) << 8) | (long)style;
            if (!_fonts.TryGetValue(key, out var f))
            {
                f = new Font("Segoe UI", px * dpi / 96f, style, GraphicsUnit.Pixel);
                _fonts[key] = f;
            }
            return f;
        }

        // ---------- cached GDI+ objects ----------
        static readonly Dictionary<int, SolidBrush> _brushes = new Dictionary<int, SolidBrush>();
        static readonly Dictionary<long, Pen> _pens = new Dictionary<long, Pen>();
        public static SolidBrush Brush(Color c)
        {
            int k = c.ToArgb();
            if (!_brushes.TryGetValue(k, out var b)) { b = new SolidBrush(c); _brushes[k] = b; }
            return b;
        }
        public static Pen Pen(Color c, float w = 1f)
        {
            long k = ((long)c.ToArgb() << 16) ^ (long)(w * 64);
            if (!_pens.TryGetValue(k, out var p)) { p = new Pen(c, w); _pens[k] = p; }
            return p;
        }

        public static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
    }

    /// <summary>Scaling helper bound to a DPI value.</summary>
    public struct Dpi
    {
        public readonly int Value;
        public Dpi(int v) { Value = v <= 0 ? 96 : v; }
        public int S(int v) => (int)Math.Round(v * Value / 96.0);
        public float F(float v) => v * Value / 96f;
        public float Scale => Value / 96f;
        public static implicit operator int(Dpi d) => d.Value;
    }

    /// <summary>Small drawing helpers shared by every custom-drawn surface.</summary>
    public static class Draw
    {
        public const TextFormatFlags TextLeft = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        public const TextFormatFlags TextCenter = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        public const TextFormatFlags TextRight = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        public static void Fill(Graphics g, Rectangle r, Color c) => g.FillRectangle(Theme.Brush(c), r);
        public static void Rect(Graphics g, Rectangle r, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            g.DrawRectangle(Theme.Pen(c), r.X, r.Y, r.Width - 1, r.Height - 1);
        }
        public static void HLine(Graphics g, int x0, int x1, int y, Color c) => g.FillRectangle(Theme.Brush(c), x0, y, x1 - x0, 1);
        public static void VLine(Graphics g, int x, int y0, int y1, Color c) => g.FillRectangle(Theme.Brush(c), x, y0, 1, y1 - y0);

        public static void Text(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags flags = TextLeft)
        {
            if (string.IsNullOrEmpty(s)) return;
            TextRenderer.DrawText(g, s, f, r, c, flags | TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.PreserveGraphicsClipping);
        }

        public static Size Measure(string s, Font f, TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine)
        {
            if (string.IsNullOrEmpty(s)) return new Size(0, f.Height);
            return TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue), flags);
        }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            if (rad <= 0.01f) { p.AddRectangle(r); return p; }
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// <summary>Standard button chrome used by ribbon, menus and dialogs.</summary>
        public static void ButtonFace(Graphics g, Rectangle r, bool hover, bool pressed, bool isChecked, bool enabled = true)
        {
            if (!enabled && !isChecked) return;
            Color bg, border;
            if (isChecked)
            {
                bg = pressed ? Theme.Pressed : hover ? Theme.CheckedHover : Theme.Checked;
                border = Theme.CheckedBorder;
            }
            else if (pressed) { bg = Theme.Pressed; border = Theme.HoverBorder; }
            else if (hover) { bg = Theme.Hover; border = Theme.HoverBorder; }
            else return;
            Fill(g, r, bg);
            Rect(g, r, border);
        }

        public static void Chevron(Graphics g, int cx, int cy, Color c, Dpi d, bool up = false)
        {
            // small filled down-triangle, like the ribbon's dropdown arrows
            int w = Math.Max(5, d.S(5)) | 1;
            int h = (w + 1) / 2;
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            for (int i = 0; i < h; i++)
            {
                int yy = up ? cy + h / 2 - i : cy - h / 2 + i;
                g.FillRectangle(Theme.Brush(c), cx - w / 2 + i, yy, w - 2 * i, 1);
            }
            g.SmoothingMode = old;
        }

        public static void RightArrow(Graphics g, int cx, int cy, Color c, Dpi d)
        {
            int h = Math.Max(7, d.S(7)) | 1;
            int w = (h + 1) / 2;
            for (int i = 0; i < w; i++)
                g.FillRectangle(Theme.Brush(c), cx - w / 2 + i, cy - h / 2 + i, 1, h - 2 * i);
        }

        public static void CheckMark(Graphics g, Rectangle r, Color c, Dpi d)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = Math.Min(r.Width, r.Height);
            float x = r.X + (r.Width - s) / 2f, y = r.Y + (r.Height - s) / 2f;
            using (var p = new Pen(c, Math.Max(1.4f, d.F(1.6f))))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                g.DrawLines(p, new[] { new PointF(x + s * 0.18f, y + s * 0.52f), new PointF(x + s * 0.42f, y + s * 0.75f), new PointF(x + s * 0.84f, y + s * 0.26f) });
            }
            g.SmoothingMode = old;
        }

        /// <summary>Checkerboard used behind transparent pixels and empty color slots.</summary>
        public static void Checker(Graphics g, Rectangle r, int cell)
        {
            Fill(g, r, Color.FromArgb(0x5A, 0x5A, 0x5A));
            var b = Theme.Brush(Color.FromArgb(0x3A, 0x3A, 0x3A));
            var st = g.Save();
            g.SetClip(r);
            for (int y = r.Top; y < r.Bottom; y += cell)
                for (int x = r.Left + (((y - r.Top) / cell) & 1) * cell; x < r.Right; x += cell * 2)
                    g.FillRectangle(b, x, y, cell, cell);
            g.Restore(st);
        }
    }
}
