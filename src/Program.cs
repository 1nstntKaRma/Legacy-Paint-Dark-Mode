using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Paint — Dark Mode")]
[assembly: AssemblyDescription("A dark-themed recreation of the classic Windows Paint")]
[assembly: AssemblyProduct("Paint Dark Mode")]
[assembly: AssemblyCopyright("Paint Dark Mode")]
[assembly: AssemblyVersion("1.4.0.0")]
[assembly: AssemblyFileVersion("1.4.0.0")]
[assembly: ComVisible(false)]

namespace PaintDark
{
    static class Program
    {
        static MainForm _main;

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--export-icon") return ExportIcon(args[1]);
            if (Array.IndexOf(args, "--test-hook") >= 0) { TestHook.Enabled = true; args = Array.FindAll(args, x => x != "--test-hook"); }
            try
            {
                var dir = Settings.DataDir;
                Directory.CreateDirectory(dir);
                // background JIT of the startup path on later launches
                System.Runtime.ProfileOptimization.SetProfileRoot(dir);
                System.Runtime.ProfileOptimization.StartProfile("startup.profile");
            }
            catch { }
            try { Native.SetCurrentProcessExplicitAppUserModelID("1nstntKaRma.PaintDarkMode"); } catch { }

            DarkMode.InitProcess();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Report(e.Exception, false);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Report(e.ExceptionObject as Exception, true);

            Settings.Load();
            _main = new MainForm(args);
            Application.Run(_main);
            return 0;
        }

        /// <summary>Build helper: writes the multi-resolution application icon (PNG entries).</summary>
        static int ExportIcon(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var images = new System.Collections.Generic.List<byte[]>();
            foreach (var s in sizes)
            {
                using (var bmp = new System.Drawing.Bitmap(s, s, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var g = System.Drawing.Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                        Icons.PaintAppIcon(g, s);
                    }
                    using (var ms = new MemoryStream()) { bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png); images.Add(ms.ToArray()); }
                }
            }
            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                    w.Write(images[i].Length); w.Write(offset);
                    offset += images[i].Length;
                }
                foreach (var img in images) w.Write(img);
            }
            return 0;
        }

        static int _reporting;

        /// <summary>Never lose a picture: on an unexpected error the current picture is saved to a recovery file.</summary>
        static void Report(Exception ex, bool fatal)
        {
            if (Interlocked.Exchange(ref _reporting, 1) == 1) return;
            try
            {
                string recovered = null;
                try
                {
                    var doc = _main?.E?.Doc;
                    if (doc != null && doc.Dirty)
                    {
                        var dir = Path.Combine(Settings.DataDir, "Recovered");
                        Directory.CreateDirectory(dir);
                        recovered = Path.Combine(dir, Path.GetFileNameWithoutExtension(doc.Title) + " (Recovered) " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ".png");
                        ImageIO.Save(doc.Image, doc.HasAlpha, recovered, "png", 32, doc.DpiX);
                    }
                }
                catch { recovered = null; }
                try { File.AppendAllText(Path.Combine(Settings.DataDir, "errors.log"), DateTime.Now + "  " + ex + Environment.NewLine + Environment.NewLine); } catch { }
                string msg = "Paint ran into a problem" + (fatal ? " and needs to close." : ", but you can keep working.") + "\n\n" + (ex?.Message ?? "Unknown error");
                if (recovered != null) msg += "\n\nA copy of your picture was saved to:\n" + recovered;
                if (_main != null && !_main.IsDisposed) MessageDialog.Show(_main, "Paint", msg, new[] { "OK" }, 0, MessageDialog.Kind.Error);
                else MessageBox.Show(msg, "Paint", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
            finally { Interlocked.Exchange(ref _reporting, 0); }
        }
    }
}
