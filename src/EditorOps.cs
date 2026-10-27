using System;
using System.Drawing;
using System.Windows.Forms;

namespace PaintDark
{
    public enum RotateKind { Right90, Left90, R180, FlipVertical, FlipHorizontal }

    public sealed partial class Editor
    {
        public bool HasSelection => Tool == ToolKind.Select && Select.HasSelection;
        public bool TextEditing => Tool == ToolKind.Text && TextTool.IsEditing;

        public void ResizeCanvas(Size s)
        {
            CommitPending();
            s = new Size(Math.Max(1, Math.Min(100000, s.Width)), Math.Max(1, Math.Min(100000, s.Height)));
            if (s == Doc.Size) return;
            Dib img;
            try { img = new Dib(s.Width, s.Height, Color2 | 0xFF000000u); }
            catch (OutOfMemoryException) { Form?.ShowError("There is not enough memory or resources to complete operation.\nClose some programs, and then try again."); return; }
            img.CopyFrom(Doc.Image, Doc.Bounds, 0, 0);
            ReplaceImage(img, "Resize canvas", Doc.HasAlpha && img.AnyTransparent(img.Bounds));
        }

        static Dib Transform(Dib src, RotateKind k)
        {
            switch (k)
            {
                case RotateKind.Right90: return Raster.Rotate90(src, true);
                case RotateKind.Left90: return Raster.Rotate90(src, false);
                case RotateKind.R180: return Raster.Rotate180(src);
                case RotateKind.FlipVertical: return Raster.Flip(src, false);
                default: return Raster.Flip(src, true);
            }
        }

        public void RotateFlip(RotateKind k)
        {
            if (HasSelection)
            {
                var f = Select.FloatingAtSize();
                var t = Transform(f, k);
                f.Dispose();
                Select.ReplaceFloating(t);
                return;
            }
            CommitPending();
            ReplaceImage(Transform(Doc.Image, k), k <= RotateKind.R180 ? "Rotate" : "Flip", Doc.HasAlpha);
        }

        /// <summary>Resize (pixels) then skew (degrees) the selection or the whole picture.</summary>
        public void ResizeSkew(int w, int h, double skewH, double skewV)
        {
            w = Math.Max(1, w); h = Math.Max(1, h);
            if (HasSelection)
            {
                var f = Select.FloatingAtSize();
                Dib r = (w != f.W || h != f.H) ? Raster.Resample(f, w, h) : f.Clone();
                f.Dispose();
                if (Math.Abs(skewH) > 0.001 || Math.Abs(skewV) > 0.001) { var s = Raster.Skew(r, skewH, skewV, 0); r.Dispose(); r = s; }
                Select.ReplaceFloating(r);
                return;
            }
            CommitPending();
            Dib img;
            try
            {
                img = (w != Doc.Width || h != Doc.Height) ? Raster.Resample(Doc.Image, w, h) : Doc.Image.Clone();
                if (Math.Abs(skewH) > 0.001 || Math.Abs(skewV) > 0.001) { var s = Raster.Skew(img, skewH, skewV, Color2 | 0xFF000000u); img.Dispose(); img = s; }
            }
            catch (OutOfMemoryException) { Form?.ShowError("There is not enough memory or resources to complete operation.\nClose some programs, and then try again."); return; }
            ReplaceImage(img, "Resize", Doc.HasAlpha);
        }

        public void Crop()
        {
            if (!HasSelection) return;
            bool alpha;
            Dib content;
            if (Select.IsFloating)
            {
                content = Select.CopyPixels(out alpha);
            }
            else
            {
                var r = Select.SelectionRect;
                r.Intersect(Doc.Bounds);
                if (r.Width <= 0 || r.Height <= 0) return;
                content = Select.CopyPixels(out alpha);
            }
            if (content == null) return;
            Select.UndoPending();        // drop the selection without stamping it
            ReplaceImage(content, "Crop", alpha);
            SetTool(ToolKind.Select);
        }

        public unsafe void InvertColors()
        {
            if (HasSelection)
            {
                var f = Select.FloatingAtSize();
                Raster.InvertColors(f, f.Bounds);
                Select.ReplaceFloating(f);
                return;
            }
            CommitPending();
            Raster.InvertColors(Doc.Image, Doc.Bounds);
            View?.InvalidateImage(Doc.Bounds);
            Commit(Doc.Bounds, "Invert color");
        }

        public void SelectAll()
        {
            if (TextEditing) { TextTool.SelectAllText(); return; }
            CommitPending();
            if (Tool != ToolKind.Select) SetTool(ToolKind.Select);
            Select.SelectAll();
        }

        public void InvertSelection()
        {
            if (Tool != ToolKind.Select) { SetTool(ToolKind.Select); }
            Select.InvertSelection();
        }

        public void DeleteSelection()
        {
            if (TextEditing) { TextTool.DeleteText(); return; }
            if (Tool == ToolKind.Shape && Shapes.HasPending) { Shapes.DeletePending(); return; }
            if (HasSelection) Select.Delete();
        }

        /// <summary>Commit pending work but keep an active selection (Resize/Rotate act on it).</summary>
        public void CommitPendingExceptSelection() { if (Tool == ToolKind.Select) return; CommitPending(); }

        public bool CanCopy => TextEditing ? TextTool.HasTextSelection : HasSelection;

        public void Copy()
        {
            if (TextEditing) { TextTool.CopyText(); return; }
            if (!HasSelection) return;
            var px = Select.CopyPixels(out bool alpha);
            if (px == null) return;
            try { ImageIO.SetClipboard(px, alpha); }
            catch (Exception ex) { Form?.ShowError("Could not copy to the Clipboard.\n" + ex.Message); }
            finally { px.Dispose(); }
        }

        public void Cut()
        {
            if (TextEditing) { TextTool.CutText(); return; }
            if (!HasSelection) return;
            Copy();
            Select.Delete();
        }

        public void Paste()
        {
            if (TextEditing && ImageIO.ClipboardHasText()) { TextTool.PasteText(); return; }
            LoadedImage li = null;
            try { li = ImageIO.GetClipboardImage(); } catch { }
            if (li == null)
            {
                if (ImageIO.ClipboardHasText() && !TextEditing) { Form?.ShowError("The information on the Clipboard can't be inserted into Paint."); }
                return;
            }
            PasteImage(li);
        }

        public void PasteImage(LoadedImage li)
        {
            CommitPending();
            var img = li.Image;
            // grow the canvas when the pasted picture is bigger (like Paint does)
            if (img.W > Doc.Width || img.H > Doc.Height)
                ResizeCanvas(new Size(Math.Max(img.W, Doc.Width), Math.Max(img.H, Doc.Height)));
            if (Tool != ToolKind.Select) SetTool(ToolKind.Select);
            var vp = View != null ? View.ImageViewport : Doc.Bounds;
            int x = Math.Max(0, Math.Min(vp.X, Doc.Width - img.W));
            int y = Math.Max(0, Math.Min(vp.Y, Doc.Height - img.H));
            if (li.HasAlpha) Doc.HasAlpha = Doc.HasAlpha || false;
            Select.PasteImage(img, li.HasAlpha, new Point(x, y));
        }

        public void ConvertToBlackAndWhite()
        {
            CommitPending();
            Raster.ToBlackAndWhite(Doc.Image);
            View?.InvalidateImage(Doc.Bounds);
            Commit(Doc.Bounds, "Black and white");
        }

        public void SetTransparentSelection(bool on)
        {
            TransparentSelection = on;
            Select.TransparencyChanged();
            Notify(Change.Tool | Change.Commands);
        }
    }
}
