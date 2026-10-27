using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PaintDark
{
    /// <summary>Callbacks the windowless RichEdit needs from its owner (the text tool).</summary>
    public interface ITextHostOwner
    {
        RECT ClientRect { get; }
        void InvalidateClient(RECT? r);
        void CreateCaret(int w, int h);
        void ShowCaret(bool show);
        void SetCaretPos(int x, int y);
        void SetCapture(bool capture);
        void SetFocus();
        void SetCursor(IntPtr hcur, bool text);
        bool ScreenToClient(ref POINT p);
        bool ClientToScreen(ref POINT p);
        void SetTimer(uint id, uint ms);
        void KillTimer(uint id);
        void RequestResize(RECT needed);
        void SelectionChanged();
        void TextChanged();
        IntPtr ImmGetContext();
        void ImmReleaseContext(IntPtr himc);
        int SysColor(int index);
    }

    /// <summary>
    /// Hosts a windowless RichEdit (msftedit.dll ITextServices) — the same engine classic Paint uses for its text tool.
    /// The ITextHost vtable is built by hand so it works for x64 and for x86 (where these methods are thiscall).
    /// </summary>
    public sealed unsafe class RichEditHost : IDisposable
    {
        // ---- ITextServices vtable slots (after IUnknown) ----
        const int S_TxSendMessage = 3, S_TxDraw = 4, S_OnTxSetCursor = 7, S_OnTxInPlaceActivate = 9, S_OnTxInPlaceDeactivate = 10,
            S_OnTxUIActivate = 11, S_OnTxUIDeactivate = 12, S_TxGetText = 13, S_TxSetText = 14, S_TxGetNaturalSize = 17, S_OnTxPropertyBitsChange = 19;

        public const int TXTBIT_RICHTEXT = 1, TXTBIT_MULTILINE = 2, TXTBIT_HIDESELECTION = 0x20, TXTBIT_SAVESELECTION = 0x40, TXTBIT_WORDWRAP = 0x400,
            TXTBIT_DISABLEDRAG = 0x1000, TXTBIT_VIEWINSETCHANGE = 0x2000, TXTBIT_BACKSTYLECHANGE = 0x4000, TXTBIT_CHARFORMATCHANGE = 0x20000,
            TXTBIT_PARAFORMATCHANGE = 0x40000, TXTBIT_EXTENTCHANGE = 0x80000, TXTBIT_CLIENTRECTCHANGE = 0x100000, TXTBIT_ALLOWBEEP = 0x800;

        public const int EM_GETSEL = 0xB0, EM_SETSEL = 0xB1, EM_REPLACESEL = 0xC2, EM_CANUNDO = 0xC6, EM_UNDO = 0xC7,
            EM_EXGETSEL = 0x434, EM_EXSETSEL = 0x437, EM_GETCHARFORMAT = 0x43A, EM_HIDESELECTION = 0x43F, EM_PASTESPECIAL = 0x440,
            EM_REQUESTRESIZE = 0x441, EM_SETCHARFORMAT = 0x444, EM_SETEVENTMASK = 0x445, EM_GETTEXTLENGTHEX = 0x45F, EM_REDO = 0x454, EM_CANREDO = 0x455,
            EM_SETTYPOGRAPHYOPTIONS = 0x4CA, EM_SETEDITSTYLE = 0x4CC, EM_SETUNDOLIMIT = 0x452, EM_SETTEXTMODE = 0x459;
        public const int SCF_SELECTION = 1, SCF_ALL = 4, SCF_DEFAULT = 0;
        public const uint CFM_BOLD = 1, CFM_ITALIC = 2, CFM_UNDERLINE = 4, CFM_STRIKEOUT = 8, CFM_SIZE = 0x80000000, CFM_COLOR = 0x40000000, CFM_FACE = 0x20000000, CFM_CHARSET = 0x08000000;
        public const uint CFE_BOLD = 1, CFE_ITALIC = 2, CFE_UNDERLINE = 4, CFE_STRIKEOUT = 8, CFE_AUTOCOLOR = 0x40000000;
        const int EN_CHANGE = 0x0300, EN_REQUESTRESIZE = 0x0701, EN_SELCHANGE = 0x0702;
        const int ENM_CHANGE = 1, ENM_REQUESTRESIZE = 0x40000, ENM_SELCHANGE = 0x80000;

        // ---- delegate types ----
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int QIFn(IntPtr self, ref Guid riid, out IntPtr ppv);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint RefFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate IntPtr H_Ptr(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int H_I_P(IntPtr self, IntPtr a);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int H_I_II(IntPtr self, int a, int b);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int H_I_IIII(IntPtr self, int a, int b, int c, int d);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int H_I_III(IntPtr self, int a, int b, int c);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate void H_V_PI(IntPtr self, IntPtr a, int b);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate void H_V_I(IntPtr self, int a);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int H_I_PII(IntPtr self, IntPtr a, int b, int c);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int H_I_I(IntPtr self, int a);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int H_I_UU(IntPtr self, uint a, uint b);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate void H_V_U(IntPtr self, uint a);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate void H_V_Scroll(IntPtr self, int dx, int dy, IntPtr a, IntPtr b, IntPtr c, IntPtr d, uint f);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate void H_V(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int H_I_IP(IntPtr self, int a, IntPtr b);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate void H_V_P(IntPtr self, IntPtr a);

        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int TS_SendMessage(IntPtr self, int msg, IntPtr wp, IntPtr lp, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int TS_Draw(IntPtr self, int aspect, int lindex, IntPtr pvAspect, IntPtr ptd, IntPtr hdc, IntPtr hicTarget, IntPtr bounds, IntPtr wbounds, IntPtr update, IntPtr pfnContinue, int dwContinue, int viewId);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int TS_P(IntPtr self, IntPtr a);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int TS_V(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int TS_UU(IntPtr self, int a, int b);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int TS_GetText(IntPtr self, out IntPtr bstr);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall, CharSet = CharSet.Unicode)] delegate int TS_SetText(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string s);
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)] delegate int TS_SetCursor(IntPtr self, int aspect, int lindex, IntPtr pvAspect, IntPtr ptd, IntPtr hdc, IntPtr hicTarget, IntPtr client, int x, int y);

        [DllImport("msftedit.dll", EntryPoint = "CreateTextServices")]
        static extern int CreateTextServices(IntPtr outer, IntPtr host, out IntPtr ppUnk);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibraryW(string name);

        // ---- shared vtable (one for all hosts; 'self' identifies the instance) ----
        static IntPtr _vtbl;
        static readonly List<Delegate> _keep = new List<Delegate>();
        static readonly Dictionary<IntPtr, RichEditHost> _instances = new Dictionary<IntPtr, RichEditHost>();
        static Guid _iidTextServices = new Guid("8d33f740-cf58-11ce-a89d-00aa006cadc5");
        static Guid _iidTextHost = new Guid("c5bdd8d0-d26e-11ce-a89e-00aa006cadc5");
        static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
        static bool _initOk;

        readonly ITextHostOwner _owner;
        readonly IntPtr _self;           // native block: [vtbl*]
        IntPtr _unk, _services;
        IntPtr _charFormat, _paraFormat;
        IntPtr _measureDC;
        int _bits = TXTBIT_RICHTEXT | TXTBIT_MULTILINE | TXTBIT_WORDWRAP | TXTBIT_DISABLEDRAG | TXTBIT_SAVESELECTION;
        int _devDpi = 96;

        TS_SendMessage _send;
        TS_Draw _draw;
        TS_P _inPlaceActivate;
        TS_V _inPlaceDeactivate, _uiActivate, _uiDeactivate;
        TS_UU _propChange;
        TS_GetText _getText;
        TS_SetText _setText;

        public static bool Available
        {
            get
            {
                try { EnsureInit(); return _initOk; } catch { return false; }
            }
        }

        static void EnsureInit()
        {
            if (_vtbl != IntPtr.Zero) return;
            IntPtr lib = LoadLibraryW("msftedit.dll");
            if (lib == IntPtr.Zero) return;
            IntPtr p = Native.GetProcAddress(lib, "IID_ITextServices");
            if (p != IntPtr.Zero) _iidTextServices = Marshal.PtrToStructure<Guid>(p);
            p = Native.GetProcAddress(lib, "IID_ITextHost");
            if (p != IntPtr.Zero) _iidTextHost = Marshal.PtrToStructure<Guid>(p);

            var fns = new List<Delegate>
            {
                new QIFn(QueryInterface), new RefFn(AddRef), new RefFn(Release),
                new H_Ptr(s => Get(s)?._measureDC ?? IntPtr.Zero),                                  // TxGetDC
                new H_I_P((s, hdc) => 1),                                                            // TxReleaseDC
                new H_I_II((s, a, b) => 1),                                                          // TxShowScrollBar
                new H_I_II((s, a, b) => 1),                                                          // TxEnableScrollBar
                new H_I_IIII((s, a, b, c, d) => 1),                                                  // TxSetScrollRange
                new H_I_III((s, a, b, c) => 1),                                                      // TxSetScrollPos
                new H_V_PI((s, prc, mode) => { var h = Get(s); if (h == null) return; h._owner.InvalidateClient(prc == IntPtr.Zero ? (RECT?)null : Marshal.PtrToStructure<RECT>(prc)); }), // TxInvalidateRect
                new H_V_I((s, upd) => Get(s)?._owner.InvalidateClient(null)),                       // TxViewChange
                new H_I_PII((s, bmp, w, h) => { Get(s)?._owner.CreateCaret(w, h); return 1; }),     // TxCreateCaret
                new H_I_I((s, show) => { Get(s)?._owner.ShowCaret(show != 0); return 1; }),          // TxShowCaret
                new H_I_II((s, x, y) => { Get(s)?._owner.SetCaretPos(x, y); return 1; }),           // TxSetCaretPos
                new H_I_UU((s, id, ms) => { Get(s)?._owner.SetTimer(id, ms); return 1; }),          // TxSetTimer
                new H_V_U((s, id) => Get(s)?._owner.KillTimer(id)),                                 // TxKillTimer
                new H_V_Scroll((s, dx, dy, a, b, c, d, f) => Get(s)?._owner.InvalidateClient(null)),// TxScrollWindowEx
                new H_V_I((s, cap) => Get(s)?._owner.SetCapture(cap != 0)),                         // TxSetCapture
                new H_V(s => Get(s)?._owner.SetFocus()),                                            // TxSetFocus
                new H_V_PI((s, cur, text) => Get(s)?._owner.SetCursor(cur, text != 0)),             // TxSetCursor
                new H_I_P((s, pt) => { var h = Get(s); if (h == null) return 0; var p2 = Marshal.PtrToStructure<POINT>(pt); bool ok = h._owner.ScreenToClient(ref p2); Marshal.StructureToPtr(p2, pt, false); return ok ? 1 : 0; }),
                new H_I_P((s, pt) => { var h = Get(s); if (h == null) return 0; var p2 = Marshal.PtrToStructure<POINT>(pt); bool ok = h._owner.ClientToScreen(ref p2); Marshal.StructureToPtr(p2, pt, false); return ok ? 1 : 0; }),
                new H_I_P((s, old) => { if (old != IntPtr.Zero) Marshal.WriteInt32(old, 0); return 0; }),    // TxActivate
                new H_I_I((s, st) => 0),                                                                    // TxDeactivate
                new H_I_P((s, prc) => { var h = Get(s); if (h == null) return 1; Marshal.StructureToPtr(h._owner.ClientRect, prc, false); return 0; }), // TxGetClientRect
                new H_I_P((s, prc) => { Marshal.StructureToPtr(new RECT(0, 0, 0, 0), prc, false); return 0; }), // TxGetViewInset
                new H_I_P((s, pp) => { var h = Get(s); if (h == null) return 1; Marshal.WriteIntPtr(pp, h._charFormat); return 0; }),   // TxGetCharFormat
                new H_I_P((s, pp) => { var h = Get(s); if (h == null) return 1; Marshal.WriteIntPtr(pp, h._paraFormat); return 0; }),   // TxGetParaFormat
                new H_I_I((s, idx) => Get(s)?._owner.SysColor(idx) ?? 0),                                  // TxGetSysColor
                new H_I_P((s, st) => { Marshal.WriteInt32(st, 0); return 0; }),                            // TxGetBackStyle: transparent
                new H_I_P((s, len) => { Marshal.WriteInt32(len, 0x7FFFFFFF); return 0; }),                 // TxGetMaxLength
                new H_I_P((s, sb) => { Marshal.WriteInt32(sb, 0x40); return 0; }),                         // TxGetScrollBars: ES_AUTOVSCROLL
                new H_I_P((s, ch) => 1),                                                                    // TxGetPasswordChar: S_FALSE
                new H_I_P((s, cp) => { Marshal.WriteInt32(cp, -1); return 0; }),                           // TxGetAcceleratorPos
                new H_I_P((s, ext) => { var h = Get(s); return h == null ? unchecked((int)0x80004001) : h.GetExtent(ext); }), // TxGetExtent
                new H_I_P((s, pcf) => 0),                                                                   // OnTxCharFormatChange
                new H_I_P((s, ppf) => 0),                                                                   // OnTxParaFormatChange
                new H_I_IP((s, mask, bits) => { var h = Get(s); Marshal.WriteInt32(bits, (h?._bits ?? 0) & mask); return 0; }), // TxGetPropertyBits
                new H_I_IP((s, code, pv) => { Get(s)?.Notify(code, pv); return 0; }),                      // TxNotify
                new H_Ptr(s => Get(s)?._owner.ImmGetContext() ?? IntPtr.Zero),                             // TxImmGetContext
                new H_V_P((s, himc) => Get(s)?._owner.ImmReleaseContext(himc)),                            // TxImmReleaseContext
                new H_I_P((s, w) => { Marshal.WriteInt32(w, 0); return 0; }),                              // TxGetSelectionBarWidth
            };
            _keep.AddRange(fns);
            _vtbl = Marshal.AllocHGlobal(IntPtr.Size * fns.Count);
            for (int i = 0; i < fns.Count; i++) Marshal.WriteIntPtr(_vtbl, i * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(fns[i]));
            _initOk = true;
        }

        static RichEditHost Get(IntPtr self) { lock (_instances) return _instances.TryGetValue(self, out var h) ? h : null; }

        static int QueryInterface(IntPtr self, ref Guid riid, out IntPtr ppv)
        {
            if (riid == IID_IUnknown || riid == _iidTextHost) { ppv = self; return 0; }
            ppv = IntPtr.Zero;
            return unchecked((int)0x80004002); // E_NOINTERFACE
        }
        static uint AddRef(IntPtr self) => 1;
        static uint Release(IntPtr self) => 1;

        public RichEditHost(ITextHostOwner owner, string face, float pointSize, uint color, bool bold, bool italic, bool underline, bool strike)
        {
            EnsureInit();
            if (!_initOk) throw new InvalidOperationException("RichEdit is not available.");
            _owner = owner;
            _self = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(_self, _vtbl);
            lock (_instances) _instances[_self] = this;

            _measureDC = Native.CreateCompatibleDC(IntPtr.Zero);
            _devDpi = Native.GetDeviceCaps(_measureDC, 90 /*LOGPIXELSY*/);
            if (_devDpi <= 0) _devDpi = 96;

            _charFormat = Marshal.AllocHGlobal(116);
            WriteCharFormat(_charFormat, CFM_FACE | CFM_SIZE | CFM_COLOR | CFM_BOLD | CFM_ITALIC | CFM_UNDERLINE | CFM_STRIKEOUT | CFM_CHARSET, face, pointSize, color, bold, italic, underline, strike);
            _paraFormat = Marshal.AllocHGlobal(156);
            for (int i = 0; i < 156; i++) Marshal.WriteByte(_paraFormat, i, 0);
            Marshal.WriteInt32(_paraFormat, 0, 156);
            Marshal.WriteInt32(_paraFormat, 4, 0x1 | 0x2 | 0x4 | 0x8 | 0x10 | 0x20);   // indents, alignment, tabs, numbering
            Marshal.WriteInt16(_paraFormat, 24, 1);                                       // PFA_LEFT

            int hr = CreateTextServices(IntPtr.Zero, _self, out _unk);
            if (hr != 0 || _unk == IntPtr.Zero) throw new COMException("CreateTextServices failed", hr);
            hr = Marshal.QueryInterface(_unk, ref _iidTextServices, out _services);
            if (hr != 0) throw new COMException("ITextServices not available", hr);
            IntPtr vt = Marshal.ReadIntPtr(_services);
            T Fn<T>(int slot) where T : class => Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, slot * IntPtr.Size), typeof(T)) as T;
            _send = Fn<TS_SendMessage>(S_TxSendMessage);
            _draw = Fn<TS_Draw>(S_TxDraw);
            _inPlaceActivate = Fn<TS_P>(S_OnTxInPlaceActivate);
            _inPlaceDeactivate = Fn<TS_V>(S_OnTxInPlaceDeactivate);
            _uiActivate = Fn<TS_V>(S_OnTxUIActivate);
            _uiDeactivate = Fn<TS_V>(S_OnTxUIDeactivate);
            _propChange = Fn<TS_UU>(S_OnTxPropertyBitsChange);
            _getText = Fn<TS_GetText>(S_TxGetText);
            _setText = Fn<TS_SetText>(S_TxSetText);

            Send(EM_SETEVENTMASK, IntPtr.Zero, (IntPtr)(ENM_CHANGE | ENM_SELCHANGE | ENM_REQUESTRESIZE));
            Send(EM_SETUNDOLIMIT, (IntPtr)100, IntPtr.Zero);
            Send(EM_SETTYPOGRAPHYOPTIONS, (IntPtr)1, (IntPtr)1);   // TO_ADVANCEDTYPOGRAPHY (better complex-script shaping)
            var rc = _owner.ClientRect;
            IntPtr prc = Marshal.AllocHGlobal(16);
            Marshal.StructureToPtr(rc, prc, false);
            _inPlaceActivate(_services, prc);
            Marshal.FreeHGlobal(prc);
            _uiActivate(_services);
            _propChange(_services, TXTBIT_EXTENTCHANGE, TXTBIT_EXTENTCHANGE);
        }

        int GetExtent(IntPtr ext)
        {
            // keep text at 96-dpi picture size regardless of the monitor DPI (RichEdit zooms client/extent)
            if (_devDpi == 96) return unchecked((int)0x80004001); // E_NOTIMPL -> no zoom
            var r = _owner.ClientRect;
            Marshal.WriteInt32(ext, 0, (int)Math.Round(r.Width * 2540.0 / 96));
            Marshal.WriteInt32(ext, 4, (int)Math.Round(r.Height * 2540.0 / 96));
            return 0;
        }

        void Notify(int code, IntPtr pv)
        {
            switch (code)
            {
                case EN_REQUESTRESIZE:
                    {
                        int off = IntPtr.Size == 8 ? 24 : 12;          // REQRESIZE { NMHDR nmhdr; RECT rc; }
                        var r = Marshal.PtrToStructure<RECT>(pv + off);
                        _owner.RequestResize(r);
                        break;
                    }
                case EN_SELCHANGE: _owner.SelectionChanged(); break;
                case EN_CHANGE: _owner.TextChanged(); break;
            }
        }

        // ------------------------------------------------------------------ API
        public IntPtr Send(int msg, IntPtr wp, IntPtr lp)
        {
            if (_services == IntPtr.Zero) return IntPtr.Zero;
            _send(_services, msg, wp, lp, out IntPtr res);
            return res;
        }

        /// <summary>Returns false when RichEdit did not handle the message (S_FALSE).</summary>
        public bool SendHandled(int msg, IntPtr wp, IntPtr lp, out IntPtr res)
        {
            res = IntPtr.Zero;
            if (_services == IntPtr.Zero) return false;
            int hr = _send(_services, msg, wp, lp, out res);
            return hr == 0;
        }

        public void Draw(IntPtr hdc)
        {
            if (_services == IntPtr.Zero) return;
            _draw(_services, 1 /*DVASPECT_CONTENT*/, 0, IntPtr.Zero, IntPtr.Zero, hdc, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, 0 /*TXTVIEW_ACTIVE*/);
        }

        public void ClientRectChanged() { _propChange?.Invoke(_services, TXTBIT_CLIENTRECTCHANGE | TXTBIT_EXTENTCHANGE, TXTBIT_CLIENTRECTCHANGE | TXTBIT_EXTENTCHANGE); }

        public void SetDefaultFormat(string face, float size, uint color, bool b, bool i, bool u, bool s)
        {
            WriteCharFormat(_charFormat, CFM_FACE | CFM_SIZE | CFM_COLOR | CFM_BOLD | CFM_ITALIC | CFM_UNDERLINE | CFM_STRIKEOUT | CFM_CHARSET, face, size, color, b, i, u, s);
            _propChange?.Invoke(_services, TXTBIT_CHARFORMATCHANGE, TXTBIT_CHARFORMATCHANGE);
        }

        public string Text
        {
            get
            {
                if (_services == IntPtr.Zero) return "";
                if (_getText(_services, out IntPtr bstr) != 0 || bstr == IntPtr.Zero) return "";
                try { return Marshal.PtrToStringBSTR(bstr); } finally { Marshal.FreeBSTR(bstr); }
            }
            set { if (_services != IntPtr.Zero) _setText(_services, value ?? ""); }
        }

        public void GetSel(out int start, out int end)
        {
            IntPtr cr = Marshal.AllocHGlobal(8);
            try { Send(EM_EXGETSEL, IntPtr.Zero, cr); start = Marshal.ReadInt32(cr, 0); end = Marshal.ReadInt32(cr, 4); }
            finally { Marshal.FreeHGlobal(cr); }
        }

        public void SetSel(int start, int end)
        {
            IntPtr cr = Marshal.AllocHGlobal(8);
            try { Marshal.WriteInt32(cr, 0, start); Marshal.WriteInt32(cr, 4, end); Send(EM_EXSETSEL, IntPtr.Zero, cr); }
            finally { Marshal.FreeHGlobal(cr); }
        }

        public void SetFormat(bool all, uint mask, string face, float size, uint color, bool b, bool i, bool u, bool s)
        {
            IntPtr cf = Marshal.AllocHGlobal(116);
            try
            {
                WriteCharFormat(cf, mask, face, size, color, b, i, u, s);
                Send(EM_SETCHARFORMAT, (IntPtr)(all ? SCF_ALL : SCF_SELECTION), cf);
            }
            finally { Marshal.FreeHGlobal(cf); }
        }

        /// <summary>Reads the selection's character format. Fields whose mask bit is clear are mixed.</summary>
        public void GetFormat(out uint mask, out string face, out float size, out uint color, out uint effects)
        {
            IntPtr cf = Marshal.AllocHGlobal(116);
            try
            {
                for (int k = 0; k < 116; k++) Marshal.WriteByte(cf, k, 0);
                Marshal.WriteInt32(cf, 0, 116);
                Send(EM_GETCHARFORMAT, (IntPtr)SCF_SELECTION, cf);
                mask = (uint)Marshal.ReadInt32(cf, 4);
                effects = (uint)Marshal.ReadInt32(cf, 8);
                size = Marshal.ReadInt32(cf, 12) / 20f;
                int cref = Marshal.ReadInt32(cf, 20);
                color = 0xFF000000u | (uint)((cref & 0xFF) << 16) | (uint)(cref & 0xFF00) | (uint)((cref >> 16) & 0xFF);
                face = Marshal.PtrToStringUni(cf + 26);
            }
            finally { Marshal.FreeHGlobal(cf); }
        }

        static void WriteCharFormat(IntPtr cf, uint mask, string face, float size, uint color, bool b, bool i, bool u, bool s)
        {
            for (int k = 0; k < 116; k++) Marshal.WriteByte(cf, k, 0);
            Marshal.WriteInt32(cf, 0, 116);
            Marshal.WriteInt32(cf, 4, (int)mask);
            uint eff = (b ? CFE_BOLD : 0) | (i ? CFE_ITALIC : 0) | (u ? CFE_UNDERLINE : 0) | (s ? CFE_STRIKEOUT : 0);
            Marshal.WriteInt32(cf, 8, (int)eff);
            Marshal.WriteInt32(cf, 12, (int)Math.Round(size * 20));
            Marshal.WriteInt32(cf, 20, Native.Rgb(color));
            Marshal.WriteByte(cf, 24, 1);   // DEFAULT_CHARSET
            if (!string.IsNullOrEmpty(face))
            {
                var chars = face.Length > 31 ? face.Substring(0, 31) : face;
                for (int k = 0; k < chars.Length; k++) Marshal.WriteInt16(cf, 26 + k * 2, chars[k]);
            }
        }

        public void Dispose()
        {
            if (_services != IntPtr.Zero)
            {
                try { _uiDeactivate(_services); _inPlaceDeactivate(_services); } catch { }
                Marshal.Release(_services); _services = IntPtr.Zero;
            }
            if (_unk != IntPtr.Zero) { Marshal.Release(_unk); _unk = IntPtr.Zero; }
            lock (_instances) _instances.Remove(_self);
            if (_charFormat != IntPtr.Zero) { Marshal.FreeHGlobal(_charFormat); _charFormat = IntPtr.Zero; }
            if (_paraFormat != IntPtr.Zero) { Marshal.FreeHGlobal(_paraFormat); _paraFormat = IntPtr.Zero; }
            if (_measureDC != IntPtr.Zero) { Native.DeleteDC(_measureDC); _measureDC = IntPtr.Zero; }
            // _self block intentionally leaked-safe: free it last
            Marshal.FreeHGlobal(_self);
        }
    }
}
