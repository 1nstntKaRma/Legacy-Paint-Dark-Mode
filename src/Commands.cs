using System.Collections.Generic;

namespace PaintDark
{
    public enum Cmd
    {
        None,
        // File / application
        AppMenu, New, Open, Save, SaveAs, SaveAsPng, SaveAsJpeg, SaveAsBmp, SaveAsGif, SaveAsOther, Print, PageSetup, PrintPreview,
        FromScanner, SendEmail, DesktopFill, DesktopTile, DesktopCenter, Properties, About, Exit, Help,
        Undo, Redo,
        // Home
        Paste, PasteMenu, PasteFrom, Cut, Copy,
        SelectTool, SelectMenu, SelectRect, SelectFree, SelectAll, InvertSelection, DeleteSelection, TransparentSelection,
        Crop, Resize, RotateMenu, RotateRight, RotateLeft, Rotate180, FlipVertical, FlipHorizontal, InvertColor,
        Pencil, FillTool, TextTool, Eraser, ColorPicker, Magnifier,
        Brushes, BrushesMenu, Shapes, ShapesMore, OutlineMenu, FillMenu, SizeMenu,
        Color1, Color2, EditColors,
        // View
        ZoomIn, ZoomOut, Zoom100, Rulers, Gridlines, StatusBar, FullScreen, Thumbnail, DarkCanvas,
        // Text
        TextBold, TextItalic, TextUnderline, TextStrike, TextOpaque, TextTransparent, FontFamily, FontSize,
        // Print preview
        PreviewPrint, PreviewPageSetup, PreviewZoomIn, PreviewZoomOut, PreviewOnePage, PreviewTwoPages, PreviewNext, PreviewPrev, PreviewClose,
        // chrome
        ToggleRibbon, QatMenu, QatBelow,
    }

    public sealed class CommandInfo
    {
        public string Label, TipTitle, Tip, KeyTip;
        public Ico Icon, LargeIcon;
    }

    /// <summary>Labels, tooltips and key tips (taken from the original Paint resources).</summary>
    public static class Commands
    {
        static readonly Dictionary<Cmd, CommandInfo> _info = new Dictionary<Cmd, CommandInfo>();

        static void A(Cmd c, string label, string tipTitle, string tip, Ico icon = Ico.None, Ico large = Ico.None, string key = null)
            => _info[c] = new CommandInfo { Label = label, TipTitle = tipTitle ?? label, Tip = tip, Icon = icon, LargeIcon = large == Ico.None ? icon : large, KeyTip = key };

        public static CommandInfo Info(Cmd c) => _info.TryGetValue(c, out var i) ? i : new CommandInfo { Label = c.ToString(), TipTitle = c.ToString() };

