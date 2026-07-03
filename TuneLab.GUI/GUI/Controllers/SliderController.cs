using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.Utils;
using Slider = TuneLab.GUI.Components.Slider;
using Button = TuneLab.GUI.Components.Button;

namespace TuneLab.GUI.Controllers;

internal class SliderController : DockPanel, IDataValueController<double>, IDataValueController<int>
{
    public IActionEvent ValueWillChange => mSlider.ValueWillChange;
    public IActionEvent ValueChanged => mSlider.ValueChanged;
    public IActionEvent ValueCommitted => mSlider.ValueCommitted;
    public double Value => mSlider.Value;
    // 非 SDK 便利路（设置面板等）：整数态同时把显示切到 0 位小数（沿用重构前 IsInteger 决定格式的行为）。
    public bool IsInteger { get => mSlider.IsInteger; set { mSlider.IsInteger = value; mNumberFormat = TuneLab.SDK.NumberFormat.Decimals(value ? 0 : 2); RefreshLabel(); RefreshLabelWidth(); } }
    public bool ShowRandomButton { get => mRandomButton.IsVisible; set => mRandomButton.IsVisible = value; }

    // SDK 滑条经此注入标度（线性/整数/自定义）。
    public void SetScale(INormalizedScale scale) { mSlider.Scale = scale; RefreshLabelWidth(); }

    // 数值显示/回读格式；置 null 回退默认（定宽 2 位小数）。务必用定宽格式——RefreshLabelWidth 只量 min/max
    // 两端定框宽，假设最宽文本在端点；变宽格式（如裁尾随零）会让中段多位小数溢出框。
    public INumberFormat? NumberFormat
    {
        get => mNumberFormat;
        set { mNumberFormat = value ?? TuneLab.SDK.NumberFormat.Decimals(2); RefreshLabel(); RefreshLabelWidth(); }
    }
    INumberFormat mNumberFormat = TuneLab.SDK.NumberFormat.Decimals(2);
    int IValueController<int>.Value => mSlider.IntergerValue;

    public SliderController()
    {
        Background = Style.INTERFACE.ToBrush();

        // 量程内随机取值的按钮（默认隐藏，由 ShowRandomButton 开启）：放在最右侧，数值标签在其左。
        mRandomButton = new Button() { Width = 24, Height = 24, Margin = new(4, 0, 0, 0), IsVisible = false, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        mRandomButton
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { HoveredColor = Style.LIGHT_WHITE.Opacity(0.1), PressedColor = Style.LIGHT_WHITE.Opacity(0.16) } })
            .AddContent(new() { Item = new IconItem() { Icon = Assets.Dice, Scale = 0.7 }, ColorSet = new() { Color = Style.LIGHT_WHITE } });
        mRandomButton.Clicked += Randomize;
        mRandomButton.SetupToolTip("Randomize".Tr(this));
        this.AddDock(mRandomButton, Dock.Right);

        // 宽度按量程最大桁数固定（见 RefreshLabelWidth）：拖动中桁数变化不改宽，避免连带 slider 长度
        // 抖动、thumb 在光标下位移。MinWidth 仅作 SetRange 前的初始保底。
        // 顶部对齐（Height 24 = 轨道行高）：端点描述行出现时控件整体变高，值框仍与轨道同排、不下坠。
        mEditableLabel = new() { Height = 24, MinWidth = 48, Padding = new(8, 0), FontFamily = Assets.SegoeUI, FontSize = 12, CornerRadius = new(4), HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.BACK.ToBrush() };
        this.AddDock(mEditableLabel, Dock.Right);

        // 量程两端描述文本（config 声明，如 min="Soft"、max="Hard"）：置于轨道正下方一行（同高度内联会挤占轨道长度），
        // min 左对齐轨道起点、max 右对齐轨道终点（行与轨道同宽、共享右缩进）；未声明整行折叠不占位（见 SetBoundLabels）。
        // 淡色小字与数值框区分——它是量纲语义、不是当前值。
        mMinBoundLabel = new() { FontSize = 11, IsVisible = false, Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush() };
        mMaxBoundLabel = new() { FontSize = 11, IsVisible = false, Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush() };
        // 顶部留 4px：与轨道行（thumb 下缘）拉开呼吸距离。
        mBoundLabelRow = new DockPanel() { Margin = new(0, 4, 24, 0), IsVisible = false, LastChildFill = false };
        mBoundLabelRow.AddDock(mMinBoundLabel, Dock.Left);
        mBoundLabelRow.AddDock(mMaxBoundLabel, Dock.Right);

        // 轨道列（轨道 + 其下描述行）占满剩余宽度；轨道行定高与右侧值框同高。
        mSlider.Height = 24;
        var sliderColumn = new StackPanel() { Orientation = Orientation.Vertical };
        sliderColumn.Children.Add(mSlider);
        sliderColumn.Children.Add(mBoundLabelRow);
        this.AddDock(sliderColumn);

