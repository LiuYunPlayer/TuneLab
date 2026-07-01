using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.GUI;
using TuneLab.GUI.Components;

using TuneLab.GUI.Controllers;

namespace TuneLab.GUI.Components;

internal class CheckBox : Toggle, IDataValueController<bool>
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

    // 概念态。关键：勾选层颜色是 150ms 淡入淡出动画（Button.CorrectColor），淡出期间会绘制当前图标——
    // 所以只在【进入勾选确定态(value=true)】时才把图标设回 √；【取消勾选(value=false)】不动图标，
    // 让旧字形(dash 或 √)按原样淡出，绝不在淡出途中换成 √（否则 Multiple→空 会闪一下 √）。
    public override void Display(bool value)
    {
        Background = Style.HIGH_LIGHT;
        if (value)
            CheckIcon = Assets.Check;
        base.Display(value);
    }

    // Invalid（无选中）：空框。不改图标/底色——未勾态本就不显，且避免污染淡出中的字形。
    public void DisplayNull()
    {
        base.Display(false);
    }

    // 多值：高亮底 + dash（中间态）。
    public void DisplayMultiple()
    {
        Background = Style.HIGH_LIGHT;
        CheckIcon = Assets.Hyphen;
        base.Display(true);
    }

    readonly ToggleContent mBackContent = new() { Item = new BorderItem() { CornerRadius = 4 }, CheckedColorSet = new() { Color = Style.HIGH_LIGHT } };
    readonly IconItem mCheckItem = new() { Icon = Assets.Check };
}
