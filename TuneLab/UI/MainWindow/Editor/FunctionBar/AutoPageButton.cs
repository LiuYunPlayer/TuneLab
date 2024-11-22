using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Input;
using TuneLab.Utils;

namespace TuneLab.UI;

internal class AutoPageButton : Toggle
{
    public INotifiableProperty<PlayScrollTarget> PlayScrollTarget { get; }

    public AutoPageButton(INotifiableProperty<PlayScrollTarget> playScrollTarget)
    {
        PlayScrollTarget = playScrollTarget;
        var backContent = new ToggleContent() { Item = new BorderItem() { CornerRadius = 4 }, CheckedColorSet = new() { Color = Style.HIGH_LIGHT }, UncheckedColorSet = new() { HoveredColor = Colors.White.Opacity(0.05), PressedColor = Colors.White.Opacity(0.05) } };
        var iconContent = new ToggleContent() { Item = mIconItem, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5) } };

        AddContent(backContent);
        AddContent(iconContent);
    }

    protected override void OnMouseDown(MouseDownEventArgs e)
    {
        switch (e.MouseButtonType) 
        { 
            case MouseButtonType.PrimaryButton:
                PlayScrollTarget.Value = PlayScrollTarget.Value == UI.PlayScrollTarget.View ? UI.PlayScrollTarget.None : UI.PlayScrollTarget.View;
                mIconItem.Icon = Assets.AutoPage;
                break;
            case MouseButtonType.SecondaryButton:
                PlayScrollTarget.Value = PlayScrollTarget.Value == UI.PlayScrollTarget.Playhead ? UI.PlayScrollTarget.None : UI.PlayScrollTarget.Playhead;
                mIconItem.Icon = Assets.AutoScroll;
                break;
        }

        switch (PlayScrollTarget.Value)
        {
            case UI.PlayScrollTarget.None:

                break;
            case UI.PlayScrollTarget.View:

                break;
            case UI.PlayScrollTarget.Playhead:

                break;
        }

        IsChecked = PlayScrollTarget.Value != UI.PlayScrollTarget.None;
        InvalidateVisual();
    }

    readonly IconItem mIconItem = new IconItem() { Icon = Assets.AutoPage };
}