        static Commands()
        {
            A(Cmd.AppMenu, "File", "Paint", "Click here to open, save, or print and to see everything else you can do with your picture.", key: "F");
            A(Cmd.New, "&New", "New (Ctrl+N)", "Create a new picture.", Ico.New, Ico.New, "N");
            A(Cmd.Open, "&Open", "Open (Ctrl+O)", "Open an existing picture.", Ico.Open, Ico.Open, "O");
            A(Cmd.Save, "&Save", "Save (Ctrl+S)", "Save the current picture.", Ico.Save, Ico.SaveBig, "S");
            A(Cmd.SaveAs, "Save &as", "Save as (F12)", "Save the current picture as a new file.", Ico.SaveAs, Ico.SaveAs, "A");
            A(Cmd.SaveAsPng, "&PNG picture", null, "Save a photo or drawing with high quality and use it on your computer or on the web.", Ico.PicPng, Ico.PicPng, "P");
            A(Cmd.SaveAsJpeg, "&JPEG picture", null, "Save a photo with good quality and use it on your computer, in email, or on the web.", Ico.PicJpeg, Ico.PicJpeg, "J");
            A(Cmd.SaveAsBmp, "&BMP picture", null, "Save any kind of picture with high quality and use it on your computer.", Ico.PicBmp, Ico.PicBmp, "B");
            A(Cmd.SaveAsGif, "&GIF picture", null, "Save a simple drawing with lower quality and use it in email or on the web.", Ico.PicGif, Ico.PicGif, "G");
            A(Cmd.SaveAsOther, "&Other formats", null, "Open the Save As dialog box to select from all possible file types.", Ico.PicOther, Ico.PicOther, "O");
            A(Cmd.Print, "&Print", "Print (Ctrl+P)", "Select a printer, number of copies, and other printing options before printing.", Ico.Print, Ico.Print, "P");
            A(Cmd.PageSetup, "Page &setup", null, "Change the layout of the picture.", Ico.PageSetup, Ico.PageSetup, "S");
            A(Cmd.PrintPreview, "Print pre&view", "Print preview", "Preview and make changes before printing.", Ico.PrintPreview, Ico.PrintPreview, "V");
            A(Cmd.FromScanner, "Fro&m scanner or camera", "From scanner or camera", "Import from scanner or camera.", Ico.Scanner, Ico.Scanner, "M");
            A(Cmd.SendEmail, "Sen&d in email", "Email", "Send a copy of the picture in an email message as an attachment.", Ico.Email, Ico.Email, "D");
            A(Cmd.DesktopFill, "&Fill", null, "Fill the entire screen with the picture.", Ico.DeskFill, Ico.DeskFill, "F");
            A(Cmd.DesktopTile, "&Tile", null, "Tile the picture so it repeats and fills the entire screen.", Ico.DeskTile, Ico.DeskTile, "T");
            A(Cmd.DesktopCenter, "&Center", null, "Center the picture in the middle of the screen.", Ico.DeskCenter, Ico.DeskCenter, "C");
            A(Cmd.Properties, "Prop&erties", "Properties (Ctrl+E)", "Change the properties of the picture.", Ico.Properties, Ico.Properties, "E");
            A(Cmd.About, "Abou&t Paint", "About Paint", "Show version and license information.", Ico.About, Ico.About, "T");
            A(Cmd.Exit, "E&xit", "Exit", "Close Paint.", Ico.Exit, Ico.Exit, "X");
            A(Cmd.Help, "Help", "Paint Help (F1)", "Get help on using Paint.", Ico.Help, key: "?");
            A(Cmd.Undo, "Undo", "Undo (Ctrl+Z)", "Undo last action.", Ico.Undo);
            A(Cmd.Redo, "Redo", "Redo (Ctrl+Y)", "Redo last action.", Ico.Redo);

            A(Cmd.Paste, "&Paste", "Paste (Ctrl+V)", "Paste the contents of the Clipboard.", Ico.PasteSmall, Ico.Paste, "V");
            A(Cmd.PasteMenu, "Paste", "Paste (Ctrl+V)", "Click here for more options, such as pasting contents from the Clipboard or from a file", Ico.PasteSmall, Ico.Paste, "V");
            A(Cmd.PasteFrom, "Paste &from", "Paste from", "Show the Paste From dialog box and pick a file to paste.", Ico.PasteFrom, Ico.PasteFrom, "F");
            A(Cmd.Cut, "Cu&t", "Cut (Ctrl+X)", "Cut the selection from the canvas and put it on the Clipboard.", Ico.Cut, key: "X");
            A(Cmd.Copy, "&Copy", "Copy (Ctrl+C)", "Copy the selection from the canvas and put it on the Clipboard.", Ico.Copy, key: "C");
            A(Cmd.SelectTool, "Select", "Selection", "Select a part of the picture.", Ico.SelRect, Ico.Select, "SE");
            A(Cmd.SelectMenu, "Select", "Selection", "Click here for selection shapes and options.", Ico.SelRect, Ico.Select, "SE");
            A(Cmd.SelectRect, "&Rectangular selection", null, "Select a rectangular area by drawing on the canvas.", Ico.SelRect, key: "R");
            A(Cmd.SelectFree, "&Free-form selection", null, "Select an area of any shape by drawing on the canvas.", Ico.SelFree, key: "F");
            A(Cmd.SelectAll, "Select &all", "Select all (Ctrl+A)", "Select the entire picture.", Ico.SelAll, key: "A");
            A(Cmd.InvertSelection, "&Invert selection", null, "Reverse the current selection.", Ico.SelInvert, key: "I");
            A(Cmd.DeleteSelection, "&Delete", "Delete selection", "Delete the current selection from the canvas.", Ico.SelDelete, key: "D");
            A(Cmd.TransparentSelection, "&Transparent selection", null, "Make the background color in the selection transparent or opaque.", Ico.SelTransparent, key: "T");
            A(Cmd.Crop, "C&rop", "Crop (Ctrl+Shift+X)", "Crop the picture so it only contains the current selection.", Ico.Crop, key: "R");
            A(Cmd.Resize, "Re&size", "Resize and skew (Ctrl+W)", "Resize and skew the picture or selection.", Ico.Resize, key: "RE");
            A(Cmd.RotateMenu, "R&otate", "Rotate or flip", "Rotate or flip the picture or selection.", Ico.Rotate, key: "RO");
            A(Cmd.RotateRight, "Rotate &right 90°", null, "Rotate the picture or selection by 90 degrees right.", Ico.RotRight, key: "R");
            A(Cmd.RotateLeft, "Rotate &left 90°", null, "Rotate the picture or selection by 90 degrees left.", Ico.RotLeft, key: "L");
            A(Cmd.Rotate180, "Ro&tate 180°", null, "Rotate the picture or selection by 180 degrees.", Ico.Rot180, key: "T");
            A(Cmd.FlipVertical, "Flip &vertical", null, "Flip the picture or selection vertically.", Ico.FlipV, key: "V");
            A(Cmd.FlipHorizontal, "Flip &horizontal", null, "Flip the picture or selection horizontally.", Ico.FlipH, key: "H");
            A(Cmd.InvertColor, "Inv&ert color", "Invert color (Ctrl+Shift+I)", "Invert the colors of the picture or selection.", Ico.InvertColor, key: "E");
            A(Cmd.Pencil, "Pencil", null, "Draw a free-form line with the selected line width.", Ico.Pencil, key: "P");
            A(Cmd.FillTool, "Fill with color", null, "Click an area on the canvas to fill it with the foreground color, or right-click to fill it with the background color.", Ico.Fill, key: "K");
            A(Cmd.TextTool, "Text", null, "Insert text into the picture.", Ico.Text, key: "T");
            A(Cmd.Eraser, "Eraser", null, "Erase part of the picture and replace it with the background color.", Ico.Eraser, key: "ER");
            A(Cmd.ColorPicker, "Color picker", null, "Pick a color from the picture and use it for drawing.", Ico.Picker, key: "D");
            A(Cmd.Magnifier, "Magnifier", null, "Change the magnification for a part of the picture.", Ico.Magnifier, key: "M");
            A(Cmd.Brushes, "Brushes", null, "Draw with different kinds of brushes.", Ico.BrushBrush, Ico.BrushBrush, "B");
            A(Cmd.BrushesMenu, "Brushes", null, "Draw with different kinds of brushes.", Ico.BrushBrush, Ico.BrushBrush, "B");
            A(Cmd.Shapes, "Shapes", null, "Insert ready-made shapes such as rectangles and circles, triangles, arrows, stars, and callouts.", Ico.Shapes, Ico.Shapes, "SH");
            A(Cmd.ShapesMore, "Shapes", null, "Insert ready-made shapes such as rectangles and circles, triangles, arrows, stars, and callouts.", Ico.Shapes, Ico.Shapes, "SH");
            A(Cmd.OutlineMenu, "Out&line", "Shape outline", "Select the medium for the shape outline.", Ico.Outline, key: "O");
            A(Cmd.FillMenu, "&Fill", "Shape fill", "Select the medium for the shape fill.", Ico.ShapeFill, key: "F");
            A(Cmd.SizeMenu, "Size", "Size (Ctrl++, Ctrl+-)", "Select the width for the selected tool.", Ico.Size, Ico.Size, "SZ");
            A(Cmd.Color1, "Color\n1", "Color 1 (foreground color)", "Click here and then select a color from the color palette. This color is used with the pencil and with brushes, as well as for shape outlines.", key: "1");
            A(Cmd.Color2, "Color\n2", "Color 2 (background color)", "Click here and then select a color from the color palette. This color is used with the eraser and for shape fills.", key: "2");
            A(Cmd.EditColors, "Edit\ncolors", "Edit colors", "Select a color from the color palette.", Ico.EditColors, Ico.EditColors, "EC");

            A(Cmd.ZoomIn, "Zoom\nin", "Zoom in (Ctrl+PgUp)", "Zoom in on the current picture.", Ico.ZoomIn, Ico.ZoomIn, "I");
            A(Cmd.ZoomOut, "Zoom\nout", "Zoom out (Ctrl+PgDn)", "Zoom out on the current picture.", Ico.ZoomOut, Ico.ZoomOut, "O");
            A(Cmd.Zoom100, "100\n%", "100%", "Zoom to 100%.", Ico.Zoom100, Ico.Zoom100, "M");
            A(Cmd.Rulers, "Rulers", "Rulers (Ctrl+R)", "View and use rulers to line up and measure objects in your picture.", Ico.Rulers, key: "R");
            A(Cmd.Gridlines, "Gridlines", "Gridlines (Ctrl+G)", "View and use gridlines to align objects in your picture.", Ico.Gridlines, key: "G");
            A(Cmd.StatusBar, "Status bar", "Status bar", "Show or hide status bar at the bottom of the window.", Ico.StatusBarIco, key: "S");
            A(Cmd.FullScreen, "Full\nscreen", "Full screen (F11)", "View the picture in full screen.", Ico.FullScreen, Ico.FullScreen, "F");
            A(Cmd.Thumbnail, "Thumbnail", "Thumbnail", "Show or hide the Thumbnail window.", Ico.Thumbnail, Ico.Thumbnail, "T");
            A(Cmd.DarkCanvas, "Dark\ncanvas", "Dark canvas", "New pictures start with a black canvas, white as Color 1 and black as Color 2. Turn off for the classic white canvas.", Ico.DarkCanvas, Ico.DarkCanvas, "D");

            A(Cmd.TextBold, "Bold", "Bold (Ctrl+B)", "Sets or clears the text bold attribute.", Ico.Bold, key: "1");
            A(Cmd.TextItalic, "Italic", "Italic (Ctrl+I)", "Sets or clears the text italic attribute.", Ico.Italic, key: "2");
            A(Cmd.TextUnderline, "Underline", "Underline (Ctrl+U)", "Sets or clears the text underline attribute.", Ico.Underline, key: "3");
            A(Cmd.TextStrike, "Strikethrough", "Strikethrough", "Draw a line through the middle of the selected text.", Ico.Strike, key: "4");
            A(Cmd.TextOpaque, "Opaque", "Opaque background", "Fill the text box with the background color (Color 2).", Ico.Opaque, key: "O");
            A(Cmd.TextTransparent, "Transparent", "Transparent background", "Show the picture behind the text.", Ico.TransparentBg, key: "T");
            A(Cmd.FontFamily, "Font family", "Font family", "Selects the font used by the text.", key: "FF");
            A(Cmd.FontSize, "Font size", "Font size", "Selects the point size of the text.", key: "FS");

            A(Cmd.PreviewPrint, "Print", "Print (Ctrl+P)", "Print the current picture.", Ico.Print, Ico.Print, "P");
            A(Cmd.PreviewPageSetup, "Page\nsetup", "Page setup", "Change the layout of the picture.", Ico.PageSetup, Ico.PageSetup, "S");
            A(Cmd.PreviewZoomIn, "Zoom\nin", "Zoom in", "Zoom in on the current picture.", Ico.ZoomIn, Ico.ZoomIn, "I");
            A(Cmd.PreviewZoomOut, "Zoom\nout", "Zoom out", "Zoom out on the current picture.", Ico.ZoomOut, Ico.ZoomOut, "O");
            A(Cmd.PreviewOnePage, "One\npage", "One page", "Zoom the picture so that one page fits in the window.", Ico.OnePage, Ico.OnePage, "1");
            A(Cmd.PreviewTwoPages, "Two\npages", "Two pages", "Zoom the picture so that two pages fit in the window.", Ico.TwoPages, Ico.TwoPages, "2");
            A(Cmd.PreviewNext, "Next\npage", "Next page", "Go to the next page in the picture.", Ico.NextPage, Ico.NextPage, "N");
            A(Cmd.PreviewPrev, "Previous\npage", "Previous page", "Go to the previous page in the picture.", Ico.PrevPage, Ico.PrevPage, "V");
            A(Cmd.PreviewClose, "Close print\npreview", "Close print preview", "Close print preview and return to your picture.", Ico.ClosePreview, Ico.ClosePreview, "C");

            A(Cmd.ToggleRibbon, "Minimize the Ribbon", "Minimize the Ribbon (Ctrl+F1)", "Show only the tab names on the Ribbon.");
            A(Cmd.QatMenu, "Customize Quick Access Toolbar", null, null);
        }

