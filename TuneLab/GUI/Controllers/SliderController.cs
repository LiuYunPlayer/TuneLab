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
    public bool IsInteger { get => mSlider.IsInteger; set { mSlider.IsInteger = value; RefreshLabelWidth(); } }
    public bool ShowRandomButton { get => mRandomButton.IsVisible; set => mRandomButton.IsVisible = value; }
    int IValueController<int>.Value => mSlider.IntergerValue;

    public SliderController()
    {
        Background = Style.INTERFACE.ToBrush();

        // 量程内随机取值的按钮（默认隐藏，由 ShowRandomButton 开启）：放在最右侧，数值标签在其左。
        mRandomButton = new Button() { Width = 24, Height = 24, Margin = new(4, 0, 0, 0), IsVisible = false };
        mRandomButton
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { HoveredColor = Style.LIGHT_WHITE.Opacity(0.1), PressedColor = Style.LIGHT_WHITE.Opacity(0.16) } })
            .AddContent(new() { Item = new IconItem() { Icon = Assets.Dice, Scale = 0.7 }, ColorSet = new() { Color = Style.LIGHT_WHITE } });
        mRandomButton.Clicked += Randomize;
        mRandomButton.SetupToolTip("Randomize".Tr(this));
        this.AddDock(mRandomButton, Dock.Right);

        // 宽度按量程最大桁数固定（见 RefreshLabelWidth）：拖动中桁数变化不改宽，避免连带 slider 长度
        // 抖动、thumb 在光标下位移。MinWidth 仅作 SetRange 前的初始保底。
        mEditableLabel = new() { Height = 24, MinWidth = 48, Padding = new(8, 0), FontFamily = Assets.NotoMono, FontSize = 12, CornerRadius = new(4), HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.BACK.ToBrush() };
        this.AddDock(mEditableLabel, Dock.Right);

        this.AddDock(mSlider);

        mEditableLabel.EndInput.Subscribe(() =>
        {
            var text = mEditableLabel.Text;
            if (double.TryParse(text, out var result))
            {
                mSlider.Value = result;
            }
            RefreshLabel();
        });
        mSlider.ValueChanged.Subscribe(RefreshLabel);
        RefreshLabel();
    }

    // double 仅能精确表示 [-2^53, 2^53] 内的整数；属性系统底层用 double 存值，
    // 超出此范围的整数会被量化、无法原样回存（种子复现会失真）。随机取值时把整数量程
    // 钳在此范围，保证抽到的值一定能精确回存/回显（不抽一个会被悄悄破坏的大数）。
    const double MaxSafeInteger = 9007199254740992; // 2^53

    // 在 [min, max] 内重新随机取值并提交（走 Value setter，记录撤销）。
    void Randomize()
    {
        var min = mSlider.MinValue;
        var max = mSlider.MaxValue;
        if (max <= min)
        {
            mSlider.Value = min;
            return;
        }

        double value;
        if (mSlider.IsInteger)
        {
            // 钳到 double 整数精确区间后再抽，hi 必 ≤ 2^53，hi+1 不会溢出 long。
            long lo = (long)Math.Ceiling(Math.Max(min, -MaxSafeInteger));
            long hi = (long)Math.Floor(Math.Min(max, MaxSafeInteger));
            value = hi <= lo ? lo : Random.Shared.NextInt64(lo, hi + 1); // 含上界
        }
        else
        {
            value = min + Random.Shared.NextDouble() * (max - min);
        }

        mSlider.Value = value;
    }

    public void SetRange(double min, double max)
    {
        mSlider.SetRange(min, max);
        RefreshLabelWidth();
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

    // 整数用 long 而非 int 格式化：随机种子等大数可超 int 范围，(int) 会截断显错值。
    string FormatValue(double value) => mSlider.IsInteger ? ((long)value).ToString() : value.ToString("F2");

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
    }

    // 量程两端格式化串里取最宽者，加内边距后固定数字框宽度——保证任意桁数都能放下且拖动中宽度不变。
    void RefreshLabelWidth()
    {
        double textWidth = Math.Max(MeasureLabelText(FormatValue(mSlider.MinValue)),
                                    MeasureLabelText(FormatValue(mSlider.MaxValue)));
        mEditableLabel.Width = Math.Max(48, Math.Ceiling(textWidth) + LabelHorizontalPadding);
    }

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
    Slider mSlider = new() { Margin = new(0, 0, 24, 0) };
}
