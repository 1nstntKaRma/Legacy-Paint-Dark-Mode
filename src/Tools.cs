using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PaintDark
{
    public struct MouseArgs
    {
        public PointF P;          // picture coordinates (sub-pixel)
        public Point Pi;          // picture pixel under the cursor
        public MouseButtons Button;
        public bool Shift, Ctrl, Alt;
        public Point ViewPt;      // canvas control coordinates
        public bool Left => (Button & MouseButtons.Left) != 0;
        public bool Right => (Button & MouseButtons.Right) != 0;
    }

    public abstract class ToolBase
    {
        protected readonly Editor E;
        protected CanvasView V => E.View;
        protected Document Doc => E.Doc;
        protected ToolBase(Editor e) { E = e; }

        public virtual void Activate() { }
        public virtual void Deactivate() { if (InStroke) CancelStroke(); if (HasPending) Commit(); }
        public virtual void DocumentChanged() { }
        public virtual void OptionsChanged() { }
        public virtual void ColorsChanged(int slot) { }
        public virtual bool HasPending => false;
        public virtual void Commit() { }
        public virtual bool UndoPending() => false;
        public virtual bool InStroke => false;
        public virtual void CancelStroke() { }
        public virtual void MouseDown(MouseArgs a) { }
        public virtual void MouseMove(MouseArgs a) { }
        public virtual void MouseUp(MouseArgs a) { }
        public virtual void MouseLeave() { }
        public virtual void DoubleClick(MouseArgs a) { MouseDown(a); }
        public virtual bool KeyDown(Keys k) => false;
        public virtual bool KeyUp(Keys k) => false;
        public virtual bool Char(char c) => false;
        public virtual IntPtr GetCursor(MouseArgs a) => AppCursors.Crosshair;
        public virtual void PaintScreen(Graphics g) { }
        public virtual Rectangle CompositeBounds => Rectangle.Empty;
        public virtual void Composite(Dib target, Rectangle area) { }
        public virtual void Tick() { }
        public virtual bool WantsRightClickMenu(MouseArgs a) => false;
        public virtual string Name => GetType().Name;

        protected uint ColorFor(MouseArgs a) => a.Right ? E.Color2 : E.Color1;
        protected uint OtherColor(MouseArgs a) => a.Right ? E.Color1 : E.Color2;
    }

    /// <summary>Common base for freehand strokes: tracks the dirty area, cancels with Esc / the other button.</summary>
    public abstract class StrokeTool : ToolBase
    {
        protected bool _down;
        protected MouseButtons _btn;
        protected Rectangle _dirty;
        protected Point _start, _last;
        protected PointF _startF, _lastF;
        int _lockAxis; // 0 none, 1 horizontal, 2 vertical, 3 diag \, 4 diag /

        protected StrokeTool(Editor e) : base(e) { }
        public override bool InStroke => _down;

        public override void MouseDown(MouseArgs a)
        {
            if (_down) { CancelStroke(); return; }   // second button cancels, like Paint
            if (!a.Left && !a.Right) return;
            _down = true; _btn = a.Left ? MouseButtons.Left : MouseButtons.Right;
            _dirty = Rectangle.Empty;
            _start = _last = a.Pi; _startF = _lastF = a.P;
            _lockAxis = 0;
            BeginStroke(a);
        }

        public override void MouseMove(MouseArgs a)
        {
            if (!_down) { Hover(a); return; }
            a.Button = _btn;
            if (a.Shift) ConstrainAxis(ref a);
            ContinueStroke(a);
            _last = a.Pi; _lastF = a.P;
        }

        public override void MouseUp(MouseArgs a)
        {
            if (!_down) return;
            if ((_btn == MouseButtons.Left && !a.Left) || (_btn == MouseButtons.Right && !a.Right)) return;
            _down = false;
            EndStroke();
            if (!_dirty.IsEmpty) E.Commit(_dirty, Name);
            _dirty = Rectangle.Empty;
        }

        public override void CancelStroke()
        {
            if (!_down) return;
            _down = false;
            AbortStroke();
            if (!_dirty.IsEmpty) { Doc.Revert(_dirty); E.InvalidateImage(_dirty); }
            _dirty = Rectangle.Empty;
        }

        public override bool KeyDown(Keys k)
        {
            if (k == Keys.Escape && _down) { CancelStroke(); return true; }
            return false;
        }

        void ConstrainAxis(ref MouseArgs a)
        {
            float dx = a.P.X - _startF.X, dy = a.P.Y - _startF.Y;
            if (_lockAxis == 0)
            {
                if (Math.Abs(dx) < 2 && Math.Abs(dy) < 2) return;
                double ang = Math.Atan2(-dy, dx) * 180 / Math.PI;
                if (ang < 0) ang += 180;
                if (ang < 22.5 || ang >= 157.5) _lockAxis = 1;
                else if (ang < 67.5) _lockAxis = 4;
                else if (ang < 112.5) _lockAxis = 2;
                else _lockAxis = 3;
            }
            switch (_lockAxis)
            {
                case 1: a.P.Y = _startF.Y; break;
                case 2: a.P.X = _startF.X; break;
                case 3: { float t = (dx + dy) / 2; a.P.X = _startF.X + t; a.P.Y = _startF.Y + t; break; }
                case 4: { float t = (dx - dy) / 2; a.P.X = _startF.X + t; a.P.Y = _startF.Y - t; break; }
            }
            a.Pi = new Point((int)Math.Floor(a.P.X), (int)Math.Floor(a.P.Y));
        }

        protected void AddDirty(Rectangle r)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            _dirty = Raster.Union(_dirty, r);
            E.InvalidateImage(r);
        }

        protected virtual void Hover(MouseArgs a) { }
        protected abstract void BeginStroke(MouseArgs a);
        protected abstract void ContinueStroke(MouseArgs a);
        protected virtual void EndStroke() { }
        protected virtual void AbortStroke() { }
    }

    public sealed class PencilTool : StrokeTool
    {
        uint _color;
        public PencilTool(Editor e) : base(e) { }
        public override string Name => "Pencil";
        public override IntPtr GetCursor(MouseArgs a) => AppCursors.Pencil;

        protected override void BeginStroke(MouseArgs a)
        {
            _color = ColorFor(a);
            AddDirty(Raster.NibLine(Doc.Image, a.Pi.X, a.Pi.Y, a.Pi.X, a.Pi.Y, E.ToolSize, _color, false));
        }

        protected override void ContinueStroke(MouseArgs a)
        {
            AddDirty(Raster.NibLine(Doc.Image, _last.X, _last.Y, a.Pi.X, a.Pi.Y, E.ToolSize, _color, false));
        }
    }

    public sealed class EraserTool : StrokeTool
    {
        Point? _hover;
        bool _replace;
        uint _from, _to;
        public EraserTool(Editor e) : base(e) { }
        public override string Name => "Eraser";
        public override IntPtr GetCursor(MouseArgs a) => V != null && V.ZoomedSize(E.ToolSize) >= 3 ? IntPtr.Zero : AppCursors.Crosshair;

        protected override void BeginStroke(MouseArgs a)
        {
            _replace = a.Right;
            _from = E.Color1; _to = E.Color2;
            _hover = a.Pi;
            Stamp(a.Pi, a.Pi);
        }

        protected override void ContinueStroke(MouseArgs a)
        {
            MoveHover(a.Pi);
            Stamp(_last, a.Pi);
        }

        void Stamp(Point p0, Point p1)
        {
            // eraser square: right button replaces only color 1 with color 2
            AddDirty(Raster.NibLine(Doc.Image, p0.X, p0.Y, p1.X, p1.Y, E.ToolSize, _to, true, _replace, _from | 0xFF000000u));
        }

        protected override void Hover(MouseArgs a) => MoveHover(a.Pi);

        void MoveHover(Point p)
        {
            if (_hover == p) return;
            InvalidateHover();
            _hover = p;
            InvalidateHover();
        }

        void InvalidateHover()
        {
            if (_hover == null || V == null) return;
            V.InvalidateImageOutline(NibRect(_hover.Value), 2);
        }

        Rectangle NibRect(Point p)
        {
            int s = E.ToolSize;
            return new Rectangle(p.X - s / 2, p.Y - s / 2, s, s);
        }

        public override void MouseLeave() { InvalidateHover(); _hover = null; }
        public override void OptionsChanged() { InvalidateHover(); }

        public override void PaintScreen(Graphics g)
        {
            if (_hover == null || V == null || V.ZoomedSize(E.ToolSize) < 3) return;
            var r = V.ImageToView(NibRect(_hover.Value));
            g.SmoothingMode = SmoothingMode.None;
            g.FillRectangle(Theme.Brush(Px.ToColor(E.Color2)), r.X + 1, r.Y + 1, r.Width - 1, r.Height - 1);
            g.DrawRectangle(Theme.Pen(Color.Black), r.X, r.Y, r.Width, r.Height);
            g.DrawRectangle(Theme.Pen(Color.FromArgb(200, 255, 255, 255)), r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2);
        }
    }

    public sealed class FillTool : ToolBase
    {
        public FillTool(Editor e) : base(e) { }
        public override string Name => "Fill with color";
        public override IntPtr GetCursor(MouseArgs a) => AppCursors.Fill;
        public override void MouseDown(MouseArgs a)
        {
            if (!a.Left && !a.Right) return;
            var r = Raster.FloodFill(Doc.Image, a.Pi.X, a.Pi.Y, ColorFor(a));
            if (r.IsEmpty) return;
            E.InvalidateImage(r);
            E.Commit(r, Name);
        }
    }

    public sealed class PickerTool : ToolBase
    {
        bool _down;
        MouseButtons _btn;
        public PickerTool(Editor e) : base(e) { }
        public override string Name => "Color picker";
        public override IntPtr GetCursor(MouseArgs a) => AppCursors.Picker;
        public override bool InStroke => _down;

        public override void MouseDown(MouseArgs a)
        {
            if (!a.Left && !a.Right) return;
            _down = true; _btn = a.Left ? MouseButtons.Left : MouseButtons.Right;
            Pick(a);
        }

        public override void MouseMove(MouseArgs a) { if (_down) { a.Button = _btn; Pick(a); } }

        void Pick(MouseArgs a)
        {
            if (a.Pi.X < 0 || a.Pi.Y < 0 || a.Pi.X >= Doc.Width || a.Pi.Y >= Doc.Height) return;
            uint c = Doc.Image.Get(a.Pi.X, a.Pi.Y);
            if (Px.A(c) < 255) c = Px.Blend(0xFFFFFFFFu, c | 0xFF000000u, Px.A(c));
            E.SetColor(_btn == MouseButtons.Left ? 1 : 2, c);
        }

        public override void MouseUp(MouseArgs a)
        {
            if (!_down) return;
            _down = false;
            E.ReturnToPreviousTool();
        }

        public override void CancelStroke() { _down = false; }
    }

    public sealed class MagnifierTool : ToolBase
    {
        Point? _hover;
        public MagnifierTool(Editor e) : base(e) { }
        public override string Name => "Magnifier";
        public override IntPtr GetCursor(MouseArgs a) => AppCursors.Magnifier;

        Rectangle ZoomRect(Point p)
        {
            float next = CanvasView.NextZoom(V.Zoom, +1);
            if (next <= V.Zoom) return Rectangle.Empty;
            var vr = V.ImageViewport;
            int w = (int)Math.Ceiling(vr.Width / next), h = (int)Math.Ceiling(vr.Height / next);
            w = Math.Min(w, Doc.Width); h = Math.Min(h, Doc.Height);
            int x = Math.Max(0, Math.Min(Doc.Width - w, p.X - w / 2));
            int y = Math.Max(0, Math.Min(Doc.Height - h, p.Y - h / 2));
            return new Rectangle(x, y, w, h);
        }

        public override void MouseMove(MouseArgs a)
        {
            var old = _hover;
            _hover = a.Pi;
            if (old != null) V.InvalidateImageOutline(ZoomRect(old.Value), 3);
            V.InvalidateImageOutline(ZoomRect(a.Pi), 3);
        }

        public override void MouseLeave()
        {
            if (_hover != null) V.InvalidateImageOutline(ZoomRect(_hover.Value), 3);
            _hover = null;
        }

        public override void MouseDown(MouseArgs a)
        {
            if (_hover != null) V.InvalidateImageOutline(ZoomRect(_hover.Value), 3);
            if (a.Left)
            {
                var r = ZoomRect(a.Pi);
                float next = CanvasView.NextZoom(V.Zoom, +1);
                if (r.IsEmpty) return;
                V.SetZoom(next, null);
                V.ScrollToImagePoint(new PointF(r.Left, r.Top));
            }
            else if (a.Right) V.SetZoom(CanvasView.NextZoom(V.Zoom, -1), a.ViewPt);
            _hover = null;
        }

        public override void PaintScreen(Graphics g)
        {
            if (_hover == null) return;
            var r = ZoomRect(_hover.Value);
            if (r.IsEmpty) return;
            var vr = V.ImageToView(r);
            g.SmoothingMode = SmoothingMode.None;
            g.DrawRectangle(Theme.Pen(Color.Black), vr.X, vr.Y, vr.Width, vr.Height);
            g.DrawRectangle(Theme.Pen(Color.White), vr.X + 1, vr.Y + 1, vr.Width - 2, vr.Height - 2);
        }
    }

    /// <summary>
    /// Classic Paint cursors: small black-outlined white art (about 16px at 100%) with the hotspot on the tip.
    /// Selection / shapes use the system's thin inverting crosshair, like the original.
    /// </summary>
    public static class AppCursors
    {
        public static IntPtr Arrow = Native.LoadCursor(IntPtr.Zero, 32512);
        public static IntPtr IBeam = Native.LoadCursor(IntPtr.Zero, 32513);
        public static IntPtr Cross = Native.LoadCursor(IntPtr.Zero, 32515);
        public static IntPtr SizeNWSE = Native.LoadCursor(IntPtr.Zero, 32642);
        public static IntPtr SizeNESW = Native.LoadCursor(IntPtr.Zero, 32643);
        public static IntPtr SizeWE = Native.LoadCursor(IntPtr.Zero, 32644);
        public static IntPtr SizeNS = Native.LoadCursor(IntPtr.Zero, 32645);
        public static IntPtr SizeAll = Native.LoadCursor(IntPtr.Zero, 32646);
        public static IntPtr Hand = Native.LoadCursor(IntPtr.Zero, 32649);
        public static IntPtr No = Native.LoadCursor(IntPtr.Zero, 32648);

        public static IntPtr Crosshair = Cross, Pencil, Fill, Picker, Magnifier;
        static int _dpi;

        public static void Init(int dpi)
        {
            if (dpi == _dpi) return;
            _dpi = dpi;
            int sz = Native.MetricForDpi(13 /*SM_CXCURSOR*/, dpi);
            if (sz < 32) sz = Math.Max(32, (int)Math.Round(32 * dpi / 96.0));
            Crosshair = Cross;
            Pencil = Make(sz, 1, 16, DrawPencil);
            Fill = Make(sz, 2, 15, DrawBucket);
            Picker = Make(sz, 1, 16, DrawPicker);
            Magnifier = Make(sz, 6, 6, DrawMagnifier);
        }

        /// <summary>Art is designed on a 32-unit cursor grid (only the top-left ~17 units are used, like real cursors).</summary>
        static IntPtr Make(int size, float hx, float hy, Action<Graphics> draw)
        {
            float s = size / 32f;
            using (var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = s == 1f ? SmoothingMode.None : SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.ScaleTransform(s, s);
                    draw(g);
                }
                return Build(bmp, (int)Math.Floor(hx * s), (int)Math.Floor(hy * s));
            }
        }

        static unsafe IntPtr Build(Bitmap bmp, int hx, int hy)
        {
            int w = bmp.Width, h = bmp.Height;
            var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            IntPtr color = Native.CreateDIBSection(IntPtr.Zero, ref bi, 0, out IntPtr bits, IntPtr.Zero, 0);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            for (int y = 0; y < h; y++)
                Buffer.MemoryCopy((byte*)data.Scan0 + y * data.Stride, (byte*)bits + y * w * 4, w * 4, w * 4);
            bmp.UnlockBits(data);
            // faint light halo around dark pixels: invisible on white, keeps the shape readable on a black canvas
            uint* px = (uint*)bits;
            var src = new uint[w * h];
            for (int i = 0; i < src.Length; i++) src[i] = px[i];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    if ((src[y * w + x] >> 24) > 40) continue;
                    bool nearDark = false;
                    for (int dy = -1; dy <= 1 && !nearDark; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if ((uint)nx >= (uint)w || (uint)ny >= (uint)h) continue;
                            uint c = src[ny * w + nx];
                            if ((c >> 24) > 140 && ((c >> 16) & 255) < 90) { nearDark = true; break; }
                        }
                    if (nearDark) px[y * w + x] = 0x90FFFFFFu;
                }
            IntPtr mask = Native.CreateBitmap(w, h, 1, 1, IntPtr.Zero);
            var ii = new ICONINFO { fIcon = false, xHotspot = hx, yHotspot = hy, hbmMask = mask, hbmColor = color };
            IntPtr cur = Native.CreateIconIndirect(ref ii);
            Native.DeleteObject(color); Native.DeleteObject(mask);
            return cur == IntPtr.Zero ? Cross : cur;
        }

        static readonly Pen Ink = new Pen(Color.Black, 1f) { LineJoin = LineJoin.Miter };

        static void Shape(Graphics g, params float[] xy)
        {
            var pts = new PointF[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(xy[i * 2], xy[i * 2 + 1]);
            g.FillPolygon(Brushes.White, pts);
            g.DrawPolygon(Ink, pts);
        }

        // pencil pointing to the lower left, tip = hotspot (1,16)
        static void DrawPencil(Graphics g)
        {
            Shape(g, 1, 16, 2, 12, 12, 2, 15, 5, 5, 15);          // body + cone outline
            g.DrawLine(Ink, 2, 12, 5, 15);                         // where the wood starts
            g.DrawLine(Ink, 10, 4, 13, 7);                         // eraser band
            g.FillPolygon(Brushes.Black, new[] { new PointF(1, 16), new PointF(1.6f, 13.6f), new PointF(3.4f, 15.4f) });
        }

        // tilted paint bucket pouring to the left, drip = hotspot (2,15)
        static void DrawBucket(Graphics g)
        {
            Shape(g, 9, 3, 16, 10, 10, 16, 3, 9);                 // can
            g.DrawLine(Ink, 9, 3, 7, 1);                           // handle
            g.DrawArc(Ink, 5, -1, 6, 6, 180, 180);
            g.FillPolygon(Brushes.Black, new[] { new PointF(3, 9), new PointF(6, 6), new PointF(5, 9), new PointF(3, 12) });   // paint at the lip
            g.FillRectangle(Brushes.Black, 2, 10, 1.6f, 5);       // pouring stream
            g.FillEllipse(Brushes.Black, 1.2f, 14, 2.4f, 2.6f);   // drop
        }

        // eyedropper, tip = hotspot (1,16)
        static void DrawPicker(Graphics g)
        {
            Shape(g, 1, 16, 1, 14, 9, 6, 11, 8, 3, 16);            // glass tube
            Shape(g, 8, 5, 12, 1, 16, 5, 12, 9);                   // bulb
            g.FillPolygon(Brushes.Black, new[] { new PointF(9, 4), new PointF(12, 1), new PointF(16, 5), new PointF(13, 8) });
        }

        // magnifying glass, centre of the lens = hotspot (6,6)
        static void DrawMagnifier(Graphics g)
        {
            using (var handle = new Pen(Color.Black, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawLine(handle, 10.5f, 10.5f, 15.5f, 15.5f);
            g.FillEllipse(Brushes.White, 1, 1, 10, 10);
            g.DrawEllipse(Ink, 1, 1, 10, 10);
            using (var shine = new Pen(Color.FromArgb(160, 0, 0, 0), 1f)) g.DrawArc(shine, 3, 3, 6, 6, 190, 70);
        }
    }
}
