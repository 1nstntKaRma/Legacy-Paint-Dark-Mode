using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PaintDark
{
    /// <summary>
    /// The application window. The caption is drawn by us (dark, with the Quick Access Toolbar) while
    /// the window keeps its real frame, so snapping, Aero shake, Win11 snap layouts and shadows all work natively.
    /// </summary>
    public sealed partial class MainForm : Form, IMessageFilter
    {
        public readonly Editor E;
        public readonly Ribbon Ribbon;
        public readonly CanvasView Canvas;
        public readonly StatusBar Status;
        PrintPreviewView _preview;
        Dpi D = new Dpi(96);
        readonly BackBuffer _buf = new BackBuffer();
        bool _active = true;
        int _capHot, _capPressed;          // 1 min, 2 max, 3 close
        bool _trackingNc;
        int _qatHot = -1, _qatPressed = -1;
        bool _altPending;
        bool _showStatus = true;
        public bool QatBelow;
        readonly bool[] _qatOn = new bool[8];
        static readonly Cmd[] QatOrder = { Cmd.New, Cmd.Open, Cmd.Save, Cmd.Print, Cmd.PrintPreview, Cmd.SendEmail, Cmd.Undo, Cmd.Redo };
        static readonly string[] QatNames = { "New", "Open", "Save", "Print", "Print preview", "Send in email", "Undo", "Redo" };
        string[] _startArgs;

        public MainForm(string[] args)
        {
            _startArgs = args;
            E = new Editor { Form = this };
            Text = "Untitled - Paint";
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
            BackColor = Theme.Caption;
            StartPosition = FormStartPosition.Manual;
            try { Icon = Icons.BuildAppIcon(32); } catch { }

            Canvas = new CanvasView(E);
            E.View = Canvas;
            Ribbon = new Ribbon(this, E);
            Status = new StatusBar(this, E);
            Controls.Add(Canvas);
            Controls.Add(Status);
            Controls.Add(Ribbon);
            Ribbon.BringToFront();
            Ribbon.LayoutChanged += () => DoLayout();
            Canvas.ZoomChanged += () => { Status.Invalidate(); Ribbon.Invalidate(); };

            E.Changed += OnEditorChanged;
            LoadSettings();
            NewDocument(true);
            Popups.MainWindow = this;
            Popups.Install();
            Application.AddMessageFilter(this);
        }

        // ================================================================== geometry
        int CaptionH => D.S(30);
        int QatStripH => D.S(28);
        bool Maximized => IsHandleCreated && Native.IsZoomed(Handle);
        int CapBtnW => D.S(46);
        Rectangle CloseRect => new Rectangle(ClientSize.Width - CapBtnW, 0, CapBtnW, CaptionH);
        Rectangle MaxRect => new Rectangle(ClientSize.Width - CapBtnW * 2, 0, CapBtnW, CaptionH);
        Rectangle MinRect => new Rectangle(ClientSize.Width - CapBtnW * 3, 0, CapBtnW, CaptionH);
        Rectangle IconRect => new Rectangle(D.S(8), (CaptionH - D.S(16)) / 2, D.S(16), D.S(16));

        public int ZoomPercent => (int)Math.Round(Canvas.Zoom * 100);
        public float ZoomValue => Canvas.Zoom;
        public void SetZoom(float z) => Canvas.SetZoom(z, null);

        List<Cmd> VisibleQat
        {
            get
            {
                var l = new List<Cmd>();
                for (int i = 0; i < QatOrder.Length; i++) if (_qatOn[i]) l.Add(QatOrder[i]);
                return l;
            }
        }

        /// <summary>Rectangles of QAT buttons (+ the customize dropdown as the last entry), in form coordinates.</summary>
        List<Rectangle> QatRects()
        {
            var res = new List<Rectangle>();
            int n = VisibleQat.Count;
            int bw = D.S(24), bh = D.S(24), pitch = D.S(26);
            int x, y;
            if (QatBelow) { x = D.S(6); y = CaptionH + Ribbon.LayoutHeight + (QatStripH - bh) / 2; }
            else { x = IconRect.Right + D.S(10); y = (CaptionH - bh) / 2; }
            for (int i = 0; i < n; i++) res.Add(new Rectangle(x + i * pitch, y, bw, bh));
            res.Add(new Rectangle(x + n * pitch - D.S(2), y, D.S(14), bh));
            return res;
        }

        public IEnumerable<KeyValuePair<Point, Cmd>> QatKeyTipTargets()
        {
            var rects = QatRects();
            var cmds = VisibleQat;
            for (int i = 0; i < cmds.Count; i++)
            {
                var r = rects[i];
                // badge position in *ribbon* coordinates is not needed: caption tips are drawn by us
                yield return new KeyValuePair<Point, Cmd>(new Point(r.X + r.Width / 2, r.Bottom + D.S(2) - (QatBelow ? 0 : 0)), cmds[i]);
            }
        }

        // ================================================================== window chrome
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkMode.ApplyToWindow(Handle);
            D = new Dpi(Native.DpiOf(Handle));
            ApplyDpi();
            Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, Native.SWP_FRAMECHANGED | Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        }

        void ApplyDpi()
        {
            AppCursors.Init(D.Value);
            Ribbon.SetDpi(D.Value);
            Canvas.SetDpi(D.Value);
            Status.SetDpi(D.Value);
            Ribbon.Height = Ribbon.LayoutHeight;
            MinimumSize = new Size(D.S(460), D.S(340));
            DoLayout();
        }

        protected override void OnLayout(LayoutEventArgs levent) { DoLayout(); }

        void DoLayout()
        {
            if (Ribbon == null) return;
            int w = ClientSize.Width, h = ClientSize.Height;
            int y = CaptionH;
            Ribbon.SetBounds(0, y, w, Ribbon.Expanded ? Ribbon.FullHeight : Ribbon.LayoutHeight);
            y += Ribbon.LayoutHeight;
            if (QatBelow) y += QatStripH;
            int sh = _showStatus ? Status.PreferredHeight : 0;
            Status.Visible = _showStatus;
            Status.SetBounds(0, h - sh, w, sh);
            var main = new Rectangle(0, y, w, Math.Max(0, h - y - sh));
            Canvas.Bounds = main;
            if (_preview != null) _preview.Bounds = main;
            if (Ribbon.Expanded) Ribbon.BringToFront();
            Invalidate(new Rectangle(0, 0, w, CaptionH));
            if (QatBelow) Invalidate(new Rectangle(0, CaptionH + Ribbon.LayoutHeight, w, QatStripH));
        }

        public void RibbonExpandedChanged(bool expanded) => DoLayout();

        protected override void WndProc(ref Message m)
        {
            if (TestHook.TryHandle(this, ref m)) return;
            switch (m.Msg)
            {
                case Native.WM_NCCALCSIZE:
                    if (m.WParam != IntPtr.Zero)
                    {
                        var p = (NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(NCCALCSIZE_PARAMS));
                        int top = p.rgrc0.Top;
                        Native.DefWindowProc(m.HWnd, m.Msg, m.WParam, m.LParam);
                        p = (NCCALCSIZE_PARAMS)Marshal.PtrToStructure(m.LParam, typeof(NCCALCSIZE_PARAMS));
                        bool max = Native.IsZoomed(m.HWnd);
                        int dpi = Native.DpiOf(m.HWnd);
                        p.rgrc0.Top = top + (max ? Native.MetricForDpi(Native.SM_CYFRAME, dpi) + Native.MetricForDpi(Native.SM_CXPADDEDBORDER, dpi) : 0);
                        if (max) AdjustForAutoHideTaskbar(ref p.rgrc0);
                        Marshal.StructureToPtr(p, m.LParam, false);
                        m.Result = IntPtr.Zero;
                        return;
                    }
                    break;
                case Native.WM_NCHITTEST:
                    {
                        var r = Native.DefWindowProc(m.HWnd, m.Msg, m.WParam, m.LParam);
                        int ht = (int)r;
                        if (ht != Native.HTCLIENT) { m.Result = r; return; }
                        var pt = PointToClient(new Point(Native.LoWord(m.LParam), Native.HiWord(m.LParam)));
                        m.Result = (IntPtr)CaptionHitTest(pt);
                        return;
                    }
                case Native.WM_NCACTIVATE:
                    _active = m.WParam != IntPtr.Zero;
                    InvalidateCaption();
                    m.Result = Native.DefWindowProc(m.HWnd, m.Msg, m.WParam, (IntPtr)(-1));
                    return;
                case Native.WM_NCMOUSEMOVE:
                    {
                        int ht = (int)m.WParam;
                        int hot = ht == Native.HTMINBUTTON ? 1 : ht == Native.HTMAXBUTTON ? 2 : ht == Native.HTCLOSE ? 3 : 0;
                        SetCapHot(hot);
                        if (!_trackingNc)
                        {
                            var tme = new TRACKMOUSEEVENT { cbSize = Marshal.SizeOf(typeof(TRACKMOUSEEVENT)), dwFlags = Native.TME_LEAVE | Native.TME_NONCLIENT, hwndTrack = Handle };
                            Native.TrackMouseEvent(ref tme);
                            _trackingNc = true;
                        }
                        if (hot != 0)
                        {
                            Tips.Request("cap" + hot, hot == 1 ? "Minimize" : hot == 2 ? (Maximized ? "Restore Down" : "Maximize") : "Close", null,
                                PointToScreen(new Point(Native.LoWord(m.LParam) - Left, CaptionH + D.S(4))), D.Value);
                            m.Result = IntPtr.Zero;
                            return;
                        }
                        Tips.Hide();
                        break;
                    }
                case Native.WM_NCMOUSELEAVE:
                    _trackingNc = false;
                    if (_capPressed == 0) SetCapHot(0);
                    Tips.Hide();
                    break;
                case Native.WM_NCLBUTTONDOWN:
                    {
                        int ht = (int)m.WParam;
                        Popups.CloseAll(); Tips.Hide();
                        if (Ribbon.KeyTipsActive) Ribbon.EndKeyTips();
                        if (ht == Native.HTMINBUTTON || ht == Native.HTMAXBUTTON || ht == Native.HTCLOSE)
                        {
                            _capPressed = ht == Native.HTMINBUTTON ? 1 : ht == Native.HTMAXBUTTON ? 2 : 3;
                            _capHot = _capPressed;
                            Capture = true;
                            InvalidateCaption();
                            m.Result = IntPtr.Zero;
                            return;
                        }
                        if (ht == Native.HTSYSMENU)
                        {
                            var sp = PointToScreen(new Point(IconRect.Left, CaptionH));
                            ShowSystemMenu(sp);
                            m.Result = IntPtr.Zero;
                            return;
                        }
                        Ribbon.Collapse();
                        break;
                    }
                case Native.WM_NCLBUTTONDBLCLK:
                    if ((int)m.WParam == Native.HTSYSMENU) { Close(); m.Result = IntPtr.Zero; return; }
                    if ((int)m.WParam == Native.HTMINBUTTON || (int)m.WParam == Native.HTMAXBUTTON || (int)m.WParam == Native.HTCLOSE) { m.Result = IntPtr.Zero; return; }
                    break;
                case Native.WM_NCLBUTTONUP:
                    if (_capPressed != 0) { m.Result = IntPtr.Zero; return; }
                    break;
                case Native.WM_NCRBUTTONUP:
                    if ((int)m.WParam == Native.HTCAPTION || (int)m.WParam == Native.HTSYSMENU)
                    {
                        ShowSystemMenu(new Point(Native.LoWord(m.LParam), Native.HiWord(m.LParam)));
                        m.Result = IntPtr.Zero;
                        return;
                    }
                    break;
                case Native.WM_DPICHANGED:
                    {
                        D = new Dpi(Native.HiWord(m.WParam));
                        var r = (RECT)Marshal.PtrToStructure(m.LParam, typeof(RECT));
                        Native.SetWindowPos(Handle, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                        ApplyDpi();
                        Invalidate(true);
                        m.Result = IntPtr.Zero;
                        return;
                    }
                case Native.WM_SYSKEYDOWN:
                    if ((int)m.WParam == Native.VK_MENU) { if (((long)m.LParam & 0x40000000) == 0) _altPending = true; }
                    else _altPending = false;
                    if ((int)m.WParam == 0x79) { m.Result = IntPtr.Zero; return; } // F10 handled on key up
                    break;
                case Native.WM_KEYDOWN:
                    _altPending = false;
                    break;
                case Native.WM_SYSKEYUP:
                    if (((int)m.WParam == Native.VK_MENU && _altPending) || (int)m.WParam == 0x79)
                    {
                        _altPending = false;
                        if (Popups.AnyOpen) { Popups.CloseAll(); m.Result = IntPtr.Zero; return; }
                        if (Ribbon.KeyTipsActive) Ribbon.EndKeyTips(); else Ribbon.BeginKeyTips();
                        m.Result = IntPtr.Zero;
                        return;
                    }
                    break;
                case Native.WM_SYSCOMMAND:
                    if ((((int)m.WParam) & 0xFFF0) == Native.SC_KEYMENU && m.LParam == IntPtr.Zero) { m.Result = IntPtr.Zero; return; }
                    break;
                case Native.WM_ACTIVATEAPP:
                    if (m.WParam == IntPtr.Zero) { Popups.CloseAll(); Tips.Hide(); Ribbon.EndKeyTips(); Ribbon.Collapse(); }
                    break;
                case Native.WM_ENTERSIZEMOVE:
                    Popups.CloseAll(); Tips.Hide();
                    break;
                case Native.WM_SETTINGCHANGE:
                case Native.WM_THEMECHANGED:
                    DarkMode.ApplyToWindow(Handle);
                    break;
            }
            base.WndProc(ref m);
        }

        void AdjustForAutoHideTaskbar(ref RECT r)
        {
            try
            {
                var abd = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)) };
                if (((long)Native.SHAppBarMessage(Native.ABM_GETSTATE, ref abd) & Native.ABS_AUTOHIDE) == 0) return;
                IntPtr mon = Native.MonitorFromWindow(Handle, Native.MONITOR_DEFAULTTONEAREST);
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                Native.GetMonitorInfo(mon, ref mi);
                for (uint edge = 0; edge < 4; edge++)
                {
                    var q = new APPBARDATA { cbSize = Marshal.SizeOf(typeof(APPBARDATA)), uEdge = edge, rc = mi.rcMonitor };
                    if (Native.SHAppBarMessage(Native.ABM_GETAUTOHIDEBAREX, ref q) == IntPtr.Zero) continue;
                    // leave a 2px strip so the auto-hide taskbar can still be revealed
                    if (edge == Native.ABE_BOTTOM) r.Bottom -= 2; else if (edge == Native.ABE_TOP) r.Top += 2; else if (edge == Native.ABE_LEFT) r.Left += 2; else r.Right -= 2;
                }
            }
            catch { }
        }

        int CaptionHitTest(Point p)
        {
            if (p.Y < 0) return Native.HTCLIENT;
            if (!Maximized && p.Y < D.S(5) && p.X < ClientSize.Width - CapBtnW * 3)
                return p.X < D.S(12) ? Native.HTTOPLEFT : (p.X > ClientSize.Width - D.S(12) ? Native.HTTOPRIGHT : Native.HTTOP);
            if (p.Y >= CaptionH) return Native.HTCLIENT;
            if (CloseRect.Contains(p)) return Native.HTCLOSE;
            if (MaxRect.Contains(p)) return Native.HTMAXBUTTON;
            if (MinRect.Contains(p)) return Native.HTMINBUTTON;
            if (Rectangle.Inflate(IconRect, D.S(4), D.S(6)).Contains(p)) return Native.HTSYSMENU;
            if (!QatBelow) foreach (var r in QatRects()) if (r.Contains(p)) return Native.HTCLIENT;
            return Native.HTCAPTION;
        }

        void SetCapHot(int hot)
        {
            if (hot == _capHot) return;
            _capHot = hot;
            Invalidate(new Rectangle(MinRect.X, 0, CapBtnW * 3, CaptionH));
        }

        void ShowSystemMenu(Point screen)
        {
            IntPtr menu = Native.GetSystemMenu(Handle, false);
            if (menu == IntPtr.Zero) return;
            bool max = Maximized;
            const uint MF_ENABLED = 0, MF_GRAYED = 1;
            Native.EnableMenuItem(menu, Native.SC_RESTORE, max ? MF_ENABLED : MF_GRAYED);
            Native.EnableMenuItem(menu, 0xF010 /*SC_MOVE*/, max ? MF_GRAYED : MF_ENABLED);
            Native.EnableMenuItem(menu, 0xF000 /*SC_SIZE*/, max ? MF_GRAYED : MF_ENABLED);
            Native.EnableMenuItem(menu, Native.SC_MINIMIZE, MF_ENABLED);
            Native.EnableMenuItem(menu, Native.SC_MAXIMIZE, max ? MF_GRAYED : MF_ENABLED);
            Native.SetMenuDefaultItem(menu, Native.SC_CLOSE, 0);
            int cmd = Native.TrackPopupMenuEx(menu, 0x0100 /*TPM_RETURNCMD*/ | 0x0002 /*TPM_RIGHTBUTTON*/, screen.X, screen.Y, Handle, IntPtr.Zero);
            if (cmd != 0) Native.PostMessage(Handle, Native.WM_SYSCOMMAND, (IntPtr)cmd, IntPtr.Zero);
        }

        public void InvalidateCaption()
        {
            if (!IsHandleCreated) return;
            Invalidate(new Rectangle(0, 0, ClientSize.Width, CaptionH));
            if (QatBelow) Invalidate(new Rectangle(0, CaptionH + Ribbon.LayoutHeight, ClientSize.Width, QatStripH));
        }

        // ================================================================== caption painting
        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var clip = e.ClipRectangle;
            var g = _buf.Begin(ClientSize, clip);
            try { PaintCaption(g); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            _buf.End(g, e.Graphics, clip);
        }

        void PaintCaption(Graphics g)
        {
            int w = ClientSize.Width;
            var bg = _active ? Theme.Caption : Theme.CaptionInactive;
            Draw.Fill(g, new Rectangle(0, 0, w, CaptionH), bg);
            if (QatBelow)
            {
                var strip = new Rectangle(0, CaptionH + Ribbon.LayoutHeight, w, QatStripH);
                Draw.Fill(g, strip, Theme.Ribbon);
                Draw.HLine(g, 0, w, strip.Bottom - 1, Theme.RibbonBorder);
            }
            var fg = _active ? Theme.CaptionText : Theme.CaptionTextInactive;
            Icons.DrawIcon(g, Ico.AppIcon, IconRect.X, IconRect.Y, D.S(16));

            // QAT
            var rects = QatRects();
            var cmds = VisibleQat;
            for (int i = 0; i < cmds.Count; i++)
            {
                bool en = IsEnabled(cmds[i]);
                Draw.ButtonFace(g, rects[i], _qatHot == i && en, _qatPressed == i && _qatHot == i, false);
                var ic = Commands.Info(cmds[i]).Icon;
                Icons.DrawIcon(g, ic, rects[i].X + (rects[i].Width - D.S(16)) / 2, rects[i].Y + (rects[i].Height - D.S(16)) / 2, D.S(16), en);
            }
            var dd = rects[rects.Count - 1];
            Draw.ButtonFace(g, dd, _qatHot == cmds.Count, false, false);
            Draw.HLine(g, dd.X + D.S(3), dd.Right - D.S(3), dd.Y + dd.Height / 2 - D.S(3), fg);
            Draw.Chevron(g, dd.X + dd.Width / 2, dd.Y + dd.Height / 2 + D.S(2), fg, D);

            // title + contextual header
            int tx = QatBelow ? IconRect.Right + D.S(10) : dd.Right + D.S(8);
            if (!QatBelow) Draw.VLine(g, tx, D.S(8), CaptionH - D.S(8), Theme.GroupSep);
            tx += D.S(10);
            var ctx = Ribbon.ContextHeaderRect;
            int titleRight = MinRect.Left - D.S(8);
            if (!ctx.IsEmpty)
            {
                var hr = new Rectangle(ctx.X, D.S(4), ctx.Width, CaptionH - D.S(4));
                using (var b = new SolidBrush(Color.FromArgb(46, Theme.ContextTabText))) g.FillRectangle(b, hr);
                Draw.Fill(g, new Rectangle(hr.X, hr.Y, hr.Width, D.S(3)), Theme.ContextTabText);
                Draw.Text(g, "Text Tools", Theme.UiFont(D), new Rectangle(hr.X, hr.Y + D.S(3), hr.Width, hr.Height - D.S(3)), Theme.ContextTabText, Draw.TextCenter);
                if (tx < hr.Right + D.S(10) && tx + D.S(200) > hr.Left) tx = hr.Right + D.S(12);
            }
            Draw.Text(g, Text, Theme.TitleFont(D), Rectangle.FromLTRB(tx, 0, Math.Max(tx, titleRight), CaptionH), fg, Draw.TextLeft | TextFormatFlags.EndEllipsis);

            // caption buttons
            PaintCapButton(g, MinRect, 1, "", fg);
            PaintCapButton(g, MaxRect, 2, Maximized ? "" : "", fg);
            PaintCapButton(g, CloseRect, 3, "", fg);

            if (!Maximized && !Native.IsWin11) Draw.HLine(g, 0, w, 0, _active ? Theme.C(0x3A3A3A) : Theme.C(0x333333));
            Ribbon.PaintCaptionKeyTips(g, D.Value);
        }

        static Font _glyphFont;
        static int _glyphDpi;

        void PaintCapButton(Graphics g, Rectangle r, int id, string glyph, Color fg)
        {
            bool hot = _capHot == id, down = _capPressed == id && hot;
            if (id == 3 && (hot || down)) { Draw.Fill(g, r, down ? Theme.CloseDown : Theme.CloseHover); fg = Color.White; }
            else if (hot || down) Draw.Fill(g, r, down ? Theme.CaptionBtnDown : Theme.CaptionBtnHover);
            if (_glyphFont == null || _glyphDpi != D.Value)
            {
                _glyphFont?.Dispose();
                string face = FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
                _glyphFont = new Font(face, 10f * D.Value / 96f, FontStyle.Regular, GraphicsUnit.Pixel);
                _glyphDpi = D.Value;
            }
            Draw.Text(g, glyph, _glyphFont, r, fg, Draw.TextCenter);
        }

        static bool FontExists(string name)
        {
            try { using (var f = new FontFamily(name)) return true; } catch { return false; }
        }

        // ================================================================== caption mouse (QAT + caption buttons while captured)
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_capPressed != 0)
            {
                var r = _capPressed == 1 ? MinRect : _capPressed == 2 ? MaxRect : CloseRect;
                SetCapHot(r.Contains(e.Location) ? _capPressed : 0);
                return;
            }
            var rects = QatRects();
            int hot = -1;
            for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(e.Location)) hot = i;
            if (hot != _qatHot) { _qatHot = hot; InvalidateCaption(); }
            var cmds = VisibleQat;
            if (hot >= 0 && hot < cmds.Count)
            {
                var info = Commands.Info(cmds[hot]);
                Tips.Request("qat" + hot, info.TipTitle, info.Tip, PointToScreen(new Point(rects[hot].X, rects[hot].Bottom + D.S(6))), D.Value);
            }
            else if (hot == cmds.Count) Tips.Request("qatdd", "Customize Quick Access Toolbar", null, PointToScreen(new Point(rects[hot].X, rects[hot].Bottom + D.S(6))), D.Value);
            else Tips.Hide();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_qatHot != -1) { _qatHot = -1; InvalidateCaption(); }
            Tips.Hide();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Tips.Hide();
            if (Ribbon.KeyTipsActive) Ribbon.EndKeyTips();
            if (e.Button != MouseButtons.Left) return;
            var rects = QatRects();
            for (int i = 0; i < rects.Count; i++)
                if (rects[i].Contains(e.Location))
                {
                    if (i == rects.Count - 1) { ShowQatMenu(RectangleToScreen(rects[i])); return; }
                    _qatPressed = i; InvalidateCaption();
                    return;
                }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_capPressed != 0)
            {
                int id = _capPressed;
                _capPressed = 0;
                Capture = false;
                var r = id == 1 ? MinRect : id == 2 ? MaxRect : CloseRect;
                SetCapHot(0);
                InvalidateCaption();
                if (r.Contains(e.Location))
                {
                    if (id == 1) WindowState = FormWindowState.Minimized;
                    else if (id == 2) WindowState = Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                    else Close();
                }
                return;
            }
            if (_qatPressed >= 0)
            {
                int i = _qatPressed;
                _qatPressed = -1;
                InvalidateCaption();
                var rects = QatRects();
                var cmds = VisibleQat;
                if (i < cmds.Count && rects[i].Contains(e.Location) && IsEnabled(cmds[i])) Execute(cmds[i]);
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Popups.CloseAll();
            InvalidateCaption();
        }

        // ================================================================== keyboard
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (Ribbon.KeyTipsActive)
            {
                if (Ribbon.HandleKeyTipKey(keyData)) return true;
            }
            var code = keyData & Keys.KeyCode;
            bool ctrl = (keyData & Keys.Control) != 0, shift = (keyData & Keys.Shift) != 0, alt = (keyData & Keys.Alt) != 0;
            bool textEditing = E.Tool == ToolKind.Text && E.TextTool.IsEditing && Canvas.Focused;
            bool boxFocused = !(ActiveControl == Canvas || ActiveControl == null || ActiveControl == Ribbon || ActiveControl == Status);

            if (boxFocused)
            {
                // a text box (ribbon font combo) has focus: let it handle editing keys
                if (ctrl && (code == Keys.A || code == Keys.C || code == Keys.X || code == Keys.V || code == Keys.Z || code == Keys.Y)) return base.ProcessCmdKey(ref msg, keyData);
                if (code == Keys.Delete || code == Keys.Back) return base.ProcessCmdKey(ref msg, keyData);
            }
            if (textEditing)
            {
                if (ctrl && !shift && (code == Keys.A || code == Keys.C || code == Keys.X || code == Keys.V || code == Keys.Z || code == Keys.Y || code == Keys.Home || code == Keys.End || code == Keys.Left || code == Keys.Right || code == Keys.Up || code == Keys.Down || code == Keys.Back || code == Keys.Delete))
                    return base.ProcessCmdKey(ref msg, keyData);
                if (code == Keys.Delete || code == Keys.Back) return base.ProcessCmdKey(ref msg, keyData);
                if (ctrl && code == Keys.B) { Execute(Cmd.TextBold); return true; }
                if (ctrl && code == Keys.I) { Execute(Cmd.TextItalic); return true; }
                if (ctrl && code == Keys.U) { Execute(Cmd.TextUnderline); return true; }
            }

            if (ctrl && !alt)
            {
                switch (code)
                {
                    case Keys.N: if (!shift) { Execute(Cmd.New); return true; } break;
                    case Keys.O: if (!shift) { Execute(Cmd.Open); return true; } break;
                    case Keys.S: if (!shift) { Execute(Cmd.Save); return true; } else { Execute(Cmd.SaveAs); return true; }
                    case Keys.P: Execute(Cmd.Print); return true;
                    case Keys.E: Execute(Cmd.Properties); return true;
                    case Keys.W: Execute(Cmd.Resize); return true;
                    case Keys.Z: Execute(Cmd.Undo); return true;
                    case Keys.Y: Execute(Cmd.Redo); return true;
                    case Keys.A: Execute(Cmd.SelectAll); return true;
                    case Keys.C: Execute(Cmd.Copy); return true;
                    case Keys.X: if (shift) Execute(Cmd.Crop); else Execute(Cmd.Cut); return true;
                    case Keys.V: Execute(Cmd.Paste); return true;
                    case Keys.I: if (shift) { Execute(Cmd.InvertColor); return true; } break;
                    case Keys.R: Execute(Cmd.Rulers); return true;
                    case Keys.G: Execute(Cmd.Gridlines); return true;
                    case Keys.PageUp: Execute(Cmd.ZoomIn); return true;
                    case Keys.PageDown: Execute(Cmd.ZoomOut); return true;
                    case Keys.Add: case Keys.Oemplus: E.StepSize(+1); return true;
                    case Keys.Subtract: case Keys.OemMinus: E.StepSize(-1); return true;
                    case Keys.F1: Execute(Cmd.ToggleRibbon); return true;
                    case Keys.Insert: Execute(Cmd.Copy); return true;
                }
            }
            if (!ctrl && !alt)
            {
                switch (code)
                {
                    case Keys.F1: Execute(Cmd.Help); return true;
                    case Keys.F11: Execute(Cmd.FullScreen); return true;
                    case Keys.F12: Execute(Cmd.SaveAs); return true;
                    case Keys.Delete:
                        if (shift) { Execute(Cmd.Cut); return true; }
                        if (E.Tool == ToolKind.Select || (E.Tool == ToolKind.Shape && E.Shapes.HasPending)) { Execute(Cmd.DeleteSelection); return true; }
                        break;
                    case Keys.Insert:
                        if (shift) { Execute(Cmd.Paste); return true; }
                        break;
                }
            }
            if (_preview != null && !ctrl && !alt)
            {
                if (code == Keys.Escape) { Execute(Cmd.PreviewClose); return true; }
                if (code == Keys.PageDown || code == Keys.Right) { Execute(Cmd.PreviewNext); return true; }
                if (code == Keys.PageUp || code == Keys.Left) { Execute(Cmd.PreviewPrev); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public bool PreFilterMessage(ref Message m)
        {
            // automated tests replay input directly; ignore the physical mouse so it cannot interleave
            if (TestHook.Enabled && ((m.Msg >= 0x200 && m.Msg <= 0x20E) || (m.Msg >= 0xA0 && m.Msg <= 0xAD) || m.Msg == 0x2A3 || m.Msg == 0x2A2)) return true;
            if (Ribbon.KeyTipsActive && (m.Msg == Native.WM_LBUTTONDOWN || m.Msg == Native.WM_RBUTTONDOWN || m.Msg == Native.WM_MBUTTONDOWN || m.Msg == Native.WM_NCLBUTTONDOWN))
                Ribbon.EndKeyTips();
            if (Ribbon.Expanded && (m.Msg == Native.WM_LBUTTONDOWN || m.Msg == Native.WM_RBUTTONDOWN) && !Popups.AnyOpen)
            {
                if (m.HWnd != Ribbon.Handle && !IsChildOf(m.HWnd, Ribbon.Handle)) Ribbon.Collapse();
            }
            return false;
        }

        static bool IsChildOf(IntPtr h, IntPtr parent)
        {
            while (h != IntPtr.Zero) { if (h == parent) return true; h = Native.GetAncestor(h, 1); }
            return false;
        }

        /// <summary>Test hook: behave as if a key was pressed in the app.</summary>
        public void TestKey(Keys k)
        {
            var code = k & Keys.KeyCode;
            if (Popups.AnyOpen)
            {
                if (!Popups.Top.HandleKey(k) && code == Keys.Escape) Popups.Close(Popups.Top);
                return;
            }
            var target = ActiveControl ?? Canvas;
            var msg = Message.Create(target.Handle, Native.WM_KEYDOWN, (IntPtr)(int)code, IntPtr.Zero);
            if (ProcessCmdKey(ref msg, k)) return;
            Native.SendMessage(target.Handle, Native.WM_KEYDOWN, (IntPtr)(int)code, IntPtr.Zero);
            Native.SendMessage(target.Handle, Native.WM_KEYUP, (IntPtr)(int)code, (IntPtr)unchecked((int)0xC0000001));
        }

        public void FocusCanvas() { if (_preview != null) _preview.Focus(); else Canvas.Focus(); }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Canvas.Focus();
            if (_thumbWanted) BeginInvoke(new Action(ToggleThumbnail));
            if (_startArgs != null && _startArgs.Length > 0)
            {
                foreach (var a in _startArgs)
                {
                    if (a.StartsWith("/") || a.StartsWith("-")) continue;
                    BeginInvoke(new Action(() => OpenFile(a, false)));
                    break;
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;
            Popups.CloseAll();
            E.CommitPending();
            if (!ConfirmDiscard()) { e.Cancel = true; return; }
            SaveSettings();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Application.RemoveMessageFilter(this);
            CloseThumbnail();
            base.OnFormClosed(e);
        }

        // ================================================================== editor notifications
        void OnEditorChanged(Change c)
        {
            if ((c & (Change.Tool | Change.Colors | Change.Commands | Change.ToolSize | Change.Palette | Change.Text | Change.Selection | Change.Zoom | Change.View | Change.Image)) != 0)
            {
                Ribbon.ShowTextTab(E.Tool == ToolKind.Text && E.TextTool.IsEditing);
                Ribbon.Invalidate();
            }
            if ((c & (Change.Commands | Change.Image | Change.Title)) != 0) InvalidateCaption();
            if ((c & (Change.Status | Change.ImageSize | Change.Zoom | Change.Title | Change.Image)) != 0) Status.Invalidate();
            if ((c & Change.Title) != 0) UpdateTitle();
            if ((c & (Change.Image | Change.ImageSize)) != 0) InvalidateThumbnail();
        }

        void UpdateTitle()
        {
            var t = E.Doc.Title + " - Paint";
            if (Text != t) { Text = t; InvalidateCaption(); }
        }

        // ================================================================== settings
        void LoadSettings()
        {
            int x = Settings.GetInt("WinX", int.MinValue), y = Settings.GetInt("WinY", int.MinValue);
            int w = Settings.GetInt("WinW", 0), h = Settings.GetInt("WinH", 0);
            var wa = Screen.PrimaryScreen.WorkingArea;
            if (w > 200 && h > 200 && x != int.MinValue && IsOnScreen(new Rectangle(x, y, w, h))) Bounds = new Rectangle(x, y, w, h);
            else
            {
                int ww = Math.Min(wa.Width - 80, Math.Max(1000, wa.Width * 3 / 4)), hh = Math.Min(wa.Height - 60, Math.Max(700, wa.Height * 3 / 4));
                Bounds = new Rectangle(wa.Left + (wa.Width - ww) / 2, wa.Top + (wa.Height - hh) / 2, ww, hh);
            }
            if (Settings.GetBool("WinMax", false)) WindowState = FormWindowState.Maximized;
            QatBelow = Settings.GetBool("QatBelow", false);
            var qat = Settings.Get("Qat", "Save,Undo,Redo");
            for (int i = 0; i < QatOrder.Length; i++) _qatOn[i] = ("," + qat + ",").IndexOf("," + QatOrder[i] + ",", StringComparison.OrdinalIgnoreCase) >= 0;
            _showStatus = Settings.GetBool("StatusBar", true);
            Canvas.Rulers = Settings.GetBool("Rulers", false);
            Canvas.Grid = Settings.GetBool("Gridlines", false);
            DarkCanvas = Settings.GetBool("DarkCanvas", true);
            E.Units = (Units)Math.Max(0, Math.Min(2, Settings.GetInt("Units", 0)));
            foreach (var kv in Settings.WithPrefix("Size."))
                if (int.TryParse(kv.Value, out int v)) E.LoadSize(kv.Key.Substring(5), v);
            for (int i = 20; i < 30; i++)
            {
                var s = Settings.Get("Custom" + (i - 20));
                if (!string.IsNullOrEmpty(s) && uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out uint c)) { E.Palette[i] = c | 0xFF000000; E.PaletteUsed[i] = true; }
            }
            E.TextTool.LoadSettings();
            PageSettings.Load();
            if (Settings.GetBool("RibbonMin", false)) Ribbon.Minimized = true;
            _thumbWanted = Settings.GetBool("Thumbnail", false);
        }

        static bool IsOnScreen(Rectangle r)
        {
            foreach (var s in Screen.AllScreens) if (s.WorkingArea.IntersectsWith(new Rectangle(r.X + 40, r.Y + 10, Math.Max(1, r.Width - 80), 30))) return true;
            return false;
        }

        void SaveSettings()
        {
            var wp = new WINDOWPLACEMENT { length = Marshal.SizeOf(typeof(WINDOWPLACEMENT)) };
            if (Native.GetWindowPlacement(Handle, ref wp))
            {
                var r = wp.rcNormalPosition;
                Settings.SetInt("WinX", r.Left); Settings.SetInt("WinY", r.Top); Settings.SetInt("WinW", r.Width); Settings.SetInt("WinH", r.Height);
                Settings.SetBool("WinMax", wp.showCmd == Native.SW_SHOWMAXIMIZED || (WindowState == FormWindowState.Minimized && (wp.flags & 2) != 0));
            }
            Settings.SetBool("QatBelow", QatBelow);
            var q = new List<string>();
            for (int i = 0; i < QatOrder.Length; i++) if (_qatOn[i]) q.Add(QatOrder[i].ToString());
            Settings.Set("Qat", string.Join(",", q));
            Settings.SetBool("StatusBar", _showStatus);
            Settings.SetBool("Rulers", Canvas.Rulers);
            Settings.SetBool("Gridlines", Canvas.Grid);
            Settings.SetBool("DarkCanvas", DarkCanvas);
            Settings.SetBool("RibbonMin", Ribbon.Minimized);
            Settings.SetInt("Units", (int)E.Units);
            Settings.SetBool("Thumbnail", _thumb != null);
            foreach (var kv in E.AllSizes) Settings.SetInt("Size." + kv.Key, kv.Value);
            for (int i = 20; i < 30; i++) Settings.Set("Custom" + (i - 20), E.PaletteUsed[i] ? (E.Palette[i] & 0xFFFFFF).ToString("X6") : null);
            E.TextTool.SaveSettings();
            PageSettings.Save();
            Settings.Save();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _buf.Dispose();
            base.Dispose(disposing);
        }
    }
}
