using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PaintDark
{
    /// <summary>
    /// Automation hook used for testing (only active with --test-hook). Commands arrive through WM_COPYDATA and are
    /// replayed straight into the app's own controls, so the real mouse, keyboard and window focus are never touched.
    /// </summary>
    public static class TestHook
    {
        public static bool Enabled;
        [DllImport("user32.dll")] static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int cx, int cy, int step, IntPtr brush, int flags);
        public static bool Ctrl, Shift, Alt;
        static readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();

        [StructLayout(LayoutKind.Sequential)]
        struct COPYDATASTRUCT { public IntPtr dwData; public int cbData; public IntPtr lpData; }

        public static bool TryHandle(MainForm f, ref Message m)
        {
            if (!Enabled || m.Msg != 0x004A) return false;
            var cds = (COPYDATASTRUCT)Marshal.PtrToStructure(m.LParam, typeof(COPYDATASTRUCT));
            string text = Marshal.PtrToStringUni(cds.lpData, cds.cbData / 2);
            string result;
            try { result = Run(f, text); }
            catch (Exception ex) { result = "ERROR " + ex; }
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "paintdark_hook.txt"), result ?? "OK"); } catch { }
            m.Result = (IntPtr)1;
            return true;
        }

        static Control FindTarget(Form root, Point p, out Point local)
        {
            Control c = root;
            local = p;
            while (true)
            {
                Control child = null;
                for (int i = 0; i < c.Controls.Count; i++)
                {
                    var k = c.Controls[i];
                    if (k.Visible && k.Bounds.Contains(local)) { child = k; break; }
                }
                if (child == null) return c;
                local = new Point(local.X - child.Left, local.Y - child.Top);
                c = child;
            }
        }

        static Form TopModal(MainForm f)
        {
            Form r = null;
            foreach (Form of in Application.OpenForms) if (of.Modal && of.Visible && of != f) r = of;
            return r;
        }

        static Form TargetForm(MainForm f, bool popup)
        {
            if (popup && Popups.Top != null) return Popups.Top;
            foreach (Form of in Application.OpenForms) if (of.Modal && of.Visible && of != f) return of;
            return f;
        }

        static void SendMouse(Control c, int msg, Point p, int keys)
        {
            Native.SendMessage(c.Handle, msg, (IntPtr)keys, Native.MakeLParam(p.X, p.Y));
        }

        static int Btn(string b) => b == "r" ? Native.MK_RBUTTON : b == "m" ? Native.MK_MBUTTON : Native.MK_LBUTTON;
        static int Mods() => (Ctrl ? Native.MK_CONTROL : 0) | (Shift ? Native.MK_SHIFT : 0);

        static void Mouse(Form root, string kind, int x, int y, string btn)
        {
            var c = FindTarget(root, new Point(x, y), out var lp);
            int down = btn == "r" ? Native.WM_RBUTTONDOWN : btn == "m" ? Native.WM_MBUTTONDOWN : Native.WM_LBUTTONDOWN;
            int up = btn == "r" ? Native.WM_RBUTTONUP : btn == "m" ? Native.WM_MBUTTONUP : Native.WM_LBUTTONUP;
            int dbl = btn == "r" ? Native.WM_RBUTTONDBLCLK : Native.WM_LBUTTONDBLCLK;
            switch (kind)
            {
                case "move": SendMouse(c, Native.WM_MOUSEMOVE, lp, Mods()); break;
                case "hover": SendMouse(c, Native.WM_MOUSEMOVE, lp, Mods()); break;
                case "down": SendMouse(c, Native.WM_MOUSEMOVE, lp, Mods()); SendMouse(c, down, lp, Btn(btn) | Mods()); break;
                case "up": SendMouse(c, up, lp, Mods()); break;
                case "dbl": SendMouse(c, dbl, lp, Btn(btn) | Mods()); SendMouse(c, up, lp, Mods()); break;
            }
            Application.DoEvents();
        }

        public static string Run(MainForm f, string script)
        {
            var sb = new StringBuilder();
            foreach (var raw in script.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                int k = line.IndexOf(':');
                string cmd = k < 0 ? line : line.Substring(0, k), arg = k < 0 ? "" : line.Substring(k + 1);
                var a = arg.Split(',');
                switch (cmd)
                {
                    case "mods": Ctrl = arg.Contains("c"); Shift = arg.Contains("s"); Alt = arg.Contains("a"); break;
                    case "move": case "down": case "up": case "dbl": case "hover":
                        Mouse(TargetForm(f, false), cmd, int.Parse(a[0]), int.Parse(a[1]), a.Length > 2 ? a[2] : "l"); break;
                    case "pmove": case "pdown": case "pup": case "pclick":
                        {
                            var root = TargetForm(f, true);
                            int x = int.Parse(a[0]), y = int.Parse(a[1]);
                            if (cmd == "pclick") { Mouse(root, "down", x, y, "l"); root = TargetForm(f, true); Mouse(root, "up", x, y, "l"); }
                            else Mouse(root, cmd.Substring(1), x, y, "l");
                            break;
                        }
                    case "click":
                        {
                            var root = TargetForm(f, false);
                            int x = int.Parse(a[0]), y = int.Parse(a[1]); string b = a.Length > 2 ? a[2] : "l";
                            Mouse(root, "down", x, y, b); Mouse(root, "up", x, y, b);
                            break;
                        }
                    case "drag":
                        {
                            var root = TargetForm(f, false);
                            int x1 = int.Parse(a[0]), y1 = int.Parse(a[1]), x2 = int.Parse(a[2]), y2 = int.Parse(a[3]); string b = a.Length > 4 ? a[4] : "l";
                            var c = FindTarget(root, new Point(x1, y1), out var lp);
                            var off = new Point(x1 - lp.X, y1 - lp.Y);
                            SendMouse(c, Native.WM_MOUSEMOVE, lp, Mods());
                            SendMouse(c, b == "r" ? Native.WM_RBUTTONDOWN : Native.WM_LBUTTONDOWN, lp, Btn(b) | Mods());
                            for (int s = 1; s <= 24; s++)
                            {
                                var q = new Point(x1 + (x2 - x1) * s / 24 - off.X, y1 + (y2 - y1) * s / 24 - off.Y);
                                SendMouse(c, Native.WM_MOUSEMOVE, q, Btn(b) | Mods());
                                Application.DoEvents();
                            }
                            SendMouse(c, b == "r" ? Native.WM_RBUTTONUP : Native.WM_LBUTTONUP, new Point(x2 - off.X, y2 - off.Y), Mods());
                            Application.DoEvents();
                            break;
                        }
                    case "path":
                        {
                            var root = TargetForm(f, false);
                            var pts = new Point[a.Length / 2];
                            for (int i = 0; i < pts.Length; i++) pts[i] = new Point(int.Parse(a[i * 2]), int.Parse(a[i * 2 + 1]));
                            var c = FindTarget(root, pts[0], out var lp);
                            var off = new Point(pts[0].X - lp.X, pts[0].Y - lp.Y);
                            SendMouse(c, Native.WM_LBUTTONDOWN, lp, Native.MK_LBUTTON | Mods());
                            for (int i = 1; i < pts.Length; i++)
                                for (int s = 1; s <= 12; s++)
                                {
                                    var q = new Point(pts[i - 1].X + (pts[i].X - pts[i - 1].X) * s / 12 - off.X, pts[i - 1].Y + (pts[i].Y - pts[i - 1].Y) * s / 12 - off.Y);
                                    SendMouse(c, Native.WM_MOUSEMOVE, q, Native.MK_LBUTTON | Mods());
                                    Application.DoEvents();
                                }
                            var last = pts[pts.Length - 1];
                            SendMouse(c, Native.WM_LBUTTONUP, new Point(last.X - off.X, last.Y - off.Y), Mods());
                            Application.DoEvents();
                            break;
                        }
                    case "key":
                        {
                            Keys keys = 0;
                            foreach (var part in arg.Split('+'))
                            {
                                var p = part.Trim().ToLowerInvariant();
                                if (p == "ctrl") keys |= Keys.Control; else if (p == "shift") keys |= Keys.Shift; else if (p == "alt") keys |= Keys.Alt;
                                else keys |= (Keys)Enum.Parse(typeof(Keys), part.Trim(), true);
                            }
                            Ctrl = (keys & Keys.Control) != 0; Shift = (keys & Keys.Shift) != 0; Alt = (keys & Keys.Alt) != 0;
                            try { f.TestKey(keys); }
                            finally { Ctrl = Shift = Alt = false; }
                            Application.DoEvents();
                            break;
                        }
                    case "type":
                        {
                            var target = Popups.Top == null ? (Control)(f.ActiveControl ?? f.Canvas) : null;
                            foreach (var ch in arg.Replace("\\n", "\r"))
                            {
                                if (target != null) Native.SendMessage(target.Handle, Native.WM_CHAR, (IntPtr)ch, IntPtr.Zero);
                                else Popups.Top.HandleChar(ch);
                            }
                            Application.DoEvents();
                            break;
                        }
                    case "cmd": f.Execute((Cmd)Enum.Parse(typeof(Cmd), arg, true)); Application.DoEvents(); break;
                    case "acmd": { var c = (Cmd)Enum.Parse(typeof(Cmd), arg, true); f.BeginInvoke(new Action(() => f.Execute(c))); break; }
                    case "c1": f.E.SetColor(1, 0xFF000000u | Convert.ToUInt32(arg.TrimStart('#'), 16)); break;
                    case "c2": f.E.SetColor(2, 0xFF000000u | Convert.ToUInt32(arg.TrimStart('#'), 16)); break;
                    case "brush": f.E.SetBrush((BrushKind)Enum.Parse(typeof(BrushKind), arg, true)); break;
                    case "shape": f.E.SetShape((ShapeKind)Enum.Parse(typeof(ShapeKind), arg, true)); break;
                    case "tool": f.E.SetTool((ToolKind)Enum.Parse(typeof(ToolKind), arg, true)); break;
                    case "toolsize": f.E.ToolSize = int.Parse(arg); break;
                    case "outline": f.E.SetOutline((Texture)Enum.Parse(typeof(Texture), arg, true)); break;
                    case "fill": f.E.SetShapeFill((Texture)Enum.Parse(typeof(Texture), arg, true)); break;
                    case "zoom": f.Canvas.SetZoom(float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture), null); break;
                    case "dclose": { var d = TopModal(f); if (d != null) { d.DialogResult = DialogResult.Cancel; d.Close(); } Application.DoEvents(); break; }
                    case "dok": { var d = TopModal(f); (d?.AcceptButton as IButtonControl)?.PerformClick(); Application.DoEvents(); break; }
                    case "img":
                        {
                            // image-space helpers: imgdrag:x1,y1,x2,y2 -> converted to form client coordinates
                            break;
                        }
                    case "imgdrag": case "imgclick": case "imgpath":
                        {
                            var pts = new System.Collections.Generic.List<string>();
                            for (int i = 0; i + 1 < a.Length; i += 2)
                            {
                                var v = f.Canvas.ImageToView(new Point(int.Parse(a[i]), int.Parse(a[i + 1])));
                                var fp = f.PointToClient(f.Canvas.PointToScreen(v));
                                pts.Add(fp.X + "," + fp.Y);
                            }
                            string extra = a.Length % 2 == 1 ? "," + a[a.Length - 1] : "";
                            sb.Append(Run(f, cmd.Substring(3) + ":" + string.Join(",", pts) + extra) == "OK" ? "" : "");
                            break;
                        }
                    case "open": f.OpenFile(arg, false, false); Application.DoEvents(); break;
                    case "size": f.WindowState = FormWindowState.Normal; f.Size = new Size(int.Parse(a[0]), int.Parse(a[1])); Application.DoEvents(); break;
                    case "max": f.WindowState = FormWindowState.Maximized; Application.DoEvents(); break;
                    case "focus": f.Canvas.Focus(); break;
                    case "sleep": System.Threading.Thread.Sleep(int.Parse(arg)); Application.DoEvents(); break;
                    case "pump": for (int i = 0; i < 20; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(10); } break;
                    case "closepopups": Popups.CloseAll(); break;
                    case "dpi":
                        {
                            int dpi = int.Parse(a[0]);
                            var b = f.Bounds;
                            var rc = Marshal.AllocHGlobal(16);
                            Marshal.StructureToPtr(new RECT(b.Left, b.Top, b.Right, b.Bottom), rc, false);
                            Native.SendMessage(f.Handle, Native.WM_DPICHANGED, (IntPtr)((dpi << 16) | dpi), rc);
                            Marshal.FreeHGlobal(rc);
                            Application.DoEvents();
                            break;
                        }
                    case "repaint":
                        {
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            int n = arg.Length > 0 ? int.Parse(arg) : 5;
                            for (int i = 0; i < n; i++) { f.Canvas.Invalidate(); f.Canvas.Update(); }
                            sb.AppendLine("repaint avg " + (sw.Elapsed.TotalMilliseconds / n).ToString("0.0") + "ms at zoom " + f.Canvas.Zoom);
                            break;
                        }
                    case "tick": sb.AppendLine("tick " + _sw.ElapsedMilliseconds + "ms " + arg); _sw.Restart(); break;
                    case "saveimg": ImageIO.Save(f.E.Doc.Image, f.E.Doc.HasAlpha, a[0], a[1], a.Length > 2 ? int.Parse(a[2]) : 24, 96); sb.AppendLine("saved " + a[0] + " " + new FileInfo(a[0]).Length + " bytes"); break;
                    case "canvas": f.E.ResizeCanvas(new Size(int.Parse(a[0]), int.Parse(a[1]))); Application.DoEvents(); break;
                    case "newdoc": f.E.Doc.MarkSaved(); f.NewDocument(false); Application.DoEvents(); break;
                    case "mem": GC.Collect(); sb.AppendLine("managed=" + (GC.GetTotalMemory(true) >> 20) + "MB ws=" + (System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 >> 20) + "MB private=" + (System.Diagnostics.Process.GetCurrentProcess().PrivateMemorySize64 >> 20) + "MB"); break;
                    case "keytips": f.Ribbon.BeginKeyTips(); f.Ribbon.Invalidate(); break;
                    case "popupinfo": sb.AppendLine(Popups.Top is AppMenu am ? am.DebugInfo : (Popups.Top?.GetType().Name ?? "none") + " " + Popups.Top?.Bounds); break;
                    case "shot": Shot(f, arg); break;
                    case "cursors":
                        {
                            var list = new[] { AppCursors.Pencil, AppCursors.Fill, AppCursors.Picker, AppCursors.Magnifier, AppCursors.Cross, AppCursors.IBeam, AppCursors.Arrow };
                            using (var bmp = new Bitmap(list.Length * 56, 112))
                            {
                                using (var g = Graphics.FromImage(bmp))
                                {
                                    g.Clear(Color.Black);
                                    g.FillRectangle(Brushes.White, 0, 56, bmp.Width, 56);
                                    IntPtr hdc = g.GetHdc();
                                    for (int i = 0; i < list.Length; i++) { DrawIconEx(hdc, i * 56 + 8, 8, list[i], 0, 0, 0, IntPtr.Zero, 3); DrawIconEx(hdc, i * 56 + 8, 64, list[i], 0, 0, 0, IntPtr.Zero, 3); }
                                    g.ReleaseHdc(hdc);
                                }
                                bmp.Save(arg, ImageFormat.Png);
                            }
                            break;
                        }
                    case "state":
                        sb.AppendLine("tool=" + f.E.Tool + " brush=" + f.E.BrushType + " shape=" + f.E.ShapeType + " c1=" + Px.Hex(f.E.Color1) + " c2=" + Px.Hex(f.E.Color2)
                            + " zoom=" + f.Canvas.Zoom + " doc=" + f.E.Doc.Width + "x" + f.E.Doc.Height + " dirty=" + f.E.Doc.Dirty + " undo=" + f.E.Doc.CanUndo
                            + " sel=" + f.E.HasSelection + " pending=" + (f.E.ActiveTool?.HasPending) + " popups=" + (Popups.Top?.GetType().Name ?? "none")
                            + " text=" + f.E.TextEditing + " title=" + f.Text + " forms=" + Application.OpenForms.Count + " shape[" + f.E.Shapes.DebugInfo + "]");
                        break;
                    case "pixel":
                        {
                            int x = int.Parse(a[0]), y = int.Parse(a[1]);
                            sb.AppendLine("pixel " + x + "," + y + "=" + Px.Hex(f.E.Doc.Image.Get(x, y)) + " a=" + Px.A(f.E.Doc.Image.Get(x, y)));
                            break;
                        }
                    case "save": ImageIO.Save(f.E.Doc.Image, f.E.Doc.HasAlpha, arg, "png", 32, 96); break;
                }
            }
            return sb.Length == 0 ? "OK" : sb.ToString();
        }

        /// <summary>Composite screenshot: main window client + every open popup / dialog at its real position.</summary>
        static void Shot(MainForm f, string path)
        {
            Application.DoEvents();
            var origin = f.PointToScreen(Point.Empty);
            var size = f.ClientSize;
            using (var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp)) g.Clear(Color.Magenta);
                RenderInto(f, bmp, Point.Empty);
                foreach (Form of in Application.OpenForms)
                {
                    if (of == f || !of.Visible) continue;
                    var p = of.PointToScreen(Point.Empty);
                    RenderInto(of, bmp, new Point(p.X - origin.X, p.Y - origin.Y));
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }

        static void RenderInto(Control c, Bitmap target, Point at)
        {
            var cs = c.ClientSize;
            if (cs.Width <= 0 || cs.Height <= 0) return;
            using (var b = new Bitmap(cs.Width, cs.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(b))
                {
                    IntPtr hdc = g.GetHdc();
                    try { Native.SendMessage(c.Handle, 0x0318 /*WM_PRINTCLIENT*/, hdc, (IntPtr)(0x4 | 0x10 /*PRF_CLIENT|PRF_CHILDREN*/)); }
                    finally { g.ReleaseHdc(hdc); }
                }
                // children are not covered by WM_PRINTCLIENT on custom controls: draw them explicitly
                using (var g = Graphics.FromImage(target))
                {
                    g.DrawImageUnscaled(b, at);
                }
                foreach (Control child in c.Controls)
                {
                    if (!child.Visible) continue;
                    RenderInto(child, target, new Point(at.X + child.Left, at.Y + child.Top));
                }
            }
        }
    }
}
