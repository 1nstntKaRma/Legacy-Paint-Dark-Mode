using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PaintDark
{
    /// <summary>Paint's File menu: big commands on the left, Recent pictures (or the hovered submenu) on the right.</summary>
    public sealed class AppMenu : PopupWindow
    {
        sealed class Entry
        {
            public Cmd Cmd;
            public bool Sep;
            public Cmd[] Sub;
            public string SubTitle;
            public Rectangle Rect;
        }

        readonly MainForm F;
        readonly List<Entry> _left = new List<Entry>();
        List<Settings.RecentItem> _recent;
        readonly List<Rectangle> _recentRects = new List<Rectangle>();
        readonly List<Rectangle> _pinRects = new List<Rectangle>();
        readonly List<Rectangle> _subRects = new List<Rectangle>();
        int _hotLeft = -1, _hotRight = -1, _hotPin = -1;
        Entry _sub;                    // submenu shown in the right pane (null = recent pictures)
        readonly Timer _subTimer = new Timer { Interval = 220 };
        int _pendingSub = -1;
        bool _keyRight;                // keyboard focus is in the right pane

        public AppMenu(MainForm f, int dpi) : base(dpi)
        {
            F = f;
            _left.Add(new Entry { Cmd = Cmd.New });
            _left.Add(new Entry { Cmd = Cmd.Open });
            _left.Add(new Entry { Cmd = Cmd.Save });
            _left.Add(new Entry { Cmd = Cmd.SaveAs, Sub = new[] { Cmd.SaveAsPng, Cmd.SaveAsJpeg, Cmd.SaveAsBmp, Cmd.SaveAsGif, Cmd.SaveAsOther }, SubTitle = "Save as" });
            _left.Add(new Entry { Sep = true });
            _left.Add(new Entry { Cmd = Cmd.Print, Sub = new[] { Cmd.Print, Cmd.PageSetup, Cmd.PrintPreview }, SubTitle = "Print" });
            _left.Add(new Entry { Cmd = Cmd.FromScanner });
            _left.Add(new Entry { Cmd = Cmd.SendEmail });
            _left.Add(new Entry { Cmd = Cmd.DesktopFill, Sub = new[] { Cmd.DesktopFill, Cmd.DesktopTile, Cmd.DesktopCenter }, SubTitle = "Set as desktop background" });
            _left.Add(new Entry { Sep = true });
            _left.Add(new Entry { Cmd = Cmd.Properties });
            _left.Add(new Entry { Sep = true });
            _left.Add(new Entry { Cmd = Cmd.About });
            _left.Add(new Entry { Cmd = Cmd.Exit });
            _recent = Settings.Recent;
            _subTimer.Tick += (s, e) => { _subTimer.Stop(); if (_pendingSub >= 0) { _sub = _left[_pendingSub].Sub != null ? _left[_pendingSub] : null; _hotRight = -1; Invalidate(); } };
        }

        int _moves; Point _lastMove;
        protected override void WndProc(ref Message m) { if (m.Msg == Native.WM_MOUSEMOVE) { _moves++; _lastMove = new Point(Native.LoWord(m.LParam), Native.HiWord(m.LParam)); } base.WndProc(ref m); }
        public string DebugInfo => "moves=" + _moves + " last=" + _lastMove + " r5=" + (_left.Count > 5 ? _left[5].Rect.ToString() : "") + " hotLeft=" + _hotLeft + " sub=" + (_sub?.SubTitle ?? "-") + " size=" + Size + " loc=" + Location;
        int LeftW => D.S(214);
        int RightW => D.S(330);
        int ItemH => D.S(44);

        string LeftLabel(Entry e) => e.Cmd == Cmd.DesktopFill ? "Set as desktop bac&kground" : Commands.Info(e.Cmd).Label;

        public void ShowAt(Rectangle fileButton)
        {
            int y = D.S(6);
            foreach (var e in _left)
            {
                int h = e.Sep ? D.S(9) : ItemH;
                e.Rect = new Rectangle(D.S(4), y, LeftW - D.S(8), h);
                y += h;
            }
            int height = Math.Max(y + D.S(6), D.S(420));
            AnchorScreen = fileButton;
            Place(new Rectangle(fileButton.X, fileButton.Y, fileButton.Width, fileButton.Height), new Size(LeftW + RightW, height));
            Popups.Open(this);
        }

        protected override void Render(Graphics g)
        {
            Draw.Fill(g, ClientRectangle, Theme.Popup);
            var right = new Rectangle(LeftW, 0, RightW, Height);
            Draw.Fill(g, right, Theme.C(0x252525));
            Draw.VLine(g, LeftW, 0, Height, Theme.PopupBorder);
            var f = Theme.UiFont(D);
            for (int i = 0; i < _left.Count; i++)
            {
                var e = _left[i];
                if (e.Sep) { Draw.HLine(g, e.Rect.X + D.S(46), e.Rect.Right - D.S(6), e.Rect.Y + e.Rect.Height / 2, Theme.MenuSep); continue; }
                bool hot = i == _hotLeft || (_sub == e && _sub != null);
                bool en = F.IsEnabled(e.Cmd);
                if (hot) { Draw.Fill(g, e.Rect, i == _hotLeft ? Theme.PopupHover : Theme.C(0x353535)); Draw.Rect(g, e.Rect, Theme.HoverBorder); }
                var info = Commands.Info(e.Cmd);
                Ico ic = e.Cmd == Cmd.DesktopFill ? Ico.Desktop : info.LargeIcon;
                Icons.DrawIcon(g, ic, e.Rect.X + D.S(6), e.Rect.Y + (e.Rect.Height - D.S(32)) / 2, D.S(32), en);
                TextRenderer.DrawText(g, LeftLabel(e), f, new Rectangle(e.Rect.X + D.S(46), e.Rect.Y, e.Rect.Width - D.S(60), e.Rect.Height), en ? Theme.Text : Theme.TextDisabled,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                if (e.Sub != null) Draw.RightArrow(g, e.Rect.Right - D.S(10), e.Rect.Y + e.Rect.Height / 2, Theme.TextDim, D);
            }
            PaintRight(g, right);
            Draw.Rect(g, ClientRectangle, Theme.PopupBorder);
        }

        void PaintRight(Graphics g, Rectangle r)
        {
            var f = Theme.UiFont(D); var fb = Theme.UiFontBold(D);
            _recentRects.Clear(); _pinRects.Clear(); _subRects.Clear();
            var head = new Rectangle(r.X + D.S(10), r.Y + D.S(8), r.Width - D.S(20), D.S(26));
            if (_sub != null)
            {
                Draw.Text(g, _sub.SubTitle, fb, head, Theme.Text);
                Draw.HLine(g, head.X, head.Right, head.Bottom, Theme.MenuSep);
                int y = head.Bottom + D.S(6);
                for (int i = 0; i < _sub.Sub.Length; i++)
                {
                    var c = _sub.Sub[i];
                    var info = Commands.Info(c);
                    var ir = new Rectangle(r.X + D.S(6), y, r.Width - D.S(12), D.S(56));
                    _subRects.Add(ir);
                    if (i == _hotRight) { Draw.Fill(g, ir, Theme.PopupHover); Draw.Rect(g, ir, Theme.HoverBorder); }
                    Icons.DrawIcon(g, info.LargeIcon, ir.X + D.S(6), ir.Y + D.S(10), D.S(32));
                    TextRenderer.DrawText(g, info.Label, fb, new Rectangle(ir.X + D.S(48), ir.Y + D.S(6), ir.Width - D.S(54), D.S(18)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.SingleLine);
                    TextRenderer.DrawText(g, info.Tip ?? "", Theme.SmallFont(D), new Rectangle(ir.X + D.S(48), ir.Y + D.S(24), ir.Width - D.S(54), D.S(30)), Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    y += D.S(58);
                }
                return;
            }
            Draw.Text(g, "Recent pictures", fb, head, Theme.Text);
            Draw.HLine(g, head.X, head.Right, head.Bottom, Theme.MenuSep);
            int yy = head.Bottom + D.S(4);
            if (_recent.Count == 0)
            {
                Draw.Text(g, "Pictures you open will appear here.", f, new Rectangle(head.X, yy + D.S(6), head.Width, D.S(20)), Theme.TextDim);
                return;
            }
            for (int i = 0; i < _recent.Count; i++)
            {
                var it = _recent[i];
                var ir = new Rectangle(r.X + D.S(6), yy, r.Width - D.S(12), D.S(26));
                if (ir.Bottom > Height - D.S(4)) break;
                _recentRects.Add(ir);
                var pin = new Rectangle(ir.Right - D.S(24), ir.Y + (ir.Height - D.S(20)) / 2, D.S(20), D.S(20));
                _pinRects.Add(pin);
                if (i == _hotRight) { Draw.Fill(g, ir, Theme.PopupHover); Draw.Rect(g, ir, Theme.HoverBorder); }
                string label = (i < 9 ? "&" + (i + 1) + "  " : "    ") + Path.GetFileName(it.Path).Replace("&", "&&");
                TextRenderer.DrawText(g, label, f, new Rectangle(ir.X + D.S(6), ir.Y, ir.Width - D.S(36), ir.Height), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                if (i == _hotPin) Draw.ButtonFace(g, pin, true, false, false);
                if (it.Pinned || i == _hotRight) Icons.DrawIcon(g, it.Pinned ? Ico.Pinned : Ico.Pin, pin.X + (pin.Width - D.S(16)) / 2, pin.Y + (pin.Height - D.S(16)) / 2, D.S(16));
                yy += D.S(26);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _keyRight = false;
            int hl = -1;
            for (int i = 0; i < _left.Count; i++) if (!_left[i].Sep && _left[i].Rect.Contains(e.Location)) hl = i;
            int hr = -1, hp = -1;
            var rects = _sub != null ? _subRects : _recentRects;
            for (int i = 0; i < rects.Count; i++) if (rects[i].Contains(e.Location)) hr = i;
            if (_sub == null) for (int i = 0; i < _pinRects.Count; i++) if (_pinRects[i].Contains(e.Location)) hp = i;
            if (hl != _hotLeft)
            {
                _hotLeft = hl;
                if (hl >= 0) { _pendingSub = hl; _subTimer.Stop(); _subTimer.Start(); }
            }
            if (hr != _hotRight || hp != _hotPin)
            {
                _hotRight = hr; _hotPin = hp;
                if (_sub == null && hr >= 0 && hr < _recent.Count) Tips.Request("recent" + hr, Path.GetFileName(_recent[hr].Path), _recent[hr].Path, PointToScreen(new Point(rects[hr].X, rects[hr].Bottom + D.S(4))), D.Value);
                else Tips.Hide();
            }
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hotLeft = -1; _hotRight = -1; _hotPin = -1; _subTimer.Stop(); Tips.Hide(); Invalidate(); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            for (int i = 0; i < _left.Count; i++)
                if (!_left[i].Sep && _left[i].Rect.Contains(e.Location)) { Run(_left[i].Cmd); return; }
            if (_sub != null)
            {
                for (int i = 0; i < _subRects.Count; i++) if (_subRects[i].Contains(e.Location)) { Run(_sub.Sub[i]); return; }
                return;
            }
            for (int i = 0; i < _pinRects.Count; i++)
                if (_pinRects[i].Contains(e.Location)) { Settings.TogglePin(_recent[i].Path); _recent = Settings.Recent; Invalidate(); return; }
            for (int i = 0; i < _recentRects.Count; i++)
                if (_recentRects[i].Contains(e.Location)) { OpenRecent(i); return; }
        }

        void Run(Cmd c)
        {
            if (!F.IsEnabled(c)) return;
            Popups.CloseAll();
            F.BeginInvoke(new Action(() => F.Execute(c)));
        }

        void OpenRecent(int i)
        {
            var p = _recent[i].Path;
            Popups.CloseAll();
            F.BeginInvoke(new Action(() => F.OpenFile(p, false)));
        }

        public override bool HandleKey(Keys k)
        {
            switch (k & Keys.KeyCode)
            {
                case Keys.Down: case Keys.Up:
                    {
                        int dir = (k & Keys.KeyCode) == Keys.Down ? 1 : -1;
                        if (_keyRight)
                        {
                            int n = _sub != null ? _sub.Sub.Length : Math.Min(_recent.Count, _recentRects.Count);
                            if (n > 0) _hotRight = (_hotRight + dir + n) % n;
                        }
                        else
                        {
                            int i = _hotLeft;
                            for (int t = 0; t < _left.Count; t++) { i = (i + dir + _left.Count) % _left.Count; if (!_left[i].Sep) break; }
                            _hotLeft = i;
                            _sub = _left[i].Sub != null ? _left[i] : null;
                        }
                        Invalidate();
                        return true;
                    }
                case Keys.Right:
                    if (!_keyRight && (_sub != null || _recent.Count > 0)) { _keyRight = true; _hotRight = 0; Invalidate(); }
                    return true;
                case Keys.Left:
                    if (_keyRight) { _keyRight = false; _hotRight = -1; Invalidate(); }
                    return true;
                case Keys.Enter: case Keys.Space:
                    if (_keyRight && _hotRight >= 0) { if (_sub != null) Run(_sub.Sub[_hotRight]); else OpenRecent(_hotRight); }
                    else if (_hotLeft >= 0) Run(_left[_hotLeft].Cmd);
                    return true;
            }
            return false;
        }

        public override bool HandleChar(char c)
        {
            c = char.ToUpperInvariant(c);
            if (_keyRight && _sub == null && c >= '1' && c <= '9') { int i = c - '1'; if (i < _recent.Count) OpenRecent(i); return true; }
            if (_keyRight && _sub != null)
            {
                foreach (var sc in _sub.Sub) { var l = Commands.Info(sc).Label; int a = l.IndexOf('&'); if (a >= 0 && char.ToUpperInvariant(l[a + 1]) == c) { Run(sc); return true; } }
            }
            foreach (var e in _left)
            {
                if (e.Sep) continue;
                var l = LeftLabel(e); int a = l.IndexOf('&');
                if (a >= 0 && a + 1 < l.Length && char.ToUpperInvariant(l[a + 1]) == c)
                {
                    if (e.Sub != null) { _sub = e; _hotLeft = _left.IndexOf(e); _keyRight = true; _hotRight = 0; Invalidate(); }
                    else Run(e.Cmd);
                    return true;
                }
            }
            if (c >= '1' && c <= '9') { int i = c - '1'; if (_sub == null && i < _recent.Count) OpenRecent(i); }
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _subTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
