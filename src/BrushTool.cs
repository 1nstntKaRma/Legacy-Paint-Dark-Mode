using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PaintDark
{
    /// <summary>All nine Paint brushes. Each stroke builds a coverage mask that is recomposited over the committed picture.</summary>
    public sealed unsafe class BrushTool : StrokeTool
    {
        Mask8 _mask;
        uint _color;
        BrushKind _kind;
        float _size;
        Random _rnd;
        PointF? _hover;
        // oil brush bristles
        float[] _bOff, _bInt;
        PointF _dir = new PointF(1, 0);
        bool _haveDir;

        public BrushTool(Editor e) : base(e) { }
        public override string Name => Commands.BrushNames[(int)E.BrushType];

        public override IntPtr GetCursor(MouseArgs a)
        {
            if (V == null) return AppCursors.Crosshair;
            return V.ZoomedSize(E.ToolSize) >= 4 ? IntPtr.Zero : AppCursors.Crosshair;
        }

        public override void DocumentChanged() { _mask?.Dispose(); _mask = null; }

        void EnsureMask()
        {
            if (_mask == null || _mask.W != Doc.Width || _mask.H != Doc.Height)
            {
                _mask?.Dispose();
                _mask = new Mask8(Doc.Width, Doc.Height);
            }
        }

        protected override void BeginStroke(MouseArgs a)
        {
            _kind = E.BrushType;
            _color = ColorFor(a) | 0xFF000000u;
            _size = E.ToolSize;
            _rnd = new Random(Environment.TickCount);
            _haveDir = false;
            if (_kind != BrushKind.Airbrush) EnsureMask();
            if (_kind == BrushKind.Oil) InitBristles();
            if (_kind == BrushKind.Airbrush) { Spray(a.P, true); V.StartToolTimer(15); return; }
            Segment(a.P, a.P, true);
        }

        protected override void ContinueStroke(MouseArgs a)
        {
            MoveHover(a.P);
            if (_kind == BrushKind.Airbrush) { _lastF = a.P; Spray(a.P, false); return; }
            Segment(_lastF, a.P, false);
        }

        public override void Tick()
        {
            if (_down && _kind == BrushKind.Airbrush) Spray(_lastF, true);
            else V.StopToolTimer();
        }

        protected override void EndStroke()
        {
            V.StopToolTimer();
            if (_mask != null && !_dirty.IsEmpty) _mask.Clear(_dirty);
        }

        protected override void AbortStroke()
        {
            V.StopToolTimer();
            if (_mask != null && !_dirty.IsEmpty) _mask.Clear(_dirty);
        }

        // ------------------------------------------------------------------ stroke segment
        void Segment(PointF p0, PointF p1, bool first)
        {
            Rectangle r = Rectangle.Empty;
            float r0 = Math.Max(0.5f, _size / 2f);
            switch (_kind)
            {
                case BrushKind.Brush:
                    r = StampLine(p0, p1, r0, Math.Max(0.35f, r0 * 0.18f), first, (x, y) => Raster.DiscToMask(_mask, x, y, r0));
                    break;
                case BrushKind.Calligraphy1:
                case BrushKind.Calligraphy2:
                    r = Calligraphy(p0, p1, _kind == BrushKind.Calligraphy1);
                    break;
                case BrushKind.Oil:
                    r = Oil(p0, p1, first);
                    break;
                case BrushKind.Crayon:
                    r = StampLine(p0, p1, r0, Math.Max(0.5f, r0 * 0.2f), first, (x, y) => TexturedDisc(x, y, r0, 0));
                    break;
                case BrushKind.Marker:
                    r = StampLine(p0, p1, r0, Math.Max(0.4f, r0 * 0.15f), first, (x, y) => Raster.DiscToMask(_mask, x, y, r0));
                    break;
                case BrushKind.NaturalPencil:
                    r = StampLine(p0, p1, r0, Math.Max(0.35f, r0 * 0.2f), first, (x, y) => TexturedDisc(x, y, Math.Max(0.6f, r0), 1));
                    break;
                case BrushKind.Watercolor:
                    r = StampLine(p0, p1, r0, Math.Max(0.5f, r0 * 0.15f), first, (x, y) => WaterDisc(x, y, r0));
                    break;
            }
            if (r.Width <= 0) return;
            r.Intersect(Doc.Bounds);
            int opacity = _kind == BrushKind.Marker ? 150 : _kind == BrushKind.Watercolor ? 120 : _kind == BrushKind.NaturalPencil ? 215 : 255;
            Raster.ApplyMask(Doc.Image, Doc.Shadow, _mask, r, _color, opacity);
            AddDirty(r);
        }

        delegate Rectangle Stamp(float x, float y);

        static Rectangle StampLine(PointF p0, PointF p1, float radius, float spacing, bool first, Stamp stamp)
        {
            Rectangle acc = Rectangle.Empty;
            float dx = p1.X - p0.X, dy = p1.Y - p0.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (first || len < 0.001f) return stamp(p1.X, p1.Y);
            int n = Math.Max(1, (int)Math.Ceiling(len / spacing));
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n;
                acc = Raster.Union(acc, stamp(p0.X + dx * t, p0.Y + dy * t));
            }
            return acc;
        }

        /// <summary>Round dab whose coverage is broken up by paper grain (kind 0 = crayon, 1 = natural pencil).</summary>
        Rectangle TexturedDisc(float cx, float cy, float radius, int kind)
        {
            var m = _mask;
            int x0 = Math.Max(0, (int)Math.Floor(cx - radius - 1)), x1 = Math.Min(m.W - 1, (int)Math.Ceiling(cx + radius + 1));
            int y0 = Math.Max(0, (int)Math.Floor(cy - radius - 1)), y1 = Math.Min(m.H - 1, (int)Math.Ceiling(cy + radius + 1));
            if (x1 < x0 || y1 < y0) return Rectangle.Empty;
            for (int y = y0; y <= y1; y++)
            {
                byte* row = m.P + (long)y * m.W;
                float ddy = y + 0.5f - cy;
                for (int x = x0; x <= x1; x++)
                {
                    float ddx = x + 0.5f - cx;
                    float d = (float)Math.Sqrt(ddx * ddx + ddy * ddy);
                    float cov = radius + 0.5f - d;
                    if (cov <= 0) continue;
                    if (cov > 1) cov = 1;
                    int gr = Grain.At(x, y);
                    float tex;
                    if (kind == 0)
                    {
                        // crayon: most of the paper catches pigment, valleys stay empty; edges are ragged
                        float edge = d / Math.Max(0.5f, radius);
                        float thr = 70 + edge * edge * 90;
                        tex = (gr - thr) / 40f;
                    }
                    else
                    {
                        // graphite: lighter, grainier, slightly stronger in the centre
                        float edge = d / Math.Max(0.5f, radius);
                        tex = (gr - 40 - edge * 70) / 120f;
                    }
                    if (tex <= 0) continue;
                    if (tex > 1) tex = 1;
                    int v = (int)(cov * tex * 255);
                    if (v > row[x]) row[x] = (byte)v;
                }
            }
            return Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
        }

        /// <summary>Soft wash with a slightly darker rim, the way pigment pools at the edge of a wet stroke.</summary>
        Rectangle WaterDisc(float cx, float cy, float radius)
        {
            var m = _mask;
            int x0 = Math.Max(0, (int)Math.Floor(cx - radius - 1)), x1 = Math.Min(m.W - 1, (int)Math.Ceiling(cx + radius + 1));
            int y0 = Math.Max(0, (int)Math.Floor(cy - radius - 1)), y1 = Math.Min(m.H - 1, (int)Math.Ceiling(cy + radius + 1));
            if (x1 < x0 || y1 < y0) return Rectangle.Empty;
            for (int y = y0; y <= y1; y++)
            {
                byte* row = m.P + (long)y * m.W;
                float ddy = y + 0.5f - cy;
                for (int x = x0; x <= x1; x++)
                {
                    float ddx = x + 0.5f - cx;
                    float d = (float)Math.Sqrt(ddx * ddx + ddy * ddy) / Math.Max(0.5f, radius);
                    if (d >= 1.05f) continue;
                    float fall = d < 0.8f ? 1f : Math.Max(0, (1.05f - d) / 0.25f);
                    float rim = (float)Math.Exp(-Math.Pow((d - 0.82f) / 0.12f, 2));
                    float v = (0.62f + 0.38f * rim) * fall * (0.86f + 0.14f * Grain.At(x, y) / 255f);
                    int iv = (int)(v * 255);
                    if (iv > row[x]) row[x] = (byte)Math.Min(255, iv);
                }
            }
            return Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
        }

        // ------------------------------------------------------------------ calligraphy
        Rectangle Calligraphy(PointF p0, PointF p1, bool fortyFive)
        {
            float half = Math.Max(1f, _size) / 2f;
            // nib 1 leans like "/" (45°), nib 2 like "\" (135°)
            float nx = fortyFive ? 0.7071f * half : -0.7071f * half;
            float ny = -0.7071f * half;
            var q = new[]
            {
                new PointF(p0.X - nx, p0.Y - ny), new PointF(p0.X + nx, p0.Y + ny),
                new PointF(p1.X + nx, p1.Y + ny), new PointF(p1.X - nx, p1.Y - ny),
            };
            var r = FillQuad(q);
            // the nib itself (so strokes parallel to the nib still leave a hairline)
            r = Raster.Union(r, StampLine(new PointF(p1.X - nx, p1.Y - ny), new PointF(p1.X + nx, p1.Y + ny), 0.6f, 0.5f, false, (x, y) => Raster.DiscToMask(_mask, x, y, 0.6f)));
            return r;
        }

        /// <summary>Anti-aliased convex quad into the mask (4x4 supersampling, max-combine).</summary>
        Rectangle FillQuad(PointF[] q)
        {
            float minX = q[0].X, maxX = q[0].X, minY = q[0].Y, maxY = q[0].Y;
            foreach (var p in q) { minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y); }
            int x0 = Math.Max(0, (int)Math.Floor(minX)), x1 = Math.Min(_mask.W - 1, (int)Math.Ceiling(maxX));
            int y0 = Math.Max(0, (int)Math.Floor(minY)), y1 = Math.Min(_mask.H - 1, (int)Math.Ceiling(maxY));
            if (x1 < x0 || y1 < y0) return Rectangle.Empty;
            // orientation-independent inside test: point is inside if all edge cross products share a sign
            for (int y = y0; y <= y1; y++)
            {
                byte* row = _mask.P + (long)y * _mask.W;
                for (int x = x0; x <= x1; x++)
                {
                    int hits = 0;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                        {
                            float px = x + (sx + 0.5f) / 4f, py = y + (sy + 0.5f) / 4f;
                            if (Inside(q, px, py)) hits++;
                        }
                    if (hits == 0) continue;
                    int v = hits * 255 / 16;
                    if (v > row[x]) row[x] = (byte)v;
                }
            }
            return Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
        }

        static bool Inside(PointF[] q, float px, float py)
        {
            bool pos = false, neg = false;
            for (int i = 0; i < 4; i++)
            {
                var a = q[i]; var b = q[(i + 1) & 3];
                float c = (b.X - a.X) * (py - a.Y) - (b.Y - a.Y) * (px - a.X);
                if (c > 0) pos = true; else if (c < 0) neg = true;
                if (pos && neg) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ oil
        void InitBristles()
        {
            int n = Math.Max(7, Math.Min(64, (int)(_size * 1.3f)));
            _bOff = new float[n]; _bInt = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0 : i / (float)(n - 1) * 2 - 1;      // -1..1 across the brush
                _bOff[i] = t * _size / 2f + (float)(_rnd.NextDouble() - 0.5) * 0.6f;
                float edge = 1 - t * t * 0.35f;
                _bInt[i] = (0.72f + 0.28f * (float)_rnd.NextDouble()) * edge;
            }
        }

        Rectangle Oil(PointF p0, PointF p1, bool first)
        {
            float dx = p1.X - p0.X, dy = p1.Y - p0.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len > 0.5f)
            {
                var nd = new PointF(dx / len, dy / len);
                if (_haveDir) { nd.X = _dir.X * 0.6f + nd.X * 0.4f; nd.Y = _dir.Y * 0.6f + nd.Y * 0.4f; float l2 = (float)Math.Sqrt(nd.X * nd.X + nd.Y * nd.Y); if (l2 > 0) { nd.X /= l2; nd.Y /= l2; } }
                _dir = nd; _haveDir = true;
            }
            if (first || !_haveDir)
            {
                // a round dab of bristles at the start
                Rectangle acc = Rectangle.Empty;
                for (int i = 0; i < _bOff.Length; i++)
                {
                    float a = (float)(_rnd.NextDouble() * Math.PI * 2);
                    float rr = (float)Math.Sqrt(_rnd.NextDouble()) * _size / 2f;
                    acc = Raster.Union(acc, Raster.DiscToMask(_mask, p1.X + (float)Math.Cos(a) * rr, p1.Y + (float)Math.Sin(a) * rr, Math.Max(0.6f, _size / 14f), (int)(_bInt[i] * 255)));
                }
                return acc;
            }
            float px = -_dir.Y, py = _dir.X;
            Rectangle res = Rectangle.Empty;
            float br = Math.Max(0.75f, _size / 11f);
            for (int i = 0; i < _bOff.Length; i++)
            {
                float o = _bOff[i];
                var a = new PointF(p0.X + px * o, p0.Y + py * o);
                var b = new PointF(p1.X + px * o, p1.Y + py * o);
                int strength = (int)(_bInt[i] * 255);
                res = Raster.Union(res, StampLine(a, b, br, 0.5f, false, (x, y) => Raster.DiscToMask(_mask, x, y, br, strength)));
            }
            return res;
        }

        // ------------------------------------------------------------------ airbrush
        void Spray(PointF c, bool full)
        {
            float r = Math.Max(1f, _size / 2f);
            int n = Math.Max(3, (int)(r * r * Math.PI / (full ? 24f : 48f)));
            var img = Doc.Image;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int i = 0; i < n; i++)
            {
                double a = _rnd.NextDouble() * Math.PI * 2, d = Math.Sqrt(_rnd.NextDouble()) * r;
                int x = (int)Math.Floor(c.X + Math.Cos(a) * d), y = (int)Math.Floor(c.Y + Math.Sin(a) * d);
                if ((uint)x >= (uint)img.W || (uint)y >= (uint)img.H) continue;
                img.P[(long)y * img.W + x] = _color;
                if (x < minX) minX = x; if (x > maxX) maxX = x; if (y < minY) minY = y; if (y > maxY) maxY = y;
            }
            if (maxX >= minX) AddDirty(Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1));
        }

        // ------------------------------------------------------------------ cursor outline
        protected override void Hover(MouseArgs a) => MoveHover(a.P);

        void MoveHover(PointF p)
        {
            if (_hover.HasValue && Math.Abs(_hover.Value.X - p.X) < 0.01 && Math.Abs(_hover.Value.Y - p.Y) < 0.01) return;
            InvalidateHover();
            _hover = p;
            InvalidateHover();
        }

        void InvalidateHover()
        {
            if (_hover == null || V == null) return;
            float r = E.ToolSize / 2f + 2;
            var p = _hover.Value;
            V.InvalidateImageOutline(Rectangle.FromLTRB((int)Math.Floor(p.X - r), (int)Math.Floor(p.Y - r), (int)Math.Ceiling(p.X + r), (int)Math.Ceiling(p.Y + r)), 3);
        }

        public override void MouseLeave() { InvalidateHover(); _hover = null; }
        public override void OptionsChanged() { InvalidateHover(); }

        public override void PaintScreen(Graphics g)
        {
            if (_hover == null || V == null) return;
            float zs = V.ZoomedSize(E.ToolSize);
            if (zs < 4) return;
            var c = V.ImageToViewF(_hover.Value);
            float r = zs / 2f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var kind = E.BrushType;
            if (kind == BrushKind.Calligraphy1 || kind == BrushKind.Calligraphy2)
            {
                float k = 0.7071f * r;
                float sx = kind == BrushKind.Calligraphy1 ? k : -k;
                using (var p1 = new Pen(Color.FromArgb(200, 0, 0, 0), 2f)) g.DrawLine(p1, c.X - sx, c.Y + k, c.X + sx, c.Y - k);
                using (var p2 = new Pen(Color.FromArgb(220, 255, 255, 255), 1f)) g.DrawLine(p2, c.X - sx, c.Y + k, c.X + sx, c.Y - k);
                return;
            }
            // thin two-tone ring: readable on black and on white canvases
            using (var p1 = new Pen(Color.FromArgb(200, 0, 0, 0), 1f)) g.DrawEllipse(p1, c.X - r - 0.5f, c.Y - r - 0.5f, r * 2 + 1, r * 2 + 1);
            using (var p2 = new Pen(Color.FromArgb(200, 255, 255, 255), 1f)) g.DrawEllipse(p2, c.X - r + 0.5f, c.Y - r + 0.5f, r * 2 - 1, r * 2 - 1);
        }
    }
}
