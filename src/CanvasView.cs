using System;
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
        public float Zoom { get; private set; } = 1f;
        public static readonly float[] Levels = { 0.125f, 0.25f, 0.5f, 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f };
        int _sx, _sy;
        bool _rulers, _grid;
        readonly DarkScroll _hs, _vs;
        Dib _scratch;
        // zoomed-out display cache (box-filtered), updated only where the picture changes
        Dib _mip, _mipSrc, _ovMip;
        int _mipK;
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
        int Scaled(int v) => (int)Math.Round(v * (double)Zoom);
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
        public Point ImageToView(Point p) => new Point((int)Math.Round(p.X * Zoom) + OriginX, (int)Math.Round(p.Y * Zoom) + OriginY);
        public Rectangle ImageToView(Rectangle r)
        {
            int x0 = (int)Math.Floor(r.Left * (double)Zoom) + OriginX, y0 = (int)Math.Floor(r.Top * (double)Zoom) + OriginY;
            int x1 = (int)Math.Ceiling(r.Right * (double)Zoom) + OriginX, y1 = (int)Math.Ceiling(r.Bottom * (double)Zoom) + OriginY;
            return Rectangle.FromLTRB(x0, y0, x1, y1);
        }

        // ------------------------------------------------------------------ zoom & scroll
        public static float NextZoom(float z, int dir)
        {
            if (dir > 0) { foreach (var l in Levels) if (l > z + 0.0001f) return l; return Levels[Levels.Length - 1]; }
            for (int i = Levels.Length - 1; i >= 0; i--) if (Levels[i] < z - 0.0001f) return Levels[i];
            return Levels[0];
        }

        public void SetZoom(float z, Point? anchor)
        {
            z = Math.Max(Levels[0], Math.Min(Levels[Levels.Length - 1], z));
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
            // like Paint: open big pictures at the largest zoom level that fits
            var va = new Size(Math.Max(100, ClientSize.Width - RulerSize - SBSize - CanvasMargin * 3), Math.Max(100, ClientSize.Height - RulerSize - SBSize - CanvasMargin * 3));
            float z = 1f;
            if (!string.IsNullOrEmpty(Doc.FilePath) && (Doc.Width > va.Width || Doc.Height > va.Height))
                for (int i = Array.IndexOf(Levels, 1f); i >= 0; i--) { z = Levels[i]; if (Doc.Width * z <= va.Width && Doc.Height * z <= va.Height) break; }
            Zoom = z;
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
            // map visible view rect -> source picture rect (aligned so integer zoom steps map exactly)
            Rectangle src, dst;
            MapToSource(vis, out src, out dst);
            if (src.Width <= 0 || src.Height <= 0) return;

            var tool = E.ActiveTool;
            Rectangle ov = tool != null ? tool.CompositeBounds : Rectangle.Empty;
            ov.Intersect(src);
            bool needScratch = Doc.HasAlpha || (ov.Width > 0 && ov.Height > 0);

            IntPtr hdc = g.GetHdc();
            try
            {
                Native.SetStretchBltMode(hdc, Zoom < 1 ? Native.HALFTONE : Native.COLORONCOLOR);
                Native.SetBrushOrgEx(hdc, 0, 0, IntPtr.Zero);
                if (Zoom < 1 && !Doc.HasAlpha)
                {
                    // zoomed out: blit from the cached box-filtered copy, overlays are reduced the same way
                    int k = EnsureMip();
                    Native.BitBlt(hdc, dst.X, dst.Y, dst.Width, dst.Height, _mip.DC, src.X / k, src.Y / k, Native.SRCCOPY);
                    if (ov.Width > 0 && ov.Height > 0)
                    {
                        var ova = Rectangle.FromLTRB(ov.X / k * k, ov.Y / k * k, Math.Min(Doc.Width, (ov.Right + k - 1) / k * k), Math.Min(Doc.Height, (ov.Bottom + k - 1) / k * k));
                        EnsureScratch(ova.Width, ova.Height);
                        _scratch.CopyFrom(Doc.Image, ova, 0, 0);
                        tool.Composite(_scratch, ova);
                        int ow = (ova.Width + k - 1) / k, oh = (ova.Height + k - 1) / k;
                        if (_ovMip == null || _ovMip.W < ow || _ovMip.H < oh) { _ovMip?.Dispose(); _ovMip = new Dib(Math.Max(ow, 64), Math.Max(oh, 64)); }
                        BoxDown(_scratch, new Rectangle(0, 0, ova.Width, ova.Height), _ovMip, 0, 0, k);
                        var od = SourceToView(ova);
                        Native.BitBlt(hdc, od.X, od.Y, ow, oh, _ovMip.DC, 0, 0, Native.SRCCOPY);
                    }
                }
                else if (!needScratch)
                {
                    Native.StretchBlt(hdc, dst.X, dst.Y, dst.Width, dst.Height, Doc.Image.DC, src.X, src.Y, src.Width, src.Height, Native.SRCCOPY);
                }
                else
                {
                    var region = Doc.HasAlpha ? src : ov;
                    EnsureScratch(region.Width, region.Height);
                    _scratch.CopyFrom(Doc.Image, region, 0, 0);
                    if (ov.Width > 0 && ov.Height > 0) tool.Composite(_scratch, region);
                    Rectangle rdst = SourceToView(region);
                    if (!Doc.HasAlpha)
                    {
                        // whole picture first, then the overlay region on top
                        Native.StretchBlt(hdc, dst.X, dst.Y, dst.Width, dst.Height, Doc.Image.DC, src.X, src.Y, src.Width, src.Height, Native.SRCCOPY);
                        Native.StretchBlt(hdc, rdst.X, rdst.Y, rdst.Width, rdst.Height, _scratch.DC, 0, 0, region.Width, region.Height, Native.SRCCOPY);
                    }
                    else
                    {
                        g.ReleaseHdc(hdc); hdc = IntPtr.Zero;
                        Draw.Checker(g, dst, D.S(8));
                        hdc = g.GetHdc();
                        for (int y = 0; y < region.Height; y++)
                        {
                            uint* row = _scratch.Row(y);
                            for (int x = 0; x < region.Width; x++) row[x] = Px.Premul(row[x]);
                        }
                        Native.SetStretchBltMode(hdc, Native.COLORONCOLOR);
                        Native.AlphaBlend(hdc, rdst.X, rdst.Y, rdst.Width, rdst.Height, _scratch.DC, 0, 0, region.Width, region.Height,
                            new BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 });
                    }
                }
            }
            finally { if (hdc != IntPtr.Zero) g.ReleaseHdc(hdc); }
        }

        int EnsureMip()
        {
            int k = Math.Max(2, (int)Math.Round(1 / Zoom));
            int mw = (Doc.Width + k - 1) / k, mh = (Doc.Height + k - 1) / k;
            if (_mip == null || _mipK != k || _mipSrc != Doc.Image || _mip.W != mw || _mip.H != mh)
            {
                _mip?.Dispose();
                _mip = new Dib(mw, mh);
                _mipK = k; _mipSrc = Doc.Image;
                BoxDown(Doc.Image, Doc.Bounds, _mip, 0, 0, k);
                _mipDirty = Rectangle.Empty;
            }
            else if (!_mipDirty.IsEmpty)
            {
                var d = Rectangle.Intersect(_mipDirty, Doc.Bounds);
                _mipDirty = Rectangle.Empty;
                if (d.Width > 0 && d.Height > 0)
                {
                    var a = Rectangle.FromLTRB(d.X / k * k, d.Y / k * k, Math.Min(Doc.Width, (d.Right + k - 1) / k * k), Math.Min(Doc.Height, (d.Bottom + k - 1) / k * k));
                    BoxDown(Doc.Image, a, _mip, a.X / k, a.Y / k, k);
                }
            }
            return k;
        }

        /// <summary>Average k x k blocks of src (rect r, origin aligned to k) into dst at (dx,dy).</summary>
        static unsafe void BoxDown(Dib src, Rectangle r, Dib dst, int dx, int dy, int k)
        {
            int ow = (r.Width + k - 1) / k, oh = (r.Height + k - 1) / k;
            var acc = new int[ow * 3];
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
                        uint c = row[x];
                        int o = x / k;
                        acc[o * 3] += (int)((c >> 16) & 255); acc[o * 3 + 1] += (int)((c >> 8) & 255); acc[o * 3 + 2] += (int)(c & 255);
                        cnt[o]++;
                    }
                }
                if (dy + oy >= dst.H) break;
                uint* drow = dst.Row(dy + oy) + dx;
                int lim = Math.Min(ow, dst.W - dx);
                for (int o = 0; o < lim; o++)
                {
                    int n = Math.Max(1, cnt[o]);
                    drow[o] = 0xFF000000u | ((uint)(acc[o * 3] / n) << 16) | ((uint)(acc[o * 3 + 1] / n) << 8) | (uint)(acc[o * 3 + 2] / n);
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

        void MapToSource(Rectangle vis, out Rectangle src, out Rectangle dst)
        {
            int ox = OriginX, oy = OriginY;
            if (Zoom >= 1)
            {
                int z = (int)Math.Round(Zoom);
                int sx0 = FloorDiv(vis.Left - ox, z), sy0 = FloorDiv(vis.Top - oy, z);
                int sx1 = CeilDiv(vis.Right - ox, z), sy1 = CeilDiv(vis.Bottom - oy, z);
                sx0 = Math.Max(0, sx0); sy0 = Math.Max(0, sy0); sx1 = Math.Min(Doc.Width, sx1); sy1 = Math.Min(Doc.Height, sy1);
                src = Rectangle.FromLTRB(sx0, sy0, sx1, sy1);
                dst = new Rectangle(ox + sx0 * z, oy + sy0 * z, src.Width * z, src.Height * z);
            }
            else
            {
                int k = (int)Math.Round(1 / Zoom);
                int dx0 = vis.Left - ox, dy0 = vis.Top - oy, dx1 = vis.Right - ox, dy1 = vis.Bottom - oy;
                int sx0 = Math.Max(0, dx0 * k), sy0 = Math.Max(0, dy0 * k);
                int sx1 = Math.Min(Doc.Width, dx1 * k), sy1 = Math.Min(Doc.Height, dy1 * k);
                src = Rectangle.FromLTRB(sx0, sy0, sx1, sy1);
                dst = SourceToView(src);
            }
        }

        Rectangle SourceToView(Rectangle s)
        {
            int ox = OriginX, oy = OriginY;
            if (Zoom >= 1)
            {
                int z = (int)Math.Round(Zoom);
                return new Rectangle(ox + s.X * z, oy + s.Y * z, s.Width * z, s.Height * z);
            }
            int k = (int)Math.Round(1 / Zoom);
            int x0 = s.X / k, y0 = s.Y / k;
            int x1 = (s.Right + k - 1) / k, y1 = (s.Bottom + k - 1) / k;
            return Rectangle.FromLTRB(ox + x0, oy + y0, ox + x1, oy + y1);
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

        protected override void WndProc(ref Message m)
        {
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
            if (disposing) { _scratch?.Dispose(); _mip?.Dispose(); _ovMip?.Dispose(); _toolTimer.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
