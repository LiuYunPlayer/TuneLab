using Avalonia.Controls;
using Avalonia.Layout;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.GUI.Components;
using TuneLab.Utils;
using Slider = TuneLab.GUI.Components.Slider;

namespace TuneLab.GUI.Controllers;

internal class SliderController : DockPanel, IDataValueController<double>, IDataValueController<int>
{
    public IActionEvent ValueWillChange => mSlider.ValueWillChange;
    public IActionEvent ValueChanged => mSlider.ValueChanged;
    public IActionEvent ValueCommitted => mSlider.ValueCommitted;
    public double Value => mSlider.Value;
    public bool IsInteger { get => mSlider.IsInteger; set => mSlider.IsInteger = value; }
    int IValueController<int>.Value => mSlider.IntergerValue;

    public SliderController()
    {
        Background = Style.INTERFACE.ToBrush();

        mEditableLabel = new() { Height = 24, Width = 48, Padding = new(0), FontFamily = Assets.NotoMono, FontSize = 12, CornerRadius = new(4), HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.BACK.ToBrush() };
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

    public void SetRange(double min, double max)
    {
        mSlider.SetRange(min, max);
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

    // 两态均空轨（thumb 随 NaN 隐藏），仅靠标签区分：Multiple 显 "-"、Invalid 留空。
    // 拖动中 slider 取到真实值（非 NaN）即照常显数，不受残留状态标志干扰。
    protected string GetValueString()
    {
        if (!double.IsNaN(mSlider.Value))
            return mSlider.IsInteger ? ((int)mSlider.Value).ToString() : mSlider.Value.ToString("F2");

        return mState == State.Multiple ? "-" : "";
    }

    enum State { Value, Multiple, Invalid }
    State mState = State.Value;

    void RefreshLabel()
    {
        mEditableLabel.Text = GetValueString();
    }

    public void Display(int value)
    {
        mState = State.Value;
        mSlider.Display(value);
        RefreshLabel();
    }

    EditableLabel mEditableLabel;
    Slider mSlider = new() { Margin = new(0, 0, 24, 0) };
}
