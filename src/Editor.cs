using System;
using System.Collections.Generic;
using System.Drawing;

namespace PaintDark
{
    public enum ToolKind { Select, Pencil, Fill, Text, Eraser, Picker, Magnifier, Brush, Shape }
    public enum BrushKind { Brush, Calligraphy1, Calligraphy2, Airbrush, Oil, Crayon, Marker, NaturalPencil, Watercolor }
    public enum ShapeKind
    {
        Line, Curve, Oval, Rectangle, RoundedRectangle, Polygon, Triangle, RightTriangle, Diamond, Pentagon, Hexagon,
        RightArrow, LeftArrow, UpArrow, DownArrow, Star4, Star5, Star6, RoundedCallout, OvalCallout, CloudCallout, Heart, Lightning
    }
    public enum Texture { None, Solid, Crayon, Marker, Oil, NaturalPencil, Watercolor }
    public enum SelectMode { Rectangle, FreeForm }
    public enum Units { Pixels, Inches, Centimeters }

    [Flags]
    public enum Change
    {
        None = 0, Tool = 1, Colors = 2, Image = 4, ImageSize = 8, Selection = 16, Zoom = 32, Title = 64,
        Status = 128, ToolSize = 256, Text = 512, Commands = 1024, Palette = 2048, View = 4096, All = 0xFFFF
    }

    /// <summary>Central state of the application: the picture, colors, tools and their options.</summary>
    public sealed partial class Editor
    {
        public Document Doc { get; private set; }
        public CanvasView View;
        public MainForm Form;

        public uint Color1, Color2;
        public int ActiveColor = 1;
        public readonly uint[] Palette = new uint[30];
        public readonly bool[] PaletteUsed = new bool[30];
        int _nextCustom;

        public ToolKind Tool { get; private set; } = ToolKind.Pencil;
        ToolKind _previousTool = ToolKind.Pencil;
        public BrushKind BrushType = BrushKind.Brush;
        public ShapeKind ShapeType = ShapeKind.Line;
        public SelectMode SelectionMode = SelectMode.Rectangle;
        public bool TransparentSelection;
        public Texture Outline = Texture.Solid;
        public Texture ShapeFill = Texture.None;
        public Units Units = Units.Pixels;

        public event Action<Change> Changed;

        readonly Dictionary<ToolKind, ToolBase> _tools = new Dictionary<ToolKind, ToolBase>();
        public ToolBase ActiveTool { get; private set; }

        public static readonly uint[] DefaultColors =
        {
            0xFF000000, 0xFF7F7F7F, 0xFF880015, 0xFFED1C24, 0xFFFF7F27, 0xFFFFF200, 0xFF22B14C, 0xFF00A2E8, 0xFF3F48CC, 0xFFA349A4,
            0xFFFFFFFF, 0xFFC3C3C3, 0xFFB97A57, 0xFFFFAEC9, 0xFFFFC90E, 0xFFEFE4B0, 0xFFB5E61D, 0xFF99D9EA, 0xFF7092BE, 0xFFC8BFE7,
        };

        // ---------- sizes (labels exist for 1,2,3,4,5,6,8,10,16,24,30,40 px) ----------
        public static readonly int[] PencilSizes = { 1, 2, 3, 4 };
        public static readonly int[] EraserSizes = { 4, 6, 8, 10 };
        public static readonly int[] LineSizes = { 1, 3, 5, 8 };
        public static readonly int[] BigBrushSizes = { 8, 16, 30, 40 };
        public static readonly int[] AirbrushSizes = { 8, 16, 24, 30 };
        readonly Dictionary<string, int> _sizes = new Dictionary<string, int>();

        public Editor()
        {
            for (int i = 0; i < 20; i++) { Palette[i] = DefaultColors[i]; PaletteUsed[i] = true; }
            _tools[ToolKind.Select] = new SelectTool(this);
            _tools[ToolKind.Pencil] = new PencilTool(this);
            _tools[ToolKind.Fill] = new FillTool(this);
            _tools[ToolKind.Text] = new TextTool(this);
            _tools[ToolKind.Eraser] = new EraserTool(this);
            _tools[ToolKind.Picker] = new PickerTool(this);
            _tools[ToolKind.Magnifier] = new MagnifierTool(this);
            _tools[ToolKind.Brush] = new BrushTool(this);
            _tools[ToolKind.Shape] = new ShapeTool(this);
            ActiveTool = _tools[Tool];
        }

