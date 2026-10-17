using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.Utils;

namespace TuneLab.UI;

// 右侧图标栏：每个图标各自开关一个侧栏面（点开、再点关），打开的面并排共存。
// 打开集合是**有序**的（= 打开的先后），并排顺序随它走——想调序就关了重开。
// 放不放得下不归这里判：由宿主经 CanOpen 回答（它知道窗口还剩多宽），这里只按答案拦点击、置灰图标。
internal class SideTabBar : ListView
{
    public IReadOnlyList<SideBarTab> OpenTabs => mOpenTabs;
    public event Action? OpenTabsChanged;

    // 宿主回答「此刻还放得下这一面吗」；null = 不设限。已开着的面不问（关总是允许的）。
    public Func<SideBarTab, bool>? CanOpen { get; set; }

    public SideTabBar()
    {
        Width = 48;

        void AddTab(SideBarTab tab, string tooltip, SvgIcon icon)
        {
            var toggle = new Toggle() { Width = 48, Height = 48 }
                        .AddContent(new() { Item = new IconItem() { Icon = icon }, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5), HoveredColor = Style.LIGHT_WHITE } });
            toggle.SetupToolTip(tooltip, placementMode: PlacementMode.Left, verticalOffset: 0, horizontalOffset: -8, showDelay: 500);
            // 放不下时拦住「打开」这一下（关闭不拦）：Toggle 在翻转前问 AllowSwitch，拒绝即原样不动。
            toggle.AllowSwitch += () => IsOpen(tab) || CanOpen == null || CanOpen(tab);
            toggle.Switched.Subscribe(() => { if (toggle.IsChecked) Open(tab); else Close(tab); });
            Content.Children.Add(toggle);
            Content.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
            mToggles.Add(tab, (toggle, tooltip));
        }

        AddTab(SideBarTab.PartProperties, "Part".Tr(this), Assets.Part);
        AddTab(SideBarTab.Effects, "Effects".Tr(this), Assets.Effects);
        AddTab(SideBarTab.NoteProperties, "Note".Tr(this), Assets.Note);
        AddTab(SideBarTab.Agent, "Agent".Tr(this), Assets.Agent);
        AddTab(SideBarTab.Script, "Script".Tr(this), Assets.Script);
        AddTab(SideBarTab.Extensions, "Extensions".Tr(this), Assets.Extensions);
        AddTab(SideBarTab.Export, "Export".Tr(this), Assets.Export);
    }

    public bool IsOpen(SideBarTab tab) => mOpenTabs.Contains(tab);

    // 确保打开（已开则不动、不调序）：动作面的 show 与图标点击共用这一条，故「再 show 一次」不会把它挪到末尾。
    public void Open(SideBarTab tab)
    {
        if (tab == SideBarTab.None || IsOpen(tab))
            return;
        mOpenTabs.Add(tab);
        RaiseChanged();
    }

    public void Close(SideBarTab tab)
    {
        if (!mOpenTabs.Remove(tab))
            return;
        RaiseChanged();
    }

    public void CloseAll()
    {
        if (mOpenTabs.Count == 0)
            return;
        mOpenTabs.Clear();
        RaiseChanged();
    }

    // 恢复持久化的打开集合（启动时）：整体替换、只发一次变更。
    public void Restore(IEnumerable<SideBarTab> tabs)
    {
        mOpenTabs.Clear();
        foreach (var tab in tabs)
            if (tab != SideBarTab.None && mToggles.ContainsKey(tab) && !mOpenTabs.Contains(tab))
                mOpenTabs.Add(tab);
        RaiseChanged();
    }

    // 宿主可用宽度变了（窗口缩放 / 面板拖宽 / 开关面板）：按 CanOpen 重算各未开图标的可开性。
    // 不能开的图标压暗并换 tooltip 说明原因——不设 IsEnabled=false，那样连 tooltip 都不弹，用户不知道为什么点不动。
    public void RefreshAvailability()
    {
        foreach (var (tab, (toggle, tooltip)) in mToggles)
        {
            bool blocked = !IsOpen(tab) && CanOpen != null && !CanOpen(tab);
            toggle.Opacity = blocked ? 0.35 : 1;
            ToolTip.SetTip(toggle, blocked ? tooltip + "\n" + "Not enough room — close another panel or widen the window".Tr(this) : tooltip);
        }
    }

    void RaiseChanged()
    {
        foreach (var (tab, (toggle, _)) in mToggles)
            toggle.Display(IsOpen(tab));
        OpenTabsChanged?.Invoke();
        RefreshAvailability();
    }

    readonly List<SideBarTab> mOpenTabs = [];
    readonly Dictionary<SideBarTab, (Toggle Toggle, string Tooltip)> mToggles = [];
}
