using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TuneLab.Data;
using TuneLab.Core.DataInfo;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Science;
using TuneLab.Foundation.Utils;
using TuneLab.GUI;
using TuneLab.GUI.Input;
using TuneLab.I18N;
using Rect = Avalonia.Rect;
using RoundedRect = Avalonia.RoundedRect;

namespace TuneLab.Utils;

internal static class Extensions
{
    public static Rect Adjusted(this Rect rect, double left, double top, double right, double bottom)
    {
        return new Rect(rect.Left + left, rect.Top + top, rect.Width + right - left, rect.Height + bottom - top);
    }

    public static Color Opacity(this Color color, double opacity)
    {
        return new Color((byte)(color.A * opacity), color.R, color.G, color.B);
    }

    public static Rect Rect(this Layoutable layoutable)
    {
        return new Rect(0, 0, layoutable.Bounds.Width, layoutable.Bounds.Height);
    }

    public static RoundedRect ToRoundedRect(this Rect rect, Avalonia.CornerRadius radius)
    {
        return new RoundedRect(rect, radius);
    }

    public static Point ToPoint(this Avalonia.Point point)
    {
        return new Point(point.X, point.Y);
    }

    public static Point ToPoint(this Avalonia.Vector vector)
    {
        return new Point(vector.X, vector.Y);
    }

    public static Avalonia.Point ToAvaloniaPoint(this Avalonia.Vector vector)
    {
        return new Avalonia.Point(vector.X, vector.Y);
    }

    public static Avalonia.Point ToAvalonia(this Point point)
    {
        return new Avalonia.Point(point.X, point.Y);
    }

    public static Avalonia.Vector ToVector(this Point point)
    {
        return new Avalonia.Vector(point.X, point.Y);
    }

    public static Avalonia.Vector ToVector(this Avalonia.Point point)
    {
        return new Avalonia.Vector(point.X, point.Y);
    }

    public static bool FuzzyEquals(this Avalonia.Point point, Avalonia.Point other, double tolerance = 1)
    {
        return Math.Abs(point.X - other.X) < tolerance && Math.Abs(point.Y - other.Y) < tolerance;
    }

    public static MenuItem SetName(this MenuItem menuItem, string name)
    {
        menuItem.Header = name;
        return menuItem;
    }

    public static MenuItem SetTrName(this MenuItem menuItem, string name)
    {
        menuItem.Header = name.Tr(TC.Menu);
        // 由于其他组件未实现翻译热更新，菜单先禁掉热更新
        // TranslationManager.CurrentLanguage.Modified.Subscribe(() => menuItem.Header = name.Tr(TC.Menu));
        return menuItem;
    }

    public static MenuItem SetAction(this MenuItem menuItem, Action action)
    {
        menuItem.Command = ReactiveCommand.Create(action);
        return menuItem;
    }

    public static MenuItem SetShortcut(this MenuItem menuItem, Key key, ModifierKeys modifierKeys = GUI.Input.ModifierKeys.None)
    {
        var shortcut = new KeyGesture(key, modifierKeys.ToAvalonia());
        menuItem.InputGesture = shortcut;
        menuItem.HotKey = shortcut;
        return menuItem;
    }

    public static MenuItem SetInputGesture(this MenuItem menuItem, Key key, ModifierKeys modifierKeys = GUI.Input.ModifierKeys.None)
    {
        var keyGesture = new KeyGesture(key, modifierKeys.ToAvalonia());
        menuItem.InputGesture = keyGesture;
        return menuItem;
    }

    public static void Unfocus(this InputElement inputElement)
    {
        Avalonia.StyledElement? s = inputElement;
        while (true)
        {
            if (s == null)
                break;

            if (s.Parent is not InputElement i)
            {
                s = s.Parent;
                continue;
            }

            if (!i.Focusable)
            {
                s = s.Parent;
                continue;
            }

            i.Focus();
            return;
        }

        inputElement.IsEnabled = false;
        inputElement.IsEnabled = true;
    }

    public static bool IsHandledByTextBox(this KeyEventArgs e)
    {
        return e.Source is TextBox textBox && textBox.IsEnabled && textBox.IsEffectivelyVisible && textBox.IsFocused;
    }

    public static ModifierKeys ModifierKeys(this KeyEventArgs e)
    {
        return e.KeyModifiers.ToModifierKeys();
    }

