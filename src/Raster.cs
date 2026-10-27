using System;
using System.Drawing;
using System.Runtime.CompilerServices;

namespace PaintDark
{
    /// <summary>Pixel math. Colors are 0xAARRGGBB (same memory layout as the BGRA DIB).</summary>
    public static unsafe class Px
    {
        public static uint Rgb(int r, int g, int b) => 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
        public static uint FromColor(Color c) => (uint)c.ToArgb();
        public static Color ToColor(uint c) => Color.FromArgb((int)(c | 0xFF000000u));
        public static int R(uint c) => (int)(c >> 16) & 255;
        public static int G(uint c) => (int)(c >> 8) & 255;
        public static int B(uint c) => (int)c & 255;
        public static int A(uint c) => (int)(c >> 24);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int Div255(int x) => (x + 1 + (x >> 8)) >> 8;

        /// <summary>Paint an opaque color over dst with coverage a (0..255).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Blend(uint dst, uint src, int a)
        {
            if (a <= 0) return dst;
            if (a >= 255) return src | 0xFF000000u;
            uint da = dst >> 24;
            int ia = 255 - a;
            if (da == 255)
            {
                int r = Div255((int)((src >> 16) & 255) * a + (int)((dst >> 16) & 255) * ia);
                int g = Div255((int)((src >> 8) & 255) * a + (int)((dst >> 8) & 255) * ia);
                int b = Div255((int)(src & 255) * a + (int)(dst & 255) * ia);
                return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
            }
            return Over(dst, (src & 0xFFFFFFu) | ((uint)a << 24));
        }

        /// <summary>Standard non-premultiplied "source over" with src's own alpha.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Over(uint dst, uint src)
        {
            int sa = (int)(src >> 24);
            if (sa == 255) return src;
            if (sa == 0) return dst;
            int da = (int)(dst >> 24);
            if (da == 255) return Blend(dst, src, sa);
            int dw = Div255(da * (255 - sa));
            int oa = sa + dw;
            if (oa <= 0) return 0;
            int r = ((int)((src >> 16) & 255) * sa + (int)((dst >> 16) & 255) * dw) / oa;
            int g = ((int)((src >> 8) & 255) * sa + (int)((dst >> 8) & 255) * dw) / oa;
            int b = ((int)(src & 255) * sa + (int)(dst & 255) * dw) / oa;
            return ((uint)oa << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
        }

        /// <summary>Multiply-style blend used by the marker/watercolor (darkens, never lightens past the pigment).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Multiply(uint dst, uint src, int a)
        {
            if (a <= 0) return dst;
            int r = Div255((int)((src >> 16) & 255) * (int)((dst >> 16) & 255));
            int g = Div255((int)((src >> 8) & 255) * (int)((dst >> 8) & 255));
            int b = Div255((int)(src & 255) * (int)(dst & 255));
            return Blend(dst, Rgb(r, g, b), a);
        }

        public static int Luma(uint c) => (R(c) * 299 + G(c) * 587 + B(c) * 114) / 1000;
        public static uint Premul(uint c)
        {
            uint a = c >> 24;
            if (a == 255) return c;
            if (a == 0) return 0;
            uint r = (((c >> 16) & 255) * a + 127) / 255, g = (((c >> 8) & 255) * a + 127) / 255, b = ((c & 255) * a + 127) / 255;
            return (a << 24) | (r << 16) | (g << 8) | b;
        }
        public static uint UnPremul(uint c)
        {
            uint a = c >> 24;
            if (a == 255 || a == 0) return a == 0 ? 0 : c;
            uint r = Math.Min(255u, (((c >> 16) & 255) * 255 + a / 2) / a), g = Math.Min(255u, (((c >> 8) & 255) * 255 + a / 2) / a), b = Math.Min(255u, ((c & 255) * 255 + a / 2) / a);
            return (a << 24) | (r << 16) | (g << 8) | b;
        }

        public static string Hex(uint c) => "#" + (c & 0xFFFFFF).ToString("X6");

        // HSL in the classic Windows color dialog ranges (0..239 / 0..240)
        public static void ToHsl(uint c, out int h, out int s, out int l)
        {
            double r = R(c) / 255.0, g = G(c) / 255.0, b = B(c) / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            double L = (max + min) / 2, S = 0, H = 0;
            if (max != min)
            {
                double d = max - min;
                S = L > 0.5 ? d / (2 - max - min) : d / (max + min);
                if (max == r) H = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) H = (b - r) / d + 2;
                else H = (r - g) / d + 4;
                H /= 6;
            }
            h = (int)Math.Round(H * 240) % 240; s = (int)Math.Round(S * 240); l = (int)Math.Round(L * 240);
        }