        public static readonly string[] PaletteNames =
        {
            "Black", "Gray-50%", "Dark red", "Red", "Orange", "Yellow", "Green", "Turquoise", "Indigo", "Purple",
            "White", "Gray-25%", "Brown", "Rose", "Gold", "Light yellow", "Lime", "Light turquoise", "Blue-gray", "Lavender",
        };

        public static readonly string[] BrushNames = { "Brush", "Calligraphy brush 1", "Calligraphy brush 2", "Airbrush", "Oil brush", "Crayon", "Marker", "Natural pencil", "Watercolor brush" };
        public static readonly string[] BrushTips =
        {
            "Draws using a brush with the selected shape and size.", "Draws using a 45 degree calligraphy brush of the selected shape and size.",
            "Draws using a 135 degree calligraphy brush of the selected shape and size.", "Draws using an airbrush of the selected size.",
            "Draws using the oil brush.", "Draws using a crayon brush.", "Draws using a marker.", "Draws using the natural pencil in the size chosen.", "Draws using the watercolor brush.",
        };

        public static readonly string[] ShapeNames =
        {
            "Line", "Curve", "Oval", "Rectangle", "Rounded rectangle", "Polygon", "Triangle", "Right triangle", "Diamond", "Pentagon", "Hexagon",
            "Right arrow", "Left arrow", "Up arrow", "Down arrow", "Four-point star", "Five-point star", "Six-point star",
            "Rounded rectangular callout", "Oval callout", "Cloud callout", "Heart", "Lightning",
        };

        public static string ShapeTip(ShapeKind k)
        {
            if (k == ShapeKind.Line) return "Draws a straight line with the selected line width.";
            if (k == ShapeKind.Curve) return "Draws a curved line with the selected line width.";
            if (k == ShapeKind.OvalCallout) return "Draws an oval-shaped callout with the selected fill style.";
            var n = ShapeNames[(int)k].ToLowerInvariant();
            string art = "aeiou".IndexOf(n[0]) >= 0 ? "an" : "a";
            return "Draws " + art + " " + n + " with the selected fill style.";
        }

        public static readonly string[] OutlineNames = { "No outline", "Solid color", "Crayon", "Marker", "Oil", "Natural pencil", "Watercolor" };
        public static readonly string[] FillNames = { "No fill", "Solid color", "Crayon", "Marker", "Oil", "Natural pencil", "Watercolor" };
    }
}
