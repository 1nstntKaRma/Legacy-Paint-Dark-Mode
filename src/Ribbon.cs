using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace PaintDark
{
    public enum RKind { Large, LargeSplit, LargeDrop, Small, SmallSplit, SmallDrop, Tool, Gallery, ColorSlot, Palette, Check, FontRows, Collapsed }

    public sealed class RItem
    {
        public RKind Kind;
        public Cmd Cmd, DropCmd;
        public string Label;
        public Ico Icon;
        public Func<Ico> IconFn;
        public string KeyTip;
        public Rectangle Bounds, DropRect;
        public bool IconOnly;
        public RGroup Group;           // for Collapsed buttons
        public int Slot;               // color slot
        public Ico CurrentIcon => IconFn != null ? IconFn() : Icon;
    }

    public enum ColKind { Large, Stack, ToolGrid, Gallery, Palette, Checks, FontRows }

    public sealed class RColumn
    {
        public ColKind Kind;
        public List<RItem> Items = new List<RItem>();
        public RColumn(ColKind k, params RItem[] items) { Kind = k; Items.AddRange(items); }
    }

    public sealed class RGroup
    {
        public string Name;
        public Ico CollapsedIcon;
        public List<RColumn> Columns = new List<RColumn>();
        public Rectangle Bounds;
        public int Level;              // 0 full, 1 reduced, 2 collapsed
        public bool InPopup;           // laid out at full size inside a collapsed-group popup
        public int EffectiveLevel => InPopup ? 0 : Level;
        public int MaxLevel = 2;
        public RItem CollapseButton;
        public string KeyTip;
        public IEnumerable<RItem> Items => Columns.SelectMany(c => c.Items);
    }

    public sealed class RTab
    {
        public string Name;
        public string KeyTip;
        public bool Contextual;
        public List<RGroup> Groups = new List<RGroup>();
        public List<RGroup> ReduceOrder = new List<RGroup>();
        public Rectangle TabRect;
    }

    public sealed partial class Ribbon : DarkControl
    {
        readonly MainForm F;
        readonly Editor E;
        readonly RTab _home, _view, _text, _preview;
        RTab _active;
        bool _minimized, _expanded, _textVisible, _previewMode;
        Rectangle _fileRect, _minRect, _helpRect;

        // interaction
        RItem _hot, _pressed;
        int _hotPart = -1, _pressedPart = -1;
        int _hotTab = -2;           // -1 file button, -3 minimize, -4 help
        int _galleryFirstRow;
        TextBox _edit;
        Cmd _editFor;

        public event Action LayoutChanged;

        public Ribbon(MainForm f, Editor e)
        {
            F = f; E = e;
            _home = BuildHome();
            _view = BuildView();
            _text = BuildText();
            _preview = BuildPreview();
            _active = _home;
        }

        int TabH => D.S(25);
        int BodyH => D.S(115);
        int BodyTop => TabH;
        int ContentTop => BodyTop + D.S(7);
        int LargeH => D.S(76);
        int RowY(int i) => ContentTop + i * D.S(26);
        int RowH => D.S(22);

        public int LayoutHeight => TabH + (_minimized ? 0 : BodyH);
        public int FullHeight => TabH + BodyH;
        public bool Minimized
        {
            get => _minimized;
            set { _minimized = value; _expanded = false; Height = LayoutHeight; Relayout(); LayoutChanged?.Invoke(); Invalidate(); }
        }
        public bool PreviewMode
        {
            get => _previewMode;
            set { _previewMode = value; _active = value ? _preview : _home; Relayout(); Invalidate(); F.InvalidateCaption(); }
        }
        public bool TextTabVisible => _textVisible;
        public bool Expanded => _expanded;

        public void ShowTextTab(bool show)
        {
            if (show == _textVisible) return;
            _textVisible = show;
            if (show) _active = _text;
            else if (_active == _text) _active = _home;
            CommitEdit(false);
            Relayout();
            Invalidate();
            F.InvalidateCaption();
        }

        /// <summary>X range (ribbon coordinates) of the contextual "Text Tools" header.</summary>
        public Rectangle ContextHeaderRect => _textVisible && !_previewMode ? _text.TabRect : Rectangle.Empty;

        List<RTab> VisibleTabs
        {
            get
            {
                if (_previewMode) return new List<RTab> { _preview };
                var l = new List<RTab> { _home, _view };
                if (_textVisible) l.Add(_text);
                return l;
            }
        }

        // ================================================================== building
        RItem I(RKind k, Cmd c, Cmd drop = Cmd.None, string label = null, Func<Ico> iconFn = null)
        {
            var info = Commands.Info(c);
            bool large = k == RKind.Large || k == RKind.LargeSplit || k == RKind.LargeDrop;
            return new RItem
            {
                Kind = k, Cmd = c, DropCmd = drop, Label = label ?? StripAmp(info.Label), Icon = large ? info.LargeIcon : info.Icon,
                IconFn = iconFn, KeyTip = info.KeyTip,
            };
        }

        static string StripAmp(string s) => s?.Replace("&", "");

        RGroup G(string name, Ico icon, string key, params RColumn[] cols)
        {
            var g = new RGroup { Name = name, CollapsedIcon = icon, KeyTip = key };
            g.Columns.AddRange(cols);
            g.CollapseButton = new RItem { Kind = RKind.Collapsed, Label = name, Icon = icon, Group = g, KeyTip = key };
            return g;
        }

        RGroup ColorsGroup()
        {
            return G("Colors", Ico.Colors, "ZC",
                new RColumn(ColKind.Large, new RItem { Kind = RKind.ColorSlot, Cmd = Cmd.Color1, Label = "Color\n1", Slot = 1, KeyTip = "1" }),
                new RColumn(ColKind.Large, new RItem { Kind = RKind.ColorSlot, Cmd = Cmd.Color2, Label = "Color\n2", Slot = 2, KeyTip = "2" }),
                new RColumn(ColKind.Palette, new RItem { Kind = RKind.Palette }),
                new RColumn(ColKind.Large, I(RKind.Large, Cmd.EditColors)));
        }

        RGroup ClipboardGroup()
        {
            return G("Clipboard", Ico.Paste, "ZB",
                new RColumn(ColKind.Large, I(RKind.LargeSplit, Cmd.Paste, Cmd.PasteMenu, "Paste")),
                new RColumn(ColKind.Stack, I(RKind.Small, Cmd.Cut), I(RKind.Small, Cmd.Copy)));
        }

        RTab BuildHome()
        {
            var t = new RTab { Name = "Home", KeyTip = "H" };
            var clip = ClipboardGroup();
            var image = G("Image", Ico.Select, "ZI",
                new RColumn(ColKind.Large, I(RKind.LargeSplit, Cmd.SelectTool, Cmd.SelectMenu, "Select")),
                new RColumn(ColKind.Stack, I(RKind.Small, Cmd.Crop), I(RKind.Small, Cmd.Resize), I(RKind.SmallDrop, Cmd.RotateMenu)));
            var tools = G("Tools", Ico.Tools, "ZT",
                new RColumn(ColKind.ToolGrid, I(RKind.Tool, Cmd.Pencil), I(RKind.Tool, Cmd.FillTool), I(RKind.Tool, Cmd.TextTool),
                    I(RKind.Tool, Cmd.Eraser), I(RKind.Tool, Cmd.ColorPicker), I(RKind.Tool, Cmd.Magnifier)));
            tools.MaxLevel = 2;
            var brushes = G("", Ico.BrushBrush, "B",
                new RColumn(ColKind.Large, I(RKind.LargeSplit, Cmd.Brushes, Cmd.BrushesMenu, "Brushes", () => Icons.ForBrush(E.BrushType))));
            brushes.MaxLevel = 0;
            var shapes = G("Shapes", Ico.Shapes, "SH",
                new RColumn(ColKind.Gallery, new RItem { Kind = RKind.Gallery, Cmd = Cmd.Shapes, KeyTip = "SH" }),
                new RColumn(ColKind.Stack, I(RKind.SmallDrop, Cmd.OutlineMenu), I(RKind.SmallDrop, Cmd.FillMenu)));
            var size = G("", Ico.Size, "SZ", new RColumn(ColKind.Large, I(RKind.LargeDrop, Cmd.SizeMenu, Cmd.SizeMenu, "Size")));
            size.MaxLevel = 0;
            var colors = ColorsGroup();
            t.Groups.AddRange(new[] { clip, image, tools, brushes, shapes, size, colors });
            t.ReduceOrder.AddRange(new[] { shapes, clip, image, shapes, colors, image, clip, tools });
            return t;
        }

        RTab BuildView()
        {
            var t = new RTab { Name = "View", KeyTip = "V" };
            var zoom = G("Zoom", Ico.ZoomIn, "ZZ",
                new RColumn(ColKind.Large, I(RKind.Large, Cmd.ZoomIn)), new RColumn(ColKind.Large, I(RKind.Large, Cmd.ZoomOut)), new RColumn(ColKind.Large, I(RKind.Large, Cmd.Zoom100)));
            var show = G("Show or hide", Ico.Rulers, "ZS",
                new RColumn(ColKind.Checks, I(RKind.Check, Cmd.Rulers), I(RKind.Check, Cmd.Gridlines), I(RKind.Check, Cmd.StatusBar)));
            var disp = G("Display", Ico.FullScreen, "ZD",
                new RColumn(ColKind.Large, I(RKind.Large, Cmd.FullScreen)), new RColumn(ColKind.Large, I(RKind.Large, Cmd.Thumbnail)));
            var dark = G("Canvas", Ico.DarkCanvas, "ZK", new RColumn(ColKind.Large, I(RKind.Large, Cmd.DarkCanvas)));
            t.Groups.AddRange(new[] { zoom, show, disp, dark });
            t.ReduceOrder.AddRange(new[] { dark, disp, show, zoom });
            return t;
        }

        RTab BuildText()
        {
            var t = new RTab { Name = "Text", KeyTip = "X", Contextual = true };
            var clip = ClipboardGroup();
            var font = G("Font", Ico.Text, "ZF", new RColumn(ColKind.FontRows, new RItem { Kind = RKind.FontRows, Cmd = Cmd.FontFamily, KeyTip = "FF" }));
            var bg = G("Background", Ico.Opaque, "ZG", new RColumn(ColKind.Stack, I(RKind.Small, Cmd.TextOpaque), I(RKind.Small, Cmd.TextTransparent)));
            var colors = ColorsGroup();
            t.Groups.AddRange(new[] { clip, font, bg, colors });
            t.ReduceOrder.AddRange(new[] { clip, bg, colors, clip, bg });
            return t;
        }

        RTab BuildPreview()
        {
            var t = new RTab { Name = "Print preview", KeyTip = "P" };
            t.Groups.Add(G("Print", Ico.Print, "ZP", new RColumn(ColKind.Large, I(RKind.Large, Cmd.PreviewPrint))));
            t.Groups.Add(G("Page Setup", Ico.PageSetup, "ZS", new RColumn(ColKind.Large, I(RKind.Large, Cmd.PreviewPageSetup))));
            t.Groups.Add(G("Zoom", Ico.ZoomIn, "ZZ", new RColumn(ColKind.Large, I(RKind.Large, Cmd.PreviewZoomIn)), new RColumn(ColKind.Large, I(RKind.Large, Cmd.PreviewZoomOut)),
                new RColumn(ColKind.Large, I(RKind.Large, Cmd.PreviewOnePage)), new RColumn(ColKind.Large, I(RKind.Large, Cmd.PreviewTwoPages))));
            t.Groups.Add(G("Preview", Ico.NextPage, "ZV", new RColumn(ColKind.Large, I(RKind.Large, Cmd.PreviewNext)), new RColumn(ColKind.Large, I(RKind.Large, Cmd.PreviewPrev))));
            t.Groups.Add(G("Close", Ico.ClosePreview, "ZC", new RColumn(ColKind.Large, I(RKind.Large, Cmd.PreviewClose))));
            foreach (var g in t.Groups) g.MaxLevel = 0;
            return t;
        }

        // ================================================================== layout
        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }
        protected override void OnDpiApplied() { Height = _expanded ? FullHeight : LayoutHeight; Relayout(); }

        public void Relayout()
        {
            var f = Theme.UiFont(D);
            // tabs row
            int fw = Math.Max(D.S(56), Draw.Measure("File", f).Width + D.S(30));
            _fileRect = new Rectangle(0, 0, fw, TabH);
            int x = fw + D.S(2);
            foreach (var t in VisibleTabs)
            {
                int w = Draw.Measure(t.Name, f).Width + D.S(28);
                t.TabRect = new Rectangle(x, D.S(2), w, TabH - D.S(2));
                x += w + D.S(1);
            }
            int bs = D.S(24);
            _helpRect = new Rectangle(Width - bs - D.S(4), (TabH - bs) / 2 + D.S(1), bs, bs);
            _minRect = new Rectangle(_helpRect.Left - bs - D.S(2), _helpRect.Top, bs, bs);
            if (_previewMode) _minRect = _helpRect = Rectangle.Empty;

            // body: reduce groups until the active tab fits
            var tab = _active;
            foreach (var g in tab.Groups) g.Level = 0;
            int avail = Width - D.S(4);
            int total = LayoutTab(tab);
            int step = 0;
            while (total > avail && step < tab.ReduceOrder.Count)
            {
                var g = tab.ReduceOrder[step++];
                if (g.Level < g.MaxLevel) g.Level++;
                total = LayoutTab(tab);
            }
            // collapse anything left (left to right) if still too wide
            if (total > avail)
                foreach (var g in tab.Groups.AsEnumerable().Reverse())
                {
                    if (total <= avail) break;
                    if (g.MaxLevel >= 2 && g.Level < 2) { g.Level = 2; total = LayoutTab(tab); }
                }
            Invalidate();
        }

        int LayoutTab(RTab tab)
        {
            int x = D.S(4);
            foreach (var g in tab.Groups)
            {
                int w = LayoutGroup(g, x);
                g.Bounds = new Rectangle(x, BodyTop, w, BodyH);
                x += w + D.S(1);
            }
            return x;
        }

        int LargeWidth(RItem it)
        {
            var f = Theme.UiFont(D);
            int tw = 0;
            foreach (var line in (it.Label ?? "").Split('\n')) tw = Math.Max(tw, Draw.Measure(line, f).Width);
            return Math.Max(D.S(42), tw + D.S(12));
        }

        int SmallWidth(RItem it)
        {
            var f = Theme.UiFont(D);
            int w = D.S(4) + D.S(16) + D.S(4);
            if (!it.IconOnly && !string.IsNullOrEmpty(it.Label)) w += Draw.Measure(it.Label, f).Width + D.S(4);
            if (it.Kind == RKind.SmallDrop || it.Kind == RKind.SmallSplit) w += D.S(12);
            return w;
        }

        int LayoutGroup(RGroup g, int x0)
        {
            var f = Theme.UiFont(D);
            if (g.EffectiveLevel >= 2)
            {
                var cb = g.CollapseButton;
                int w = Math.Max(D.S(48), Draw.Measure(g.Name, f).Width + D.S(14));
                cb.Bounds = new Rectangle(x0 + D.S(3), ContentTop, w, LargeH);
                return w + D.S(6);
            }
            int x = x0 + D.S(4);
            foreach (var col in g.Columns)
            {
                int cw = 0;
                switch (col.Kind)
                {
                    case ColKind.Large:
                        foreach (var it in col.Items)
                        {
                            int w = LargeWidth(it);
                            it.Bounds = new Rectangle(x + cw, ContentTop, w, LargeH);
                            if (it.Kind == RKind.LargeSplit) it.DropRect = Rectangle.FromLTRB(it.Bounds.Left, it.Bounds.Top + D.S(44), it.Bounds.Right, it.Bounds.Bottom);
                            cw += w + D.S(2);
                        }
                        break;
                    case ColKind.Stack:
                    case ColKind.Checks:
                        {
                            int i = 0;
                            foreach (var it in col.Items) it.IconOnly = g.EffectiveLevel >= 1 && col.Kind == ColKind.Stack;
                            foreach (var it in col.Items) cw = Math.Max(cw, col.Kind == ColKind.Checks ? Draw.Measure(it.Label, f).Width + D.S(30) : SmallWidth(it));
                            foreach (var it in col.Items)
                            {
                                it.Bounds = new Rectangle(x, RowY(i++), cw, RowH);
                                if (it.Kind == RKind.SmallSplit || it.Kind == RKind.SmallDrop) it.DropRect = Rectangle.FromLTRB(it.Bounds.Right - D.S(12), it.Bounds.Top, it.Bounds.Right, it.Bounds.Bottom);
                            }
                            cw += D.S(2);
                            break;
                        }
                    case ColKind.ToolGrid:
                        {
                            int bs = D.S(24), pitch = D.S(28);
                            int cy = ContentTop + LargeH / 2;
                            for (int i = 0; i < col.Items.Count; i++)
                            {
                                int r = i / 3, c = i % 3;
                                col.Items[i].Bounds = new Rectangle(x + D.S(2) + c * pitch, cy - D.S(18) + r * D.S(36) - bs / 2, bs, bs);
                            }
                            cw = pitch * 3 + D.S(2);
                            break;
                        }
                    case ColKind.Gallery:
                        {
                            var it = col.Items[0];
                            int cols = GalleryCols(g);
                            int cell = D.S(24);
                            it.Bounds = new Rectangle(x, ContentTop, cols * cell + D.S(2) + D.S(16), cell * 3 + D.S(2));
                            cw = it.Bounds.Width + D.S(6);
                            break;
                        }
                    case ColKind.Palette:
                        {
                            var it = col.Items[0];
                            int pitch = D.S(27);
                            it.Bounds = new Rectangle(x, ContentTop, pitch * 10, pitch * 3);
                            cw = it.Bounds.Width + D.S(2);
                            break;
                        }
                    case ColKind.FontRows:
                        {
                            var it = col.Items[0];
                            it.Bounds = new Rectangle(x, RowY(0), D.S(214), RowH * 2 + D.S(10));
                            cw = it.Bounds.Width + D.S(4);
                            break;
                        }
                }
                x += cw;
            }
            int width = x - x0 + D.S(2);
            int labelW = Draw.Measure(g.Name, f).Width + D.S(16);
            if (width < labelW)
            {
                // center the content under a wider label
                int shift = (labelW - width) / 2;
                foreach (var it in g.Items) { it.Bounds.Offset(shift, 0); if (!it.DropRect.IsEmpty) it.DropRect.Offset(shift, 0); }
                width = labelW;
            }
            return width;
        }

        int GalleryCols(RGroup g) => g.EffectiveLevel >= 1 ? 4 : 7;

        // ================================================================== painting
        protected override void Render(Graphics g, Rectangle clip)
        {
            var f = Theme.UiFont(D);
            Draw.Fill(g, new Rectangle(0, 0, Width, TabH), Theme.TabRow);
            bool showBody = !_minimized || _expanded;
            if (showBody)
            {
                var body = new Rectangle(0, BodyTop, Width, BodyH);
                Draw.Fill(g, body, Theme.Ribbon);
                Draw.HLine(g, 0, Width, BodyTop, Theme.RibbonBorder);
                Draw.HLine(g, 0, Width, BodyTop + BodyH - 1, Theme.RibbonBorder);
            }
            else Draw.HLine(g, 0, Width, TabH - 1, Theme.RibbonBorder);

            // File button
            var fileBg = _hotTab == -1 ? Theme.AccentHover : Theme.Accent;
            if (F.AppMenuOpen) fileBg = Theme.AccentDown;
            Draw.Fill(g, _fileRect, fileBg);
            Draw.Text(g, "File", f, _fileRect, Theme.AccentText, Draw.TextCenter);

            // tabs
            foreach (var t in VisibleTabs)
            {
                var r = t.TabRect;
                bool sel = t == _active && showBody;
                if (t.Contextual)
                {
                    using (var b = new SolidBrush(Color.FromArgb(40, Theme.ContextTabText))) g.FillRectangle(b, r);
                    Draw.HLine(g, r.Left, r.Right, r.Top, Theme.ContextTabText);
                }
                if (sel)
                {
                    var rr = new Rectangle(r.X, r.Y, r.Width, TabH - r.Y + 1);
                    Draw.Fill(g, rr, Theme.Ribbon);
                    Draw.VLine(g, rr.Left, rr.Top, rr.Bottom, Theme.RibbonBorder);
                    Draw.VLine(g, rr.Right - 1, rr.Top, rr.Bottom, Theme.RibbonBorder);
                    Draw.HLine(g, rr.Left, rr.Right, rr.Top, t.Contextual ? Theme.ContextTabText : Theme.RibbonBorder);
                }
                else if (_hotTab >= 0 && VisibleTabs.IndexOf(t) == _hotTab)
                    Draw.Fill(g, Rectangle.Inflate(r, 0, -D.S(1)), Theme.Hover);
                Draw.Text(g, t.Name, f, r, t.Contextual ? Theme.ContextTabText : (sel ? Theme.Text : Theme.TextDim), Draw.TextCenter);
            }

            // minimize + help
            if (!_minRect.IsEmpty)
            {
                Draw.ButtonFace(g, _minRect, _hotTab == -3, false, false);
                Draw.Chevron(g, _minRect.X + _minRect.Width / 2, _minRect.Y + _minRect.Height / 2, Theme.TextDim, D, !_minimized);
                Draw.ButtonFace(g, _helpRect, _hotTab == -4, false, false);
                Icons.DrawIcon(g, Ico.Help, _helpRect.X + (_helpRect.Width - D.S(16)) / 2, _helpRect.Y + (_helpRect.Height - D.S(16)) / 2, D.S(16));
            }

            if (showBody) PaintBody(g, clip);
            if (_keyTipMode != KeyTipMode.Off) PaintKeyTips(g);
        }

        void PaintBody(Graphics g, Rectangle clip)
        {
            var f = Theme.UiFont(D);
            foreach (var grp in _active.Groups)
            {
                var gb = grp.Bounds;
                if (!gb.IntersectsWith(clip) && !Rectangle.Inflate(gb, 2, 0).IntersectsWith(clip)) continue;
                if (grp.Level >= 2) PaintItem(g, grp.CollapseButton, f);
                else foreach (var it in grp.Items) PaintItem(g, it, f);
                if (!string.IsNullOrEmpty(grp.Name) && grp.Level < 2)
                    Draw.Text(g, grp.Name, f, new Rectangle(gb.X, BodyTop + D.S(93), gb.Width, D.S(18)), Theme.GroupLabel, Draw.TextCenter);
                Draw.VLine(g, gb.Right, BodyTop + D.S(4), BodyTop + BodyH - D.S(5), Theme.GroupSep);
            }
        }

        bool IsItemEnabled(RItem it) => it.Kind == RKind.Collapsed || it.Kind == RKind.Palette || it.Kind == RKind.Gallery && F.IsEnabled(Cmd.Shapes) || F.IsEnabled(it.Cmd);
        bool Checked(RItem it) => it.Kind != RKind.Collapsed && F.IsChecked(it.Cmd);

        void PaintItem(Graphics g, RItem it, Font f)
        {
            bool en = IsItemEnabled(it), chk = Checked(it);
            bool hot = it == _hot && en, pressed = it == _pressed && _pressedPart == _hotPart && hot;
            var b = it.Bounds;
            switch (it.Kind)
            {
                case RKind.Large:
                case RKind.LargeDrop:
                case RKind.Collapsed:
                case RKind.ColorSlot:
                    {
                        bool open = F.IsDropdownOpenFor(it);
                        Draw.ButtonFace(g, b, hot, pressed || open, chk);
                        int isz = D.S(32);
                        int ix = b.X + (b.Width - isz) / 2, iy = b.Y + D.S(3);
                        if (it.Kind == RKind.ColorSlot) PaintSwatch(g, new Rectangle(ix, iy, isz, isz), it.Slot == 1 ? E.Color1 : E.Color2, true);
                        else Icons.DrawIcon(g, it.Kind == RKind.Collapsed ? it.Icon : it.CurrentIcon, ix, iy, isz, en);
                        PaintLargeLabel(g, it, f, en, it.Kind == RKind.LargeDrop || it.Kind == RKind.Collapsed);
                        break;
                    }
                case RKind.LargeSplit:
                    {
                        bool open = F.IsDropdownOpenFor(it);
                        var top = Rectangle.FromLTRB(b.Left, b.Top, b.Right, it.DropRect.Top);
                        if (chk) { Draw.Fill(g, b, Theme.Checked); Draw.Rect(g, b, Theme.CheckedBorder); }
                        if (hot || open)
                        {
                            if (!chk) Draw.Rect(g, b, Theme.HoverBorder);
                            var part = open || _hotPart == 1 ? it.DropRect : top;
                            Draw.Fill(g, Rectangle.Inflate(part, -1, -1), pressed || open ? Theme.Pressed : (chk ? Theme.CheckedHover : Theme.Hover));
                            Draw.HLine(g, b.Left + 1, b.Right - 1, it.DropRect.Top, chk ? Theme.CheckedBorder : Theme.HoverBorder);
                        }
                        int isz = D.S(32);
                        Icons.DrawIcon(g, it.CurrentIcon, b.X + (b.Width - isz) / 2, b.Y + D.S(5), isz, en);
                        PaintLargeLabel(g, it, f, en, true);
                        break;
                    }
                case RKind.Small:
                case RKind.SmallDrop:
                case RKind.SmallSplit:
                case RKind.Check:
                    {
                        bool open = F.IsDropdownOpenFor(it);
                        if (it.Kind == RKind.Check)
                        {
                            Draw.ButtonFace(g, b, hot, pressed, false);
                            int cs = D.S(13);
                            var cr = new Rectangle(b.X + D.S(5), b.Y + (b.Height - cs) / 2, cs, cs);
                            Draw.Fill(g, cr, Theme.Input);
                            Draw.Rect(g, cr, hot ? Theme.InputBorderHover : Theme.InputBorder);
                            if (chk) Draw.CheckMark(g, Rectangle.Inflate(cr, -D.S(1), -D.S(1)), Theme.Text, D);
                            Draw.Text(g, it.Label, f, Rectangle.FromLTRB(cr.Right + D.S(6), b.Top, b.Right, b.Bottom), en ? Theme.Text : Theme.TextDisabled);
                            break;
                        }
                        Draw.ButtonFace(g, b, hot, pressed || open, chk);
                        if (it.Kind == RKind.SmallSplit && hot) Draw.VLine(g, it.DropRect.Left, b.Top + 1, b.Bottom - 1, Theme.HoverBorder);
                        Icons.DrawIcon(g, it.CurrentIcon, b.X + D.S(4), b.Y + (b.Height - D.S(16)) / 2, D.S(16), en);
                        if (!it.IconOnly) Draw.Text(g, it.Label, f, Rectangle.FromLTRB(b.X + D.S(24), b.Top, b.Right, b.Bottom), en ? Theme.Text : Theme.TextDisabled);
                        if (it.Kind != RKind.Small) Draw.Chevron(g, it.DropRect.X + it.DropRect.Width / 2, b.Y + b.Height / 2 + D.S(1), en ? Theme.TextDim : Theme.TextDisabled, D);
                        break;
                    }
                case RKind.Tool:
                    Draw.ButtonFace(g, b, hot, pressed, chk);
                    Icons.DrawIcon(g, it.CurrentIcon, b.X + (b.Width - D.S(16)) / 2, b.Y + (b.Height - D.S(16)) / 2, D.S(16), en);
                    break;
                case RKind.Gallery: PaintGallery(g, it, en); break;
                case RKind.Palette: PaintPalette(g, it); break;
                case RKind.FontRows: PaintFontRows(g, it, f); break;
            }
        }

        void PaintLargeLabel(Graphics g, RItem it, Font f, bool en, bool arrow)
        {
            var b = it.Bounds;
            var c = en ? Theme.Text : Theme.TextDisabled;
            var lines = (it.Kind == RKind.Collapsed ? it.Label : it.Label ?? "").Split('\n');
            int y1 = b.Y + D.S(46), y2 = b.Y + D.S(60);
            Draw.Text(g, lines[0], f, new Rectangle(b.X, y1, b.Width, D.S(15)), c, Draw.TextCenter);
            if (lines.Length > 1) Draw.Text(g, lines[1], f, new Rectangle(b.X, y2, b.Width, D.S(15)), c, Draw.TextCenter);
            if (arrow) Draw.Chevron(g, b.X + b.Width / 2, (lines.Length > 1 ? y2 + D.S(17) : y2 + D.S(8)), en ? Theme.TextDim : Theme.TextDisabled, D);
        }

        public void PaintSwatch(Graphics g, Rectangle r, uint color, bool big)
        {
            Draw.Rect(g, r, Theme.C(0x8A8A8A));
            var inner = Rectangle.Inflate(r, -1, -1);
            Draw.Rect(g, inner, Theme.C(0x1E1E1E));
            Draw.Fill(g, Rectangle.Inflate(inner, -1, -1), Px.ToColor(color));
        }

        Rectangle PaletteCell(RItem it, int i)
        {
            int pitch = D.S(27), cs = D.S(21);
            int c = i % 10, r = i / 10;
            return new Rectangle(it.Bounds.X + c * pitch + (pitch - cs) / 2, it.Bounds.Y + r * pitch + (pitch - cs) / 2 - D.S(2), cs, cs);
        }

        void PaintPalette(Graphics g, RItem it)
        {
            for (int i = 0; i < 30; i++)
            {
                var r = PaletteCell(it, i);
                bool hot = it == _hot && _hotPart == i;
                if (E.PaletteUsed[i])
                {
                    if (hot) Draw.Rect(g, Rectangle.Inflate(r, D.S(2), D.S(2)), Theme.Focus);
                    PaintSwatch(g, r, E.Palette[i], false);
                }
                else
                {
                    Draw.Rect(g, r, hot ? Theme.Focus : Theme.C(0x5A5A5A));
                    Draw.Fill(g, Rectangle.Inflate(r, -1, -1), Theme.C(0x242424));
                }
            }
        }

        Rectangle GalleryCell(RItem it, int visIndex, int cols)
        {
            int cell = D.S(24);
            int r = visIndex / cols, c = visIndex % cols;
            return new Rectangle(it.Bounds.X + 1 + c * cell, it.Bounds.Y + 1 + r * cell, cell, cell);
        }

        Rectangle GalleryButton(RItem it, int which)
        {
            int cell = D.S(24) * 3 / 3;
            int bx = it.Bounds.Right - D.S(16);
            int h = (it.Bounds.Height - 2) / 3;
            return new Rectangle(bx, it.Bounds.Y + 1 + which * h, D.S(16) - 1, which == 2 ? it.Bounds.Height - 2 - 2 * h : h);
        }

        void PaintGallery(Graphics g, RItem it, bool en)
        {
            var grp = _active.Groups.First(x => x.Items.Contains(it));
            int cols = GalleryCols(grp);
            int rows = (23 + cols - 1) / cols;
            _galleryFirstRow = Math.Max(0, Math.Min(_galleryFirstRow, rows - 3));
            Draw.Fill(g, it.Bounds, Theme.C(0x242424));
            Draw.Rect(g, it.Bounds, Theme.RibbonBorder);
            for (int v = 0; v < cols * 3; v++)
            {
                int idx = _galleryFirstRow * cols + v;
                if (idx >= 23) break;
                var r = GalleryCell(it, v, cols);
                bool chk = E.Tool == ToolKind.Shape && (int)E.ShapeType == idx;
                bool hot = it == _hot && _hotPart == idx;
                Draw.ButtonFace(g, r, hot, it == _pressed && _pressedPart == idx, chk);
                Icons.DrawIcon(g, Icons.ForShape((ShapeKind)idx), r.X + (r.Width - D.S(16)) / 2, r.Y + (r.Height - D.S(16)) / 2, D.S(16), en);
            }
            Draw.VLine(g, it.Bounds.Right - D.S(16) - 1, it.Bounds.Top, it.Bounds.Bottom, Theme.RibbonBorder);
            for (int i = 0; i < 3; i++)
            {
                var br = GalleryButton(it, i);
                bool can = i == 2 || (i == 0 ? _galleryFirstRow > 0 : _galleryFirstRow < rows - 3);
                bool hot = it == _hot && _hotPart == 100 + i && can;
                if (hot) Draw.Fill(g, br, Theme.Hover);
                if (i > 0) Draw.HLine(g, br.Left, br.Right, br.Top, Theme.RibbonBorder);
                var c = can ? Theme.TextDim : Theme.TextDisabled;
                int cx = br.X + br.Width / 2, cy = br.Y + br.Height / 2;
                if (i == 0) Draw.Chevron(g, cx, cy, c, D, true);
                else if (i == 1) Draw.Chevron(g, cx, cy, c, D, false);
                else { Draw.HLine(g, cx - D.S(3), cx + D.S(4), cy - D.S(3), c); Draw.Chevron(g, cx, cy + D.S(1), c, D, false); }
            }
        }

        // ---- font rows (Text tab)
        Rectangle FontFamilyRect(RItem it) => new Rectangle(it.Bounds.X, it.Bounds.Y, D.S(150), RowH);
        Rectangle FontSizeRect(RItem it) => new Rectangle(it.Bounds.X + D.S(156), it.Bounds.Y, D.S(54), RowH);
        Rectangle StyleButton(RItem it, int i) => new Rectangle(it.Bounds.X + i * D.S(26), it.Bounds.Y + D.S(30), D.S(24), D.S(24));

        void PaintCombo(Graphics g, Rectangle r, string text, bool hotText, bool hotArrow, bool en, Font f)
        {
            Draw.Fill(g, r, Theme.Input);
            Draw.Rect(g, r, hotText || hotArrow ? Theme.InputBorderHover : Theme.InputBorder);
            var ar = Rectangle.FromLTRB(r.Right - D.S(16), r.Top + 1, r.Right - 1, r.Bottom - 1);
            if (hotArrow) Draw.Fill(g, ar, Theme.Hover);
            Draw.Chevron(g, ar.X + ar.Width / 2, ar.Y + ar.Height / 2, en ? Theme.TextDim : Theme.TextDisabled, D);
            Draw.Text(g, text, f, Rectangle.FromLTRB(r.Left + D.S(4), r.Top, ar.Left - D.S(2), r.Bottom), en ? Theme.Text : Theme.TextDisabled, Draw.TextLeft | TextFormatFlags.EndEllipsis);
        }

        void PaintFontRows(Graphics g, RItem it, Font f)
        {
            var st = E.TextTool.CurrentStyle;
            bool hot = it == _hot;
            if (!(_edit != null && _editFor == Cmd.FontFamily))
                PaintCombo(g, FontFamilyRect(it), st.Face ?? "", hot && _hotPart == 0, hot && _hotPart == 1, true, f);
            else PaintCombo(g, FontFamilyRect(it), "", false, false, true, f);
            if (!(_edit != null && _editFor == Cmd.FontSize))
                PaintCombo(g, FontSizeRect(it), st.Size > 0 ? FormatSize(st.Size) : "", hot && _hotPart == 2, hot && _hotPart == 3, true, f);
            else PaintCombo(g, FontSizeRect(it), "", false, false, true, f);
            Cmd[] cmds = { Cmd.TextBold, Cmd.TextItalic, Cmd.TextUnderline, Cmd.TextStrike };
            for (int i = 0; i < 4; i++)
            {
                var r = StyleButton(it, i);
                Draw.ButtonFace(g, r, hot && _hotPart == 10 + i, _pressed == it && _pressedPart == 10 + i, F.IsChecked(cmds[i]));
                Icons.DrawIcon(g, Commands.Info(cmds[i]).Icon, r.X + (r.Width - D.S(16)) / 2, r.Y + (r.Height - D.S(16)) / 2, D.S(16));
            }
        }

        static string FormatSize(float s) => Math.Abs(s - Math.Round(s)) < 0.01 ? ((int)Math.Round(s)).ToString() : s.ToString("0.#");

        // ================================================================== hit testing
        RItem HitItem(Point p, out int part)
        {
            part = 0;
            if (_minimized && !_expanded) return null;
            if (p.Y < BodyTop) return null;
            foreach (var grp in _active.Groups)
            {
                if (grp.EffectiveLevel >= 2) { if (grp.CollapseButton.Bounds.Contains(p)) return grp.CollapseButton; continue; }
                foreach (var it in grp.Items)
                {
                    if (!it.Bounds.Contains(p)) continue;
                    switch (it.Kind)
                    {
                        case RKind.LargeSplit: case RKind.SmallSplit: part = it.DropRect.Contains(p) ? 1 : 0; break;
                        case RKind.LargeDrop: case RKind.SmallDrop: part = 1; break;
                        case RKind.Palette:
                            part = -1;
                            for (int i = 0; i < 30; i++) if (Rectangle.Inflate(PaletteCell(it, i), D.S(2), D.S(2)).Contains(p)) { part = i; break; }
                            if (part < 0) continue;
                            break;
                        case RKind.Gallery:
                            {
                                int cols = GalleryCols(grp);
                                part = -1;
                                for (int i = 0; i < 3; i++) if (GalleryButton(it, i).Contains(p)) part = 100 + i;
                                if (part < 0)
                                    for (int v = 0; v < cols * 3; v++)
                                    {
                                        int idx = _galleryFirstRow * cols + v;
                                        if (idx < 23 && GalleryCell(it, v, cols).Contains(p)) { part = idx; break; }
                                    }
                                if (part < 0) continue;
                                break;
                            }
                        case RKind.FontRows:
                            {
                                part = -1;
                                var fr = FontFamilyRect(it); var sr = FontSizeRect(it);
                                if (fr.Contains(p)) part = p.X >= fr.Right - D.S(16) ? 1 : 0;
                                else if (sr.Contains(p)) part = p.X >= sr.Right - D.S(16) ? 3 : 2;
                                else for (int i = 0; i < 4; i++) if (StyleButton(it, i).Contains(p)) part = 10 + i;
                                if (part < 0) continue;
                                break;
                            }
                    }
                    return it;
                }
            }
            return null;
        }

        int HitTab(Point p)
        {
            if (p.Y >= TabH) return -2;
            if (_fileRect.Contains(p)) return -1;
            if (_minRect.Contains(p)) return -3;
            if (_helpRect.Contains(p)) return -4;
            var tabs = VisibleTabs;
            for (int i = 0; i < tabs.Count; i++) if (tabs[i].TabRect.Contains(p)) return i;
            return -2;
        }

        // ================================================================== input
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_keyTipMode != KeyTipMode.Off) return;
            int ht = HitTab(e.Location);
            int part;
            var it = HitItem(e.Location, out part);
            if (ht != _hotTab) { _hotTab = ht; Invalidate(new Rectangle(0, 0, Width, TabH)); }
            if (it != _hot || part != _hotPart)
            {
                InvalidateItem(_hot);
                _hot = it; _hotPart = part;
                InvalidateItem(_hot);
            }
            UpdateTip(e.Location, ht, it, part);
        }

        void UpdateTip(Point p, int ht, RItem it, int part)
        {
            string title = null, text = null;
            object owner = null;
            if (ht == -1) { var i = Commands.Info(Cmd.AppMenu); title = i.TipTitle; text = i.Tip; owner = "file"; }
            else if (ht == -3) { title = _minimized ? "Expand the Ribbon (Ctrl+F1)" : "Minimize the Ribbon (Ctrl+F1)"; text = _minimized ? "Show the Ribbon so that it is always expanded." : "Show only the tab names on the Ribbon."; owner = "min"; }
            else if (ht == -4) { var i = Commands.Info(Cmd.Help); title = i.TipTitle; text = i.Tip; owner = "help"; }
            else if (it != null)
            {
                owner = (object)it.GetHashCode() + ":" + part;
                switch (it.Kind)
                {
                    case RKind.Palette:
                        title = part < 20 ? Commands.PaletteNames[part] : (E.PaletteUsed[part] ? "Custom color " + Px.Hex(E.Palette[part]) : "Custom color");
                        text = "Select a color from the color palette.";
                        break;
                    case RKind.Gallery:
                        if (part < 23) { title = Commands.ShapeNames[part]; text = Commands.ShapeTip((ShapeKind)part); }
                        else if (part == 100) title = "Row up";
                        else if (part == 101) title = "Row down";
                        else { title = "More shapes"; }
                        break;
                    case RKind.Collapsed: title = it.Label; text = "Click to show the " + it.Label + " group."; break;
                    case RKind.FontRows:
                        {
                            var c = part <= 1 ? Cmd.FontFamily : part <= 3 ? Cmd.FontSize : (Cmd)((int)Cmd.TextBold + part - 10);
                            var i = Commands.Info(c); title = i.TipTitle; text = i.Tip;
                            break;
                        }
                    default:
                        {
                            var c = part == 1 && it.DropCmd != Cmd.None ? it.DropCmd : it.Cmd;
                            var i = Commands.Info(c); title = i.TipTitle; text = i.Tip;
                            if (it.Cmd == Cmd.Brushes && part == 0) { title = Commands.BrushNames[(int)E.BrushType]; text = Commands.BrushTips[(int)E.BrushType]; }
                            break;
                        }
                }
            }
            if (owner == null) { Tips.Hide(); return; }
            var anchor = it != null ? it.Bounds : (ht == -1 ? _fileRect : ht == -3 ? _minRect : _helpRect);
            var sp = PointToScreen(new Point(Math.Max(anchor.Left, p.X - D.S(10)), it != null ? BodyTop + BodyH + D.S(2) : anchor.Bottom + D.S(4)));
            Tips.Request(owner, title, text, sp, D.Value);
        }

        void InvalidateItem(RItem it)
        {
            if (it == null) return;
            var r = it.Bounds; r.Inflate(D.S(3), D.S(3));
            Invalidate(r);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            InvalidateItem(_hot);
            _hot = null; _hotPart = -1;
            if (_hotTab != -2) { _hotTab = -2; Invalidate(new Rectangle(0, 0, Width, TabH)); }
            Tips.Hide();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Tips.Hide();
            if (_keyTipMode != KeyTipMode.Off) EndKeyTips();
            if (e.Button != MouseButtons.Left) return;
            int ht = HitTab(e.Location);
            if (ht == -1) { F.ShowAppMenu(RectangleToScreen(_fileRect)); return; }
            if (ht == -3) { F.Execute(Cmd.ToggleRibbon); return; }
            if (ht == -4) { F.Execute(Cmd.Help); return; }
            if (ht >= 0)
            {
                var t = VisibleTabs[ht];
                if (e.Clicks >= 2) { F.Execute(Cmd.ToggleRibbon); return; }
                if (_minimized)
                {
                    if (_expanded && t == _active) { Collapse(); return; }
                    _active = t; Expand();
                }
                else if (t != _active) { _active = t; CommitEdit(false); Relayout(); }
                Invalidate();
                return;
            }
            int part;
            var it = HitItem(e.Location, out part);
            if (it == null || !IsItemEnabled(it)) return;
            _pressed = it; _pressedPart = part;
            InvalidateItem(it);
            // dropdowns open on press, like the real ribbon
            if (it.Kind == RKind.Collapsed) { ShowCollapsedGroup(it); _pressed = null; return; }
            if ((it.Kind == RKind.LargeSplit || it.Kind == RKind.SmallSplit) && part == 1 || it.Kind == RKind.LargeDrop || it.Kind == RKind.SmallDrop)
            {
                _pressed = null;
                F.ShowDropdown(it.DropCmd != Cmd.None ? it.DropCmd : it.Cmd, it, RectangleToScreen(it.Bounds));
                return;
            }
            if (it.Kind == RKind.Gallery && part >= 100)
            {
                var grp = _active.Groups.First(x => x.Items.Contains(it));
                int rows = (23 + GalleryCols(grp) - 1) / GalleryCols(grp);
                if (part == 100 && _galleryFirstRow > 0) _galleryFirstRow--;
                else if (part == 101 && _galleryFirstRow < rows - 3) _galleryFirstRow++;
                else if (part == 102) F.ShowDropdown(Cmd.ShapesMore, it, RectangleToScreen(it.Bounds));
                _pressed = null;
                InvalidateItem(it);
                return;
            }
            if (it.Kind == RKind.FontRows && part <= 3)
            {
                _pressed = null;
                if (part == 1) F.ShowDropdown(Cmd.FontFamily, it, RectangleToScreen(FontFamilyRect(it)));
                else if (part == 3) F.ShowDropdown(Cmd.FontSize, it, RectangleToScreen(FontSizeRect(it)));
                else BeginEdit(part == 0 ? Cmd.FontFamily : Cmd.FontSize, part == 0 ? FontFamilyRect(it) : FontSizeRect(it));
                return;
            }
            Capture = true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var pressed = _pressed; int pp = _pressedPart;
            _pressed = null; _pressedPart = -1;
            if (pressed == null) return;
            InvalidateItem(pressed);
            int part;
            var it = HitItem(e.Location, out part);
            if (it != pressed || part != pp) return;
            ClickItem(it, part, e.Button == MouseButtons.Right);
        }

        void ClickItem(RItem it, int part, bool right)
        {
            switch (it.Kind)
            {
                case RKind.Palette:
                    E.PickPaletteColor(part, right);
                    break;
                case RKind.Gallery:
                    if (part < 23) E.SetShape((ShapeKind)part);
                    break;
                case RKind.ColorSlot:
                    E.ActiveColor = it.Slot; E.Notify(Change.Colors);
                    break;
                case RKind.FontRows:
                    if (part >= 10) F.Execute((Cmd)((int)Cmd.TextBold + part - 10));
                    break;
                default:
                    F.Execute(it.Cmd);
                    break;
            }
            if (_expanded && it.Kind != RKind.Palette && it.Kind != RKind.ColorSlot) Collapse();
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_minimized && !_expanded) return;
            var tabs = VisibleTabs;
            int i = tabs.IndexOf(_active);
            int n = Math.Max(0, Math.Min(tabs.Count - 1, i + (e.Delta > 0 ? -1 : 1)));
            if (n != i) { _active = tabs[n]; CommitEdit(false); Relayout(); }
        }

        // RButton on palette sets color 2 (classic behaviour)
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_RBUTTONDOWN)
            {
                var p = new Point(Native.LoWord(m.LParam), Native.HiWord(m.LParam));
                int part; var it = HitItem(p, out part);
                if (it != null && it.Kind == RKind.Palette) { E.PickPaletteColor(part, true); m.Result = IntPtr.Zero; return; }
            }
            base.WndProc(ref m);
        }

        // ================================================================== minimized ribbon
        void Expand()
        {
            _expanded = true;
            Height = FullHeight;
            BringToFront();
            Relayout();
            F.RibbonExpandedChanged(true);
        }

        public void Collapse()
        {
            if (!_expanded) return;
            _expanded = false;
            Height = LayoutHeight;
            Invalidate();
            F.RibbonExpandedChanged(false);
        }

        // ================================================================== collapsed group popup
        void ShowCollapsedGroup(RItem cb)
        {
            var popup = new GroupPopup(this, cb.Group, D.Value);
            popup.ShowFor(RectangleToScreen(cb.Bounds));
        }

        /// <summary>Lays a group out at full size inside a popup and forwards clicks.</summary>
        sealed class GroupPopup : PopupWindow
        {
            readonly Ribbon R; readonly RGroup G;
            RItem _hot; int _hotPart = -1;
            public GroupPopup(Ribbon r, RGroup g, int dpi) : base(dpi) { R = r; G = g; }

            public void ShowFor(Rectangle anchor)
            {
                G.InPopup = true;
                int w = R.LayoutGroup(G, 0);
                AnchorScreen = anchor;
                Place(anchor, new Size(w + R.D.S(4), R.BodyH), false);
                Dismissed += () => { G.InPopup = false; R.Relayout(); };
                Popups.Open(this);
            }

            Point ToRibbon(Point p) => new Point(p.X, p.Y + R.BodyTop);
            public Rectangle ToScreen(Rectangle r) { r.Offset(0, -R.BodyTop); return RectangleToScreen(r); }

            protected override void Render(Graphics g)
            {
                Draw.Fill(g, ClientRectangle, Theme.Ribbon);
                var f = Theme.UiFont(R.D);
                var saveHot = R._hot; var savePart = R._hotPart;
                R._hot = _hot; R._hotPart = _hotPart;
                var st = g.Save();
                g.TranslateTransform(0, -R.BodyTop);
                foreach (var it in G.Items) R.PaintItem(g, it, f);
                g.Restore(st);
                R._hot = saveHot; R._hotPart = savePart;
                Draw.Text(g, G.Name, f, new Rectangle(0, R.D.S(93), Width, R.D.S(18)), Theme.GroupLabel, Draw.TextCenter);
                Draw.Rect(g, ClientRectangle, Theme.PopupBorder);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int part; var it = R.HitInGroup(G, ToRibbon(e.Location), out part);
                if (it != _hot || part != _hotPart) { _hot = it; _hotPart = part; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = null; _hotPart = -1; Invalidate(); }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                int part; var it = R.HitInGroup(G, ToRibbon(e.Location), out part);
                if (it == null || !R.IsItemEnabled(it)) return;
                if ((it.Kind == RKind.LargeSplit || it.Kind == RKind.SmallSplit) && part == 1 || it.Kind == RKind.LargeDrop || it.Kind == RKind.SmallDrop)
                {
                    var cmd = it.DropCmd != Cmd.None ? it.DropCmd : it.Cmd;
                    var rect = ToScreen(it.Bounds);
                    Popups.CloseAll();
                    R.F.ShowDropdown(cmd, it, rect);
                    return;
                }
                if (it.Kind == RKind.Gallery && part >= 100)
                {
                    int rows = (23 + 6) / 7;
                    if (part == 100 && R._galleryFirstRow > 0) R._galleryFirstRow--;
                    else if (part == 101 && R._galleryFirstRow < rows - 3) R._galleryFirstRow++;
                    else if (part == 102) { var rect = ToScreen(it.Bounds); Popups.CloseAll(); R.F.ShowDropdown(Cmd.ShapesMore, it, rect); return; }
                    Invalidate(); return;
                }
                if (it.Kind == RKind.FontRows && part <= 3) return;
                bool keep = it.Kind == RKind.Palette || it.Kind == RKind.ColorSlot;
                if (!keep) Popups.CloseAll();
                R.ClickItem(it, part, e.Button == MouseButtons.Right);
                if (keep) Invalidate();
            }
        }

        RItem HitInGroup(RGroup g, Point ribbonPt, out int part)
        {
            var act = _active; bool savedMin = _minimized;
            _active = new RTab { Groups = new List<RGroup> { g } };
            _minimized = false;
            try { return HitItem(ribbonPt, out part); }
            finally { _active = act; _minimized = savedMin; }
        }

        // ================================================================== inline editing (font family / size)
        void BeginEdit(Cmd which, Rectangle r)
        {
            CommitEdit(false);
            _editFor = which;
            var st = E.TextTool.CurrentStyle;
            _edit = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Input,
                ForeColor = Theme.Text,
                Font = Theme.UiFont(D),
                Text = which == Cmd.FontFamily ? st.Face : (st.Size > 0 ? FormatSize(st.Size) : ""),
            };
            int h = _edit.PreferredHeight;
            _edit.Bounds = new Rectangle(r.X + D.S(4), r.Y + (r.Height - h) / 2, r.Width - D.S(22), h);
            Controls.Add(_edit);
            _edit.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitEdit(true); F.FocusCanvas(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CommitEdit(false); F.FocusCanvas(); }
            };
            _edit.LostFocus += (s, e) => BeginInvoke(new Action(() => CommitEdit(true)));
            _edit.Focus();
            _edit.SelectAll();
            Invalidate();
        }

        public bool Editing => _edit != null;

        void CommitEdit(bool apply)
        {
            if (_edit == null) return;
            var box = _edit; var which = _editFor;
            _edit = null;
            string text = box.Text.Trim();
            Controls.Remove(box);
            box.Dispose();
            if (apply && text.Length > 0)
            {
                if (which == Cmd.FontFamily) E.TextTool.SetFace(text);
                else if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out float sz) && sz >= 1 && sz <= 1638) E.TextTool.SetSize(sz);
            }
            Invalidate();
        }
    }
}
