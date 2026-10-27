using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PaintDark
{
    public sealed partial class MainForm
    {
        object _dropFor;
        AppMenu _appMenu;

        public bool IsDropdownOpenFor(RItem it) => it != null && _dropFor == it && Popups.AnyOpen;
        public bool AppMenuOpen => _appMenu != null && !_appMenu.IsDisposed && _appMenu.Visible;

        MenuItem M(Cmd c, Ico icon = Ico.None, string shortcut = null, bool radio = false, Action click = null)
        {
            var info = Commands.Info(c);
            return new MenuItem
            {
                Text = info.Label, Icon = icon == Ico.None ? info.Icon : icon, Shortcut = shortcut, Radio = radio,
                Enabled = () => IsEnabled(c),
                Checked = () => IsChecked(c),
                Click = click ?? (() => Execute(c)),
            };
        }

        void Track(PopupWindow p, RItem it)
        {
            _dropFor = it;
            p.Dismissed += () => { if (_dropFor == it) _dropFor = null; Ribbon.Invalidate(); };
            Ribbon.Invalidate();
        }

        public void ShowDropdown(Cmd c, RItem it, Rectangle anchor)
        {
            Tips.Hide();
            switch (c)
            {
                case Cmd.PasteMenu:
                    ShowMenu(it, anchor, new List<MenuItem> { M(Cmd.Paste, Ico.PasteSmall, "Ctrl+V"), M(Cmd.PasteFrom, Ico.PasteFrom) });
                    break;
                case Cmd.SelectMenu:
                    ShowMenu(it, anchor, new List<MenuItem>
                    {
                        MenuItem.Head("Selection shapes"),
                        M(Cmd.SelectRect, Ico.SelRect, null, true), M(Cmd.SelectFree, Ico.SelFree, null, true),
                        MenuItem.Head("Selection options"),
                        M(Cmd.SelectAll, Ico.SelAll, "Ctrl+A"), M(Cmd.InvertSelection, Ico.SelInvert), M(Cmd.DeleteSelection, Ico.SelDelete, "Delete"),
                        M(Cmd.TransparentSelection, Ico.SelTransparent),
                    });
                    break;
                case Cmd.RotateMenu:
                    ShowMenu(it, anchor, RotateItems());
                    break;
                case Cmd.OutlineMenu:
                case Cmd.FillMenu:
                    {
                        bool outline = c == Cmd.OutlineMenu;
                        var items = new List<MenuItem>();
                        for (int i = 0; i < 7; i++)
                        {
                            var t = (Texture)i;
                            items.Add(new MenuItem
                            {
                                Text = outline ? Commands.OutlineNames[i] : Commands.FillNames[i],
                                Icon = Icons.ForTexture(t), Radio = true,
                                Checked = () => outline ? E.Outline == t : E.ShapeFill == t,
                                Click = () => { if (outline) E.SetOutline(t); else E.SetShapeFill(t); },
                            });
                        }
                        ShowMenu(it, anchor, items);
                        break;
                    }
                case Cmd.SizeMenu:
                    {
                        var choices = E.SizeChoices;
                        if (choices == null) return;
                        var items = new List<MenuItem>();
                        bool square = E.Tool == ToolKind.Eraser;
                        foreach (var s in choices)
                        {
                            int sz = s;
                            items.Add(new MenuItem
                            {
                                Text = sz + "px", CustomHeight = Math.Max(26, Math.Min(46, sz + 14)),
                                Checked = () => E.ToolSize == sz,
                                Click = () => E.ToolSize = sz,
                                CustomIcon = (g, r) =>
                                {
                                    float px = sz * D.Value / 96f;
                                    int lineW = r.Width - D.S(40);
                                    float h = Math.Max(1, Math.Min(r.Height - 2, px));
                                    var lr = new RectangleF(r.X + D.S(4), r.Y + (r.Height - h) / 2f, lineW, h);
                                    if (E.ToolSize == sz) { Draw.Fill(g, Rectangle.Inflate(r, D.S(4), D.S(2)), Theme.Checked); Draw.Rect(g, Rectangle.Inflate(r, D.S(4), D.S(2)), Theme.CheckedBorder); }
                                    g.SmoothingMode = SmoothingMode.None;
                                    g.FillRectangle(Theme.Brush(Theme.Text), lr);
                                    Draw.Text(g, sz + "px", Theme.UiFont(D), new Rectangle(r.Right - D.S(34), r.Y, D.S(34), r.Height), Theme.TextDim, Draw.TextRight);
                                },
                            });
                        }
                        var m = new MenuPopup(D.Value, items) { MinWidth = D.S(150) };
                        Track(m, it);
                        m.ShowBelow(anchor);
                        break;
                    }
                case Cmd.BrushesMenu:
                    {
                        var g = new GalleryPopup(D.Value, 3, D.S(52), 9, i => Icons.ForBrush((BrushKind)i), i => Commands.BrushNames[i], i => Commands.BrushTips[i],
                            i => E.Tool == ToolKind.Brush && (int)E.BrushType == i, i => E.SetBrush((BrushKind)i), D.S(32));
                        Track(g, it);
                        g.ShowBelow(anchor);
                        break;
                    }
                case Cmd.ShapesMore:
                    {
                        var g = new GalleryPopup(D.Value, 7, D.S(26), 23, i => Icons.ForShape((ShapeKind)i), i => Commands.ShapeNames[i], i => Commands.ShapeTip((ShapeKind)i),
                            i => E.Tool == ToolKind.Shape && (int)E.ShapeType == i, i => E.SetShape((ShapeKind)i), D.S(16));
                        Track(g, it);
                        g.ShowBelow(anchor);
                        break;
                    }
                case Cmd.FontFamily:
                    {
                        var p = new FontListPopup(D.Value, E.TextTool.CurrentStyle.Face, f => E.TextTool.SetFace(f));
                        Track(p, it);
                        p.ShowBelow(anchor);
                        break;
                    }
                case Cmd.FontSize:
                    {
                        float[] sizes = { 8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72 };
                        var items = new List<MenuItem>();
                        foreach (var s in sizes)
                        {
                            float sz = s;
                            items.Add(new MenuItem { Text = sz.ToString(), Radio = true, Checked = () => Math.Abs(E.TextTool.CurrentStyle.Size - sz) < 0.01, Click = () => E.TextTool.SetSize(sz), CustomHeight = 22 });
                        }
                        var m = new MenuPopup(D.Value, items) { MinWidth = anchor.Width };
                        Track(m, it);
                        m.ShowBelow(anchor);
                        break;
                    }
            }
        }

        List<MenuItem> RotateItems() => new List<MenuItem>
        {
            M(Cmd.RotateRight, Ico.RotRight), M(Cmd.RotateLeft, Ico.RotLeft), M(Cmd.Rotate180, Ico.Rot180),
            M(Cmd.FlipVertical, Ico.FlipV), M(Cmd.FlipHorizontal, Ico.FlipH),
        };

        void ShowMenu(RItem it, Rectangle anchor, List<MenuItem> items)
        {
            var m = new MenuPopup(D.Value, items) { MinWidth = Math.Max(D.S(180), anchor.Width) };
            Track(m, it);
            m.ShowBelow(anchor);
        }

        public void ShowCanvasMenu(Point screen)
        {
            Tips.Hide();
            List<MenuItem> items;
            if (E.TextEditing)
            {
                items = new List<MenuItem>
                {
                    new MenuItem { Text = "&Undo", Shortcut = "Ctrl+Z", Click = () => E.Undo() },
                    MenuItem.Sep(),
                    M(Cmd.Cut, Ico.Cut, "Ctrl+X"), M(Cmd.Copy, Ico.Copy, "Ctrl+C"), M(Cmd.Paste, Ico.PasteSmall, "Ctrl+V"),
                    new MenuItem { Text = "&Delete", Icon = Ico.SelDelete, Shortcut = "Del", Enabled = () => E.TextTool.HasTextSelection, Click = () => E.TextTool.DeleteText() },
                    MenuItem.Sep(),
                    new MenuItem { Text = "Select &All", Shortcut = "Ctrl+A", Click = () => E.TextTool.SelectAllText() },
                };
            }
            else
            {
                items = new List<MenuItem>
                {
                    M(Cmd.Cut, Ico.Cut, "Ctrl+X"), M(Cmd.Copy, Ico.Copy, "Ctrl+C"), M(Cmd.Paste, Ico.PasteSmall, "Ctrl+V"),
                    M(Cmd.Crop, Ico.Crop, "Ctrl+Shift+X"),
                    M(Cmd.SelectAll, Ico.SelAll, "Ctrl+A"), M(Cmd.InvertSelection, Ico.SelInvert), M(Cmd.DeleteSelection, Ico.SelDelete, "Delete"),
                    new MenuItem { Text = "R&otate", Icon = Ico.Rotate, Sub = RotateItems() },
                    M(Cmd.Resize, Ico.Resize, "Ctrl+W"),
                    M(Cmd.InvertColor, Ico.InvertColor, "Ctrl+Shift+I"),
                };
            }
            var m = new MenuPopup(D.Value, items) { MinWidth = D.S(200) };
            m.ShowAtPoint(screen);
        }

        void ShowQatMenu(Rectangle anchor)
        {
            var items = new List<MenuItem> { MenuItem.Head("Customize Quick Access Toolbar") };
            for (int i = 0; i < QatOrder.Length; i++)
            {
                int k = i;
                items.Add(new MenuItem { Text = QatNames[i], Checked = () => _qatOn[k], Click = () => { _qatOn[k] = !_qatOn[k]; InvalidateCaption(); DoLayout(); } });
            }
            items.Add(MenuItem.Sep());
            items.Add(new MenuItem { Text = QatBelow ? "Show above the Ribbon" : "Show below the Ribbon", Click = () => Execute(Cmd.QatBelow) });
            items.Add(new MenuItem { Text = "Minimize the Ribbon", Checked = () => Ribbon.Minimized, Click = () => Execute(Cmd.ToggleRibbon) });
            var m = new MenuPopup(D.Value, items) { MinWidth = D.S(230) };
            m.ShowBelow(anchor);
        }

        public void ShowAppMenu(Rectangle anchor)
        {
            Tips.Hide();
            if (AppMenuOpen) { Popups.CloseAll(); return; }
            E.CommitPendingExceptSelection();
            _appMenu = new AppMenu(this, D.Value);
            _appMenu.Dismissed += () => { _appMenu = null; Ribbon.Invalidate(); };
            _appMenu.ShowAt(anchor);
            Ribbon.Invalidate();
        }
    }

    /// <summary>Grid of icons (Brushes / Shapes galleries).</summary>
    public sealed class GalleryPopup : PopupWindow
    {
        readonly int _cols, _cell, _count, _icon;
        readonly Func<int, Ico> _ico;
        readonly Func<int, string> _name, _tip;
        readonly Func<int, bool> _checked;
        readonly Action<int> _click;
        int _hot = -1;

        public GalleryPopup(int dpi, int cols, int cell, int count, Func<int, Ico> ico, Func<int, string> name, Func<int, string> tip, Func<int, bool> chk, Action<int> click, int iconPx) : base(dpi)
        {
            _cols = cols; _cell = cell; _count = count; _ico = ico; _name = name; _tip = tip; _checked = chk; _click = click; _icon = iconPx;
        }

        int Pad => D.S(4);

        public void ShowBelow(Rectangle anchor)
        {
            int rows = (_count + _cols - 1) / _cols;
            var sz = new Size(_cols * _cell + Pad * 2, rows * _cell + Pad * 2);
            AnchorScreen = anchor;
            Place(anchor, sz);
            Popups.Open(this);
        }

        Rectangle Cell(int i) => new Rectangle(Pad + (i % _cols) * _cell, Pad + (i / _cols) * _cell, _cell, _cell);

        protected override void Render(Graphics g)
        {
            Draw.Fill(g, ClientRectangle, Theme.Popup);
            Draw.Rect(g, ClientRectangle, Theme.PopupBorder);
            for (int i = 0; i < _count; i++)
            {
                var r = Rectangle.Inflate(Cell(i), -1, -1);
                Draw.ButtonFace(g, r, i == _hot, false, _checked(i));
                Icons.DrawIcon(g, _ico(i), r.X + (r.Width - _icon) / 2, r.Y + (r.Height - _icon) / 2, _icon);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = -1;
            for (int i = 0; i < _count; i++) if (Cell(i).Contains(e.Location)) h = i;
            if (h != _hot)
            {
                _hot = h; Invalidate();
                if (h >= 0) Tips.Request(this.GetHashCode() + ":" + h, _name(h), _tip(h), PointToScreen(new Point(Cell(h).X, Cell(h).Bottom + D.S(4))), D.Value);
                else Tips.Hide();
            }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = -1; Invalidate(); Tips.Hide(); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            for (int i = 0; i < _count; i++)
                if (Cell(i).Contains(e.Location)) { Popups.CloseAll(); _click(i); return; }
        }

        public override bool HandleKey(Keys k)
        {
            int n = _hot < 0 ? 0 : _hot;
            switch (k & Keys.KeyCode)
            {
                case Keys.Left: _hot = Math.Max(0, n - 1); break;
                case Keys.Right: _hot = Math.Min(_count - 1, n + 1); break;
                case Keys.Up: _hot = Math.Max(0, n - _cols); break;
                case Keys.Down: _hot = Math.Min(_count - 1, n + _cols); break;
                case Keys.Enter: case Keys.Space: if (_hot >= 0) { int h = _hot; Popups.CloseAll(); _click(h); } return true;
                default: return false;
            }
            Invalidate();
            return true;
        }
    }

    /// <summary>Scrollable font list where every family is previewed in its own typeface.</summary>
    public sealed class FontListPopup : PopupWindow
    {
        static List<string> _fonts;
        readonly Action<string> _pick;
        readonly string _current;
        int _top, _hot = -1;
        readonly Dictionary<string, Font> _cache = new Dictionary<string, Font>();
        string _typed = "";
        DateTime _typedAt;

        public FontListPopup(int dpi, string current, Action<string> pick) : base(dpi)
        {
            _pick = pick; _current = current;
            if (_fonts == null) _fonts = EnumFonts();
            int i = _fonts.FindIndex(f => string.Equals(f, current, StringComparison.OrdinalIgnoreCase));
            _hot = i;
            _top = Math.Max(0, i - 4);
        }

        public static List<string> EnumFonts()
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            try { foreach (var ff in FontFamily.Families) if (!ff.Name.StartsWith("@")) set.Add(ff.Name); } catch { }
            return new List<string>(set);
        }

        int RowH => D.S(28);
        int VisibleRows => 14;

        public void ShowBelow(Rectangle anchor)
        {
            AnchorScreen = anchor;
            Place(anchor, new Size(Math.Max(anchor.Width, D.S(260)), RowH * Math.Min(VisibleRows, _fonts.Count) + D.S(4)));
            Popups.Open(this);
        }

        Font PreviewFont(string name)
        {
            if (_cache.TryGetValue(name, out var f)) return f;
            try { f = new Font(name, 15f * D.Value / 96f, FontStyle.Regular, GraphicsUnit.Pixel); }
            catch { f = Theme.UiFont(D); }
            if (_cache.Count > 200) { foreach (var v in _cache.Values) if (v != Theme.UiFont(D)) v.Dispose(); _cache.Clear(); }
            _cache[name] = f;
            return f;
        }

        protected override void Render(Graphics g)
        {
            Draw.Fill(g, ClientRectangle, Theme.Popup);
            int n = Math.Min(VisibleRows, _fonts.Count - _top);
            for (int i = 0; i < n; i++)
            {
                int idx = _top + i;
                var r = new Rectangle(D.S(2), D.S(2) + i * RowH, Width - D.S(12), RowH);
                bool cur = string.Equals(_fonts[idx], _current, StringComparison.OrdinalIgnoreCase);
                if (idx == _hot) { Draw.Fill(g, r, Theme.PopupHover); Draw.Rect(g, r, Theme.HoverBorder); }
                else if (cur) Draw.Fill(g, r, Theme.Checked);
                Draw.Text(g, _fonts[idx], PreviewFont(_fonts[idx]), new Rectangle(r.X + D.S(8), r.Y, r.Width - D.S(10), r.Height), Theme.Text, Draw.TextLeft | TextFormatFlags.EndEllipsis);
            }
            // scroll indicator
            if (_fonts.Count > VisibleRows)
            {
                int trackH = Height - D.S(4);
                int th = Math.Max(D.S(20), trackH * VisibleRows / _fonts.Count);
                int ty = D.S(2) + (trackH - th) * _top / Math.Max(1, _fonts.Count - VisibleRows);
                Draw.Fill(g, new Rectangle(Width - D.S(8), ty, D.S(5), th), Theme.ScrollThumb);
            }
            Draw.Rect(g, ClientRectangle, Theme.PopupBorder);
        }

        int Hit(Point p)
        {
            int i = (p.Y - D.S(2)) / RowH;
            if (i < 0 || i >= VisibleRows) return -1;
            int idx = _top + i;
            return idx < _fonts.Count ? idx : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = Hit(e.Location); if (h != _hot) { _hot = h; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); int h = Hit(e.Location); if (h >= 0) { Popups.CloseAll(); _pick(_fonts[h]); } }

        public override bool HandleWheel(int delta)
        {
            _top = Math.Max(0, Math.Min(_fonts.Count - VisibleRows, _top - Math.Sign(delta) * 3));
            Invalidate();
            return true;
        }

        void EnsureVisible()
        {
            if (_hot < _top) _top = _hot;
            if (_hot >= _top + VisibleRows) _top = _hot - VisibleRows + 1;
            _top = Math.Max(0, Math.Min(Math.Max(0, _fonts.Count - VisibleRows), _top));
        }

        public override bool HandleKey(Keys k)
        {
            switch (k & Keys.KeyCode)
            {
                case Keys.Down: _hot = Math.Min(_fonts.Count - 1, _hot + 1); break;
                case Keys.Up: _hot = Math.Max(0, _hot - 1); break;
                case Keys.PageDown: _hot = Math.Min(_fonts.Count - 1, _hot + VisibleRows); break;
                case Keys.PageUp: _hot = Math.Max(0, _hot - VisibleRows); break;
                case Keys.Enter: if (_hot >= 0) { Popups.CloseAll(); _pick(_fonts[_hot]); } return true;
                default: return false;
            }
            EnsureVisible(); Invalidate();
            return true;
        }

        public override bool HandleChar(char c)
        {
            if (char.IsControl(c)) return false;
            if ((DateTime.Now - _typedAt).TotalMilliseconds > 900) _typed = "";
            _typed += c; _typedAt = DateTime.Now;
            int i = _fonts.FindIndex(f => f.StartsWith(_typed, StringComparison.OrdinalIgnoreCase));
            if (i >= 0) { _hot = i; EnsureVisible(); Invalidate(); }
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) foreach (var f in _cache.Values) if (f != Theme.UiFont(D)) f.Dispose();
            base.Dispose(disposing);
        }
    }
}
