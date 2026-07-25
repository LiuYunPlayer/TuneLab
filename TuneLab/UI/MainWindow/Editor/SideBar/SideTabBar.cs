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
using TuneLab.Extensions.Derivers;

namespace TuneLab.UI;

internal class SideTabBar : ListView
{
    public INotifiableProperty<SideBarTab> SelectedTab = new NotifiableProperty<SideBarTab>(SideBarTab.None);

    public SideTabBar()
    {
        Width = 48;
        var hoverBack = Colors.White.Opacity(0.05);

        // badge：可选叠在 tab 图标右上角的小控件（如派生 tab 的「有可处理 / 新」提示点）。
        void AddTab(SideBarTab tab, string tooltip, SvgIcon icon, Control? badge = null)
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
            Control tabControl = toggle;
            if (badge != null)
            {
                var wrap = new Panel() { Width = 48, Height = 48 };
                wrap.Children.Add(toggle);
                wrap.Children.Add(badge);
                tabControl = wrap;
            }
            Content.Children.Add(tabControl);
            Content.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
            OnTabChanged();
        }

        // 派生 tab 提示点：有「可处理 / 新」（本会话未查看的新完成、或失败任务）且当前没在看该 tab 时亮起。
        mDerivationDot = new Border()
        {
            Width = 8, Height = 8, CornerRadius = new(4),
            Background = Style.HIGH_LIGHT.ToBrush(),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Margin = new(0, 10, 10, 0),
            IsVisible = false,
            IsHitTestVisible = false,
        };

        AddTab(SideBarTab.PartProperties, "Part".Tr(this), Assets.Part);
        AddTab(SideBarTab.NoteProperties, "Note".Tr(this), Assets.Note);
        AddTab(SideBarTab.Agent, "Agent".Tr(this), Assets.Agent);
        AddTab(SideBarTab.Script, "Script".Tr(this), Assets.Script);
        AddTab(SideBarTab.Derivation, "Derivation".Tr(this), Assets.Derive, mDerivationDot);
        AddTab(SideBarTab.Extensions, "Extensions".Tr(this), Assets.Extensions);
        AddTab(SideBarTab.Export, "Export".Tr(this), Assets.Export);

        DerivationTaskManager.Changed.Subscribe(UpdateDerivationDot);
        SelectedTab.Modified.Subscribe(OnSelectedTabChanged);
        UpdateDerivationDot();
    }

    void OnSelectedTabChanged()
    {
        // 打开 Derivation tab 即清「未查看的新完成」标志（失败任务须显式处理才消）。
        if (SelectedTab.Value == SideBarTab.Derivation)
            DerivationTaskManager.NotifyTabOpened();
        UpdateDerivationDot();
    }

    void UpdateDerivationDot()
    {
        mDerivationDot.IsVisible = DerivationTaskManager.HasActionable && SelectedTab.Value != SideBarTab.Derivation;
    }

    readonly Border mDerivationDot;
}
