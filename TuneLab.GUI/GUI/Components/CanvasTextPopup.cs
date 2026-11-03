using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TuneLab.SDK;
using TuneLab.Utils;

namespace TuneLab.GUI.Components;

// 画布上弹出的文本框（ICanvasHost.PopupTextBox 的宿主侧）。几何与 ICanvasDrawing.DrawText 同一套：文字区 = 插件给的矩形，
// 文字按 alignment 排在里面、字号随插件、字体随界面——传画字的参数，框里的字就正好盖在画出来的字上。外框（内边距 + 边线）
// 向外长，不占文字区。框不随内容变大，装不下时框内滚动（不自动折行，与 DrawText 一致）。
// 按键同宿主文本框：Enter / 点别处 = 确认，Esc = 取消；多行时 Shift+Enter 换行。结果经 finish 只交一次。
// 不是弹层：挂在画布子树里、按画布坐标摆放（同钢琴窗的歌词输入框）——面板滚动、钢琴窗滚动缩放时跟着画布走，被外层面板裁剪，
// 不会浮到窗口别处，滚动也不打断输入。
internal sealed class CanvasTextPopup
{
    const double MinTextWidth = 40;
    const double PaddingX = 6, PaddingY = 3, BorderWidth = 1;

    public CanvasTextPopup(PluginCanvas canvas, string text, double x, double y, double width, double height, double fontSize,
        CanvasAlignment alignment, bool multiline, Action<string?> finish)
    {
        mCanvas = canvas;
        mFinish = finish;

        double size = fontSize > 0 && double.IsFinite(fontSize) ? fontSize : 12;
        int align = Enum.IsDefined(alignment) ? (int)alignment : (int)CanvasAlignment.Center;
        int column = align % 4, row = align >> 2;   // 0 / 1 / 2 = 左（上）/ 中 / 右（下）

        // 太小的矩形按锚点扩到一行高与最小宽度：锚点（alignment 指的那个点）不动，往另一侧长。
        double lineHeight = new FormattedText("Xg", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Extensions.DrawStringTypeface, size, null).Height;
        double w = double.IsFinite(width) ? Math.Max(0, width) : 0;
        double h = double.IsFinite(height) ? Math.Max(0, height) : 0;
        double textWidth = Math.Max(w, MinTextWidth), textHeight = Math.Max(h, lineHeight);
        double textX = (double.IsFinite(x) ? x : 0) - (textWidth - w) * column / 2;
        double textY = (double.IsFinite(y) ? y : 0) - (textHeight - h) * row / 2;

        mBox = new TextInput()
        {
            FontSize = size,
            Padding = new(PaddingX, PaddingY),
            MinHeight = 0,
            Width = textWidth + 2 * PaddingX,
            Height = textHeight + 2 * PaddingY,
            // 白底黑字，同卷帘上的歌词输入框：画布底色与宿主输入框同档（深色），深色框盖在上面分不清边界。
            Background = Brushes.White,
            Foreground = Brushes.Black,
            CaretBrush = Brushes.Black,
            AcceptsReturn = multiline,
            NewLine = "\n",   // 换行在值里一律是 \n（同 DrawText、同多行字段的存值）
            TextWrapping = TextWrapping.NoWrap,
            TextAlignment = column switch { 1 => TextAlignment.Center, 2 => TextAlignment.Right, _ => TextAlignment.Left },
            VerticalContentAlignment = row switch { 1 => Avalonia.Layout.VerticalAlignment.Center, 2 => Avalonia.Layout.VerticalAlignment.Bottom, _ => Avalonia.Layout.VerticalAlignment.Top },
        };
        mBox.Display(text);
        mBox.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        mBox.LostFocus += (_, _) => Finish(mBox.Text);

        // 白底配深色描边：插件可能把底画成任何颜色（包括浅色），白底失去反差时还有深边勾出边界。
        mFrame = new Border()
        {
            Background = Brushes.White,
            CornerRadius = new(4),
            BorderThickness = new(BorderWidth),
            BorderBrush = Style.DARK.ToBrush(),
            Child = mBox,
        };

        // 外框左上角 = 文字区左上角再往外让出内边距与边线（外框向外长、不占文字区）。
        mRect = new Rect(textX - PaddingX - BorderWidth, textY - PaddingY - BorderWidth,
            textWidth + 2 * (PaddingX + BorderWidth), textHeight + 2 * (PaddingY + BorderWidth));
    }

    public void Open()
    {
        mCanvas.AttachInline(mFrame, mRect);
        // 点别处 = 确认：焦点移走时文本框自己会失焦（LostFocus），但点到不可聚焦的地方焦点不动，所以在窗口上听按下。
        mWindow = TopLevel.GetTopLevel(mCanvas);
        mWindow?.AddHandler(InputElement.PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        Dispatcher.UIThread.Post(() =>
        {
            if (mDone)
                return;
            mBox.Focus();
            mBox.CaretIndex = mBox.Text.Length;
        }, DispatcherPriority.Loaded);
    }

    // 宿主替插件关掉（实例作废、又弹了一个）：结果为 null。
    public void Cancel() => Finish(null);

    void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && (source == mFrame || mFrame.IsVisualAncestorOf(source)))
            return;
        Finish(mBox.Text);
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Finish(null);
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            Finish(mBox.Text);
        }
        // Shift+Enter（多行）交给文本框自己插入换行
    }

    void Finish(string? result)
    {
        if (mDone)
            return;
        mDone = true;
        mFinish(result);
        mWindow?.RemoveHandler(InputElement.PointerPressedEvent, OnWindowPointerPressed);
        mWindow = null;
        mFrame.IsVisible = false;
        // 摘下推迟一拍：可能正处在它自己的事件里（失焦 / 按键）。
        Dispatcher.UIThread.Post(() => mCanvas.DetachInline(mFrame));
    }

    readonly PluginCanvas mCanvas;
    readonly Action<string?> mFinish;
    readonly TextInput mBox;
    readonly Border mFrame;
    readonly Rect mRect;
    TopLevel? mWindow;
    bool mDone;
}