        public T GetTool<T>(ToolKind k) where T : ToolBase => (T)_tools[k];
        public SelectTool Select => (SelectTool)_tools[ToolKind.Select];
        public TextTool TextTool => (TextTool)_tools[ToolKind.Text];
        public ShapeTool Shapes => (ShapeTool)_tools[ToolKind.Shape];

        public void Notify(Change c) => Changed?.Invoke(c);

        public void SetDocument(Document d)
        {
            var old = Doc;
            Doc = d;
            old?.Dispose();
            foreach (var t in _tools.Values) t.DocumentChanged();
            View?.DocumentReplaced();
            Notify(Change.All);
        }

        // ---------- tools ----------
        public void SetTool(ToolKind k, bool rememberPrevious = true)
        {
            if (k == Tool && ActiveTool != null) { Notify(Change.Tool); return; }
            ActiveTool?.Deactivate();
            if (rememberPrevious && Tool != ToolKind.Picker) _previousTool = Tool;
            Tool = k;
            ActiveTool = _tools[k];
            ActiveTool.Activate();
            View?.ToolChanged();
            Notify(Change.Tool | Change.ToolSize | Change.Commands | Change.Status);
        }

        public void SetBrush(BrushKind b) { BrushType = b; SetTool(ToolKind.Brush); Notify(Change.Tool | Change.ToolSize); }

        public void SetShape(ShapeKind s)
        {
            if (Tool == ToolKind.Shape && Shapes.HasPending) Shapes.Commit();
            ShapeType = s;
            SetTool(ToolKind.Shape);
            Notify(Change.Tool | Change.ToolSize);
        }

        public void ReturnToPreviousTool() => SetTool(_previousTool == ToolKind.Picker ? ToolKind.Pencil : _previousTool, false);

        public void CommitPending()
        {
            if (ActiveTool != null && ActiveTool.HasPending) ActiveTool.Commit();
        }

        // ---------- sizes ----------
        string SizeKey()
        {
            switch (Tool)
            {
                case ToolKind.Pencil: return "pencil";
                case ToolKind.Eraser: return "eraser";
                case ToolKind.Brush: return "brush." + BrushType;
                case ToolKind.Shape: return "shape";
                default: return null;
            }
        }

        public int[] SizeChoices
        {
            get
            {
                switch (Tool)
                {
                    case ToolKind.Pencil: return PencilSizes;
                    case ToolKind.Eraser: return EraserSizes;
                    case ToolKind.Shape: return LineSizes;
                    case ToolKind.Brush:
                        switch (BrushType)
                        {
                            case BrushKind.Brush: case BrushKind.NaturalPencil: return LineSizes;
                            case BrushKind.Airbrush: return AirbrushSizes;
                            default: return BigBrushSizes;
                        }
                    default: return null;
                }
            }
        }

        public bool HasSize => SizeChoices != null;

        int DefaultSizeIndex()
        {
            switch (Tool)
            {
                case ToolKind.Eraser: return 2;
                case ToolKind.Brush: return BrushType == BrushKind.Brush || BrushType == BrushKind.NaturalPencil || BrushType == BrushKind.Airbrush ? 1 : 0;
                default: return 0;
            }
        }

        public int ToolSize
        {
            get
            {
                var k = SizeKey();
                if (k == null) return 1;
                if (_sizes.TryGetValue(k, out int v)) return v;
                var c = SizeChoices;
                return c[DefaultSizeIndex()];
            }
            set
            {
                var k = SizeKey();
                if (k == null) return;
                _sizes[k] = Math.Max(1, Math.Min(100, value));
                ActiveTool?.OptionsChanged();
                View?.ToolChanged();
                Notify(Change.ToolSize);
            }
        }

        public int SizeFor(string key, int[] choices, int defIndex) => _sizes.TryGetValue(key, out int v) ? v : choices[defIndex];

        public void StepSize(int delta) { if (HasSize) ToolSize = ToolSize + delta; }

        public IEnumerable<KeyValuePair<string, int>> AllSizes => _sizes;
        public void LoadSize(string key, int v) { if (v >= 1 && v <= 100) _sizes[key] = v; }

