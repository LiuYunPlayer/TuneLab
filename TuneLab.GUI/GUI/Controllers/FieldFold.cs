using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using System;
using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.Utils;
using IDataObject = TuneLab.Foundation.IDataObject;

namespace TuneLab.GUI.Controllers;

// 字段折叠状态的持久化入口（用户习惯：收起了哪些字段）。GUI 层只认这个接口，存哪儿由宿主决定并在启动时装上；
// 没装（如测试 / 独立使用）时折叠照常可用、只是不记。键由调用方拼好（作用域 + 字段路径，见 PropertyObjectController.CollapseScope）。
internal interface IFieldFoldStore
{
    bool IsCollapsed(string key);
    void SetCollapsed(string key, bool collapsed);
}

internal static class FieldFoldStore
{
    public static IFieldFoldStore? Current { get; set; }
}

// 小三角：收起朝右、展开朝下。字段 / 对象的三角放在标题左侧的缩进留白里（不挤动标签文字），
// 分组标题的放在右侧（见 SideBarSection）——两种位置本身就是层级区分。
internal sealed class Chevron : Path
{
    public Chevron(double size, Color color)
    {
        Data = Geometry.Parse("M0,0 L4,4 L0,8 Z");
        Fill = color.ToBrush();
        // 同色描边 + 圆角转角：三个尖角磨圆，小尺寸下也不扎眼。描边越粗转角越圆，但它画在缩放后的三角外沿上、会把
        // 整体往外撑——外框按描边粗细收回去，使看到的大小只由 size 定。按 Uniform 缩放保持三角比例（Fill 会把它拉成外框
        // 的比例，外框宽高各减同样的描边量后比例就不是 1:2 了，三角被拉瘦）。
        const double stroke = 2;
        Stroke = Fill;
        StrokeThickness = stroke;
        StrokeJoin = PenLineJoin.Round;
        Stretch = Stretch.Uniform;
        Width = size * 0.4 + 1 - stroke;
        Height = size * 0.8 + 1 - stroke;
        mVisualWidth = Width + stroke;
        RenderTransformOrigin = RelativePoint.Center;
        IsHitTestVisible = false;
    }

    // 左对齐摆放时让中心落在 x 处的左边距（三角绕中心旋转，展开 / 收起中心不动）。
    public double LeftForCenter(double x) => x - mVisualWidth / 2 + StrokeThickness / 2;

    readonly double mVisualWidth;

    public bool IsExpanded
    {
        get => mExpanded;
        set
        {
            mExpanded = value;
            RenderTransform = value ? new RotateTransform(90) : null;
        }
    }

    bool mExpanded;
}

// 一个可折叠字段：标题行（左侧留白里的三角 + 标签 + 收起时右侧的值摘要）+ 主体（收起时隐藏）。
// 收起的意义是让用户把不常用的字段常年收着，故普通字段收起后不是「藏起来」，而是压成一行、把当前值摘要显示在标题行右侧。
// persistKey 非 null 时折叠状态按它记进 FieldFoldStore（按插件 + 字段路径，跨会话保留）。foldable 缺省 = 有 persistKey 才可折叠
//（普通字段：记不住就不给折叠，标题行与从前一样）；对象显式传 true——没有记忆作用域时也能临时折叠、只是不记。
internal sealed class FieldFold : IDisposable
{
    public Control Header => mHeader;

    // title：调用方建好的标题 Label（字号 / 颜色 / 高度由调用方定）。body：收起时隐藏的那些控件。
    // summary：收起时标题行右侧显示的文字（null = 不显示摘要，如对象）。watch：值变化时刷新摘要的数据源。
    public FieldFold(Label title, IReadOnlyList<Control> body, string? persistKey, Func<string?>? summary = null, IDataObject? watch = null, bool? foldable = null)
    {
        mBody = body;
        mPersistKey = persistKey;
        mSummary = summary;

        mHeader = new Panel { Background = Brushes.Transparent };
        var row = new DockPanel { LastChildFill = true };
        mSummaryText = new TextBlock
        {
            FontSize = 12,
            Foreground = Style.LIGHT_WHITE.ToBrush(),   // 值的亮度：标签暗、值亮
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,   // 与标签同一条中线（标题行文字一律居中）
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 160,
            Margin = new(8, 0, 24, 0),
            IsVisible = false,
        };
        DockPanel.SetDock(mSummaryText, Dock.Right);
        row.Children.Add(mSummaryText);
        row.Children.Add(title);
        mHeader.Children.Add(row);

        if (!(foldable ?? persistKey != null))
            return;

        // 三角落在标签左侧 24px 留白的中段（中心在 GuideX，与分组三角同一条竖线），与标签文字同一条中线。
        // 比分组标题的三角小一档（字段层级低一级）。
        mChevron = new Chevron(FieldChevronSize, Style.TEXT_LABEL)
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        mChevron.Margin = new(mChevron.LeftForCenter(GuideX), 0, 0, 0);
        mHeader.Children.Add(mChevron);
        mHeader.PointerPressed += OnHeaderPressed;
        if (watch != null)
            watch.Modified.Subscribe(RefreshSummary, s);

        SetCollapsed(persistKey != null && (FieldFoldStore.Current?.IsCollapsed(persistKey) ?? false), persist: false);
    }

