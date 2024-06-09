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
    public Color Background { get => mBackContent.CheckedColorSet.Color; set { mBackContent.CheckedColorSet.Color = value; InvalidateVisual(); } }
    public SvgIcon? CheckIcon { get => mCheckItem.Icon; set { mCheckItem.Icon = value; InvalidateVisual(); } }
    public CheckBox()
    {
        Width = 16;
        Height = 16;
        AddContent(new() { Item = new IconItem() { Icon = Assets.CheckBoxFrame }, UncheckedColorSet = new() { Color = Colors.White } });
        AddContent(mBackContent);
        AddContent(new() { Item = mCheckItem, CheckedColorSet = new() { Color = Colors.White } });
    }

    readonly ToggleContent mBackContent = new() { Item = new BorderItem() { CornerRadius = 4 }, CheckedColorSet = new() { Color = Style.HIGH_LIGHT } };
    readonly IconItem mCheckItem = new() { Icon = Assets.Check };
}
