using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace PaintDark
{
    // ===================================================================== Resize and Skew
    public sealed class ResizeDialog : DarkDialog
    {
        readonly DarkCheck _pct, _px, _keep;
        readonly DarkTextBox _h, _v, _sh, _sv;
        readonly int _w0, _h0;
        bool _updating;

        ResizeDialog(Form owner, int w, int h) : base(owner, "Resize and Skew")
        {
            _w0 = w; _h0 = h;
            SetClientSizeScaled(292, 368);
            var g1 = new DarkGroup { Text = "Resize", Bounds = R(12, 8, 268, 170), Font = Font };
            var g2 = new DarkGroup { Text = "Skew (Degrees)", Bounds = R(12, 188, 268, 120), Font = Font };
            Controls.Add(g1); Controls.Add(g2);
            var by = new DarkLabel { Text = "By:", Bounds = R(16, 30, 30, 22), Font = Font };
            _pct = new DarkCheck { Radio = true, Text = "Percentage", Bounds = R(60, 30, 104, 22), Font = Font, Checked = true };
            _px = new DarkCheck { Radio = true, Text = "Pixels", Bounds = R(170, 30, 90, 22), Font = Font };
            g1.Controls.AddRange(new Control[] { by, _pct, _px });
            _h = Num(g1, 0, 66, "&Horizontal:", false);
            _v = Num(g1, 1, 100, "&Vertical:", false);
            _keep = new DarkCheck { Text = "&Maintain aspect ratio", Bounds = R(16, 136, 220, 22), Font = Font, Checked = true };
            g1.Controls.Add(_keep);
            _sh = Num(g2, 2, 32, "H&orizontal:", true);
            _sv = Num(g2, 3, 66, "V&ertical:", true);
            _h.Text = "100"; _v.Text = "100"; _sh.Text = "0"; _sv.Text = "0";
            _pct.CheckedChanged += (s, e) => { if (_pct.Checked) SwitchMode(); };
            _px.CheckedChanged += (s, e) => { if (_px.Checked) SwitchMode(); };
            _h.TextChanged += (s, e) => Linked(true);
            _v.TextChanged += (s, e) => Linked(false);
            var ok = AddButton("OK", 104, 326, 84, DialogResult.None, true);
            ok.Click += (s, e) => Accept();
            AddButton("Cancel", 196, 326, 84, DialogResult.Cancel);
            _pct.TabIndex = 0; _px.TabIndex = 1; _h.TabIndex = 2; _v.TabIndex = 3; _keep.TabIndex = 4; _sh.TabIndex = 5; _sv.TabIndex = 6;
        }

        DarkTextBox Num(Control parent, int icon, int y, string label, bool neg)
        {
            var ic = new IconBox { Kind = icon, Bounds = R(16, y, 24, 24) };
            var l = new DarkLabel { Text = label, Bounds = R(48, y, 100, 24), Font = Font };
            var t = new DarkTextBox { Bounds = R(160, y, 90, 26), Font = Font, NumericOnly = true, AllowNegative = neg };
            l.MnemonicTarget = t;
            parent.Controls.Add(ic); parent.Controls.Add(l); parent.Controls.Add(t);
            return t;
        }

        void SwitchMode()
        {
            _updating = true;
            if (_px.Checked) { _h.Text = _w0.ToString(); _v.Text = _h0.ToString(); }
            else { _h.Text = "100"; _v.Text = "100"; }
            _updating = false;
        }

        void Linked(bool fromH)
        {
            if (_updating || !_keep.Checked) return;
            _updating = true;
            if (_pct.Checked) { if (fromH) _v.Text = _h.Text; else _h.Text = _v.Text; }
            else
            {
                if (fromH && int.TryParse(_h.Text, out int w)) _v.Text = Math.Max(1, (int)Math.Round(w * (double)_h0 / _w0)).ToString();
                else if (!fromH && int.TryParse(_v.Text, out int hh)) _h.Text = Math.Max(1, (int)Math.Round(hh * (double)_w0 / _h0)).ToString();
            }
            _updating = false;
        }

        void Accept()
        {
            int a = _h.IntValue(-1), b = _v.IntValue(-1);
            int sh = _sh.IntValue(int.MinValue), sv = _sv.IntValue(int.MinValue);
            if (_pct.Checked)
            {
                if (a < 1 || a > 500) { Bad(_h, "Enter a number between 1 and 500."); return; }
                if (b < 1 || b > 500) { Bad(_v, "Enter a number between 1 and 500."); return; }
                ResultW = Math.Max(1, (int)Math.Round(_w0 * a / 100.0)); ResultH = Math.Max(1, (int)Math.Round(_h0 * b / 100.0));
            }
            else
            {
                if (a < 1 || a > 50000) { Bad(_h, "Enter a number between 1 and 50000."); return; }
                if (b < 1 || b > 50000) { Bad(_v, "Enter a number between 1 and 50000."); return; }
                ResultW = a; ResultH = b;
            }
            if (sh < -89 || sh > 89) { Bad(_sh, "Enter a number between -89 and 89."); return; }
            if (sv < -89 || sv > 89) { Bad(_sv, "Enter a number between -89 and 89."); return; }
            SkewH = sh; SkewV = sv;
            DialogResult = DialogResult.OK;
            Close();
        }

        void Bad(DarkTextBox t, string msg)
        {
            MessageDialog.Show(this, "Resize and Skew", msg, new[] { "OK" }, 0, MessageDialog.Kind.Warning);
            t.SelectAll();
        }

        public int ResultW, ResultH, SkewH, SkewV;

        public static void ShowFor(MainForm f, Editor e)
        {
            Size s;
            if (e.HasSelection) s = e.Select.SelectionRect.Size;
            else s = e.Doc.Size;
            using (var d = new ResizeDialog(f, s.Width, s.Height))
            {
                if (d.ShowDialog(f) != DialogResult.OK) return;
                if (d.ResultW == s.Width && d.ResultH == s.Height && d.SkewH == 0 && d.SkewV == 0) return;
                e.ResizeSkew(d.ResultW, d.ResultH, d.SkewH, d.SkewV);
            }
        }

        sealed class IconBox : Control
        {
            public int Kind;
            public IconBox() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Parent.BackColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float s = Width / 24f;
                g.ScaleTransform(s, s);
                using (var pen = new Pen(Theme.C(0x4BA3F2), 1.6f))
                using (var page = new SolidBrush(Theme.C(0xD9D9D9)))
                {
                    switch (Kind)
                    {
                        case 0: g.FillRectangle(page, 7, 5, 10, 14); g.DrawLine(pen, 2, 12, 22, 12); g.DrawLines(pen, new[] { new PointF(5, 9), new PointF(2, 12), new PointF(5, 15) }); g.DrawLines(pen, new[] { new PointF(19, 9), new PointF(22, 12), new PointF(19, 15) }); break;
                        case 1: g.FillRectangle(page, 5, 7, 14, 10); g.DrawLine(pen, 12, 2, 12, 22); g.DrawLines(pen, new[] { new PointF(9, 5), new PointF(12, 2), new PointF(15, 5) }); g.DrawLines(pen, new[] { new PointF(9, 19), new PointF(12, 22), new PointF(15, 19) }); break;
                        case 2: g.FillPolygon(page, new[] { new PointF(9, 4), new PointF(21, 4), new PointF(15, 20), new PointF(3, 20) }); break;
                        default: g.FillPolygon(page, new[] { new PointF(4, 9), new PointF(20, 3), new PointF(20, 15), new PointF(4, 21) }); break;
                    }
                }
            }
        }
    }

    // ===================================================================== Image Properties
    public sealed class PropertiesDialog : DarkDialog
    {
        readonly Editor E;
        readonly DarkCheck _in, _cm, _px, _bw, _color;
        readonly DarkTextBox _w, _h;
        Units _units;
        double _wPx, _hPx;
        bool _updating;

        PropertiesDialog(Form owner, Editor e) : base(owner, "Image Properties")
        {
            E = e;
            SetClientSizeScaled(340, 318);
            var d = e.Doc;
            var fa = new DarkGroup { Text = "File attributes", Bounds = R(12, 8, 316, 104), Font = Font };
            Controls.Add(fa);
            string saved = d.LastSaved.HasValue ? d.LastSaved.Value.ToString("g") : "Not Available";
            string size = d.FileSize >= 0 ? FormatBytes(d.FileSize) : "Not Available";
            Row(fa, 28, "Last Saved:", saved);
            Row(fa, 52, "Size on disk:", size);
            Row(fa, 76, "Resolution:", Math.Round(d.DpiX) + " DPI");
            var gu = new DarkGroup { Text = "Units", Bounds = R(12, 120, 150, 104), Font = Font };
            var gc = new DarkGroup { Text = "Colors", Bounds = R(174, 120, 154, 104), Font = Font };
            Controls.Add(gu); Controls.Add(gc);
            _in = new DarkCheck { Radio = true, Text = "&Inches", Bounds = R(14, 26, 130, 22), Font = Font };
            _cm = new DarkCheck { Radio = true, Text = "Centi&meters", Bounds = R(14, 50, 130, 22), Font = Font };
            _px = new DarkCheck { Radio = true, Text = "&Pixels", Bounds = R(14, 74, 130, 22), Font = Font };
            gu.Controls.AddRange(new Control[] { _in, _cm, _px });
            _bw = new DarkCheck { Radio = true, Text = "&Black and white", Bounds = R(14, 26, 136, 22), Font = Font };
            _color = new DarkCheck { Radio = true, Text = "Co&lor", Bounds = R(14, 50, 136, 22), Font = Font, Checked = true };
            gc.Controls.AddRange(new Control[] { _bw, _color });
            var lw = AddLabel("&Width:", 14, 236, 50);
            _w = new DarkTextBox { Bounds = R(66, 233, 80, 26), Font = Font, NumericOnly = true, AllowDecimal = true };
            var lh = AddLabel("&Height:", 160, 236, 54);
            _h = new DarkTextBox { Bounds = R(216, 233, 80, 26), Font = Font, NumericOnly = true, AllowDecimal = true };
            lw.MnemonicTarget = _w; lh.MnemonicTarget = _h;
            Controls.Add(_w); Controls.Add(_h);
            var def = AddButton("&Default", 12, 280, 90, DialogResult.None);
            def.Click += (s, a) => { var ds = MainForm.DefaultImageSize; _wPx = ds.Width; _hPx = ds.Height; ShowValues(); };
            var ok = AddButton("OK", 152, 280, 84, DialogResult.None, true);
            ok.Click += (s, a) => Accept();
            AddButton("Cancel", 244, 280, 84, DialogResult.Cancel);
            _wPx = d.Width; _hPx = d.Height;
            _units = e.Units;
            (_units == Units.Inches ? _in : _units == Units.Centimeters ? _cm : _px).Checked = true;
            EventHandler unitsChanged = (s, a) =>
            {
                if (!((DarkCheck)s).Checked) return;
                ReadValues();
                _units = _in.Checked ? Units.Inches : _cm.Checked ? Units.Centimeters : Units.Pixels;
                ShowValues();
            };
            _in.CheckedChanged += unitsChanged; _cm.CheckedChanged += unitsChanged; _px.CheckedChanged += unitsChanged;
            ShowValues();
        }

        void Row(Control parent, int y, string label, string value)
        {
            parent.Controls.Add(new DarkLabel { Text = label, Bounds = R(14, y, 100, 20), Font = Font, Dim = true });
            parent.Controls.Add(new DarkLabel { Text = value, Bounds = R(118, y, 190, 20), Font = Font });
        }

        static string FormatBytes(long b) => b < 1024 ? b + " bytes" : b < 1048576 ? (b / 1024.0).ToString("0.#") + "KB" : (b / 1048576.0).ToString("0.##") + "MB";

        double Factor => _units == Units.Inches ? 1.0 / E.Doc.DpiX : _units == Units.Centimeters ? 2.54 / E.Doc.DpiX : 1.0;

        void ShowValues()
        {
            _updating = true;
            string fmt = _units == Units.Pixels ? "0" : "0.##";
            _w.Text = (_wPx * Factor).ToString(fmt);
            _h.Text = (_hPx * Factor).ToString(fmt);
            _updating = false;
        }

        void ReadValues()
        {
            double w = _w.DoubleValue(-1), h = _h.DoubleValue(-1);
            if (w > 0) _wPx = w / Factor;
            if (h > 0) _hPx = h / Factor;
        }

        void Accept()
        {
            double w = _w.DoubleValue(-1), h = _h.DoubleValue(-1);
            if (w <= 0 || h <= 0) { MessageDialog.Show(this, "Image Properties", "Bitmaps must be greater than one pixel on a side.", new[] { "OK" }, 0, MessageDialog.Kind.Warning); return; }
            int pw = (int)Math.Round(w / Factor), ph = (int)Math.Round(h / Factor);
            if (pw < 1 || ph < 1 || pw > 100000 || ph > 100000) { MessageDialog.Show(this, "Image Properties", "Bitmaps must be greater than one pixel on a side.", new[] { "OK" }, 0, MessageDialog.Kind.Warning); return; }
            if (_bw.Checked)
            {
                if (MessageDialog.Show(this, "Paint", "Converting to black and white may cause some loss of color information.\nDo you want to continue?", new[] { "OK", "Cancel" }, 1, MessageDialog.Kind.Warning) != 0) return;
            }
            NewSize = new Size(pw, ph);
            ToBw = _bw.Checked;
            NewUnits = _units;
            DialogResult = DialogResult.OK;
            Close();
        }

        public Size NewSize;
        public bool ToBw;
        public Units NewUnits;

        public static void ShowFor(MainForm f, Editor e)
        {
            using (var d = new PropertiesDialog(f, e))
            {
                if (d.ShowDialog(f) != DialogResult.OK) return;
                e.Units = d.NewUnits;
                if (d.NewSize != e.Doc.Size) e.ResizeCanvas(d.NewSize);
                if (d.ToBw) e.ConvertToBlackAndWhite();
                e.Notify(Change.Status);
            }
        }
    }

    // ===================================================================== Edit Colors
    public sealed class ColorPickerDialog : DarkDialog
    {
        static readonly uint[] Basic =
        {
            0xFF8080, 0xFFFF80, 0x80FF80, 0x00FF80, 0x80FFFF, 0x0080FF, 0xFF80C0, 0xFF80FF,
            0xFF0000, 0xFFFF00, 0x80FF00, 0x00FF40, 0x00FFFF, 0x0080C0, 0x8080C0, 0xFF00FF,
            0x804040, 0xFF8040, 0x00FF00, 0x008080, 0x004080, 0x8080FF, 0x800040, 0xFF0080,
            0x800000, 0xFF8000, 0x008000, 0x008040, 0x0000FF, 0x0000A0, 0x800080, 0x8000FF,
            0x400000, 0x804000, 0x004000, 0x004040, 0x000080, 0x000040, 0x400040, 0x400080,
            0x000000, 0x808000, 0x808040, 0x808080, 0x408080, 0xC0C0C0, 0x400040, 0xFFFFFF,
        };
        static uint[] _custom;
        static int _customSlot;

        int _hue, _sat, _lum;
        uint _color;
        readonly ColorSurface _surface;
        readonly DarkTextBox _th, _ts, _tl, _tr, _tg, _tb, _hex;
        bool _updating;

        ColorPickerDialog(Form owner, uint start) : base(owner, "Edit Colors")
        {
            if (_custom == null)
            {
                _custom = new uint[16];
                for (int i = 0; i < 16; i++)
                {
                    var s = Settings.Get("DlgCustom" + i);
                    _custom[i] = s != null && uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out uint c) ? c | 0xFF000000u : 0xFF000000u;
                }
            }
            SetClientSizeScaled(488, 344);
            _surface = new ColorSurface(this) { Bounds = new Rectangle(0, 0, D.S(488), D.S(292)) };
            Controls.Add(_surface);
            int x1 = 312, x2 = 400;
            _th = Field("Hu&e:", x1, 212); _ts = Field("&Sat:", x1, 240); _tl = Field("&Lum:", x1, 268);
            _tr = Field("&Red:", x2, 212); _tg = Field("&Green:", x2, 240); _tb = Field("Bl&ue:", x2, 268);
            var hl = AddLabel("He&x:", 236, 268, 34);
            _hex = new DarkTextBox { Bounds = R(268, 265, 0, 0), Font = Font };
            _hex.Bounds = new Rectangle(D.S(236), D.S(240), D.S(66), D.S(24));
            hl.Bounds = new Rectangle(D.S(236), D.S(266), D.S(66), D.S(20));
            hl.Text = "Hex (#RGB)";
            hl.Dim = true;
            hl.MnemonicTarget = _hex;
            Controls.Add(_hex);
            foreach (var t in new[] { _th, _ts, _tl, _tr, _tg, _tb }) t.TextChanged += (s, e) => FieldChanged((DarkTextBox)s);
            _hex.TextChanged += (s, e) => HexChanged();
            var add = AddButton("&Add to Custom Colors", 236, 304, 240, DialogResult.None);
            add.Click += (s, e) => { _custom[_customSlot] = _color; Settings.Set("DlgCustom" + _customSlot, (_color & 0xFFFFFF).ToString("X6")); _customSlot = (_customSlot + 1) % 16; _surface.Invalidate(); };
            AddButton("OK", 12, 304, 104, DialogResult.OK, true);
            AddButton("Cancel", 124, 304, 104, DialogResult.Cancel);
            SetColor(start | 0xFF000000u, true);
        }

        DarkTextBox Field(string label, int x, int y)
        {
            var l = AddLabel(label, x, y + 2, 46);
            var t = new DarkTextBox { Bounds = R(x + 44, y, 40, 24), Font = Font, NumericOnly = true };
            l.MnemonicTarget = t;
            Controls.Add(t);
            return t;
        }

        void SetColor(uint c, bool updateHsl)
        {
            _color = c | 0xFF000000u;
            if (updateHsl) { Px.ToHsl(_color, out _hue, out _sat, out _lum); }
            _updating = true;
            _th.Text = _hue.ToString(); _ts.Text = _sat.ToString(); _tl.Text = _lum.ToString();
            _tr.Text = Px.R(_color).ToString(); _tg.Text = Px.G(_color).ToString(); _tb.Text = Px.B(_color).ToString();
            if (!_hex.Box.Focused) _hex.Text = Px.Hex(_color);
            _updating = false;
            _surface.Invalidate();
        }

        void SetHsl(int h, int s, int l)
        {
            _hue = Math.Max(0, Math.Min(239, h)); _sat = Math.Max(0, Math.Min(240, s)); _lum = Math.Max(0, Math.Min(240, l));
            SetColor(Px.FromHsl(_hue, _sat, _lum), false);
        }

        void FieldChanged(DarkTextBox t)
        {
            if (_updating) return;
            int v = t.IntValue(0);
            if (t == _th || t == _ts || t == _tl)
            {
                int h = t == _th ? Math.Min(239, v) : _hue, s = t == _ts ? Math.Min(240, v) : _sat, l = t == _tl ? Math.Min(240, v) : _lum;
                _hue = h; _sat = s; _lum = l;
                _color = Px.FromHsl(h, s, l);
                _updating = true;
                _tr.Text = Px.R(_color).ToString(); _tg.Text = Px.G(_color).ToString(); _tb.Text = Px.B(_color).ToString();
                if (!_hex.Box.Focused) _hex.Text = Px.Hex(_color);
                _updating = false;
            }
            else
            {
                int r = Math.Min(255, _tr.IntValue(0)), g = Math.Min(255, _tg.IntValue(0)), b = Math.Min(255, _tb.IntValue(0));
                _color = Px.Rgb(r, g, b);
                Px.ToHsl(_color, out _hue, out _sat, out _lum);
                _updating = true;
                _th.Text = _hue.ToString(); _ts.Text = _sat.ToString(); _tl.Text = _lum.ToString();
                if (!_hex.Box.Focused) _hex.Text = Px.Hex(_color);
                _updating = false;
            }
            _surface.Invalidate();
        }

        void HexChanged()
        {
            if (_updating || !_hex.Box.Focused) return;
            var s = _hex.Text.Trim().TrimStart('#');
            if (s.Length == 3) s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });
            if (s.Length == 6 && uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out uint c)) SetColor(c | 0xFF000000u, true);
        }

        public static bool Pick(Form owner, uint start, out uint result)
        {
            using (var d = new ColorPickerDialog(owner, start))
            {
                bool ok = d.ShowDialog(owner) == DialogResult.OK;
                result = d._color;
                return ok;
            }
        }

        /// <summary>Swatches, spectrum, luminance bar and preview.</summary>
        sealed class ColorSurface : Control
        {
            readonly ColorPickerDialog P;
            Bitmap _spectrum;
            int _drag;      // 1 spectrum, 2 lum
            Dpi D => P.D;

            public ColorSurface(ColorPickerDialog p)
            {
                P = p;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            Rectangle SpecRect => new Rectangle(D.S(236), D.S(12), D.S(200), D.S(186));
            Rectangle LumRect => new Rectangle(D.S(446), D.S(12), D.S(14), D.S(186));
            Rectangle Preview => new Rectangle(D.S(236), D.S(208), D.S(66), D.S(28));
            Rectangle BasicCell(int i) => new Rectangle(D.S(12) + (i % 8) * D.S(27), D.S(32) + (i / 8) * D.S(23), D.S(22), D.S(18));
            Rectangle CustomCell(int i) => new Rectangle(D.S(12) + (i % 8) * D.S(27), D.S(200) + (i / 8) * D.S(23), D.S(22), D.S(18));

            unsafe void BuildSpectrum()
            {
                var r = SpecRect;
                if (_spectrum != null && _spectrum.Width == r.Width && _spectrum.Height == r.Height) return;
                _spectrum?.Dispose();
                _spectrum = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
                var data = _spectrum.LockBits(new Rectangle(0, 0, r.Width, r.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                for (int y = 0; y < r.Height; y++)
                {
                    uint* row = (uint*)((byte*)data.Scan0 + y * data.Stride);
                    int s = 240 - y * 240 / Math.Max(1, r.Height - 1);
                    for (int x = 0; x < r.Width; x++) row[x] = Px.FromHsl(x * 239 / Math.Max(1, r.Width - 1), s, 120);
                }
                _spectrum.UnlockBits(data);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Dialog);
                var f = Font;
                TextRenderer.DrawText(g, "&Basic colors:", f, new Point(D.S(12), D.S(10)), Theme.Text, TextFormatFlags.HidePrefix);
                for (int i = 0; i < 48; i++) Swatch(g, BasicCell(i), Basic[i] | 0xFF000000u, (Basic[i] | 0xFF000000u) == P._color);
                TextRenderer.DrawText(g, "&Custom colors:", f, new Point(D.S(12), D.S(178)), Theme.Text, TextFormatFlags.HidePrefix);
                for (int i = 0; i < 16; i++) Swatch(g, CustomCell(i), _custom[i], i == _customSlot);
                BuildSpectrum();
                var sr = SpecRect;
                g.DrawImageUnscaled(_spectrum, sr.X, sr.Y);
                using (var p = new Pen(Theme.InputBorder)) g.DrawRectangle(p, sr.X - 1, sr.Y - 1, sr.Width + 1, sr.Height + 1);
                // crosshair
                int cx = sr.X + P._hue * (sr.Width - 1) / 239, cy = sr.Y + (240 - P._sat) * (sr.Height - 1) / 240;
                using (var pb = new Pen(Color.Black, 3)) { g.DrawLine(pb, cx - 9, cy, cx - 3, cy); g.DrawLine(pb, cx + 3, cy, cx + 9, cy); g.DrawLine(pb, cx, cy - 9, cx, cy - 3); g.DrawLine(pb, cx, cy + 3, cx, cy + 9); }
                using (var pw = new Pen(Color.White, 1)) { g.DrawLine(pw, cx - 9, cy, cx - 3, cy); g.DrawLine(pw, cx + 3, cy, cx + 9, cy); g.DrawLine(pw, cx, cy - 9, cx, cy - 3); g.DrawLine(pw, cx, cy + 3, cx, cy + 9); }
                // luminance bar
                var lr = LumRect;
                for (int y = 0; y < lr.Height; y++)
                {
                    int l = 240 - y * 240 / Math.Max(1, lr.Height - 1);
                    using (var b = new SolidBrush(Px.ToColor(Px.FromHsl(P._hue, P._sat, l)))) g.FillRectangle(b, lr.X, lr.Y + y, lr.Width, 1);
                }
                using (var p = new Pen(Theme.InputBorder)) g.DrawRectangle(p, lr.X - 1, lr.Y - 1, lr.Width + 1, lr.Height + 1);
                int ly = lr.Y + (240 - P._lum) * (lr.Height - 1) / 240;
                using (var b = new SolidBrush(Theme.Text)) g.FillPolygon(b, new[] { new Point(lr.Right + D.S(3), ly), new Point(lr.Right + D.S(10), ly - D.S(5)), new Point(lr.Right + D.S(10), ly + D.S(5)) });
                // preview
                var pr = Preview;
                using (var b = new SolidBrush(Px.ToColor(P._color))) g.FillRectangle(b, pr);
                using (var p = new Pen(Theme.InputBorder)) g.DrawRectangle(p, pr.X - 1, pr.Y - 1, pr.Width + 1, pr.Height + 1);
                TextRenderer.DrawText(g, "Color|Solid", Theme.SmallFont(D), new Rectangle(pr.X, pr.Bottom + 1, pr.Width, D.S(14)), Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            }

            void Swatch(Graphics g, Rectangle r, uint c, bool sel)
            {
                using (var b = new SolidBrush(Px.ToColor(c))) g.FillRectangle(b, r);
                using (var p = new Pen(sel ? Theme.Focus : Theme.C(0x5A5A5A), sel ? 2 : 1)) g.DrawRectangle(p, r.X - (sel ? 1 : 0), r.Y - (sel ? 1 : 0), r.Width + (sel ? 1 : -1), r.Height + (sel ? 1 : -1));
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                for (int i = 0; i < 48; i++) if (Rectangle.Inflate(BasicCell(i), 2, 2).Contains(e.Location)) { P.SetColor(Basic[i] | 0xFF000000u, true); return; }
                for (int i = 0; i < 16; i++) if (Rectangle.Inflate(CustomCell(i), 2, 2).Contains(e.Location)) { _customSlot = i; P.SetColor(_custom[i], true); return; }
                if (Rectangle.Inflate(SpecRect, 3, 3).Contains(e.Location)) { _drag = 1; Capture = true; Track(e.Location); }
                else if (Rectangle.FromLTRB(LumRect.Left - 2, LumRect.Top - 6, LumRect.Right + D.S(12), LumRect.Bottom + 6).Contains(e.Location)) { _drag = 2; Capture = true; Track(e.Location); }
            }

            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (_drag != 0) Track(e.Location); }
            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _drag = 0; Capture = false; }

            void Track(Point p)
            {
                if (_drag == 1)
                {
                    var r = SpecRect;
                    int h = (p.X - r.X) * 239 / Math.Max(1, r.Width - 1), s = 240 - (p.Y - r.Y) * 240 / Math.Max(1, r.Height - 1);
                    int l = P._lum == 0 || P._lum == 240 ? 120 : P._lum;
                    P.SetHsl(h, s, l);
                }
                else
                {
                    var r = LumRect;
                    P.SetHsl(P._hue, P._sat, 240 - (p.Y - r.Y) * 240 / Math.Max(1, r.Height - 1));
                }
            }

            protected override void Dispose(bool disposing) { if (disposing) _spectrum?.Dispose(); base.Dispose(disposing); }
        }
    }

    // ===================================================================== About
    public sealed class AboutDialog : DarkDialog
    {
        AboutDialog(Form owner) : base(owner, "About Paint")
        {
            SetClientSizeScaled(520, 384);
            var hero = new Hero { Bounds = R(0, 0, 520, 112) };
            Controls.Add(hero);
            string product = "Windows", version = "", build = "", owner_ = "";
            try
            {
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    product = (string)k.GetValue("ProductName") ?? "Windows";
                    version = (string)k.GetValue("DisplayVersion") ?? (string)k.GetValue("ReleaseId") ?? "";
                    build = (string)k.GetValue("CurrentBuild") ?? "";
                    var ubr = k.GetValue("UBR");
                    if (ubr != null) build += "." + ubr;
                    owner_ = (string)k.GetValue("RegisteredOwner") ?? Environment.UserName;
                }
                if (Native.IsWin11 && product.Contains("Windows 10")) product = product.Replace("Windows 10", "Windows 11");
            }
            catch { }
            var asm = typeof(AboutDialog).Assembly.GetName().Version;
            AddLabel("Paint — Dark Mode", 36, 130, 440, 22);
            AddLabel("Version " + asm.Major + "." + asm.Minor + "." + asm.Build, 36, 152, 440, 20, true);
            AddLabel("A dark-themed, feature-for-feature recreation of the classic Windows Paint.", 36, 182, 450, 20);
            AddLabel("Running on " + product + (version.Length > 0 ? ", Version " + version : "") + (build.Length > 0 ? " (OS Build " + build + ")" : ""), 36, 212, 450, 36, true);
            AddLabel("Windows is licensed to:", 36, 256, 440, 20, true);
            AddLabel(owner_, 52, 278, 420, 20);
            AddLabel("Paint, Windows and the Windows logo are trademarks of Microsoft Corporation. This is an independent project and is not affiliated with Microsoft.", 36, 304, 450, 36, true);
            AddButton("OK", 416, 346, 88, DialogResult.OK, true);
            CancelButton = AcceptButton;
        }

        public static void ShowFor(Form owner) { using (var d = new AboutDialog(owner)) d.ShowDialog(owner); }

        sealed class Hero : Control
        {
            public Hero() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                var d = new Dpi(Native.DpiOf(Handle));
                using (var lg = new LinearGradientBrush(ClientRectangle, Theme.C(0x1B2A3D), Theme.C(0x202020), 0f)) g.FillRectangle(lg, ClientRectangle);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                var st = g.Save();
                g.TranslateTransform(d.S(36), d.S(24));
                Icons.PaintAppIcon(g, d.S(64));
                g.Restore(st);
                using (var f = new Font("Segoe UI Light", d.S(40), FontStyle.Regular, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, "Paint", f, new Point(d.S(116), d.S(18)), Theme.Text, TextFormatFlags.NoPadding);
                using (var f = new Font("Segoe UI Semibold", d.S(15), FontStyle.Regular, GraphicsUnit.Pixel))
                    TextRenderer.DrawText(g, "DARK MODE", f, new Point(d.S(120), d.S(72)), Theme.C(0x6FB3FF), TextFormatFlags.NoPadding);
                using (var p = new Pen(Theme.C(0x3A3A3A))) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            }
        }
    }

    // ===================================================================== Help
    public sealed class HelpDialog : DarkDialog
    {
        static readonly string[][] Sections =
        {
            new[] { "File", "Ctrl+N|New picture", "Ctrl+O|Open", "Ctrl+S|Save", "F12|Save as", "Ctrl+P|Print", "Ctrl+E|Image properties", "Alt+F4|Exit" },
            new[] { "Edit", "Ctrl+Z|Undo", "Ctrl+Y|Redo", "Ctrl+X / Shift+Del|Cut", "Ctrl+C / Ctrl+Ins|Copy", "Ctrl+V / Shift+Ins|Paste", "Ctrl+A|Select all", "Delete|Delete selection or shape", "Ctrl+Shift+X|Crop to selection", "Ctrl+W|Resize and skew", "Ctrl+Shift+I|Invert colors" },
            new[] { "View", "Ctrl+PgUp / Ctrl+PgDn|Zoom in / out", "Ctrl+Mouse wheel|Zoom at the cursor", "Shift+Mouse wheel|Scroll sideways", "Middle mouse drag|Pan the canvas", "Ctrl+R|Rulers", "Ctrl+G|Gridlines", "F11|Full screen", "Ctrl+F1|Minimize the Ribbon", "Alt or F10|Key tips for every Ribbon command" },
            new[] { "Drawing", "Right mouse button|Draw with Color 2 (fill with Color 2, swap shape colors)", "Other button / Esc while dragging|Cancel the current stroke", "Shift while drawing|Straight lines (45° steps), squares and circles", "Ctrl++ / Ctrl+-|Change the size of the current tool", "Eraser + right button|Replace Color 1 with Color 2 only" },
            new[] { "Selection", "Drag inside|Move (leaves Color 2 behind)", "Ctrl+drag|Move a copy", "Shift+drag|Stamp a trail of copies", "Arrow keys|Nudge 1 px (Shift: 10 px)", "Handles + Shift|Resize keeping the aspect ratio", "Esc / Enter|Place the selection" },
            new[] { "Text", "Ctrl+B / Ctrl+I / Ctrl+U|Bold / italic / underline", "No selection + format change|Applies to the whole text box", "Esc or click outside|Place the text" },
            new[] { "Dark mode", "View › Dark canvas|New pictures start black with a white Color 1 (turn off for classic white)" },
        };

        HelpDialog(Form owner) : base(owner, "Paint Help")
        {
            SetClientSizeScaled(620, 560);
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(D.S(420), D.S(320));
            var view = new HelpView(Sections) { Dock = DockStyle.Fill, Font = Font };
            Controls.Add(view);
            var bar = new Panel { Dock = DockStyle.Bottom, Height = D.S(48), BackColor = Theme.DialogPanel };
            var ok = new DarkButton { Text = "Close", Font = Font, Size = new Size(D.S(88), D.S(26)), Anchor = AnchorStyles.Right | AnchorStyles.Bottom, DialogResult = DialogResult.OK };
            ok.Location = new Point(bar.Width - ok.Width - D.S(14), D.S(11));
            bar.Controls.Add(ok);
            bar.Resize += (s, e) => ok.Location = new Point(bar.Width - ok.Width - D.S(14), D.S(11));
            Controls.Add(bar);
            AcceptButton = ok; CancelButton = ok;
        }

        public static void ShowFor(Form owner) { using (var d = new HelpDialog(owner)) d.ShowDialog(owner); }

        sealed class HelpView : Control
        {
            readonly string[][] _s;
            int _scroll, _content;
            public HelpView(string[][] s) { _s = s; SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true); TabStop = true; }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                var d = new Dpi(Native.DpiOf(Handle));
                g.Clear(Theme.Dialog);
                var fb = Theme.UiFontBold(d); var f = Theme.UiFont(d);
                int y = d.S(16) - _scroll;
                int keyW = d.S(210);
                foreach (var sec in _s)
                {
                    TextRenderer.DrawText(g, sec[0], Theme.GetFont(d, 15, FontStyle.Bold), new Point(d.S(20), y), Theme.C(0x6FB3FF), TextFormatFlags.NoPrefix);
                    y += d.S(28);
                    for (int i = 1; i < sec.Length; i++)
                    {
                        var parts = sec[i].Split('|');
                        var sz = TextRenderer.MeasureText(parts[1], f, new Size(Width - keyW - d.S(44), 1000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                        TextRenderer.DrawText(g, parts[0], fb, new Rectangle(d.S(28), y, keyW - d.S(10), d.S(20)), Theme.Text, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                        TextRenderer.DrawText(g, parts[1], f, new Rectangle(d.S(28) + keyW, y, Width - keyW - d.S(44), sz.Height), Theme.TextDim, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                        y += Math.Max(d.S(22), sz.Height + d.S(4));
                    }
                    y += d.S(12);
                }
                _content = y + _scroll;
            }
            protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Scroll(-e.Delta / 2); }
            void Scroll(int dy) { _scroll = Math.Max(0, Math.Min(Math.Max(0, _content - Height), _scroll + dy)); Invalidate(); }
            protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); }
            protected override bool IsInputKey(Keys k) { var c = k & Keys.KeyCode; return c == Keys.Up || c == Keys.Down || c == Keys.PageUp || c == Keys.PageDown || base.IsInputKey(k); }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Keys.Down) Scroll(40); else if (e.KeyCode == Keys.Up) Scroll(-40);
                else if (e.KeyCode == Keys.PageDown) Scroll(Height - 40); else if (e.KeyCode == Keys.PageUp) Scroll(-(Height - 40));
            }
        }
    }
}