    public static bool Match(this KeyEventArgs e, Key key, ModifierKeys modifiers = GUI.Input.ModifierKeys.None)
    {
        return e.Key == key && e.ModifierKeys() == modifiers;
    }

    public static bool HasModifiers(this KeyEventArgs e, ModifierKeys modifiers)
    {
        return (e.ModifierKeys() & modifiers) != 0;
    }

    public static SolidColorBrush ToBrush(this Color color)
    {
        return new SolidColorBrush(color);
    }

    public static void DrawString(this DrawingContext context, string text, Rect rect, IBrush brush, double fontSize, int alignment, int pivotAlignment, Avalonia.Point offset = new Avalonia.Point(), Typeface? typeface = null)
    {
        var anchor = alignment.Offset(rect.Width, rect.Height);
        context.DrawString(text, new Avalonia.Point(rect.X - anchor.Item1 + offset.X, rect.Y - anchor.Item2 + offset.Y), brush, fontSize, pivotAlignment, typeface);
    }

    public static void DrawString(this DrawingContext context, string text, Avalonia.Point point, IBrush brush, double fontSize, int alignment, Typeface? typeface = null)
    {
        typeface ??= Typeface.Default;
        if (string.IsNullOrWhiteSpace(text))
            return;

        var formattedText = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface.Value, fontSize, brush);
        var (x, y) = alignment.Offset(formattedText.Width, formattedText.Height);
        context.DrawText(formattedText, point + new Avalonia.Point(x, y));
    }

    public static PathGeometry ToPath(this IEnumerable<Avalonia.Point> points, bool isClosed = false)
    {
        var path = new PathGeometry();

        using var it = points.GetEnumerator();
        if (!it.MoveNext())
            return path;

        using (var pathContext = path.Open())
        {
            pathContext.BeginFigure(it.Current, false);
            while (it.MoveNext())
            {
                pathContext.LineTo(it.Current);
            }
            pathContext.EndFigure(isClosed);
        }

        return path;
    }

    public static void DrawCurve(this DrawingContext context, IEnumerable<Avalonia.Point> points, Color color, double thickness = 1, bool isClosed = false)
    {
        context.DrawGeometry(null, new Pen(color.ToBrush(), thickness, null, PenLineCap.Round, PenLineJoin.Round), points.ToPath(isClosed));
    }

    public static void DrawCurveLines(this DrawingContext context, IEnumerable<Avalonia.Point> points, Color color, double thickness = 1, bool isClosed = false)
    {
        using var it = points.GetEnumerator();
        if (!it.MoveNext())
            return;

        var pen = new Pen(color.ToBrush(), thickness, null, PenLineCap.Round, PenLineJoin.Round);
        List<List<Avalonia.Point>> pointLines = new();
        List<Avalonia.Point> pointLine = new();
        do
        {
            var point = it.Current;
            if (double.IsNaN(point.Y))
            {
                if (pointLine.IsEmpty())
                    continue;

                pointLines.Add(pointLine);
                pointLine = new();
                continue;
            }

            pointLine.Add(point);
        }
        while (it.MoveNext());

        if (!pointLine.IsEmpty())
            pointLines.Add(pointLine);

        foreach (var ps in pointLines)
        {
            context.DrawCurve(ps, color, thickness, isClosed);
        }
    }

    public static Window Window(this Avalonia.Visual visual)
    {
        return (Window)TopLevel.GetTopLevel(visual)!;
    }

    public static async Task ShowMessage(this Avalonia.Visual visual, string title, string message)
    {
        var dialog = new Dialog();
        dialog.SetTitle(title);
        dialog.SetMessage(message);
        dialog.AddButton("OK", Dialog.ButtonType.Primary);
        await dialog.ShowDialog(visual.Window());
    }

    public static async Task<IReadOnlyList<string>> OpenFiles(this Avalonia.Visual visual, FilePickerOpenOptions options)
    {
        options.AllowMultiple = true;
        return await visual.OpenFilesInternal(options);
    }

    public static async Task<IReadOnlyList<string>> OpenFolders(this Avalonia.Visual visual, FolderPickerOpenOptions options)
    {
        options.AllowMultiple = true;
        return await visual.OpenFoldersInternal(options);
    }

    public static async Task<string?> OpenFile(this Avalonia.Visual visual, FilePickerOpenOptions options)
    {
        options.AllowMultiple = false;
        var files = await OpenFilesInternal(visual, options);
        if (files.IsEmpty())
            return null;

        return files[0];
    }

    public static async Task<string?> OpenFolder(this Avalonia.Visual visual, FolderPickerOpenOptions options)
    {
        options.AllowMultiple = false;
        var files = await OpenFoldersInternal(visual, options);
        if (files.IsEmpty())
            return null;

        return files[0];
    }

    static async Task<IReadOnlyList<string>> OpenFilesInternal(this Avalonia.Visual visual, FilePickerOpenOptions options)
    {
        var toplevel = TopLevel.GetTopLevel(visual);
        if (toplevel == null)
            return [];

        var files = await toplevel.StorageProvider.OpenFilePickerAsync(options);
        List<string> result = [];
        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (path == null)
                continue;

            result.Add(path);
        }
        return result;
    }

    static async Task<IReadOnlyList<string>> OpenFoldersInternal(this Avalonia.Visual visual, FolderPickerOpenOptions options)
    {
        var toplevel = TopLevel.GetTopLevel(visual);
        if (toplevel == null)
            return [];

        var files = await toplevel.StorageProvider.OpenFolderPickerAsync(options);
        List<string> result = [];
        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (path == null)
                continue;

            result.Add(path);
        }
        return result;
    }

    public static void SetupToolTip(this Control control, string text, PlacementMode placementMode = PlacementMode.Top, double verticalOffset = 0, double horizontalOffset = 0, int showDelay = 0)
    {
        ToolTip.SetPlacement(control, placementMode);
        ToolTip.SetVerticalOffset(control, verticalOffset);
        ToolTip.SetHorizontalOffset(control, horizontalOffset);
        ToolTip.SetShowDelay(control, showDelay);
        ToolTip.SetTip(control, text);
    }

    static ContextMenu? mCurrentContextMenu = null;
    public static void OpenContextMenu(this Control control, ContextMenu menu)
    {
        if (mCurrentContextMenu != null)
            mCurrentContextMenu.Close();

        if (menu.ItemCount == 0)
            return;

        mCurrentContextMenu = menu;
        menu.Closed += (_, _) => mCurrentContextMenu = null;
        menu.Open(control);
    }

    public static Color Lerp(this Color c1, Color c2, double ratio)
    {
        return new Color(
            (byte)MathUtility.Lerp(c1.A, c2.A, ratio),
            (byte)MathUtility.Lerp(c1.R, c2.R, ratio),
            (byte)MathUtility.Lerp(c1.G, c2.G, ratio),
            (byte)MathUtility.Lerp(c1.B, c2.B, ratio)
        );
    }

    public static Color Brighter(this Color color, double ratio = 0.25)
    {
        var hsv = color.ToHsv();
        hsv = new HsvColor(hsv.A, hsv.H, hsv.S, hsv.V * (1 + ratio));
        return hsv.ToRgb();
    }

    public static Color Lighter(this Color color, double ratio = 0.25)
    {
        var d = (int)(255 * ratio);
        return new Color(color.A,
            (byte)(color.R + d).Limit(0, 255),
            (byte)(color.G + d).Limit(0, 255),
            (byte)(color.B + d).Limit(0, 255));
    }

    public static Color Whiter(this Color color, double ratio = 0.25)
    {
        return color.Lerp(new Color(color.A, 255, 255, 255), ratio);
    }

    public static Color Blacker(this Color color, double ratio = 0.2)
    {
        return color.Lerp(new Color(color.A, 0, 0, 0), ratio);
    }

    public static void AddDock(this DockPanel panel, Control control, Dock dock)
    {
        panel.Children.Add(control);
        DockPanel.SetDock(control, dock);
    }

    public static void AddDock(this DockPanel panel, Control control)
    {
        panel.Children.Add(control);
    }

    public static void NewTrack(this IProject project)
    {
        project.AddTrack(new TrackInfo() { Name = "Track".Tr(TC.Document) + "_" + (project.Tracks.Count + 1), Color = Style.GetNewColor(project.Tracks.Count) });
    }

    public static int PartsCount(this IProject project)
    {
        int count = 0;
        foreach (var track in project.Tracks)
        {
            count += track.Parts.Count;
        }
        return count;
    }
}
