using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.GUI.Controllers;
using TuneLab.Utils;
using TuneLab.SDK;
using TuneLab.I18N;

namespace TuneLab.UI;

internal class MidiPartFixedController : StackPanel
{
    public MidiPartFixedController()
    {
        Background = Style.INTERFACE.ToBrush();
        Orientation = Avalonia.Layout.Orientation.Vertical;

        AddController(mGainController, "Gain".Tr(TC.Property));
        mGainController.SetRange(-24, +24);
        mGainController.SetDefaultValue(0);
    }

    // 绑定到目标 part 集（单/多/空统一走 MultipleDataProperty：空→滑块 Invalid、单→等价单绑、多→三态合并 + 写扇出）。
    public void SetParts(IReadOnlyList<IMidiPart> parts)
    {
        s.DisposeAll();
        var gain = new MultipleDataProperty<double>(parts.Select(part => part.Gain).ToList(), 0.0, value => PropertyValue.Create(value));
        mGainController.BindDataProperty(gain, s);
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

    readonly SliderController mGainController = new() { Margin = new(24, 12) };
    readonly DisposableManager s = new();
}
