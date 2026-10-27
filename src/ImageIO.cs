using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PaintDark
{
    public sealed class LoadedImage
    {
        public Dib Image;
        public bool HasAlpha;
        public double DpiX = 96, DpiY = 96;
        public string Format = "png";
    }

    public static unsafe class ImageIO
    {
        public const string OpenFilter =
            "All Picture Files|*.bmp;*.dib;*.jpg;*.jpeg;*.jpe;*.jfif;*.gif;*.tif;*.tiff;*.png;*.ico;*.heic;*.heif;*.webp;*.jxr;*.wdp;*.dds|" +
            "Bitmap Files (*.bmp;*.dib)|*.bmp;*.dib|JPEG (*.jpg;*.jpeg;*.jpe;*.jfif)|*.jpg;*.jpeg;*.jpe;*.jfif|GIF (*.gif)|*.gif|" +
            "TIFF (*.tif;*.tiff)|*.tif;*.tiff|PNG (*.png)|*.png|ICO (*.ico)|*.ico|HEIC (*.heic)|*.heic;*.heif|WEBP (*.webp)|*.webp|All Files (*.*)|*.*";

        public static readonly (string Name, string Filter, string Ext, string Format, int Bits)[] SaveTypes =
        {
            ("Monochrome Bitmap (*.bmp;*.dib)", "*.bmp;*.dib", ".bmp", "bmp", 1),
            ("16 Color Bitmap (*.bmp;*.dib)", "*.bmp;*.dib", ".bmp", "bmp", 4),
            ("256 Color Bitmap (*.bmp;*.dib)", "*.bmp;*.dib", ".bmp", "bmp", 8),
            ("24-bit Bitmap (*.bmp;*.dib)", "*.bmp;*.dib", ".bmp", "bmp", 24),
            ("JPEG (*.jpg;*.jpeg;*.jpe;*.jfif)", "*.jpg;*.jpeg;*.jpe;*.jfif", ".jpg", "jpeg", 24),
            ("GIF (*.gif)", "*.gif", ".gif", "gif", 8),
            ("TIFF (*.tif;*.tiff)", "*.tif;*.tiff", ".tif", "tiff", 32),
            ("PNG (*.png)", "*.png", ".png", "png", 32),
        };

        public static string FormatFromExtension(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".bmp": case ".dib": case ".rle": return "bmp";
                case ".jpg": case ".jpeg": case ".jpe": case ".jfif": return "jpeg";
                case ".gif": return "gif";
                case ".tif": case ".tiff": return "tiff";
                case ".png": return "png";
                case ".ico": return "ico";
                case ".heic": case ".heif": return "heic";
                case ".webp": return "webp";
                default: return null;
            }
        }

        // ================================================================== load
        public static LoadedImage Load(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            try { return LoadGdiPlus(bytes); }
            catch (Exception) when (!(LooksLikeGdiPlusFormat(bytes)))
            {
                return WicLoader.Load(path);
            }
        }

        public static LoadedImage LoadFromBytes(byte[] bytes)
        {
            return LoadGdiPlus(bytes);
        }

        static bool LooksLikeGdiPlusFormat(byte[] b)
        {
            if (b.Length < 4) return false;
            if (b[0] == 'B' && b[1] == 'M') return true;
            if (b[0] == 0xFF && b[1] == 0xD8) return true;
            if (b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G') return true;
            if (b[0] == 'G' && b[1] == 'I' && b[2] == 'F') return true;
            if ((b[0] == 'I' && b[1] == 'I') || (b[0] == 'M' && b[1] == 'M')) return true;
            return false;
        }

        static LoadedImage LoadGdiPlus(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes))
            using (var src = new Bitmap(ms))
            {
                ApplyExifOrientation(src);
                var res = FromBitmap(src);
                res.Format = FormatName(src.RawFormat);
                return res;
            }
        }

        static string FormatName(ImageFormat f)
        {
            if (f.Equals(ImageFormat.Bmp) || f.Equals(ImageFormat.MemoryBmp)) return "bmp";
            if (f.Equals(ImageFormat.Jpeg)) return "jpeg";
            if (f.Equals(ImageFormat.Gif)) return "gif";
            if (f.Equals(ImageFormat.Tiff)) return "tiff";
            if (f.Equals(ImageFormat.Icon)) return "ico";
            return "png";
        }

        static void ApplyExifOrientation(Bitmap b)
        {
            try
            {
                if (Array.IndexOf(b.PropertyIdList, 0x0112) < 0) return;
                var p = b.GetPropertyItem(0x0112);
                int o = BitConverter.ToUInt16(p.Value, 0);
                RotateFlipType t;
                switch (o)
                {
                    case 2: t = RotateFlipType.RotateNoneFlipX; break;
                    case 3: t = RotateFlipType.Rotate180FlipNone; break;
                    case 4: t = RotateFlipType.Rotate180FlipX; break;
                    case 5: t = RotateFlipType.Rotate90FlipX; break;
                    case 6: t = RotateFlipType.Rotate90FlipNone; break;
                    case 7: t = RotateFlipType.Rotate270FlipX; break;
                    case 8: t = RotateFlipType.Rotate270FlipNone; break;
                    default: return;
                }
                b.RotateFlip(t);
            }
            catch { }
        }

        public static LoadedImage FromBitmap(Bitmap src)
        {
            int w = src.Width, h = src.Height;
            var dib = new Dib(w, h);
            var data = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++)
                    Buffer.MemoryCopy((byte*)data.Scan0 + (long)y * data.Stride, dib.Row(y), w * 4L, w * 4L);
            }
            finally { src.UnlockBits(data); }
            bool alphaFormat = Image.IsAlphaPixelFormat(src.PixelFormat) || (src.PixelFormat & PixelFormat.Indexed) != 0;
            bool hasAlpha = alphaFormat && dib.AnyTransparent(dib.Bounds);
            if (!hasAlpha) dib.MakeOpaque();
            return new LoadedImage { Image = dib, HasAlpha = hasAlpha, DpiX = src.HorizontalResolution > 1 ? src.HorizontalResolution : 96, DpiY = src.VerticalResolution > 1 ? src.VerticalResolution : 96 };
        }

        // ================================================================== save
        /// <summary>Writes the picture atomically (temp file + replace). Throws on failure.</summary>
        public static void Save(Dib img, bool hasAlpha, string path, string format, int bmpBits, double dpi)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            string tmp = Path.Combine(dir, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            try
            {
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    Encode(img, hasAlpha, fs, format, bmpBits, dpi);
                if (File.Exists(path))
                {
                    var attr = File.GetAttributes(path);
                    if ((attr & FileAttributes.ReadOnly) != 0) throw new UnauthorizedAccessException("This file is read-only.\nTo save your changes, use a different file name.");
                    try { File.Replace(tmp, path, null, true); }
                    catch (IOException) { File.Copy(tmp, path, true); File.Delete(tmp); }
                }
                else File.Move(tmp, path);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }

        public static void Encode(Dib img, bool hasAlpha, Stream s, string format, int bmpBits, double dpi)
        {
            int w = img.W, h = img.H;
            using (var shared = new Bitmap(w, h, w * 4, PixelFormat.Format32bppArgb, (IntPtr)img.P))
            {
                switch (format)
                {
                    case "png":
                        if (hasAlpha) { using (var b = shared.Clone(new Rectangle(0, 0, w, h), PixelFormat.Format32bppArgb)) { b.SetResolution((float)dpi, (float)dpi); b.Save(s, ImageFormat.Png); } }
                        else using (var b = Flatten(img, PixelFormat.Format24bppRgb)) { b.SetResolution((float)dpi, (float)dpi); b.Save(s, ImageFormat.Png); }
                        break;
                    case "tiff":
                        if (hasAlpha) { using (var b = shared.Clone(new Rectangle(0, 0, w, h), PixelFormat.Format32bppArgb)) { b.SetResolution((float)dpi, (float)dpi); b.Save(s, ImageFormat.Tiff); } }
                        else using (var b = Flatten(img, PixelFormat.Format24bppRgb)) { b.SetResolution((float)dpi, (float)dpi); SaveTiff(b, s); }
                        break;
                    case "jpeg":
                        using (var b = Flatten(img, PixelFormat.Format24bppRgb))
                        {
                            b.SetResolution((float)dpi, (float)dpi);
                            var enc = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                            using (var ep = new EncoderParameters(1))
                            {
                                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
                                b.Save(s, enc, ep);
                            }
                        }
                        break;
                    case "gif":
                        using (var b = ToIndexed(img, 8, false)) b.Save(s, ImageFormat.Gif);
                        break;
                    default: // bmp
                        if (bmpBits >= 24) using (var b = Flatten(img, PixelFormat.Format24bppRgb)) { b.SetResolution((float)dpi, (float)dpi); b.Save(s, ImageFormat.Bmp); }
                        else using (var b = ToIndexed(img, bmpBits, true)) { b.SetResolution((float)dpi, (float)dpi); b.Save(s, ImageFormat.Bmp); }
                        break;
                }
            }
        }

        static void SaveTiff(Bitmap b, Stream s)
        {
            var enc = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Tiff.Guid);
            using (var ep = new EncoderParameters(1))
            {
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Compression, (long)EncoderValue.CompressionLZW);
                b.Save(s, enc, ep);
            }
        }

        /// <summary>Opaque copy (transparent pixels composited on white, like Paint does).</summary>
        public static Bitmap Flatten(Dib img, PixelFormat fmt)
        {
            int w = img.W, h = img.H;
            var b = new Bitmap(w, h, fmt);
            var data = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, fmt);
            try
            {
                for (int y = 0; y < h; y++)
                {
                    uint* s = img.Row(y);
                    byte* d = (byte*)data.Scan0 + (long)y * data.Stride;
                    for (int x = 0; x < w; x++)
                    {
                        uint c = s[x];
                        if (c < 0xFF000000u) c = Px.Blend(0xFFFFFFFFu, c, (int)(c >> 24));
                        if (fmt == PixelFormat.Format24bppRgb) { d[0] = (byte)c; d[1] = (byte)(c >> 8); d[2] = (byte)(c >> 16); d += 3; }
                        else { *(uint*)d = c | 0xFF000000u; d += 4; }
                    }
                }
            }
            finally { b.UnlockBits(data); }
            return b;
        }

        static readonly uint[] Vga16 =
        {
            0xFF000000, 0xFF800000, 0xFF008000, 0xFF808000, 0xFF000080, 0xFF800080, 0xFF008080, 0xFFC0C0C0,
            0xFF808080, 0xFFFF0000, 0xFF00FF00, 0xFFFFFF00, 0xFF0000FF, 0xFFFF00FF, 0xFF00FFFF, 0xFFFFFFFF,
        };

        /// <summary>Indexed bitmap (1, 4 or 8 bpp). 8-bit uses the exact colors when possible, otherwise an octree palette.</summary>
        static Bitmap ToIndexed(Dib img, int bits, bool forBmp)
        {
            int w = img.W, h = img.H;
            var fmt = bits == 1 ? PixelFormat.Format1bppIndexed : bits == 4 ? PixelFormat.Format4bppIndexed : PixelFormat.Format8bppIndexed;
            uint[] pal;
            if (bits == 1) pal = new uint[] { 0xFF000000, 0xFFFFFFFF };
            else if (bits == 4) pal = Vga16;
            else pal = BuildPalette(img, 256);
            var b = new Bitmap(w, h, fmt);
            var cp = b.Palette;
            for (int i = 0; i < cp.Entries.Length; i++) cp.Entries[i] = i < pal.Length ? Color.FromArgb((int)pal[i]) : Color.Black;
            b.Palette = cp;
            var map = new Dictionary<uint, byte>();
            var data = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, fmt);
            try
            {
                for (int y = 0; y < h; y++)
                {
                    uint* s = img.Row(y);
                    byte* d = (byte*)data.Scan0 + (long)y * data.Stride;
                    for (int x = 0; x < w; x++)
                    {
                        uint c = s[x];
                        if (c < 0xFF000000u) c = Px.Blend(0xFFFFFFFFu, c, (int)(c >> 24));
                        c |= 0xFF000000u;
                        byte idx;
                        if (bits == 1) idx = (byte)(Px.Luma(c) >= 128 ? 1 : 0);
                        else if (!map.TryGetValue(c, out idx)) { idx = Nearest(pal, c); if (map.Count < 1 << 20) map[c] = idx; }
                        if (bits == 8) d[x] = idx;
                        else if (bits == 4) { if ((x & 1) == 0) d[x >> 1] = (byte)(idx << 4); else d[x >> 1] |= idx; }
                        else { if ((x & 7) == 0) d[x >> 3] = 0; if (idx != 0) d[x >> 3] |= (byte)(0x80 >> (x & 7)); }
                    }
                }
            }
            finally { b.UnlockBits(data); }
            return b;
        }

        static byte Nearest(uint[] pal, uint c)
        {
            int r = Px.R(c), g = Px.G(c), bl = Px.B(c);
            int best = 0, bd = int.MaxValue;
            for (int i = 0; i < pal.Length; i++)
            {
                int dr = r - Px.R(pal[i]), dg = g - Px.G(pal[i]), db = bl - Px.B(pal[i]);
                int dd = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                if (dd < bd) { bd = dd; best = i; if (dd == 0) break; }
            }
            return (byte)best;
        }

        static uint[] BuildPalette(Dib img, int max)
        {
            var set = new HashSet<uint>();
            long n = (long)img.W * img.H;
            for (long i = 0; i < n; i++)
            {
                uint c = img.P[i];
                if (c < 0xFF000000u) c = Px.Blend(0xFFFFFFFFu, c, (int)(c >> 24));
                set.Add(c | 0xFF000000u);
                if (set.Count > max) break;
            }
            if (set.Count <= max) return set.ToArray();
            return Octree.Build(img, max);
        }

        /// <summary>Classic octree color quantizer.</summary>
        sealed class Octree
        {
            sealed class Node { public long R, G, B, Count; public Node[] Kids; public bool Leaf; public Node NextReducible; }
            readonly Node _root = new Node();
            readonly Node[] _reducible = new Node[8];
            int _leaves;

            public static uint[] Build(Dib img, int max)
            {
                var o = new Octree();
                long n = (long)img.W * img.H;
                long step = Math.Max(1, n / 2000000);
                for (long i = 0; i < n; i += step)
                {
                    uint c = img.P[i];
                    o.Add(c, max);
                }
                var list = new List<uint>();
                o.Collect(o._root, list);
                return list.Take(max).ToArray();
            }

            void Add(uint c, int max)
            {
                int r = Px.R(c), g = Px.G(c), b = Px.B(c);
                var node = _root;
                for (int level = 0; level < 8; level++)
                {
                    if (node.Leaf) break;
                    int shift = 7 - level;
                    int idx = ((r >> shift) & 1) << 2 | ((g >> shift) & 1) << 1 | ((b >> shift) & 1);
                    if (node.Kids == null) node.Kids = new Node[8];
                    if (node.Kids[idx] == null)
                    {
                        var k = new Node();
                        if (level == 7) { k.Leaf = true; _leaves++; }
                        else { k.NextReducible = _reducible[level]; _reducible[level] = k; }
                        node.Kids[idx] = k;
                    }
                    node = node.Kids[idx];
                }
                node.R += r; node.G += g; node.B += b; node.Count++;
                while (_leaves > max) Reduce();
            }

            void Reduce()
            {
                int level = 6;
                while (level > 0 && _reducible[level] == null) level--;
                var node = _reducible[level];
                if (node == null) return;
                _reducible[level] = node.NextReducible;
                int removed = 0;
                foreach (var k in node.Kids)
                {
                    if (k == null) continue;
                    Merge(k, node);
                    removed++;
                }
                node.Kids = null;
                node.Leaf = true;
                _leaves -= removed - 1;
            }

            static void Merge(Node k, Node into)
            {
                if (k.Kids != null) foreach (var kk in k.Kids) if (kk != null) Merge(kk, k);
                into.R += k.R; into.G += k.G; into.B += k.B; into.Count += k.Count;
            }

            void Collect(Node n, List<uint> list)
            {
                if (n.Leaf || n.Kids == null)
                {
                    if (n.Count > 0) list.Add(Px.Rgb((int)(n.R / n.Count), (int)(n.G / n.Count), (int)(n.B / n.Count)));
                    return;
                }
                foreach (var k in n.Kids) if (k != null) Collect(k, list);
            }
        }

        // ================================================================== clipboard
        public static void SetClipboard(Dib img, bool hasAlpha)
        {
            var data = new DataObject();
            using (var b24 = Flatten(img, PixelFormat.Format24bppRgb))
            {
                data.SetData(DataFormats.Dib, false, DibStream(b24));
            }
            var png = new MemoryStream();
            Encode(img, hasAlpha, png, "png", 32, 96);
            png.Position = 0;
            data.SetData("PNG", false, png);
            for (int i = 0; i < 6; i++)
            {
                try { Clipboard.SetDataObject(data, true, 4, 60); return; }
                catch (ExternalException) { System.Threading.Thread.Sleep(50); }
            }
        }

        static MemoryStream DibStream(Bitmap b24)
        {
            int w = b24.Width, h = b24.Height;
            int stride = (w * 3 + 3) & ~3;
            var ms = new MemoryStream(40 + stride * h);
            var bw = new BinaryWriter(ms);
            bw.Write(40); bw.Write(w); bw.Write(h); bw.Write((short)1); bw.Write((short)24); bw.Write(0); bw.Write(stride * h);
            bw.Write(3780); bw.Write(3780); bw.Write(0); bw.Write(0);
            var data = b24.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                var row = new byte[stride];
                for (int y = h - 1; y >= 0; y--)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, w * 3);
                    bw.Write(row);
                }
            }
            finally { b24.UnlockBits(data); }
            bw.Flush();
            ms.Position = 0;
            return ms;
        }

        public static bool ClipboardHasImage()
        {
            try
            {
                return Native.IsClipboardFormatAvailable(8 /*CF_DIB*/) || Native.IsClipboardFormatAvailable(2 /*CF_BITMAP*/) || Native.IsClipboardFormatAvailable(17 /*CF_DIBV5*/)
                    || Native.IsClipboardFormatAvailable(Native.RegisterClipboardFormat("PNG")) || Native.IsClipboardFormatAvailable(15 /*CF_HDROP*/);
            }
            catch { return false; }
        }

        public static bool ClipboardHasText()
        {
            try { return Native.IsClipboardFormatAvailable(13) || Native.IsClipboardFormatAvailable(1); } catch { return false; }
        }

        public static LoadedImage GetClipboardImage()
        {
            IDataObject d = null;
            for (int i = 0; i < 5 && d == null; i++)
            {
                try { d = Clipboard.GetDataObject(); }
                catch (ExternalException) { System.Threading.Thread.Sleep(40); }
            }
            if (d == null) return null;
            try
            {
                if (d.GetDataPresent("PNG") && d.GetData("PNG") is MemoryStream ms)
                {
                    using (var b = new Bitmap(ms)) return FromBitmap(b);
                }
            }
            catch { }
            try
            {
                if (d.GetDataPresent(DataFormats.Dib) && d.GetData(DataFormats.Dib) is MemoryStream dib)
                {
                    var r = FromDibStream(dib);
                    if (r != null) return r;
                }
            }
            catch { }
            try
            {
                if (d.GetDataPresent(DataFormats.Bitmap) && d.GetData(DataFormats.Bitmap) is Bitmap bmp)
                    using (bmp) { var r = FromBitmap(bmp); r.Image.MakeOpaque(); r.HasAlpha = false; return r; }
            }
            catch { }
            try
            {
                if (d.GetDataPresent(DataFormats.FileDrop) && d.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0 && File.Exists(files[0]))
                    return Load(files[0]);
            }
            catch { }
            return null;
        }

        /// <summary>CF_DIB parser (BI_RGB / BI_BITFIELDS, 1-32 bpp). Alpha in 32bpp DIBs is ignored like Paint does.</summary>
        static LoadedImage FromDibStream(MemoryStream ms)
        {
            var buf = ms.ToArray();
            if (buf.Length < 40) return null;
            int hdr = BitConverter.ToInt32(buf, 0);
            int w = BitConverter.ToInt32(buf, 4), h = BitConverter.ToInt32(buf, 8);
            int bpp = BitConverter.ToInt16(buf, 14), comp = BitConverter.ToInt32(buf, 16), clrUsed = BitConverter.ToInt32(buf, 32);
            if (w <= 0 || h == 0 || hdr < 40) return null;
            if (bpp != 32 && bpp != 24) return null; // let GDI+ handle the rare palette formats via CF_BITMAP
            bool bottomUp = h > 0; h = Math.Abs(h);
            int off = hdr;
            uint rm = 0x00FF0000, gm = 0x0000FF00, bm = 0x000000FF;
            if (comp == 3 && hdr == 40) { rm = BitConverter.ToUInt32(buf, 40); gm = BitConverter.ToUInt32(buf, 44); bm = BitConverter.ToUInt32(buf, 48); off += 12; }
            else if (comp == 3) { rm = BitConverter.ToUInt32(buf, 40); gm = BitConverter.ToUInt32(buf, 44); bm = BitConverter.ToUInt32(buf, 48); }
            else if (comp != 0) return null;
            off += clrUsed * 4;
            int stride = ((w * bpp + 31) / 32) * 4;
            if (off + (long)stride * h > buf.Length) return null;
            var dib = new Dib(w, h);
            int rs = Shift(rm), gs = Shift(gm), bs = Shift(bm);
            fixed (byte* p = buf)
            {
                for (int y = 0; y < h; y++)
                {
                    byte* row = p + off + (long)(bottomUp ? h - 1 - y : y) * stride;
                    uint* d = dib.Row(y);
                    for (int x = 0; x < w; x++)
                    {
                        if (bpp == 24) d[x] = 0xFF000000u | ((uint)row[x * 3 + 2] << 16) | ((uint)row[x * 3 + 1] << 8) | row[x * 3];
                        else
                        {
                            uint v = *(uint*)(row + x * 4);
                            d[x] = 0xFF000000u | (((v & rm) >> rs) << 16) | (((v & gm) >> gs) << 8) | ((v & bm) >> bs);
                        }
                    }
                }
            }
            return new LoadedImage { Image = dib, HasAlpha = false };
        }

        static int Shift(uint mask)
        {
            if (mask == 0) return 0;
            int s = 0;
            while ((mask & 1) == 0) { mask >>= 1; s++; }
            // normalize masks wider than 8 bits
            int bits = 0; while ((mask & 1) != 0) { mask >>= 1; bits++; }
            return s + Math.Max(0, bits - 8);
        }
    }

    /// <summary>Windows Imaging Component fallback (HEIC/WebP/JXR/DDS... whatever codecs are installed). Kept in its own class so WPF only loads when needed.</summary>
    static class WicLoader
    {
        public static LoadedImage Load(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                var dec = System.Windows.Media.Imaging.BitmapDecoder.Create(fs, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                System.Windows.Media.Imaging.BitmapSource frame = dec.Frames[0];
                var conv = new System.Windows.Media.Imaging.FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = conv.PixelHeight;
                var dib = new Dib(w, h);
                unsafe { conv.CopyPixels(new System.Windows.Int32Rect(0, 0, w, h), (IntPtr)dib.P, w * h * 4, w * 4); }
                bool alpha = dib.AnyTransparent(dib.Bounds);
                return new LoadedImage { Image = dib, HasAlpha = alpha, DpiX = frame.DpiX > 1 ? frame.DpiX : 96, DpiY = frame.DpiY > 1 ? frame.DpiY : 96, Format = ImageIO.FormatFromExtension(path) ?? "png" };
            }
        }
    }
}