        mEditableLabel.EndInput.Subscribe(() =>
        {
            var text = mEditableLabel.Text;
            if (mNumberFormat.Parse(text) is double result)
            {
                mSlider.Value = result;
            }
            RefreshLabel();
        });
        mSlider.ValueChanged.Subscribe(RefreshLabel);
        RefreshLabel();
    }

    // 在标度上按归一化均匀重取值并提交（走 Value setter，记录撤销）。
    // 抽 [0,1) 再经标度回值：对数轴按对数均匀、整数轴落整数，统一一行。
    void Randomize()
    {
        mSlider.Value = mSlider.Scale.ToValue(Random.Shared.NextDouble());
    }

    public void SetRange(double min, double max)
    {
        mSlider.SetRange(min, max);
        RefreshLabelWidth();
    }

    // 量程两端描述文本（null/空 = 该端不显示；两端皆空整行折叠不占位）。池化复用方每次 Apply 都要调（含清空路径）。
    public void SetBoundLabels(string? minLabel, string? maxLabel)
    {
        mMinBoundLabel.Text = minLabel;
        mMinBoundLabel.IsVisible = !string.IsNullOrEmpty(minLabel);
        mMaxBoundLabel.Text = maxLabel;
        mMaxBoundLabel.IsVisible = !string.IsNullOrEmpty(maxLabel);
        mBoundLabelRow.IsVisible = mMinBoundLabel.IsVisible || mMaxBoundLabel.IsVisible;
    }

    public void SetDefaultValue(double value)
    {
        mSlider.DefaultValue = value;
    }

    public void Display(double value)
    {
        mState = State.Value;
        mSlider.Display(value);
        RefreshLabel();
    }

    public void DisplayNull()
    {
        mState = State.Invalid;
        mSlider.Display(double.NaN);
        RefreshLabel();
    }

    public void DisplayMultiple()
    {
        mState = State.Multiple;
        mSlider.Display(double.NaN);
        RefreshLabel();
    }

    string FormatValue(double value) => mNumberFormat.Format(value);

    // 两态均空轨（thumb 随 NaN 隐藏），仅靠标签区分：Multiple 显 "-"、Invalid 留空。
    // 拖动中 slider 取到真实值（非 NaN）即照常显数，不受残留状态标志干扰。
    protected string GetValueString()
    {
        if (!double.IsNaN(mSlider.Value))
            return FormatValue(mSlider.Value);

        return mState == State.Multiple ? "-" : "";
    }

    enum State { Value, Multiple, Invalid }
    State mState = State.Value;

    void RefreshLabel()
    {
        mEditableLabel.Text = GetValueString();

        // 兜底：静态四样本估不到的宽度（如 Custom 格式在区间内部的宽度峰值）若真超框，临时扩张到放得下、绝不裁字。
        // 不持久也不回缩——面板常重建，下次 RefreshLabelWidth / 控件重建即回落到估计值，维护回缩无意义。
        double needed = Math.Ceiling(MeasureLabelText(mEditableLabel.Text) + LabelHorizontalPadding);
        if (needed > mEditableLabel.Width)
            mEditableLabel.Width = needed;
    }

    // 估"最宽显示文本"定框宽：min/max 端点取最大整数位与符号，再各向内扰动一个多位小数（min+ε / max−ε）逼出
    // 变宽格式（Custom）的小数宽度——四样本取最宽。这只是静态估计，渲染时真超了由 RefreshLabel 临时扩张兜底。
    void RefreshLabelWidth()
    {
        double min = mSlider.MinValue;
        double max = mSlider.MaxValue;
        double textWidth = Math.Max(
            Math.Max(MeasureLabelText(FormatValue(min)), MeasureLabelText(FormatValue(min + WidthProbeOffset))),
            Math.Max(MeasureLabelText(FormatValue(max)), MeasureLabelText(FormatValue(max - WidthProbeOffset))));
        mEditableLabel.Width = Math.Max(48, Math.Ceiling(textWidth) + LabelHorizontalPadding);
    }

    // 向内扰动量：一个多位小数，逼变宽格式吐出小数宽度；够小不致跨整数位、对定宽格式无影响（照样按其位数渲染）。
    const double WidthProbeOffset = 0.1234567890123;

    double MeasureLabelText(string text)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(mEditableLabel.FontFamily), mEditableLabel.FontSize, Brushes.White);
        return formatted.Width;
    }

    // 数字框左右内边距(8+8) + 编辑态光标余白。
    const double LabelHorizontalPadding = 20;

    public void Display(int value)
    {
        mState = State.Value;
        mSlider.Display(value);
        RefreshLabel();
    }

    EditableLabel mEditableLabel;
    Button mRandomButton;
    readonly TextBlock mMinBoundLabel;
    readonly TextBlock mMaxBoundLabel;
    readonly DockPanel mBoundLabelRow;
    Slider mSlider = new() { Margin = new(0, 0, 24, 0) };
}