        public static uint FromHsl(int h, int s, int l)
        {
            double H = (h % 240) / 240.0, S = Math.Min(240, Math.Max(0, s)) / 240.0, L = Math.Min(240, Math.Max(0, l)) / 240.0;
            double r, g, b;
            if (S == 0) r = g = b = L;
            else
            {
                double q = L < 0.5 ? L * (1 + S) : L + S - L * S;
                double p = 2 * L - q;
                r = Hue(p, q, H + 1.0 / 3); g = Hue(p, q, H); b = Hue(p, q, H - 1.0 / 3);
            }
            return Rgb((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
        }

        static double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1; if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
    }

    /// <summary>Byte coverage buffer the size of the picture, used to build strokes that must not darken where they cross themselves.</summary>
    public unsafe sealed class Mask8 : IDisposable
    {
        public int W, H;
        public byte* P;
        IntPtr _mem;

        public Mask8(int w, int h)
        {
            W = w; H = h;
            long n = (long)w * h;
            _mem = System.Runtime.InteropServices.Marshal.AllocHGlobal((IntPtr)Math.Max(1, n));
            P = (byte*)_mem;
            Clear();
        }

        public void Clear()
        {
            long n = (long)W * H;
            ulong* q = (ulong*)P; long m = n / 8;
            for (long i = 0; i < m; i++) q[i] = 0;
            for (long i = m * 8; i < n; i++) P[i] = 0;
        }

        public void Clear(Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, W, H));
            for (int y = r.Top; y < r.Bottom; y++)
            {
                byte* row = P + (long)y * W;
                for (int x = r.Left; x < r.Right; x++) row[x] = 0;
            }
        }

        public void Dispose()
        {
            if (_mem != IntPtr.Zero) { System.Runtime.InteropServices.Marshal.FreeHGlobal(_mem); _mem = IntPtr.Zero; P = null; }
        }
    }

    public static unsafe class Raster
    {
        public static Rectangle Clip(Rectangle r, int w, int h) { r.Intersect(new Rectangle(0, 0, w, h)); return r; }
        public static Rectangle Union(Rectangle a, Rectangle b)
        {
            if (a.Width <= 0 || a.Height <= 0) return b;
            if (b.Width <= 0 || b.Height <= 0) return a;
            return Rectangle.Union(a, b);
        }

        // ---------------- flood fill ----------------
        public static Rectangle FloodFill(Dib d, int sx, int sy, uint color)
        {
            if (sx < 0 || sy < 0 || sx >= d.W || sy >= d.H) return Rectangle.Empty;
            color |= 0xFF000000u;
            uint target = d.Get(sx, sy);
            if (target == color) return Rectangle.Empty;
            int w = d.W, h = d.H;
            int minX = sx, maxX = sx, minY = sy, maxY = sy;
            var stack = new int[1024];
            int sp = 0;
            void Push(int x, int y)
            {
                if (sp + 2 > stack.Length) Array.Resize(ref stack, stack.Length * 2);
                stack[sp++] = x; stack[sp++] = y;
            }
            Push(sx, sy);
            while (sp > 0)
            {
                int y = stack[--sp], x = stack[--sp];
                uint* row = d.Row(y);
                if (row[x] != target) continue;
                int lx = x, rx = x;
                while (lx > 0 && row[lx - 1] == target) lx--;
                while (rx < w - 1 && row[rx + 1] == target) rx++;
                for (int i = lx; i <= rx; i++) row[i] = color;
                if (lx < minX) minX = lx; if (rx > maxX) maxX = rx;
                if (y < minY) minY = y; if (y > maxY) maxY = y;
                for (int ny = y - 1; ny <= y + 1; ny += 2)
                {
                    if (ny < 0 || ny >= h) continue;
                    uint* nr = d.Row(ny);
                    int i = lx;
                    while (i <= rx)
                    {
                        while (i <= rx && nr[i] != target) i++;
                        if (i > rx) break;
                        Push(i, ny);
                        while (i <= rx && nr[i] == target) i++;
                    }
                }
            }
            return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }

        // ---------------- lines & stamps ----------------
        public interface IPlot { void Plot(int x, int y); }

        public static void Line<T>(int x0, int y0, int x1, int y1, ref T p) where T : struct, IPlot
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            while (true)
            {
                p.Plot(x0, y0);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }

        /// <summary>Hard-edged nib (pencil = disc, eraser = square) stamped along a Bresenham path.</summary>
        public struct NibPlot : IPlot
        {
            public Dib D; public uint Color; public int Size; public bool Square;
            public int MinX, MinY, MaxX, MaxY;
            public uint Only; public bool ReplaceMode;  // eraser right-click: replace only pixels == Only
            public void Plot(int cx, int cy)
            {
                int s = Size;
                int o = Square ? s / 2 : (s - 1) / 2;
                int x0 = cx - o, y0 = cy - o;
                float c = (s - 1) / 2f, r2 = s * s / 4f + 0.01f;
                for (int j = 0; j < s; j++)
                {
                    int y = y0 + j;
                    if ((uint)y >= (uint)D.H) continue;
                    uint* row = D.Row(y);
                    for (int i = 0; i < s; i++)
                    {
                        int x = x0 + i;
                        if ((uint)x >= (uint)D.W) continue;
                        if (!Square && s > 2)
                        {
                            float fx = i - c, fy = j - c;
                            if (fx * fx + fy * fy > r2) continue;
                        }
                        if (ReplaceMode) { if (row[x] != Only) continue; }
                        row[x] = Color;
                        if (x < MinX) MinX = x; if (x > MaxX) MaxX = x;
                        if (y < MinY) MinY = y; if (y > MaxY) MaxY = y;
                    }
                }
            }
            public Rectangle Bounds => MaxX < MinX ? Rectangle.Empty : Rectangle.FromLTRB(MinX, MinY, MaxX + 1, MaxY + 1);
        }

        public static Rectangle NibLine(Dib d, int x0, int y0, int x1, int y1, int size, uint color, bool square, bool replaceMode = false, uint only = 0)
        {
            var p = new NibPlot { D = d, Color = color | 0xFF000000u, Size = Math.Max(1, size), Square = square, MinX = int.MaxValue, MinY = int.MaxValue, MaxX = int.MinValue, MaxY = int.MinValue, ReplaceMode = replaceMode, Only = only };
            Line(x0, y0, x1, y1, ref p);
            return p.Bounds;
        }

