using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PaintDark
{
    /// <summary>Outlines of Paint's ready-made shapes inside a bounding box (Office-art proportions).</summary>
    public static class ShapeGeometry
    {
        public static GraphicsPath Build(ShapeKind k, RectangleF r, float strokeWidth)
        {
            var p = new GraphicsPath(FillMode.Winding);
            float x0 = r.Left, y0 = r.Top, w = Math.Max(r.Width, 0.01f), h = Math.Max(r.Height, 0.01f);
            float x1 = x0 + w, y1 = y0 + h, cx = x0 + w / 2, cy = y0 + h / 2;
            switch (k)
            {
                case ShapeKind.Oval: p.AddEllipse(x0, y0, w, h); break;
                case ShapeKind.Rectangle: p.AddRectangle(new RectangleF(x0, y0, w, h)); break;
                case ShapeKind.RoundedRectangle:
                    {
                        float rad = Math.Min(Math.Min(w, h) / 2f, Math.Max(1.5f, Math.Min(w, h) * 0.17f));
                        AddRoundRect(p, x0, y0, w, h, rad);
                        break;
                    }
                case ShapeKind.Triangle: p.AddPolygon(new[] { new PointF(cx, y0), new PointF(x1, y1), new PointF(x0, y1) }); break;
                case ShapeKind.RightTriangle: p.AddPolygon(new[] { new PointF(x0, y0), new PointF(x1, y1), new PointF(x0, y1) }); break;
                case ShapeKind.Diamond: p.AddPolygon(new[] { new PointF(cx, y0), new PointF(x1, cy), new PointF(cx, y1), new PointF(x0, cy) }); break;
                case ShapeKind.Pentagon: p.AddPolygon(Fit(Regular(5, -90, 1, 1), r)); break;
                case ShapeKind.Hexagon: p.AddPolygon(Fit(Regular(6, 0, 1, 1), r)); break;
                case ShapeKind.RightArrow:
                    {
                        float hl = Math.Min(w, h) * 0.5f, t = h / 4;
                        p.AddPolygon(new[] { new PointF(x0, cy - t), new PointF(x1 - hl, cy - t), new PointF(x1 - hl, y0), new PointF(x1, cy), new PointF(x1 - hl, y1), new PointF(x1 - hl, cy + t), new PointF(x0, cy + t) });
                        break;
                    }
                case ShapeKind.LeftArrow:
                    {
                        float hl = Math.Min(w, h) * 0.5f, t = h / 4;
                        p.AddPolygon(new[] { new PointF(x1, cy - t), new PointF(x0 + hl, cy - t), new PointF(x0 + hl, y0), new PointF(x0, cy), new PointF(x0 + hl, y1), new PointF(x0 + hl, cy + t), new PointF(x1, cy + t) });
                        break;
                    }
                case ShapeKind.UpArrow:
                    {
                        float hl = Math.Min(w, h) * 0.5f, t = w / 4;
                        p.AddPolygon(new[] { new PointF(cx - t, y1), new PointF(cx - t, y0 + hl), new PointF(x0, y0 + hl), new PointF(cx, y0), new PointF(x1, y0 + hl), new PointF(cx + t, y0 + hl), new PointF(cx + t, y1) });
                        break;
                    }
                case ShapeKind.DownArrow:
                    {
                        float hl = Math.Min(w, h) * 0.5f, t = w / 4;
                        p.AddPolygon(new[] { new PointF(cx - t, y0), new PointF(cx - t, y1 - hl), new PointF(x0, y1 - hl), new PointF(cx, y1), new PointF(x1, y1 - hl), new PointF(cx + t, y1 - hl), new PointF(cx + t, y0) });
                        break;
                    }
                case ShapeKind.Star4: p.AddPolygon(Fit(Star(4, 0.27f, -90), r)); break;
                case ShapeKind.Star5: p.AddPolygon(Fit(Star(5, 0.382f, -90), r)); break;
                case ShapeKind.Star6: p.AddPolygon(Fit(Star(6, 0.5774f, -90), r)); break;
                case ShapeKind.RoundedCallout: RoundCallout(p, x0, y0, w, h); break;
                case ShapeKind.OvalCallout: OvalCallout(p, x0, y0, w, h); break;
                case ShapeKind.CloudCallout: CloudCallout(p, r); break;
                case ShapeKind.Heart: Heart(p, r); break;
                case ShapeKind.Lightning:
                    {
                        float[] v = { 8472, 0, 12860, 6080, 11050, 6797, 16577, 12007, 14767, 12877, 21600, 21600, 10012, 14915, 12222, 13987, 5022, 9705, 7602, 8382, 0, 3890 };
                        var pts = new PointF[v.Length / 2];
                        for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(x0 + v[i * 2] / 21600f * w, y0 + v[i * 2 + 1] / 21600f * h);
                        p.AddPolygon(pts);
                        break;
                    }
                default: p.AddRectangle(new RectangleF(x0, y0, w, h)); break;
            }
            return p;
        }

        static void AddRoundRect(GraphicsPath p, float x, float y, float w, float h, float rad)
        {
            float d = rad * 2;
            if (d < 0.5f) { p.AddRectangle(new RectangleF(x, y, w, h)); return; }
            p.StartFigure();
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
        }

        static List<PointF> Regular(int n, float startDeg, float rx, float ry)
        {
            var l = new List<PointF>();
            for (int i = 0; i < n; i++)
            {
                double a = (startDeg + i * 360.0 / n) * Math.PI / 180;
                l.Add(new PointF((float)(Math.Cos(a) * rx), (float)(Math.Sin(a) * ry)));
            }
            return l;
        }

        static List<PointF> Star(int n, float inner, float startDeg)
        {
            var l = new List<PointF>();
            for (int i = 0; i < n * 2; i++)
            {
                double a = (startDeg + i * 180.0 / n) * Math.PI / 180;
                float rr = (i & 1) == 0 ? 1f : inner;
                l.Add(new PointF((float)(Math.Cos(a) * rr), (float)(Math.Sin(a) * rr)));
            }
            return l;
        }

        /// <summary>Stretch a point set so its bounding box exactly fills r.</summary>
        static PointF[] Fit(List<PointF> pts, RectangleF r)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var q in pts) { minX = Math.Min(minX, q.X); maxX = Math.Max(maxX, q.X); minY = Math.Min(minY, q.Y); maxY = Math.Max(maxY, q.Y); }
            float sx = r.Width / Math.Max(1e-6f, maxX - minX), sy = r.Height / Math.Max(1e-6f, maxY - minY);
            var res = new PointF[pts.Count];
            for (int i = 0; i < pts.Count; i++) res[i] = new PointF(r.Left + (pts[i].X - minX) * sx, r.Top + (pts[i].Y - minY) * sy);
            return res;
        }

        static void FitPath(GraphicsPath p, RectangleF r)
        {
            RectangleF b;
            // measure the real curve, not its Bézier control points
            using (var flat = (GraphicsPath)p.Clone()) { flat.Flatten(null, 0.05f); b = flat.GetBounds(); }
            if (b.Width < 1e-6f || b.Height < 1e-6f) return;
            using (var m = new Matrix())
            {
                m.Translate(r.Left, r.Top);
                m.Scale(r.Width / b.Width, r.Height / b.Height);
                m.Translate(-b.Left, -b.Top);
                p.Transform(m);
            }
        }

        static void RoundCallout(GraphicsPath p, float x0, float y0, float w, float h)
        {
            float bh = h * 0.8f;                      // body takes the top 80%, the tail reaches the bottom edge
            float rad = Math.Min(Math.Min(w, bh) / 2f, Math.Max(1.5f, Math.Min(w, bh) * 0.17f));
            float d = rad * 2;
            float by = y0 + bh;
            float b1 = x0 + w * 0.25f, b2 = x0 + w * 0.42f;
            float tipX = x0 + w * 0.17f, tipY = y0 + h;
            p.StartFigure();
            p.AddArc(x0, y0, d, d, 180, 90);
            p.AddArc(x0 + w - d, y0, d, d, 270, 90);
            p.AddArc(x0 + w - d, by - d, d, d, 0, 90);
            p.AddLine(x0 + w - rad, by, b2, by);
            p.AddLine(b2, by, tipX, tipY);
            p.AddLine(tipX, tipY, b1, by);
            p.AddLine(b1, by, x0 + rad, by);
            p.AddArc(x0, by - d, d, d, 90, 90);
            p.CloseFigure();
        }

        static void OvalCallout(GraphicsPath p, float x0, float y0, float w, float h)
        {
            float bh = h * 0.8f;
            float tipX = x0 + w * 0.17f, tipY = y0 + h;
            p.StartFigure();
            p.AddArc(x0, y0, w, bh, 128, 332);   // leaves a gap around the lower-left for the wedge
            p.AddLine(PointOnEllipse(x0, y0, w, bh, 100), new PointF(tipX, tipY));
            p.AddLine(new PointF(tipX, tipY), PointOnEllipse(x0, y0, w, bh, 128));
            p.CloseFigure();
        }

        static PointF PointOnEllipse(float x, float y, float w, float h, float deg)
        {
            // GDI+ arc angles are measured on the ellipse itself
            double a = deg * Math.PI / 180;
            double rx = w / 2, ry = h / 2;
            double t = Math.Atan2(Math.Sin(a) * rx, Math.Cos(a) * ry);
            return new PointF((float)(x + rx + rx * Math.Cos(t)), (float)(y + ry + ry * Math.Sin(t)));
        }

        static void CloudCallout(GraphicsPath p, RectangleF r)
        {
            var cloud = new GraphicsPath(FillMode.Winding);
            int n = 10;
            var pts = new PointF[n];
            var nor = new PointF[n];
            float[] jitter = { 0.00f, 0.03f, -0.02f, 0.04f, -0.03f, 0.02f, -0.01f, 0.03f, -0.02f, 0.01f };
            for (int i = 0; i < n; i++)
            {
                double a = (i + jitter[i] * 4) * Math.PI * 2 / n - Math.PI / 2;
                pts[i] = new PointF((float)Math.Cos(a), (float)Math.Sin(a) * 0.72f);
                var nn = new PointF((float)Math.Cos(a) * 0.72f, (float)Math.Sin(a));
                float len = (float)Math.Sqrt(nn.X * nn.X + nn.Y * nn.Y);
                nor[i] = new PointF(nn.X / len, nn.Y / len);
            }
            cloud.StartFigure();
            for (int i = 0; i < n; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % n];
                var na = nor[i]; var nb = nor[(i + 1) % n];
                float chord = (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                float k = chord * 0.75f;
                cloud.AddBezier(a, new PointF(a.X + na.X * k, a.Y + na.Y * k), new PointF(b.X + nb.X * k, b.Y + nb.Y * k), b);
            }
            cloud.CloseFigure();
            var body = new RectangleF(r.Left + r.Width * 0.08f, r.Top, r.Width * 0.92f, r.Height * 0.76f);
            FitPath(cloud, body);
            p.AddPath(cloud, false);
            cloud.Dispose();
            // thought bubbles trailing to the lower-left corner
            float bx = r.Left + r.Width * 0.22f, byy = r.Top + r.Height * 0.78f;
            float tx = r.Left + r.Width * 0.02f, ty = r.Bottom;
            float[] sizes = { 0.11f, 0.075f, 0.05f };
            for (int i = 0; i < 3; i++)
            {
                float t = (i + 0.6f) / 3.2f;
                float s = Math.Min(r.Width, r.Height) * sizes[i];
                float ex = bx + (tx - bx) * t, ey = byy + (ty - byy) * t;
                p.StartFigure();
                p.AddEllipse(ex - s / 2, ey - s / 2 * 0.85f, s, s * 0.85f);
            }
        }

        static void Heart(GraphicsPath p, RectangleF r)
        {
            float w = r.Width, h = r.Height, hc = r.Left + w / 2, t = r.Top;
            float y0 = t + h / 4, y1 = t - h / 3, b = t + h;
            float dx1 = w * 49 / 48f, dx2 = w * 10 / 48f;
            var hp = new GraphicsPath();
            hp.StartFigure();
            hp.AddBezier(hc, y0, hc + dx2, y1, hc + dx1, y0, hc, b);
            hp.AddBezier(hc, b, hc - dx1, y0, hc - dx2, y1, hc, y0);
            hp.CloseFigure();
            FitPath(hp, r);
            p.AddPath(hp, false);
            hp.Dispose();
        }
    }
}
