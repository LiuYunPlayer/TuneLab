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

namespace TuneLab.Views;

internal class MidiPartFixedController : StackPanel
{
    public MidiPart? Part { get => mPart; set => mPart.Set(value); }
    public MidiPartFixedController()
    {
        Background = Style.INTERFACE.ToBrush();
        Orientation = Avalonia.Layout.Orientation.Vertical;

        AddController(mGainController, "Gain");

        mGainController.SetRange(-12, +12);
        mGainController.SetDefaultValue(0);
        mGainController.ValueWillChange.Subscribe(() =>
        {
            if (Part == null)
                return;

            mHead = Part.Head;
        });

        mGainController.ValueChanged.Subscribe(() =>
        {
            if (Part == null)
                return;

            double value = mGainController.Value;
            Part.Gain.DiscardTo(mHead);
            Part.Gain.Set(value);
        });

        mGainController.ValueCommited.Subscribe(() =>
        {
            if (Part == null)
                return;

            Part.Gain.Commit();
        });

        mPart.When(part => part.Gain.Modified).Subscribe(() => { if (Part == null) return;  mGainController.Display(Part.Gain); }, s);
        mPart.ObjectChanged.Subscribe(() =>
        {
            if (Part == null)
            {
                mGainController.Display(double.NaN);
            }
            else
            {
                mGainController.Display(Part.Gain);
            }
        });
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

    readonly Owner<MidiPart> mPart = new();

    readonly SliderController mGainController = new();
    Head mHead;
    readonly DisposableManager s = new();
}
