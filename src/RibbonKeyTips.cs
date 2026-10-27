using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PaintDark
{
    public sealed partial class Ribbon
    {
        enum KeyTipMode { Off, Top, Tab }
        KeyTipMode _keyTipMode = KeyTipMode.Off;
        string _typed = "";

        sealed class KeyTip
        {
            public string Tip;
            public Point Pos;          // ribbon coords (badge center-top); for caption tips Pos.Y < 0
            public Action Act;
            public bool InCaption;
        }
        readonly List<KeyTip> _tips = new List<KeyTip>();

        public bool KeyTipsActive => _keyTipMode != KeyTipMode.Off;

        public void BeginKeyTips()
        {
            if (_minimized && !_expanded) { }
            _keyTipMode = KeyTipMode.Top;
            _typed = "";
            BuildTopTips();
            Tips.Hide();
            Invalidate();
            F.InvalidateCaption();
        }

        public void EndKeyTips()
        {
            if (_keyTipMode == KeyTipMode.Off) return;
            _keyTipMode = KeyTipMode.Off;
            _tips.Clear();
            Invalidate();
            F.InvalidateCaption();
        }

        void BuildTopTips()
        {
            _tips.Clear();
            _tips.Add(new KeyTip { Tip = "F", Pos = new Point(_fileRect.X + _fileRect.Width / 2, TabH - D.S(6)), Act = () => { EndKeyTips(); F.ShowAppMenu(RectangleToScreen(_fileRect)); } });
            foreach (var t in VisibleTabs)
            {
                var tab = t;
                _tips.Add(new KeyTip { Tip = t.Contextual ? "JT" : t.KeyTip, Pos = new Point(t.TabRect.X + t.TabRect.Width / 2, TabH - D.S(6)), Act = () => EnterTab(tab) });
            }
            int i = 1;
            foreach (var q in F.QatKeyTipTargets())
            {
                var cmd = q.Value;
                _tips.Add(new KeyTip { Tip = (i++).ToString(), Pos = q.Key, InCaption = true, Act = () => { EndKeyTips(); F.Execute(cmd); } });
            }
        }

        void EnterTab(RTab t)
        {
            _active = t;
            if (_minimized && !_expanded) Expand();
            Relayout();
            _keyTipMode = KeyTipMode.Tab;
            _typed = "";
            _tips.Clear();
            foreach (var g in t.Groups)
            {
                if (g.EffectiveLevel >= 2)
                {
                    var cb = g.CollapseButton;
                    AddItemTip(cb, cb.KeyTip ?? "Z", new Point(cb.Bounds.X + cb.Bounds.Width / 2, cb.Bounds.Bottom - D.S(4)));
                    continue;
                }
                foreach (var it in g.Items)
                {
                    switch (it.Kind)
                    {
                        case RKind.Palette: continue;
                        case RKind.Large: case RKind.LargeDrop: case RKind.LargeSplit: case RKind.ColorSlot:
                            AddItemTip(it, it.KeyTip, new Point(it.Bounds.X + it.Bounds.Width / 2, it.Bounds.Bottom - D.S(4)));
                            break;
                        case RKind.Tool:
                            AddItemTip(it, it.KeyTip, new Point(it.Bounds.X + it.Bounds.Width / 2, it.Bounds.Bottom - D.S(2)));
                            break;
                        case RKind.Gallery:
                            AddItemTip(it, it.KeyTip, new Point(it.Bounds.Right - D.S(8), it.Bounds.Bottom - D.S(2)));
                            break;
                        case RKind.FontRows:
                            {
                                var item = it;
                                _tips.Add(new KeyTip { Tip = "FF", Pos = new Point(FontFamilyRect(it).X + D.S(14), FontFamilyRect(it).Bottom - D.S(4)), Act = () => { EndKeyTips(); BeginEdit(Cmd.FontFamily, FontFamilyRect(item)); } });
                                _tips.Add(new KeyTip { Tip = "FS", Pos = new Point(FontSizeRect(it).X + D.S(14), FontSizeRect(it).Bottom - D.S(4)), Act = () => { EndKeyTips(); BeginEdit(Cmd.FontSize, FontSizeRect(item)); } });
                                Cmd[] cmds = { Cmd.TextBold, Cmd.TextItalic, Cmd.TextUnderline, Cmd.TextStrike };
                                for (int i = 0; i < 4; i++)
                                {
                                    var c = cmds[i];
                                    var r = StyleButton(it, i);
                                    _tips.Add(new KeyTip { Tip = (i + 1).ToString(), Pos = new Point(r.X + r.Width / 2, r.Bottom - D.S(2)), Act = () => { EndKeyTips(); F.Execute(c); } });
                                }
                                break;
                            }
                        default:
                            AddItemTip(it, it.KeyTip, new Point(it.Bounds.X + D.S(12), it.Bounds.Bottom - D.S(4)));
                            break;
                    }
                }
            }
            Invalidate();
        }

        void AddItemTip(RItem it, string tip, Point pos)
        {
            if (string.IsNullOrEmpty(tip)) return;
            var item = it;
            _tips.Add(new KeyTip
            {
                Tip = tip,
                Pos = pos,
                Act = () =>
                {
                    EndKeyTips();
                    if (!IsItemEnabled(item)) { Native.MessageBeep(0); return; }
                    var screen = RectangleToScreen(item.Bounds);
                    switch (item.Kind)
                    {
                        case RKind.Collapsed: ShowCollapsedGroup(item); break;
                        case RKind.LargeDrop: case RKind.SmallDrop: F.ShowDropdown(item.DropCmd != Cmd.None ? item.DropCmd : item.Cmd, item, screen); break;
                        case RKind.LargeSplit: case RKind.SmallSplit: F.ShowDropdown(item.DropCmd, item, screen); break;
                        case RKind.Gallery: F.ShowDropdown(Cmd.ShapesMore, item, screen); break;
                        default: ClickItem(item, 0, false); break;
                    }
                }
            });
        }

        /// <summary>Returns true when the key was consumed by key-tip navigation.</summary>
        public bool HandleKeyTipKey(Keys k)
        {
            if (_keyTipMode == KeyTipMode.Off) return false;
            var code = k & Keys.KeyCode;
            if (code == Keys.Escape)
            {
                if (_typed.Length > 0) { _typed = ""; Invalidate(); F.InvalidateCaption(); }
                else if (_keyTipMode == KeyTipMode.Tab) { _keyTipMode = KeyTipMode.Top; BuildTopTips(); if (_expanded) Collapse(); Invalidate(); F.InvalidateCaption(); }
                else EndKeyTips();
                return true;
            }
            if (code == Keys.Menu || code == Keys.F10) { EndKeyTips(); return true; }
            char c = '\0';
            if (code >= Keys.A && code <= Keys.Z) c = (char)('A' + (code - Keys.A));
            else if (code >= Keys.D0 && code <= Keys.D9) c = (char)('0' + (code - Keys.D0));
            else if (code >= Keys.NumPad0 && code <= Keys.NumPad9) c = (char)('0' + (code - Keys.NumPad0));
            else if (code == Keys.OemQuestion) c = '?';
            if (c == '\0') { EndKeyTips(); return false; }
            string typed = _typed + c;
            var exact = _tips.Find(t => t.Tip == typed);
            bool prefix = _tips.Exists(t => t.Tip.StartsWith(typed, StringComparison.Ordinal) && t.Tip != typed);
            if (exact != null && !prefix) { _typed = ""; exact.Act(); return true; }
            if (exact != null || prefix) { _typed = typed; Invalidate(); F.InvalidateCaption(); return true; }
            Native.MessageBeep(0);
            return true;
        }

        void PaintKeyTips(Graphics g)
        {
            var f = Theme.SmallFont(D);
            foreach (var t in _tips)
            {
                if (t.InCaption) continue;
                if (_typed.Length > 0 && !t.Tip.StartsWith(_typed, StringComparison.Ordinal)) continue;
                PaintBadge(g, t.Tip, t.Pos, f, D);
            }
        }

        public static void PaintBadge(Graphics g, string tip, Point pos, Font f, Dpi d)
        {
            var sz = Draw.Measure(tip, f);
            var r = new Rectangle(pos.X - (sz.Width + d.S(8)) / 2, pos.Y - d.S(8), sz.Width + d.S(8), d.S(16));
            Draw.Fill(g, r, Theme.C(0x3C3C3C));
            Draw.Rect(g, r, Theme.C(0x9A9A9A));
            Draw.Text(g, tip, f, r, Color.White, Draw.TextCenter);
        }

        /// <summary>QAT badges are drawn by the caption.</summary>
        public void PaintCaptionKeyTips(Graphics g, int dpi)
        {
            if (_keyTipMode != KeyTipMode.Top) return;
            var f = Theme.SmallFont(dpi);
            foreach (var t in _tips)
                if (t.InCaption && (_typed.Length == 0 || t.Tip.StartsWith(_typed, StringComparison.Ordinal)))
                    PaintBadge(g, t.Tip, t.Pos, f, new Dpi(dpi));
        }
    }
}
