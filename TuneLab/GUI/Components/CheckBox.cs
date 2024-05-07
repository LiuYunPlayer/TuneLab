using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.GUI;
using TuneLab.GUI.Components;

namespace TuneLab.GUI.Components;

internal class CheckBox : Toggle
{
    public CheckBox()
    {
        Width = 16;
        Height = 16;
        AddContent(new() { Item = new IconItem() { Icon = Assets.CheckBoxFrame }, UncheckedColorSet = new() { Color = Colors.White } });
        AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, CheckedColorSet = new() { Color = Style.HIGH_LIGHT } });
        AddContent(new() { Item = new IconItem() { Icon = Assets.Check }, CheckedColorSet = new() { Color = Colors.White } });
    }
}
