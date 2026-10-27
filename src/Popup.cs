using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PaintDark
{
    /// <summary>Non-activating, shadowed, custom drawn popup window (menus, galleries, tooltips).</summary>
    public class PopupWindow : Form
    {
        protected Dpi D;
        readonly BackBuffer _buf = new BackBuffer();
        public Rectangle AnchorScreen;          // the button that opened us (click on it again toggles)
        public PopupWindow ParentPopup;
        public PopupWindow ChildPopup;
        public Action Dismissed;
        public bool IsTooltip;
        protected bool Shadow = true;

        public PopupWindow(int dpi)
        {
            D = new Dpi(dpi);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ControlBox = false;
            MinimizeBox = MaximizeBox = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Popup;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style = unchecked((int)0x80000000);            // WS_POPUP
                cp.ExStyle |= 0x80 | 0x08000000 | 0x8;            // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST
                if (Shadow) cp.ClassStyle |= 0x00020000;          // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = (IntPtr)3; return; } // MA_NOACTIVATE
            if (m.Msg == Native.WM_NCHITTEST && IsTooltip) { m.Result = (IntPtr)Native.HTTRANSPARENT; return; }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = _buf.Begin(ClientSize, e.ClipRectangle);
            try { Render(g); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            _buf.End(g, e.Graphics, e.ClipRectangle);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected virtual void Render(Graphics g)
        {
            Draw.Fill(g, ClientRectangle, Theme.Popup);
            Draw.Rect(g, ClientRectangle, Theme.PopupBorder);
        }

        /// <summary>Keyboard routed from the message filter. Return true when consumed.</summary>
        public virtual bool HandleKey(Keys k) => false;
        public virtual bool HandleChar(char c) => false;
        public virtual bool HandleWheel(int delta) => false;

        /// <summary>Place below (or above / beside) an anchor, kept inside the monitor work area.</summary>
        public void Place(Rectangle anchor, Size size, bool beside = false)
        {
            var wa = Screen.FromRectangle(anchor).WorkingArea;
            int x, y;
            if (beside)
            {
                x = anchor.Right - D.S(2); y = anchor.Top - D.S(3);
                if (x + size.Width > wa.Right) x = anchor.Left - size.Width + D.S(2);
                if (y + size.Height > wa.Bottom) y = wa.Bottom - size.Height;
            }
            else
            {
                x = anchor.Left; y = anchor.Bottom;
                if (y + size.Height > wa.Bottom && anchor.Top - size.Height >= wa.Top) y = anchor.Top - size.Height;
                if (x + size.Width > wa.Right) x = wa.Right - size.Width;
            }
            x = Math.Max(wa.Left, x); y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - size.Height));
            Bounds = new Rectangle(x, y, size.Width, size.Height);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            Dismissed?.Invoke();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _buf.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Owns the stack of open popups and implements menu-like dismissal.</summary>
    public sealed class Popups : IMessageFilter
    {
        static readonly List<PopupWindow> _stack = new List<PopupWindow>();
        static Popups _filter;
        public static Form MainWindow;
        public static bool AnyOpen => _stack.Count > 0;
        public static PopupWindow Top => _stack.Count > 0 ? _stack[_stack.Count - 1] : null;

        public static void Install()
        {
            if (_filter != null) return;
            _filter = new Popups();
            Application.AddMessageFilter(_filter);
        }

        public static void Open(PopupWindow p, PopupWindow parent = null)
        {
            if (parent == null) CloseAll();
            else { CloseChildren(parent); parent.ChildPopup = p; p.ParentPopup = parent; }
            _stack.Add(p);
            Tips.Hide();
            p.Show(MainWindow);
        }

        public static void CloseChildren(PopupWindow parent)
        {
            int i = _stack.IndexOf(parent);
            if (i < 0) return;
            for (int k = _stack.Count - 1; k > i; k--) CloseAt(k);
            parent.ChildPopup = null;
        }

        public static void Close(PopupWindow p)
        {
            int i = _stack.IndexOf(p);
            if (i < 0) return;
            for (int k = _stack.Count - 1; k >= i; k--) CloseAt(k);
        }

        static void CloseAt(int k)
        {
            var p = _stack[k];
            _stack.RemoveAt(k);
            if (p.ParentPopup != null && p.ParentPopup.ChildPopup == p) p.ParentPopup.ChildPopup = null;
            try { p.Close(); p.Dispose(); } catch { }
        }

        public static void CloseAll()
        {
            for (int k = _stack.Count - 1; k >= 0; k--) CloseAt(k);
            Tips.Hide();
        }

        static PopupWindow FindByHandle(IntPtr h)
        {
            while (h != IntPtr.Zero)
            {
                foreach (var p in _stack) if (p.IsHandleCreated && p.Handle == h) return p;
                h = Native.GetAncestor(h, 1); // GA_PARENT
                if (h == IntPtr.Zero) break;
            }
            return null;
        }

        public bool PreFilterMessage(ref Message m)
        {
            switch (m.Msg)
            {
                case Native.WM_LBUTTONDOWN: case Native.WM_RBUTTONDOWN: case Native.WM_MBUTTONDOWN:
                case Native.WM_NCLBUTTONDOWN: case Native.WM_NCRBUTTONDOWN:
                case Native.WM_LBUTTONDBLCLK: case Native.WM_RBUTTONDBLCLK:
                    {
                        Tips.Hide();
                        if (_stack.Count == 0) return false;
                        if (FindByHandle(m.HWnd) != null) return false;
                        Native.GetCursorPos(out var pt);
                        var top = _stack[0];
                        bool onAnchor = top.AnchorScreen.Contains(pt.X, pt.Y);
                        CloseAll();
                        // the dismissing click is eaten (like real menus); clicking the anchor just toggles it closed
                        return true;
                    }
                case Native.WM_KEYDOWN: case Native.WM_SYSKEYDOWN:
                    {
                        if (_stack.Count == 0) { Tips.Hide(); return false; }
                        var k = (Keys)(int)m.WParam | Control.ModifierKeys;
                        var top = Top;
                        if (top.HandleKey(k)) return true;
                        if ((k & Keys.KeyCode) == Keys.Escape) { Close(top); return true; }
                        if ((k & Keys.KeyCode) == Keys.Menu || (k & Keys.KeyCode) == Keys.F10) { CloseAll(); return true; }
                        return top is MenuPopup;   // menus swallow everything else
                    }
                case Native.WM_CHAR: case Native.WM_SYSCHAR:
                    if (_stack.Count == 0) return false;
                    if (Top.HandleChar((char)(int)m.WParam)) return true;
                    return Top is MenuPopup;
                case Native.WM_MOUSEWHEEL:
                    if (_stack.Count == 0) return false;
                    {
                        Native.GetCursorPos(out var pt);
                        for (int i = _stack.Count - 1; i >= 0; i--)
                            if (_stack[i].Bounds.Contains(pt.X, pt.Y)) { _stack[i].HandleWheel(Native.HiWord(m.WParam)); return true; }
                        return true;
                    }
            }
            return false;
        }
    }

    // ===================================================================== menus

    public sealed class MenuItem
    {
        public string Text;
        public Ico Icon;
        public bool LargeIcon;                  // 32px rows with a description line
        public string Description;
        public string Shortcut;
        public Func<bool> Enabled;
        public Func<bool> Checked;
        public bool Radio;                      // checked shown as a dot / highlighted icon
        public Action Click;
        public List<MenuItem> Sub;
        public bool Separator, Header;
        public Action<Graphics, Rectangle> CustomIcon;   // e.g. size lines, color previews
        public int CustomHeight;                // logical px

        public static MenuItem Sep() => new MenuItem { Separator = true };
        public static MenuItem Head(string t) => new MenuItem { Header = true, Text = t };
        public bool IsEnabled => Enabled == null || Enabled();
        public bool IsChecked => Checked != null && Checked();
        public bool Selectable => !Separator && !Header && IsEnabled;
        public char Mnemonic
        {
            get
            {
                if (Text == null) return '\0';
                int i = Text.IndexOf('&');
                return i >= 0 && i + 1 < Text.Length ? char.ToUpperInvariant(Text[i + 1]) : '\0';
            }
        }
    }

    public sealed class MenuPopup : PopupWindow
    {
        readonly List<MenuItem> _items;
        readonly List<Rectangle> _rects = new List<Rectangle>();
        int _hot = -1;
        readonly Timer _subTimer = new Timer { Interval = 300 };
        int _subFor = -1;
        public int MinWidth;
        bool _keyboardMode;

        public MenuPopup(int dpi, List<MenuItem> items) : base(dpi)
        {
            _items = items;
            _subTimer.Tick += (s, e) => { _subTimer.Stop(); if (_hot >= 0 && _items[_hot].Sub != null) OpenSub(_hot, false); };
        }

        int RowH(MenuItem it)
        {
            if (it.Separator) return D.S(7);
            if (it.Header) return D.S(24);
            if (it.CustomHeight > 0) return D.S(it.CustomHeight);
            if (it.LargeIcon) return D.S(it.Description != null ? 46 : 40);
            return D.S(26);
        }

        Size LayoutItems()
        {
            var f = Theme.UiFont(D);
            var fb = Theme.UiFontBold(D);
            int w = MinWidth > 0 ? MinWidth : D.S(160);
            int iconCol = _items.Exists(i => i.LargeIcon) ? D.S(46) : D.S(32);
            foreach (var it in _items)
            {
                if (it.Separator) continue;
                int tw = Draw.Measure(Clean(it.Text), it.Header ? fb : f).Width;
                if (it.Description != null) tw = Math.Max(tw, Math.Min(D.S(260), Draw.Measure(it.Description, Theme.SmallFont(D)).Width));
                int sw = string.IsNullOrEmpty(it.Shortcut) ? 0 : Draw.Measure(it.Shortcut, f).Width + D.S(24);
                int extra = it.Sub != null ? D.S(20) : D.S(10);
                w = Math.Max(w, (it.Header ? D.S(10) : iconCol) + tw + sw + extra + D.S(12));
            }
            _rects.Clear();
            int y = D.S(2);
            foreach (var it in _items)
            {
                int h = RowH(it);
                _rects.Add(new Rectangle(D.S(2), y, w - D.S(4), h));
                y += h;
            }
            return new Size(w, y + D.S(2));
        }

        static string Clean(string s) => s?.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&");

        public void ShowBelow(Rectangle anchorScreen, PopupWindow parent = null)
        {
            var sz = LayoutItems();
            AnchorScreen = parent == null ? anchorScreen : Rectangle.Empty;
            Place(anchorScreen, sz, parent != null);
            Popups.Open(this, parent);
        }

        public void ShowAtPoint(Point screen)
        {
            var sz = LayoutItems();
            var wa = Screen.FromPoint(screen).WorkingArea;
            int x = screen.X, y = screen.Y;
            if (x + sz.Width > wa.Right) x = Math.Max(wa.Left, screen.X - sz.Width);
            if (y + sz.Height > wa.Bottom) y = Math.Max(wa.Top, screen.Y - sz.Height);
            Bounds = new Rectangle(x, y, sz.Width, sz.Height);
            Popups.Open(this);
        }

        protected override void Render(Graphics g)
        {
            Draw.Fill(g, ClientRectangle, Theme.Popup);
            Draw.Rect(g, ClientRectangle, Theme.PopupBorder);
            var f = Theme.UiFont(D);
            var fb = Theme.UiFontBold(D);
            bool anyLarge = _items.Exists(i => i.LargeIcon);
            int iconCol = anyLarge ? D.S(46) : D.S(32);
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i]; var r = _rects[i];
                if (it.Separator) { Draw.HLine(g, r.Left + iconCol, r.Right - D.S(4), r.Top + r.Height / 2, Theme.MenuSep); continue; }
                if (it.Header)
                {
                    Draw.Fill(g, r, Theme.PopupHeader);
                    Draw.Text(g, Clean(it.Text), fb, new Rectangle(r.X + D.S(8), r.Y, r.Width - D.S(10), r.Height), Theme.Text);
                    continue;
                }
                bool en = it.IsEnabled, chk = it.IsChecked;
                if (i == _hot && en) { Draw.Fill(g, r, Theme.PopupHover); Draw.Rect(g, r, Theme.HoverBorder); }
                else if (i == _hot) Draw.Rect(g, r, Theme.MenuSep);
                int isz = it.LargeIcon ? D.S(32) : D.S(16);
                var ir = new Rectangle(r.X + (iconCol - isz) / 2, r.Y + (r.Height - isz) / 2, isz, isz);
                if (chk)
                {
                    var cr = Rectangle.Inflate(ir, D.S(3), D.S(3));
                    Draw.Fill(g, cr, Theme.Checked); Draw.Rect(g, cr, Theme.CheckedBorder);
                }
                if (it.CustomIcon != null) it.CustomIcon(g, new Rectangle(r.X + D.S(8), r.Y + D.S(3), r.Width - D.S(16), r.Height - D.S(6)));
                else if (it.Icon != Ico.None) Icons.DrawIcon(g, it.Icon, ir.X, ir.Y, isz, en);
                else if (chk)
                {
                    if (it.Radio) g.FillEllipse(Theme.Brush(Theme.Text), ir.X + isz / 2 - D.S(3), ir.Y + isz / 2 - D.S(3), D.S(6), D.S(6));
                    else Draw.CheckMark(g, Rectangle.Inflate(ir, -D.S(1), -D.S(1)), Theme.Text, D);
                }
                if (it.CustomIcon == null)
                {
                    var tc = en ? Theme.Text : Theme.TextDisabled;
                    var tr = new Rectangle(r.X + iconCol + D.S(4), r.Y, r.Width - iconCol - D.S(8), r.Height);
                    if (it.Description != null)
                    {
                        Draw.Text(g, Clean(it.Text), fb, new Rectangle(tr.X, r.Y + D.S(5), tr.Width, D.S(18)), tc);
                        Draw.Text(g, it.Description, Theme.SmallFont(D), new Rectangle(tr.X, r.Y + D.S(23), tr.Width, D.S(18)), Theme.TextDim, Draw.TextLeft | TextFormatFlags.EndEllipsis);
                    }
                    else
                    {
                        TextRenderer.DrawText(g, it.Text ?? "", f, tr, tc, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | (_keyboardMode ? 0 : TextFormatFlags.HidePrefix));
                        if (!string.IsNullOrEmpty(it.Shortcut)) Draw.Text(g, it.Shortcut, f, new Rectangle(tr.X, tr.Y, tr.Width - D.S(it.Sub != null ? 14 : 4), tr.Height), en ? Theme.TextDim : Theme.TextDisabled, Draw.TextRight);
                    }
                    if (it.Sub != null) Draw.RightArrow(g, r.Right - D.S(10), r.Y + r.Height / 2, tc, D);
                }
            }
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < _rects.Count; i++) if (_rects[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitTest(e.Location);
            if (h >= 0 && (_items[h].Separator || _items[h].Header)) h = -1;
            if (h != _hot)
            {
                _hot = h; _keyboardMode = false; Invalidate();
                _subTimer.Stop();
                if (h >= 0 && _items[h].Sub != null && _items[h].IsEnabled) { _subTimer.Start(); }
                else if (ChildPopup != null && h >= 0) { Popups.CloseChildren(this); _subFor = -1; }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (ChildPopup == null) { _hot = -1; Invalidate(); }
            _subTimer.Stop();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int h = HitTest(e.Location);
            if (h >= 0) Activate(h);
        }

        void Activate(int i)
        {
            var it = _items[i];
            if (!it.Selectable) return;
            if (it.Sub != null) { OpenSub(i, true); return; }
            Popups.CloseAll();
            it.Click?.Invoke();
        }

        void OpenSub(int i, bool selectFirst)
        {
            if (_subFor == i && ChildPopup != null) return;
            _subFor = i;
            var sub = new MenuPopup(D.Value, _items[i].Sub);
            var r = _rects[i];
            sub.ShowBelow(RectangleToScreen(r), this);
            sub.Dismissed += () => { if (_subFor == i) _subFor = -1; };
            if (selectFirst) sub.SelectFirst();
        }

        public void SelectFirst() { _keyboardMode = true; MoveHot(1, -1); }

        void MoveHot(int dir, int from)
        {
            int n = _items.Count;
            int i = from;
            for (int k = 0; k < n; k++)
            {
                i = (i + dir + n) % n;
                if (_items[i].Selectable) { _hot = i; Invalidate(); return; }
            }
        }

        public override bool HandleKey(Keys k)
        {
            _keyboardMode = true;
            switch (k & Keys.KeyCode)
            {
                case Keys.Down: MoveHot(1, _hot < 0 ? -1 : _hot); return true;
                case Keys.Up: MoveHot(-1, _hot < 0 ? 0 : _hot); return true;
                case Keys.Right: if (_hot >= 0 && _items[_hot].Sub != null) { OpenSub(_hot, true); return true; } return true;
                case Keys.Left: if (ParentPopup != null) { Popups.Close(this); return true; } return true;
                case Keys.Enter: case Keys.Space: if (_hot >= 0) Activate(_hot); return true;
                case Keys.Escape: Popups.Close(this); return true;
                case Keys.Home: MoveHot(1, -1); return true;
                case Keys.End: MoveHot(-1, 0); return true;
            }
            return false;
        }

        public override bool HandleChar(char c)
        {
            c = char.ToUpperInvariant(c);
            var matches = new List<int>();
            for (int i = 0; i < _items.Count; i++) if (_items[i].Selectable && _items[i].Mnemonic == c) matches.Add(i);
            if (matches.Count == 1) { Activate(matches[0]); return true; }
            if (matches.Count > 1) { int next = matches.Find(x => x > _hot); _hot = next > 0 || matches[0] == next ? next : matches[0]; Invalidate(); return true; }
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _subTimer.Dispose();
            base.Dispose(disposing);
        }
    }

    // ===================================================================== tooltips

    public sealed class TipWindow : PopupWindow
    {
        string _title, _text;
        public TipWindow(int dpi) : base(dpi) { IsTooltip = true; }

        public void Set(string title, string text, Point screenPt)
        {
            _title = title; _text = text;
            var fb = Theme.UiFontBold(D); var f = Theme.UiFont(D);
            int maxW = D.S(300);
            var ts = Draw.Measure(title ?? "", fb);
            Size ds = Size.Empty;
            if (!string.IsNullOrEmpty(text))
                ds = TextRenderer.MeasureText(text, f, new Size(maxW, 1000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int w = Math.Max(ts.Width, ds.Width) + D.S(16);
            int h = ts.Height + (ds.Height > 0 ? ds.Height + D.S(6) : 0) + D.S(12);
            var wa = Screen.FromPoint(screenPt).WorkingArea;
            int x = Math.Min(screenPt.X, wa.Right - w), y = screenPt.Y;
            if (y + h > wa.Bottom) y = screenPt.Y - h - D.S(24);
            Bounds = new Rectangle(Math.Max(wa.Left, x), Math.Max(wa.Top, y), w, h);
            Invalidate();
        }

        protected override void Render(Graphics g)
        {
            Draw.Fill(g, ClientRectangle, Theme.Tooltip);
            Draw.Rect(g, ClientRectangle, Theme.TooltipBorder);
            var fb = Theme.UiFontBold(D); var f = Theme.UiFont(D);
            var ts = Draw.Measure(_title ?? "", fb);
            Draw.Text(g, _title, string.IsNullOrEmpty(_text) ? f : fb, new Rectangle(D.S(8), D.S(6), Width - D.S(16), ts.Height), Theme.Text);
            if (!string.IsNullOrEmpty(_text))
                TextRenderer.DrawText(g, _text, f, new Rectangle(D.S(8), D.S(6) + ts.Height + D.S(6), Width - D.S(16), Height), Theme.TextDim, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }

    public static class Tips
    {
        static TipWindow _tip;
        static readonly Timer _timer = new Timer { Interval = 600 };
        static object _pendingOwner;
        static string _t, _d;
        static Point _pt;
        static Dpi _dpi;
        static object _shownFor;

        static Tips()
        {
            _timer.Tick += (s, e) =>
            {
                _timer.Stop();
                if (_pendingOwner == null || Popups.AnyOpen) return;
                if (_tip == null || _tip.IsDisposed) _tip = new TipWindow(_dpi.Value);
                _tip.Set(_t, _d, _pt);
                if (!_tip.Visible) { _tip.Show(Popups.MainWindow); }
                _shownFor = _pendingOwner;
            };
        }

        /// <summary>Request a tip for 'owner' (any object identity). Re-requests for the same owner are ignored.</summary>
        public static void Request(object owner, string title, string text, Point screenPt, int dpi)
        {
            if (owner == _pendingOwner) return;
            Hide();
            if (string.IsNullOrEmpty(title)) return;
            _pendingOwner = owner; _t = title; _d = text; _pt = screenPt; _dpi = new Dpi(dpi);
            _timer.Start();
        }

        public static void Hide()
        {
            _timer.Stop();
            _pendingOwner = null; _shownFor = null;
            if (_tip != null && !_tip.IsDisposed && _tip.Visible) _tip.Hide();
        }
    }
}