    // 三角中心的 x（字段与分组标题同一条竖线）：对象的引导线据此对齐到三角正下方（见 ObjectCreator）。
    public const double GuideX = 14;
    // 字段 / 对象三角的尺寸（分组标题用 8，见 SideBarSection）。
    public const double FieldChevronSize = 6;
    // 末项（收起或行内字段）的底边距：标题行 30、文字居中，文字下方本有约 7 的留白，再补 7 凑成 14——
    // 与「分组标题线 → 第一项文字」的 14 对称（那一侧由分组内容区的顶部留白补足，见侧栏的 SideBarSection.CreateInset）。
    const double CollapsedBottomGap = 7;

    public bool IsCollapsed { get; private set; }

    // 行内控件（勾选框 / 数值框这类单行字段）：直接放在标题行右侧、与文字同一条中线，收起时同一位置换成值摘要、不可改
    //（收起即只读，这也是收起的一层含义）。行内控件须同时出现在 body 里（收起时由 FieldFold 隐藏）。
    // 单行字段展开、收起都是一行标准行高，与其余收起的字段等高。
    public void AddInline(Control control)
    {
        control.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        control.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        control.Margin = new(0, 0, 24, 0);
        mHeader.Children.Add(control);
        mInlines.Add(control);
        mInline = true;
        RefreshTrailingGap();
    }

    // 是否所在列表的最后一项（由容器在排好顺序后设置，见 PropertyObjectController.Reconcile）。
    public bool IsTrailing
    {
        get => mTrailing;
        set { mTrailing = value; RefreshTrailingGap(); }
    }

    // 展开时字段下方的留白来自主体控件自身的外边距；收起后主体隐藏，留白跟着没了。连续几项都收起时不需要补——
    // 下一项的标题行本身上方就有那段留白；只有末项收起时，后面紧跟的是分组线 / 对象结尾，才由标题行补上底边距。
    // 行内字段展开时也只有标题这一行（控件就在行内），同收起时一样需要补。
    void RefreshTrailingGap() => mHeader.Margin = (IsCollapsed || mInline) && mTrailing ? new(0, 0, 0, CollapsedBottomGap) : default;

    void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(mHeader).Properties.IsLeftButtonPressed)
            return;
        // 行内控件（勾选框 / 数值框）就在标题行里：在它上面按下是在操作控件，冒泡上来的按下不算点标题。
        if (e.Source is Visual source && mInlines.Exists(inline => inline == source || inline.IsVisualAncestorOf(source)))
            return;
        e.Handled = true;
        SetCollapsed(!IsCollapsed, persist: true);
    }

    void SetCollapsed(bool collapsed, bool persist)
    {
        IsCollapsed = collapsed;
        foreach (var control in mBody)
            control.IsVisible = !collapsed;
        RefreshTrailingGap();
        if (mChevron != null)
            mChevron.IsExpanded = !collapsed;
        RefreshSummary();
        if (persist && mPersistKey != null)
            FieldFoldStore.Current?.SetCollapsed(mPersistKey, collapsed);
    }

    public void RefreshSummary()
    {
        var text = IsCollapsed ? mSummary?.Invoke() : null;
        mSummaryText.Text = text;
        mSummaryText.IsVisible = !string.IsNullOrEmpty(text);
    }

    public void Dispose()
    {
        s.DisposeAll();
        mHeader.PointerPressed -= OnHeaderPressed;
        foreach (var control in mBody)
            control.IsVisible = true;   // 主体控件多出自对象池：归还前恢复可见，别让下一任主人接手一个隐藏的控件
        // 标题 Label 也出自对象池：从行里摘下来，免得归还后仍挂在本行的可视树上。
        foreach (var child in mHeader.Children)
            if (child is DockPanel row)
                row.Children.Clear();
        // 行内控件同出自对象池：摘下并还原对齐 / 边距（下一任主人可能按块布局使用它）。
        foreach (var child in mHeader.Children)
        {
            child.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            child.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        }
        mHeader.Children.Clear();
    }

    readonly Panel mHeader;
    bool mTrailing;
    bool mInline;
    readonly List<Control> mInlines = [];
    readonly TextBlock mSummaryText;
    readonly Chevron? mChevron;
    readonly IReadOnlyList<Control> mBody;
    readonly string? mPersistKey;
    readonly Func<string?>? mSummary;
    readonly DisposableManager s = new();
}
