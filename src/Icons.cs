using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace PaintDark
{
    public enum Ico
    {
        None,
        // small
        Cut, Copy, Crop, Resize, Rotate, Pencil, Fill, Text, Eraser, Picker, Magnifier, Outline, ShapeFill,
        Save, Undo, Redo, PasteSmall, PasteFrom,
        RotRight, RotLeft, Rot180, FlipV, FlipH,
        SelRect, SelFree, SelAll, SelInvert, SelDelete, SelTransparent, InvertColor,
        Bold, Italic, Underline, Strike, Opaque, TransparentBg,
        StCursor, StSelection, StCanvas, StFile, Pin, Pinned, Help, AppIcon,
        // large
        Paste, Select, Size, EditColors, ZoomIn, ZoomOut, Zoom100, FullScreen, Thumbnail,
        New, Open, SaveBig, SaveAs, Print, PageSetup, PrintPreview, Scanner, Email, Desktop, Properties, About, Exit,
        PicPng, PicJpeg, PicBmp, PicGif, PicOther, DeskFill, DeskTile, DeskCenter,
        OnePage, TwoPages, NextPage, PrevPage, ClosePreview, Shapes, Tools, Colors, Rulers, Gridlines, StatusBarIco, DarkCanvas,
        BrushBrush, BrushCalli1, BrushCalli2, BrushAir, BrushOil, BrushCrayon, BrushMarker, BrushPencil, BrushWater,
        // shapes (small)
        ShLine, ShCurve, ShOval, ShRect, ShRoundRect, ShPolygon, ShTriangle, ShRightTriangle, ShDiamond, ShPentagon, ShHexagon,
        ShRightArrow, ShLeftArrow, ShUpArrow, ShDownArrow, ShStar4, ShStar5, ShStar6, ShRoundCallout, ShOvalCallout, ShCloudCallout, ShHeart, ShLightning,
        TexNone, TexSolid, TexCrayon, TexMarker, TexOil, TexPencil, TexWater,
    }

    /// <summary>Renders and caches every icon as a premultiplied bitmap at the exact pixel size requested.</summary>
    public static class Icons
    {
        static readonly Dictionary<long, Bitmap> _cache = new Dictionary<long, Bitmap>();

        public static Bitmap Get(Ico id, int px)
        {
            long key = ((long)id << 16) | (uint)px;
            if (_cache.TryGetValue(key, out var b)) return b;
            b = new Bitmap(px, px, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                float grid = IsLarge(id) ? 32f : 16f;
                g.ScaleTransform(px / grid, px / grid);
                try { Paint(g, id); } catch { }
            }
            _cache[key] = b;
            return b;
        }

        public static void DrawIcon(Graphics g, Ico id, int x, int y, int px, bool enabled = true)
        {
            if (id == Ico.None) return;
            var bmp = Get(id, px);
            if (enabled) g.DrawImage(bmp, x, y, px, px);
            else
            {
                using (var ia = new ImageAttributes())
                {
                    var cm = new ColorMatrix(new[]
                    {
                        new float[] { 0.3f, 0.3f, 0.3f, 0, 0 }, new float[] { 0.59f, 0.59f, 0.59f, 0, 0 }, new float[] { 0.11f, 0.11f, 0.11f, 0, 0 },
                        new float[] { 0, 0, 0, 0.35f, 0 }, new float[] { 0, 0, 0, 0, 1 }
                    });
                    ia.SetColorMatrix(cm);
                    g.DrawImage(bmp, new Rectangle(x, y, px, px), 0, 0, px, px, GraphicsUnit.Pixel, ia);
                }
            }
        }

        public static bool IsLarge(Ico id) => id >= Ico.Paste && id <= Ico.BrushWater;

        // ---------------------------------------------------------------- palette
        static readonly Color Ln = Theme.C(0xD4D4D4);     // neutral stroke on dark
        static readonly Color LnDim = Theme.C(0x9A9A9A);
        static readonly Color Paper = Theme.C(0xF2F2F2);
        static readonly Color PaperEdge = Theme.C(0xA9A9A9);
        static readonly Color Blue = Theme.C(0x4BA3F2);
        static readonly Color DBlue = Theme.C(0x2D6FC0);
        static readonly Color Yellow = Theme.C(0xF4C443);
        static readonly Color Orange = Theme.C(0xF08A3C);
        static readonly Color Red = Theme.C(0xE5534B);
        static readonly Color Green = Theme.C(0x57B85A);
        static readonly Color Purple = Theme.C(0xA36BDB);
        static readonly Color Pink = Theme.C(0xF2A0B6);
        static readonly Color Brown = Theme.C(0xB98250);
        static readonly Color Dark = Theme.C(0x303030);
        static readonly Color Sky = Theme.C(0x6FC3F7);

        static Pen P(Color c, float w = 1f) => new Pen(c, w) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        static Pen PSq(Color c, float w = 1f) => new Pen(c, w) { LineJoin = LineJoin.Miter };
        static SolidBrush B(Color c) => new SolidBrush(c);

        static void Line(Graphics g, Color c, float w, float x0, float y0, float x1, float y1) { using (var p = P(c, w)) g.DrawLine(p, x0, y0, x1, y1); }
        static void Poly(Graphics g, Color fill, Color stroke, float w, params float[] pts)
        {
            var ps = new PointF[pts.Length / 2];
            for (int i = 0; i < ps.Length; i++) ps[i] = new PointF(pts[i * 2], pts[i * 2 + 1]);
            if (fill.A > 0) using (var b = B(fill)) g.FillPolygon(b, ps);
            if (stroke.A > 0 && w > 0) using (var p = P(stroke, w)) g.DrawPolygon(p, ps);
        }
        static void Lines(Graphics g, Color stroke, float w, params float[] pts)
        {
            var ps = new PointF[pts.Length / 2];
            for (int i = 0; i < ps.Length; i++) ps[i] = new PointF(pts[i * 2], pts[i * 2 + 1]);
            using (var p = P(stroke, w)) g.DrawLines(p, ps);
        }
        static void RectF(Graphics g, Color fill, Color stroke, float w, float x, float y, float rw, float rh)
        {
            if (fill.A > 0) using (var b = B(fill)) g.FillRectangle(b, x, y, rw, rh);
            if (stroke.A > 0 && w > 0) using (var p = PSq(stroke, w)) g.DrawRectangle(p, x, y, rw, rh);
        }
        static void Ellipse(Graphics g, Color fill, Color stroke, float w, float x, float y, float rw, float rh)
        {
            if (fill.A > 0) using (var b = B(fill)) g.FillEllipse(b, x, y, rw, rh);
            if (stroke.A > 0 && w > 0) using (var p = P(stroke, w)) g.DrawEllipse(p, x, y, rw, rh);
        }
        static void RoundRect(Graphics g, Color fill, Color stroke, float w, float x, float y, float rw, float rh, float r)
        {
            using (var path = Draw.RoundRect(new RectangleF(x, y, rw, rh), r))
            {
                if (fill.A > 0) using (var b = B(fill)) g.FillPath(b, path);
                if (stroke.A > 0 && w > 0) using (var p = P(stroke, w)) g.DrawPath(p, path);
            }
        }

        static void Page(Graphics g, float x, float y, float w, float h, float fold, Color fill, Color edge, float lw)
        {
            Poly(g, fill, edge, lw, x, y, x + w - fold, y, x + w, y + fold, x + w, y + h, x, y + h);
            Poly(g, Theme.C(0xCFCFCF), edge, lw, x + w - fold, y, x + w - fold, y + fold, x + w, y + fold);
        }

        static void Picture(Graphics g, float x, float y, float w, float h, float lw)
        {
            RectF(g, Sky, Ln, lw, x, y, w, h);
            Poly(g, Green, Color.Empty, 0, x + lw / 2, y + h - lw / 2, x + w * 0.35f, y + h * 0.45f, x + w * 0.6f, y + h * 0.75f, x + w * 0.75f, y + h * 0.6f, x + w - lw / 2, y + h * 0.85f, x + w - lw / 2, y + h - lw / 2);
            Ellipse(g, Yellow, Color.Empty, 0, x + w * 0.62f, y + h * 0.16f, w * 0.18f, w * 0.18f);
            RectF(g, Color.Empty, Ln, lw, x, y, w, h);
        }

        static void Magnifier(Graphics g, float cx, float cy, float r, float lw, Color glass)
        {
            Line(g, Brown, lw * 2.4f, cx + r * 0.72f, cy + r * 0.72f, cx + r * 1.75f, cy + r * 1.75f);
            Ellipse(g, glass, Ln, lw, cx - r, cy - r, r * 2, r * 2);
        }

        static void Text(Graphics g, string s, float size, FontStyle st, Color c, float x, float y, string face = "Segoe UI")
        {
            using (var f = new Font(face, size, st, GraphicsUnit.Pixel))
            using (var b = B(c))
            using (var sf = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(s, f, b, x, y, sf);
        }

        static void Floppy(Graphics g, float x, float y, float s, float lw)
        {
            float k = s / 16f;
            Poly(g, Theme.C(0x5B7FD6), Theme.C(0xB9C8F2), lw, x + 1 * k, y + 1 * k, x + 13 * k, y + 1 * k, x + 15 * k, y + 3 * k, x + 15 * k, y + 15 * k, x + 1 * k, y + 15 * k);
            RectF(g, Paper, Color.Empty, 0, x + 4 * k, y + 1.5f * k, 7 * k, 4.5f * k);
            RectF(g, Theme.C(0x5B7FD6), Color.Empty, 0, x + 8.5f * k, y + 2.2f * k, 1.6f * k, 3 * k);
            RectF(g, Paper, Color.Empty, 0, x + 3.5f * k, y + 9 * k, 9 * k, 5.5f * k);
            Line(g, PaperEdge, lw * 0.7f, x + 5 * k, y + 11 * k, x + 11 * k, y + 11 * k);
            Line(g, PaperEdge, lw * 0.7f, x + 5 * k, y + 13 * k, x + 11 * k, y + 13 * k);
        }

        static void PencilShape(Graphics g, float x0, float y0, float x1, float y1, float th, float lw)
        {
            // pencil from tip (x0,y0) to end (x1,y1)
            float dx = x1 - x0, dy = y1 - y0, len = (float)Math.Sqrt(dx * dx + dy * dy);
            float ux = dx / len, uy = dy / len, nx = -uy * th / 2, ny = ux * th / 2;
            float tipLen = th * 1.25f;
            float bx = x0 + ux * tipLen, by = y0 + uy * tipLen;
            float ex = x1 - ux * th * 0.9f, ey = y1 - uy * th * 0.9f;
            Poly(g, Yellow, Theme.C(0xB8860B), lw * 0.8f, bx + nx, by + ny, ex + nx, ey + ny, ex - nx, ey - ny, bx - nx, by - ny);
            Poly(g, Pink, Theme.C(0xC06080), lw * 0.8f, ex + nx, ey + ny, x1 + nx, y1 + ny, x1 - nx, y1 - ny, ex - nx, ey - ny);
            Poly(g, Theme.C(0xEFD9B4), Theme.C(0xB8860B), lw * 0.8f, x0, y0, bx + nx, by + ny, bx - nx, by - ny);
            Poly(g, Dark, Color.Empty, 0, x0, y0, x0 + ux * tipLen * 0.45f + nx * 0.45f, y0 + uy * tipLen * 0.45f + ny * 0.45f, x0 + ux * tipLen * 0.45f - nx * 0.45f, y0 + uy * tipLen * 0.45f - ny * 0.45f);
        }

        static void ArrowHead(Graphics g, Color c, float x, float y, float dx, float dy, float size)
        {
            float len = (float)Math.Sqrt(dx * dx + dy * dy); dx /= len; dy /= len;
            float nx = -dy, ny = dx;
            Poly(g, c, Color.Empty, 0, x + dx * size, y + dy * size, x + nx * size * 0.75f, y + ny * size * 0.75f, x - nx * size * 0.75f, y - ny * size * 0.75f);
        }

        // ---------------------------------------------------------------- icon artwork
        static void Paint(Graphics g, Ico id)
        {
            switch (id)
            {
                // ======================= SMALL (16) =======================
                case Ico.Cut:
                    Line(g, Ln, 1.3f, 4.5f, 1.5f, 10f, 10f);
                    Line(g, Ln, 1.3f, 11.5f, 1.5f, 6f, 10f);
                    Ellipse(g, Color.Empty, Blue, 1.4f, 2f, 9.5f, 4.5f, 4.5f);
                    Ellipse(g, Color.Empty, Blue, 1.4f, 9.5f, 9.5f, 4.5f, 4.5f);
                    break;
                case Ico.Copy:
                    Page(g, 1.5f, 1.5f, 8.5f, 10.5f, 2.5f, Paper, PaperEdge, 1f);
                    Page(g, 6f, 4.5f, 8.5f, 10.5f, 2.5f, Paper, PaperEdge, 1f);
                    Line(g, LnDim, 0.9f, 8f, 9f, 12.5f, 9f); Line(g, LnDim, 0.9f, 8f, 11f, 12.5f, 11f); Line(g, LnDim, 0.9f, 8f, 13f, 11f, 13f);
                    break;
                case Ico.Crop:
                    Lines(g, Ln, 1.6f, 4.5f, 1f, 4.5f, 11.5f, 15f, 11.5f);
                    Lines(g, Blue, 1.6f, 1f, 4.5f, 11.5f, 4.5f, 11.5f, 15f);
                    break;
                case Ico.Resize:
                    RectF(g, Color.FromArgb(60, 75, 163, 242), Ln, 1f, 1.5f, 1.5f, 13f, 13f);
                    RectF(g, Theme.C(0x2A2A2A), Blue, 1f, 1.5f, 6.5f, 8f, 8f);
                    Line(g, Ln, 1.1f, 7.5f, 8.5f, 12.5f, 3.5f);
                    ArrowHead(g, Ln, 13f, 3f, 1, -1, 3.2f);
                    break;
                case Ico.Rotate:
                    Page(g, 2.5f, 4.5f, 8f, 10f, 2.2f, Paper, PaperEdge, 1f);
                    using (var p = P(Blue, 1.4f)) g.DrawArc(p, 7f, 1.5f, 7.5f, 7.5f, 200, 230);
                    ArrowHead(g, Blue, 9.5f, 9.2f, -1, 0.3f, 2.6f);
                    break;
                case Ico.Pencil:
                    PencilShape(g, 1.5f, 14.5f, 14f, 2f, 4f, 1f);
                    break;
                case Ico.Fill:
                    Poly(g, Paper, PaperEdge, 1f, 7f, 2f, 13.5f, 8.5f, 8.5f, 13.5f, 2f, 7f);
                    Poly(g, Blue, Color.Empty, 0, 2.6f, 7.4f, 8.5f, 13f, 13f, 8.6f);
                    using (var p = P(Ln, 1f)) g.DrawArc(p, 4.5f, 0.5f, 5f, 6f, 180, 180);
                    Ellipse(g, Blue, Color.Empty, 0, 13.2f, 10.5f, 2.4f, 4f);
                    break;
                case Ico.Text:
                    Text(g, "A", 17f, FontStyle.Bold, Ln, 8f, 8.6f, "Times New Roman");
                    break;
                case Ico.Eraser:
                    Poly(g, Pink, Theme.C(0xD07090), 1f, 6f, 3f, 14f, 7.5f, 10f, 13.5f, 2f, 9f);
                    Poly(g, Paper, Theme.C(0xD07090), 1f, 2f, 9f, 10f, 13.5f, 8.2f, 15f, 1f, 11f);
                    Line(g, LnDim, 1f, 9f, 15f, 15f, 15f);
                    break;
                case Ico.Picker:
                    Poly(g, Theme.C(0xEDEDED), Ln, 1f, 1.5f, 14.5f, 2f, 11.5f, 8.5f, 5f, 11f, 7.5f, 4.5f, 14f);
                    Line(g, Blue, 2f, 3.2f, 12.8f, 6f, 10f);
                    Poly(g, Ln, Color.Empty, 0, 8f, 4f, 11.5f, 0.5f, 15.5f, 4.5f, 12f, 8f);
                    break;
                case Ico.Magnifier:
                    Magnifier(g, 6.5f, 6.5f, 4.6f, 1.4f, Color.FromArgb(70, 111, 195, 247));
                    break;
                case Ico.Outline:
                    Ellipse(g, Color.Empty, Ln, 1.3f, 1.5f, 3f, 10f, 8.5f);
                    PencilShape(g, 9f, 12f, 15f, 6f, 2.8f, 0.8f);
                    break;
                case Ico.ShapeFill:
                    Ellipse(g, Theme.C(0xF4A23C), Ln, 1f, 1.5f, 5f, 10f, 8.5f);
                    Poly(g, Paper, PaperEdge, 0.9f, 9f, 1f, 14.5f, 6.5f, 11f, 10f, 5.5f, 4.5f);
                    break;
                case Ico.Save:
                    Floppy(g, 0, 0, 16, 1f);
                    break;
                case Ico.Undo:
                    using (var p = P(Blue, 2f)) { g.DrawArc(p, 3.5f, 4f, 10f, 9f, 180, 200); }
                    ArrowHead(g, Blue, 3.5f, 7f, 0, 1, 4f);
                    Poly(g, Blue, Color.Empty, 0, 0.5f, 6.5f, 6.5f, 6.5f, 3.5f, 10.5f);
                    break;
                case Ico.Redo:
                    using (var p = P(Blue, 2f)) { g.DrawArc(p, 2.5f, 4f, 10f, 9f, 160, 200); }
                    Poly(g, Blue, Color.Empty, 0, 15.5f, 6.5f, 9.5f, 6.5f, 12.5f, 10.5f);
                    break;
                case Ico.PasteSmall:
                    RoundRect(g, Brown, Theme.C(0x8A5A30), 1f, 2f, 2f, 10f, 13f, 1.2f);
                    RectF(g, Theme.C(0xBDBDBD), Ln, 0.8f, 4.5f, 1f, 5f, 2.5f);
                    Page(g, 6f, 5f, 8.5f, 10f, 2.2f, Paper, PaperEdge, 1f);
                    break;
                case Ico.PasteFrom:
                    RoundRect(g, Brown, Theme.C(0x8A5A30), 1f, 2f, 2f, 10f, 13f, 1.2f);
                    RectF(g, Theme.C(0xBDBDBD), Ln, 0.8f, 4.5f, 1f, 5f, 2.5f);
                    Picture(g, 6.5f, 7f, 8.5f, 7.5f, 0.9f);
                    break;
                case Ico.RotRight:
                    Page(g, 1.5f, 5.5f, 8f, 9.5f, 2f, Paper, PaperEdge, 1f);
                    using (var p = P(Blue, 1.4f)) g.DrawArc(p, 6.5f, 1.5f, 8f, 8f, 180, 200);
                    ArrowHead(g, Blue, 14.4f, 7.5f, 0, 1, 2.8f);
                    break;
                case Ico.RotLeft:
                    Page(g, 6.5f, 5.5f, 8f, 9.5f, 2f, Paper, PaperEdge, 1f);
                    using (var p = P(Blue, 1.4f)) g.DrawArc(p, 1.5f, 1.5f, 8f, 8f, 160, 200);
                    ArrowHead(g, Blue, 1.6f, 7.5f, 0, 1, 2.8f);
                    break;
                case Ico.Rot180:
                    Page(g, 4f, 4f, 8f, 10f, 2f, Paper, PaperEdge, 1f);
                    using (var p = P(Blue, 1.4f)) g.DrawArc(p, 1.5f, 1.5f, 13f, 13f, 200, 280);
                    ArrowHead(g, Blue, 2.2f, 10.5f, -0.2f, 1, 2.8f);
                    break;
                case Ico.FlipV:
                    Poly(g, Paper, PaperEdge, 1f, 2f, 1.5f, 14f, 1.5f, 8f, 6.5f);
                    Poly(g, Blue, Color.Empty, 0, 2f, 14.5f, 14f, 14.5f, 8f, 9.5f);
                    Line(g, LnDim, 1f, 0.5f, 8f, 15.5f, 8f);
                    break;
                case Ico.FlipH:
                    Poly(g, Paper, PaperEdge, 1f, 1.5f, 2f, 1.5f, 14f, 6.5f, 8f);
                    Poly(g, Blue, Color.Empty, 0, 14.5f, 2f, 14.5f, 14f, 9.5f, 8f);
                    Line(g, LnDim, 1f, 8f, 0.5f, 8f, 15.5f);
                    break;
                case Ico.SelRect:
                    using (var p = new Pen(Ln, 1.2f) { DashPattern = new[] { 2f, 1.5f } }) g.DrawRectangle(p, 1.5f, 2.5f, 13f, 11f);
                    break;
                case Ico.SelFree:
                    using (var p = new Pen(Ln, 1.2f) { DashPattern = new[] { 2f, 1.5f } })
                    using (var path = new GraphicsPath())
                    {
                        path.AddBezier(2, 9, 0.5f, 3, 8, 0.5f, 11, 3);
                        path.AddBezier(11, 3, 15, 6, 15, 12, 10, 13);
                        path.AddBezier(10, 13, 6, 14.5f, 3, 13, 2, 9);
                        g.DrawPath(p, path);
                    }
                    break;
                case Ico.SelAll:
                    RectF(g, Color.FromArgb(80, 75, 163, 242), Color.Empty, 0, 2f, 2.5f, 12f, 11f);
                    using (var p = new Pen(Blue, 1.2f) { DashPattern = new[] { 2f, 1.5f } }) g.DrawRectangle(p, 1.5f, 2f, 13f, 12f);
                    break;
                case Ico.SelInvert:
                    RectF(g, Color.FromArgb(90, 75, 163, 242), Color.Empty, 0, 1.5f, 2f, 13f, 12f);
                    RectF(g, Theme.C(0x2B2B2B), Color.Empty, 0, 5f, 5f, 6f, 6f);
                    using (var p = new Pen(Ln, 1f) { DashPattern = new[] { 2f, 1.5f } }) { g.DrawRectangle(p, 1.5f, 2f, 13f, 12f); g.DrawRectangle(p, 5f, 5f, 6f, 6f); }
                    break;
                case Ico.SelDelete:
                    using (var p = new Pen(Ln, 1.1f) { DashPattern = new[] { 2f, 1.5f } }) g.DrawRectangle(p, 1.5f, 2.5f, 10f, 9f);
                    Line(g, Red, 2f, 8.5f, 8.5f, 14.5f, 14.5f); Line(g, Red, 2f, 14.5f, 8.5f, 8.5f, 14.5f);
                    break;
                case Ico.SelTransparent:
                    Draw.Checker(g, new Rectangle(2, 3, 12, 10), 2);
                    using (var p = new Pen(Ln, 1.1f) { DashPattern = new[] { 2f, 1.5f } }) g.DrawRectangle(p, 1.5f, 2.5f, 13f, 11f);
                    Ellipse(g, Red, Color.Empty, 0, 5f, 5f, 6f, 6f);
                    break;
                case Ico.InvertColor:
                    Ellipse(g, Paper, Ln, 1f, 1.5f, 1.5f, 13f, 13f);
                    using (var path = new GraphicsPath()) { path.AddPie(1.5f, 1.5f, 13f, 13f, 90, 180); g.FillPath(Brushes.Black, path); }
                    Ellipse(g, Color.Empty, Ln, 1f, 1.5f, 1.5f, 13f, 13f);
                    break;
                case Ico.Bold: Text(g, "B", 13f, FontStyle.Bold, Ln, 8f, 8.5f); break;
                case Ico.Italic: Text(g, "I", 13f, FontStyle.Italic | FontStyle.Bold, Ln, 8f, 8.5f, "Times New Roman"); break;
                case Ico.Underline: Text(g, "U", 12f, FontStyle.Regular, Ln, 8f, 7f); Line(g, Ln, 1f, 3f, 14.5f, 13f, 14.5f); break;
                case Ico.Strike: Text(g, "abc", 9f, FontStyle.Regular, Ln, 8f, 8f); Line(g, Red, 1f, 1.5f, 8.5f, 14.5f, 8.5f); break;
                case Ico.Opaque:
                    RectF(g, Paper, Color.Empty, 0, 1.5f, 2.5f, 13f, 11f);
                    Text(g, "A", 11f, FontStyle.Bold, Dark, 8f, 8f);
                    RectF(g, Color.Empty, Ln, 1f, 1.5f, 2.5f, 13f, 11f);
                    break;
                case Ico.TransparentBg:
                    Picture(g, 1.5f, 2.5f, 13f, 11f, 1f);
                    Text(g, "A", 11f, FontStyle.Bold, Dark, 8f, 8f);
                    break;
                case Ico.StCursor:
                    Line(g, Ln, 1f, 8f, 1f, 8f, 15f); Line(g, Ln, 1f, 1f, 8f, 15f, 8f);
                    ArrowHead(g, Ln, 8f, 0.5f, 0, -1, 2.5f); ArrowHead(g, Ln, 8f, 15.5f, 0, 1, 2.5f);
                    ArrowHead(g, Ln, 0.5f, 8f, -1, 0, 2.5f); ArrowHead(g, Ln, 15.5f, 8f, 1, 0, 2.5f);
                    break;
                case Ico.StSelection:
                    using (var p = new Pen(Ln, 1f) { DashPattern = new[] { 2f, 1.5f } }) g.DrawRectangle(p, 1.5f, 2.5f, 13f, 11f);
                    break;
                case Ico.StCanvas:
                    RectF(g, Color.Empty, Ln, 1f, 1.5f, 2.5f, 11f, 9f);
                    RectF(g, Ln, Color.Empty, 0, 12f, 11f, 3f, 3f);
                    RectF(g, Ln, Color.Empty, 0, 12f, 6f, 2f, 2f);
                    RectF(g, Ln, Color.Empty, 0, 6f, 12f, 2f, 2f);
                    break;
                case Ico.StFile:
                    Floppy(g, 1, 1, 14, 0.9f);
                    break;
                case Ico.Pin:
                    Poly(g, Color.Empty, LnDim, 1f, 6f, 2f, 10f, 2f, 10f, 7f, 12f, 9.5f, 4f, 9.5f, 6f, 7f);
                    Line(g, LnDim, 1f, 8f, 9.5f, 8f, 14.5f);
                    break;
                case Ico.Pinned:
                    Poly(g, Blue, Blue, 1f, 6f, 2f, 10f, 2f, 10f, 7f, 12f, 9.5f, 4f, 9.5f, 6f, 7f);
                    Line(g, Blue, 1f, 8f, 9.5f, 8f, 14.5f);
                    break;
                case Ico.Help:
                    Ellipse(g, Blue, Color.Empty, 0, 0.5f, 0.5f, 15f, 15f);
                    Text(g, "?", 11f, FontStyle.Bold, Color.White, 8f, 8.2f);
                    break;
                case Ico.AppIcon:
                    PaintAppIcon(g, 16f);
                    break;

                // ======================= LARGE (32) =======================
                case Ico.Paste:
                    RoundRect(g, Brown, Theme.C(0x8A5A30), 1.5f, 4f, 4f, 19f, 25f, 2f);
                    RoundRect(g, Theme.C(0xC8C8C8), Ln, 1.2f, 8.5f, 1.5f, 10f, 5f, 1.2f);
                    Page(g, 13f, 10f, 16f, 20f, 4.5f, Paper, PaperEdge, 1.2f);
                    for (int i = 0; i < 4; i++) Line(g, LnDim, 1.2f, 16f, 18f + i * 3, 26f, 18f + i * 3);
                    break;
                case Ico.Select:
                    using (var p = new Pen(Ln, 1.8f) { DashPattern = new[] { 2.2f, 1.6f } }) g.DrawRectangle(p, 3f, 4f, 26f, 20f);
                    Poly(g, Paper, Dark, 1.2f, 17f, 15f, 17f, 30f, 20.5f, 26.5f, 23.5f, 31f, 25.5f, 30f, 22.5f, 25.5f, 27.5f, 25.5f);
                    break;
                case Ico.Size:
                    RectF(g, Ln, Color.Empty, 0, 4f, 6f, 24f, 1.2f);
                    RectF(g, Ln, Color.Empty, 0, 4f, 11f, 24f, 2.5f);
                    RectF(g, Ln, Color.Empty, 0, 4f, 17f, 24f, 4f);
                    RectF(g, Ln, Color.Empty, 0, 4f, 24f, 24f, 6f);
                    break;
                case Ico.EditColors:
                    {
                        Color[] cs = { Red, Orange, Yellow, Green, Sky, Blue, Purple, Pink };
                        for (int i = 0; i < 8; i++) using (var b = B(cs[i])) g.FillPie(b, 3, 3, 26, 26, i * 45 - 90, 45.5f);
                        Ellipse(g, Theme.C(0x2B2B2B), Color.Empty, 0, 10f, 10f, 12f, 12f);
                        Ellipse(g, Color.Empty, Ln, 1.2f, 3f, 3f, 26f, 26f);
                        PencilShape(g, 14f, 18f, 30f, 2f, 5f, 1f);
                    }
                    break;
                case Ico.ZoomIn:
                    Magnifier(g, 13f, 13f, 9f, 2f, Color.FromArgb(60, 111, 195, 247));
                    Line(g, Ln, 2.4f, 8.5f, 13f, 17.5f, 13f); Line(g, Ln, 2.4f, 13f, 8.5f, 13f, 17.5f);
                    break;
                case Ico.ZoomOut:
                    Magnifier(g, 13f, 13f, 9f, 2f, Color.FromArgb(60, 111, 195, 247));
                    Line(g, Ln, 2.4f, 8.5f, 13f, 17.5f, 13f);
                    break;
                case Ico.Zoom100:
                    Magnifier(g, 13f, 13f, 9f, 2f, Color.FromArgb(60, 111, 195, 247));
                    Text(g, "100", 7.5f, FontStyle.Bold, Ln, 13f, 13.3f);
                    break;
                case Ico.FullScreen:
                    RoundRect(g, Theme.C(0x1E1E1E), Ln, 1.6f, 2f, 4f, 28f, 19f, 1.5f);
                    RectF(g, Ln, Color.Empty, 0, 13f, 23f, 6f, 3f); RectF(g, Ln, Color.Empty, 0, 9f, 26f, 14f, 2f);
                    Lines(g, Blue, 1.8f, 6f, 11f, 6f, 7.5f, 10f, 7.5f); Lines(g, Blue, 1.8f, 26f, 11f, 26f, 7.5f, 22f, 7.5f);
                    Lines(g, Blue, 1.8f, 6f, 14f, 6f, 17.5f, 10f, 17.5f); Lines(g, Blue, 1.8f, 26f, 14f, 26f, 17.5f, 22f, 17.5f);
                    break;
                case Ico.Thumbnail:
                    RoundRect(g, Theme.C(0x1E1E1E), Ln, 1.5f, 2f, 3f, 28f, 26f, 1.5f);
                    RectF(g, Theme.C(0x3A3A3A), Color.Empty, 0, 3f, 4f, 26f, 4f);
                    Picture(g, 12f, 13f, 14f, 11f, 1.2f);
                    break;
                case Ico.New:
                    Page(g, 6f, 2f, 20f, 27f, 6f, Paper, PaperEdge, 1.4f);
                    break;
                case Ico.Open:
                    Poly(g, Theme.C(0xD9A53A), Theme.C(0xA87A20), 1.2f, 2f, 7f, 11f, 7f, 13.5f, 10f, 27f, 10f, 27f, 27f, 2f, 27f);
                    Page(g, 7f, 4f, 16f, 18f, 4f, Paper, PaperEdge, 1.1f);
                    Poly(g, Theme.C(0xF2C14E), Theme.C(0xA87A20), 1.2f, 2f, 27f, 7f, 14f, 31f, 14f, 27f, 27f);
                    break;
                case Ico.SaveBig:
                    Floppy(g, 3, 3, 26, 1.4f);
                    break;
                case Ico.SaveAs:
                    Floppy(g, 2, 2, 24, 1.4f);
                    PencilShape(g, 16f, 30f, 30f, 16f, 5f, 1f);
                    break;
                case Ico.Print:
                    Page(g, 9f, 2f, 14f, 10f, 3f, Paper, PaperEdge, 1.2f);
                    RoundRect(g, Theme.C(0x6A6A6A), Ln, 1.4f, 2f, 11f, 28f, 12f, 2f);
                    Ellipse(g, Green, Color.Empty, 0, 24f, 14f, 2.5f, 2.5f);
                    RectF(g, Paper, PaperEdge, 1.2f, 8f, 19f, 16f, 10f);
                    Line(g, LnDim, 1.2f, 11f, 23f, 21f, 23f); Line(g, LnDim, 1.2f, 11f, 26f, 18f, 26f);
                    break;
                case Ico.PageSetup:
                    Page(g, 5f, 2f, 20f, 27f, 5f, Paper, PaperEdge, 1.3f);
                    using (var p = new Pen(Blue, 1.2f) { DashPattern = new[] { 2f, 1.5f } }) g.DrawRectangle(p, 8.5f, 7f, 13f, 18.5f);
                    break;
                case Ico.PrintPreview:
                    Page(g, 4f, 2f, 19f, 25f, 5f, Paper, PaperEdge, 1.3f);
                    Magnifier(g, 18f, 18f, 6.5f, 1.8f, Color.FromArgb(110, 111, 195, 247));
                    break;
                case Ico.Scanner:
                    Poly(g, Theme.C(0x6A6A6A), Ln, 1.3f, 2f, 18f, 30f, 18f, 30f, 27f, 2f, 27f);
                    Poly(g, Theme.C(0x8A8A8A), Ln, 1.3f, 3f, 18f, 7f, 8f, 29f, 12f, 29f, 18f);
                    RectF(g, Sky, Color.Empty, 0, 5f, 21f, 22f, 2f);
                    Ellipse(g, Green, Color.Empty, 0, 25f, 23.5f, 2.5f, 2.5f);
                    break;
                case Ico.Email:
                    RectF(g, Paper, PaperEdge, 1.4f, 2.5f, 7f, 27f, 19f);
                    Lines(g, PaperEdge, 1.4f, 2.5f, 7f, 16f, 18f, 29.5f, 7f);
                    Picture(g, 17f, 17f, 13f, 11f, 1.2f);
                    break;
                case Ico.Desktop:
                    RoundRect(g, Theme.C(0x1E1E1E), Ln, 1.6f, 2f, 3f, 28f, 20f, 1.5f);
                    Picture(g, 4.5f, 5.5f, 23f, 15f, 1f);
                    RectF(g, Ln, Color.Empty, 0, 13f, 23f, 6f, 3f); RectF(g, Ln, Color.Empty, 0, 9f, 26f, 14f, 2f);
                    break;
                case Ico.DeskFill:
                    RoundRect(g, Theme.C(0x1E1E1E), Ln, 1.6f, 2f, 3f, 28f, 20f, 1.5f);
                    Picture(g, 3.5f, 4.5f, 25f, 17f, 1f);
                    RectF(g, Ln, Color.Empty, 0, 13f, 23f, 6f, 3f); RectF(g, Ln, Color.Empty, 0, 9f, 26f, 14f, 2f);
                    break;
                case Ico.DeskTile:
                    RoundRect(g, Theme.C(0x1E1E1E), Ln, 1.6f, 2f, 3f, 28f, 20f, 1.5f);
                    for (int yy = 0; yy < 2; yy++) for (int xx = 0; xx < 3; xx++) Picture(g, 4f + xx * 8f, 5f + yy * 8f, 7f, 6.5f, 0.8f);
                    RectF(g, Ln, Color.Empty, 0, 13f, 23f, 6f, 3f); RectF(g, Ln, Color.Empty, 0, 9f, 26f, 14f, 2f);
                    break;
                case Ico.DeskCenter:
                    RoundRect(g, Theme.C(0x1E1E1E), Ln, 1.6f, 2f, 3f, 28f, 20f, 1.5f);
                    Picture(g, 10f, 8f, 12f, 10f, 1f);
                    RectF(g, Ln, Color.Empty, 0, 13f, 23f, 6f, 3f); RectF(g, Ln, Color.Empty, 0, 9f, 26f, 14f, 2f);
                    break;
                case Ico.Properties:
                    Page(g, 5f, 2f, 20f, 27f, 5f, Paper, PaperEdge, 1.3f);
                    for (int i = 0; i < 4; i++) { RectF(g, Blue, Color.Empty, 0, 8.5f, 10f + i * 4.5f, 2.5f, 2.5f); Line(g, LnDim, 1.3f, 13f, 11.2f + i * 4.5f, 21.5f, 11.2f + i * 4.5f); }
                    break;
                case Ico.About:
                    Ellipse(g, DBlue, Theme.C(0x7FB4F0), 1.4f, 3f, 3f, 26f, 26f);
                    Text(g, "i", 18f, FontStyle.Bold, Color.White, 16f, 16.5f, "Georgia");
                    break;
                case Ico.Exit:
                    RectF(g, Theme.C(0x8A5A30), Ln, 1.3f, 6f, 3f, 16f, 26f);
                    Poly(g, Brown, Ln, 1.1f, 6f, 3f, 15f, 6f, 15f, 32f, 6f, 29f);
                    Line(g, Red, 2.4f, 18f, 17f, 29f, 17f);
                    ArrowHead(g, Red, 30f, 17f, 1, 0, 4.5f);
                    break;
                case Ico.PicPng: PictureLabel(g, "PNG", Blue); break;
                case Ico.PicJpeg: PictureLabel(g, "JPEG", Orange); break;
                case Ico.PicBmp: PictureLabel(g, "BMP", Green); break;
                case Ico.PicGif: PictureLabel(g, "GIF", Purple); break;
                case Ico.PicOther:
                    Picture(g, 2f, 3f, 22f, 17f, 1.3f);
                    Floppy(g, 14, 14, 17, 1.2f);
                    break;
                case Ico.OnePage:
                    Page(g, 8f, 3f, 16f, 26f, 4f, Paper, PaperEdge, 1.3f);
                    break;
                case Ico.TwoPages:
                    Page(g, 2f, 5f, 13f, 22f, 3.5f, Paper, PaperEdge, 1.2f);
                    Page(g, 17f, 5f, 13f, 22f, 3.5f, Paper, PaperEdge, 1.2f);
                    break;
                case Ico.NextPage:
                    Page(g, 5f, 3f, 17f, 25f, 4f, Paper, PaperEdge, 1.3f);
                    Poly(g, Blue, Color.Empty, 0, 17f, 14f, 30f, 21f, 17f, 28f);
                    break;
                case Ico.PrevPage:
                    Page(g, 10f, 3f, 17f, 25f, 4f, Paper, PaperEdge, 1.3f);
                    Poly(g, Blue, Color.Empty, 0, 15f, 14f, 2f, 21f, 15f, 28f);
                    break;
                case Ico.ClosePreview:
                    Ellipse(g, Red, Theme.C(0xF09090), 1.3f, 4f, 4f, 24f, 24f);
                    Line(g, Color.White, 3f, 11f, 11f, 21f, 21f); Line(g, Color.White, 3f, 21f, 11f, 11f, 21f);
                    break;
                case Ico.Shapes:
                    RectF(g, Color.FromArgb(70, 75, 163, 242), Ln, 1.5f, 3f, 14f, 13f, 13f);
                    Ellipse(g, Color.FromArgb(80, 240, 138, 60), Orange, 1.5f, 12f, 4f, 15f, 15f);
                    Poly(g, Color.FromArgb(90, 87, 184, 90), Green, 1.5f, 22f, 15f, 30f, 29f, 14f, 29f);
                    break;
                case Ico.Tools:
                    PencilShape(g, 3f, 29f, 24f, 8f, 6f, 1.2f);
                    Text(g, "A", 16f, FontStyle.Bold, Ln, 24f, 22f, "Times New Roman");
                    break;
                case Ico.Colors:
                    RoundRect(g, Theme.C(0xD9A86A), Theme.C(0x8A6030), 1.4f, 2f, 5f, 28f, 22f, 10f);
                    Ellipse(g, Theme.C(0x2B2B2B), Color.Empty, 0, 18f, 18f, 6f, 6f);
                    Ellipse(g, Red, Color.Empty, 0, 6f, 9f, 5f, 5f); Ellipse(g, Yellow, Color.Empty, 0, 12f, 7f, 5f, 5f);
                    Ellipse(g, Green, Color.Empty, 0, 18.5f, 8f, 5f, 5f); Ellipse(g, Blue, Color.Empty, 0, 6f, 15.5f, 5f, 5f);
                    break;
                case Ico.Rulers:
                    RectF(g, Yellow, Theme.C(0xA88A20), 1.2f, 2f, 10f, 28f, 10f);
                    for (int i = 0; i < 8; i++) Line(g, Dark, 1f, 5f + i * 3.2f, 10.5f, 5f + i * 3.2f, i % 2 == 0 ? 16f : 13.5f);
                    break;
                case Ico.Gridlines:
                    RectF(g, Theme.C(0x1E1E1E), Ln, 1.4f, 3f, 3f, 26f, 26f);
                    for (int i = 1; i < 4; i++) { Line(g, LnDim, 1f, 3f + i * 6.5f, 3f, 3f + i * 6.5f, 29f); Line(g, LnDim, 1f, 3f, 3f + i * 6.5f, 29f, 3f + i * 6.5f); }
                    break;
                case Ico.StatusBarIco:
                    RectF(g, Theme.C(0x1E1E1E), Ln, 1.4f, 3f, 5f, 26f, 22f);
                    RectF(g, Blue, Color.Empty, 0, 4f, 22f, 24f, 4f);
                    break;
                case Ico.DarkCanvas:
                    RectF(g, Theme.C(0x050505), Ln, 1.4f, 4f, 5f, 24f, 22f);
                    using (var path = new GraphicsPath())
                    {
                        path.AddEllipse(10f, 9f, 13f, 13f);
                        using (var cut = new GraphicsPath()) { cut.AddEllipse(14.5f, 6.5f, 12f, 12f); var r = new Region(path); r.Exclude(cut); g.FillRegion(B(Yellow), r); }
                    }
                    break;
                case Ico.BrushBrush: BrushIcon(g, BrushKind.Brush); break;
                case Ico.BrushCalli1: BrushIcon(g, BrushKind.Calligraphy1); break;
                case Ico.BrushCalli2: BrushIcon(g, BrushKind.Calligraphy2); break;
                case Ico.BrushAir: BrushIcon(g, BrushKind.Airbrush); break;
                case Ico.BrushOil: BrushIcon(g, BrushKind.Oil); break;
                case Ico.BrushCrayon: BrushIcon(g, BrushKind.Crayon); break;
                case Ico.BrushMarker: BrushIcon(g, BrushKind.Marker); break;
                case Ico.BrushPencil: BrushIcon(g, BrushKind.NaturalPencil); break;
                case Ico.BrushWater: BrushIcon(g, BrushKind.Watercolor); break;

                // ======================= SHAPES (16) =======================
                default:
                    if (id >= Ico.ShLine && id <= Ico.ShLightning) ShapeIcon(g, (ShapeKind)(id - Ico.ShLine));
                    else if (id >= Ico.TexNone && id <= Ico.TexWater) TextureIcon(g, (Texture)(id - Ico.TexNone));
                    break;
            }
        }

        static void PictureLabel(Graphics g, string label, Color c)
        {
            Picture(g, 2f, 2f, 24f, 18f, 1.3f);
            RoundRect(g, c, Color.Empty, 0, 8f, 17f, 23f, 12f, 2f);
            Text(g, label, label.Length > 3 ? 7.5f : 8.5f, FontStyle.Bold, Color.White, 19.5f, 23.3f);
        }

        public static void PaintAppIcon(Graphics g, float s)
        {
            // palette with paint dabs and a brush: the classic Paint metaphor, recolored for dark mode
            float k = s / 16f;
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(0.6f * k, 2.2f * k, 13.8f * k, 11.6f * k);
                using (var hole = new GraphicsPath()) { hole.AddEllipse(8.4f * k, 8.6f * k, 3f * k, 2.6f * k); var reg = new Region(path); reg.Exclude(hole); g.FillRegion(B(Theme.C(0xE9C38D)), reg); }
                using (var p = new Pen(Theme.C(0x8A6030), Math.Max(0.6f, 0.8f * k))) g.DrawPath(p, path);
            }
            Ellipse(g, Red, Color.Empty, 0, 2.6f * k, 5.6f * k, 2.6f * k, 2.6f * k);
            Ellipse(g, Yellow, Color.Empty, 0, 5.4f * k, 3.6f * k, 2.6f * k, 2.6f * k);
            Ellipse(g, Green, Color.Empty, 0, 8.6f * k, 4f * k, 2.6f * k, 2.6f * k);
            Ellipse(g, Blue, Color.Empty, 0, 3f * k, 9f * k, 2.6f * k, 2.6f * k);
            // brush
            Line(g, Theme.C(0x5A3A1A), 1.6f * k, 9.5f * k, 9.5f * k, 15.2f * k, 0.9f * k);
            Line(g, Theme.C(0xC0C0C0), 1.9f * k, 9.6f * k, 9.6f * k, 11f * k, 7.4f * k);
            Poly(g, Theme.C(0x202020), Color.Empty, 0, 7.4f * k, 13.6f * k, 8.6f * k, 9.6f * k, 10.4f * k, 10.8f * k);
        }

        static void BrushIcon(Graphics g, BrushKind k)
        {
            // a stroke sample on the left, the tool on the right
            var rnd = new Random(5);
            switch (k)
            {
                case BrushKind.Brush:
                    using (var p = P(Theme.C(0x4BA3F2), 4.5f)) g.DrawBezier(p, 3, 26, 8, 14, 14, 30, 20, 16);
                    BrushTool(g, 2.5f);
                    break;
                case BrushKind.Calligraphy1:
                case BrushKind.Calligraphy2:
                    {
                        bool one = k == BrushKind.Calligraphy1;
                        var pts = new List<PointF>();
                        for (int i = 0; i <= 24; i++)
                        {
                            float t = i / 24f;
                            pts.Add(new PointF(3 + t * 18, 22 + (float)Math.Sin(t * Math.PI * 2) * 6));
                        }
                        float nx = one ? 2.2f : -2.2f, ny = one ? -2.2f : -2.2f;
                        for (int i = 0; i < pts.Count - 1; i++)
                            Poly(g, Theme.C(0x4BA3F2), Color.Empty, 0, pts[i].X - nx, pts[i].Y - ny, pts[i].X + nx, pts[i].Y + ny, pts[i + 1].X + nx, pts[i + 1].Y + ny, pts[i + 1].X - nx, pts[i + 1].Y - ny);
                        Line(g, Theme.C(0x5A3A1A), 3f, 31f, 1f, 24f, 9f);
                        Poly(g, Ln, Color.Empty, 0, one ? 20f : 26f, one ? 15f : 15f, one ? 25f : 21f, one ? 10f : 10f, 24f, 9f, 23f, 12f);
                    }
                    break;
                case BrushKind.Airbrush:
                    for (int i = 0; i < 70; i++)
                    {
                        double a = rnd.NextDouble() * Math.PI * 2, r = Math.Sqrt(rnd.NextDouble()) * 8;
                        g.FillRectangle(B(Theme.C(0x4BA3F2)), (float)(10 + Math.Cos(a) * r), (float)(22 + Math.Sin(a) * r), 1.1f, 1.1f);
                    }
                    RoundRect(g, Theme.C(0x8A8A8A), Ln, 1.2f, 19f, 6f, 9f, 22f, 2.5f);
                    RectF(g, Dark, Color.Empty, 0, 21.5f, 2f, 4f, 4f);
                    RectF(g, Red, Color.Empty, 0, 19.6f, 12f, 7.8f, 4f);
                    break;
                case BrushKind.Oil:
                    for (int b = 0; b < 6; b++)
                        using (var p = P(Color.FromArgb(150 + rnd.Next(100), 75, 140 + rnd.Next(60), 230), 1.6f)) g.DrawBezier(p, 2, 21 + b * 1.4f, 8, 13 + b * 1.4f, 14, 29 + b * 1.4f, 20, 17 + b * 1.4f);
                    BrushTool(g, 3.5f);
                    break;
                case BrushKind.Crayon:
                    for (int i = 0; i < 220; i++)
                    {
                        float t = (float)rnd.NextDouble();
                        float x = 3 + t * 17, y = 22 + (float)Math.Sin(t * 6) * 5 + (float)(rnd.NextDouble() - 0.5) * 6;
                        if (rnd.NextDouble() < 0.75) g.FillRectangle(B(Theme.C(0xE5534B)), x, y, 1f, 1f);
                    }
                    Poly(g, Red, Theme.C(0xA03030), 1f, 18f, 18f, 27f, 4f, 31f, 7f, 22f, 21f);
                    Poly(g, Theme.C(0xF0F0F0), Color.Empty, 0, 21.5f, 12.5f, 24.5f, 7.6f, 28.4f, 10.2f, 25.4f, 15f);
                    Poly(g, Red, Color.Empty, 0, 18f, 18f, 22f, 21f, 16.5f, 23f);
                    break;
                case BrushKind.Marker:
                    using (var p = new Pen(Color.FromArgb(150, 240, 200, 60), 6f) { StartCap = LineCap.Flat, EndCap = LineCap.Flat }) g.DrawBezier(p, 2, 22, 8, 15, 13, 28, 19, 20);
                    RoundRect(g, Theme.C(0xF4C443), Theme.C(0xA88A20), 1.2f, 21f, 3f, 7f, 19f, 2f);
                    Poly(g, Dark, Color.Empty, 0, 21.5f, 22f, 27.5f, 22f, 25.5f, 27f, 23.5f, 27f);
                    break;
                case BrushKind.NaturalPencil:
                    for (int i = 0; i < 160; i++)
                    {
                        float t = (float)rnd.NextDouble();
                        float x = 3 + t * 16, y = 24 + (float)Math.Sin(t * 5) * 4 + (float)(rnd.NextDouble() - 0.5) * 2.2f;
                        g.FillRectangle(B(Color.FromArgb(120 + rnd.Next(120), 200, 200, 200)), x, y, 0.9f, 0.9f);
                    }
                    PencilShape(g, 17f, 21f, 30f, 2f, 5f, 1f);
                    break;
                case BrushKind.Watercolor:
                    using (var p = P(Color.FromArgb(90, 75, 163, 242), 7f)) g.DrawBezier(p, 3, 24, 8, 15, 13, 30, 19, 18);
                    using (var p = P(Color.FromArgb(70, 75, 163, 242), 4f)) g.DrawBezier(p, 3, 24, 8, 15, 13, 30, 19, 18);
                    BrushTool(g, 3f);
                    Ellipse(g, Color.FromArgb(160, 75, 163, 242), Color.Empty, 0, 16f, 18f, 4f, 5f);
                    break;
            }
        }

        static void BrushTool(Graphics g, float tip)
        {
            Line(g, Theme.C(0x8B5A2B), 3.2f, 30f, 2f, 23.5f, 10f);
            Poly(g, Theme.C(0xC8C8C8), Theme.C(0x8A8A8A), 0.8f, 24.5f, 8f, 21f, 12.5f, 19.5f, 11f, 23f, 6.5f);
            using (var path = new GraphicsPath())
            {
                path.AddBezier(21f, 12.5f, 19f, 15f, 18f + tip * 0.2f, 18f, 16f, 20f);
                path.AddBezier(16f, 20f, 16.5f, 16f, 17.5f, 13f, 19.5f, 11f);
                g.FillPath(B(Theme.C(0x2D6FC0)), path);
            }
        }

        static void ShapeIcon(Graphics g, ShapeKind k)
        {
            var r = new RectangleF(1.5f, 1.5f, 13f, 13f);
            if (k == ShapeKind.Line) { Line(g, Ln, 1.2f, 2f, 14f, 14f, 2f); return; }
            if (k == ShapeKind.Curve)
            {
                using (var p = P(Ln, 1.2f)) g.DrawBezier(p, 1.5f, 13f, 4f, 0f, 10f, 17f, 14.5f, 3f);
                return;
            }
            if (k == ShapeKind.Polygon)
            {
                Poly(g, Color.FromArgb(40, 255, 255, 255), Ln, 1.1f, 2f, 5f, 8f, 1.5f, 14.5f, 6f, 11f, 14.5f, 4f, 13f, 6f, 9f);
                return;
            }
            using (var path = ShapeGeometry.Build(k, r, 1f))
            {
                using (var b = B(Color.FromArgb(40, 255, 255, 255))) g.FillPath(b, path);
                using (var p = P(Ln, 1.1f)) g.DrawPath(p, path);
            }
        }

        static void TextureIcon(Graphics g, Texture t)
        {
            var rnd = new Random(3);
            switch (t)
            {
                case Texture.None:
                    using (var p = new Pen(LnDim, 1f) { DashPattern = new[] { 2f, 2f } }) g.DrawRectangle(p, 1.5f, 1.5f, 13f, 13f);
                    break;
                case Texture.Solid: RectF(g, Ln, Color.Empty, 0, 1.5f, 1.5f, 13f, 13f); break;
                case Texture.Crayon:
                    for (int i = 0; i < 120; i++) if (rnd.NextDouble() < 0.7) g.FillRectangle(B(Ln), 1.5f + (float)rnd.NextDouble() * 13, 1.5f + (float)rnd.NextDouble() * 13, 1, 1);
                    break;
                case Texture.Marker: RectF(g, Color.FromArgb(150, 212, 212, 212), Color.Empty, 0, 1.5f, 1.5f, 13f, 13f); RectF(g, Color.FromArgb(90, 212, 212, 212), Color.Empty, 0, 5.5f, 1.5f, 5f, 13f); break;
                case Texture.Oil:
                    for (int i = 0; i < 7; i++) Line(g, Color.FromArgb(140 + rnd.Next(110), 212, 212, 212), 1.6f, 2f, 2.5f + i * 1.8f, 14f, 2.5f + i * 1.8f + (float)(rnd.NextDouble() - 0.5));
                    break;
                case Texture.NaturalPencil:
                    for (int i = 0; i < 90; i++) g.FillRectangle(B(Color.FromArgb(90 + rnd.Next(120), 212, 212, 212)), 1.5f + (float)rnd.NextDouble() * 13, 1.5f + (float)rnd.NextDouble() * 13, 1, 1);
                    break;
                case Texture.Watercolor:
                    Ellipse(g, Color.FromArgb(70, 75, 163, 242), Color.FromArgb(140, 75, 163, 242), 1f, 1.5f, 2f, 13f, 12f);
                    break;
            }
        }

        // map enum values used by menus
        public static Ico ForShape(ShapeKind s) => Ico.ShLine + (int)s;
        public static Ico ForTexture(Texture t) => Ico.TexNone + (int)t;
        public static Ico ForBrush(BrushKind b) => Ico.BrushBrush + (int)b;

        /// <summary>Multi-size .ico for the window/taskbar.</summary>
        public static Icon BuildAppIcon(int size)
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    PaintAppIcon(g, size);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
