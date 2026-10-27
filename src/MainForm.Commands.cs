using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace PaintDark
{
    public sealed partial class MainForm
    {
        public bool DarkCanvas = true;

        // ================================================================== document
        public static Size DefaultImageSize
        {
            get
            {
                var b = Screen.PrimaryScreen.Bounds;
                return new Size(Math.Max(320, (int)Math.Round(b.Width * 0.6)), Math.Max(240, (int)Math.Round(b.Height * 0.6)));
            }
        }

        public void NewDocument(bool initial)
        {
            if (!initial)
            {
                E.CommitPending();
                if (!ConfirmDiscard()) return;
            }
            if (initial)
            {
                E.Color1 = DarkCanvas ? 0xFFFFFFFFu : 0xFF000000u;
                E.Color2 = DarkCanvas ? 0xFF000000u : 0xFFFFFFFFu;
            }
            var sz = DefaultImageSize;
            var doc = new Document(sz.Width, sz.Height, DarkCanvas ? 0xFF000000u : 0xFFFFFFFFu);
            E.SetDocument(doc);
            if (E.Tool == ToolKind.Picker || E.Tool == ToolKind.Magnifier) E.SetTool(ToolKind.Pencil);
            UpdateTitle();
        }

        public void OpenFile(string path, bool fromDrop) => OpenFile(path, fromDrop, true);

        public void OpenFile(string path, bool fromDrop, bool askSave)
        {
            E.CommitPending();
            if (!File.Exists(path)) { ShowError("Paint cannot open this file.\n\n" + path); Settings.RemoveRecent(path); return; }
            if (askSave && !ConfirmDiscard()) return;
            LoadedImage li;
            Cursor.Current = Cursors.WaitCursor;
            try { li = ImageIO.Load(path); }
            catch (OutOfMemoryException) { ShowError("There is not enough memory or resources to complete operation.\nClose some programs, and then try again."); return; }
            catch (Exception) { ShowError("Paint cannot read this file.\nThis is not a valid bitmap file, or its format is not currently supported."); return; }
            finally { Cursor.Current = Cursors.Default; }
            var doc = new Document(li.Image)
            {
                HasAlpha = li.HasAlpha,
                FilePath = Path.GetFullPath(path),
                FormatName = ImageIO.FormatFromExtension(path) ?? li.Format,
                DpiX = li.DpiX, DpiY = li.DpiY,
            };
            try { var fi = new FileInfo(path); doc.FileSize = fi.Length; doc.LastSaved = fi.LastWriteTime; } catch { }
            if (doc.FormatName == "bmp") doc.BmpBits = 24;
            doc.MarkSaved();
            E.SetDocument(doc);
            Settings.AddRecent(doc.FilePath);
            UpdateTitle();
            FocusCanvas();
        }

        public bool ConfirmDiscard()
        {
            if (E.Doc == null || !E.Doc.Dirty) return true;
            var r = MessageDialog.Show(this, "Paint", "Do you want to save changes to " + E.Doc.Title + "?", new[] { "&Save", "Do&n't Save", "Cancel" }, 0, MessageDialog.Kind.Question);
            if (r == 0) return Save();
            return r == 1;
        }

        public bool Save()
        {
            E.CommitPending();
            var d = E.Doc;
            if (string.IsNullOrEmpty(d.FilePath) || d.FormatName == "ico" || d.FormatName == "heic" || d.FormatName == "webp" || d.FormatName == null) return SaveAs(null, 0);
            return WriteFile(d.FilePath, d.FormatName, d.BmpBits);
        }

        public bool SaveAs(string format, int bits)
        {
            E.CommitPending();
            var d = E.Doc;
            using (var dlg = new SaveFileDialog())
            {
                dlg.Title = "Save As";
                dlg.AddExtension = true;
                dlg.OverwritePrompt = true;
                var filter = string.Join("|", Array.ConvertAll(ImageIO.SaveTypes, t => t.Name + "|" + t.Filter));
                dlg.Filter = filter;
                string fmt = format ?? (d.FormatName == "ico" || d.FormatName == "heic" || d.FormatName == "webp" ? "png" : d.FormatName ?? "png");
                int b = bits > 0 ? bits : (fmt == "bmp" ? d.BmpBits : 0);
                int idx = Array.FindIndex(ImageIO.SaveTypes, t => t.Format == fmt && (fmt != "bmp" || t.Bits == (b > 0 ? b : 24)));
                if (idx < 0) idx = Array.FindIndex(ImageIO.SaveTypes, t => t.Format == "png");
                dlg.FilterIndex = idx + 1;
                dlg.DefaultExt = ImageIO.SaveTypes[idx].Ext.TrimStart('.');
                string baseName = string.IsNullOrEmpty(d.FilePath) ? "Untitled" : Path.GetFileNameWithoutExtension(d.FilePath);
                dlg.FileName = baseName;
                if (!string.IsNullOrEmpty(d.FilePath)) dlg.InitialDirectory = Path.GetDirectoryName(d.FilePath);
                else dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                var t = ImageIO.SaveTypes[Math.Max(0, dlg.FilterIndex - 1)];
                string path = dlg.FileName;
                string extFmt = ImageIO.FormatFromExtension(path);
                string useFmt = t.Format; int useBits = t.Bits;
                if (extFmt != null && extFmt != useFmt && extFmt != "ico" && extFmt != "heic" && extFmt != "webp") { useFmt = extFmt; useBits = extFmt == "bmp" ? 24 : 32; }
                if (WriteFile(path, useFmt, useBits))
                {
                    d.FilePath = Path.GetFullPath(path);
                    d.FormatName = useFmt;
                    d.BmpBits = useFmt == "bmp" ? useBits : 24;
                    Settings.AddRecent(d.FilePath);
                    UpdateTitle();
                    return true;
                }
                return false;
            }
        }

        bool WriteFile(string path, string format, int bits)
        {
            var d = E.Doc;
            bool lossyColors = format == "gif" || (format == "bmp" && bits < 24);
            if (d.HasAlpha && (format == "jpeg" || format == "bmp" || format == "gif"))
            {
                if (MessageDialog.Show(this, "Paint", "Any transparency will be lost if you save this picture. Do you want to continue?", new[] { "OK", "Cancel" }, 0, MessageDialog.Kind.Warning) != 0) return false;
            }
            else if (lossyColors && d.FormatName != format)
            {
                if (MessageDialog.Show(this, "Paint", "The color quality might be reduced if you save the picture in this format.\nDo you want to continue?", new[] { "OK", "Cancel" }, 0, MessageDialog.Kind.Warning) != 0) return false;
            }
            Cursor.Current = Cursors.WaitCursor;
            try
            {
                ImageIO.Save(d.Image, d.HasAlpha, path, format, bits, d.DpiX);
            }
            catch (UnauthorizedAccessException ex)
            {
                ShowError(ex.Message.Contains("read-only") ? ex.Message : "You cannot save to a read-only file.\nUse a different file name.");
                return false;
            }
            catch (IOException ex)
            {
                ShowError("Paint cannot save this file.\n\n" + ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                ShowError("Save was interrupted, so your file has not been saved.\n\n" + ex.Message);
                return false;
            }
            finally { Cursor.Current = Cursors.Default; }
            d.MarkSaved();
            try { var fi = new FileInfo(path); d.FileSize = fi.Length; d.LastSaved = fi.LastWriteTime; } catch { }
            E.Notify(Change.Title | Change.Status | Change.Commands);
            return true;
        }

        public void ShowError(string text) => MessageDialog.Show(this, "Paint", text, new[] { "OK" }, 0, MessageDialog.Kind.Error);

        // ================================================================== command dispatch
        public void Execute(Cmd c)
        {
            if (!IsEnabled(c)) return;
            try { ExecuteCore(c); }
            catch (OutOfMemoryException) { ShowError("There is not enough memory or resources to complete operation.\nClose some programs, and then try again."); }
            catch (Exception ex) { ShowError("Something went wrong:\n" + ex.Message); }
            Ribbon.Invalidate();
            InvalidateCaption();
        }

        void ExecuteCore(Cmd c)
        {
            switch (c)
            {
                case Cmd.AppMenu: ShowAppMenu(Ribbon.RectangleToScreen(new Rectangle(0, 0, D.S(56), D.S(25)))); break;
                case Cmd.New: NewDocument(false); break;
                case Cmd.Open: DoOpen(); break;
                case Cmd.Save: Save(); break;
                case Cmd.SaveAs: SaveAs(null, 0); break;
                case Cmd.SaveAsPng: SaveAs("png", 32); break;
                case Cmd.SaveAsJpeg: SaveAs("jpeg", 24); break;
                case Cmd.SaveAsBmp: SaveAs("bmp", 24); break;
                case Cmd.SaveAsGif: SaveAs("gif", 8); break;
                case Cmd.SaveAsOther: SaveAs(null, 0); break;
                case Cmd.Print: case Cmd.PreviewPrint: E.CommitPending(); Printing.Print(this, E.Doc); break;
                case Cmd.PageSetup: case Cmd.PreviewPageSetup: E.CommitPending(); if (PageSetupDialog.ShowFor(this, E.Doc)) _preview?.Invalidate(); break;
                case Cmd.PrintPreview: EnterPreview(); break;
                case Cmd.FromScanner: FromScanner(); break;
                case Cmd.SendEmail: SendEmail(); break;
                case Cmd.DesktopFill: SetWallpaper(10, 0); break;
                case Cmd.DesktopTile: SetWallpaper(0, 1); break;
                case Cmd.DesktopCenter: SetWallpaper(0, 0); break;
                case Cmd.Properties: E.CommitPending(); PropertiesDialog.ShowFor(this, E); break;
                case Cmd.About: AboutDialog.ShowFor(this); break;
                case Cmd.Exit: Close(); break;
                case Cmd.Help: HelpDialog.ShowFor(this); break;
                case Cmd.Undo: E.Undo(); break;
                case Cmd.Redo: E.Redo(); break;

                case Cmd.Paste: E.Paste(); break;
                case Cmd.PasteFrom: DoPasteFrom(); break;
                case Cmd.Cut: E.Cut(); break;
                case Cmd.Copy: E.Copy(); break;
                case Cmd.SelectTool: E.SetTool(ToolKind.Select); break;
                case Cmd.SelectRect: E.SelectionMode = SelectMode.Rectangle; E.SetTool(ToolKind.Select); E.Notify(Change.Tool); break;
                case Cmd.SelectFree: E.SelectionMode = SelectMode.FreeForm; E.SetTool(ToolKind.Select); E.Notify(Change.Tool); break;
                case Cmd.SelectAll: E.SelectAll(); break;
                case Cmd.InvertSelection: E.InvertSelection(); break;
                case Cmd.DeleteSelection: E.DeleteSelection(); break;
                case Cmd.TransparentSelection: E.SetTransparentSelection(!E.TransparentSelection); break;
                case Cmd.Crop: E.Crop(); break;
                case Cmd.Resize: E.CommitPendingExceptSelection(); ResizeDialog.ShowFor(this, E); break;
                case Cmd.RotateRight: E.RotateFlip(RotateKind.Right90); break;
                case Cmd.RotateLeft: E.RotateFlip(RotateKind.Left90); break;
                case Cmd.Rotate180: E.RotateFlip(RotateKind.R180); break;
                case Cmd.FlipVertical: E.RotateFlip(RotateKind.FlipVertical); break;
                case Cmd.FlipHorizontal: E.RotateFlip(RotateKind.FlipHorizontal); break;
                case Cmd.InvertColor: E.InvertColors(); break;

                case Cmd.Pencil: E.SetTool(ToolKind.Pencil); break;
                case Cmd.FillTool: E.SetTool(ToolKind.Fill); break;
                case Cmd.TextTool: E.SetTool(ToolKind.Text); break;
                case Cmd.Eraser: E.SetTool(ToolKind.Eraser); break;
                case Cmd.ColorPicker: E.SetTool(ToolKind.Picker); break;
                case Cmd.Magnifier: E.SetTool(ToolKind.Magnifier); break;
                case Cmd.Brushes: E.SetBrush(E.BrushType); break;
                case Cmd.Shapes: E.SetShape(E.ShapeType); break;
                case Cmd.Color1: E.ActiveColor = 1; E.Notify(Change.Colors); break;
                case Cmd.Color2: E.ActiveColor = 2; E.Notify(Change.Colors); break;
                case Cmd.EditColors: EditColors(); break;

                case Cmd.ZoomIn: case Cmd.PreviewZoomIn: if (_preview != null) _preview.ZoomStep(1); else Canvas.SetZoom(CanvasView.NextZoom(Canvas.Zoom, 1), null); break;
                case Cmd.ZoomOut: case Cmd.PreviewZoomOut: if (_preview != null) _preview.ZoomStep(-1); else Canvas.SetZoom(CanvasView.NextZoom(Canvas.Zoom, -1), null); break;
                case Cmd.Zoom100: Canvas.SetZoom(1f, null); break;
                case Cmd.Rulers: Canvas.Rulers = !Canvas.Rulers; E.Notify(Change.View); break;
                case Cmd.Gridlines: Canvas.Grid = !Canvas.Grid; E.Notify(Change.View); break;
                case Cmd.StatusBar: _showStatus = !_showStatus; DoLayout(); E.Notify(Change.View); break;
                case Cmd.FullScreen: E.CommitPending(); FullScreenWindow.ShowFor(this, E.Doc); break;
                case Cmd.Thumbnail: ToggleThumbnail(); break;
                case Cmd.DarkCanvas:
                    DarkCanvas = !DarkCanvas;
                    E.Notify(Change.View);
                    break;

                case Cmd.TextBold: E.TextTool.ToggleBold(); break;
                case Cmd.TextItalic: E.TextTool.ToggleItalic(); break;
                case Cmd.TextUnderline: E.TextTool.ToggleUnderline(); break;
                case Cmd.TextStrike: E.TextTool.ToggleStrike(); break;
                case Cmd.TextOpaque: E.TextTool.SetOpaque(true); break;
                case Cmd.TextTransparent: E.TextTool.SetOpaque(false); break;

                case Cmd.PreviewOnePage: _preview?.SetPages(1); break;
                case Cmd.PreviewTwoPages: _preview?.SetPages(2); break;
                case Cmd.PreviewNext: _preview?.Step(1); break;
                case Cmd.PreviewPrev: _preview?.Step(-1); break;
                case Cmd.PreviewClose: ExitPreview(); break;

                case Cmd.ToggleRibbon: Ribbon.Minimized = !Ribbon.Minimized; DoLayout(); break;
                case Cmd.QatBelow: QatBelow = !QatBelow; DoLayout(); Invalidate(); break;
            }
        }

        public bool IsEnabled(Cmd c)
        {
            bool preview = _preview != null;
            switch (c)
            {
                case Cmd.Undo: return !preview && E.CanUndo;
                case Cmd.Redo: return !preview && E.CanRedo;
                case Cmd.Paste: case Cmd.PasteMenu: return !preview && (ImageIO.ClipboardHasImage() || (E.TextEditing && ImageIO.ClipboardHasText()));
                case Cmd.Cut: case Cmd.Copy: return !preview && E.CanCopy;
                case Cmd.Crop: return !preview && E.HasSelection;
                case Cmd.InvertSelection: return !preview && E.HasSelection;
                case Cmd.DeleteSelection: return !preview && (E.HasSelection || (E.Tool == ToolKind.Shape && E.Shapes.HasPending) || E.TextEditing);
                case Cmd.OutlineMenu: case Cmd.FillMenu: return !preview && E.Tool == ToolKind.Shape;
                case Cmd.SizeMenu: return !preview && E.HasSize;
                case Cmd.PreviewPrint: case Cmd.PreviewPageSetup: case Cmd.PreviewZoomIn: case Cmd.PreviewZoomOut: case Cmd.PreviewOnePage: case Cmd.PreviewTwoPages: case Cmd.PreviewClose: return preview;
                case Cmd.PreviewNext: return preview && _preview.CanStep(1);
                case Cmd.PreviewPrev: return preview && _preview.CanStep(-1);
                case Cmd.TextBold: case Cmd.TextItalic: case Cmd.TextUnderline: case Cmd.TextStrike: case Cmd.TextOpaque: case Cmd.TextTransparent: case Cmd.FontFamily: case Cmd.FontSize:
                    return E.Tool == ToolKind.Text;
                case Cmd.AppMenu: case Cmd.Help: case Cmd.ToggleRibbon: case Cmd.Exit: case Cmd.About: case Cmd.QatMenu: case Cmd.QatBelow: return true;
                case Cmd.Print: case Cmd.PageSetup: case Cmd.Save: case Cmd.SaveAs: case Cmd.New: case Cmd.Open: case Cmd.PrintPreview: case Cmd.SendEmail: return true;
                default: return !preview;
            }
        }

        public bool IsChecked(Cmd c)
        {
            switch (c)
            {
                case Cmd.Pencil: return E.Tool == ToolKind.Pencil;
                case Cmd.FillTool: return E.Tool == ToolKind.Fill;
                case Cmd.TextTool: return E.Tool == ToolKind.Text;
                case Cmd.Eraser: return E.Tool == ToolKind.Eraser;
                case Cmd.ColorPicker: return E.Tool == ToolKind.Picker;
                case Cmd.Magnifier: return E.Tool == ToolKind.Magnifier;
                case Cmd.Brushes: return E.Tool == ToolKind.Brush;
                case Cmd.SelectTool: return E.Tool == ToolKind.Select;
                case Cmd.SelectRect: return E.SelectionMode == SelectMode.Rectangle;
                case Cmd.SelectFree: return E.SelectionMode == SelectMode.FreeForm;
                case Cmd.TransparentSelection: return E.TransparentSelection;
                case Cmd.Color1: return E.ActiveColor == 1;
                case Cmd.Color2: return E.ActiveColor == 2;
                case Cmd.Rulers: return Canvas.Rulers;
                case Cmd.Gridlines: return Canvas.Grid;
                case Cmd.StatusBar: return _showStatus;
                case Cmd.Thumbnail: return _thumb != null;
                case Cmd.DarkCanvas: return DarkCanvas;
                case Cmd.TextBold: return E.TextTool.CurrentStyle.Bold;
                case Cmd.TextItalic: return E.TextTool.CurrentStyle.Italic;
                case Cmd.TextUnderline: return E.TextTool.CurrentStyle.Underline;
                case Cmd.TextStrike: return E.TextTool.CurrentStyle.Strike;
                case Cmd.TextOpaque: return E.TextTool.Opaque;
                case Cmd.TextTransparent: return !E.TextTool.Opaque;
                case Cmd.PreviewOnePage: return _preview != null && _preview.Pages == 1;
                case Cmd.PreviewTwoPages: return _preview != null && _preview.Pages == 2;
                case Cmd.ToggleRibbon: return Ribbon.Minimized;
                case Cmd.QatBelow: return QatBelow;
            }
            return false;
        }

        // ================================================================== commands with UI
        void DoOpen()
        {
            E.CommitPending();
            if (!ConfirmDiscard()) return;
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Open";
                dlg.Filter = ImageIO.OpenFilter;
                dlg.FilterIndex = 1;
                if (!string.IsNullOrEmpty(E.Doc.FilePath)) dlg.InitialDirectory = Path.GetDirectoryName(E.Doc.FilePath);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                OpenFile(dlg.FileName, false, false);   // the user already confirmed discarding
            }
        }

        void DoPasteFrom()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Paste From";
                dlg.Filter = ImageIO.OpenFilter;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try { E.PasteImage(ImageIO.Load(dlg.FileName)); }
                catch (Exception) { ShowError("Paint cannot read this file.\nThis is not a valid bitmap file, or its format is not currently supported."); }
            }
        }

        void EditColors()
        {
            int slot = E.ActiveColor;
            uint start = slot == 1 ? E.Color1 : E.Color2;
            if (ColorPickerDialog.Pick(this, start, out uint c))
            {
                if (slot == 1 && E.TextEditing) E.TextTool.ApplyTextColor(c);
                E.SetColor(slot, c);
                E.AddCustomColor(c);
            }
        }

        void SetWallpaper(int style, int tile)
        {
            E.CommitPending();
            var d = E.Doc;
            if (string.IsNullOrEmpty(d.FilePath) || d.Dirty)
            {
                if (MessageDialog.Show(this, "Paint", "You must save the file before choosing it as desktop background.", new[] { "&Save", "Cancel" }, 0, MessageDialog.Kind.Info) != 0) return;
                if (!Save()) return;
            }
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", true))
                {
                    k?.SetValue("WallpaperStyle", style.ToString());
                    k?.SetValue("TileWallpaper", tile.ToString());
                }
                if (!Native.SystemParametersInfo(0x14 /*SPI_SETDESKWALLPAPER*/, 0, d.FilePath, 3)) throw new Win32Exception();
            }
            catch (Exception ex) { ShowError("The desktop background could not be changed.\n" + ex.Message); }
        }

        sealed class Win32Exception : Exception { public Win32Exception() : base(new System.ComponentModel.Win32Exception().Message) { } }

        // ---- e-mail through Simple MAPI ----
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MapiFileDescW { public int ulReserved, flFlags, nPosition; public string lpszPathName, lpszFileName; public IntPtr lpFileType; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MapiMessageW { public int ulReserved; public string lpszSubject, lpszNoteText, lpszMessageType, lpszDateReceived, lpszConversationID; public int flFlags; public IntPtr lpOriginator; public int nRecipCount; public IntPtr lpRecips; public int nFileCount; public IntPtr lpFiles; }
        [DllImport("mapi32.dll", CharSet = CharSet.Unicode)] static extern int MAPISendMailW(IntPtr session, IntPtr ui, ref MapiMessageW msg, int flags, int reserved);
        [DllImport("mapi32.dll", CharSet = CharSet.Ansi)] static extern int MAPISendMail(IntPtr session, IntPtr ui, IntPtr msg, int flags, int reserved);

        void SendEmail()
        {
            E.CommitPending();
            var d = E.Doc;
            string name = string.IsNullOrEmpty(d.FilePath) ? "Untitled.png" : Path.GetFileNameWithoutExtension(d.FilePath) + ".png";
            string dir = Path.Combine(Path.GetTempPath(), "PaintDark");
            Directory.CreateDirectory(dir);
            string tmp = Path.Combine(dir, name);
            try { ImageIO.Save(d.Image, d.HasAlpha, tmp, "png", 32, d.DpiX); }
            catch (Exception ex) { ShowError("Could not prepare the attachment.\n" + ex.Message); return; }
            IntPtr fileMem = IntPtr.Zero;
            try
            {
                var fd = new MapiFileDescW { nPosition = -1, lpszPathName = tmp, lpszFileName = name };
                fileMem = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MapiFileDescW)));
                Marshal.StructureToPtr(fd, fileMem, false);
                var msg = new MapiMessageW { lpszSubject = Path.GetFileNameWithoutExtension(name), lpszNoteText = "", nFileCount = 1, lpFiles = fileMem };
                int rc = MAPISendMailW(IntPtr.Zero, Handle, ref msg, 0x8 /*MAPI_DIALOG*/ | 0x1 /*MAPI_LOGON_UI*/, 0);
                if (rc != 0 && rc != 1 /*MAPI_USER_ABORT*/)
                    ShowError("There is no email program associated to perform the requested action. Please install an email program or, if one is already installed, create an association in the Default Programs control panel.");
            }
            catch (Exception) { ShowError("There is no email program associated to perform the requested action."); }
            finally { if (fileMem != IntPtr.Zero) { Marshal.DestroyStructure(fileMem, typeof(MapiFileDescW)); Marshal.FreeHGlobal(fileMem); } }
        }

        // ---- scanner / camera through WIA automation ----
        void FromScanner()
        {
            E.CommitPending();
            if (!ConfirmDiscard()) return;
            try
            {
                var t = Type.GetTypeFromProgID("WIA.CommonDialog");
                if (t == null) throw new COMException("WIA not available", unchecked((int)0x80210015));
                object dlg = Activator.CreateInstance(t);
                object img = t.InvokeMember("ShowAcquireImage", BindingFlags.InvokeMethod, null, dlg,
                    new object[] { 0, 0, 131072, "{B96B3CAB-0728-11D3-9D7B-0000F81EF32E}", false, true, false });
                if (img == null) return;
                string tmp = Path.Combine(Path.GetTempPath(), "PaintDark_scan_" + Guid.NewGuid().ToString("N") + ".bmp");
                img.GetType().InvokeMember("SaveFile", BindingFlags.InvokeMethod, null, img, new object[] { tmp });
                var li = ImageIO.Load(tmp);
                try { File.Delete(tmp); } catch { }
                var doc = new Document(li.Image) { HasAlpha = false, DpiX = li.DpiX, DpiY = li.DpiY };
                E.Doc.MarkSaved();
                E.SetDocument(doc);
                doc.MarkDirty();
                UpdateTitle();
            }
            catch (TargetInvocationException tie) when (tie.InnerException is COMException ce) { ScannerError(ce.ErrorCode); }
            catch (COMException ce) { ScannerError(ce.ErrorCode); }
            catch (Exception ex) { ShowError("Unable to retrieve picture from device. Verify the device is properly connected and try again.\n" + ex.Message); }
        }

        void ScannerError(int hr)
        {
            if (hr == unchecked((int)0x80210064) /*WIA_ERROR_USER_CANCELLED-ish*/ || hr == 1) return;
            ShowError("Unable to communicate with the device. Verify the device is properly connected and try again.");
        }

        // ================================================================== print preview
        void EnterPreview()
        {
            E.CommitPending();
            if (_preview != null) return;
            _preview = new PrintPreviewView(this, E.Doc);
            _preview.SetDpi(D.Value);
            Controls.Add(_preview);
            _preview.BringToFront();
            Canvas.Visible = false;
            Ribbon.PreviewMode = true;
            DoLayout();
            _preview.Focus();
        }

        void ExitPreview()
        {
            if (_preview == null) return;
            Controls.Remove(_preview);
            _preview.Dispose();
            _preview = null;
            Canvas.Visible = true;
            Ribbon.PreviewMode = false;
            DoLayout();
            Canvas.Focus();
        }

        // ================================================================== thumbnail
        ThumbnailWindow _thumb;
        bool _thumbWanted;

        void ToggleThumbnail()
        {
            if (_thumb != null) { CloseThumbnail(); return; }
            _thumb = new ThumbnailWindow(this, E);
            _thumb.FormClosed += (s, e) => { _thumb = null; Ribbon.Invalidate(); };
            _thumb.Show(this);
        }

        void InvalidateThumbnail() => _thumb?.Invalidate();

        void CloseThumbnail()
        {
            if (_thumb == null) return;
            var t = _thumb;
            _thumb = null;
            t.Close();
        }
    }
}
