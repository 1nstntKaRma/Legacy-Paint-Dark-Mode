using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PaintDark
{
    /// <summary>Base for every dialog: dark title bar, dark surface, DPI from the owner, Enter/Esc handling.</summary>
    public class DarkDialog : Form
    {
        protected Dpi D;

        public DarkDialog(Form owner, string title)
        {
            D = new Dpi(owner != null && owner.IsHandleCreated ? Native.DpiOf(owner.Handle) : 96);
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Dialog;
            ForeColor = Theme.Text;
            Font = Theme.UiFont(D);
            KeyPreview = true;
            ShowIcon = false;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkMode.ApplyToWindow(Handle);
        }

        protected void SetClientSizeScaled(int w, int h) => ClientSize = new Size(D.S(w), D.S(h));
        protected Rectangle R(int x, int y, int w, int h) => new Rectangle(D.S(x), D.S(y), D.S(w), D.S(h));

        protected DarkButton AddButton(string text, int x, int y, int w, DialogResult result, bool isDefault = false)
        {
            var b = new DarkButton { Text = text, Bounds = R(x, y, w, 26), DialogResult = result, Font = Font };
            Controls.Add(b);
            if (isDefault) AcceptButton = b;
            if (result == DialogResult.Cancel) CancelButton = b;
            return b;
        }

        protected DarkLabel AddLabel(string text, int x, int y, int w, int h = 20, bool dim = false)
        {
            var l = new DarkLabel { Text = text, Bounds = R(x, y, w, h), Dim = dim, Font = Font };
            Controls.Add(l);
            return l;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Dialog);
            base.OnPaint(e);
        }
    }

    public class DarkButton : Control, IButtonControl
    {
        bool _hover, _down, _default;
        public DialogResult DialogResult { get; set; }

        public DarkButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            TabStop = true;
            Cursor = Cursors.Default;
        }

        public void NotifyDefault(bool value) { _default = value; Invalidate(); }

        public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

        protected override void OnClick(EventArgs e)
        {
            var f = FindForm();
            if (f != null && DialogResult != DialogResult.None) f.DialogResult = DialogResult;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            g.Clear(Parent?.BackColor ?? Theme.Dialog);
            Color bg = !Enabled ? Theme.C(0x2A2A2A) : _down ? Theme.Pressed : _hover ? Theme.Hover : Theme.Button;
            Color border = (_default || Focused) && Enabled ? Theme.Focus : Theme.ButtonBorder;
            if (_default && Enabled && !_hover && !_down) bg = Theme.C(0x2E3E52);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Draw.RoundRect(new RectangleF(0.5f, 0.5f, r.Width - 1, r.Height - 1), 3f))
            {
                using (var b = new SolidBrush(bg)) g.FillPath(b, p);
                using (var pen = new Pen(border)) g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, Text, Font, r, Enabled ? Theme.Text : Theme.TextDisabled, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix));
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { _down = true; Focus(); Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) == Keys.Space || base.IsInputKey(keyData);
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.KeyCode == Keys.Space) PerformClick(); }

        protected override bool ProcessMnemonic(char charCode)
        {
            if (CanSelect && IsMnemonic(charCode, Text)) { PerformClick(); return true; }
            return base.ProcessMnemonic(charCode);
        }
    }

    public class DarkLabel : Control
    {
        public bool Dim;
        public TextFormatFlags Flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak;
        public Control MnemonicTarget;
        public DarkLabel() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true); TabStop = false; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Dialog);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? (Dim ? Theme.TextDim : Theme.Text) : Theme.TextDisabled, Flags | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix));
        }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override bool ProcessMnemonic(char charCode)
        {
            if (MnemonicTarget != null && IsMnemonic(charCode, Text)) { MnemonicTarget.Focus(); if (MnemonicTarget is DarkTextBox tb) tb.SelectAll(); return true; }
            return false;
        }
    }

    /// <summary>Check box and radio button in one class.</summary>
    public class DarkCheck : Control
    {
        bool _checked, _hover;
        public bool Radio;
        public event EventHandler CheckedChanged;

        public DarkCheck()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            TabStop = true;
        }

        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                if (value && Radio && Parent != null)
                    foreach (Control c in Parent.Controls) if (c != this && c is DarkCheck dc && dc.Radio) dc.Checked = false;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Dialog);
            int s = Math.Min(Height - 4, (int)(Font.Height * 0.95f));
            var box = new Rectangle(1, (Height - s) / 2, s, s);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var border = !Enabled ? Theme.TextDisabled : (_hover || Focused) ? Theme.Focus : Theme.InputBorder;
            if (Radio)
            {
                using (var b = new SolidBrush(Theme.Input)) g.FillEllipse(b, box);
                using (var p = new Pen(border)) g.DrawEllipse(p, box);
                if (_checked) using (var b = new SolidBrush(Enabled ? Theme.Focus : Theme.TextDisabled)) g.FillEllipse(b, Rectangle.Inflate(box, -s / 4, -s / 4));
            }
            else
            {
                using (var b = new SolidBrush(_checked && Enabled ? Theme.Accent : Theme.Input)) g.FillRectangle(b, box);
                using (var p = new Pen(_checked && Enabled ? Theme.Accent : border)) g.DrawRectangle(p, box.X, box.Y, box.Width - 1, box.Height - 1);
                if (_checked) Draw.CheckMark(g, Rectangle.Inflate(box, -1, -1), Color.White, new Dpi(96));
            }
            var tr = new Rectangle(box.Right + 6, 0, Width - box.Right - 6, Height);
            TextRenderer.DrawText(g, Text, Font, tr, Enabled ? Theme.Text : Theme.TextDisabled, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix));
            if (Focused && ShowFocusCues)
            {
                var ts = TextRenderer.MeasureText(Text.Replace("&", ""), Font);
                using (var p = new Pen(Theme.TextDim) { DashStyle = DashStyle.Dot }) g.DrawRectangle(p, tr.X - 2, (Height - ts.Height) / 2, Math.Min(tr.Width - 1, ts.Width + 2), ts.Height - 1);
            }
        }

        protected override void OnClick(EventArgs e) { base.OnClick(e); if (Radio) Checked = true; else Checked = !Checked; Focus(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override bool IsInputKey(Keys k) => (k & Keys.KeyCode) == Keys.Space || base.IsInputKey(k);
        protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (e.KeyCode == Keys.Space) { if (Radio) Checked = true; else Checked = !Checked; } }
        protected override bool ProcessMnemonic(char charCode)
        {
            if (CanSelect && IsMnemonic(charCode, Text)) { Focus(); if (Radio) Checked = true; else Checked = !Checked; return true; }
            return false;
        }
    }

    /// <summary>Single-line dark edit box (wraps a borderless TextBox).</summary>
    public class DarkTextBox : Control
    {
        public readonly TextBox Box;
        public bool NumericOnly, AllowDecimal, AllowNegative;
        bool _hover;

        public DarkTextBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Box = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text };
            Controls.Add(Box);
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Box.MouseEnter += (s, e) => { _hover = true; Invalidate(); };
            Box.MouseLeave += (s, e) => { _hover = false; Invalidate(); };
            Box.TextChanged += (s, e) => OnTextChanged(e);
            Box.KeyPress += (s, e) =>
            {
                if (!NumericOnly || char.IsControl(e.KeyChar)) return;
                if (char.IsDigit(e.KeyChar)) return;
                string dec = System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
                if (AllowDecimal && (e.KeyChar.ToString() == dec || e.KeyChar == '.') && !Box.Text.Contains(dec)) return;
                if (AllowNegative && e.KeyChar == '-' && Box.SelectionStart == 0 && !Box.Text.Contains("-")) return;
                e.Handled = true;
            };
            TabStop = false;
        }

        public override string Text { get => Box.Text; set => Box.Text = value; }
        public void SelectAll() { Box.Focus(); Box.SelectAll(); }

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Box.Font = Font; LayoutBox(); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutBox(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Box.Focus(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Box.Enabled = Enabled; Box.BackColor = Enabled ? Theme.Input : Theme.C(0x242424); Invalidate(); }

        void LayoutBox()
        {
            int h = Box.PreferredHeight;
            Box.Bounds = new Rectangle(6, Math.Max(2, (Height - h) / 2), Math.Max(4, Width - 12), h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Enabled ? Theme.Input : Theme.C(0x242424));
            var border = Box.Focused ? Theme.Focus : _hover ? Theme.InputBorderHover : Theme.InputBorder;
            using (var p = new Pen(border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            if (Box.Focused) using (var p = new Pen(Theme.Focus)) g.DrawLine(p, 1, Height - 2, Width - 2, Height - 2);
        }

        public int IntValue(int def) => int.TryParse(Box.Text, out int v) ? v : def;
        public double DoubleValue(double def) => double.TryParse(Box.Text.Replace(".", System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator), out double v) ? v : def;
    }

    /// <summary>Titled frame ("group box").</summary>
    public class DarkGroup : Control
    {
        public DarkGroup() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); TabStop = false; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Dialog);
            var ts = TextRenderer.MeasureText(Text, Font);
            int ty = ts.Height / 2;
            using (var p = new Pen(Theme.C(0x454545))) g.DrawRectangle(p, 0, ty, Width - 1, Height - ty - 1);
            var tr = new Rectangle(8, 0, ts.Width + 4, ts.Height);
            using (var b = new SolidBrush(Parent?.BackColor ?? Theme.Dialog)) g.FillRectangle(b, tr);
            TextRenderer.DrawText(g, Text, Font, new Point(10, 0), Theme.TextDim, TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Dropdown list (used by Page Setup / Print).</summary>
    public class DarkCombo : Control
    {
        public System.Collections.Generic.List<string> Items = new System.Collections.Generic.List<string>();
        int _index = -1;
        bool _hover;
        public event EventHandler SelectedIndexChanged;

        public DarkCombo()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            TabStop = true;
        }

        public int SelectedIndex
        {
            get => _index;
            set { if (value == _index) return; _index = value; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); }
        }
        public string SelectedItem => _index >= 0 && _index < Items.Count ? Items[_index] : null;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Input);
            using (var p = new Pen(Focused ? Theme.Focus : _hover ? Theme.InputBorderHover : Theme.InputBorder)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            var d = new Dpi(Native.DpiOf(Handle));
            Draw.Chevron(g, Width - d.S(10), Height / 2, Enabled ? Theme.TextDim : Theme.TextDisabled, d);
            TextRenderer.DrawText(g, SelectedItem ?? "", Font, new Rectangle(6, 0, Width - d.S(24), Height), Enabled ? Theme.Text : Theme.TextDisabled, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); Open(); }
        protected override bool IsInputKey(Keys k) { var c = k & Keys.KeyCode; return c == Keys.Up || c == Keys.Down || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Down && e.Alt) { Open(); return; }
            if (e.KeyCode == Keys.Down && _index < Items.Count - 1) SelectedIndex = _index + 1;
            if (e.KeyCode == Keys.Up && _index > 0) SelectedIndex = _index - 1;
            if (e.KeyCode == Keys.F4) Open();
        }

        void Open()
        {
            var popup = new ListPopup(this);
            popup.Show(FindForm());
        }

        /// <summary>Simple modal-less list for dialogs (dialogs are modal, so the popup manager is not used).</summary>
        sealed class ListPopup : Form
        {
            readonly DarkCombo _c;
            int _hot, _top;
            readonly int _rowH, _visible;
            public ListPopup(DarkCombo c)
            {
                _c = c;
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                Font = c.Font;
                _rowH = Math.Max(c.Height - 2, Font.Height + 6);
                _visible = Math.Min(12, Math.Max(1, c.Items.Count));
                var sp = c.PointToScreen(new Point(0, c.Height));
                var wa = Screen.FromControl(c).WorkingArea;
                int h = _rowH * _visible + 2;
                if (sp.Y + h > wa.Bottom) sp.Y = c.PointToScreen(Point.Empty).Y - h;
                Bounds = new Rectangle(sp.X, sp.Y, c.Width, h);
                _hot = Math.Max(0, c.SelectedIndex);
                _top = Math.Max(0, Math.Min(_hot - _visible / 2, c.Items.Count - _visible));
                KeyPreview = true;
            }
            protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; cp.ExStyle |= 0x80; return cp; } }
            protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Close(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Popup);
                for (int i = 0; i < _visible && _top + i < _c.Items.Count; i++)
                {
                    var r = new Rectangle(1, 1 + i * _rowH, Width - 2, _rowH);
                    if (_top + i == _hot) using (var b = new SolidBrush(Theme.PopupHover)) g.FillRectangle(b, r);
                    TextRenderer.DrawText(g, _c.Items[_top + i], Font, new Rectangle(r.X + 6, r.Y, r.Width - 8, r.Height), Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
                using (var p = new Pen(Theme.PopupBorder)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }
            int Hit(Point p) { int i = (p.Y - 1) / _rowH; return i >= 0 && i < _visible && _top + i < _c.Items.Count ? _top + i : -1; }
            protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); int h = Hit(e.Location); if (h >= 0 && h != _hot) { _hot = h; Invalidate(); } }
            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); int h = Hit(e.Location); if (h >= 0) { _c.SelectedIndex = h; Close(); } }
            protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); _top = Math.Max(0, Math.Min(_c.Items.Count - _visible, _top - Math.Sign(e.Delta) * 3)); Invalidate(); }
            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode == Keys.Escape) Close();
                else if (e.KeyCode == Keys.Enter) { _c.SelectedIndex = _hot; Close(); }
                else if (e.KeyCode == Keys.Down) { _hot = Math.Min(_c.Items.Count - 1, _hot + 1); if (_hot >= _top + _visible) _top++; Invalidate(); }
                else if (e.KeyCode == Keys.Up) { _hot = Math.Max(0, _hot - 1); if (_hot < _top) _top--; Invalidate(); }
            }
        }
    }

    /// <summary>Dark replacement for MessageBox with arbitrary buttons. Returns the clicked index, or the last index on Esc/close.</summary>
    public sealed class MessageDialog : DarkDialog
    {
        public enum Kind { Info, Question, Warning, Error }
        int _result = -1;
        readonly int _cancelIndex;

        MessageDialog(Form owner, string title, string text, string[] buttons, int def, Kind kind) : base(owner, title)
        {
            _cancelIndex = buttons.Length - 1;
            var f = Font;
            int maxTextW = D.S(380);
            var ts = TextRenderer.MeasureText(text, f, new Size(maxTextW, 2000), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int iconSz = D.S(32);
            int contentW = Math.Max(ts.Width, D.S(220)) + iconSz + D.S(16);
            int btnW = D.S(88), gap = D.S(8);
            foreach (var b in buttons) btnW = Math.Max(btnW, TextRenderer.MeasureText(b.Replace("&", ""), f).Width + D.S(28));
            int buttonsW = buttons.Length * btnW + (buttons.Length - 1) * gap;
            int w = Math.Max(contentW, buttonsW) + D.S(48);
            int textH = Math.Max(ts.Height, iconSz);
            int barY = D.S(24) + textH + D.S(24);
            ClientSize = new Size(w, barY + D.S(50));
            var icon = new MessageIcon { Kind = kind, Bounds = new Rectangle(D.S(24), D.S(24), iconSz, iconSz) };
            Controls.Add(icon);
            var lbl = new DarkLabel { Text = text, Bounds = new Rectangle(D.S(24) + iconSz + D.S(16), D.S(24) + Math.Max(0, (iconSz - ts.Height) / 2), ts.Width + D.S(4), ts.Height + D.S(4)), Font = f, Flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix };
            Controls.Add(lbl);
            var bar = new Panel { Bounds = new Rectangle(0, barY, w, ClientSize.Height - barY), BackColor = Theme.DialogPanel };
            Controls.Add(bar);
            int x = w - D.S(16) - buttonsW;
            for (int i = 0; i < buttons.Length; i++)
            {
                int idx = i;
                var b = new DarkButton { Text = buttons[i], Bounds = new Rectangle(x, D.S(12), btnW, D.S(26)), Font = f, TabIndex = i };
                b.Click += (s, e) => { _result = idx; DialogResult = DialogResult.OK; Close(); };
                bar.Controls.Add(b);
                if (i == def) { AcceptButton = b; ActiveControl = b; }
                x += btnW + gap;
            }
            bar.Controls[0].BringToFront();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (kindSound) Native.MessageBeep(0x30);
            (AcceptButton as Control)?.Focus();
        }
        bool kindSound;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { _result = _cancelIndex; Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        public static int Show(Form owner, string title, string text, string[] buttons, int def, Kind kind)
        {
            using (var d = new MessageDialog(owner, title, text, buttons, def, kind))
            {
                d.kindSound = kind == Kind.Warning || kind == Kind.Error;
                d.ShowDialog(owner);
                return d._result < 0 ? d._cancelIndex : d._result;
            }
        }

        sealed class MessageIcon : Control
        {
            public Kind Kind;
            public MessageIcon() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Dialog);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new RectangleF(1, 1, Width - 2, Height - 2);
                Color c = Kind == Kind.Error ? Theme.C(0xD9433A) : Kind == Kind.Warning ? Theme.C(0xE8A317) : Theme.C(0x2B79D0);
                string glyph = Kind == Kind.Error ? "✕" : Kind == Kind.Warning ? "!" : Kind == Kind.Question ? "?" : "i";
                if (Kind == Kind.Warning)
                {
                    using (var b = new SolidBrush(c)) g.FillPolygon(b, new[] { new PointF(r.X + r.Width / 2, r.Y + 1), new PointF(r.Right, r.Bottom - 1), new PointF(r.X, r.Bottom - 1) });
                }
                else using (var b = new SolidBrush(c)) g.FillEllipse(b, r);
                using (var f = new Font("Segoe UI", Height * 0.5f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(glyph, f, Brushes.White, new RectangleF(r.X, r.Y + (Kind == Kind.Warning ? Height * 0.1f : 0), r.Width, r.Height), sf);
            }
        }
    }
}
