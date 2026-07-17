using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.GUI.Controllers;
using TuneLab.SDK;
using Avalonia.Media;
using TuneLab.GUI.Components;
using TuneLab.Utils;
using TuneLab.I18N;
using TuneLab.Configs;
using static TuneLab.GUI.Dialog;

namespace TuneLab.UI;

// 目标 part 的来源：钢琴窗当前编辑的 part（Current）或编排区选中的 part 集（Selected）。决定大标题文案。
internal enum PartPanelSource { Current, Selected }

// Part 作用域属性侧栏：Preset / Properties / Automation / Effects。与 note 作用域面板（见 NotePropertySideBarContentProvider）
// 拆为两个独立页签，两者各自订阅当前 part 的数据层事件、互不引用（靠事件解耦）。
//
// 目标 part 由宿主按焦点感知下发（见 Editor.UpdatePartPanelTarget）：单 part = 单选，多 part = 编排区多选。
// 多选语义：Gain（公共属性）始终合并展示；动态属性仅当全部 part 同引擎（Kind+Type 一致）时调该引擎 GetPartPropertyConfig
// 合并展示，混源则只剩 Gain；Preset/Automation 是单 part 概念，多选时隐藏；Effects 多选时按槽位（index）对齐
// 合并展示——槽内类型全等则全功能合并，类型不等/部分缺位显示 (Multiple)/占位行（可替换/删除），不整块隐藏。
internal class PartPropertySideBarContentProvider : ISideBarContentProvider
{
    public SideBar.SideBarContent Content => new() { Icon = Assets.Part.GetImage(Style.LIGHT_WHITE), Name = Title, Items = [mPresetPanel, mVoicePanel, mPartPanel, mAutomationPanel, mEffectsPanel] };

    // 大标题随目标 part 变化（重命名/选中变化/单多切换）实时更新，宿主据此刷新 SideBar 顶栏。
    public event Action? TitleChanged;
    public string Title { get; private set; } = "Part".Tr(TC.Property);

