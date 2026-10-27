using System;
using System.Drawing;
using System.Windows.Forms;

namespace PaintDark
{
    /// <summary>Cursor position, selection size, picture size, file size and the zoom slider.</summary>
    public sealed class StatusBar : DarkControl
    {
        readonly Editor E;
        readonly MainForm F;
        Rectangle _minus, _plus, _track, _zoomText;
        int _hot;          // 1 minus, 2 plus, 3 thumb/track
        bool _dragging;

        public StatusBar(MainForm f, Editor e) { F = f; E = e; }

        public int PreferredHeight => D.S(26);

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutParts(); }
        protected override void OnDpiApplied() => LayoutParts();

        void LayoutParts()
        {
            int h = Height, bs = D.S(20);
            _plus = new Rectangle(Width - D.S(8) - bs, (h - bs) / 2, bs, bs);
            _track = new Rectangle(_plus.Left - D.S(4) - D.S(110), (h - bs) / 2, D.S(110), bs);
            _minus = new Rectangle(_track.Left - D.S(4) - bs, (h - bs) / 2, bs, bs);
            _zoomText = new Rectangle(_minus.Left - D.S(52), 0, D.S(48), h);
        }

        // slider position <-> zoom: 100% sits in the middle, left half spans 12.5..100, right half 100..800
        float ZoomToPos(float z)
        {
            double t = z <= 1 ? 0.5 * (Math.Log(z / 0.125, 2) / 3.0) : 0.5 + 0.5 * ((z - 1) / 7.0);
            return (float)Math.Max(0, Math.Min(1, t));
        }

        float PosToZoom(float t)
        {
            float z = t <= 0.5f ? (float)(0.125 * Math.Pow(2, t / 0.5 * 3)) : 1 + (t - 0.5f) / 0.5f * 7;
            // snap to Paint's zoom levels
            float best = CanvasView.Levels[0];
            foreach (var l in CanvasView.Levels) if (Math.Abs(ZoomToPos(l) - t) < Math.Abs(ZoomToPos(best) - t)) best = l;
            return best;
        }

        protected override void Render(Graphics g, Rectangle clip)
        {
            Draw.Fill(g, ClientRectangle, Theme.Status);
            Draw.HLine(g, 0, Width, 0, Theme.StatusBorder);
            var f = Theme.UiFont(D);
            int h = Height;
            int sec = D.S(200);
            int x = 0;
            void Section(Ico ico, string text, int width)
            {
                if (ico != Ico.None) Icons.DrawIcon(g, ico, x + D.S(10), (h - D.S(16)) / 2 + D.S(1), D.S(16));
                Draw.Text(g, text, f, new Rectangle(x + D.S(32), 1, width - D.S(34), h - 1), Theme.StatusText);
                x += width;
                Draw.VLine(g, x - 1, D.S(5), h - D.S(4), Theme.StatusBorder);
            }
            string u = E.UnitSuffix;
            string cur = E.CursorPos.HasValue ? E.FormatLength(E.CursorPos.Value.X) + ", " + E.FormatLength(E.CursorPos.Value.Y) + u : "";
            var ss = E.StatusSelSize;
            string sel = ss.HasValue ? E.FormatLength(ss.Value.Width) + " × " + E.FormatLength(ss.Value.Height) + u : "";
            string img = E.Doc == null ? "" : E.FormatLength(E.Doc.Width) + " × " + E.FormatLength(E.Doc.Height) + u;
            string size = E.Doc != null && E.Doc.FileSize >= 0 ? "Size: " + FormatBytes(E.Doc.FileSize) : "";
            if (Width > D.S(560)) Section(Ico.StCursor, cur, sec);
            Section(Ico.StSelection, sel, sec);
            if (Width > D.S(760)) Section(Ico.StCanvas, img, sec);
            if (Width > D.S(980)) Section(Ico.StFile, size, sec);

            // zoom
            Draw.Text(g, F.ZoomPercent + "%", f, _zoomText, Theme.StatusText, Draw.TextRight);
            Draw.ButtonFace(g, _minus, _hot == 1, false, false);
            Draw.ButtonFace(g, _plus, _hot == 2, false, false);
            int cy = h / 2;
            Draw.HLine(g, _minus.X + D.S(6), _minus.Right - D.S(6), cy, Theme.StatusText);
            Draw.HLine(g, _plus.X + D.S(6), _plus.Right - D.S(6), cy, Theme.StatusText);
            Draw.VLine(g, _plus.X + _plus.Width / 2, _plus.Y + D.S(6), _plus.Bottom - D.S(6), Theme.StatusText);
            Draw.HLine(g, _track.Left, _track.Right, cy, Theme.C(0x6A6A6A));
            Draw.VLine(g, _track.Left + _track.Width / 2, cy - D.S(4), cy + D.S(5), Theme.C(0x8A8A8A));
            int tx = _track.Left + (int)(ZoomToPos(F.ZoomValue) * _track.Width);
            var thumb = new Rectangle(tx - D.S(3), cy - D.S(7), D.S(7), D.S(14));
            Draw.Fill(g, thumb, _hot == 3 || _dragging ? Theme.AccentHover : Theme.C(0xBDBDBD));
        }

        static string FormatBytes(long b)
        {
            if (b < 1024) return b + " bytes";
            if (b < 1024 * 1024) return (b / 1024.0).ToString("0.#") + "KB";
            if (b < 1024L * 1024 * 1024) return (b / 1048576.0).ToString("0.#") + "MB";
            return (b / 1073741824.0).ToString("0.##") + "GB";
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) { SetFromX(e.X); return; }
            int h = _minus.Contains(e.Location) ? 1 : _plus.Contains(e.Location) ? 2 : Rectangle.Inflate(_track, D.S(4), 0).Contains(e.Location) ? 3 : 0;
            if (h != _hot) { _hot = h; Invalidate(); }
            if (h == 1) Tips.Request("zm-", "Zoom out", null, PointToScreen(new Point(e.X, -D.S(28))), D.Value);
            else if (h == 2) Tips.Request("zm+", "Zoom in", null, PointToScreen(new Point(e.X, -D.S(28))), D.Value);
            else if (h == 3) Tips.Request("zms", "Zoom level", null, PointToScreen(new Point(e.X, -D.S(28))), D.Value);
            else Tips.Hide();
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = 0; Invalidate(); Tips.Hide(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Tips.Hide();
            if (e.Button != MouseButtons.Left) return;
            if (_minus.Contains(e.Location)) F.Execute(Cmd.ZoomOut);
            else if (_plus.Contains(e.Location)) F.Execute(Cmd.ZoomIn);
            else if (Rectangle.Inflate(_track, D.S(4), 0).Contains(e.Location)) { _dragging = true; Capture = true; SetFromX(e.X); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false; Capture = false; Invalidate();
        }

        void SetFromX(int x)
        {
            float t = Math.Max(0, Math.Min(1, (x - _track.Left) / (float)_track.Width));
            F.SetZoom(PosToZoom(t));
        }
    }
}
