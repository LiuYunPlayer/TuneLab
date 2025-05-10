using Avalonia.Controls;
using System.Linq;
using TuneLab.Data;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.Utils;
using TuneLab.GUI;
using TuneLab.GUI.Controllers;
using TuneLab.I18N;
using TuneLab.Utils;

namespace TuneLab.UI;

internal class MidiPartFixedController : StackPanel
{
    public IMidiPart? Part { get => mPart.Object; set => mPart.Set(value); }
    public MidiPartFixedController()
    {
        Background = Style.INTERFACE.ToBrush();
        Orientation = Avalonia.Layout.Orientation.Vertical;

        AddController(mGainController, "Gain".Tr(TC.Property));
        mGainController.SetRange(-24, +24);
        mGainController.SetDefaultValue(0);
        mGainController.BindDataProperty(mPart.Select(part => part.Gain), s);
    }

    void AddController(Control control, string name)
    {
        Children.Add(new Avalonia.Controls.Label()
        {
            Height = 30,
            FontSize = 12,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
            Foreground = Style.LIGHT_WHITE.ToBrush(),
            Content = name,
            Padding = new(24, 0)
        });
        Children.Add(control);
        Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
    }

    readonly Owner<IMidiPart> mPart = new();

    readonly SliderController mGainController = new() { Margin = new(24, 12) };
    readonly DisposableManager s = new();
}
