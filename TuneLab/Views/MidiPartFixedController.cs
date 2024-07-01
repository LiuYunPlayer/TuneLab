using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Base.Event;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.GUI.Controllers;
using TuneLab.Utils;
using TuneLab.Base.Utils;
using TuneLab.Base.Properties;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Base.Structures;
using TuneLab.I18N;

namespace TuneLab.Views;

internal class MidiPartFixedController : StackPanel
{
    public IMidiPart? Part { get => mPart.Object; set => mPart.Set(value); }
    public MidiPartFixedController()
    {
        Background = Style.INTERFACE.ToBrush();
        Orientation = Avalonia.Layout.Orientation.Vertical;

        AddController(mGainController, "Gain".Tr(TC.Property));
        mGainController.SetRange(-12, +12);
        mGainController.SetDefaultValue(0);
        mGainController.Bind(mPart.Select(part => part.Gain), s);

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

    readonly SliderController mGainController = new();
    readonly DisposableManager s = new();
}
