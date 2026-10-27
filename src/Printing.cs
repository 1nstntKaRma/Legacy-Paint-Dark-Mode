using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PaintDark
{
    /// <summary>Remembered page setup (units: 1/100 inch, like System.Drawing.Printing).</summary>
    public static class PageSettings
    {
        public static string PrinterName;
        public static string PaperName = "";
        public static int PaperW = 850, PaperH = 1100;
        public static string SourceName = "";
        public static bool Landscape;
        public static int MarginL = 75, MarginT = 100, MarginR = 75, MarginB = 100;
        public static bool CenterH, CenterV;
        public static bool Fit;
        public static int AdjustPct = 100, FitW = 1, FitH = 1;

        public static void Load()
        {
            PrinterName = Settings.Get("PrnName");
            PaperName = Settings.Get("PrnPaper", "");
            PaperW = Settings.GetInt("PrnPaperW", RegionMetric ? 827 : 850);
            PaperH = Settings.GetInt("PrnPaperH", RegionMetric ? 1169 : 1100);
            SourceName = Settings.Get("PrnSource", "");
            Landscape = Settings.GetBool("PrnLandscape", false);
            MarginL = Settings.GetInt("PrnML", 75); MarginT = Settings.GetInt("PrnMT", 100);
            MarginR = Settings.GetInt("PrnMR", 75); MarginB = Settings.GetInt("PrnMB", 100);
            CenterH = Settings.GetBool("PrnCH", false); CenterV = Settings.GetBool("PrnCV", false);
            Fit = Settings.GetBool("PrnFit", false);
            AdjustPct = Math.Max(10, Math.Min(400, Settings.GetInt("PrnPct", 100)));
            FitW = Math.Max(1, Settings.GetInt("PrnFitW", 1)); FitH = Math.Max(1, Settings.GetInt("PrnFitH", 1));
        }

        public static void Save()
        {
            Settings.Set("PrnName", PrinterName); Settings.Set("PrnPaper", PaperName);
            Settings.SetInt("PrnPaperW", PaperW); Settings.SetInt("PrnPaperH", PaperH); Settings.Set("PrnSource", SourceName);
            Settings.SetBool("PrnLandscape", Landscape);
            Settings.SetInt("PrnML", MarginL); Settings.SetInt("PrnMT", MarginT); Settings.SetInt("PrnMR", MarginR); Settings.SetInt("PrnMB", MarginB);
            Settings.SetBool("PrnCH", CenterH); Settings.SetBool("PrnCV", CenterV); Settings.SetBool("PrnFit", Fit);
            Settings.SetInt("PrnPct", AdjustPct); Settings.SetInt("PrnFitW", FitW); Settings.SetInt("PrnFitH", FitH);
        }

        public static bool RegionMetric { get { try { return System.Globalization.RegionInfo.CurrentRegion.IsMetric; } catch { return false; } } }
        public static int PageW => Landscape ? PaperH : PaperW;
        public static int PageH => Landscape ? PaperW : PaperH;
    }

    /// <summary>How the picture is spread across pages.</summary>
    public struct PrintLayout
    {
        public double Scale;           // 1/100 inch per picture pixel
        public int Cols, Rows;
        public double ContentW, ContentH, AreaW, AreaH;
        public int Pages => Cols * Rows;

        public static PrintLayout Compute(Document doc, int pageW, int pageH)
        {
            var l = new PrintLayout();
            l.AreaW = Math.Max(10, pageW - PageSettings.MarginL - PageSettings.MarginR);
            l.AreaH = Math.Max(10, pageH - PageSettings.MarginT - PageSettings.MarginB);
            double dpi = doc.DpiX > 1 ? doc.DpiX : 96;
            double natW = doc.Width / dpi * 100, natH = doc.Height / dpi * 100;
            if (PageSettings.Fit)
            {
                double s = Math.Min(PageSettings.FitW * l.AreaW / natW, PageSettings.FitH * l.AreaH / natH);
                l.Scale = s * 100 / dpi;
            }
            else l.Scale = PageSettings.AdjustPct / 100.0 * 100 / dpi;
            l.ContentW = doc.Width * l.Scale; l.ContentH = doc.Height * l.Scale;
            l.Cols = Math.Max(1, (int)Math.Ceiling(l.ContentW / l.AreaW - 1e-6));
            l.Rows = Math.Max(1, (int)Math.Ceiling(l.ContentH / l.AreaH - 1e-6));
            return l;
        }

        /// <summary>Destination (page coordinates, 1/100 inch) and source (picture pixels) for page index.</summary>
        public void PageRects(int page, out RectangleF dest, out RectangleF src)
        {
            int c = page % Cols, r = page / Cols;
            double x0 = c * AreaW, y0 = r * AreaH;
            double w = Math.Min(AreaW, ContentW - x0), h = Math.Min(AreaH, ContentH - y0);
            double ox = PageSettings.MarginL, oy = PageSettings.MarginT;
            if (Cols == 1 && PageSettings.CenterH) ox += (AreaW - ContentW) / 2;
            if (Rows == 1 && PageSettings.CenterV) oy += (AreaH - ContentH) / 2;
            dest = new RectangleF((float)ox, (float)oy, (float)w, (float)h);
            src = new RectangleF((float)(x0 / Scale), (float)(y0 / Scale), (float)(w / Scale), (float)(h / Scale));
        }
    }

    public static class Printing
    {
        [DllImport("winspool.drv", CharSet = CharSet.Unicode)]
        static extern int DocumentProperties(IntPtr hwnd, IntPtr hPrinter, string device, IntPtr devOut, IntPtr devIn, int mode);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
        [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr h);

        public static List<string> Printers()
        {
            var l = new List<string>();
            try { foreach (string p in PrinterSettings.InstalledPrinters) l.Add(p); } catch { }
            return l;
        }

        public static PrinterSettings MakePrinterSettings(string name)
        {
            var ps = new PrinterSettings();
            if (!string.IsNullOrEmpty(name)) { try { ps.PrinterName = name; } catch { } }
            if (!ps.IsValid) ps = new PrinterSettings();
            return ps;
        }

        /// <summary>The printer driver's own preferences dialog.</summary>
        public static void ShowPreferences(IWin32Window owner, PrinterSettings ps, System.Drawing.Printing.PageSettings pg)
        {
            IntPtr hDev = ps.GetHdevmode(pg);
            try
            {
                IntPtr p = GlobalLock(hDev);
                int size = DocumentProperties(owner.Handle, IntPtr.Zero, ps.PrinterName, IntPtr.Zero, IntPtr.Zero, 0);
                IntPtr outBuf = Marshal.AllocHGlobal(Math.Max(size, 1024));
                int r = DocumentProperties(owner.Handle, IntPtr.Zero, ps.PrinterName, outBuf, p, 14 /*DM_IN_BUFFER|DM_IN_PROMPT|DM_OUT_BUFFER*/);
                GlobalUnlock(hDev);
                if (r == 1 /*IDOK*/) { ps.SetHdevmode(outBuf); pg.SetHdevmode(outBuf); }
                Marshal.FreeHGlobal(outBuf);
            }
            finally { GlobalFree(hDev); }
        }

        public static void ApplyPage(System.Drawing.Printing.PageSettings pg, PrinterSettings ps)
        {
            pg.Landscape = PageSettings.Landscape;
            pg.Margins = new Margins(PageSettings.MarginL, PageSettings.MarginR, PageSettings.MarginT, PageSettings.MarginB);
            try
            {
                foreach (PaperSize s in ps.PaperSizes) if (s.PaperName == PageSettings.PaperName) { pg.PaperSize = s; break; }
                foreach (PaperSource s in ps.PaperSources) if (s.SourceName == PageSettings.SourceName) { pg.PaperSource = s; break; }
            }
            catch { }
        }

        public static void Print(MainForm owner, Document doc)
        {
            var printers = Printers();
            if (printers.Count == 0) { owner.ShowError("No printers are installed. To install a printer, open Settings › Devices › Printers & scanners."); return; }
            using (var dlg = new PrintDialogDark(owner, doc))
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK) return;
                Run(owner, doc, dlg.Settings, dlg.FromPage, dlg.ToPage);
            }
        }

        static void Run(MainForm owner, Document doc, PrinterSettings ps, int fromPage, int toPage)
        {
            Bitmap bmp = null;
            try
            {
                bmp = ImageIO.Flatten(doc.Image, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                using (var pd = new PrintDocument())
                {
                    pd.DocumentName = doc.Title;
                    pd.PrinterSettings = ps;
                    pd.DefaultPageSettings.PrinterSettings = ps;
                    ApplyPage(pd.DefaultPageSettings, ps);
                    pd.PrintController = new StandardPrintController();
                    int page = Math.Max(0, fromPage - 1);
                    pd.PrintPage += (s, e) =>
                    {
                        var g = e.Graphics;
                        g.PageUnit = GraphicsUnit.Display;
                        g.TranslateTransform(-e.PageSettings.HardMarginX, -e.PageSettings.HardMarginY);
                        var pb = e.PageBounds;
                        var layout = PrintLayout.Compute(doc, pb.Width, pb.Height);
                        layout.PageRects(page, out var dest, out var src);
                        g.InterpolationMode = layout.Scale * 6 > 100 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(bmp, dest, src, GraphicsUnit.Pixel);
                        page++;
                        int last = Math.Min(layout.Pages, toPage > 0 ? toPage : layout.Pages);
                        e.HasMorePages = page < last;
                    };
                    pd.Print();
                }
            }
            catch (Exception ex)
            {
                owner.ShowError("Paint was unable to print your document. Please make sure you have sufficient disk space and that your printer is working correctly.\n\n" + ex.Message);
            }
            finally { bmp?.Dispose(); }
        }

        /// <summary>Paper size of the current printer (falls back to the remembered size).</summary>
        public static void CurrentPaper(out int w, out int h)
        {
            w = PageSettings.PageW; h = PageSettings.PageH;
        }
    }

    // ===================================================================== Print dialog
    public sealed class PrintDialogDark : DarkDialog
    {
        readonly DarkCombo _printer;
        readonly DarkTextBox _copies, _from, _to;
        readonly DarkCheck _all, _range, _collate;
        readonly DarkLabel _status;
        readonly Document _doc;
        public PrinterSettings Settings;
        public int FromPage = 1, ToPage = 0;

        public PrintDialogDark(Form owner, Document doc) : base(owner, "Print")
        {
            _doc = doc;
            SetClientSizeScaled(440, 290);
            var gp = new DarkGroup { Text = "Select Printer", Bounds = R(12, 8, 416, 96), Font = Font };
            Controls.Add(gp);
            _printer = new DarkCombo { Bounds = R(14, 28, 270, 26), Font = Font };
            _printer.Items.AddRange(Printing.Printers());
            string def = PageSettings.PrinterName;
            if (string.IsNullOrEmpty(def) || !_printer.Items.Contains(def)) { try { def = new PrinterSettings().PrinterName; } catch { } }
            _printer.SelectedIndex = Math.Max(0, _printer.Items.IndexOf(def));
            gp.Controls.Add(_printer);
            var pref = new DarkButton { Text = "P&references", Bounds = R(294, 28, 108, 26), Font = Font };
            gp.Controls.Add(pref);
            _status = new DarkLabel { Bounds = R(14, 62, 390, 24), Font = Font, Dim = true };
            gp.Controls.Add(_status);
            var gr = new DarkGroup { Text = "Page Range", Bounds = R(12, 112, 230, 120), Font = Font };
            Controls.Add(gr);
            _all = new DarkCheck { Radio = true, Text = "A&ll", Bounds = R(14, 28, 100, 22), Font = Font, Checked = true };
            _range = new DarkCheck { Radio = true, Text = "Pa&ges:", Bounds = R(14, 58, 76, 22), Font = Font };
            _from = new DarkTextBox { Bounds = R(92, 56, 46, 26), Font = Font, NumericOnly = true, Text = "1" };
            var dash = new DarkLabel { Text = "–", Bounds = R(142, 58, 14, 22), Font = Font };
            _to = new DarkTextBox { Bounds = R(158, 56, 46, 26), Font = Font, NumericOnly = true, Text = "1" };
            gr.Controls.AddRange(new Control[] { _all, _range, _from, dash, _to });
            var gc = new DarkGroup { Text = "Copies", Bounds = R(252, 112, 176, 120), Font = Font };
            Controls.Add(gc);
            gc.Controls.Add(new DarkLabel { Text = "Number of &copies:", Bounds = R(14, 28, 120, 22), Font = Font });
            _copies = new DarkTextBox { Bounds = R(14, 54, 60, 26), Font = Font, NumericOnly = true, Text = "1" };
            _collate = new DarkCheck { Text = "C&ollate", Bounds = R(84, 56, 84, 22), Font = Font, Checked = true };
            gc.Controls.Add(_copies); gc.Controls.Add(_collate);
            var ok = AddButton("&Print", 252, 250, 84, DialogResult.None, true);
            ok.Click += (s, e) => Accept();
            AddButton("Cancel", 344, 250, 84, DialogResult.Cancel);
            _printer.SelectedIndexChanged += (s, e) => UpdateInfo();
            pref.Click += (s, e) =>
            {
                var ps = Printing.MakePrinterSettings(_printer.SelectedItem);
                var pg = new System.Drawing.Printing.PageSettings(ps);
                Printing.ApplyPage(pg, ps);
                Printing.ShowPreferences(this, ps, pg);
                PageSettings.Landscape = pg.Landscape;
                try { PageSettings.PaperName = pg.PaperSize.PaperName; PageSettings.PaperW = pg.PaperSize.Width; PageSettings.PaperH = pg.PaperSize.Height; } catch { }
                _prefs = ps;
                UpdateInfo();
            };
            UpdateInfo();
        }

        PrinterSettings _prefs;

        void UpdateInfo()
        {
            var layout = PrintLayout.Compute(_doc, PageSettings.PageW, PageSettings.PageH);
            _status.Text = layout.Pages == 1 ? "The picture fits on 1 page." : "The picture will print on " + layout.Pages + " pages (" + layout.Cols + " × " + layout.Rows + ").";
            _to.Text = layout.Pages.ToString();
            _range.Enabled = _from.Enabled = _to.Enabled = layout.Pages > 1;
        }

        void Accept()
        {
            var name = _printer.SelectedItem;
            var ps = _prefs != null && _prefs.PrinterName == name ? _prefs : Printing.MakePrinterSettings(name);
            if (!ps.IsValid) { MessageDialog.Show(this, "Print", "The specified printer is invalid.\nPlease choose a valid printer.", new[] { "OK" }, 0, MessageDialog.Kind.Warning); return; }
            int copies = Math.Max(1, Math.Min(999, _copies.IntValue(1)));
            ps.Copies = (short)copies;
            ps.Collate = _collate.Checked;
            if (_range.Checked && _range.Enabled) { FromPage = Math.Max(1, _from.IntValue(1)); ToPage = Math.Max(FromPage, _to.IntValue(FromPage)); }
            PageSettings.PrinterName = name;
            Settings = ps;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // ===================================================================== Page setup dialog
    public sealed class PageSetupDialog : DarkDialog
    {
        readonly Document _doc;
        readonly DarkCombo _size, _source;
        readonly DarkCheck _portrait, _landscape, _ch, _cv, _adjust, _fit;
        readonly DarkTextBox _ml, _mt, _mr, _mb, _pct, _fw, _fh;
        readonly PagePreview _preview;
        readonly List<PaperSize> _sizes = new List<PaperSize>();
        readonly bool _metric = PageSettings.RegionMetric;

        PageSetupDialog(Form owner, Document doc) : base(owner, "Page Setup")
        {
            _doc = doc;
            SetClientSizeScaled(560, 420);
            _preview = new PagePreview(this) { Bounds = R(12, 12, 190, 210) };
            Controls.Add(_preview);
            var gp = new DarkGroup { Text = "Paper", Bounds = R(214, 8, 334, 96), Font = Font };
            Controls.Add(gp);
            gp.Controls.Add(new DarkLabel { Text = "Si&ze:", Bounds = R(14, 28, 60, 24), Font = Font });
            _size = new DarkCombo { Bounds = R(80, 26, 240, 26), Font = Font };
            gp.Controls.Add(new DarkLabel { Text = "&Source:", Bounds = R(14, 60, 60, 24), Font = Font });
            _source = new DarkCombo { Bounds = R(80, 58, 240, 26), Font = Font };
            gp.Controls.Add(_size); gp.Controls.Add(_source);
            var go = new DarkGroup { Text = "Orientation", Bounds = R(214, 112, 140, 84), Font = Font };
            Controls.Add(go);
            _portrait = new DarkCheck { Radio = true, Text = "P&ortrait", Bounds = R(14, 26, 110, 22), Font = Font };
            _landscape = new DarkCheck { Radio = true, Text = "L&andscape", Bounds = R(14, 52, 110, 22), Font = Font };
            go.Controls.Add(_portrait); go.Controls.Add(_landscape);
            string unit = _metric ? "Margins (millimeters)" : "Margins (inches)";
            var gm = new DarkGroup { Text = unit, Bounds = R(364, 112, 184, 84), Font = Font };
            Controls.Add(gm);
            _ml = MarginBox(gm, "&Left:", 6, 24); _mr = MarginBox(gm, "&Right:", 6, 52);
            _mt = MarginBox(gm, "&Top:", 94, 24); _mb = MarginBox(gm, "&Bottom:", 94, 52);
            var gce = new DarkGroup { Text = "Centering", Bounds = R(12, 230, 190, 84), Font = Font };
            Controls.Add(gce);
            _ch = new DarkCheck { Text = "&Horizontal", Bounds = R(14, 26, 160, 22), Font = Font };
            _cv = new DarkCheck { Text = "&Vertical", Bounds = R(14, 52, 160, 22), Font = Font };
            gce.Controls.Add(_ch); gce.Controls.Add(_cv);
            var gs = new DarkGroup { Text = "Scaling", Bounds = R(214, 204, 334, 110), Font = Font };
            Controls.Add(gs);
            _adjust = new DarkCheck { Radio = true, Text = "A&djust to:", Bounds = R(14, 28, 100, 24), Font = Font };
            _pct = new DarkTextBox { Bounds = R(120, 26, 56, 26), Font = Font, NumericOnly = true };
            gs.Controls.Add(new DarkLabel { Text = "% normal size", Bounds = R(184, 28, 120, 24), Font = Font });
            _fit = new DarkCheck { Radio = true, Text = "&Fit to:", Bounds = R(14, 66, 100, 24), Font = Font };
            _fw = new DarkTextBox { Bounds = R(120, 64, 42, 26), Font = Font, NumericOnly = true };
            gs.Controls.Add(new DarkLabel { Text = "by", Bounds = R(168, 66, 22, 24), Font = Font });
            _fh = new DarkTextBox { Bounds = R(192, 64, 42, 26), Font = Font, NumericOnly = true };
            gs.Controls.Add(new DarkLabel { Text = "page(s)", Bounds = R(240, 66, 70, 24), Font = Font });
            gs.Controls.AddRange(new Control[] { _adjust, _pct, _fit, _fw, _fh });
            var ok = AddButton("OK", 372, 380, 84, DialogResult.None, true);
            ok.Click += (s, e) => { if (Store()) { DialogResult = DialogResult.OK; Close(); } };
            AddButton("Cancel", 464, 380, 84, DialogResult.Cancel);

            // values
            var ps = Printing.MakePrinterSettings(PageSettings.PrinterName);
            try
            {
                foreach (PaperSize s in ps.PaperSizes) { _sizes.Add(s); _size.Items.Add(s.PaperName); }
                foreach (PaperSource s in ps.PaperSources) _source.Items.Add(s.SourceName);
            }
            catch { }
            if (_sizes.Count == 0)
            {
                _sizes.Add(new PaperSize("Letter", 850, 1100)); _sizes.Add(new PaperSize("A4", 827, 1169)); _sizes.Add(new PaperSize("Legal", 850, 1400)); _sizes.Add(new PaperSize("A3", 1169, 1654));
                foreach (var s in _sizes) _size.Items.Add(s.PaperName);
            }
            if (_source.Items.Count == 0) _source.Items.Add("Automatically Select");
            int si = _sizes.FindIndex(s => s.PaperName == PageSettings.PaperName);
            if (si < 0) si = _sizes.FindIndex(s => Math.Abs(s.Width - PageSettings.PaperW) < 5 && Math.Abs(s.Height - PageSettings.PaperH) < 5);
            _size.SelectedIndex = Math.Max(0, si);
            _source.SelectedIndex = Math.Max(0, _source.Items.IndexOf(PageSettings.SourceName));
            (PageSettings.Landscape ? _landscape : _portrait).Checked = true;
            SetMargin(_ml, PageSettings.MarginL); SetMargin(_mt, PageSettings.MarginT); SetMargin(_mr, PageSettings.MarginR); SetMargin(_mb, PageSettings.MarginB);
            _ch.Checked = PageSettings.CenterH; _cv.Checked = PageSettings.CenterV;
            (PageSettings.Fit ? _fit : _adjust).Checked = true;
            _pct.Text = PageSettings.AdjustPct.ToString(); _fw.Text = PageSettings.FitW.ToString(); _fh.Text = PageSettings.FitH.ToString();
            foreach (Control c in new Control[] { _ml, _mt, _mr, _mb, _pct, _fw, _fh }) c.TextChanged += (s, e) => _preview.Invalidate();
            foreach (var c in new[] { _portrait, _landscape, _ch, _cv, _adjust, _fit }) c.CheckedChanged += (s, e) => _preview.Invalidate();
            _size.SelectedIndexChanged += (s, e) => _preview.Invalidate();
        }

        DarkTextBox MarginBox(Control parent, string label, int x, int y)
        {
            var l = new DarkLabel { Text = label, Bounds = R(x, y + 2, 46, 22), Font = Font };
            var t = new DarkTextBox { Bounds = R(x + 46, y, 38, 24), Font = Font, NumericOnly = true, AllowDecimal = true };
            l.MnemonicTarget = t;
            parent.Controls.Add(l); parent.Controls.Add(t);
            return t;
        }

        void SetMargin(DarkTextBox t, int hundredths) => t.Text = _metric ? Math.Round(hundredths * 0.254, 1).ToString() : (hundredths / 100.0).ToString("0.##");
        int GetMargin(DarkTextBox t) { double v = Math.Max(0, t.DoubleValue(0)); return (int)Math.Round(_metric ? v / 0.254 : v * 100); }

        // working copy used by the preview
        public void Snapshot(out int pw, out int ph, out bool landscape)
        {
            var s = _sizes[Math.Max(0, _size.SelectedIndex)];
            landscape = _landscape.Checked;
            pw = landscape ? s.Height : s.Width; ph = landscape ? s.Width : s.Height;
        }

        bool Store()
        {
            int pct = _pct.IntValue(100), fw = _fw.IntValue(1), fh = _fh.IntValue(1);
            if (_adjust.Checked && (pct < 10 || pct > 400)) { MessageDialog.Show(this, "Page Setup", "Enter a scaling between 10 and 400 percent.", new[] { "OK" }, 0, MessageDialog.Kind.Warning); return false; }
            if (_fit.Checked && (fw < 1 || fh < 1 || fw > 50 || fh > 50)) { MessageDialog.Show(this, "Page Setup", "Enter a number of pages between 1 and 50.", new[] { "OK" }, 0, MessageDialog.Kind.Warning); return false; }
            var s = _sizes[Math.Max(0, _size.SelectedIndex)];
            PageSettings.PaperName = s.PaperName; PageSettings.PaperW = s.Width; PageSettings.PaperH = s.Height;
            PageSettings.SourceName = _source.SelectedItem ?? "";
            PageSettings.Landscape = _landscape.Checked;
            PageSettings.MarginL = GetMargin(_ml); PageSettings.MarginT = GetMargin(_mt); PageSettings.MarginR = GetMargin(_mr); PageSettings.MarginB = GetMargin(_mb);
            PageSettings.CenterH = _ch.Checked; PageSettings.CenterV = _cv.Checked;
            PageSettings.Fit = _fit.Checked; PageSettings.AdjustPct = pct; PageSettings.FitW = fw; PageSettings.FitH = fh;
            PageSettings.Save();
            return true;
        }

        public static bool ShowFor(Form owner, Document doc)
        {
            using (var d = new PageSetupDialog(owner, doc)) return d.ShowDialog(owner) == DialogResult.OK;
        }

        sealed class PagePreview : Control
        {
            readonly PageSetupDialog P;
            public PagePreview(PageSetupDialog p) { P = p; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.DialogPanel);
                P.Snapshot(out int pw, out int ph, out bool land);
                // temporarily apply the dialog's values to compute the layout
                var save = (PageSettings.MarginL, PageSettings.MarginT, PageSettings.MarginR, PageSettings.MarginB, PageSettings.CenterH, PageSettings.CenterV, PageSettings.Fit, PageSettings.AdjustPct, PageSettings.FitW, PageSettings.FitH);
                PageSettings.MarginL = P.GetMargin(P._ml); PageSettings.MarginT = P.GetMargin(P._mt); PageSettings.MarginR = P.GetMargin(P._mr); PageSettings.MarginB = P.GetMargin(P._mb);
                PageSettings.CenterH = P._ch.Checked; PageSettings.CenterV = P._cv.Checked; PageSettings.Fit = P._fit.Checked;
                PageSettings.AdjustPct = Math.Max(10, Math.Min(400, P._pct.IntValue(100))); PageSettings.FitW = Math.Max(1, P._fw.IntValue(1)); PageSettings.FitH = Math.Max(1, P._fh.IntValue(1));
                var layout = PrintLayout.Compute(P._doc, pw, ph);
                float s = Math.Min((Width - 24f) / pw, (Height - 24f) / ph);
                var page = new RectangleF((Width - pw * s) / 2, (Height - ph * s) / 2, pw * s, ph * s);
                g.FillRectangle(Brushes.Black, page.X + 3, page.Y + 3, page.Width, page.Height);
                g.FillRectangle(Brushes.White, page);
                layout.PageRects(0, out var dest, out var src);
                using (var b = new SolidBrush(Theme.C(0x9AB8D8))) g.FillRectangle(b, page.X + dest.X * s, page.Y + dest.Y * s, dest.Width * s, dest.Height * s);
                using (var pen = new Pen(Theme.C(0xB0B0B0)) { DashPattern = new[] { 2f, 2f } })
                    g.DrawRectangle(pen, page.X + PageSettings.MarginL * s, page.Y + PageSettings.MarginT * s, (pw - PageSettings.MarginL - PageSettings.MarginR) * s, (ph - PageSettings.MarginT - PageSettings.MarginB) * s);
                (PageSettings.MarginL, PageSettings.MarginT, PageSettings.MarginR, PageSettings.MarginB, PageSettings.CenterH, PageSettings.CenterV, PageSettings.Fit, PageSettings.AdjustPct, PageSettings.FitW, PageSettings.FitH) = save;
                TextRenderer.DrawText(g, "Preview", Font, new Point(6, 4), Theme.TextDim);
            }
        }
    }

    // ===================================================================== Print preview (ribbon mode)
    public sealed class PrintPreviewView : DarkControl
    {
        readonly MainForm F;
        readonly Document _doc;
        Bitmap _bmp;
        public int Pages = 1;
        int _page;
        float _zoom = 1f;
        Point _pan;
        bool _dragging; Point _dragStart, _panStart;

        public PrintPreviewView(MainForm f, Document doc)
        {
            F = f; _doc = doc;
            _bmp = ImageIO.Flatten(doc.Image, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            TabStop = true;
        }

        PrintLayout CurLayout => PrintLayout.Compute(_doc, PageSettings.PageW, PageSettings.PageH);
        public void SetPages(int n) { Pages = n; _page = Math.Min(_page, Math.Max(0, CurLayout.Pages - 1)); Invalidate(); F.Ribbon.Invalidate(); }
        public bool CanStep(int d) { int n = _page + d * Pages; return n >= 0 && n < CurLayout.Pages; }
        public void Step(int d) { if (CanStep(d)) { _page += d * Pages; Invalidate(); F.Ribbon.Invalidate(); } }
        public void ZoomStep(int d) { _zoom = Math.Max(0.25f, Math.Min(8f, d > 0 ? _zoom * 1.5f : _zoom / 1.5f)); if (_zoom <= 1.01f) _pan = Point.Empty; Invalidate(); }

        protected override void Render(Graphics g, Rectangle clip)
        {
            Draw.Fill(g, ClientRectangle, Theme.Workspace);
            var layout = CurLayout;
            int pw = PageSettings.PageW, ph = PageSettings.PageH;
            int n = Math.Min(Pages, Math.Max(1, layout.Pages - _page));
            int gap = D.S(24);
            float avW = (Width - gap * (Pages + 1)) / (float)Pages, avH = Height - gap * 2;
            float s = Math.Min(avW / pw, avH / ph) * _zoom;
            float totalW = Pages * pw * s + (Pages - 1) * gap;
            float x = (Width - totalW) / 2 + _pan.X, y = (Height - ph * s) / 2 + _pan.Y;
            g.SmoothingMode = SmoothingMode.None;
            for (int i = 0; i < n; i++)
            {
                var page = new RectangleF(x + i * (pw * s + gap), y, pw * s, ph * s);
                using (var sh = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) g.FillRectangle(sh, page.X + D.S(4), page.Y + D.S(4), page.Width, page.Height);
                g.FillRectangle(Brushes.White, page);
                layout.PageRects(_page + i, out var dest, out var src);
                g.InterpolationMode = s * layout.Scale > 1.5f ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(_bmp, new RectangleF(page.X + dest.X * s, page.Y + dest.Y * s, dest.Width * s, dest.Height * s), src, GraphicsUnit.Pixel);
                g.PixelOffsetMode = PixelOffsetMode.Default;
                using (var p = new Pen(Theme.C(0x606060))) g.DrawRectangle(p, page.X, page.Y, page.Width, page.Height);
            }
            string info = layout.Pages > 1 ? "Page " + (_page + 1) + (n > 1 ? "–" + (_page + n) : "") + " of " + layout.Pages : "1 page";
            Draw.Text(g, info, Theme.UiFont(D), new Rectangle(D.S(10), Height - D.S(26), D.S(300), D.S(22)), Theme.TextDim);
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); if (_zoom > 1.01f) { _dragging = true; _dragStart = e.Location; _panStart = _pan; Cursor = Cursors.SizeAll; } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (_dragging) { _pan = new Point(_panStart.X + e.X - _dragStart.X, _panStart.Y + e.Y - _dragStart.Y); Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _dragging = false; Cursor = Cursors.Default; }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Native.CtrlDown) { ZoomStep(e.Delta > 0 ? 1 : -1); return; }
            if (_zoom > 1.01f) { _pan.Y += e.Delta / 2; Invalidate(); }
            else Step(e.Delta > 0 ? -1 : 1);
        }

        protected override void Dispose(bool disposing) { if (disposing) _bmp?.Dispose(); base.Dispose(disposing); }
    }
}
