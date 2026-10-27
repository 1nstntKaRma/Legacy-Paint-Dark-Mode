using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace PaintDark
{
    /// <summary>Lines, curves, polygons and the 20 box shapes, with Paint's "still editable until you click away" behaviour.</summary>
    public sealed unsafe class ShapeTool : ToolBase
    {
        enum Phase { Idle, Creating, Pending, CurveBend1, CurveBend2, PolyDrawing }
        Phase _phase = Phase.Idle;
        ShapeKind _kind;
        bool _swap;                 // drawn with the right button: outline = color 2, fill = color 1
        PointF _a, _b;              // line endpoints / box corners (image coords, snapped)
        PointF _c1, _c2;            // curve control points
        List<PointF> _poly = new List<PointF>();
        bool _polyClosed;
        // interaction
        int _drag;                  // 0 none, 1 creating, 2 moving, 3 handle, 4 curve bend, 5 poly segment
        int _handle;
        PointF _dragStart, _origA, _origB, _origC1, _origC2;
        List<PointF> _origPoly;
        bool _btnDown;
        MouseButtons _btn;
        Rectangle _lastBounds;
        DateTime _lastPolyClick;
        // render cache
        Bitmap _bmp;
        byte[] _fillMask, _lineMask;
        Rectangle _maskRect;
        bool _maskValid;

        public ShapeTool(Editor e) : base(e) { }
        Point _downView;
        bool _dragged;
        public override string Name => Commands.ShapeNames[(int)_kind];
        public override bool HasPending => _phase != Phase.Idle;
        public override bool InStroke => _btnDown;
        public string DebugInfo => "phase=" + _phase + " drag=" + _drag + " down=" + _btnDown + " poly=" + _poly.Count + " closed=" + _polyClosed;

        int Width => E.ToolSize;
        float Snap(float v) => (Width & 1) == 1 ? (float)Math.Floor(v) + 0.5f : (float)Math.Round(v);
        PointF SnapP(PointF p) => new PointF(Snap(p.X), Snap(p.Y));
        bool IsBox => _kind != ShapeKind.Line && _kind != ShapeKind.Curve && _kind != ShapeKind.Polygon;

        public override void Activate() { _kind = E.ShapeType; }
        public override void DocumentChanged() { Reset(); }
        public override void OptionsChanged() { Changed(); }
        public override void ColorsChanged(int slot) { Changed(); }

        void Reset()
        {
            var old = _lastBounds;
            _phase = Phase.Idle; _drag = 0; _btnDown = false;
            _poly.Clear(); _polyClosed = false;
            _maskValid = false;
            _lastBounds = Rectangle.Empty;
            if (!old.IsEmpty) V?.InvalidateImageOutline(old, 12);
            E.SetSelSize(null);
        }

        // ------------------------------------------------------------------ geometry
        RectangleF BoxRect
        {
            get
            {
                float x0 = Math.Min(_a.X, _b.X), y0 = Math.Min(_a.Y, _b.Y), x1 = Math.Max(_a.X, _b.X), y1 = Math.Max(_a.Y, _b.Y);
                return RectangleF.FromLTRB(x0, y0, x1, y1);
            }
        }

        RectangleF Extent
        {
            get
            {
                switch (_kind)
                {
                    case ShapeKind.Line: return RectangleF.FromLTRB(Math.Min(_a.X, _b.X), Math.Min(_a.Y, _b.Y), Math.Max(_a.X, _b.X), Math.Max(_a.Y, _b.Y));
                    case ShapeKind.Curve:
                        {
                            float x0 = Math.Min(Math.Min(_a.X, _b.X), Math.Min(_c1.X, _c2.X)), x1 = Math.Max(Math.Max(_a.X, _b.X), Math.Max(_c1.X, _c2.X));
                            float y0 = Math.Min(Math.Min(_a.Y, _b.Y), Math.Min(_c1.Y, _c2.Y)), y1 = Math.Max(Math.Max(_a.Y, _b.Y), Math.Max(_c1.Y, _c2.Y));
                            return RectangleF.FromLTRB(x0, y0, x1, y1);
                        }
                    case ShapeKind.Polygon:
                        {
                            if (_poly.Count == 0) return RectangleF.Empty;
                            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                            foreach (var p in _poly) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
                            if (_phase == Phase.PolyDrawing) { x0 = Math.Min(x0, _b.X); y0 = Math.Min(y0, _b.Y); x1 = Math.Max(x1, _b.X); y1 = Math.Max(y1, _b.Y); }
                            return RectangleF.FromLTRB(x0, y0, x1, y1);
                        }
                    default: return BoxRect;
                }
            }
        }

        /// <summary>Picture-space pixels the shape can touch.</summary>
        Rectangle PixelBounds
        {
            get
            {
                var r = Extent;
                float pad = Width / 2f + 3;
                if (_kind == ShapeKind.Curve || IsBox) pad = Width * 2f + 3;   // miter joins on stars/arrows overshoot
                return Rectangle.FromLTRB((int)Math.Floor(r.Left - pad), (int)Math.Floor(r.Top - pad), (int)Math.Ceiling(r.Right + pad), (int)Math.Ceiling(r.Bottom + pad));
            }
        }

        GraphicsPath BuildPath(out bool closed)
        {
            closed = true;
            var p = new GraphicsPath(FillMode.Winding);
            switch (_kind)
            {
                case ShapeKind.Line:
                    closed = false;
                    p.AddLine(_a, _b);
                    break;
                case ShapeKind.Curve:
                    closed = false;
                    p.AddBezier(_a, _c1, _c2, _b);
                    break;
                case ShapeKind.Polygon:
                    {
                        var pts = new List<PointF>(_poly);
                        if (_phase == Phase.PolyDrawing) pts.Add(_b);
                        if (pts.Count < 2) { p.AddLine(pts[0], pts[0]); closed = false; break; }
                        if (_polyClosed && pts.Count >= 3) p.AddPolygon(pts.ToArray());
                        else { p.AddLines(pts.ToArray()); closed = false; }
                        break;
                    }
                default:
                    {
                        var r = BoxRect;
                        if (r.Width < 0.5f || r.Height < 0.5f) { p.AddLine(new PointF(r.Left, r.Top), new PointF(r.Right, r.Bottom)); closed = false; break; }
                        var built = ShapeGeometry.Build(_kind, r, Width);
                        p.Dispose();
                        return built;
                    }
            }
            return p;
        }

        // ------------------------------------------------------------------ mask rendering
        void Changed()
        {
            if (_phase == Phase.Idle) return;
            _maskValid = false;
            var nb = PixelBounds;
            var inv = Raster.Union(_lastBounds, nb);
            _lastBounds = nb;
            V?.InvalidateImageOutline(inv, 12);
            var ext = Extent;
            if (_phase != Phase.Idle) E.SetSelSize(new Size((int)Math.Round(ext.Width) + (_kind == ShapeKind.Line || _kind == ShapeKind.Curve ? 1 : 1), (int)Math.Round(ext.Height) + 1));
        }

        void EnsureMasks(Rectangle region)
        {
            var b = PixelBounds;
            b.Intersect(region);
            if (_maskValid && _maskRect == b) return;
            _maskRect = b;
            _maskValid = true;
            if (b.Width <= 0 || b.Height <= 0) return;
            int w = b.Width, h = b.Height;
            if (_bmp == null || _bmp.Width < w || _bmp.Height < h)
            {
                _bmp?.Dispose();
                _bmp = new Bitmap(Math.Max(w, 64), Math.Max(h, 64), PixelFormat.Format32bppPArgb);
            }
            int n = w * h;
            if (_fillMask == null || _fillMask.Length < n) { _fillMask = new byte[n]; _lineMask = new byte[n]; }
            bool closed;
            using (var path = BuildPath(out closed))
            {
                bool wantFill = closed && E.ShapeFill != Texture.None && (_kind != ShapeKind.Polygon || _polyClosed);
                bool wantLine = E.Outline != Texture.None || !closed;
                RenderPath(path, b, true, wantFill, _fillMask);
                RenderPath(path, b, false, wantLine, _lineMask);
            }
        }

        void RenderPath(GraphicsPath path, Rectangle b, bool fill, bool want, byte[] mask)
        {
            int w = b.Width, h = b.Height;
            if (!want) { Array.Clear(mask, 0, w * h); return; }
            using (var g = Graphics.FromImage(_bmp))
            {
                g.SetClip(new Rectangle(0, 0, w, h));
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(-b.X - 0.5f, -b.Y - 0.5f);
                if (fill) g.FillPath(Brushes.White, path);
                else
                {
                    using (var pen = new Pen(Color.White, Width))
                    {
                        bool open = _kind == ShapeKind.Line || _kind == ShapeKind.Curve || (_kind == ShapeKind.Polygon && !_polyClosed);
                        pen.LineJoin = _kind == ShapeKind.Oval || _kind == ShapeKind.Curve || _kind == ShapeKind.Heart || _kind == ShapeKind.CloudCallout ? LineJoin.Round : LineJoin.Miter;
                        pen.MiterLimit = 4f;
                        if (open) { pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round; }
                        g.DrawPath(pen, path);
                    }
                }
            }
            var data = _bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                fixed (byte* m = mask)
                    for (int y = 0; y < h; y++)
                    {
                        byte* s = (byte*)data.Scan0 + (long)y * data.Stride + 3;
                        byte* d = m + y * w;
                        for (int x = 0; x < w; x++) { d[x] = *s; s += 4; }
                    }
            }
            finally { _bmp.UnlockBits(data); }
        }

        uint LineColor => (_swap ? E.Color2 : E.Color1) | 0xFF000000u;
        uint FillColor => (_swap ? E.Color1 : E.Color2) | 0xFF000000u;

        public override Rectangle CompositeBounds => _phase == Phase.Idle ? Rectangle.Empty : PixelBounds;

        public override void Composite(Dib target, Rectangle area)
        {
            if (_phase == Phase.Idle) return;
            var region = V != null ? Rectangle.Inflate(V.ImageViewport, 4, 4) : Doc.Bounds;
            region = Rectangle.Union(region, area);
            EnsureMasks(region);
            CompositeInto(target, area.X, area.Y, area);
        }

        void CompositeInto(Dib target, int ox, int oy, Rectangle area)
        {
            var b = Rectangle.Intersect(_maskRect, area);
            if (b.Width <= 0 || b.Height <= 0) return;
            int mw = _maskRect.Width;
            uint lc = LineColor, fc = FillColor;
            var ft = E.ShapeFill; var lt = E.Outline;
            bool closed = !(_kind == ShapeKind.Line || _kind == ShapeKind.Curve || (_kind == ShapeKind.Polygon && !_polyClosed));
            if (!closed && lt == Texture.None) lt = Texture.Solid;   // open strokes are always visible
            for (int y = b.Top; y < b.Bottom; y++)
            {
                uint* row = target.Row(y - oy) - ox;
                int mo = (y - _maskRect.Y) * mw - _maskRect.X;
                for (int x = b.Left; x < b.Right; x++)
                {
                    int fm = _fillMask[mo + x], lm = _lineMask[mo + x];
                    if (fm == 0 && lm == 0) continue;
                    uint c = row[x];
                    if (fm != 0) c = Textured(c, fc, fm, ft, x, y);
                    if (lm != 0) c = Textured(c, lc, lm, lt, x, y);
                    row[x] = c;
                }
            }
        }

        /// <summary>Applies a Paint "medium" to a coverage value.</summary>
        static uint Textured(uint dst, uint color, int cov, Texture t, int x, int y)
        {
            switch (t)
            {
                case Texture.None: return dst;
                case Texture.Solid: return Px.Blend(dst, color, cov);
                case Texture.Crayon:
                    {
                        int gr = Grain.At(x, y);
                        int a = Math.Max(0, Math.Min(255, (gr - 80) * 255 / 45));
                        return Px.Blend(dst, color, cov * a / 255);
                    }
                case Texture.Marker: return Px.Blend(dst, color, cov * 150 / 255);
                case Texture.Oil:
                    {
                        // streaky bristle look: low frequency noise stretched horizontally
                        int s = Grain.At(x / 6, y * 3);
                        int a = 150 + s * 105 / 255;
                        return Px.Blend(dst, color, cov * a / 255);
                    }
                case Texture.NaturalPencil:
                    {
                        int gr = Grain.At(x, y);
                        int a = Math.Max(0, Math.Min(255, (gr - 40) * 2));
                        return Px.Blend(dst, color, cov * a * 200 / (255 * 255));
                    }
                case Texture.Watercolor:
                    {
                        int gr = Grain.At(x, y);
                        int a = 95 + gr * 30 / 255;
                        return Px.Blend(dst, color, cov * a / 255);
                    }
            }
            return dst;
        }

        // ------------------------------------------------------------------ commit / cancel
        public override void Commit()
        {
            if (_phase == Phase.Idle) return;
            if (_phase == Phase.PolyDrawing) { if (_poly.Count >= 3) _polyClosed = true; else if (_poly.Count < 2) { Reset(); return; } }
            if ((_phase == Phase.Creating) && Extent.Width < 0.5f && Extent.Height < 0.5f && _kind != ShapeKind.Line) { Reset(); return; }
            var b = PixelBounds;
            b.Intersect(Doc.Bounds);
            if (b.Width > 0 && b.Height > 0)
            {
                _maskValid = false;
                EnsureMasks(Doc.Bounds);
                CompositeInto(Doc.Image, 0, 0, b);
                E.Commit(b, Name);
            }
            Reset();
        }

        public override bool UndoPending()
        {
            if (_phase == Phase.Idle) return false;
            Reset();
            return true;
        }

        public void DeletePending() => Reset();

        public override void CancelStroke()
        {
            if (!_btnDown) return;
            _btnDown = false;
            if (_drag == 1 && _phase == Phase.Creating) { Reset(); return; }
            if (_drag == 2 || _drag == 3) { _a = _origA; _b = _origB; _c1 = _origC1; _c2 = _origC2; if (_origPoly != null) _poly = new List<PointF>(_origPoly); }
            _drag = 0;
            Changed();
        }

        // ------------------------------------------------------------------ handles
        int HandleScreen => Math.Max(5, V.DpiValue * 6 / 96);

        List<PointF> HandlePoints()
        {
            var l = new List<PointF>();
            if (_kind == ShapeKind.Line) { l.Add(_a); l.Add(_b); return l; }
            var r = Extent;
            float cx = (r.Left + r.Right) / 2, cy = (r.Top + r.Bottom) / 2;
            l.Add(new PointF(r.Left, r.Top)); l.Add(new PointF(cx, r.Top)); l.Add(new PointF(r.Right, r.Top)); l.Add(new PointF(r.Right, cy));
            l.Add(new PointF(r.Right, r.Bottom)); l.Add(new PointF(cx, r.Bottom)); l.Add(new PointF(r.Left, r.Bottom)); l.Add(new PointF(r.Left, cy));
            return l;
        }

        int HitHandle(Point view)
        {
            if (_phase != Phase.Pending) return -1;
            var hs = HandlePoints();
            int tol = HandleScreen / 2 + 3;
            for (int i = 0; i < hs.Count; i++)
            {
                var v = V.ImageToViewF(hs[i]);
                if (Math.Abs(v.X - view.X) <= tol && Math.Abs(v.Y - view.Y) <= tol) return i;
            }
            return -1;
        }

        bool HitBody(MouseArgs a)
        {
            if (_phase != Phase.Pending) return false;
            if (_kind == ShapeKind.Line)
            {
                float tol = Math.Max(Width / 2f + 1, 5f / V.Zoom);
                return DistToSegment(a.P, _a, _b) <= tol;
            }
            var r = Extent;
            float pad = Math.Max(1, 3 / V.Zoom);
            return a.P.X >= r.Left - pad && a.P.X <= r.Right + pad && a.P.Y >= r.Top - pad && a.P.Y <= r.Bottom + pad;
        }

        static float DistToSegment(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float l2 = dx * dx + dy * dy;
            float t = l2 < 1e-6f ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2));
            float qx = a.X + t * dx - p.X, qy = a.Y + t * dy - p.Y;
            return (float)Math.Sqrt(qx * qx + qy * qy);
        }

        public override IntPtr GetCursor(MouseArgs a)
        {
            if (_phase == Phase.Pending && !_btnDown)
            {
                int h = HitHandle(a.ViewPt);
                if (h >= 0)
                {
                    if (_kind == ShapeKind.Line) return AppCursors.Crosshair;
                    switch (h) { case 0: case 4: return AppCursors.SizeNWSE; case 2: case 6: return AppCursors.SizeNESW; case 1: case 5: return AppCursors.SizeNS; default: return AppCursors.SizeWE; }
                }
                if (HitBody(a)) return AppCursors.SizeAll;
            }
            if (_drag == 2) return AppCursors.SizeAll;
            return AppCursors.Crosshair;
        }

        // ------------------------------------------------------------------ mouse
        public override void MouseDown(MouseArgs a)
        {
            if (_btnDown) { CancelStroke(); return; }   // the other button cancels the current drag
            if (!a.Left && !a.Right) return;
            _btn = a.Left ? MouseButtons.Left : MouseButtons.Right;
            var p = SnapP(a.P);

            if (_phase == Phase.CurveBend1 || _phase == Phase.CurveBend2)
            {
                _btnDown = true; _drag = 4;
                SetBend(p);
                return;
            }
            if (_phase == Phase.PolyDrawing)
            {
                _btnDown = true; _drag = 5;
                _b = Constrain(_poly[_poly.Count - 1], p, a.Shift);
                Changed();
                return;
            }
            if (_phase == Phase.Pending)
            {
                int h = HitHandle(a.ViewPt);
                if (h >= 0) { StartEdit(3, a); _handle = h; return; }
                if (HitBody(a)) { StartEdit(2, a); return; }
                Commit();
            }
            // start a new shape
            _downView = a.ViewPt;
            _dragged = false;
            _kind = E.ShapeType;
            _swap = a.Right;
            _a = _b = p;
            _c1 = _c2 = p;
            _poly.Clear(); _polyClosed = false;
            if (_kind == ShapeKind.Polygon) _poly.Add(p);
            _phase = Phase.Creating;
            _btnDown = true; _drag = 1;
            Changed();
        }

        void StartEdit(int mode, MouseArgs a)
        {
            _btnDown = true; _drag = mode;
            _dragStart = a.P;
            _origA = _a; _origB = _b; _origC1 = _c1; _origC2 = _c2;
            _origPoly = new List<PointF>(_poly);
            if (_kind != ShapeKind.Line && _kind != ShapeKind.Curve && _kind != ShapeKind.Polygon)
            {
                var r = BoxRect;
                _a = _origA = new PointF(r.Left, r.Top); _b = _origB = new PointF(r.Right, r.Bottom);
            }
        }

        void SetBend(PointF p)
        {
            if (_phase == Phase.CurveBend1) { _c1 = p; _c2 = p; }
            else _c2 = p;
            Changed();
        }

        static PointF Constrain(PointF from, PointF to, bool shift)
        {
            if (!shift) return to;
            float dx = to.X - from.X, dy = to.Y - from.Y;
            double ang = Math.Atan2(dy, dx);
            double snap = Math.Round(ang / (Math.PI / 4)) * (Math.PI / 4);
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (Math.Abs(Math.Cos(snap)) > 0.1 && Math.Abs(Math.Sin(snap)) > 0.1) len = Math.Max(Math.Abs(dx), Math.Abs(dy)) * Math.Sqrt(2);
            return new PointF(from.X + (float)(Math.Cos(snap) * len), from.Y + (float)(Math.Sin(snap) * len));
        }

        public override void MouseMove(MouseArgs a)
        {
            var p = SnapP(a.P);
            if (!_btnDown)
            {
                if (_phase == Phase.PolyDrawing) { _b = Constrain(_poly[_poly.Count - 1], p, a.Shift); Changed(); }
                return;
            }
            if (_drag == 1 && !_dragged)
            {
                int tx = Math.Max(3, Native.MetricForDpi(Native.SM_CXDRAG, V.DpiValue)), ty = Math.Max(3, Native.MetricForDpi(Native.SM_CYDRAG, V.DpiValue));
                if (Math.Abs(a.ViewPt.X - _downView.X) < tx && Math.Abs(a.ViewPt.Y - _downView.Y) < ty) return;
                _dragged = true;
            }
            switch (_drag)
            {
                case 1:
                    if (_kind == ShapeKind.Line || _kind == ShapeKind.Curve || _kind == ShapeKind.Polygon) _b = Constrain(_a, p, a.Shift);
                    else if (a.Shift)
                    {
                        float dx = p.X - _a.X, dy = p.Y - _a.Y, s = Math.Max(Math.Abs(dx), Math.Abs(dy));
                        _b = new PointF(_a.X + Math.Sign(dx == 0 ? 1 : dx) * s, _a.Y + Math.Sign(dy == 0 ? 1 : dy) * s);
                    }
                    else _b = p;
                    if (_kind == ShapeKind.Curve) { _c1 = _a; _c2 = _b; }
                    Changed();
                    break;
                case 2:
                    {
                        float dx = (float)Math.Round(a.P.X - _dragStart.X), dy = (float)Math.Round(a.P.Y - _dragStart.Y);
                        _a = new PointF(_origA.X + dx, _origA.Y + dy); _b = new PointF(_origB.X + dx, _origB.Y + dy);
                        _c1 = new PointF(_origC1.X + dx, _origC1.Y + dy); _c2 = new PointF(_origC2.X + dx, _origC2.Y + dy);
                        if (_kind == ShapeKind.Polygon) { _poly.Clear(); foreach (var q in _origPoly) _poly.Add(new PointF(q.X + dx, q.Y + dy)); }
                        Changed();
                        break;
                    }
                case 3: DragHandle(p, a.Shift); break;
                case 4: SetBend(p); break;
                case 5: _b = Constrain(_poly[_poly.Count - 1], p, a.Shift); Changed(); break;
            }
        }

        void DragHandle(PointF p, bool shift)
        {
            if (_kind == ShapeKind.Line)
            {
                if (_handle == 0) _a = Constrain(_b, p, shift); else _b = Constrain(_a, p, shift);
                Changed();
                return;
            }
            // box style resize of the extent (curve/polygon points are scaled)
            float x0 = _origA.X, y0 = _origA.Y, x1 = _origB.X, y1 = _origB.Y;
            if (_kind == ShapeKind.Curve || _kind == ShapeKind.Polygon)
            {
                var oe = OrigExtent();
                x0 = oe.Left; y0 = oe.Top; x1 = oe.Right; y1 = oe.Bottom;
            }
            switch (_handle)
            {
                case 0: x0 = p.X; y0 = p.Y; break;
                case 1: y0 = p.Y; break;
                case 2: x1 = p.X; y0 = p.Y; break;
                case 3: x1 = p.X; break;
                case 4: x1 = p.X; y1 = p.Y; break;
                case 5: y1 = p.Y; break;
                case 6: x0 = p.X; y1 = p.Y; break;
                case 7: x0 = p.X; break;
            }
            if (_kind == ShapeKind.Curve || _kind == ShapeKind.Polygon)
            {
                var oe = OrigExtent();
                PointF Map(PointF q) => new PointF(
                    x0 + (oe.Width < 0.01f ? 0 : (q.X - oe.Left) / oe.Width * (x1 - x0)),
                    y0 + (oe.Height < 0.01f ? 0 : (q.Y - oe.Top) / oe.Height * (y1 - y0)));
                if (_kind == ShapeKind.Curve) { _a = Map(_origA); _b = Map(_origB); _c1 = Map(_origC1); _c2 = Map(_origC2); }
                else { _poly.Clear(); foreach (var q in _origPoly) _poly.Add(Map(q)); }
            }
            else { _a = new PointF(x0, y0); _b = new PointF(x1, y1); }
            Changed();
        }

        RectangleF OrigExtent()
        {
            if (_kind == ShapeKind.Curve)
            {
                float x0 = Math.Min(Math.Min(_origA.X, _origB.X), Math.Min(_origC1.X, _origC2.X)), x1 = Math.Max(Math.Max(_origA.X, _origB.X), Math.Max(_origC1.X, _origC2.X));
                float y0 = Math.Min(Math.Min(_origA.Y, _origB.Y), Math.Min(_origC1.Y, _origC2.Y)), y1 = Math.Max(Math.Max(_origA.Y, _origB.Y), Math.Max(_origC1.Y, _origC2.Y));
                return RectangleF.FromLTRB(x0, y0, x1, y1);
            }
            float a = float.MaxValue, b = float.MaxValue, c = float.MinValue, d = float.MinValue;
            foreach (var q in _origPoly) { a = Math.Min(a, q.X); b = Math.Min(b, q.Y); c = Math.Max(c, q.X); d = Math.Max(d, q.Y); }
            return RectangleF.FromLTRB(a, b, c, d);
        }

        public override void MouseUp(MouseArgs a)
        {
            if (!_btnDown) return;
            if ((_btn == MouseButtons.Left && !a.Left) || (_btn == MouseButtons.Right && !a.Right)) return;
            _btnDown = false;
            int drag = _drag;
            _drag = 0;
            if (drag == 1 && !_dragged) { Reset(); return; }   // plain click: no shape
            switch (drag)
            {
                case 1:
                    if (_kind == ShapeKind.Curve)
                    {
                        if (Dist(_a, _b) < 0.5f) { Reset(); return; }
                        _phase = Phase.CurveBend1;
                    }
                    else if (_kind == ShapeKind.Polygon)
                    {
                        if (Dist(_a, _b) < 0.5f) { _phase = Phase.PolyDrawing; Changed(); return; }
                        _poly.Add(_b);
                        _phase = Phase.PolyDrawing;
                        _lastPolyClick = DateTime.Now;
                    }
                    else
                    {
                        if (Extent.Width < 0.5f && Extent.Height < 0.5f && _kind != ShapeKind.Line) { Reset(); return; }
                        if (_kind == ShapeKind.Line && Dist(_a, _b) < 0.01f) { Commit(); return; }
                        _phase = Phase.Pending;
                    }
                    Changed();
                    break;
                case 4:
                    if (_phase == Phase.CurveBend1) _phase = Phase.CurveBend2;
                    else { _phase = Phase.Pending; }
                    Changed();
                    break;
                case 5:
                    {
                        // close when clicking on the first point
                        var first = _poly[0];
                        float tol = Math.Max(2f, 5f / V.Zoom);
                        if (_poly.Count >= 2 && Dist(_b, first) <= tol) { ClosePolygon(); return; }
                        _poly.Add(_b);
                        _lastPolyClick = DateTime.Now;
                        Changed();
                        break;
                    }
                default:
                    Changed();
                    break;
            }
        }

        public override void DoubleClick(MouseArgs a)
        {
            if (_phase == Phase.PolyDrawing) { ClosePolygon(); return; }
            MouseDown(a);
        }

        void ClosePolygon()
        {
            // drop a duplicated last vertex produced by the double click
            while (_poly.Count > 2 && Dist(_poly[_poly.Count - 1], _poly[_poly.Count - 2]) < 0.5f) _poly.RemoveAt(_poly.Count - 1);
            if (_poly.Count >= 3 && Dist(_poly[_poly.Count - 1], _poly[0]) < 0.5f) _poly.RemoveAt(_poly.Count - 1);
            _polyClosed = true;
            _phase = Phase.Pending;
            Changed();
        }

        static float Dist(PointF a, PointF b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        public override bool KeyDown(Keys k)
        {
            var code = k & Keys.KeyCode;
            if (_btnDown && code == Keys.Escape) { CancelStroke(); return true; }
            if (_phase == Phase.Idle) return false;
            switch (code)
            {
                case Keys.Escape:
                case Keys.Enter:
                    if (_phase == Phase.PolyDrawing && code == Keys.Enter) { ClosePolygon(); return true; }
                    Commit();
                    return true;
                case Keys.Delete:
                    Reset();
                    return true;
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                    if (_phase != Phase.Pending) return false;
                    {
                        float dx = code == Keys.Left ? -1 : code == Keys.Right ? 1 : 0, dy = code == Keys.Up ? -1 : code == Keys.Down ? 1 : 0;
                        _a.X += dx; _a.Y += dy; _b.X += dx; _b.Y += dy; _c1.X += dx; _c1.Y += dy; _c2.X += dx; _c2.Y += dy;
                        for (int i = 0; i < _poly.Count; i++) _poly[i] = new PointF(_poly[i].X + dx, _poly[i].Y + dy);
                        Changed();
                        return true;
                    }
            }
            return false;
        }

        // ------------------------------------------------------------------ overlay
        public override void PaintScreen(Graphics g)
        {
            if (_phase != Phase.Pending) return;
            g.SmoothingMode = SmoothingMode.None;
            int hs = HandleScreen;
            if (_kind != ShapeKind.Line)
            {
                var r = Extent;
                var p0 = V.ImageToViewF(new PointF(r.Left, r.Top)); var p1 = V.ImageToViewF(new PointF(r.Right, r.Bottom));
                var vr = Rectangle.FromLTRB((int)Math.Round(p0.X), (int)Math.Round(p0.Y), (int)Math.Round(p1.X), (int)Math.Round(p1.Y));
                using (var a = new Pen(Color.White)) g.DrawRectangle(a, vr);
                using (var b = new Pen(Color.FromArgb(0x30, 0x60, 0xA0)) { DashPattern = new[] { 3f, 3f } }) g.DrawRectangle(b, vr);
            }
            foreach (var hp in HandlePoints())
            {
                var v = V.ImageToViewF(hp);
                var hr = new Rectangle((int)Math.Round(v.X) - hs / 2, (int)Math.Round(v.Y) - hs / 2, hs, hs);
                g.FillRectangle(Theme.Brush(Theme.Handle), hr);
                g.DrawRectangle(Theme.Pen(Color.FromArgb(0x50, 0x50, 0x50)), hr.X, hr.Y, hr.Width - 1, hr.Height - 1);
            }
        }

        public override void Deactivate()
        {
            if (_btnDown) CancelStroke();
            if (HasPending) Commit();
        }
    }
}
