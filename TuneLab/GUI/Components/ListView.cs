using Avalonia.Controls;
using Avalonia.Layout;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.GUI.Components;

internal class ListView : ScrollView
{
    public new StackPanel Content => mContent;
    public Orientation Orientation
    {
        get => mContent.Orientation;
        set
        {
            mContent.Orientation = value;
            FitWidth = value == Orientation.Vertical;
            FitHeight = value == Orientation.Horizontal;
        }
    }

    public ListView()
    {
        Orientation = Orientation.Vertical;
        base.Content = mContent;
    }

    readonly StackPanel mContent = new();
}
