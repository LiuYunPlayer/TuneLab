using Avalonia.Media;
using TuneLab.GUI.Components;
using TuneLab.Utils;
using TuneLab.GUI;
using Avalonia.Controls;
using TuneLab.I18N;
using TuneLab.Foundation.Event;

namespace TuneLab.UI;

internal class SideTabBar : ListView
{
    public INotifiableProperty<SideBarTab> SelectedTab = new NotifiableProperty<SideBarTab>(SideBarTab.None);

    public SideTabBar()
    {
        Width = 48;
        var hoverBack = Colors.White.Opacity(0.05);

        void AddTab(SideBarTab tab, string tooltip, SvgIcon icon)
        {
            var toggle = new Toggle() { Width = 48, Height = 48 }
                        .AddContent(new() { Item = new IconItem() { Icon = icon }, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5), HoveredColor = Style.LIGHT_WHITE } });
            void OnTabChanged()
            {
                toggle.Display(SelectedTab.Value == tab);
            }
            toggle.SetupToolTip(tooltip, placementMode: PlacementMode.Left, verticalOffset: 0, horizontalOffset: -8, showDelay: 500);
            toggle.Switched.Subscribe(() => SelectedTab.Value = toggle.IsChecked ? tab : SideBarTab.None);
            SelectedTab.Modified.Subscribe(OnTabChanged);
            Content.Children.Add(toggle);
            Content.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
            OnTabChanged();
        }

        AddTab(SideBarTab.Extensions, "Extensions".Tr(this), Assets.Extensions);
        AddTab(SideBarTab.Properties, "Properties".Tr(this), Assets.Properties);
        AddTab(SideBarTab.Export, "Export".Tr(this), Assets.Export);
    }
}
