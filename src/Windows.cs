using System;
using System.Drawing;
using System.Windows.Forms;

namespace PaintDark
{
    /// <summary>Floating "Thumbnail" window: the picture at 100% around the area visible in the zoomed main view.</summary>
    public sealed class ThumbnailWindow : Form
    {
        readonly MainForm F;
        readonly Editor E;
        readonly BackBuffer _buf = new BackBuffer();

        public ThumbnailWindow(MainForm f, Editor e)
        {
            F = f; E = e;
            Text = "Thumbnail";
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            var d = new Dpi(Native.DpiOf(f.Handle));
            var cr = f.Canvas.RectangleToScreen(f.Canvas.ClientRectangle);
            Bounds = new Rectangle(cr.Right - d.S(260) - d.S(24), cr.Top + d.S(16), d.S(260), d.S(200));
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
            f.Canvas.Scrolled += Invalidate;
            f.Canvas.ZoomChanged += Invalidate;
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); DarkMode.ApplyToWindow(Handle); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = _buf.Begin(ClientSize, e.ClipRectangle);
            try
            {
                Draw.Fill(g, ClientRectangle, Theme.Workspace);
                var doc = E.Doc;
                if (doc != null)
                {
                    var vp = F.Canvas.ImageViewport;
                    int sx = Math.Max(0, Math.Min(Math.Max(0, doc.Width - ClientSize.Width), vp.X));
                    int sy = Math.Max(0, Math.Min(Math.Max(0, doc.Height - ClientSize.Height), vp.Y));
                    int w = Math.Min(ClientSize.Width, doc.Width - sx), h = Math.Min(ClientSize.Height, doc.Height - sy);
                    IntPtr hdc = g.GetHdc();
                    try { Native.BitBlt(hdc, 0, 0, w, h, doc.Image.DC, sx, sy, Native.SRCCOPY); }
                    finally { g.ReleaseHdc(hdc); }
                }
            }
            finally { _buf.End(g, e.Graphics, e.ClipRectangle); }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            F.Canvas.Scrolled -= Invalidate;
            F.Canvas.ZoomChanged -= Invalidate;
            _buf.Dispose();
            base.OnFormClosed(e);
        }
    }

    /// <summary>F11 view: the picture alone on a black screen. Any key or click returns.</summary>
    public sealed class FullScreenWindow : Form
    {
        readonly Document _doc;

        FullScreenWindow(Form owner, Document doc)
        {
            _doc = doc;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            Bounds = Screen.FromControl(owner).Bounds;
            TopMost = true;
            KeyPreview = true;
            Cursor = Cursors.Default;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        public static void ShowFor(Form owner, Document doc)
        {
            using (var f = new FullScreenWindow(owner, doc)) f.ShowDialog(owner);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.Black);
            int w = _doc.Width, h = _doc.Height;
            double s = Math.Min(1.0, Math.Min(Width / (double)w, Height / (double)h));
            int dw = Math.Max(1, (int)Math.Round(w * s)), dh = Math.Max(1, (int)Math.Round(h * s));
            int x = (Width - dw) / 2, y = (Height - dh) / 2;
            IntPtr hdc = g.GetHdc();
            try
            {
                Native.SetStretchBltMode(hdc, s < 1 ? Native.HALFTONE : Native.COLORONCOLOR);
                Native.SetBrushOrgEx(hdc, 0, 0, IntPtr.Zero);
                Native.StretchBlt(hdc, x, y, dw, dh, _doc.Image.DC, 0, 0, w, h, Native.SRCCOPY);
            }
            finally { g.ReleaseHdc(hdc); }
        }

        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); Close(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Close(); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Close(); }
    }
}
