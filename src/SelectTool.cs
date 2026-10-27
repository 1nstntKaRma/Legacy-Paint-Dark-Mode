using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PaintDark
{
    /// <summary>
    /// Rectangular / free-form selection with Paint's floating behaviour: lifting leaves Color 2 behind, Ctrl+drag copies,
    /// Shift+drag stamps a trail, handles stretch, arrows nudge, transparent selection hides Color 2 pixels.
    /// </summary>
    public sealed unsafe class SelectTool : ToolBase
    {
        // selection region in picture coordinates
        Rectangle _rect;              // bounding box of the selected area (where the floating content is drawn)
        byte[] _mask;                 // per-pixel mask of the selection (null = full rectangle); size = _srcRect
        Rectangle _srcRect;           // original area (mask coordinates)
        Dib _float;                   // floating pixels (size = _srcRect), null when not lifted
        bool _lifted;
        bool _active;                 // a selection exists
        bool _inverted;               // "invert selection" mode: everything except _invHole
        Rectangle _invHole;
        Rectangle _liftedFrom;        // area that was cleared when lifting (for undo diff)
        bool _pasted;                 // floating content came from the clipboard / file (no hole)
        bool _hasAlphaContent;

        // interaction
        int _mode;                    // 0 none, 1 marquee rect, 2 lasso, 3 move, 4 resize
        int _handle;
        Point _startPt;
        PointF _startF;
        Rectangle _startRect;
        readonly List<Point> _lasso = new List<Point>();
        bool _btnDown;
        MouseButtons _btn;
        Rectangle _touched;           // union of everything modified while floating
        int _antsPhase;

        public SelectTool(Editor e) : base(e) { }
        public override string Name => "Selection";
        public bool HasSelection => _active;
        public bool IsFloating => _lifted;
        public override bool HasPending => _active;
        public override bool InStroke => _btnDown;
        public Rectangle SelectionRect => _rect;

        public override void DocumentChanged() { Drop(); }

        void Drop()
        {
            var old = _rect;
            _active = _lifted = _inverted = _pasted = false;
            _float?.Dispose(); _float = null;
            _mask = null;
            _mode = 0; _btnDown = false;
            _touched = Rectangle.Empty;
            if (!old.IsEmpty) V?.InvalidateImageOutline(old, 8);
            E.SetSelSize(null);
            E.Notify(Change.Selection | Change.Commands);
        }

        // ------------------------------------------------------------------ public operations
        public void SelectAll()
        {
            CommitInternal();
            _active = true; _inverted = false;
            _rect = _srcRect = Doc.Bounds;
            _mask = null;
            AfterChange();
        }

        public void InvertSelection()
        {
            if (!_active) { SelectAll(); return; }
            if (_lifted) CommitInternal();
            if (_inverted) { _rect = _srcRect = _invHole; _inverted = false; _mask = null; _active = true; AfterChange(); return; }
            var hole = _rect;
            var holeMask = _mask;
            _active = true; _inverted = true; _invHole = hole;
            _rect = _srcRect = Doc.Bounds;
            // mask = everything except the old selection
            var m = new byte[Doc.Width * Doc.Height];
            for (int i = 0; i < m.Length; i++) m[i] = 1;
            for (int y = Math.Max(0, hole.Top); y < Math.Min(Doc.Height, hole.Bottom); y++)
                for (int x = Math.Max(0, hole.Left); x < Math.Min(Doc.Width, hole.Right); x++)
                {
                    bool inHole = holeMask == null || holeMask[(y - hole.Y) * hole.Width + (x - hole.X)] != 0;
                    if (inHole) m[y * Doc.Width + x] = 0;
                }
            _mask = m;
            AfterChange();
        }

        /// <summary>Paste a picture as a floating selection at the top-left of the visible area.</summary>
        public void PasteImage(Dib img, bool hasAlpha, Point at)
        {
            CommitInternal();
            _float = img;
            _srcRect = new Rectangle(0, 0, img.W, img.H);
            _rect = new Rectangle(at.X, at.Y, img.W, img.H);
            _mask = null;
            _lifted = true; _pasted = true; _active = true; _inverted = false;
            _hasAlphaContent = hasAlpha;
            _liftedFrom = Rectangle.Empty;
            _touched = _rect;
            AfterChange();
        }

        public void Delete()
        {
            if (!_active) return;
            if (_lifted && _pasted) { var r = _rect; Drop(); E.InvalidateImage(r); return; }
            if (!_lifted) Lift(false);
            // discard the floating pixels: the hole stays filled with Color 2
            var touched = Raster.Union(_touched, _liftedFrom);
            _float?.Dispose(); _float = null;
            _lifted = false; _active = false;
            if (!touched.IsEmpty) E.Commit(touched, "Delete");
            Drop();
        }

        /// <summary>Pixels of the selection (floating or not), with transparent outside the free-form mask.</summary>
        public Dib CopyPixels(out bool hasAlpha)
        {
            hasAlpha = false;
            if (!_active) return null;
            Dib src;
            if (_lifted) src = _float.Clone();
            else src = Doc.Image.Crop(_srcRect);
            if (_mask != null)
            {
                for (int y = 0; y < src.H; y++)
                {
                    uint* row = src.Row(y);
                    for (int x = 0; x < src.W; x++) if (_mask[y * _srcRect.Width + x] == 0) { row[x] = E.Color2 | 0xFF000000u; }
                }
            }
            if (_lifted && (_rect.Width != src.W || _rect.Height != src.H))
            {
                var scaled = Raster.Resample(src, _rect.Width, _rect.Height);
                src.Dispose();
                src = scaled;
            }
            hasAlpha = _hasAlphaContent && src.AnyTransparent(src.Bounds);
            return src;
        }

        public void ReplaceFloating(Dib img, bool keepTopLeft = true)
        {
            if (!_active) return;
            if (!_lifted) Lift(false);
            var old = _rect;
            _float?.Dispose();
            _float = img;
            _mask = null;                       // shape is carried by alpha from now on
            _hasAlphaContent = _hasAlphaContent || img.AnyTransparent(img.Bounds);
            _srcRect = new Rectangle(0, 0, img.W, img.H);
            _rect = new Rectangle(old.X, old.Y, img.W, img.H);
            _touched = Raster.Union(_touched, _rect);
            V?.InvalidateImageOutline(old, 8);
            AfterChange();
        }

        byte[] ScaleMaskTo(int w, int h)
        {
            if (_mask == null) return null;
            var m = new byte[w * h];
            for (int y = 0; y < h; y++)
            {
                int sy = Math.Min(_srcRect.Height - 1, y * _srcRect.Height / Math.Max(1, h));
                for (int x = 0; x < w; x++)
                {
                    int sx = Math.Min(_srcRect.Width - 1, x * _srcRect.Width / Math.Max(1, w));
                    m[y * w + x] = _mask[sy * _srcRect.Width + sx];
                }
            }
            return m;
        }

        /// <summary>The floating content at its current displayed size (used by rotate/resize/invert).</summary>
        public Dib FloatingAtSize()
        {
            if (!_lifted) Lift(false);
            if (_float.W == _rect.Width && _float.H == _rect.Height) return _float.Clone();
            return Raster.Resample(_float, _rect.Width, _rect.Height);
        }

        public void SetMask(byte[] m) { _mask = m; }

        // ------------------------------------------------------------------ lifting & committing
        void Lift(bool copy)
        {
            if (_lifted || !_active) return;
            if (_inverted)
            {
                // inverted selections are not movable; materialise them as a full-size float with mask
            }
            var r = _srcRect;
            r.Intersect(Doc.Bounds);
            if (r.Width <= 0 || r.Height <= 0) { _active = false; return; }
            if (r != _srcRect)
            {
                // clip the mask to the picture
                if (_mask != null)
                {
                    var nm = new byte[r.Width * r.Height];
                    for (int y = 0; y < r.Height; y++)
                        for (int x = 0; x < r.Width; x++)
                            nm[y * r.Width + x] = _mask[(y + r.Y - _srcRect.Y) * _srcRect.Width + (x + r.X - _srcRect.X)];
                    _mask = nm;
                }
                _rect = new Rectangle(_rect.X + (r.X - _srcRect.X), _rect.Y + (r.Y - _srcRect.Y), r.Width, r.Height);
                _srcRect = r;
            }
            _float = Doc.Image.Crop(_srcRect);
            _hasAlphaContent = Doc.HasAlpha;
            if (_mask != null)
                for (int y = 0; y < _float.H; y++)
                {
                    uint* row = _float.Row(y);
                    for (int x = 0; x < _float.W; x++) if (_mask[y * _float.W + x] == 0) row[x] = 0;   // transparent outside the lasso
                }
            if (!copy)
            {
                uint bg = E.Color2 | 0xFF000000u;
                for (int y = _srcRect.Top; y < _srcRect.Bottom; y++)
                {
                    uint* row = Doc.Image.Row(y);
                    for (int x = _srcRect.Left; x < _srcRect.Right; x++)
                        if (_mask == null || _mask[(y - _srcRect.Y) * _srcRect.Width + (x - _srcRect.X)] != 0) row[x] = bg;
                }
                _liftedFrom = _srcRect;
            }
            else _liftedFrom = Rectangle.Empty;
            _lifted = true;
            _pasted = false;
            _touched = Raster.Union(_touched, _srcRect);
            _srcRect = new Rectangle(0, 0, _float.W, _float.H);
            E.InvalidateImage(_rect);
        }

        /// <summary>Stamp the floating pixels into the picture at the current position.</summary>
        void Stamp(Dib target, Rectangle dst)
        {
            if (_float == null) return;
            bool scaled = dst.Width != _float.W || dst.Height != _float.H;
            Dib src = scaled ? Raster.ScaleNearest(_float, dst.Width, dst.Height) : _float;
            byte[] mask = scaled ? null : _mask;
            uint key = E.Color2 | 0xFF000000u;
            bool transparent = E.TransparentSelection;
            var clip = Rectangle.Intersect(dst, target.Bounds);
            for (int y = clip.Top; y < clip.Bottom; y++)
            {
                uint* d = target.Row(y);
                uint* s = src.Row(y - dst.Y);
                for (int x = clip.Left; x < clip.Right; x++)
                {
                    int sx = x - dst.X;
                    uint c = s[sx];
                    if (mask != null && mask[(y - dst.Y) * src.W + sx] == 0) continue;
                    if (transparent && (c | 0xFF000000u) == key && (c >> 24) == 255) continue;
                    d[x] = (c >> 24) == 255 ? c : Px.Over(d[x], c);
                }
            }
            if (scaled) src.Dispose();
        }

        void CommitInternal()
        {
            if (!_active) return;
            if (_lifted)
            {
                var dst = _rect;
                if (_float != null && (dst.Width != _float.W || dst.Height != _float.H))
                {
                    // final quality resample for stretched selections
                    var hq = Raster.Resample(_float, dst.Width, dst.Height);
                    var m = ScaleMaskTo(dst.Width, dst.Height);
                    _float.Dispose(); _float = hq; _mask = m;
                    _srcRect = new Rectangle(0, 0, hq.W, hq.H);
                }
                Stamp(Doc.Image, dst);
                var touched = Raster.Union(Raster.Union(_touched, _liftedFrom), dst);
                _float?.Dispose(); _float = null;
                _lifted = false;
                E.Commit(touched, _pasted ? "Paste" : "Move selection");
            }
            var old = _rect;
            _active = false; _inverted = false; _mask = null; _pasted = false;
            _touched = Rectangle.Empty;
            V?.InvalidateImageOutline(old, 8);
            E.SetSelSize(null);
            E.Notify(Change.Selection | Change.Commands);
        }

        public override void Commit() => CommitInternal();

        public override bool UndoPending()
        {
            if (!_active) return false;
            if (_lifted)
            {
                // put everything back exactly as it was before the lift
                var r = Raster.Union(Raster.Union(_touched, _liftedFrom), _rect);
                Doc.Revert(r);
                E.InvalidateImage(r);
            }
            Drop();
            return true;
        }

        void AfterChange()
        {
            E.SetSelSize(_active ? (Size?)(_inverted ? _invHole.Size : _rect.Size) : null);
            V?.Invalidate();
            E.Notify(Change.Selection | Change.Commands);
        }

        // ------------------------------------------------------------------ overlay
        public override Rectangle CompositeBounds => _lifted ? _rect : Rectangle.Empty;

        public override void Composite(Dib target, Rectangle area)
        {
            if (!_lifted || _float == null) return;
            // draw floating content into the scratch whose origin is area.Location
            var dst = _rect; dst.Offset(-area.X, -area.Y);
            Stamp(target, dst);
        }

        int HandleSize => Math.Max(5, V.DpiValue * 6 / 96);

        Rectangle ViewRect { get { var r = V.ImageToView(_inverted ? _invHole : _rect); r.Inflate(1, 1); return r; } }

        public override void PaintScreen(Graphics g)
        {
            if (_mode == 1 || _mode == 2) PaintMarquee(g);
            if (!_active) return;
            g.SmoothingMode = SmoothingMode.None;
            var vr = ViewRect;
            if (_mask != null && !_lifted && !_inverted && _lassoOutline != null) PaintPath(g, _lassoOutline);
            else if (_mask != null && _lifted && _lassoOutline != null) PaintPath(g, _lassoOutline, _rect.X - _lassoOrigin.X, _rect.Y - _lassoOrigin.Y, _rect.Width / (float)Math.Max(1, _lassoSize.Width), _rect.Height / (float)Math.Max(1, _lassoSize.Height));
            if (_inverted)
            {
                DashedRect(g, Rectangle.Inflate(V.ImageToView(Doc.Bounds), 1, 1));
            }
            DashedRect(g, vr);
            if (_inverted) return;
            int hs = HandleSize;
            foreach (var hr in Handles(vr, hs))
            {
                g.FillRectangle(Theme.Brush(Theme.Handle), hr);
                g.DrawRectangle(Theme.Pen(Color.FromArgb(0x50, 0x50, 0x50)), hr.X, hr.Y, hr.Width - 1, hr.Height - 1);
            }
        }

        static void DashedRect(Graphics g, Rectangle r)
        {
            using (var a = new Pen(Color.White)) g.DrawRectangle(a, r.X, r.Y, r.Width - 1, r.Height - 1);
            using (var b = new Pen(Color.FromArgb(0x10, 0x10, 0x10)) { DashPattern = new[] { 4f, 4f } }) g.DrawRectangle(b, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        List<Point> _lassoOutline; Point _lassoOrigin; Size _lassoSize;

        void PaintPath(Graphics g, List<Point> pts, int dx = 0, int dy = 0, float sx = 1, float sy = 1)
        {
            if (pts.Count < 2) return;
            var vp = new PointF[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                float ix = _lassoOrigin.X + dx + (pts[i].X - _lassoOrigin.X) * sx, iy = _lassoOrigin.Y + dy + (pts[i].Y - _lassoOrigin.Y) * sy;
                vp[i] = V.ImageToViewF(new PointF(ix, iy));
            }
            g.SmoothingMode = SmoothingMode.None;
            using (var a = new Pen(Color.White)) g.DrawPolygon(a, vp);
            using (var b = new Pen(Color.FromArgb(0x10, 0x10, 0x10)) { DashPattern = new[] { 4f, 4f } }) g.DrawPolygon(b, vp);
        }

        void PaintMarquee(Graphics g)
        {
            if (!_dragged) return;
            g.SmoothingMode = SmoothingMode.None;
            if (_mode == 1)
            {
                var r = MarqueeRect();
                DashedRect(g, Rectangle.Inflate(V.ImageToView(r), 1, 1));
            }
            else if (_lasso.Count > 1)
            {
                var vp = new PointF[_lasso.Count];
                for (int i = 0; i < _lasso.Count; i++) vp[i] = V.ImageToViewF(new PointF(_lasso[i].X + 0.5f, _lasso[i].Y + 0.5f));
                using (var a = new Pen(Color.White)) g.DrawLines(a, vp);
                using (var b = new Pen(Color.FromArgb(0x10, 0x10, 0x10)) { DashPattern = new[] { 4f, 4f } }) g.DrawLines(b, vp);
            }
        }

        static IEnumerable<Rectangle> Handles(Rectangle vr, int hs)
        {
            int cx = vr.X + vr.Width / 2, cy = vr.Y + vr.Height / 2;
            int l = vr.Left - hs / 2, t = vr.Top - hs / 2, r = vr.Right - 1 - hs / 2, b = vr.Bottom - 1 - hs / 2;
            yield return new Rectangle(l, t, hs, hs);
            yield return new Rectangle(cx - hs / 2, t, hs, hs);
            yield return new Rectangle(r, t, hs, hs);
            yield return new Rectangle(r, cy - hs / 2, hs, hs);
            yield return new Rectangle(r, b, hs, hs);
            yield return new Rectangle(cx - hs / 2, b, hs, hs);
            yield return new Rectangle(l, b, hs, hs);
            yield return new Rectangle(l, cy - hs / 2, hs, hs);
        }

        int HitHandle(Point view)
        {
            if (!_active || _inverted) return -1;
            int i = 0, hs = HandleSize;
            foreach (var r in Handles(ViewRect, hs))
            {
                var rr = Rectangle.Inflate(r, 3, 3);
                if (rr.Contains(view)) return i;
                i++;
            }
            return -1;
        }

        bool HitInside(MouseArgs a)
        {
            if (!_active) return false;
            if (_inverted) return false;
            return _rect.Contains(a.Pi);
        }

        public override IntPtr GetCursor(MouseArgs a)
        {
            if (_mode == 3) return AppCursors.SizeAll;
            if (!_btnDown)
            {
                int h = HitHandle(a.ViewPt);
                if (h >= 0) switch (h) { case 0: case 4: return AppCursors.SizeNWSE; case 2: case 6: return AppCursors.SizeNESW; case 1: case 5: return AppCursors.SizeNS; default: return AppCursors.SizeWE; }
                if (HitInside(a)) return AppCursors.SizeAll;
            }
            return AppCursors.Crosshair;
        }

        public override bool WantsRightClickMenu(MouseArgs a) => !_btnDown;

        // ------------------------------------------------------------------ mouse
        public override void MouseDown(MouseArgs a)
        {
            if (_btnDown) { CancelStroke(); return; }
            if (!a.Left) return;
            _btnDown = true; _btn = MouseButtons.Left;
            _startPt = _cur = a.Pi; _startF = a.P;
            _downView = a.ViewPt;
            _dragged = false;
            _copyOnLift = a.Ctrl;
            int h = HitHandle(a.ViewPt);
            if (h >= 0)
            {
                _mode = 4; _handle = h; _startRect = _rect;
                return;
            }
            if (HitInside(a))
            {
                _mode = 3; _startRect = _rect;
                return;
            }
            // a click outside places the current selection; a new one only starts once the mouse is dragged
            CommitInternal();
            _mode = E.SelectionMode == SelectMode.FreeForm ? 2 : 1;
            _lasso.Clear();
            _lasso.Add(ClampPt(a.Pi));
        }

        Point _downView;
        bool _dragged, _copyOnLift;

        /// <summary>True once the pointer moved past the system drag threshold (a plain click never selects or lifts).</summary>
        bool PastDragThreshold(Point view)
        {
            if (_dragged) return true;
            int dpi = V.DpiValue;
            int tx = Math.Max(3, Native.MetricForDpi(Native.SM_CXDRAG, dpi)), ty = Math.Max(3, Native.MetricForDpi(Native.SM_CYDRAG, dpi));
            if (Math.Abs(view.X - _downView.X) < tx && Math.Abs(view.Y - _downView.Y) < ty) return false;
            _dragged = true;
            if (_mode == 3 || _mode == 4)
            {
                if (!_lifted) Lift(_mode == 3 && _copyOnLift);
                else if (_mode == 3 && _copyOnLift)
                {
                    // Ctrl+drag a floating selection: leave a copy behind
                    Stamp(Doc.Image, _rect);
                    _touched = Raster.Union(_touched, _rect);
                    E.InvalidateImage(_rect);
                }
                _startRect = _rect;
            }
            return true;
        }

        Point ClampPt(Point p) => new Point(Math.Max(0, Math.Min(Doc.Width - 1, p.X)), Math.Max(0, Math.Min(Doc.Height - 1, p.Y)));

        Rectangle MarqueeRect()
        {
            var a = _startPt; var b = _cur;
            int x0 = Math.Min(a.X, b.X), y0 = Math.Min(a.Y, b.Y), x1 = Math.Max(a.X, b.X), y1 = Math.Max(a.Y, b.Y);
            var r = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
            r.Intersect(Doc.Bounds);
            return r;
        }
        Point _cur;

        public override void MouseMove(MouseArgs a)
        {
            if (!_btnDown) return;
            if (!PastDragThreshold(a.ViewPt)) return;
            _cur = a.Pi;
            switch (_mode)
            {
                case 1:
                    {
                        var r = MarqueeRect();
                        E.SetSelSize(r.Size);
                        V.Invalidate();
                        break;
                    }
                case 2:
                    {
                        var p = ClampPt(a.Pi);
                        if (_lasso[_lasso.Count - 1] != p) _lasso.Add(p);
                        V.Invalidate();
                        break;
                    }
                case 3:
                    {
                        int dx = (int)Math.Round(a.P.X - _startF.X), dy = (int)Math.Round(a.P.Y - _startF.Y);
                        var nr = new Rectangle(_startRect.X + dx, _startRect.Y + dy, _startRect.Width, _startRect.Height);
                        if (nr == _rect) break;
                        if (a.Shift)
                        {
                            // smear: stamp a copy at every intermediate position
                            Stamp(Doc.Image, _rect);
                            _touched = Raster.Union(_touched, _rect);
                            E.InvalidateImage(_rect);
                        }
                        var old = _rect;
                        _rect = nr;
                        _touched = Raster.Union(_touched, nr);
                        V.InvalidateImageOutline(Rectangle.Union(old, nr), 8);
                        break;
                    }
                case 4: ResizeTo(a); break;
            }
        }

        void ResizeTo(MouseArgs a)
        {
            int x0 = _startRect.Left, y0 = _startRect.Top, x1 = _startRect.Right, y1 = _startRect.Bottom;
            int px = (int)Math.Round(a.P.X), py = (int)Math.Round(a.P.Y);
            switch (_handle)
            {
                case 0: x0 = px; y0 = py; break;
                case 1: y0 = py; break;
                case 2: x1 = px; y0 = py; break;
                case 3: x1 = px; break;
                case 4: x1 = px; y1 = py; break;
                case 5: y1 = py; break;
                case 6: x0 = px; y1 = py; break;
                case 7: x0 = px; break;
            }
            if (a.Shift && (_handle % 2 == 0))
            {
                // keep aspect ratio on corner handles
                double ar = _startRect.Width / (double)Math.Max(1, _startRect.Height);
                int w = Math.Abs(x1 - x0), h = Math.Abs(y1 - y0);
                if (w / ar > h) h = (int)Math.Round(w / ar); else w = (int)Math.Round(h * ar);
                if (_handle == 0) { x0 = x1 - w; y0 = y1 - h; } else if (_handle == 2) { x1 = x0 + w; y0 = y1 - h; } else if (_handle == 4) { x1 = x0 + w; y1 = y0 + h; } else { x0 = x1 - w; y1 = y0 + h; }
            }
            if (x1 - x0 < 1) { if (_handle == 0 || _handle == 6 || _handle == 7) x0 = x1 - 1; else x1 = x0 + 1; }
            if (y1 - y0 < 1) { if (_handle <= 2) y0 = y1 - 1; else y1 = y0 + 1; }
            var nr = Rectangle.FromLTRB(x0, y0, x1, y1);
            if (nr == _rect) return;
            var old = _rect;
            _rect = nr;
            _touched = Raster.Union(_touched, nr);
            E.SetSelSize(nr.Size);
            V.InvalidateImageOutline(Rectangle.Union(old, nr), 8);
        }

        public override void MouseUp(MouseArgs a)
        {
            if (!_btnDown || !a.Left) return;
            _btnDown = false;
            int mode = _mode;
            _mode = 0;
            if (!_dragged)
            {
                // plain click: nothing is selected, lifted or moved
                _lasso.Clear();
                E.SetSelSize(_active ? (Size?)(_inverted ? _invHole.Size : _rect.Size) : null);
                V.Invalidate();
                return;
            }
            if (mode == 1)
            {
                var r = MarqueeRect();
                if (r.Width >= 1 && r.Height >= 1)
                {
                    _active = true; _inverted = false;
                    _rect = _srcRect = r; _mask = null; _lassoOutline = null;
                    AfterChange();
                }
                else { V.Invalidate(); E.SetSelSize(null); }
            }
            else if (mode == 2) FinishLasso();
            else if (mode == 3 || mode == 4) { E.SetSelSize(_rect.Size); V.Invalidate(); E.Notify(Change.Selection | Change.Commands); }
        }

        void FinishLasso()
        {
            if (_lasso.Count < 3) { _lasso.Clear(); V.Invalidate(); E.SetSelSize(null); return; }
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            foreach (var p in _lasso) { x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y); }
            var r = Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
            r.Intersect(Doc.Bounds);
            if (r.Width < 1 || r.Height < 1) { _lasso.Clear(); V.Invalidate(); return; }
            // rasterise the polygon into a mask (even-odd, pixel centres)
            var m = new byte[r.Width * r.Height];
            int n = _lasso.Count;
            var xs = new List<float>();
            for (int y = 0; y < r.Height; y++)
            {
                float py = r.Y + y + 0.5f;
                xs.Clear();
                for (int i = 0; i < n; i++)
                {
                    var a = _lasso[i]; var b = _lasso[(i + 1) % n];
                    float ay = a.Y + 0.5f, by = b.Y + 0.5f;
                    if ((ay <= py && by > py) || (by <= py && ay > py))
                        xs.Add(a.X + 0.5f + (py - ay) / (by - ay) * (b.X - a.X));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int xa = (int)Math.Ceiling(xs[k] - 0.5f), xb = (int)Math.Floor(xs[k + 1] - 0.5f);
                    for (int x = Math.Max(r.X, xa); x <= Math.Min(r.Right - 1, xb); x++) m[y * r.Width + (x - r.X)] = 1;
                }
            }
            // include the outline itself
            for (int i = 0; i < n; i++)
            {
                var a = _lasso[i]; var b = _lasso[(i + 1) % n];
                var plot = new MaskPlot { M = m, R = r };
                Raster.Line(a.X, a.Y, b.X, b.Y, ref plot);
            }
            _lassoOutline = new List<Point>(_lasso);
            _lassoOrigin = r.Location; _lassoSize = r.Size;
            _lasso.Clear();
            _active = true; _inverted = false;
            _rect = _srcRect = r; _mask = m;
            AfterChange();
        }

        struct MaskPlot : Raster.IPlot
        {
            public byte[] M; public Rectangle R;
            public void Plot(int x, int y) { if (R.Contains(x, y)) M[(y - R.Y) * R.Width + (x - R.X)] = 1; }
        }

        public override void CancelStroke()
        {
            if (!_btnDown) return;
            _btnDown = false;
            if (_mode == 3 || _mode == 4)
            {
                var old = _rect;
                _rect = _startRect;
                V.InvalidateImageOutline(Rectangle.Union(old, _rect), 8);
            }
            _mode = 0; _lasso.Clear();
            V.Invalidate();
        }

        public override bool KeyDown(Keys k)
        {
            var code = k & Keys.KeyCode;
            if (_btnDown && code == Keys.Escape) { CancelStroke(); return true; }
            if (!_active) return false;
            switch (code)
            {
                case Keys.Escape:
                case Keys.Enter:
                    CommitInternal();
                    return true;
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down:
                    {
                        if (_inverted) return true;
                        if (!_lifted) Lift((k & Keys.Control) != 0);
                        int step = (k & Keys.Shift) != 0 ? 10 : 1;
                        int dx = code == Keys.Left ? -step : code == Keys.Right ? step : 0, dy = code == Keys.Up ? -step : code == Keys.Down ? step : 0;
                        var old = _rect;
                        _rect.Offset(dx, dy);
                        _touched = Raster.Union(_touched, _rect);
                        V.InvalidateImageOutline(Rectangle.Union(old, _rect), 8);
                        return true;
                    }
            }
            return false;
        }

        public override void Deactivate()
        {
            if (_btnDown) CancelStroke();
            CommitInternal();
        }

        public override void ColorsChanged(int slot)
        {
            if (_lifted && E.TransparentSelection) E.InvalidateImage(_rect);
        }

        public void TransparencyChanged() { if (_lifted) E.InvalidateImage(_rect); }
    }
}
