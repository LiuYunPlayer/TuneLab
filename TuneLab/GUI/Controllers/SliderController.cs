using Avalonia.Controls;
using Avalonia.Layout;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.Base.Properties;
using TuneLab.GUI.Components;
using TuneLab.Utils;
using Slider = TuneLab.GUI.Components.Slider;

namespace TuneLab.GUI.Controllers;

internal class SliderController : DockPanel, IValueController<double>
{
    public IActionEvent ValueWillChange => mSlider.ValueWillChange;
    public IActionEvent ValueChanged => mSlider.ValueChanged;
    public IActionEvent ValueCommited => mSlider.ValueCommited;
    public double Value => mSlider.Value;
    public bool IsInterger { get => mSlider.IsInterger; set => mSlider.IsInterger = value; }
    public SliderController()
    {
        Background = Style.INTERFACE.ToBrush();

        mEditableLabel = new() { Height = 24, Width = 48, Margin = new(0, 12, 24, 12), Padding = new(0), FontFamily = Assets.NotoMono, FontSize = 12, CornerRadius = new(4), HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.BACK.ToBrush() };
        Children.Add(mEditableLabel);
        DockPanel.SetDock(mEditableLabel, Dock.Right);

        Children.Add(mSlider);

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

    public void Display(object? value)
    {
        Display(value is double d ? d : double.NaN);
    }

    public void Display(double value)
    {
        mSlider.Display(value);
        RefreshLabel();
    }

    protected string GetValueString()
    {
        return double.IsNaN(mSlider.Value) ? "-" : mSlider.IsInterger ? ((int)mSlider.Value).ToString() : mSlider.Value.ToString("F2");
    }

    void RefreshLabel()
    {
        mEditableLabel.Text = GetValueString();
    }

    EditableLabel mEditableLabel;
    Slider mSlider = new() { Margin = new(24, 12, 24, 12) };
}