        /// <summary>Anti-aliased round dab into a coverage mask (max-combine).</summary>
        public static Rectangle DiscToMask(Mask8 m, float cx, float cy, float radius, int strength = 255)
        {
            if (radius < 0.35f) radius = 0.35f;
            int x0 = (int)Math.Floor(cx - radius - 1), x1 = (int)Math.Ceiling(cx + radius + 1);
            int y0 = (int)Math.Floor(cy - radius - 1), y1 = (int)Math.Ceiling(cy + radius + 1);
            x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); x1 = Math.Min(m.W - 1, x1); y1 = Math.Min(m.H - 1, y1);
            if (x1 < x0 || y1 < y0) return Rectangle.Empty;
            for (int y = y0; y <= y1; y++)
            {
                byte* row = m.P + (long)y * m.W;
                float dy = y + 0.5f - cy;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - cx;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    float cov = radius + 0.5f - dist;
                    if (cov <= 0) continue;
                    int v = cov >= 1 ? strength : (int)(cov * strength);
                    if (v > row[x]) row[x] = (byte)v;
                }
            }
            return Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
        }

        /// <summary>Recomposite Image = Shadow + color * mask * opacity over r.</summary>
        public static void ApplyMask(Dib img, Dib shadow, Mask8 m, Rectangle r, uint color, int opacity, bool multiply = false)
        {
            r.Intersect(img.Bounds);
            for (int y = r.Top; y < r.Bottom; y++)
            {
                uint* d = img.Row(y), s = shadow.Row(y);
                byte* mr = m.P + (long)y * m.W;
                for (int x = r.Left; x < r.Right; x++)
                {
                    int a = mr[x];
                    if (a == 0) { d[x] = s[x]; continue; }
                    a = a * opacity / 255;
                    d[x] = multiply ? Px.Multiply(s[x], color, a) : Px.Blend(s[x], color, a);
                }
            }
        }

        // ---------------- whole-image transforms ----------------
        public static Dib Rotate90(Dib s, bool clockwise)
        {
            var d = new Dib(s.H, s.W);
            int sw = s.W, sh = s.H;
            for (int y = 0; y < sh; y++)
            {
                uint* row = s.Row(y);
                for (int x = 0; x < sw; x++)
                {
                    if (clockwise) d.P[(long)x * sh + (sh - 1 - y)] = row[x];
                    else d.P[(long)(sw - 1 - x) * sh + y] = row[x];
                }
            }
            return d;
        }

        public static Dib Rotate180(Dib s)
        {
            var d = new Dib(s.W, s.H);
            long n = (long)s.W * s.H;
            for (long i = 0; i < n; i++) d.P[n - 1 - i] = s.P[i];
            return d;
        }

        public static Dib Flip(Dib s, bool horizontal)
        {
            var d = new Dib(s.W, s.H);
            for (int y = 0; y < s.H; y++)
            {
                uint* src = s.Row(y);
                if (horizontal)
                {
                    uint* dst = d.Row(y);
                    for (int x = 0; x < s.W; x++) dst[s.W - 1 - x] = src[x];
                }
                else
                {
                    long bytes = (long)s.W * 4;
                    Buffer.MemoryCopy(src, d.Row(s.H - 1 - y), bytes, bytes);
                }
            }
            return d;
        }

        public static void InvertColors(Dib d, Rectangle r, byte[] mask = null, int maskW = 0, int mx = 0, int my = 0)
        {
            r.Intersect(d.Bounds);
            for (int y = r.Top; y < r.Bottom; y++)
            {
                uint* row = d.Row(y);
                for (int x = r.Left; x < r.Right; x++)
                {
                    if (mask != null && mask[(y - my) * maskW + (x - mx)] == 0) continue;
                    row[x] ^= 0x00FFFFFFu;
                }
            }
        }

        public static void ToBlackAndWhite(Dib d)
        {
            long n = (long)d.W * d.H;
            for (long i = 0; i < n; i++)
            {
                uint c = d.P[i];
                d.P[i] = (c & 0xFF000000u) | (Px.Luma(c) >= 128 ? 0xFFFFFFu : 0u);
            }
        }

        /// <summary>High quality separable resample (area/triangle when shrinking, Catmull-Rom when enlarging), alpha aware.</summary>
        public static Dib Resample(Dib src, int nw, int nh)
        {
            nw = Math.Max(1, nw); nh = Math.Max(1, nh);
            if (nw == src.W && nh == src.H) return src.Clone();
            // horizontal pass into float buffer (premultiplied), then vertical
            var hx = Contribs(src.W, nw);
            var hy = Contribs(src.H, nh);
            int sw = src.W, sh = src.H;
            var tmp = new float[(long)nw * sh * 4];
            bool alpha = src.AnyTransparent(src.Bounds);
            fixed (float* t = tmp)
            {
                for (int y = 0; y < sh; y++)
                {
                    uint* row = src.Row(y);
                    float* trow = t + (long)y * nw * 4;
                    for (int x = 0; x < nw; x++)
                    {
                        var c = hx[x];
                        float a = 0, r = 0, g = 0, b = 0;
                        for (int k = 0; k < c.N; k++)
                        {
                            uint p = row[c.Start + k];
                            float wgt = c.W[k];
                            float pa = (p >> 24) / 255f;
                            if (!alpha) pa = 1;
                            a += pa * wgt;
                            r += ((p >> 16) & 255) * pa * wgt;
                            g += ((p >> 8) & 255) * pa * wgt;
                            b += (p & 255) * pa * wgt;
                        }
                        trow[x * 4] = a; trow[x * 4 + 1] = r; trow[x * 4 + 2] = g; trow[x * 4 + 3] = b;
                    }
                }
                var d = new Dib(nw, nh);
                for (int y = 0; y < nh; y++)
                {
                    var c = hy[y];
                    uint* drow = d.Row(y);
                    for (int x = 0; x < nw; x++)
                    {
                        float a = 0, r = 0, g = 0, b = 0;
                        for (int k = 0; k < c.N; k++)
                        {
                            float* tp = t + ((long)(c.Start + k) * nw + x) * 4;
                            float wgt = c.W[k];
                            a += tp[0] * wgt; r += tp[1] * wgt; g += tp[2] * wgt; b += tp[3] * wgt;
                        }
                        if (a <= 0.0005f) { drow[x] = alpha ? 0u : 0xFF000000u; continue; }
                        float inv = 1f / a;
                        int ia = alpha ? Clamp255(a * 255f) : 255;
                        drow[x] = ((uint)ia << 24) | ((uint)Clamp255(r * inv) << 16) | ((uint)Clamp255(g * inv) << 8) | (uint)Clamp255(b * inv);
                    }
                }
                return d;
            }
        }

        static int Clamp255(float v) => v <= 0 ? 0 : v >= 255 ? 255 : (int)(v + 0.5f);

        struct Contrib { public int Start, N; public float[] W; }

        static Contrib[] Contribs(int srcLen, int dstLen)
        {
            var res = new Contrib[dstLen];
            double scale = (double)dstLen / srcLen;
            bool down = scale < 1;
            double support = down ? 1.0 / scale : 2.0;   // triangle widened when shrinking; Catmull-Rom radius 2 when enlarging
            for (int i = 0; i < dstLen; i++)
            {
                double center = (i + 0.5) / scale - 0.5;
                int start = (int)Math.Floor(center - support) + 1;
                int end = (int)Math.Floor(center + support);
                start = Math.Max(0, start); end = Math.Min(srcLen - 1, end);
                int n = Math.Max(1, end - start + 1);
                var w = new float[n];
                double sum = 0;
                for (int k = 0; k < n; k++)
                {
                    double x = start + k - center;
                    double v = down ? Math.Max(0, 1 - Math.Abs(x * scale)) : CatmullRom(x);
                    w[k] = (float)v; sum += v;
                }
                if (Math.Abs(sum) < 1e-9) { w = new[] { 1f }; n = 1; start = Math.Min(srcLen - 1, Math.Max(0, (int)Math.Round(center))); sum = 1; }
                for (int k = 0; k < n; k++) w[k] = (float)(w[k] / sum);
                res[i] = new Contrib { Start = start, N = n, W = w };
            }
            return res;
        }

        static double CatmullRom(double x)
        {
            x = Math.Abs(x);
            if (x < 1) return 1.5 * x * x * x - 2.5 * x * x + 1;
            if (x < 2) return -0.5 * x * x * x + 2.5 * x * x - 4 * x + 2;
            return 0;
        }

        /// <summary>Skew (shear) by angles in degrees. Uncovered area gets 'background'.</summary>
        public static Dib Skew(Dib s, double degH, double degV, uint background)
        {
            Dib cur = s;
            bool own = false;
            if (Math.Abs(degH) > 0.001)
            {
                double t = Math.Tan(degH * Math.PI / 180);
                int extra = (int)Math.Ceiling(Math.Abs(t) * s.H);
                var d = new Dib(cur.W + extra, cur.H, background);
                for (int y = 0; y < cur.H; y++)
                {
                    // positive = top leans right (italic)
                    double off = t >= 0 ? (cur.H - 1 - y) * t : y * -t;
                    ShiftRow(cur.Row(y), cur.W, d.Row(y), d.W, off, background);
                }
                cur = d; own = true;
            }
            if (Math.Abs(degV) > 0.001)
            {
                double t = Math.Tan(degV * Math.PI / 180);
                int extra = (int)Math.Ceiling(Math.Abs(t) * cur.W);
                var d = new Dib(cur.W, cur.H + extra, background);
                var col = new uint[cur.H];
                var outCol = new uint[d.H];
                fixed (uint* cp = col) fixed (uint* op = outCol)
                {
                    for (int x = 0; x < cur.W; x++)
                    {
                        for (int y = 0; y < cur.H; y++) cp[y] = cur.P[(long)y * cur.W + x];
                        // positive = right side lower... matches Paint's sense of a positive vertical skew (left edge up)
                        double off = t >= 0 ? (cur.W - 1 - x) * t : x * -t;
                        ShiftRow(cp, cur.H, op, d.H, off, background);
                        for (int y = 0; y < d.H; y++) d.P[(long)y * d.W + x] = op[y];
                    }
                }
                if (own) cur.Dispose();
                cur = d;
            }
            return cur == s ? s.Clone() : cur;
        }

        static void ShiftRow(uint* src, int sn, uint* dst, int dn, double off, uint bg)
        {
            int io = (int)Math.Floor(off);
            float f = (float)(off - io);
            int wa = (int)(f * 256), wb = 256 - wa;
            for (int i = 0; i < dn; i++)
            {
                // dst[i] samples src at (i - off)
                int j = i - io;
                uint a = (j >= 0 && j < sn) ? src[j] : bg;
                uint b = (j - 1 >= 0 && j - 1 < sn) ? src[j - 1] : bg;
                if (wa == 0) { dst[i] = a; continue; }
                dst[i] = Mix(a, b, wb, wa);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint Mix(uint a, uint b, int wa, int wb)
        {
            uint ag = (a >> 8) & 0x00FF00FFu, ar = a & 0x00FF00FFu;
            uint bg = (b >> 8) & 0x00FF00FFu, br = b & 0x00FF00FFu;
            uint g = ((ag * (uint)wa + bg * (uint)wb) >> 8) & 0x00FF00FFu;
            uint r = ((ar * (uint)wa + br * (uint)wb) >> 8) & 0x00FF00FFu;
            return (g << 8) | r;
        }

        /// <summary>Nearest-neighbour scale, used for interactive previews.</summary>
        public static Dib ScaleNearest(Dib s, int nw, int nh)
        {
            nw = Math.Max(1, nw); nh = Math.Max(1, nh);
            var d = new Dib(nw, nh);
            var xs = new int[nw];
            for (int x = 0; x < nw; x++) xs[x] = Math.Min(s.W - 1, (int)((x + 0.5) * s.W / nw));
            for (int y = 0; y < nh; y++)
            {
                uint* sr = s.Row(Math.Min(s.H - 1, (int)((y + 0.5) * s.H / nh)));
                uint* dr = d.Row(y);
                for (int x = 0; x < nw; x++) dr[x] = sr[xs[x]];
            }
            return d;
        }
    }

    /// <summary>Deterministic value noise anchored to picture coordinates (paper grain for crayon/pencil/watercolor).</summary>
    public static class Grain
    {
        static readonly byte[] _tex = Build();
        const int N = 256;

        static byte[] Build()
        {
            var t = new byte[N * N];
            var rnd = new Random(1234567);
            var coarse = new float[(N / 4) * (N / 4)];
            for (int i = 0; i < coarse.Length; i++) coarse[i] = (float)rnd.NextDouble();
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    // fine white noise mixed with smoothed coarse noise = paper fibre look
                    float fx = x / 4f, fy = y / 4f;
                    int x0 = (int)fx, y0 = (int)fy; float tx = fx - x0, ty = fy - y0;
                    int cw = N / 4;
                    float c00 = coarse[(y0 % cw) * cw + x0 % cw], c10 = coarse[(y0 % cw) * cw + (x0 + 1) % cw];
                    float c01 = coarse[((y0 + 1) % cw) * cw + x0 % cw], c11 = coarse[((y0 + 1) % cw) * cw + (x0 + 1) % cw];
                    float c = (c00 * (1 - tx) + c10 * tx) * (1 - ty) + (c01 * (1 - tx) + c11 * tx) * ty;
                    float v = 0.55f * (float)rnd.NextDouble() + 0.45f * c;
                    t[y * N + x] = (byte)Math.Min(255, Math.Max(0, (int)(v * 255)));
                }
            return t;
        }

        public static int At(int x, int y) => _tex[((y & (N - 1)) << 8) | (x & (N - 1))];
    }
}
