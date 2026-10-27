using System;
using System.Drawing;
using System.Windows.Forms;

namespace PaintDark
{
    /// <summary>
    /// Persistent GDI back buffer. Painting goes into a DIB the size of the control and only the
    /// invalid rectangle is blitted to the screen, so there is never any flicker.
    /// </summary>
    public sealed class BackBuffer : IDisposable
    {
        Dib _dib;
        public Dib Dib => _dib;

        public Graphics Begin(Size size, Rectangle clip)
        {
            int w = Math.Max(1, size.Width), h = Math.Max(1, size.Height);
            if (_dib == null || _dib.W < w || _dib.H < h || _dib.W > w * 2 + 256 || _dib.H > h * 2 + 256)
            {
                _dib?.Dispose();
                _dib = new Dib(w + 64, h + 64);
            }
            var g = Graphics.FromHdc(_dib.DC);
            g.SetClip(clip);
            return g;
        }

        public void End(Graphics g, Graphics target, Rectangle clip)
        {
            g.Dispose();
            Native.GdiFlush();
            IntPtr hdc = target.GetHdc();
            try { Native.BitBlt(hdc, clip.X, clip.Y, clip.Width, clip.Height, _dib.DC, clip.X, clip.Y, Native.SRCCOPY); }
            finally { target.ReleaseHdc(hdc); }
        }

        public void Dispose() { _dib?.Dispose(); _dib = null; }
    }

    /// <summary>Base for custom drawn controls: opaque, flicker free, DPI aware.</summary>
    public class DarkControl : Control
    {
        readonly BackBuffer _buf = new BackBuffer();
        protected Dpi D = new Dpi(96);

        public DarkControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.DoubleBuffer | ControlStyles.SupportsTransparentBackColor, false);
        }

        public int DpiValue => D.Value;
        public virtual void SetDpi(int dpi) { D = new Dpi(dpi); OnDpiApplied(); Invalidate(); }
        protected virtual void OnDpiApplied() { }

        protected override void OnPaint(PaintEventArgs e)
        {
            var clip = e.ClipRectangle;
            if (clip.Width <= 0 || clip.Height <= 0) return;
            var g = _buf.Begin(ClientSize, clip);
            try { Render(g, clip); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            _buf.End(g, e.Graphics, clip);
        }

        protected virtual void Render(Graphics g, Rectangle clip) { }
        protected Dib BufferDib => _buf.Dib;

        protected override void OnPaintBackground(PaintEventArgs pevent) { }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _buf.Dispose();
            base.Dispose(disposing);
        }
    }
}