        // ---------- colors ----------
        public void SetColor(int slot, uint c)
        {
            c |= 0xFF000000u;
            if (slot == 1) Color1 = c; else Color2 = c;
            ActiveTool?.ColorsChanged(slot);
            Notify(Change.Colors);
        }

        public void PickPaletteColor(int index, bool secondary)
        {
            if (!PaletteUsed[index]) return;
            int slot = secondary ? 2 : ActiveColor;
            if (ActiveTool is TextTool tt && tt.IsEditing && slot == 1) tt.ApplyTextColor(Palette[index]);
            SetColor(slot, Palette[index]);
        }

        public void AddCustomColor(uint c)
        {
            c |= 0xFF000000u;
            for (int i = 20; i < 30; i++) if (PaletteUsed[i] && Palette[i] == c) return;
            int idx = -1;
            for (int i = 20; i < 30; i++) if (!PaletteUsed[i]) { idx = i; break; }
            if (idx < 0) { idx = 20 + _nextCustom; _nextCustom = (_nextCustom + 1) % 10; }
            Palette[idx] = c; PaletteUsed[idx] = true;
            Notify(Change.Palette);
        }

        public void SetOutline(Texture t) { Outline = t; ActiveTool?.OptionsChanged(); Notify(Change.Tool); }
        public void SetShapeFill(Texture t) { ShapeFill = t; ActiveTool?.OptionsChanged(); Notify(Change.Tool); }

        // ---------- invalidation helpers used by tools ----------
        public void InvalidateImage(Rectangle r) => View?.InvalidateImage(r);
        public void InvalidateAll() => View?.Invalidate();

        /// <summary>Record the change and refresh UI state.</summary>
        public void Commit(Rectangle hint, string name)
        {
            var changed = Doc.Commit(hint, name);
            View?.InvalidateImage(changed);
            Notify(Change.Image | Change.Commands | Change.Title);
        }

        public void ReplaceImage(Dib img, string name, bool? alpha = null)
        {
            bool sizeChanged = img.W != Doc.Width || img.H != Doc.Height;
            Doc.ReplaceImage(img, name, alpha);
            foreach (var t in _tools.Values) t.DocumentChanged();
            View?.ImageSizeChanged();
            Notify(Change.Image | Change.Commands | Change.Title | (sizeChanged ? Change.ImageSize : 0));
        }

        public bool CanUndo => (ActiveTool != null && ActiveTool.HasPending) || Doc.CanUndo;
        public bool CanRedo => Doc.CanRedo;

        public void Undo()
        {
            if (ActiveTool != null && ActiveTool.HasPending)
            {
                if (ActiveTool.UndoPending()) { Notify(Change.Image | Change.Commands | Change.Selection | Change.Title); return; }
            }
            if (Doc.Undo(out var r, out bool sizeChanged)) AfterHistory(r, sizeChanged);
        }

        public void Redo()
        {
            CommitPending();
            if (Doc.Redo(out var r, out bool sizeChanged)) AfterHistory(r, sizeChanged);
        }

        void AfterHistory(Rectangle r, bool sizeChanged)
        {
            foreach (var t in _tools.Values) t.DocumentChanged();
            if (sizeChanged) View?.ImageSizeChanged(); else View?.InvalidateImage(r);
            Notify(Change.Image | Change.Commands | Change.Title | Change.Selection | (sizeChanged ? Change.ImageSize : 0));
        }

        // ---------- status bar ----------
        public Point? CursorPos;
        public Size? StatusSelSize;
        public void SetCursorPos(Point? p) { if (p != CursorPos) { CursorPos = p; Notify(Change.Status); } }
        public void SetSelSize(Size? s) { if (s != StatusSelSize) { StatusSelSize = s; Notify(Change.Status); } }

        public string FormatLength(double px)
        {
            switch (Units)
            {
                case Units.Inches: return (px / Doc.DpiX).ToString("0.##");
                case Units.Centimeters: return (px / Doc.DpiX * 2.54).ToString("0.##");
                default: return ((int)Math.Round(px)).ToString();
            }
        }
        public string UnitSuffix => Units == Units.Inches ? " in" : Units == Units.Centimeters ? " cm" : "px";
    }
}
