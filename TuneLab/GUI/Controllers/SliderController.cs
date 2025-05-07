using Avalonia.Controls;
using TuneLab.Foundation.Event;
using TuneLab.GUI.Components;
using TuneLab.Utils;
using Slider = TuneLab.GUI.Components.Slider;

namespace TuneLab.GUI.Controllers;

internal class SliderController : DockPanel, IDataValueController<double>, IDataValueController<int>
{
    public IActionEvent ValueWillChange => mSlider.ValueWillChange;
    public IActionEvent ValueChanged => mSlider.ValueChanged;
    public IActionEvent ValueCommited => mSlider.ValueCommited;
    public double Value => mSlider.Value;
    public bool IsInterger { get => mSlider.IsInterger; set => mSlider.IsInterger = value; }
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
        mSlider.Display(value);
        RefreshLabel();
    }

    public void DisplayNull()
    {
        Display(double.NaN);
    }

    public void DisplayMultiple()
    {
        Display(double.NaN);
    }

    protected string GetValueString()
    {
        return double.IsNaN(mSlider.Value) ? "-" : mSlider.IsInterger ? ((int)mSlider.Value).ToString() : mSlider.Value.ToString("F2");
    }

    void RefreshLabel()
    {
        mEditableLabel.Text = GetValueString();
    }

    public void Display(int value)
    {
        mSlider.Display(value);
        RefreshLabel();
    }

    EditableLabel mEditableLabel;
    Slider mSlider = new() { Margin = new(0, 0, 24, 0) };
}
