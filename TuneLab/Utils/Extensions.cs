using System;
using System.Collections.Generic;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Layout;
using ReactiveUI;
using TuneLab.Base.Data;
using TuneLab.Base.Structures;
using TuneLab.GUI.Input;
using Rect = Avalonia.Rect;
using RoundedRect = Avalonia.RoundedRect;
using TuneLab.GUI;
using TuneLab.Views;
using TuneLab.Base.Science;
using TuneLab.Base.Utils;
using System.Threading.Tasks;
using TuneLab.Data;
using TuneLab.Extensions.Formats.DataInfo;

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

    public static MenuItem SetName(this MenuItem menuItem, string name)
    {
        menuItem.Header = name;
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

    public static Color GetColor(this ITrack track)
    {
        if (Color.TryParse(track.Color.Value, out var color)) 
            return color;

        return Style.DefaultTrackColor;
    }

    public static void NewTrack(this IProject project)
    {
        project.AddTrack(new TrackInfo() { Name = "Track_" + (project.Tracks.Count + 1), Color = Style.GetNewColor(project.Tracks.Count) });
    }
}
