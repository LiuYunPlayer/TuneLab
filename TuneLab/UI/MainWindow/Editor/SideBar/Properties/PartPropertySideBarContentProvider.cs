using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
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
// 合并展示，混源则只剩 Gain；Preset/Automation/Effects 是单 part 概念，多选时隐藏（Effects 待 SDK 支持多对象 config 后再做）。
internal class PartPropertySideBarContentProvider : ISideBarContentProvider
{
    public SideBar.SideBarContent Content => new() { Icon = Assets.Part.GetImage(Style.LIGHT_WHITE), Name = Title, Items = [mPresetPanel, mPartPanel, mAutomationPanel, mEffectsPanel] };

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

        // 与 Script 侧栏脚本下拉同构：点开自定义 Flyout，列 None + 各 preset（点击应用、行右侧 ✕ 删除）。钮文字显示当前选中 preset。
        mPresetButton = new TuneLab.GUI.Components.Button() { Height = 28, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch }
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER } });
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

        var presetRow = new DockPanel() { LastChildFill = true };
        DockPanel.SetDock(mPresetMoreButton, Dock.Right);
        presetRow.Children.Add(mPresetMoreButton);
        mPresetButton.Margin = new(0, 0, 8, 0);
        presetRow.Children.Add(mPresetButton);
        mPresetContent.Children.Add(presetRow);

        mPresetContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush(), Margin = new(-12, 0) });
        mPresetContentContainer.Child = mPresetContent;
        mPresetPanel.Content = mPresetContentContainer;

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
        PopulatePresetMenu();
        mPresetFlyout.ShowAt(mPresetButton);
    }

    // 每次打开重建：顶部「None」（恢复默认）+ 分隔线 + 各 preset（点击应用并记为选中、右侧 ✕ 删除）。
    // 删除走二次确认（preset 比会话贵重，保留谨慎，不跟 script 的即时删一刀切）。
    void PopulatePresetMenu()
    {
        var stack = new StackPanel() { Orientation = Orientation.Vertical, MinWidth = 220 };
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

    // 点行：记为选中并应用到当前 part（None = 恢复默认）。没有 part 时只更新选中、不应用。
    void ApplySelection(string? presetName)
    {
        SetSelectedPreset(presetName);
        OnApplyPresetClicked();
    }

    void OnPresetMoreButtonClicked()
    {
        var menu = new ContextMenu();
        var hasSelection = SelectedPresetName() != null;
        var hasPart = mPart != null;

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

        // Preset/Effects 是单 part 概念，仅单选时绑当前 part；多/空选 mPart 为 null（panel 也已隐藏）。
        mEffectsController.SetPart(mPart);

        // Automation 默认值：单选或同引擎多选才合并展示，否则清空。
        mAutomationController.SetParts(ShowsMerged() ? mParts : Array.Empty<IMidiPart>());

        // Gain：单/多/空统一合并绑定（空 → 滑块 Invalid）。
        mPartFixedController.SetParts(mParts);

        // 动态属性：单选或同引擎多选才求 config 合并展示，否则清空。
        RefreshPartController();
    }

    // 按目标 part 集决定各栏显隐（见类注释的多选语义）。
    void ApplyMode()
    {
        bool single = mParts.Count == 1;
        bool merged = ShowsMerged();   // 单选或同引擎多选：动态属性 + Automation 合并展示
        bool empty = mParts.Count == 0;

        mPresetPanel.IsVisible = single;       // Preset/Effects 单 part 概念
        mEffectsPanel.IsVisible = single;
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
    }

    async Task OnRenamePresetClicked()
    {
        var selectedPresetName = SelectedPresetName();
        if (selectedPresetName == null)
            return;

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

        var selectedPresetName = SelectedPresetName();
        if (selectedPresetName == null)
            return;

        if (!await ConfirmOverwriteAsync(selectedPresetName))
            return;

        SavePreset(selectedPresetName);
    }

    void OnApplyPresetClicked()
    {
        if (mPart == null)
            return;

        var selectedPresetName = SelectedPresetName();
        if (selectedPresetName == null)
        {
            ApplyDefaultPreset();
            return;
        }

        var preset = mPresets.FirstOrDefault(item => item.Name.Equals(selectedPresetName, StringComparison.OrdinalIgnoreCase));
        if (preset == null)
            return;

        ApplyPreset(preset);
    }

    void ApplyDefaultPreset()
    {
        if (mPart == null)
            return;

        ResetPartPropertiesToDefaults(mPart.SoundSource.GetPartPropertyConfig(BuildPartContext()), mPart.Properties);
        ResetAutomationDefaults();
        mPart.Commit();
    }

    void ApplyPreset(PartPreset preset)
    {
        if (mPart == null)
            return;

        mPart.SoundSource.SetInfo(new SoundSourceInfo() { Type = preset.Voice.Type, ID = preset.Voice.ID });
        ResetPartPropertiesToDefaults(mPart.SoundSource.GetPartPropertyConfig(BuildPartContext()), mPart.Properties);
        ApplyPresetProperties(preset.Properties, mPart.Properties);
        ApplyAutomationDefaults(preset);
        mPart.Commit();
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

    void ResetAutomationDefaults()
    {
        if (mPart == null)
            return;

        foreach (var kvp in mPart.SoundSource.AutomationConfigs)
        {
            if (mPart.Automations.TryGetValue(kvp.Key.Id, out var automation))
                automation.DefaultValue.Set(kvp.Value.DefaultValue);
        }
    }

    void ApplyAutomationDefaults(PartPreset preset)
    {
        if (mPart == null)
            return;

        foreach (var kvp in mPart.SoundSource.AutomationConfigs)
        {
            double value = preset.Automations.GetValueOrDefault(kvp.Key.Id, kvp.Value.DefaultValue);
            if (mPart.Automations.TryGetValue(kvp.Key.Id, out var automation))
            {
                automation.DefaultValue.Set(value);
            }
            else if (value != kvp.Value.DefaultValue)
            {
                mPart.AddAutomation(kvp.Key.Id)?.DefaultValue.Set(value);
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
            if (presetName.Equals(mSelectedPresetName, StringComparison.OrdinalIgnoreCase))
                SetSelectedPreset(null);
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

        return presetName;
    }

    void SavePreset(string presetName)
    {
        try
        {
            PresetConfigManager.SavePreset(BuildPreset(presetName));
            LoadPresets(presetName);
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
            LoadPresets(newPresetName);
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
            Voice = mPart.SoundSource.GetInfo(),
            Properties = mPart.Properties.GetInfo(),
        };

        foreach (var kvp in mPart.SoundSource.AutomationConfigs)
        {
            var key = kvp.Key.Id;
            if (mPart.Automations.TryGetValue(key, out var automation))
                preset.Automations[key] = automation.DefaultValue.Value;
            else
                preset.Automations[key] = kvp.Value.DefaultValue;
        }

        return preset;
    }

    // 重载 preset 列表（下拉每次打开时按 mPresets 重建）。selectedPresetName 给定则设为选中；否则保留当前选中，
    // 若当前选中已不在列表中（被删/改名）则回落 None。
    void LoadPresets(string? selectedPresetName = null)
    {
        mPresets = PresetConfigManager.LoadPresets();
        var keep = selectedPresetName ?? mSelectedPresetName;
        SetSelectedPreset(keep != null && mPresets.Any(p => p.Name.Equals(keep, StringComparison.OrdinalIgnoreCase)) ? keep : null);
    }

    // 选中态：撑住 ⋯ 菜单 Save/Rename 的作用目标 + 下拉钮文字（null = None）。Flyout 是瞬态菜单，故选中态须自己记。
    void SetSelectedPreset(string? presetName)
    {
        mSelectedPresetName = string.IsNullOrWhiteSpace(presetName) ? null : presetName;
        mPresetLabel.Item = new TextItem() { Text = (mSelectedPresetName ?? NonePresetOption.Tr(TC.Property)) + "  ▾", FontSize = 12 };
    }

    string? SelectedPresetName() => mSelectedPresetName;

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

    readonly Border mPresetContentContainer = new() { Background = Style.INTERFACE.ToBrush(), Padding = new(12, 0, 12, 12) };
    readonly StackPanel mPresetContent = new() { Orientation = Orientation.Vertical, Spacing = 8 };
    readonly StackPanel mAutomationContent = new() { Orientation = Orientation.Vertical };
    readonly StackPanel mEffectsContent = new() { Orientation = Orientation.Vertical };
    readonly StackPanel mPartContent = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mPresetPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mAutomationPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mEffectsPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mPartPanel = new() { Orientation = Orientation.Vertical };
    readonly LayerPanel mPartContentPanel = new();
    readonly Border mPartContentMask = new() { Background = Colors.Black.Opacity(0.3).ToBrush() };

    readonly TuneLab.GUI.Components.Button mPresetButton;
    readonly ButtonContent mPresetLabel;
    readonly Flyout mPresetFlyout;
    bool mPresetFlyoutJustClosed;
    string? mSelectedPresetName;
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