    public PartPropertySideBarContentProvider()
    {
        var presetName = new Label() { Content = "Preset".Tr(TC.Property), Height = 38, FontSize = 14, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), Padding = new(24, 0) };
        mPresetPanel.Title = presetName;
        mPresetContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });

        mPresetMoreButton = new TuneLab.GUI.Components.Button() { Width = 28, Height = 28 }
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER, PressedColor = Style.INTERFACE } })
            .AddContent(new() { Item = new TextItem() { Text = "⋯", FontSize = 16 }, ColorSet = new() { Color = Colors.White } });
        mPresetMoreButton.Clicked += OnPresetMoreButtonClicked;

        // 点开自定义 Flyout，列 None + 各 preset（点击应用、行右侧 ✕ 删除）。钮文字显示当前 part 关联的 preset。
        // 触发钮用深色（Style.BACK）+ ▾，与 Voice/属性等下拉外观统一（不再用偏亮的 BUTTON_NORMAL）。
        mPresetButton = new TuneLab.GUI.Components.Button() { Height = 28, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch }
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { Color = Style.BACK, HoveredColor = Style.BACK } });
        mPresetLabel = new ButtonContent() { Item = new TextItem() { Text = NonePresetOption, FontSize = 12 }, ColorSet = new() { Color = Style.LIGHT_WHITE } };
        mPresetButton.AddContent(mPresetLabel);
        mPresetButton.Clicked += OnPresetButtonClicked;

        mPresetFlyout = new Flyout() { Placement = PlacementMode.BottomEdgeAlignedLeft };
        mPresetFlyout.FlyoutPresenterClasses.Add("agent-menu");
        mPresetFlyout.Closed += (_, _) =>
        {
            mPresetFlyoutJustClosed = true;   // light-dismiss 再次点钮时先关闭，置标志让随后 Click 不重开 → toggle
            Dispatcher.UIThread.Post(() => mPresetFlyoutJustClosed = false, DispatcherPriority.Input);
        };

        // 缩进 24（与其它控件一致）：整行 Margin(24,12)，⋯ 钮靠右即距面板边 24，主钮填满、与 ⋯ 间隔 8。
        var presetRow = new DockPanel() { LastChildFill = true, Margin = new(24, 12) };
        DockPanel.SetDock(mPresetMoreButton, Dock.Right);
        presetRow.Children.Add(mPresetMoreButton);
        mPresetButton.Margin = new(0, 0, 8, 0);
        presetRow.Children.Add(mPresetButton);
        mPresetContent.Children.Add(presetRow);

        mPresetContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        mPresetPanel.Content = mPresetContent;

        var voiceName = new Label() { Content = "Voice".Tr(TC.Property), Height = 38, FontSize = 14, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), Padding = new(24, 0) };
        mVoicePanel.Title = voiceName;
        mVoiceContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        mVoiceContent.Children.Add(mPartVoiceController);
        mVoicePanel.Content = mVoiceContent;

        var propertiesName = new Label() { Content = "Properties".Tr(TC.Property), Height = 38, FontSize = 14, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), Padding = new(24, 0) };
        mPartPanel.Title = propertiesName;
        mPartContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        mPartContent.Children.Add(mPartFixedController);
        mPartContent.Children.Add(mPartPropertiesController);
        // 无目标 part 时盖遮罩（压暗 + 挡交互），提示去选 part；有 part 时按 mode 显隐 Gain / 动态属性。
        mPartContentPanel.Children.Add(mPartContent);
        mPartContentPanel.Children.Add(mPartContentMask);
        mPartPanel.Content = mPartContentPanel;

        var effectsName = new Label() { Content = "Effects".Tr(TC.Property), Height = 38, FontSize = 14, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), Padding = new(24, 0) };
        mEffectsPanel.Title = effectsName;
        mEffectsContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        mEffectsContent.Children.Add(mEffectsController);
        mEffectsPanel.Content = mEffectsContent;

        var automationName = new Label() { Content = "Automation".Tr(TC.Property), Height = 38, FontSize = 14, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), Padding = new(24, 0) };
        mAutomationPanel.Title = automationName;
        mAutomationContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        mAutomationContent.Children.Add(mAutomationController);
        mAutomationPanel.Content = mAutomationContent;

        ApplyMode();
        LoadPresets();
    }

    void OnPresetButtonClicked()
    {
        if (mPresetFlyoutJustClosed) return;   // 再次点击恰逢 light-dismiss 刚关 → 不重开（toggle）
        LoadPresets();   // 打开列表前重扫磁盘：手工放进 Presets 文件夹的 preset 文件（转发共享）免重启即现
        PopulatePresetMenu();
        mPresetFlyout.ShowAt(mPresetButton);
    }

    // 每次打开重建：顶部「None」（恢复默认）+ 分隔线 + 各 preset（点击应用并记为选中、右侧 ✕ 删除）。
    // 删除走二次确认（preset 比会话贵重，保留谨慎，不跟 script 的即时删一刀切）。
    void PopulatePresetMenu()
    {
        // 宽度对齐触发钮（Flyout 已 BottomEdgeAlignedLeft 左对齐）：扣掉 agent-menu presenter 的内边距(4)+描边(1)×2，
        // 使 presenter 外框正好等于钮宽（否则左对齐后右侧多出这一圈）。
        var stack = new StackPanel() { Orientation = Orientation.Vertical, Width = Math.Max(0, mPresetButton.Bounds.Width - 10) };
        stack.Children.Add(FlyoutMenuRow.Build(NonePresetOption.Tr(TC.Property), null, () => ApplySelection(null), null, mPresetFlyout));

        if (mPresets.Count > 0)
            stack.Children.Add(new Border() { Height = 1, Margin = new(8, 4), Background = Style.LIGHT_WHITE.Opacity(0.15).ToBrush() });
        foreach (var preset in mPresets)
        {
            var name = preset.Name;
            stack.Children.Add(FlyoutMenuRow.Build(name, name,
                () => ApplySelection(name),
                () => { mPresetFlyout.Hide(); _ = DeletePreset(name); }, mPresetFlyout));
        }

        mPresetFlyout.Content = stack;
    }

    // 点行：应用到所有目标 part（None = 恢复默认）并建立运行期关联（None = 解除关联）。
    void ApplySelection(string? presetName)
    {
        ApplyPresetToAll(presetName);
        foreach (var part in mParts)
            Associate(part, presetName);
        RefreshPresetLabel();
    }

    void OnPresetMoreButtonClicked()
    {
        var menu = new ContextMenu();
        var hasPart = mPart != null;
        var hasSelection = mPart != null && AssociationOf(mPart) != null;

        {
            var menuItem = new MenuItem().SetName("Save As".Tr(TC.Menu)).SetAction(async () => await OnSaveAsPresetClicked());
            menuItem.IsEnabled = hasPart;
            menu.Items.Add(menuItem);
        }
        {
            var menuItem = new MenuItem().SetName("Save".Tr(TC.Property)).SetAction(async () => await OnSavePresetClicked());
            menuItem.IsEnabled = hasPart && hasSelection;
            menu.Items.Add(menuItem);
        }
        {
            var menuItem = new MenuItem().SetName("Rename".Tr(TC.Menu)).SetAction(async () => await OnRenamePresetClicked());
            menuItem.IsEnabled = hasSelection;
            menu.Items.Add(menuItem);
        }

        mPresetMoreButton.OpenContextMenu(menu);
    }

    // 焦点感知下发的目标 part 集（单/多/空）。单 part 字段 mPart 供单 part 专属栏（Preset/Automation/Effects）使用。
    public void SetParts(IReadOnlyList<IMidiPart> parts, PartPanelSource source)
    {
        s.DisposeAll();

        mParts = parts;
        mSource = source;
        mPart = parts.Count == 1 ? parts[0] : null;

        foreach (var part in mParts)
        {
            part.SoundSource.Modified.Subscribe(OnConfigChnaged, s);
            // part 属性 commit（结果态）→ 重算动态属性面板（自身联动）。
            part.Properties.Modified.Subscribe(OnPartPropertiesModified, s);
            // 重命名 → 刷新大标题。
            part.Name.Modified.Subscribe(RaiseTitleChanged, s);
        }

        Setup();
        RaiseTitleChanged();
    }

    void Setup()
    {
        ApplyMode();

        // Effects：单/多选统一按 part 集合并展示（多选要求链对齐，不对齐时面板隐藏、控件树自清空）。
        mEffectsController.SetParts(mParts);

        // Voice 必定展示（混引擎自行退化为全引擎列表 + (Multiple)）；Automation 仅单选或同引擎多选才合并展示。
        mPartVoiceController.SetParts(mParts);
        mAutomationController.SetParts(ShowsMerged() ? mParts : Array.Empty<IMidiPart>());

        // Gain：单/多/空统一合并绑定（空 → 滑块 Invalid）。
        mPartFixedController.SetParts(mParts);

        // 动态属性：单选或同引擎多选才求 config 合并展示，否则清空。
        RefreshPartController();

        // 下拉钮文字 = 当前目标 part 的运行期 preset 关联（切 part 即跟随）。
        RefreshPresetLabel();
    }

    // 按目标 part 集决定各栏显隐（见类注释的多选语义）。
    void ApplyMode()
    {
        bool merged = ShowsMerged();   // 单选或同引擎多选：动态属性 + Automation 合并展示
        bool empty = mParts.Count == 0;

        mPresetPanel.IsVisible = !empty;       // Preset：单/多选均可（Apply 扇出到全部，含统一混源音源）
        mVoicePanel.IsVisible = !empty;        // Voice：必定显示（混引擎也合并显示 (Multiple)，菜单退化为全引擎列表）
        mEffectsPanel.IsVisible = !empty;      // Effects：有 part 即显（多选按槽位对齐合并，链不齐以 Multiple/empty 槽呈现）
        mAutomationPanel.IsVisible = merged;

        mPartFixedController.IsVisible = !empty;        // Gain：>=1 即显（含混源公共属性）
        mPartPropertiesController.IsVisible = merged;   // 动态属性：单选或同引擎多选
        mPartContentMask.IsVisible = empty;
    }

    // 是否合并展示引擎相关栏（动态属性 / Automation）：单选，或同引擎多选。混源多选 / 空选为否。
    bool ShowsMerged() => mParts.Count == 1 || (mParts.Count > 1 && AllSameEngine());

    bool AllSameEngine()
    {
        if (mParts.Count == 0)
            return false;
        var kind = mParts[0].SoundSource.Kind;
        var type = mParts[0].SoundSource.Type;
        for (int i = 1; i < mParts.Count; i++)
            if (mParts[i].SoundSource.Kind != kind || mParts[i].SoundSource.Type != type)
                return false;
        return true;
    }

    // part 值 commit：动态属性面板按当前值重算（数据对象不变，走 Reconcile）。
    void OnPartPropertiesModified()
    {
        if (mParts.Count == 0)
            return;

        ReconcilePartController();
    }

    // ---- 条件属性面板：config = f(context)，按当前值重算并 keyed-diff 到控件树 ----
    // part config 依赖各 part 自身值（多选传多 part context，由引擎合并）。

    void RefreshPartController()
    {
        if (!ShowsMerged())
        {
            mPartPropertiesController.ResetConfig();
            return;
        }

        var data = mParts.Count == 1
            ? (IDataPropertyObject)mParts[0].Properties
            : new MultipleDataPropertyObject(mParts.Select(part => part.Properties).ToList());
        mPartPropertiesController.SetConfig(mParts[0].SoundSource.GetPartPropertyConfig(BuildPartContext()), data);
    }

    // 重算 defer 到下一 UI 调度：属性 commit 可能发生在控件自身事件回调链中（如 ComboBox 的 SelectionChanged），
    // 同步重算会重入修改控件集合——Avalonia 的 ComboBox 在其 SelectionChanged 处理中 Clear/重填 Items 会抛 IndexOutOfRange。
    // 推迟到当前事件链完全返回后再 reconcile；pending 标志合并一拍内的多次触发。
    void ReconcilePartController()
    {
        if (mPartReconcilePending)
            return;
        mPartReconcilePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            mPartReconcilePending = false;
            if (!ShowsMerged())
                return;
            mPartPropertiesController.Reconcile(mParts[0].SoundSource.GetPartPropertyConfig(BuildPartContext()));
        });
    }

    // 声明面活视图壳（引擎无关、调用级）：part 面板各 part → 一个 PartContext，组成 part 列表
    //（宿主不替插件合并，插件按需 .Merge()）。复用数据层 PartContext（TuneLab.Data）。
    PartPropertyContext BuildPartContext()
        => new(mParts.Select(part => new PartContext(part)).ToList());

    // 任一 part 音源变化（换音源 → 引擎/门控/config 可能变）：整体按当前目标重建。
    void OnConfigChnaged()
    {
        Setup();
    }

    async Task OnSaveAsPresetClicked()
    {
        if (mPart == null)
            return;

        LoadPresets();   // 重名判定基于磁盘现状（用户可能刚在资源管理器里增删过文件）
        var presetName = await RequestPresetNameAsync();
        if (presetName == null)
            return;

        var existingPreset = mPresets.FirstOrDefault(preset => preset.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
        if (existingPreset != null)
        {
            var shouldOverwrite = await ConfirmOverwriteAsync(existingPreset.Name);
            if (!shouldOverwrite)
                return;

            presetName = existingPreset.Name;
        }

        SavePreset(presetName);
        Associate(mPart, presetName);   // Save As 建立运行期关联（后续 Save 直接回写它）
        RefreshPresetLabel();
    }

    async Task OnRenamePresetClicked()
    {
        var selectedPresetName = mPart != null ? AssociationOf(mPart) : null;
        if (selectedPresetName == null)
            return;

        LoadPresets();   // 同 Save As：重名判定基于磁盘现状
        var presetName = await RequestPresetNameAsync(selectedPresetName);
        if (presetName == null || presetName.Equals(selectedPresetName, StringComparison.OrdinalIgnoreCase))
            return;

        var existingPreset = mPresets.FirstOrDefault(preset => preset.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
        if (existingPreset != null)
        {
            var shouldOverwrite = await ConfirmOverwriteAsync(existingPreset.Name);
            if (!shouldOverwrite)
                return;

            presetName = existingPreset.Name;
        }

        RenamePreset(selectedPresetName, presetName);
    }

    async Task OnSavePresetClicked()
    {
        if (mPart == null)
            return;

        var selectedPresetName = AssociationOf(mPart);
        if (selectedPresetName == null)
            return;

        if (!await ConfirmOverwriteAsync(selectedPresetName))
            return;

        SavePreset(selectedPresetName);
    }

    // 应用 preset / 恢复默认：扇出到所有目标 part（单选即 1 个），归为一个撤销步（共享文档，commit 一次）。
    // None = 恢复默认；其余 = 设音源 + 重置属性 + 套 preset 属性/自动化默认。多选混源亦可——apply 会统一各 part 音源。
    void ApplyPresetToAll(string? presetName)
    {
        if (mParts.Count == 0)
            return;

        PartPreset? preset = null;
        if (presetName != null)
        {
            preset = mPresets.FirstOrDefault(item => item.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
            if (preset == null)
                return;
        }

        foreach (var part in mParts)
            part.BeginMergeDirty();
        foreach (var part in mParts)
        {
            if (preset == null)
                ApplyDefaultPresetTo(part);
            else
                ApplyPresetTo(part, preset);
        }
        foreach (var part in mParts)
            part.EndMergeDirty();
        mParts[0].Commit();
    }

    // 单 part 的应用（config 按该 part 自身音源现算：apply 可能正在改音源，须 per-part 单元素 context）。
    void ApplyDefaultPresetTo(IMidiPart part)
    {
        ResetPartPropertiesToDefaults(part.SoundSource.GetPartPropertyConfig(PartPropertyContext.Single(part)), part.Properties);
        ResetAutomationDefaultsOf(part);
    }

    void ApplyPresetTo(IMidiPart part, PartPreset preset)
    {
        part.SoundSource.SetInfo(preset.Source);
        ResetPartPropertiesToDefaults(part.SoundSource.GetPartPropertyConfig(PartPropertyContext.Single(part)), part.Properties);
        ApplyPresetProperties(preset.Properties, part.Properties);
        ApplyAutomationDefaultsTo(part, preset);
    }

    // 沿 ObjectConfig 结构与数据节点并行导航：嵌套 config 走 node.Object(key) 下降，叶子 config 写 node.SetValue。
    static void ResetPartPropertiesToDefaults(ObjectConfig config, IDataPropertyObject node)
    {
        foreach (var kvp in config.Properties)
        {
            if (kvp.Value is ObjectConfig objectConfig)
            {
                ResetPartPropertiesToDefaults(objectConfig, node.Object(kvp.Key.Id));
            }
            else if (kvp.Value is ArrayConfig or ListConfig or ExtensibleObjectConfig)
            {
                // 数组/列表/变长键控容器：写入默认值（递归各元素/键 config 默认值拼成 PropertyArray/PropertyObject）。
                // 显式重置即物化该值（变长键控 = 当前声明键的默认对象，替换整个容器值）。
                node.SetValue(kvp.Key.Id, kvp.Value.GetDefaultValue());
            }
            else if (kvp.Value is IValueConfig valueConfig)
            {
                node.SetValue(kvp.Key.Id, valueConfig.DefaultValue);
            }
        }
    }

    // preset 抓取用：按 config 树把默认值物化进快照——显式值优先、absent 字段落抓取时的声明默认值。
    // 默认值也是声音的一部分：引擎日后改默认值不应改变既存 preset 的声音（与 automations 的物化抓取同口径）。
    // config 之外的既有键（孤儿/条件面板当前隐藏字段的存值）原样保留，与换引擎保留数据同判例。
    static PropertyObject MaterializeProperties(ObjectConfig config, PropertyObject current)
    {
        var map = new Map<string, PropertyValue>();
        foreach (var kvp in config.Properties)
        {
            var key = kvp.Key.Id;
            bool hasCurrent = current.Map.TryGetValue(key, out var currentValue);
            if (kvp.Value is ObjectConfig objectConfig)
            {
                var sub = hasCurrent && currentValue.ToObject(out var currentObject) ? currentObject : PropertyObject.Empty;
                map.Add(key, MaterializeProperties(objectConfig, sub));
            }
            else if (kvp.Value is ArrayConfig or ListConfig or ExtensibleObjectConfig)
            {
                map.Add(key, hasCurrent ? currentValue : kvp.Value.GetDefaultValue());
            }
            else if (kvp.Value is IValueConfig valueConfig)
            {
                map.Add(key, hasCurrent ? currentValue : valueConfig.DefaultValue);
            }
        }
        foreach (var kvp in current.Map)
        {
            if (!map.ContainsKey(kvp.Key))
                map.Add(kvp.Key, kvp.Value);
        }
        return new PropertyObject(map);
    }

    static void ApplyPresetProperties(PropertyObject properties, IDataPropertyObject node)
    {
        foreach (var property in properties.Map)
        {
            if (property.Value.ToObject(out var propertyObject))
            {
                ApplyPresetProperties(propertyObject, node.Object(property.Key));
            }
            else
            {
                node.SetValue(property.Key, property.Value);
            }
        }
    }

    void ResetAutomationDefaultsOf(IMidiPart part)
    {
        foreach (var kvp in part.SoundSource.AutomationConfigs)
        {
            if (part.Automations.TryGetValue(kvp.Key.Id, out var automation))
                automation.DefaultValue.Set(kvp.Value.DefaultValue);
        }
    }

    void ApplyAutomationDefaultsTo(IMidiPart part, PartPreset preset)
    {
        foreach (var kvp in part.SoundSource.AutomationConfigs)
        {
            double value = preset.Automations.TryGetValue(kvp.Key.Id, out var info) ? info.DefaultValue : kvp.Value.DefaultValue;
            if (part.Automations.TryGetValue(kvp.Key.Id, out var automation))
            {
                automation.DefaultValue.Set(value);
            }
            else if (value != kvp.Value.DefaultValue)
            {
                part.AddAutomation(kvp.Key.Id)?.DefaultValue.Set(value);
            }
        }
    }

    // 删除指定 preset（行内 ✕ 触发，先二次确认）；若删的是当前选中项，选中回落 None。
    async Task DeletePreset(string presetName)
    {
        if (!await ConfirmDeleteAsync(presetName))
            return;

        try
        {
            PresetConfigManager.DeletePreset(presetName);
            RemapAssociations(presetName, null);   // 删除：指向它的运行期关联一并解除
            LoadPresets();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to delete preset: " + ex);
            _ = mPresetPanel.ShowMessage("Error".Tr(TC.Dialog), "Failed to delete preset: \n" + ex.Message);
        }
    }

    async Task<bool> ConfirmOverwriteAsync(string presetName)
    {
        return await ConfirmAsync(string.Format("Overwrite preset \"{0}\"?".Tr(TC.Property), presetName), "Save".Tr(TC.Property));
    }

    async Task<bool> ConfirmDeleteAsync(string presetName)
    {
        return await ConfirmAsync(string.Format("Delete preset \"{0}\"?".Tr(TC.Property), presetName), "Delete".Tr(TC.Property));
    }

    async Task<bool> ConfirmAsync(string message, string confirmText)
    {
        var dialog = new Dialog();
        dialog.SetTitle("Tips".Tr(TC.Dialog));
        dialog.SetMessage(message);

        bool confirmed = false;
        dialog.AddButton("Cancel".Tr(TC.Dialog), ButtonType.Normal);
        var confirmButton = dialog.AddButton(confirmText, ButtonType.Primary);
        confirmButton.Pressed += () => confirmed = true;
        dialog.Topmost = true;
        await dialog.ShowDialog(mPresetPanel.Window());
        return confirmed;
    }

    async Task<string?> RequestPresetNameAsync(string initialName = "")
    {
        var dialog = new NameInputDialog("Preset Name".Tr(TC.Property), initialName);
        var presetName = await dialog.ShowDialog<string?>(mPresetPanel.Window());
        presetName = presetName?.Trim();
        if (string.IsNullOrWhiteSpace(presetName))
            return null;

        if (presetName.Equals(NonePresetOption, StringComparison.OrdinalIgnoreCase))
        {
            await mPresetPanel.ShowMessage("Error".Tr(TC.Dialog), "\"None\" is reserved.");
            return null;
        }

        // 文件名即 preset 名：非法文件名当场拦下（存储层同规则兜底）。
        var nameError = PresetConfigManager.GetPresetNameError(presetName);
        if (nameError != null)
        {
            await mPresetPanel.ShowMessage("Error".Tr(TC.Dialog), nameError);
            return null;
        }

        return presetName;
    }

    void SavePreset(string presetName)
    {
        try
        {
            PresetConfigManager.SavePreset(BuildPreset(presetName));
            LoadPresets();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save preset: " + ex);
            _ = mPresetPanel.ShowMessage("Error".Tr(TC.Dialog), "Failed to save preset: \n" + ex.Message);
        }
    }

    void RenamePreset(string oldPresetName, string newPresetName)
    {
        try
        {
            PresetConfigManager.RenamePreset(oldPresetName, newPresetName);
            RemapAssociations(oldPresetName, newPresetName);   // 关联跟随改名
            LoadPresets();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to rename preset: " + ex);
            _ = mPresetPanel.ShowMessage("Error".Tr(TC.Dialog), "Failed to rename preset: \n" + ex.Message);
        }
    }

    PartPreset BuildPreset(string presetName)
    {
        if (mPart == null)
            throw new InvalidOperationException("Part is null.");

        var preset = new PartPreset()
        {
            Name = presetName,
            Source = mPart.SoundSource.GetInfo(),
            Properties = MaterializeProperties(
                mPart.SoundSource.GetPartPropertyConfig(PartPropertyContext.Single(mPart)),
                mPart.Properties.GetInfo()),
        };

        // 只抓默认值、Points 恒空（preset 口径：不含时间轴内容，见 PartPreset 头注释）。
        foreach (var kvp in mPart.SoundSource.AutomationConfigs)
        {
            var key = kvp.Key.Id;
            double value = mPart.Automations.TryGetValue(key, out var automation) ? automation.DefaultValue.Value : kvp.Value.DefaultValue;
            preset.Automations.Add(key, new AutomationInfo() { DefaultValue = value });
        }

        return preset;
    }

    // 重载 preset 列表（下拉每次打开时按 mPresets 重建）+ 刷新钮文字。
    void LoadPresets()
    {
        mPresets = PresetConfigManager.LoadPresets();
        RefreshPresetLabel();
    }

    // ---- part↔preset 运行期关联（弱键、会话内有效、不进工程、part 删除自动回收）----
    string? AssociationOf(IMidiPart part) => mPresetAssociations.TryGetValue(part, out var name) ? name : null;

    void Associate(IMidiPart part, string? presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
            mPresetAssociations.Remove(part);
        else
            mPresetAssociations.AddOrUpdate(part, presetName);
    }

    // 改名/删除后批量重映射关联（newName=null 表示删除该名的所有关联）。
    void RemapAssociations(string oldName, string? newName)
    {
        var affected = new List<IMidiPart>();
        foreach (var kvp in mPresetAssociations)
            if (kvp.Value.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                affected.Add(kvp.Key);
        foreach (var part in affected)
            Associate(part, newName);
    }

    // 下拉钮文字 = 当前目标 part 的关联（全等显之、混选不一致显 (Multiple)、无 part/无关联显 None）。
    void RefreshPresetLabel()
    {
        string? common = null;
        bool first = true, mixed = false;
        foreach (var part in mParts)
        {
            var a = AssociationOf(part);
            if (first) { common = a; first = false; }
            else if (a != common) { mixed = true; break; }
        }
        string text = mixed ? "(Multiple)" : (common ?? NonePresetOption.Tr(TC.Property));
        mPresetLabel.Item = new TextItem() { Text = text + "  ▾", FontSize = 12 };
    }

    void RaiseTitleChanged()
    {
        Title = ComputeTitle();
        TitleChanged?.Invoke();
    }

    // 大标题：无 → "Part"；单个 → "Editing/Selected: 名字"（来源区分当前 part vs 编排区选中）；多选 → "Selected: N parts"。
    string ComputeTitle()
    {
        if (mParts.Count == 0)
            return "Part".Tr(TC.Property);
        if (mParts.Count == 1)
            return string.Format((mSource == PartPanelSource.Selected ? "Selected: {0}" : "Editing: {0}").Tr(TC.Property), mParts[0].Name.Value);
        return string.Format("Selected: {0} parts".Tr(TC.Property), mParts.Count);
    }

    // 满宽 INTERFACE 底（与其它栏一致——它们的 INTERFACE 底来自控件自身）；内容缩进由行 Margin(24,12) 提供。
    readonly StackPanel mPresetContent = new() { Orientation = Orientation.Vertical, Background = Style.INTERFACE.ToBrush() };
    readonly StackPanel mAutomationContent = new() { Orientation = Orientation.Vertical };
    readonly StackPanel mEffectsContent = new() { Orientation = Orientation.Vertical };
    readonly StackPanel mVoiceContent = new() { Orientation = Orientation.Vertical };
    readonly StackPanel mPartContent = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mPresetPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mAutomationPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mEffectsPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mPartPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mVoicePanel = new() { Orientation = Orientation.Vertical };
    readonly PartVoiceController mPartVoiceController = new();
    readonly LayerPanel mPartContentPanel = new();
    readonly Border mPartContentMask = new() { Background = Colors.Black.Opacity(0.3).ToBrush() };

    readonly TuneLab.GUI.Components.Button mPresetButton;
    readonly ButtonContent mPresetLabel;
    readonly Flyout mPresetFlyout;
    bool mPresetFlyoutJustClosed;
    // part↔preset 运行期关联（弱键、会话内）：apply/saveAs 建立，None/删除解除；切 part 跟随、重启自然丢、不进工程文件。
    readonly ConditionalWeakTable<IMidiPart, string> mPresetAssociations = new();
    readonly TuneLab.GUI.Components.Button mPresetMoreButton;
    readonly AutomationDefaultsController mAutomationController = new();
    readonly EffectsController mEffectsController = new();
    readonly MidiPartFixedController mPartFixedController = new();
    readonly PropertyObjectController mPartPropertiesController = new();

    const string NonePresetOption = "None";
    IReadOnlyList<IMidiPart> mParts = [];
    PartPanelSource mSource = PartPanelSource.Current;
    IMidiPart? mPart = null;   // 单选时 = 唯一 part，供单 part 专属栏（Preset/Automation/Effects）使用；多/空选为 null
    List<PartPreset> mPresets = [];
    bool mPartReconcilePending = false;
    readonly DisposableManager s = new();
}
