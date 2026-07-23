using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.GUI.Components;
using TuneLab.Utils;
using TuneLab.GUI;
using System.Reflection;
using Tomlyn;
using TuneLab.Foundation;
using Avalonia.Controls;
using TuneLab.I18N;

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

        AddTab(SideBarTab.PartProperties, "Part".Tr(this), Assets.Part);
        AddTab(SideBarTab.NoteProperties, "Note".Tr(this), Assets.Note);
        AddTab(SideBarTab.History, "History".Tr(TC.Menu), Assets.History);
        AddTab(SideBarTab.Agent, "Agent".Tr(this), Assets.Agent);
        AddTab(SideBarTab.Script, "Script".Tr(this), Assets.Script);
        AddTab(SideBarTab.Extensions, "Extensions".Tr(this), Assets.Extensions);
        AddTab(SideBarTab.Export, "Export".Tr(this), Assets.Export);
    }
}
