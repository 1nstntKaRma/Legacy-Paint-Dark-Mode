using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PaintDark
{
    /// <summary>Dark scrollbar drawn inside its host control.</summary>
    public sealed class DarkScroll
    {
        public bool Vertical;
        public Rectangle Bounds;
        public bool Visible;
        public int Value, Max, Page, Line = 40;
        public event Action Scrolled;
        int _hot, _press;           // 1 arrow-, 2 track-, 3 thumb, 4 track+, 5 arrow+
        int _dragOffset;
        readonly Timer _repeat = new Timer();
        readonly Control _host;

        public DarkScroll(Control host, bool vertical)
        {
            _host = host; Vertical = vertical;
            _repeat.Tick += (s, e) => { _repeat.Interval = 50; Step(); };
        }

        public int MaxValue => Math.Max(0, Max - Page);
        public bool Dragging => _press != 0;

        public bool SetValue(int v)
        {
            v = Math.Max(0, Math.Min(MaxValue, v));
            if (v == Value) return false;
            Value = v;
            _host.Invalidate(Bounds);
            Scrolled?.Invoke();
            return true;
        }

        int ArrowLen => Vertical ? Math.Min(Bounds.Width, Bounds.Height / 2) : Math.Min(Bounds.Height, Bounds.Width / 2);
        Rectangle Arrow1 => Vertical ? new Rectangle(Bounds.X, Bounds.Y, Bounds.Width, ArrowLen) : new Rectangle(Bounds.X, Bounds.Y, ArrowLen, Bounds.Height);
        Rectangle Arrow2 => Vertical ? new Rectangle(Bounds.X, Bounds.Bottom - ArrowLen, Bounds.Width, ArrowLen) : new Rectangle(Bounds.Right - ArrowLen, Bounds.Y, ArrowLen, Bounds.Height);
        Rectangle Track => Vertical ? Rectangle.FromLTRB(Bounds.X, Bounds.Y + ArrowLen, Bounds.Right, Bounds.Bottom - ArrowLen) : Rectangle.FromLTRB(Bounds.X + ArrowLen, Bounds.Y, Bounds.Right - ArrowLen, Bounds.Bottom);

        Rectangle Thumb
        {
            get
            {
                var t = Track;
                int len = Vertical ? t.Height : t.Width;
                if (Max <= 0 || Page >= Max) return Rectangle.Empty;
                int tl = Math.Max(Math.Min(len, Vertical ? Bounds.Width * 2 : Bounds.Height * 2), (int)((long)len * Page / Max));
                int pos = MaxValue == 0 ? 0 : (int)((long)(len - tl) * Value / MaxValue);
                return Vertical ? new Rectangle(t.X, t.Y + pos, t.Width, tl) : new Rectangle(t.X + pos, t.Y, tl, t.Height);
            }
        }

        int HitPart(Point p)
        {
            if (!Visible || !Bounds.Contains(p)) return 0;
            if (Arrow1.Contains(p)) return 1;
            if (Arrow2.Contains(p)) return 5;
            var th = Thumb;
            if (th.Contains(p)) return 3;
            if (Vertical ? p.Y < th.Y : p.X < th.X) return 2;
            return 4;
        }

        public bool Contains(Point p) => Visible && Bounds.Contains(p);

        public void MouseMove(Point p)
        {
            if (_press == 3)
            {
                var t = Track; var th = Thumb;
                int len = (Vertical ? t.Height : t.Width) - (Vertical ? th.Height : th.Width);
                int pos = (Vertical ? p.Y - t.Y : p.X - t.X) - _dragOffset;
                if (len > 0) SetValue((int)((long)pos * MaxValue / len));
                return;
            }
            int h = HitPart(p);
            if (h != _hot) { _hot = h; _host.Invalidate(Bounds); }
        }

        public void MouseLeave() { if (_hot != 0 && _press == 0) { _hot = 0; _host.Invalidate(Bounds); } }

        public bool MouseDown(Point p)
        {
            int h = HitPart(p);
            if (h == 0) return false;
            _press = h; _hot = h;
            if (h == 3)
            {
                var th = Thumb;
                _dragOffset = Vertical ? p.Y - th.Y : p.X - th.X;
            }
            else
            {
                Step();
                _repeat.Interval = 350; _repeat.Start();
            }
            _host.Invalidate(Bounds);
            return true;
        }

        public void MouseUp()
        {
            _repeat.Stop();
            _press = 0;
            _host.Invalidate(Bounds);
        }

        void Step()
        {
            var p = _host.PointToClient(Cursor.Position);
            int h = HitPart(p);
            if (_press == 2 || _press == 4)
            {
                if (h != _press) return;   // track paging stops under the thumb
                SetValue(Value + (_press == 2 ? -Page : Page) * 9 / 10);
            }
            else if (_press == 1) SetValue(Value - Line);
            else if (_press == 5) SetValue(Value + Line);
        }

        public void Paint(Graphics g, Dpi d)
        {
            if (!Visible) return;
            Draw.Fill(g, Bounds, Theme.ScrollTrack);
            var th = Thumb;
            if (!th.IsEmpty)
            {
                var c = _press == 3 ? Theme.ScrollThumbDown : (_hot == 3 ? Theme.ScrollThumbHover : Theme.ScrollThumb);
                int inset = Math.Max(2, d.S(3));
                var r = Vertical ? Rectangle.Inflate(th, -inset, -1) : Rectangle.Inflate(th, -1, -inset);
                Draw.Fill(g, r, c);
            }
            var a1 = Arrow1; var a2 = Arrow2;
            if (_hot == 1 || _press == 1) Draw.Fill(g, a1, _press == 1 ? Theme.Pressed : Theme.Hover);
            if (_hot == 5 || _press == 5) Draw.Fill(g, a2, _press == 5 ? Theme.Pressed : Theme.Hover);
            var ac = Theme.ScrollArrow;
            if (Vertical)
            {
                Draw.Chevron(g, a1.X + a1.Width / 2, a1.Y + a1.Height / 2, ac, d, true);
                Draw.Chevron(g, a2.X + a2.Width / 2, a2.Y + a2.Height / 2, ac, d, false);
            }
            else
            {
                DrawSideArrow(g, a1, ac, d, true);
                DrawSideArrow(g, a2, ac, d, false);
            }
        }

        static void DrawSideArrow(Graphics g, Rectangle r, Color c, Dpi d, bool left)
        {
            int h = Math.Max(5, d.S(5)) | 1; int w = (h + 1) / 2;
            int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            for (int i = 0; i < w; i++)
            {
                int x = left ? cx - w / 2 + i : cx + w / 2 - i;
                g.FillRectangle(Theme.Brush(c), x, cy - i, 1, 2 * i + 1);
            }
        }
    }

    /// <summary>The picture area: draws the picture at the current zoom, routes mouse/keyboard to the active tool.</summary>
    public sealed class CanvasView : DarkControl
    {
        readonly Editor E;
        float _zoom = 1f;
        int _zn = 1, _zd = 1;   // the zoom as an exact fraction: _zd picture pixels span _zn screen pixels
        public float Zoom { get => _zoom; private set { _zoom = value; Ratio(value, out _zn, out _zd); } }
        public static readonly float[] Levels =
        {
            0.1f, 0.125f, 0.15f, 0.2f, 0.25f, 1f / 3, 0.4f, 0.5f, 0.6f, 0.7f, 0.75f, 0.8f, 0.9f,
            1f, 1.25f, 1.5f, 1.75f, 2f, 2.5f, 3f, 4f, 5f, 6f, 7f, 8f,
        };
        bool _autoFit;          // an opened picture keeps fitting the window until the user zooms or starts working
        int _sx, _sy;
        bool _rulers, _grid;
        readonly DarkScroll _hs, _vs;
        Dib _scratch;
        // zoomed-out display cache (box-filtered), updated only where the picture changes
        Dib _mip, _mipSrc, _ovMip, _red, _viewBuf;
        int _mipK;
        bool _mipPremul;        // the cache holds premultiplied pixels with alpha (transparent pictures)
        Rectangle _mipDirty;
        int _resizeMode; Size _resizeSize; Point _resizeStart;
        bool _panning; Point _panStart; int _panSX, _panSY;
        bool _toolDown;
        Point _lastMouse = new Point(-1, -1);
        readonly Timer _toolTimer = new Timer();
        DarkScroll _scrollCapture;

        public CanvasView(Editor e)
        {
            E = e;
            _hs = new DarkScroll(this, false);
            _vs = new DarkScroll(this, true);
            _hs.Scrolled += () => { _sx = _hs.Value; Invalidate(); Scrolled?.Invoke(); };
            _vs.Scrolled += () => { _sy = _vs.Value; Invalidate(); Scrolled?.Invoke(); };
            _toolTimer.Interval = 15;
            _toolTimer.Tick += (s, a) => E.ActiveTool?.Tick();
            TabStop = true;
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, true);
            AllowDrop = true;
        }

        public event Action Scrolled;
        public event Action ZoomChanged;

        public bool Rulers { get => _rulers; set { _rulers = value; UpdateScrollbars(); Invalidate(); } }
        public bool Grid { get => _grid; set { _grid = value; Invalidate(); } }

        int CanvasMargin => D.S(5);
        int RulerSize => _rulers ? D.S(17) : 0;
        int SBSize => D.S(17);
        int HandleSize => Math.Max(5, D.S(5));

        Rectangle ViewArea
        {
            get
            {
                int r = RulerSize;
                return Rectangle.FromLTRB(r, r, ClientSize.Width - (_vs.Visible ? SBSize : 0), ClientSize.Height - (_hs.Visible ? SBSize : 0));
            }
        }

        int OriginX => ViewArea.X + CanvasMargin - _sx;
        int OriginY => ViewArea.Y + CanvasMargin - _sy;
        Document Doc => E.Doc;

        public Rectangle CanvasRect => new Rectangle(OriginX, OriginY, Scaled(Doc.Width), Scaled(Doc.Height));
        int Scaled(int v) => (int)Math.Round((double)v * _zn / _zd);
        public float ZoomedSize(int px) => px * Zoom;

        public Rectangle ImageViewport
        {
            get
            {
                var va = ViewArea;
                var p0 = ViewToImage(va.Location); var p1 = ViewToImage(new Point(va.Right, va.Bottom));
                var r = Rectangle.FromLTRB((int)Math.Floor(p0.X), (int)Math.Floor(p0.Y), (int)Math.Ceiling(p1.X), (int)Math.Ceiling(p1.Y));
                r.Intersect(Doc.Bounds);
                return r;
            }
        }

        public PointF ViewToImage(Point p) => new PointF((p.X - OriginX) / Zoom, (p.Y - OriginY) / Zoom);
        public PointF ViewToImage(PointF p) => new PointF((p.X - OriginX) / Zoom, (p.Y - OriginY) / Zoom);
        public PointF ImageToViewF(PointF p) => new PointF(p.X * Zoom + OriginX, p.Y * Zoom + OriginY);
        public Point ImageToView(Point p) => new Point(Scaled(p.X) + OriginX, Scaled(p.Y) + OriginY);
        public Rectangle ImageToView(Rectangle r)
        {
            int x0 = (int)Math.Floor((double)r.Left * _zn / _zd) + OriginX, y0 = (int)Math.Floor((double)r.Top * _zn / _zd) + OriginY;
            int x1 = (int)Math.Ceiling((double)r.Right * _zn / _zd) + OriginX, y1 = (int)Math.Ceiling((double)r.Bottom * _zn / _zd) + OriginY;
            return Rectangle.FromLTRB(x0, y0, x1, y1);
        }

        // ------------------------------------------------------------------ zoom & scroll
        public static float NextZoom(float z, int dir)
        {
            if (dir > 0) { foreach (var l in Levels) if (l > z + 0.0001f) return l; return Levels[Levels.Length - 1]; }
            for (int i = Levels.Length - 1; i >= 0; i--) if (Levels[i] < z - 0.0001f) return Levels[i];
            return Levels[0];
        }

        static float NearestLevel(float z)
        {
            float best = Levels[0];
            foreach (var l in Levels) if (Math.Abs(Math.Log(l / z)) < Math.Abs(Math.Log(best / z))) best = l;
            return best;
        }

        static void Ratio(float z, out int n, out int d)
        {
            for (d = 1; d <= 64; d++)
            {
                double v = z * (double)d;
                n = (int)Math.Round(v);
                if (n > 0 && Math.Abs(v - n) < 1e-3) return;
            }
            d = 64; n = Math.Max(1, (int)Math.Round(z * 64.0));
        }

        static int Gcd(int a, int b) { while (b != 0) { int t = a % b; a = b; b = t; } return a; }

        /// <summary>Zoomed out, the picture is first box-filtered by this whole factor (cached), then stretched by what is left.</summary>
        int MipFactor => _zn < _zd ? _zd / _zn : 1;

        /// <summary>Whole blocks of <paramref name="bi"/> picture pixels land on exactly <paramref name="bv"/> screen pixels (and whole mip pixels).</summary>
        void Blocks(out int bi, out int bv)
        {
            int k = MipFactor;
            bi = _zd / Gcd(_zd, k) * k;
            bv = bi / _zd * _zn;
        }

        /// <summary>Would a picture area of size s be shown whole at zoom z (scroll bars appear when the whole canvas does not fit)?</summary>
        bool Fits(Size s, float z)
        {
            int pad = CanvasMargin * 2 + HandleSize * 2;
            int cw = pad + (int)Math.Round(Doc.Width * (double)z), ch = pad + (int)Math.Round(Doc.Height * (double)z);
            int w = ClientSize.Width - RulerSize, h = ClientSize.Height - RulerSize;
            bool hv = cw > w, vv = ch > h;
            if (hv && !vv) vv = ch > h - SBSize;
            if (vv && !hv) hv = cw > w - SBSize;
            int vw = w - (vv ? SBSize : 0), vh = h - (hv ? SBSize : 0);
            return pad + (int)Math.Round(s.Width * (double)z) <= vw && pad + (int)Math.Round(s.Height * (double)z) <= vh;
        }

        /// <summary>The largest zoom level, at most <paramref name="max"/>, that shows a picture area of size s whole.</summary>
        float FitZoom(Size s, float max)
        {
            for (int i = Levels.Length - 1; i >= 0; i--)
                if (Levels[i] <= max + 0.0001f && Fits(s, Levels[i])) return Levels[i];
            return Levels[0];
        }

        bool CanFit => ClientSize.Width > D.S(120) && ClientSize.Height > D.S(120);

        /// <summary>Zooms out (never in) until a picture area of size s, e.g. a pasted picture, fits the window.</summary>
        public void ZoomToShow(Size s)
        {
            if (Doc == null || !CanFit || Fits(s, Zoom)) return;
            SetZoom(FitZoom(s, Zoom), null);
        }

        /// <summary>Scrolls so the picture rect r is in view, when it is not already.</summary>
        public void ScrollIntoView(Rectangle r)
        {
            var rd = Rectangle.Intersect(r, Doc.Bounds);
            if (rd.IsEmpty || ImageViewport.Contains(rd)) return;
            ScrollToImagePoint(new PointF(rd.Left, rd.Top));
        }

        void ApplyAutoFit()
        {
            if (!CanFit) return;
            float z = FitZoom(Doc.Size, 1f);
            if (Math.Abs(z - Zoom) < 0.0001f) return;
            Zoom = z;
            _sx = _sy = 0;
            UpdateScrollbars();
            AppCursors.Init(D.Value);
            Invalidate();
            ZoomChanged?.Invoke();
            E.Notify(Change.Zoom);
        }

        public void SetZoom(float z, Point? anchor)
        {
            _autoFit = false;
            z = NearestLevel(Math.Max(Levels[0], Math.Min(Levels[Levels.Length - 1], z)));
            if (Math.Abs(z - Zoom) < 0.0001f) return;
            var va = ViewArea;
            Point a = anchor ?? new Point(va.X + va.Width / 2, va.Y + va.Height / 2);
            var img = ViewToImage(a);
            img.X = Math.Max(0, Math.Min(Doc.Width, img.X));
            img.Y = Math.Max(0, Math.Min(Doc.Height, img.Y));
            Zoom = z;
            UpdateScrollbars();
            // keep the picture point under the anchor
            int nsx = (int)Math.Round(img.X * Zoom + ViewArea.X + CanvasMargin - a.X);
            int nsy = (int)Math.Round(img.Y * Zoom + ViewArea.Y + CanvasMargin - a.Y);
            SetScroll(nsx, nsy);
            AppCursors.Init(D.Value);
            Invalidate();
            ZoomChanged?.Invoke();
            E.Notify(Change.Zoom);
        }

        public void ScrollToImagePoint(PointF p) => SetScroll((int)Math.Round(p.X * Zoom), (int)Math.Round(p.Y * Zoom));

        public void SetScroll(int x, int y)
        {
            x = Math.Max(0, Math.Min(_hs.MaxValue, x));
            y = Math.Max(0, Math.Min(_vs.MaxValue, y));
            if (!_hs.Visible) x = 0;
            if (!_vs.Visible) y = 0;
            _hs.Value = x; _vs.Value = y;
            if (x != _sx || y != _sy) { _sx = x; _sy = y; Invalidate(); Scrolled?.Invoke(); }
        }

        public void ScrollBy(int dx, int dy) => SetScroll(_sx + dx, _sy + dy);

        void UpdateScrollbars()
        {
            if (Doc == null) return;
            int cw = CanvasMargin + Scaled(Doc.Width) + CanvasMargin + HandleSize * 2;
            int ch = CanvasMargin + Scaled(Doc.Height) + CanvasMargin + HandleSize * 2;
            int w = ClientSize.Width - RulerSize, h = ClientSize.Height - RulerSize;
            bool hv = cw > w, vv = ch > h;
            if (hv && !vv) vv = ch > h - SBSize;
            if (vv && !hv) hv = cw > w - SBSize;
            _hs.Visible = hv; _vs.Visible = vv;
            int vw = w - (vv ? SBSize : 0), vh = h - (hv ? SBSize : 0);
            _hs.Bounds = new Rectangle(RulerSize, ClientSize.Height - SBSize, vw, SBSize);
            _vs.Bounds = new Rectangle(ClientSize.Width - SBSize, RulerSize, SBSize, vh);
            _hs.Max = cw; _hs.Page = vw; _hs.Line = D.S(40);
            _vs.Max = ch; _vs.Page = vh; _vs.Line = D.S(40);
            _hs.Value = Math.Max(0, Math.Min(_hs.MaxValue, _sx));
            _vs.Value = Math.Max(0, Math.Min(_vs.MaxValue, _sy));
            if (!hv) _hs.Value = 0;
            if (!vv) _vs.Value = 0;
            _sx = _hs.Value; _sy = _vs.Value;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_autoFit && Doc != null) ApplyAutoFit();
            UpdateScrollbars();
            Invalidate();
        }

        protected override void OnDpiApplied()
        {
            AppCursors.Init(D.Value);
            UpdateScrollbars();
        }

        public void DocumentReplaced()
        {
            _mipSrc = null;
            _sx = _sy = 0;
            // open pictures at the largest zoom level (up to 100%) that shows them whole, and keep fitting while the
            // window settles or is resized, until the user zooms or starts working
            _autoFit = !string.IsNullOrEmpty(Doc.FilePath);
            Zoom = _autoFit && CanFit ? FitZoom(Doc.Size, 1f) : 1f;
            UpdateScrollbars();
            Invalidate();
            ZoomChanged?.Invoke();
        }

        public void ImageSizeChanged()
        {
            _mipSrc = null;
            UpdateScrollbars();
            SetScroll(_sx, _sy);
            Invalidate();
        }

        public void ToolChanged()
        {
            UpdateCursor();
            Invalidate();
        }

        public void InvalidateImage(Rectangle r)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            if (_mip != null) _mipDirty = Raster.Union(_mipDirty, r);
            var v = ImageToView(r);
            v.Inflate(2, 2);
            Invalidate(v);
        }

        public void InvalidateImageOutline(Rectangle r, int pad)
        {
            if (r.Width < 0 || r.Height < 0) return;
            var v = ImageToView(r);
            v.Inflate(pad + 1, pad + 1);
            Invalidate(v);
        }

        public void StartToolTimer(int ms) { _toolTimer.Interval = Math.Max(1, ms); _toolTimer.Start(); }
        public void StopToolTimer() => _toolTimer.Stop();

        // ------------------------------------------------------------------ painting
        protected override void Render(Graphics g, Rectangle clip)
        {
            Draw.Fill(g, clip, Theme.Workspace);
            if (Doc == null) return;
            var va = ViewArea;
            var cr = CanvasRect;

            g.SetClip(Rectangle.Intersect(clip, va));
            // soft shadow + edge so the canvas reads against the dark workspace
            using (var sh = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            {
                g.FillRectangle(sh, cr.Right, cr.Top + D.S(3), D.S(3), cr.Height);
                g.FillRectangle(sh, cr.Left + D.S(3), cr.Bottom, cr.Width, D.S(3));
            }
            Draw.Rect(g, Rectangle.Inflate(cr, 1, 1), Theme.CanvasBorder);

            var vis = Rectangle.Intersect(Rectangle.Intersect(cr, clip), va);
            if (vis.Width > 0 && vis.Height > 0) DrawPicture(g, vis);

            if (_grid) DrawGrid(g, vis);

            var smooth = g.SmoothingMode;
            E.ActiveTool?.PaintScreen(g);
            g.SmoothingMode = smooth;

            DrawCanvasHandles(g);
            g.ResetClip();
            g.SetClip(clip);
            if (_rulers) DrawRulers(g, clip);
            _hs.Paint(g, D); _vs.Paint(g, D);
            if (_hs.Visible && _vs.Visible)
                Draw.Fill(g, new Rectangle(_vs.Bounds.X, _hs.Bounds.Y, SBSize, SBSize), Theme.ScrollTrack);
        }

        unsafe void DrawPicture(Graphics g, Rectangle vis)
        {
            int k = MipFactor;
            bool halftone = _zn < _zd;                 // zoomed out: average the pixels instead of dropping them
            bool exact = _zn * k == _zd;               // whole mip pixels land on whole screen pixels
            // the visible part of the picture in whole zoom blocks; stretched halftone gets one more block on every side
            // so its filter edges fall outside the area being painted
            var src = VisibleSource(vis, halftone && !exact && !Doc.HasAlpha ? 1 : 0);
            if (src.Width <= 0 || src.Height <= 0) return;
            Blocks(out int bi, out _);

            var tool = E.ActiveTool;
            Rectangle ov = tool != null ? tool.CompositeBounds : Rectangle.Empty;
            ov.Intersect(src);
            bool hasOv = ov.Width > 0 && ov.Height > 0;

            IntPtr hdc = g.GetHdc();
            int saved = 0;
            try
            {
                Native.SetStretchBltMode(hdc, halftone ? Native.HALFTONE : Native.COLORONCOLOR);
                Native.SetBrushOrgEx(hdc, 0, 0, IntPtr.Zero);
                if (!Doc.HasAlpha)
                {
                    // zoomed out by a whole factor or more, the cached box-filtered copy is the source
                    if (k > 1) { EnsureMip(k); Blit(hdc, _mip.DC, src, Point.Empty, k); }
                    else Blit(hdc, Doc.Image.DC, src, Point.Empty, 1);
                    if (hasOv)
                    {
                        // the tool's overlay on top, reduced the same way (with the same extra block for halftone)
                        var ova = AlignOut(ov, bi);
                        if (halftone && !exact) ova = Rectangle.Intersect(Rectangle.Inflate(ova, bi, bi), Doc.Bounds);
                        EnsureScratch(ova.Width, ova.Height);
                        _scratch.CopyFrom(Doc.Image, ova, 0, 0);
                        tool.Composite(_scratch, ova);
                        if (k > 1)
                        {
                            int ow = (ova.Width + k - 1) / k, oh = (ova.Height + k - 1) / k;
                            if (_ovMip == null || _ovMip.W < ow || _ovMip.H < oh) { _ovMip?.Dispose(); _ovMip = new Dib(Math.Max(ow, 64), Math.Max(oh, 64)); }
                            BoxDown(_scratch, new Rectangle(0, 0, ova.Width, ova.Height), _ovMip, 0, 0, k);
                            Blit(hdc, _ovMip.DC, ova, ova.Location, k);
                        }
                        else Blit(hdc, _scratch.DC, ova, ova.Location, 1);
                    }
                }
                else if (halftone)
                {
                    // transparency zoomed out: average premultiplied pixels (the cached box-filtered copy, then an exact
                    // area average for what is left) and alpha blend the result 1:1 over the checkerboard
                    Dib red; Point ro;      // the picture from image point ro on, reduced k times and premultiplied
                    if (k > 1)
                    {
                        EnsureMip(k);
                        red = _mip; ro = Point.Empty;
                        if (hasOv)
                        {
                            int rw = (src.Width + k - 1) / k, rh = (src.Height + k - 1) / k;
                            if (_red == null || _red.W < rw || _red.H < rh) { _red?.Dispose(); _red = new Dib(Math.Max(rw, 64), Math.Max(rh, 64)); }
                            _red.CopyFrom(_mip, new Rectangle(src.X / k, src.Y / k, rw, rh), 0, 0);
                            var ova = AlignOut(ov, bi);
                            EnsureScratch(ova.Width, ova.Height);
                            _scratch.CopyFrom(Doc.Image, ova, 0, 0);
                            tool.Composite(_scratch, ova);
                            BoxDown(_scratch, new Rectangle(0, 0, ova.Width, ova.Height), _red, (ova.X - src.X) / k, (ova.Y - src.Y) / k, k, true);
                            red = _red; ro = src.Location;
                        }
                    }
                    else
                    {
                        EnsureScratch(src.Width, src.Height);
                        _scratch.CopyFrom(Doc.Image, src, 0, 0);
                        if (hasOv) tool.Composite(_scratch, src);
                        for (int y = 0; y < src.Height; y++)
                        {
                            uint* row = _scratch.Row(y);
                            for (int x = 0; x < src.Width; x++) row[x] = Px.Premul(row[x]);
                        }
                        red = _scratch; ro = src.Location;
                    }
                    g.ReleaseHdc(hdc); hdc = IntPtr.Zero;
                    Draw.Checker(g, SourceToView(src), D.S(8), new Point(OriginX, OriginY));
                    hdc = g.GetHdc();
                    // blend only where the checkerboard was just drawn: whole zoom blocks reach past the painted area
                    saved = Native.SaveDC(hdc);
                    Native.IntersectClipRect(hdc, vis.Left, vis.Top, vis.Right, vis.Bottom);
                    Blocks(out _, out int bv);
                    int mb = bi / k, gr = Gcd(bv, mb);
                    foreach (var p in Pieces(src))
                    {
                        var d = SourceToView(p);
                        if (d.Width <= 0 || d.Height <= 0) continue;
                        int sx = (p.X - ro.X) / k, sy = (p.Y - ro.Y) / k;
                        int sw = (p.Right - ro.X + k - 1) / k - sx, sh = (p.Bottom - ro.Y + k - 1) / k - sy;
                        Dib from = red;
                        if (!exact || sw != d.Width || sh != d.Height)
                        {
                            if (_viewBuf == null || _viewBuf.W < d.Width || _viewBuf.H < d.Height) { _viewBuf?.Dispose(); _viewBuf = new Dib(Math.Max(d.Width, 64), Math.Max(d.Height, 64)); }
                            AreaReduce(red, sx, sy, sw, sh, _viewBuf, d.Width, d.Height, bv / gr, mb / gr);
                            from = _viewBuf; sx = sy = 0;
                        }
                        Native.AlphaBlend(hdc, d.X, d.Y, d.Width, d.Height, from.DC, sx, sy, d.Width, d.Height,
                            new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 });
                    }
                }
                else
                {
                    EnsureScratch(src.Width, src.Height);
                    _scratch.CopyFrom(Doc.Image, src, 0, 0);
                    if (hasOv) tool.Composite(_scratch, src);
                    g.ReleaseHdc(hdc); hdc = IntPtr.Zero;
                    Draw.Checker(g, SourceToView(src), D.S(8), new Point(OriginX, OriginY));
                    hdc = g.GetHdc();
                    // blend only where the checkerboard was just drawn: whole zoom blocks reach past the painted area
                    saved = Native.SaveDC(hdc);
                    Native.IntersectClipRect(hdc, vis.Left, vis.Top, vis.Right, vis.Bottom);
                    for (int y = 0; y < src.Height; y++)
                    {
                        uint* row = _scratch.Row(y);
                        for (int x = 0; x < src.Width; x++) row[x] = Px.Premul(row[x]);
                    }
                    Native.SetStretchBltMode(hdc, Native.COLORONCOLOR);
                    foreach (var p in Pieces(src))
                    {
                        var d = SourceToView(p);
                        if (d.Width <= 0 || d.Height <= 0) continue;
                        Native.AlphaBlend(hdc, d.X, d.Y, d.Width, d.Height, _scratch.DC, p.X - src.X, p.Y - src.Y, p.Width, p.Height,
                            new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 });
                    }
                }
            }
            finally
            {
                if (hdc != IntPtr.Zero)
                {
                    if (saved != 0) Native.RestoreDC(hdc, saved);
                    g.ReleaseHdc(hdc);
                }
            }
        }

        /// <summary>
        /// Draws picture rect s from a bitmap that holds the picture from <paramref name="origin"/> on, reduced k times.
        /// The last partial zoom block is drawn on its own, so every piece is stretched by exactly the zoom ratio and a
        /// partial repaint matches a full one pixel for pixel.
        /// </summary>
        void Blit(IntPtr hdc, IntPtr srcDC, Rectangle s, Point origin, int k)
        {
            foreach (var p in Pieces(s))
            {
                var d = SourceToView(p);
                if (d.Width <= 0 || d.Height <= 0) continue;
                int sx = (p.X - origin.X) / k, sy = (p.Y - origin.Y) / k;
                int sw = (p.Right - origin.X + k - 1) / k - sx, sh = (p.Bottom - origin.Y + k - 1) / k - sy;
                if (sw == d.Width && sh == d.Height) Native.BitBlt(hdc, d.X, d.Y, d.Width, d.Height, srcDC, sx, sy, Native.SRCCOPY);
                else Native.StretchBlt(hdc, d.X, d.Y, d.Width, d.Height, srcDC, sx, sy, sw, sh, Native.SRCCOPY);
            }
        }

        /// <summary>Splits a block-aligned picture rect at the start of the last, partial zoom block.</summary>
        IEnumerable<Rectangle> Pieces(Rectangle s)
        {
            Blocks(out int bi, out _);
            int ax = Doc.Width / bi * bi, ay = Doc.Height / bi * bi;
            int[] xs = s.Left < ax && ax < s.Right ? new[] { s.Left, ax, s.Right } : new[] { s.Left, s.Right };
            int[] ys = s.Top < ay && ay < s.Bottom ? new[] { s.Top, ay, s.Bottom } : new[] { s.Top, s.Bottom };
            for (int j = 0; j + 1 < ys.Length; j++)
                for (int i = 0; i + 1 < xs.Length; i++)
                    yield return Rectangle.FromLTRB(xs[i], ys[j], xs[i + 1], ys[j + 1]);
        }

        Rectangle AlignOut(Rectangle r, int b) =>
            Rectangle.FromLTRB(r.Left / b * b, r.Top / b * b, Math.Min(Doc.Width, (r.Right + b - 1) / b * b), Math.Min(Doc.Height, (r.Bottom + b - 1) / b * b));

        void EnsureMip(int k)
        {
            int mw = (Doc.Width + k - 1) / k, mh = (Doc.Height + k - 1) / k;
            bool pm = Doc.HasAlpha;
            if (_mip == null || _mipK != k || _mipSrc != Doc.Image || _mip.W != mw || _mip.H != mh || _mipPremul != pm)
            {
                _mip?.Dispose();
                _mip = new Dib(mw, mh);
                _mipK = k; _mipSrc = Doc.Image; _mipPremul = pm;
                BoxDown(Doc.Image, Doc.Bounds, _mip, 0, 0, k, pm);
                _mipDirty = Rectangle.Empty;
            }
            else if (!_mipDirty.IsEmpty)
            {
                var d = Rectangle.Intersect(_mipDirty, Doc.Bounds);
                _mipDirty = Rectangle.Empty;
                if (d.Width > 0 && d.Height > 0)
                {
                    var a = Rectangle.FromLTRB(d.X / k * k, d.Y / k * k, Math.Min(Doc.Width, (d.Right + k - 1) / k * k), Math.Min(Doc.Height, (d.Bottom + k - 1) / k * k));
                    BoxDown(Doc.Image, a, _mip, a.X / k, a.Y / k, k, pm);
                }
            }
        }

        /// <summary>
        /// Average k x k blocks of src (rect r, origin aligned to k) into dst at (dx,dy). With <paramref name="premul"/> the
        /// pixels are premultiplied first and the result keeps its alpha (premultiplied), otherwise it is opaque.
        /// </summary>
        static unsafe void BoxDown(Dib src, Rectangle r, Dib dst, int dx, int dy, int k, bool premul = false)
        {
            int ow = (r.Width + k - 1) / k, oh = (r.Height + k - 1) / k;
            var acc = new int[ow * 4];
            var cnt = new int[ow];
            for (int oy = 0; oy < oh; oy++)
            {
                Array.Clear(acc, 0, acc.Length); Array.Clear(cnt, 0, cnt.Length);
                int y0 = r.Y + oy * k, y1 = Math.Min(r.Bottom, y0 + k);
                for (int y = y0; y < y1; y++)
                {
                    uint* row = src.Row(y) + r.X;
                    int w = r.Width;
                    for (int x = 0; x < w; x++)
                    {
                        uint c = premul ? Px.Premul(row[x]) : row[x];
                        int o = x / k;
                        acc[o * 4] += (int)((c >> 16) & 255); acc[o * 4 + 1] += (int)((c >> 8) & 255); acc[o * 4 + 2] += (int)(c & 255); acc[o * 4 + 3] += (int)(c >> 24);
                        cnt[o]++;
                    }
                }
                if (dy + oy >= dst.H) break;
                uint* drow = dst.Row(dy + oy) + dx;
                int lim = Math.Min(ow, dst.W - dx);
                for (int o = 0; o < lim; o++)
                {
                    int n = Math.Max(1, cnt[o]);
                    uint a = premul ? (uint)(acc[o * 4 + 3] / n) << 24 : 0xFF000000u;
                    drow[o] = a | ((uint)(acc[o * 4] / n) << 16) | ((uint)(acc[o * 4 + 1] / n) << 8) | (uint)(acc[o * 4 + 2] / n);
                }
            }
        }

        /// <summary>
        /// Area-averages the premultiplied pixels src[sx.., sy..] (sw x sh) into dst (dw x dh at 0,0), shrinking by exactly
        /// p/q (q/2 &lt; p &lt;= q): every output pixel is the coverage-weighted mean of the up to 3 x 3 source pixels under it.
        /// </summary>
        static unsafe void AreaReduce(Dib src, int sx, int sy, int sw, int sh, Dib dst, int dw, int dh, int p, int q)
        {
            Taps(sw, dw, p, q, out var xi, out var xw, out var xs);
            Taps(sh, dh, p, q, out var yi, out var yw, out var ys);
            // horizontally reduced source rows, kept for the (at most 3 consecutive) rows the current output row needs
            var rows = new int[3][];
            var rowOf = new[] { -1, -1, -1 };
            for (int t = 0; t < 3; t++) rows[t] = new int[dw * 4];
            for (int j = 0; j < dh; j++)
            {
                for (int t = 0; t < 3; t++)
                {
                    if (yw[j * 3 + t] == 0) continue;
                    int i = yi[j * 3 + t];
                    if (rowOf[i % 3] == i) continue;
                    rowOf[i % 3] = i;
                    int[] h = rows[i % 3];
                    uint* srow = src.Row(sy + i) + sx;
                    for (int c = 0; c < dw; c++)
                    {
                        int a = 0, r = 0, g = 0, b = 0;
                        for (int u = 0; u < 3; u++)
                        {
                            int w = xw[c * 3 + u];
                            if (w == 0) continue;
                            uint px = srow[xi[c * 3 + u]];
                            a += (int)(px >> 24) * w; r += (int)((px >> 16) & 255) * w; g += (int)((px >> 8) & 255) * w; b += (int)(px & 255) * w;
                        }
                        h[c * 4] = a; h[c * 4 + 1] = r; h[c * 4 + 2] = g; h[c * 4 + 3] = b;
                    }
                }
                uint* drow = dst.Row(j);
                for (int c = 0; c < dw; c++)
                {
                    int a = 0, r = 0, g = 0, b = 0;
                    for (int t = 0; t < 3; t++)
                    {
                        int w = yw[j * 3 + t];
                        if (w == 0) continue;
                        int[] h = rows[yi[j * 3 + t] % 3];
                        a += h[c * 4] * w; r += h[c * 4 + 1] * w; g += h[c * 4 + 2] * w; b += h[c * 4 + 3] * w;
                    }
                    int tot = xs[c] * ys[j];
                    if (tot == 0) { drow[c] = 0; continue; }
                    drow[c] = ((uint)((a + tot / 2) / tot) << 24) | ((uint)((r + tot / 2) / tot) << 16) | ((uint)((g + tot / 2) / tot) << 8) | (uint)((b + tot / 2) / tot);
                }
            }
        }

        /// <summary>For each of dn output pixels, the source pixels (of sn, each p units wide) under its q units, and how much.</summary>
        static void Taps(int sn, int dn, int p, int q, out int[] idx, out int[] wt, out int[] sum)
        {
            idx = new int[dn * 3]; wt = new int[dn * 3]; sum = new int[dn];
            for (int j = 0; j < dn; j++)
            {
                long lo = (long)j * q, hi = lo + q;
                int t = 0;
                for (int i = (int)(lo / p); i < sn && (long)i * p < hi && t < 3; i++)
                {
                    int w = (int)(Math.Min((long)(i + 1) * p, hi) - Math.Max((long)i * p, lo));
                    if (w <= 0) continue;
                    idx[j * 3 + t] = i; wt[j * 3 + t] = w; sum[j] += w; t++;
                }
            }
        }

        void EnsureScratch(int w, int h)
        {
            if (_scratch == null || _scratch.W < w || _scratch.H < h)
            {
                _scratch?.Dispose();
                _scratch = new Dib(Math.Max(w, 64), Math.Max(h, 64));
            }
        }

        /// <summary>The picture rect behind view rect vis, widened to whole zoom blocks (plus <paramref name="margin"/> more blocks).</summary>
        Rectangle VisibleSource(Rectangle vis, int margin)
        {
            Blocks(out int bi, out int bv);
            int ox = OriginX, oy = OriginY;
            int x0 = (FloorDiv(vis.Left - ox, bv) - margin) * bi, y0 = (FloorDiv(vis.Top - oy, bv) - margin) * bi;
            int x1 = (CeilDiv(vis.Right - ox, bv) + margin) * bi, y1 = (CeilDiv(vis.Bottom - oy, bv) + margin) * bi;
            return Rectangle.FromLTRB(Math.Max(0, x0), Math.Max(0, y0), Math.Min(Doc.Width, x1), Math.Min(Doc.Height, y1));
        }

        Rectangle SourceToView(Rectangle s)
        {
            int ox = OriginX, oy = OriginY;
            return Rectangle.FromLTRB(ox + Scaled(s.Left), oy + Scaled(s.Top), ox + Scaled(s.Right), oy + Scaled(s.Bottom));
        }

        static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
        static int CeilDiv(int a, int b) => a >= 0 ? (a + b - 1) / b : -((-a) / b);

        void DrawGrid(Graphics g, Rectangle vis)
        {
            if (vis.Width <= 0 || vis.Height <= 0) return;
            int step;
            if (Zoom >= 4) step = 1;
            else
            {
                int[] steps = { 1, 2, 5, 10, 20, 50, 100, 200, 500 };
                step = 500;
                foreach (var s in steps) if (s * Zoom >= D.S(8)) { step = s; break; }
            }
            var src = Rectangle.Intersect(ImageViewport, Doc.Bounds);
            using (var pen = new Pen(Color.FromArgb(Zoom >= 4 ? 70 : 90, 128, 128, 128)))
            {
                g.SmoothingMode = SmoothingMode.None;
                int x0 = (src.Left / step) * step, y0 = (src.Top / step) * step;
                for (int x = x0; x <= src.Right; x += step)
                {
                    if (x <= 0 || x >= Doc.Width) continue;
                    int vx = OriginX + Scaled(x);
                    if (vx < vis.Left || vx >= vis.Right) continue;
                    g.DrawLine(pen, vx, vis.Top, vx, vis.Bottom - 1);
                }
                for (int y = y0; y <= src.Bottom; y += step)
                {
                    if (y <= 0 || y >= Doc.Height) continue;
                    int vy = OriginY + Scaled(y);
                    if (vy < vis.Top || vy >= vis.Bottom) continue;
                    g.DrawLine(pen, vis.Left, vy, vis.Right - 1, vy);
                }
            }
        }

        Rectangle HandleRect(int which)
        {
            var cr = CanvasRect; int s = HandleSize;
            switch (which)
            {
                case 1: return new Rectangle(cr.Right + 1, cr.Top + cr.Height / 2 - s / 2, s, s);
                case 2: return new Rectangle(cr.Left + cr.Width / 2 - s / 2, cr.Bottom + 1, s, s);
                default: return new Rectangle(cr.Right + 1, cr.Bottom + 1, s, s);
            }
        }

        bool CanvasHandlesHidden => E.ActiveTool != null && E.ActiveTool.HasPending && (E.Tool == ToolKind.Text || E.Tool == ToolKind.Select);

        int HitCanvasHandle(Point p)
        {
            if (CanvasHandlesHidden) return 0;
            for (int i = 3; i >= 1; i--)
            {
                var r = HandleRect(i); r.Inflate(D.S(3), D.S(3));
                if (r.Contains(p)) return i;
            }
            return 0;
        }

        void DrawCanvasHandles(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.None;
            if (_resizeMode != 0)
            {
                var r = new Rectangle(OriginX, OriginY, Scaled(_resizeSize.Width), Scaled(_resizeSize.Height));
                using (var p1 = new Pen(Color.White)) g.DrawRectangle(p1, r.X, r.Y, r.Width, r.Height);
                using (var p2 = new Pen(Color.Black) { DashPattern = new[] { 3f, 3f } }) g.DrawRectangle(p2, r.X, r.Y, r.Width, r.Height);
                return;
            }
            if (CanvasHandlesHidden) return;
            for (int i = 1; i <= 3; i++)
            {
                var r = HandleRect(i);
                g.FillRectangle(Theme.Brush(Theme.Handle), r);
                g.DrawRectangle(Theme.Pen(Color.FromArgb(0x70, 0x70, 0x70)), r.X, r.Y, r.Width - 1, r.Height - 1);
            }
        }

        void DrawRulers(Graphics g, Rectangle clip)
        {
            int rs = RulerSize;
            var top = new Rectangle(rs, 0, ClientSize.Width - rs, rs);
            var left = new Rectangle(0, rs, rs, ClientSize.Height - rs);
            Draw.Fill(g, new Rectangle(0, 0, rs, rs), Theme.Ruler);
            Draw.Fill(g, top, Theme.Ruler);
            Draw.Fill(g, left, Theme.Ruler);
            Draw.HLine(g, 0, ClientSize.Width, rs - 1, Theme.RibbonBorder);
            Draw.VLine(g, rs - 1, 0, ClientSize.Height, Theme.RibbonBorder);

            int[] steps = { 1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000, 2000, 5000, 10000 };
            int major = 10000;
            foreach (var s in steps) if (s * Zoom >= D.S(60)) { major = s; break; }
            int minorDiv = major % 10 == 0 || major == 1 ? 10 : (major % 5 == 0 ? 5 : 2);
            if (major * Zoom / minorDiv < D.S(5)) minorDiv = major % 5 == 0 ? 5 : 2;
            if (major * Zoom / minorDiv < D.S(4)) minorDiv = 1;
            float minor = (float)major / minorDiv;
            var font = Theme.SmallFont(D.Value);
            var tickPen = Theme.Pen(Theme.RulerTick);

            var st = g.Save();
            g.SetClip(Rectangle.Intersect(top, clip));
            float startImg = (float)Math.Floor(ViewToImage(new Point(rs, 0)).X / major) * major;
            float endImg = ViewToImage(new Point(ClientSize.Width, 0)).X;
            for (int i = 0; ; i++)
            {
                float v = startImg + i * minor;
                if (v > endImg) break;
                float vx = OriginX + v * Zoom;
                bool isMajor = Math.Abs(v / major - Math.Round(v / major)) < 0.001;
                bool half = !isMajor && minorDiv % 2 == 0 && Math.Abs((v - major / 2f) / major - Math.Round((v - major / 2f) / major)) < 0.001;
                int len = isMajor ? rs - 2 : half ? rs / 2 : rs / 4;
                g.DrawLine(tickPen, (int)vx, rs - 1 - len, (int)vx, rs - 1);
                if (isMajor) TextRenderer.DrawText(g, ((int)Math.Round(v)).ToString(), font, new Point((int)vx + D.S(2), 0), Theme.RulerText, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
            }
            g.Restore(st);

            st = g.Save();
            g.SetClip(Rectangle.Intersect(left, clip));
            startImg = (float)Math.Floor(ViewToImage(new Point(0, rs)).Y / major) * major;
            endImg = ViewToImage(new Point(0, ClientSize.Height)).Y;
            for (int i = 0; ; i++)
            {
                float v = startImg + i * minor;
                if (v > endImg) break;
                float vy = OriginY + v * Zoom;
                bool isMajor = Math.Abs(v / major - Math.Round(v / major)) < 0.001;
                bool half = !isMajor && minorDiv % 2 == 0 && Math.Abs((v - major / 2f) / major - Math.Round((v - major / 2f) / major)) < 0.001;
                int len = isMajor ? rs - 2 : half ? rs / 2 : rs / 4;
                g.DrawLine(tickPen, rs - 1 - len, (int)vy, rs - 1, (int)vy);
                if (isMajor)
                {
                    var s = ((int)Math.Round(v)).ToString();
                    var stt = g.Save();
                    g.TranslateTransform(0, vy + D.S(2));
                    g.RotateTransform(90);
                    using (var b = new SolidBrush(Theme.RulerText))
                    using (var sf = new StringFormat())
                    {
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                        g.DrawString(s, font, b, 0, -rs + D.S(1), sf);
                    }
                    g.Restore(stt);
                }
            }
            g.Restore(st);

            // cursor marks
            if (E.CursorPos.HasValue)
            {
                var p = E.CursorPos.Value;
                int vx = OriginX + Scaled(p.X), vy = OriginY + Scaled(p.Y);
                using (var mp = new Pen(Theme.RulerMark))
                {
                    if (vx >= rs) g.DrawLine(mp, vx, 0, vx, rs - 2);
                    if (vy >= rs) g.DrawLine(mp, 0, vy, rs - 2, vy);
                }
            }
        }

        // ------------------------------------------------------------------ input
        MouseArgs Args(MouseEventArgs e, MouseButtons buttons)
        {
            var p = ViewToImage(e.Location);
            return new MouseArgs
            {
                P = p,
                Pi = new Point((int)Math.Floor(p.X), (int)Math.Floor(p.Y)),
                Button = buttons,
                Shift = Native.ShiftDown,
                Ctrl = Native.CtrlDown,
                Alt = Native.AltDown,
                ViewPt = e.Location,
            };
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _autoFit = false;
            if (!Focused) Focus();
            Popups.CloseAll();
            if (e.Button == MouseButtons.Middle)
            {
                _panning = true; _panStart = e.Location; _panSX = _sx; _panSY = _sy;
                Native.SetCursor(AppCursors.SizeAll);
                return;
            }
            if (_hs.MouseDown(e.Location)) { _scrollCapture = _hs; return; }
            if (_vs.MouseDown(e.Location)) { _scrollCapture = _vs; return; }
            if (_rulers && (e.X < RulerSize || e.Y < RulerSize)) return;

            if (!_toolDown && e.Button == MouseButtons.Left)
            {
                int h = HitCanvasHandle(e.Location);
                if (h != 0)
                {
                    E.CommitPending();
                    _resizeMode = h; _resizeStart = e.Location; _resizeSize = Doc.Size;
                    E.SetSelSize(_resizeSize);
                    Invalidate();
                    return;
                }
            }
            if (_resizeMode != 0) return;

            var a = Args(e, e.Button);
            if (e.Button == MouseButtons.Right && !_toolDown && E.ActiveTool.WantsRightClickMenu(a))
            {
                return; // context menu on mouse up
            }
            bool was = E.ActiveTool.InStroke;
            if (e.Clicks >= 2 && !_toolDown) E.ActiveTool.DoubleClick(a);
            else E.ActiveTool.MouseDown(a);
            _toolDown = E.ActiveTool.InStroke;
            if (was && !_toolDown) Invalidate();
            UpdateCursor();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_panning)
            {
                SetScroll(_panSX - (e.X - _panStart.X), _panSY - (e.Y - _panStart.Y));
                return;
            }
            if (_scrollCapture != null) { _scrollCapture.MouseMove(e.Location); return; }
            _hs.MouseMove(e.Location); _vs.MouseMove(e.Location);
            if (e.Location == _lastMouse && e.Button == MouseButtons.None) return;
            _lastMouse = e.Location;

            var a = Args(e, e.Button);
            UpdateStatusCursor(a);

            if (_resizeMode != 0)
            {
                var img = ViewToImage(e.Location);
                int nw = _resizeSize.Width, nh = _resizeSize.Height;
                if (_resizeMode == 1 || _resizeMode == 3) nw = Math.Max(1, (int)Math.Round(img.X));
                if (_resizeMode == 2 || _resizeMode == 3) nh = Math.Max(1, (int)Math.Round(img.Y));
                if (nw != _resizeSize.Width || nh != _resizeSize.Height)
                {
                    _resizeSize = new Size(nw, nh);
                    E.SetSelSize(_resizeSize);
                    Invalidate();
                }
                return;
            }
            if (_rulers && (e.X < RulerSize || e.Y < RulerSize) && !_toolDown) { E.ActiveTool.MouseLeave(); Invalidate(new Rectangle(0, 0, ClientSize.Width, RulerSize)); return; }
            if ((_hs.Contains(e.Location) || _vs.Contains(e.Location)) && !_toolDown) { E.ActiveTool.MouseLeave(); return; }
            E.ActiveTool.MouseMove(a);
            if (_rulers) { Invalidate(new Rectangle(0, 0, ClientSize.Width, RulerSize)); Invalidate(new Rectangle(0, 0, RulerSize, ClientSize.Height)); }
        }

        void UpdateStatusCursor(MouseArgs a)
        {
            if (a.Pi.X >= 0 && a.Pi.Y >= 0 && a.Pi.X < Doc.Width && a.Pi.Y < Doc.Height) E.SetCursorPos(a.Pi);
            else E.SetCursorPos(null);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_panning && e.Button == MouseButtons.Middle) { _panning = false; UpdateCursor(); return; }
            if (_scrollCapture != null) { _scrollCapture.MouseUp(); _scrollCapture = null; return; }
            if (_resizeMode != 0)
            {
                _resizeMode = 0;
                if (_resizeSize != Doc.Size) E.ResizeCanvas(_resizeSize);
                E.SetSelSize(null);
                Invalidate();
                return;
            }
            var a = Args(e, e.Button);
            if (e.Button == MouseButtons.Right && !_toolDown && E.ActiveTool.WantsRightClickMenu(a))
            {
                E.Form?.ShowCanvasMenu(PointToScreen(e.Location));
                return;
            }
            E.ActiveTool.MouseUp(a);
            _toolDown = E.ActiveTool.InStroke;
            UpdateCursor();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hs.MouseLeave(); _vs.MouseLeave();
            if (!_toolDown) { E.ActiveTool?.MouseLeave(); E.SetCursorPos(null); }
            _lastMouse = new Point(-1, -1);
            if (_rulers) Invalidate();
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (!Capture && _toolDown)
            {
                // capture stolen (e.g. Alt+Tab mid-stroke): finish like a mouse-up
                var p = PointToClient(Cursor.Position);
                var a = Args(new MouseEventArgs(MouseButtons.None, 0, p.X, p.Y, 0), MouseButtons.Left | MouseButtons.Right);
                E.ActiveTool.MouseUp(a);
                _toolDown = E.ActiveTool.InStroke;
                if (_toolDown) { E.ActiveTool.CancelStroke(); _toolDown = false; }
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _autoFit = false;
            Popups.CloseAll();
            if (Native.CtrlDown)
            {
                SetZoom(NextZoom(Zoom, e.Delta > 0 ? 1 : -1), e.Location);
                return;
            }
            int lines = SystemInformation.MouseWheelScrollLines;
            if (lines <= 0) lines = 3;
            int amount = -e.Delta * lines * D.S(16) / 120;
            if (Native.ShiftDown || !_vs.Visible) ScrollBy(amount, 0); else ScrollBy(0, amount);
        }

        /// <summary>True while the key being handled is an auto-repeat of a held key (WM_KEYDOWN lParam bit 30).</summary>
        public bool KeyRepeat { get; private set; }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_KEYDOWN) KeyRepeat = ((long)m.LParam & 0x40000000) != 0;
            switch (m.Msg)
            {
                case Native.WM_SETCURSOR:
                    if (Native.LoWord(m.LParam) == Native.HTCLIENT) { UpdateCursor(); m.Result = (IntPtr)1; return; }
                    break;
                case Native.WM_MOUSEHWHEEL:
                    ScrollBy(Native.HiWord(m.WParam) * D.S(16) / 40, 0);
                    m.Result = IntPtr.Zero;
                    return;
                case Native.WM_KEYDOWN: case Native.WM_KEYUP: case Native.WM_CHAR: case Native.WM_SYSKEYDOWN: case Native.WM_SYSCHAR:
                case Native.WM_IME_STARTCOMPOSITION: case Native.WM_IME_COMPOSITION: case Native.WM_IME_ENDCOMPOSITION:
                case Native.WM_IME_CHAR: case Native.WM_IME_NOTIFY: case Native.WM_IME_REQUEST: case Native.WM_IME_SETCONTEXT:
                    if (E.Tool == ToolKind.Text && E.TextTool.IsEditing && E.TextTool.HandleMessage(ref m)) return;
                    break;
            }
            base.WndProc(ref m);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: case Keys.Tab: case Keys.Enter: case Keys.Escape:
                case Keys.Home: case Keys.End: case Keys.PageUp: case Keys.PageDown:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            _autoFit = false;
            if (_resizeMode != 0 && e.KeyCode == Keys.Escape) { _resizeMode = 0; E.SetSelSize(null); Invalidate(); e.Handled = true; return; }
            if (E.ActiveTool.KeyDown(e.KeyData)) { e.Handled = true; _toolDown = E.ActiveTool.InStroke; UpdateCursor(); }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            if (E.ActiveTool.KeyUp(e.KeyData)) e.Handled = true;
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (E.ActiveTool.Char(e.KeyChar)) e.Handled = true;
        }

        public void UpdateCursor()
        {
            var p = PointToClient(Cursor.Position);
            if (!ClientRectangle.Contains(p) && !Capture) return;
            if (_panning) { Native.SetCursor(AppCursors.SizeAll); return; }
            if (_resizeMode == 1) { Native.SetCursor(AppCursors.SizeWE); return; }
            if (_resizeMode == 2) { Native.SetCursor(AppCursors.SizeNS); return; }
            if (_resizeMode == 3) { Native.SetCursor(AppCursors.SizeNWSE); return; }
            if (_hs.Contains(p) || _vs.Contains(p) || (_rulers && (p.X < RulerSize || p.Y < RulerSize))) { Native.SetCursor(AppCursors.Arrow); return; }
            if (!_toolDown)
            {
                int h = HitCanvasHandle(p);
                if (h == 1) { Native.SetCursor(AppCursors.SizeWE); return; }
                if (h == 2) { Native.SetCursor(AppCursors.SizeNS); return; }
                if (h == 3) { Native.SetCursor(AppCursors.SizeNWSE); return; }
            }
            var a = Args(new MouseEventArgs(MouseButtons.None, 0, p.X, p.Y, 0), MouseButtons.None);
            Native.SetCursor(E.ActiveTool.GetCursor(a));
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); E.TextTool?.FocusChanged(true); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); E.TextTool?.FocusChanged(false); }

        protected override void OnDragEnter(DragEventArgs e)
        {
            base.OnDragEnter(e);
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            base.OnDragDrop(e);
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                BeginInvoke(new Action(() => E.Form?.OpenFile(files[0], true)));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _scratch?.Dispose(); _mip?.Dispose(); _ovMip?.Dispose(); _red?.Dispose(); _viewBuf?.Dispose(); _toolTimer.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
