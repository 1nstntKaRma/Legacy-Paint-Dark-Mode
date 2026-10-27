using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PaintDark
{
    public struct TextStyle
    {
        public string Face;
        public float Size;        // points, 0 = mixed
        public bool Bold, Italic, Underline, Strike;
    }

    /// <summary>Paint's text tool: a rich text box floating over the picture until it is committed.</summary>
    public sealed unsafe class TextTool : ToolBase, ITextHostOwner
    {
        RichEditHost _re;
        Rectangle _box;                // text area in picture coordinates
        int _minHeight;
        bool _creating;                // dragging out a new box
        Point _createStart, _createCur;
        int _drag;                     // 0 none, 1 move, 2 resize, 3 text (forwarded)
        int _handle;
        PointF _dragStartF;
        Rectangle _dragStartBox;
        bool _btnDown;
        // caret
        Rectangle _caret;              // picture coords
        bool _caretShown, _caretVisible = true, _focused;
        readonly Timer _blink = new Timer();
        readonly Dictionary<uint, Timer> _timers = new Dictionary<uint, Timer>();
        // defaults
        public string DefFace = "Calibri";
        public float DefSize = 11;
        public bool DefBold, DefItalic, DefUnderline, DefStrike;
        public bool Opaque;
        TextStyle _cur;

        public TextTool(Editor e) : base(e)
        {
            int bt = Native.GetCaretBlinkTime();
            _blink.Interval = bt > 0 && bt < 5000 ? bt : 530;
            _blink.Tick += (s, a) => { _caretVisible = !_caretVisible; InvalidateCaret(); };
            _cur = new TextStyle { Face = DefFace, Size = DefSize };
        }

        public override string Name => "Text";
        public bool IsEditing => _re != null;
        public override bool HasPending => _re != null;
        public override bool InStroke => _btnDown;
        public TextStyle CurrentStyle => _re != null ? _cur : new TextStyle { Face = DefFace, Size = DefSize, Bold = DefBold, Italic = DefItalic, Underline = DefUnderline, Strike = DefStrike };

        public void LoadSettings()
        {
            DefFace = Settings.Get("TextFace", "Calibri");
            DefSize = (float)Settings.GetDouble("TextSize", 11);
            DefBold = Settings.GetBool("TextBold", false); DefItalic = Settings.GetBool("TextItalic", false);
            DefUnderline = Settings.GetBool("TextUnderline", false); DefStrike = Settings.GetBool("TextStrike", false);
            Opaque = Settings.GetBool("TextOpaque", false);
        }

        public void SaveSettings()
        {
            Settings.Set("TextFace", DefFace); Settings.SetDouble("TextSize", DefSize);
            Settings.SetBool("TextBold", DefBold); Settings.SetBool("TextItalic", DefItalic);
            Settings.SetBool("TextUnderline", DefUnderline); Settings.SetBool("TextStrike", DefStrike);
            Settings.SetBool("TextOpaque", Opaque);
        }

        // ------------------------------------------------------------------ box lifecycle
        void CreateBox(Rectangle r)
        {
            if (!RichEditHost.Available) { MessageBox.Show("The text engine (msftedit.dll) is not available on this system."); return; }
            _box = r;
            _minHeight = r.Height;
            try
            {
                _re = new RichEditHost(this, DefFace, DefSize, E.Color1, DefBold, DefItalic, DefUnderline, DefStrike);
            }
            catch (Exception ex)
            {
                _re = null;
                MessageBox.Show("Could not start the text engine: " + ex.Message);
                return;
            }
            if (V.Focused) FocusChanged(true);
            else V.Focus();
            _re.Send(RichEditHost.EM_REQUESTRESIZE, IntPtr.Zero, IntPtr.Zero);
            ReadFormat();
            InvalidateBox();
            E.Notify(Change.Text | Change.Tool | Change.Commands);
        }

        void DestroyBox()
        {
            if (_re == null) return;
            var old = BoxBounds;
            foreach (var t in _timers.Values) t.Dispose();
            _timers.Clear();
            _blink.Stop();
            _caretShown = false;
            var re = _re;
            _re = null;
            re.Dispose();
            V?.InvalidateImageOutline(old, 12);
            E.Notify(Change.Text | Change.Tool | Change.Commands);
        }

        public override void Commit()
        {
            if (_re == null) return;
            string text = _re.Text ?? "";
            bool hasText = text.Trim('\r', '\n', ' ', '\t', '\v').Length > 0 || (Opaque && text.Length > 0);
            if (hasText)
            {
                var r = _box;
                r.Intersect(Doc.Bounds);
                if (r.Width > 0 && r.Height > 0)
                {
                    _re.Send(RichEditHost.EM_HIDESELECTION, (IntPtr)1, IntPtr.Zero);
                    Render(Doc.Image, new Rectangle(0, 0, Doc.Width, Doc.Height));
                    E.Commit(r, "Text");
                }
            }
            DestroyBox();
        }

        public override bool UndoPending()
        {
            if (_re == null) return false;
            if (_re.Send(RichEditHost.EM_CANUNDO, IntPtr.Zero, IntPtr.Zero) != IntPtr.Zero) { _re.Send(RichEditHost.EM_UNDO, IntPtr.Zero, IntPtr.Zero); return true; }
            DestroyBox();
            return true;
        }

        public override void DocumentChanged() { DestroyBox(); }
        public override void Deactivate() { if (_btnDown) CancelStroke(); Commit(); }

        // ------------------------------------------------------------------ rendering
        Rectangle BoxBounds { get { var r = _box; r.Inflate(2, 2); return r; } }

        public override Rectangle CompositeBounds => _re != null ? _box : Rectangle.Empty;

        public override void Composite(Dib target, Rectangle area) => Render(target, area);

        /// <summary>Draws the text box into target whose (0,0) corresponds to picture point area.Location.</summary>
        void Render(Dib target, Rectangle area)
        {
            if (_re == null) return;
            var box = Rectangle.Intersect(_box, area);
            if (box.Width <= 0 || box.Height <= 0) return;
            // remember the pixels to restore alpha afterwards (GDI writes alpha = 0)
            int bw = box.Width, bh = box.Height;
            var before = new uint[bw * bh];
            for (int y = 0; y < bh; y++)
            {
                uint* row = target.Row(box.Y - area.Y + y) + (box.X - area.X);
                for (int x = 0; x < bw; x++) before[y * bw + x] = row[x];
            }
            IntPtr hdc = target.DC;
            int saved = Native.SaveDC(hdc);
            try
            {
                Native.SetViewportOrgEx(hdc, -area.X, -area.Y, IntPtr.Zero);
                Native.IntersectClipRect(hdc, box.Left, box.Top, box.Right, box.Bottom);
                if (Opaque)
                {
                    var rc = RECT.From(_box);
                    IntPtr br = Native.CreateSolidBrush(Native.Rgb(E.Color2));
                    Native.FillRect(hdc, ref rc, br);
                    Native.DeleteObject(br);
                }
                _re.Draw(hdc);
            }
            finally { Native.RestoreDC(hdc, saved); }
            Native.GdiFlush();
            for (int y = 0; y < bh; y++)
            {
                uint* row = target.Row(box.Y - area.Y + y) + (box.X - area.X);
                for (int x = 0; x < bw; x++)
                {
                    uint o = before[y * bw + x], n = row[x];
                    if (Opaque || (n & 0xFFFFFF) != (o & 0xFFFFFF)) row[x] = n | 0xFF000000u;
                    else row[x] = o;
                }
            }
        }

        // ------------------------------------------------------------------ ITextHostOwner
        RECT ITextHostOwner.ClientRect => RECT.From(_box);

        void ITextHostOwner.InvalidateClient(RECT? r)
        {
            if (r.HasValue) V?.InvalidateImage(r.Value.ToRectangle());
            else InvalidateBox();
        }

        void ITextHostOwner.CreateCaret(int w, int h) { _caret.Width = Math.Max(1, w); _caret.Height = Math.Max(1, h); }
        void ITextHostOwner.ShowCaret(bool show)
        {
            _caretShown = show;
            _caretVisible = true;
            if (show && _focused) _blink.Start(); else _blink.Stop();
            InvalidateCaret();
        }
        void ITextHostOwner.SetCaretPos(int x, int y)
        {
            InvalidateCaret();
            _caret.X = x; _caret.Y = y;
            _caretVisible = true;
            if (_blink.Enabled) { _blink.Stop(); _blink.Start(); }
            InvalidateCaret();
            UpdateImePosition();
        }
        void ITextHostOwner.SetCapture(bool capture) { if (V != null) V.Capture = capture; }
        void ITextHostOwner.SetFocus() { V?.Focus(); }
        void ITextHostOwner.SetCursor(IntPtr hcur, bool text) { if (hcur != IntPtr.Zero) Native.SetCursor(hcur); }

        bool ITextHostOwner.ScreenToClient(ref POINT p)
        {
            if (V == null) return false;
            var vp = V.PointToClient(new Point(p.X, p.Y));
            var ip = V.ViewToImage(vp);
            p.X = (int)Math.Floor(ip.X); p.Y = (int)Math.Floor(ip.Y);
            return true;
        }

        bool ITextHostOwner.ClientToScreen(ref POINT p)
        {
            if (V == null) return false;
            var vp = V.ImageToView(new Point(p.X, p.Y));
            var sp = V.PointToScreen(vp);
            p.X = sp.X; p.Y = sp.Y;
            return true;
        }

        void ITextHostOwner.SetTimer(uint id, uint ms)
        {
            if (!_timers.TryGetValue(id, out var t))
            {
                t = new Timer();
                uint tid = id;
                t.Tick += (s, e) => _re?.Send(Native.WM_TIMER, (IntPtr)tid, IntPtr.Zero);
                _timers[id] = t;
            }
            t.Interval = (int)Math.Max(1, Math.Min(int.MaxValue, ms));
            t.Start();
        }

        void ITextHostOwner.KillTimer(uint id) { if (_timers.TryGetValue(id, out var t)) t.Stop(); }

        void ITextHostOwner.RequestResize(RECT needed)
        {
            // grow downward (never shrink below what the user dragged)
            int h = Math.Max(_minHeight, needed.Height);
            if (h == _box.Height) return;
            var old = BoxBounds;
            _box.Height = h;
            _re?.ClientRectChanged();
            V?.InvalidateImageOutline(Rectangle.Union(old, BoxBounds), 12);
        }

        void ITextHostOwner.SelectionChanged() { ReadFormat(); }
        void ITextHostOwner.TextChanged() { InvalidateBox(); if (_re != null) _re.Send(RichEditHost.EM_REQUESTRESIZE, IntPtr.Zero, IntPtr.Zero); }
        IntPtr ITextHostOwner.ImmGetContext() => V != null ? Native.ImmGetContext(V.Handle) : IntPtr.Zero;
        void ITextHostOwner.ImmReleaseContext(IntPtr himc) { if (V != null) Native.ImmReleaseContext(V.Handle, himc); }

        int ITextHostOwner.SysColor(int index)
        {
            switch (index)
            {
                case 13: return Native.Rgb(0xFF3399FFu);   // COLOR_HIGHLIGHT
                case 14: return Native.Rgb(0xFFFFFFFFu);   // COLOR_HIGHLIGHTTEXT
                case 8: return Native.Rgb(E.Color1);       // COLOR_WINDOWTEXT
                case 5: return Native.Rgb(E.Color2);       // COLOR_WINDOW
                default: return Native.GetSysColor(index);
            }
        }

        void UpdateImePosition()
        {
            if (V == null || !V.IsHandleCreated) return;
            IntPtr himc = Native.ImmGetContext(V.Handle);
            if (himc == IntPtr.Zero) return;
            var p = V.ImageToView(new Point(_caret.X, _caret.Y));
            var cf = new Native.COMPOSITIONFORM { dwStyle = 2 /*CFS_POINT*/, ptCurrentPos = new POINT(p.X, p.Y) };
            Native.ImmSetCompositionWindow(himc, ref cf);
            Native.ImmReleaseContext(V.Handle, himc);
        }

        void InvalidateBox() { if (_re != null) V?.InvalidateImageOutline(BoxBounds, 12); }
        void InvalidateCaret() { if (_caret.Width > 0) V?.InvalidateImage(new Rectangle(_caret.X - 1, _caret.Y, _caret.Width + 2, _caret.Height)); }

        public void FocusChanged(bool focused)
        {
            _focused = focused;
            if (_re == null) return;
            _re.Send(focused ? Native.WM_SETFOCUS : Native.WM_KILLFOCUS, IntPtr.Zero, IntPtr.Zero);
            if (focused && _caretShown) _blink.Start(); else _blink.Stop();
            _caretVisible = focused;
            InvalidateCaret();
        }

        // ------------------------------------------------------------------ formatting
        void ReadFormat()
        {
            if (_re == null) return;
            _re.GetFormat(out uint mask, out string face, out float size, out uint color, out uint eff);
            _cur.Face = (mask & RichEditHost.CFM_FACE) != 0 ? face : "";
            _cur.Size = (mask & RichEditHost.CFM_SIZE) != 0 ? size : 0;
            _cur.Bold = (mask & RichEditHost.CFM_BOLD) != 0 && (eff & RichEditHost.CFE_BOLD) != 0;
            _cur.Italic = (mask & RichEditHost.CFM_ITALIC) != 0 && (eff & RichEditHost.CFE_ITALIC) != 0;
            _cur.Underline = (mask & RichEditHost.CFM_UNDERLINE) != 0 && (eff & RichEditHost.CFE_UNDERLINE) != 0;
            _cur.Strike = (mask & RichEditHost.CFM_STRIKEOUT) != 0 && (eff & RichEditHost.CFE_STRIKEOUT) != 0;
            E.Notify(Change.Text);
        }

        bool SelectionEmpty()
        {
            if (_re == null) return true;
            _re.GetSel(out int a, out int b);
            return a == b;
        }

        void Apply(uint mask, string face, float size, uint color, bool b, bool i, bool u, bool s)
        {
            if (_re == null) return;
            bool all = SelectionEmpty();     // no selection: the whole box changes (and new typing)
            _re.SetFormat(all, mask, face, size, color, b, i, u, s);
            if (all) _re.SetFormat(false, mask, face, size, color, b, i, u, s);
            _re.Send(RichEditHost.EM_REQUESTRESIZE, IntPtr.Zero, IntPtr.Zero);
            ReadFormat();
            InvalidateBox();
        }

        public void SetFace(string face)
        {
            DefFace = face;
            if (_re != null) Apply(RichEditHost.CFM_FACE | RichEditHost.CFM_CHARSET, face, 0, 0, false, false, false, false);
            E.Notify(Change.Text);
            V?.Focus();
        }

        public void SetSize(float pt)
        {
            DefSize = pt;
            if (_re != null) Apply(RichEditHost.CFM_SIZE, null, pt, 0, false, false, false, false);
            E.Notify(Change.Text);
            V?.Focus();
        }

        public void ToggleBold() { bool v = !CurrentStyle.Bold; DefBold = v; if (_re != null) Apply(RichEditHost.CFM_BOLD, null, 0, 0, v, false, false, false); E.Notify(Change.Text); }
        public void ToggleItalic() { bool v = !CurrentStyle.Italic; DefItalic = v; if (_re != null) Apply(RichEditHost.CFM_ITALIC, null, 0, 0, false, v, false, false); E.Notify(Change.Text); }
        public void ToggleUnderline() { bool v = !CurrentStyle.Underline; DefUnderline = v; if (_re != null) Apply(RichEditHost.CFM_UNDERLINE, null, 0, 0, false, false, v, false); E.Notify(Change.Text); }
        public void ToggleStrike() { bool v = !CurrentStyle.Strike; DefStrike = v; if (_re != null) Apply(RichEditHost.CFM_STRIKEOUT, null, 0, 0, false, false, false, v); E.Notify(Change.Text); }

        public void SetOpaque(bool opaque)
        {
            Opaque = opaque;
            InvalidateBox();
            E.Notify(Change.Text);
        }

        public void ApplyTextColor(uint c)
        {
            if (_re == null) return;
            Apply(RichEditHost.CFM_COLOR, null, 0, c, false, false, false, false);
        }

        public override void ColorsChanged(int slot)
        {
            if (_re == null) return;
            if (slot == 2 && Opaque) InvalidateBox();
        }

        // clipboard inside the box
        public void CutText() => _re?.Send(Native.WM_CUT, IntPtr.Zero, IntPtr.Zero);
        public void CopyText() => _re?.Send(Native.WM_COPY, IntPtr.Zero, IntPtr.Zero);
        public void PasteText() { if (_re != null) { _re.Send(RichEditHost.EM_PASTESPECIAL, (IntPtr)13 /*CF_UNICODETEXT*/, IntPtr.Zero); InvalidateBox(); } }
        public void SelectAllText() => _re?.SetSel(0, -1);
        public void DeleteText() => _re?.Send(Native.WM_KEYDOWN, (IntPtr)0x2E, IntPtr.Zero);
        public bool HasTextSelection => _re != null && !SelectionEmpty();

        // ------------------------------------------------------------------ keyboard (raw messages from the canvas)
        public bool HandleMessage(ref Message m)
        {
            if (_re == null) return false;
            if (m.Msg == Native.WM_KEYDOWN)
            {
                int vk = (int)m.WParam;
                bool ctrl = Native.CtrlDown, shift = Native.ShiftDown;
                if ((ctrl && vk == 'V') || (shift && vk == 0x2D)) { PasteText(); m.Result = IntPtr.Zero; return true; }
                if (vk == 0x1B) { Commit(); m.Result = IntPtr.Zero; return true; }          // Esc places the text
                if (ctrl && (vk == 'E' || vk == 'L' || vk == 'R' || vk == 'J')) return false;  // paragraph alignment shortcuts are Paint commands
            }
            if (m.Msg == Native.WM_CHAR && Native.CtrlDown && (int)m.WParam < 32 && (int)m.WParam != 13 && (int)m.WParam != 8 && (int)m.WParam != 10)
            {
                m.Result = IntPtr.Zero; return true;   // swallow control characters like ^B
            }
            if (m.Msg == Native.WM_IME_SETCONTEXT || m.Msg == Native.WM_IME_NOTIFY) return false;
            if (_re.SendHandled(m.Msg, m.WParam, m.LParam, out IntPtr res))
            {
                m.Result = res;
                if (m.Msg == Native.WM_KEYDOWN || m.Msg == Native.WM_CHAR) { _caretVisible = true; InvalidateCaret(); }
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ mouse
        Rectangle ViewBox => V.ImageToView(_box);
        int HandleSize => Math.Max(5, V.DpiValue * 6 / 96);

        int HitHandle(Point view)
        {
            if (_re == null) return -1;
            var vb = ViewBox;
            vb.Inflate(2, 2);
            int i = 0;
            foreach (var h in Handles(vb, HandleSize)) { var hr = h; hr.Inflate(3, 3); if (hr.Contains(view)) return i; i++; }
            return -1;
        }

        bool OnBorder(Point view)
        {
            var vb = ViewBox;
            var outer = Rectangle.Inflate(vb, 6, 6);
            return outer.Contains(view) && !vb.Contains(view);
        }

        static IEnumerable<Rectangle> Handles(Rectangle vr, int hs)
        {
            int cx = vr.X + vr.Width / 2, cy = vr.Y + vr.Height / 2;
            int l = vr.Left - hs / 2, t = vr.Top - hs / 2, r = vr.Right - hs / 2, b = vr.Bottom - hs / 2;
            yield return new Rectangle(l, t, hs, hs); yield return new Rectangle(cx - hs / 2, t, hs, hs); yield return new Rectangle(r, t, hs, hs);
            yield return new Rectangle(r, cy - hs / 2, hs, hs); yield return new Rectangle(r, b, hs, hs); yield return new Rectangle(cx - hs / 2, b, hs, hs);
            yield return new Rectangle(l, b, hs, hs); yield return new Rectangle(l, cy - hs / 2, hs, hs);
        }

        public override IntPtr GetCursor(MouseArgs a)
        {
            if (_re != null)
            {
                if (_drag == 1) return AppCursors.SizeAll;
                int h = HitHandle(a.ViewPt);
                if (h >= 0) switch (h) { case 0: case 4: return AppCursors.SizeNWSE; case 2: case 6: return AppCursors.SizeNESW; case 1: case 5: return AppCursors.SizeNS; default: return AppCursors.SizeWE; }
                if (OnBorder(a.ViewPt)) return AppCursors.SizeAll;
                if (ViewBox.Contains(a.ViewPt)) return AppCursors.IBeam;
            }
            return AppCursors.Crosshair;
        }

        IntPtr ImageLParam(MouseArgs a) => Native.MakeLParam((int)Math.Floor(a.P.X), (int)Math.Floor(a.P.Y));
        IntPtr Keys(MouseArgs a, int btn) => (IntPtr)(btn | (a.Shift ? Native.MK_SHIFT : 0) | (a.Ctrl ? Native.MK_CONTROL : 0));

        public override bool WantsRightClickMenu(MouseArgs a) => _re != null && ViewBox.Contains(a.ViewPt);

        public override void MouseDown(MouseArgs a)
        {
            if (_btnDown) { CancelStroke(); return; }
            if (!a.Left) return;
            if (_re != null)
            {
                int h = HitHandle(a.ViewPt);
                if (h >= 0) { _btnDown = true; _drag = 2; _handle = h; _dragStartF = a.P; _dragStartBox = _box; return; }
                if (OnBorder(a.ViewPt)) { _btnDown = true; _drag = 1; _dragStartF = a.P; _dragStartBox = _box; return; }
                if (ViewBox.Contains(a.ViewPt))
                {
                    _btnDown = true; _drag = 3;
                    if (!V.Focused) V.Focus();
                    _re.Send(Native.WM_LBUTTONDOWN, Keys(a, Native.MK_LBUTTON), ImageLParam(a));
                    return;
                }
                Commit();   // clicking outside places the text
                return;
            }
            _btnDown = true;
            _creating = true;
            _createStart = _createCur = a.Pi;
            V.Invalidate();
        }

        public override void DoubleClick(MouseArgs a)
        {
            if (_re != null && ViewBox.Contains(a.ViewPt) && !OnBorder(a.ViewPt) && HitHandle(a.ViewPt) < 0)
            {
                _btnDown = true; _drag = 3;
                _re.Send(Native.WM_LBUTTONDBLCLK, Keys(a, Native.MK_LBUTTON), ImageLParam(a));
                return;
            }
            MouseDown(a);
        }

        public override void MouseMove(MouseArgs a)
        {
            if (_creating) { _createCur = a.Pi; V.Invalidate(); E.SetSelSize(CreateRect().Size); return; }
            if (_re == null) return;
            if (!_btnDown)
            {
                if (ViewBox.Contains(a.ViewPt)) _re.Send(Native.WM_MOUSEMOVE, Keys(a, 0), ImageLParam(a));
                return;
            }
            switch (_drag)
            {
                case 1:
                    {
                        int dx = (int)Math.Round(a.P.X - _dragStartF.X), dy = (int)Math.Round(a.P.Y - _dragStartF.Y);
                        SetBox(new Rectangle(_dragStartBox.X + dx, _dragStartBox.Y + dy, _dragStartBox.Width, _dragStartBox.Height));
                        break;
                    }
                case 2:
                    {
                        int x0 = _dragStartBox.Left, y0 = _dragStartBox.Top, x1 = _dragStartBox.Right, y1 = _dragStartBox.Bottom;
                        int px = (int)Math.Round(a.P.X), py = (int)Math.Round(a.P.Y);
                        switch (_handle)
                        {
                            case 0: x0 = px; y0 = py; break; case 1: y0 = py; break; case 2: x1 = px; y0 = py; break; case 3: x1 = px; break;
                            case 4: x1 = px; y1 = py; break; case 5: y1 = py; break; case 6: x0 = px; y1 = py; break; case 7: x0 = px; break;
                        }
                        if (x1 - x0 < 8) { if (_handle == 0 || _handle == 6 || _handle == 7) x0 = x1 - 8; else x1 = x0 + 8; }
                        if (y1 - y0 < 8) { if (_handle <= 2) y0 = y1 - 8; else y1 = y0 + 8; }
                        _minHeight = y1 - y0;
                        SetBox(Rectangle.FromLTRB(x0, y0, x1, y1));
                        _re.Send(RichEditHost.EM_REQUESTRESIZE, IntPtr.Zero, IntPtr.Zero);
                        break;
                    }
                case 3:
                    _re.Send(Native.WM_MOUSEMOVE, Keys(a, Native.MK_LBUTTON), ImageLParam(a));
                    break;
            }
        }

        void SetBox(Rectangle r)
        {
            var old = BoxBounds;
            _box = r;
            _re?.ClientRectChanged();
            V.InvalidateImageOutline(Rectangle.Union(old, BoxBounds), 12);
            E.SetSelSize(_box.Size);
        }

        Rectangle CreateRect()
        {
            int x0 = Math.Min(_createStart.X, _createCur.X), y0 = Math.Min(_createStart.Y, _createCur.Y);
            int x1 = Math.Max(_createStart.X, _createCur.X), y1 = Math.Max(_createStart.Y, _createCur.Y);
            return Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
        }

        public override void MouseUp(MouseArgs a)
        {
            if (!_btnDown || !a.Left) return;
            _btnDown = false;
            if (_creating)
            {
                _creating = false;
                var r = CreateRect();
                E.SetSelSize(null);
                int lineH = (int)Math.Ceiling(DefSize * 96 / 72 * 1.35) + 4;
                if (r.Width < 8 && r.Height < 8)
                {
                    // a click: default sized box, kept inside the picture when possible
                    int w = Math.Max(140, (int)(DefSize * 96 / 72 * 12));
                    r = new Rectangle(r.X, r.Y, w, lineH);
                    if (r.Right > Doc.Width) r.X = Math.Max(0, Doc.Width - w);
                }
                else { r.Width = Math.Max(r.Width, 16); r.Height = Math.Max(r.Height, lineH); }
                V.Invalidate();
                CreateBox(r);
                return;
            }
            if (_drag == 3 && _re != null) _re.Send(Native.WM_LBUTTONUP, Keys(a, 0), ImageLParam(a));
            _drag = 0;
        }

        public override void CancelStroke()
        {
            if (!_btnDown) return;
            _btnDown = false;
            if (_creating) { _creating = false; E.SetSelSize(null); V.Invalidate(); return; }
            if (_drag == 1 || _drag == 2) SetBox(_dragStartBox);
            if (_drag == 3) _re?.Send(Native.WM_LBUTTONUP, IntPtr.Zero, IntPtr.Zero);
            _drag = 0;
        }

        public override bool KeyDown(System.Windows.Forms.Keys k)
        {
            if (_btnDown && (k & System.Windows.Forms.Keys.KeyCode) == System.Windows.Forms.Keys.Escape) { CancelStroke(); return true; }
            return false;
        }

        // ------------------------------------------------------------------ overlay
        public override void PaintScreen(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.None;
            if (_creating)
            {
                var vr = V.ImageToView(CreateRect());
                using (var a = new Pen(Color.White)) g.DrawRectangle(a, vr.X, vr.Y, vr.Width - 1, vr.Height - 1);
                using (var b = new Pen(Color.FromArgb(0x10, 0x10, 0x10)) { DashPattern = new[] { 4f, 4f } }) g.DrawRectangle(b, vr.X, vr.Y, vr.Width - 1, vr.Height - 1);
                return;
            }
            if (_re == null) return;
            var vb = ViewBox;
            var frame = Rectangle.Inflate(vb, 2, 2);
            using (var a = new Pen(Color.White)) g.DrawRectangle(a, frame.X, frame.Y, frame.Width - 1, frame.Height - 1);
            using (var b = new Pen(Color.FromArgb(0x10, 0x10, 0x10)) { DashPattern = new[] { 4f, 4f } }) g.DrawRectangle(b, frame.X, frame.Y, frame.Width - 1, frame.Height - 1);
            foreach (var h in Handles(frame, HandleSize))
            {
                g.FillRectangle(Theme.Brush(Theme.Handle), h);
                g.DrawRectangle(Theme.Pen(Color.FromArgb(0x50, 0x50, 0x50)), h.X, h.Y, h.Width - 1, h.Height - 1);
            }
            if (_caretShown && _caretVisible && _focused)
            {
                var cr = V.ImageToView(_caret);
                int w = Math.Max(1, (int)Math.Round(V.Zoom * Math.Max(1, _caret.Width)));
                if (V.Zoom < 1) w = 1;
                // two-tone caret readable on any background
                g.FillRectangle(Brushes.White, cr.X - 1, cr.Y, w + 2, cr.Height);
                g.FillRectangle(Brushes.Black, cr.X, cr.Y, w, cr.Height);
            }
        }
    }
}
