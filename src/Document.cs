using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace PaintDark
{
    /// <summary>32bpp top-down BGRA GDI DIB section. Pixel memory is directly addressable and GDI can draw into it.</summary>
    public unsafe sealed class Dib : IDisposable
    {
        public readonly int W, H;
        public readonly uint* P;
        public readonly IntPtr HBitmap;
        IntPtr _dc, _oldBmp;
        bool _disposed;

        public Dib(int w, int h)
        {
            if (w < 1 || h < 1) throw new ArgumentException("Bitmaps must be greater than one pixel on a side.");
            if ((long)w * h > 400_000_000L) throw new OutOfMemoryException();
            var bi = new BITMAPINFOHEADER { biSize = 40, biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
            HBitmap = Native.CreateDIBSection(IntPtr.Zero, ref bi, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (HBitmap == IntPtr.Zero || bits == IntPtr.Zero) throw new OutOfMemoryException("There is not enough memory or resources to complete operation.");
            P = (uint*)bits; W = w; H = h;
            GC.AddMemoryPressure((long)w * h * 4);
        }

        public Dib(int w, int h, uint fill) : this(w, h) { Clear(fill); }

        public Rectangle Bounds => new Rectangle(0, 0, W, H);
        public Size Size => new Size(W, H);
        public long Bytes => (long)W * H * 4;

        public IntPtr DC
        {
            get
            {
                if (_dc == IntPtr.Zero)
                {
                    _dc = Native.CreateCompatibleDC(IntPtr.Zero);
                    _oldBmp = Native.SelectObject(_dc, HBitmap);
                }
                return _dc;
            }
        }

        public uint* Row(int y) => P + (long)y * W;
        public uint Get(int x, int y) => P[(long)y * W + x];
        public void Set(int x, int y, uint c) => P[(long)y * W + x] = c;

        public void Clear(uint c)
        {
            long n = (long)W * H;
            uint* p = P;
            if (c == 0) { Fill0(p, n); return; }
            for (long i = 0; i < n; i++) p[i] = c;
        }

        static void Fill0(uint* p, long n)
        {
            ulong* q = (ulong*)p; long m = n / 2;
            for (long i = 0; i < m; i++) q[i] = 0;
            if ((n & 1) != 0) p[n - 1] = 0;
        }

        public void Fill(Rectangle r, uint c)
        {
            r.Intersect(Bounds);
            if (r.Width <= 0 || r.Height <= 0) return;
            for (int y = r.Top; y < r.Bottom; y++)
            {
                uint* row = Row(y) + r.Left;
                for (int x = 0; x < r.Width; x++) row[x] = c;
            }
        }

        public Dib Clone()
        {
            var d = new Dib(W, H);
            Buffer.MemoryCopy(P, d.P, Bytes, Bytes);
            return d;
        }

        public Dib Crop(Rectangle r)
        {
            var d = new Dib(r.Width, r.Height, 0);
            d.CopyFrom(this, r, 0, 0);
            return d;
        }

        /// <summary>Copies srcRect of src into this at (dx,dy), clipping both sides.</summary>
        public void CopyFrom(Dib src, Rectangle srcRect, int dx, int dy)
        {
            int sx = srcRect.X, sy = srcRect.Y, w = srcRect.Width, h = srcRect.Height;
            if (sx < 0) { dx -= sx; w += sx; sx = 0; }
            if (sy < 0) { dy -= sy; h += sy; sy = 0; }
            if (dx < 0) { sx -= dx; w += dx; dx = 0; }
            if (dy < 0) { sy -= dy; h += dy; dy = 0; }
            w = Math.Min(w, Math.Min(src.W - sx, W - dx));
            h = Math.Min(h, Math.Min(src.H - sy, H - dy));
            if (w <= 0 || h <= 0) return;
            long bytes = (long)w * 4;
            for (int y = 0; y < h; y++)
                Buffer.MemoryCopy(src.Row(sy + y) + sx, Row(dy + y) + dx, bytes, bytes);
        }

        public void CopyFrom(Dib src, Rectangle r) => CopyFrom(src, r, r.X, r.Y);

        public bool AnyTransparent(Rectangle r)
        {
            r.Intersect(Bounds);
            for (int y = r.Top; y < r.Bottom; y++)
            {
                uint* row = Row(y);
                for (int x = r.Left; x < r.Right; x++) if (row[x] < 0xFF000000u) return true;
            }
            return false;
        }

        public void MakeOpaque()
        {
            long n = (long)W * H;
            for (long i = 0; i < n; i++) P[i] |= 0xFF000000u;
        }

        public void Dispose() { DisposeCore(); GC.SuppressFinalize(this); }
        ~Dib() { DisposeCore(); }
        void DisposeCore()
        {
            if (_disposed) return;
            _disposed = true;
            if (_dc != IntPtr.Zero) { Native.SelectObject(_dc, _oldBmp); Native.DeleteDC(_dc); _dc = IntPtr.Zero; }
            if (HBitmap != IntPtr.Zero) Native.DeleteObject(HBitmap);
            GC.RemoveMemoryPressure((long)W * H * 4);
        }
    }

    /// <summary>Run-length codec for undo snapshots. Paint-style content (flat fills, line art) shrinks a lot; photos fall back to raw.</summary>
    public static unsafe class PixelPack
    {
        public static uint[] Pack(Dib src, Rectangle r, out bool compressed)
        {
            int w = r.Width, h = r.Height;
            long n = (long)w * h;
            var raw = new uint[n];
            fixed (uint* dst = raw)
            {
                long bytes = (long)w * 4;
                for (int y = 0; y < h; y++)
                    Buffer.MemoryCopy(src.Row(r.Y + y) + r.X, dst + (long)y * w, bytes, bytes);
            }
            var packed = Rle(raw);
            if (packed != null && packed.Length < n * 3 / 4) { compressed = true; return packed; }
            compressed = false;
            return raw;
        }

        public static void Unpack(uint[] data, bool compressed, Dib dst, Rectangle r)
        {
            int w = r.Width, h = r.Height;
            uint[] raw = compressed ? UnRle(data, (long)w * h) : data;
            fixed (uint* s = raw)
            {
                long bytes = (long)w * 4;
                for (int y = 0; y < h; y++)
                    Buffer.MemoryCopy(s + (long)y * w, dst.Row(r.Y + y) + r.X, bytes, bytes);
            }
        }

        static uint[] Rle(uint[] a)
        {
            long n = a.LongLength;
            long cap = Math.Max(64, n / 2);
            var outBuf = new uint[cap];
            long o = 0, i = 0;
            while (i < n)
            {
                long j = i + 1;
                uint v = a[i];
                while (j < n && a[j] == v && j - i < 0x7FFFFFFF) j++;
                long run = j - i;
                if (run >= 3)
                {
                    if (o + 2 > cap) return null;
                    outBuf[o++] = 0x80000000u | (uint)run;
                    outBuf[o++] = v;
                    i = j;
                    continue;
                }
                // literal block: until a run of 3 begins
                long start = i;
                long k = i;
                while (k < n && k - start < 0x7FFFFFFF)
                {
                    if (k + 2 < n && a[k] == a[k + 1] && a[k] == a[k + 2]) break;
                    k++;
                }
                long len = k - start;
                if (o + 1 + len > cap) return null;
                outBuf[o++] = (uint)len;
                Array.Copy(a, start, outBuf, o, len);
                o += len;
                i = k;
            }
            var res = new uint[o];
            Array.Copy(outBuf, res, o);
            return res;
        }

        static uint[] UnRle(uint[] d, long n)
        {
            var a = new uint[n];
            long o = 0, i = 0;
            while (i < d.LongLength && o < n)
            {
                uint h = d[i++];
                if ((h & 0x80000000u) != 0)
                {
                    long run = h & 0x7FFFFFFF;
                    uint v = d[i++];
                    for (long k = 0; k < run && o < n; k++) a[o++] = v;
                }
                else
                {
                    long len = h;
                    Array.Copy(d, i, a, o, Math.Min(len, n - o));
                    i += len; o += len;
                }
            }
            return a;
        }
    }

    public sealed class UndoEntry
    {
        public string Name;
        public bool Full;
        public Rectangle Rect;
        public int W, H;
        public uint[] Data;
        public bool Compressed;
        public bool HasAlpha;
        public int StateBefore, StateAfter;
        public long Bytes => Data == null ? 0 : Data.LongLength * 4;
    }

    /// <summary>
    /// The picture. Image holds live pixels; Shadow holds the last committed state. Tools draw straight into Image,
    /// then Commit() diffs the touched area against Shadow to build a compact undo record.
    /// </summary>
    public unsafe sealed class Document : IDisposable
    {
        public Dib Image { get; private set; }
        public Dib Shadow { get; private set; }
        public bool HasAlpha;
        public string FilePath;
        public string FormatName;          // png, jpeg, bmp, gif, tiff ...
        public int BmpBits = 24;           // for BMP saves
        public DateTime? LastSaved;
        public long FileSize = -1;
        public double DpiX = 96, DpiY = 96;

        readonly List<UndoEntry> _undo = new List<UndoEntry>();
        readonly List<UndoEntry> _redo = new List<UndoEntry>();
        int _state, _savedState, _stateCounter;
        public const int MaxUndo = 50;
        static long _maxBytes = -1;

        public int Width => Image.W;
        public int Height => Image.H;
        public Size Size => new Size(Image.W, Image.H);
        public Rectangle Bounds => new Rectangle(0, 0, Image.W, Image.H);
        public bool Dirty => _state != _savedState;
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public string Title => string.IsNullOrEmpty(FilePath) ? "Untitled" : System.IO.Path.GetFileName(FilePath);

        public Document(int w, int h, uint fill)
        {
            Image = new Dib(w, h, fill);
            Shadow = Image.Clone();
        }

        public Document(Dib img)
        {
            Image = img;
            Shadow = img.Clone();
        }

        static long MaxBytes
        {
            get
            {
                if (_maxBytes < 0)
                {
                    long cap = 768L << 20;
                    try
                    {
                        var m = new Native.MEMORYSTATUSEX { dwLength = Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
                        if (Native.GlobalMemoryStatusEx(ref m)) cap = (long)Math.Min(Math.Max(m.ullTotalPhys / 8, 256UL << 20), 2048UL << 20);
                    }
                    catch { }
                    if (!Environment.Is64BitProcess) cap = Math.Min(cap, 384L << 20);
                    _maxBytes = cap;
                }
                return _maxBytes;
            }
        }

        public void MarkSaved() { _savedState = _state; }
        public void MarkDirty() { _state = ++_stateCounter; }

        /// <summary>Restore Image from Shadow inside r (cancels an uncommitted stroke).</summary>
        public void Revert(Rectangle r)
        {
            r.Intersect(Bounds);
            if (r.Width > 0 && r.Height > 0) Image.CopyFrom(Shadow, r);
        }

        public void RevertAll() => Image.CopyFrom(Shadow, Bounds);

        /// <summary>Record changes made to Image (inside hint) as one undo step. Returns the actual changed rect.</summary>
        public Rectangle Commit(Rectangle hint, string name)
        {
            hint.Intersect(Bounds);
            if (hint.Width <= 0 || hint.Height <= 0) return Rectangle.Empty;
            var r = DiffRect(hint);
            if (r.IsEmpty) return r;
            bool comp;
            var data = PixelPack.Pack(Shadow, r, out comp);
            var e = new UndoEntry { Name = name, Rect = r, Data = data, Compressed = comp, StateBefore = _state };
            Shadow.CopyFrom(Image, r);
            _state = ++_stateCounter;
            e.StateAfter = _state;
            Push(e);
            if (!HasAlpha) { } else HasAlpha = Image.AnyTransparent(Bounds);
            return r;
        }

        public Rectangle CommitAll(string name) => Commit(Bounds, name);

        /// <summary>Replace the whole picture (size may change). The old picture becomes one undo step.</summary>
        public void ReplaceImage(Dib img, string name, bool? hasAlpha = null)
        {
            bool comp;
            var data = PixelPack.Pack(Shadow, Shadow.Bounds, out comp);
            var e = new UndoEntry { Name = name, Full = true, W = Shadow.W, H = Shadow.H, Data = data, Compressed = comp, HasAlpha = HasAlpha, StateBefore = _state };
            Image.Dispose(); Shadow.Dispose();
            Image = img;
            Shadow = img.Clone();
            HasAlpha = hasAlpha ?? img.AnyTransparent(img.Bounds);
            _state = ++_stateCounter;
            e.StateAfter = _state;
            Push(e);
        }

        void Push(UndoEntry e)
        {
            _redo.Clear();
            _undo.Add(e);
            long total = 0;
            foreach (var u in _undo) total += u.Bytes;
            while (_undo.Count > 1 && (_undo.Count > MaxUndo || total > MaxBytes))
            {
                total -= _undo[0].Bytes;
                _undo.RemoveAt(0);
            }
        }

        public string UndoName => _undo.Count > 0 ? _undo[_undo.Count - 1].Name : null;
        public string RedoName => _redo.Count > 0 ? _redo[_redo.Count - 1].Name : null;

        /// <summary>Returns the affected rect, or Rectangle.Empty (size changed => caller refreshes everything).</summary>
        public bool Undo(out Rectangle affected, out bool sizeChanged)
        {
            affected = Rectangle.Empty; sizeChanged = false;
            if (_undo.Count == 0) return false;
            var e = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            Swap(e, out affected, out sizeChanged);
            _state = e.StateBefore;
            _redo.Add(e);
            return true;
        }

        public bool Redo(out Rectangle affected, out bool sizeChanged)
        {
            affected = Rectangle.Empty; sizeChanged = false;
            if (_redo.Count == 0) return false;
            var e = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            Swap(e, out affected, out sizeChanged);
            _state = e.StateAfter;
            _undo.Add(e);
            return true;
        }

        void Swap(UndoEntry e, out Rectangle affected, out bool sizeChanged)
        {
            if (e.Full)
            {
                bool comp;
                var cur = PixelPack.Pack(Shadow, Shadow.Bounds, out comp);
                int cw = Shadow.W, ch = Shadow.H; bool ca = HasAlpha;
                var img = new Dib(e.W, e.H);
                PixelPack.Unpack(e.Data, e.Compressed, img, img.Bounds);
                sizeChanged = cw != e.W || ch != e.H;
                Image.Dispose(); Shadow.Dispose();
                Image = img; Shadow = img.Clone();
                HasAlpha = e.HasAlpha;
                e.Data = cur; e.Compressed = comp; e.W = cw; e.H = ch; e.HasAlpha = ca;
                affected = Bounds;
            }
            else
            {
                bool comp;
                var cur = PixelPack.Pack(Shadow, e.Rect, out comp);
                PixelPack.Unpack(e.Data, e.Compressed, Shadow, e.Rect);
                Image.CopyFrom(Shadow, e.Rect);
                e.Data = cur; e.Compressed = comp;
                affected = e.Rect;
                sizeChanged = false;
                HasAlpha = Image.AnyTransparent(Bounds);
            }
        }

        /// <summary>Smallest rect inside hint where Image differs from Shadow.</summary>
        Rectangle DiffRect(Rectangle hint)
        {
            int w = hint.Width;
            UIntPtr rowBytes = (UIntPtr)(uint)(w * 4);
            int y0 = -1, y1 = -1;
            for (int y = hint.Top; y < hint.Bottom; y++)
                if (Native.memcmp(Image.Row(y) + hint.Left, Shadow.Row(y) + hint.Left, rowBytes) != 0) { y0 = y; break; }
            if (y0 < 0) return Rectangle.Empty;
            for (int y = hint.Bottom - 1; y >= y0; y--)
                if (Native.memcmp(Image.Row(y) + hint.Left, Shadow.Row(y) + hint.Left, rowBytes) != 0) { y1 = y; break; }
            int x0 = hint.Right, x1 = hint.Left - 1;
            for (int y = y0; y <= y1; y++)
            {
                uint* a = Image.Row(y), b = Shadow.Row(y);
                for (int x = hint.Left; x < x0; x++) if (a[x] != b[x]) { x0 = x; break; }
                for (int x = hint.Right - 1; x > x1; x--) if (a[x] != b[x]) { x1 = x; break; }
            }
            if (x1 < x0) return Rectangle.Empty;
            return Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
        }

        public void Dispose()
        {
            Image?.Dispose(); Shadow?.Dispose();
            _undo.Clear(); _redo.Clear();
        }
    }
}
