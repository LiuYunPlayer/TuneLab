using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1Voice;

// 叠到钢琴窗上的时间轴画布演示：在每个音符正下方显示它的语言（一个小标签），点标签弹菜单换语言。
// 值 = 语言代码字符串（"zh" / "ja" / "en"）；没设过 = Null，显示为 "auto"（按声库默认）。
// 覆盖：
// · 音高轴（ITimelineCanvasHost.PitchAxis）：插件在声明时拿到音符的音高，PitchToY(pitch) = 这个音符那一行的下沿，标签就画在它下面。
//   叠到钢琴窗上时这正好贴着音符；在参数面板里纵轴是"面板背后那块音符区"，标签多半落在面板外，看不到是正常的。
// · 只在画了东西处接按下：块铺满音符区，但只有标签那一小块接左右键，其余（空白处框选、点音符拖动……）落到下面的工具上。
// · 悬停：指针在标签上时提亮。
sealed class NoteLanguageCanvas(ITimelineCanvasHost host, PropertyValue value, int pitch) : ICanvasControl
{
    public static TimelineCanvasConfig Config(PropertyValue value, int pitch)
        => TimelineCanvasConfig.Create(host => new NoteLanguageCanvas(host, value, pitch)).WithSummary(Read(value) ?? "auto");

    static readonly string[] Languages = ["zh", "ja", "en"];
    static readonly CanvasTextStyle TextStyle = CanvasTextStyle.Create().WithFontSize(11).WithAlignment(CanvasAlignment.Center);
    static readonly CanvasBrush Fill = CanvasBrush.Solid(Color.FromArgb(200, 40, 44, 52));
    static readonly CanvasBrush HotFill = CanvasBrush.Solid(Color.FromArgb(230, 70, 76, 90));
    static readonly CanvasPen Border = CanvasPen.Create(Color.FromArgb(120, 255, 255, 255));
    static readonly CanvasBrush SetText = CanvasBrush.Solid(Color.FromRgb(255, 255, 255));
    static readonly CanvasBrush AutoText = CanvasBrush.Solid(Color.FromArgb(140, 255, 255, 255));

    const double PadX = 5, Height = 16, Gap = 2, Left = 2;

    static string? Read(PropertyValue value) => value.ToString(out var text) && text.Length > 0 ? text : null;

    PropertyValue Current => mDraft ?? value;

    // 标签的位置：音符那一行的下沿往下一点，靠块的左缘。
    Rect LabelBounds(ICanvasDrawing? d = null)
    {
        string text = Read(Current) ?? "auto";
        double width = (d?.MeasureText(text, TextStyle).Width ?? text.Length * 7) + PadX * 2;
        double top = host.PitchAxis.PitchToY(pitch) + Gap;
        return new Rect(Left, top, Math.Min(width, Math.Max(0, host.Width - Left)), Height);
    }

    Rect mLabel;

    public void Render(ICanvasDrawing d)
    {
        mLabel = LabelBounds(d);
        if (mLabel.Width <= 0)
            return;
        string? language = Read(Current);
        d.FillRectangle(mLabel, mHot ? HotFill : Fill, 3);
        d.StrokeRectangle(mLabel, Border, 3);
        d.DrawText(language ?? "auto", mLabel, language != null ? SetText : AutoText, TextStyle);
    }

    public void OnMouseDown(CanvasMouseButtonEvent e)
    {
        if (!mLabel.Contains(e.Position) || e.Button is not (CanvasMouseButton.Primary or CanvasMouseButton.Secondary))
            return;   // 标签外不接：交给下面的层 / 钢琴窗的工具
        e.Handled = true;
        ShowMenu();
    }

    public void OnMouseMove(CanvasMouseEvent e)
    {
        bool hot = mLabel.Contains(e.Position);
        if (hot != mHot)
        {
            mHot = hot;
            host.Invalidate();
        }
        e.Handled = hot;   // 悬停在标签上算本画布的（指针形状不跟工具走）
    }

    public void OnMouseLeave(CanvasMouseEvent e)
    {
        if (mHot)
        {
            mHot = false;
            host.Invalidate();
        }
    }

    async void ShowMenu()
    {
        string? current = Read(Current);
        var items = new List<ComboBoxItem>();
        foreach (var language in Languages)
            items.Add(new ComboBoxItem(PropertyValue.Create(language), language + (language == current ? "  (current)" : "")));
        items.Add(ComboBoxItem.Separator());
        items.Add(new ComboBoxItem(PropertyValue.Create(""), "auto" + (current == null ? "  (current)" : "")));

        var picked = await host.PopupMenu(new Rect(mLabel.Left, mLabel.Bottom, mLabel.Width, 0), items);
        if (picked is not { } choice || !choice.ToString(out var code))
            return;
        var next = code.Length > 0 ? PropertyValue.Create(code) : PropertyValue.Null;
        if (host.SetValue(next))
        {
            mDraft = next;
            host.CommitValue();
        }
    }

    PropertyValue? mDraft;
    bool mHot;
}
