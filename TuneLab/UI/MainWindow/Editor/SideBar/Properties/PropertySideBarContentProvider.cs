using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.GUI.Controllers;
using TuneLab.SDK;
using DynamicData;
using Avalonia.Media;
using TuneLab.GUI.Components;
using TuneLab.Utils;
using TuneLab.I18N;
using TuneLab.Configs;
using static TuneLab.GUI.Dialog;

namespace TuneLab.UI;

internal class PropertySideBarContentProvider : ISideBarContentProvider
{
    public SideBar.SideBarContent Content => new() { Icon = Assets.Properties.GetImage(Style.LIGHT_WHITE), Name = "Properties".Tr(TC.Property), Items = [mPresetPanel, mPartPanel, mEffectsPanel, mAutomationPanel, mNotePanel] };

    public PropertySideBarContentProvider()
    {
        var presetName = new Label() { Content = "Preset".Tr(TC.Property), Height = 38, FontSize = 14, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), Padding = new(24, 0) };
        mPresetPanel.Title = presetName;
        mPresetContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });

        mPresetMoreButton = new TuneLab.GUI.Components.Button() { Width = 28, Height = 28 }
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER, PressedColor = Style.INTERFACE } })
            .AddContent(new() { Item = new TextItem() { Text = "\u22EF", FontSize = 16 }, ColorSet = new() { Color = Colors.White } });
        mPresetMoreButton.Clicked += OnPresetMoreButtonClicked;

        var presetRow = new DockPanel() { LastChildFill = true };
        DockPanel.SetDock(mPresetMoreButton, Dock.Right);
        presetRow.Children.Add(mPresetMoreButton);
        mPresetComboBox.Margin = new(0, 0, 8, 0);
        presetRow.Children.Add(mPresetComboBox);
        mPresetContent.Children.Add(presetRow);

        mPresetComboBox.ValueCommitted.Subscribe(OnPresetComboBoxValueCommitted);

        mPresetContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush(), Margin = new(-12, 0) });
        mPresetContentContainer.Child = mPresetContent;
        mPresetPanel.Content = mPresetContentContainer;

        var partName = new Label() { Content = "Part".Tr(TC.Property), Height = 38, FontSize = 14, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), Padding = new(24, 0) };
        mPartPanel.Title = partName;
        mPartContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        mPartContent.Children.Add(mPartFixedController);
        mPartContent.Children.Add(mPartPropertiesController);
        mPartPanel.Content = mPartContent;

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

        var noteName = new Label() { Content = "Note".Tr(TC.Property), Height = 38, FontSize = 14, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), Padding = new(24, 0) };
        mNotePanel.Title = noteName;
        mNoteContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        mNoteContent.Children.Add(mNotePropertiesController);
        mNoteContentPanel.Children.Add(mNoteContent);
        mNoteContentPanel.Children.Add(mNoteContentMask);
        mNotePanel.Content = mNoteContentPanel;

        LoadPresets();
    }

    void OnPresetComboBoxValueCommitted()
    {
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
        {
            var menuItem = new MenuItem().SetName("Delete".Tr(TC.Property)).SetAction(async () => await OnDeletePresetClicked());
            menuItem.IsEnabled = hasSelection;
            menu.Items.Add(menuItem);
        }

        mPresetMoreButton.OpenContextMenu(menu);
    }

    public void SetPart(IMidiPart? part)
    {
        if (mPart != null)
        {
            s.DisposeAll();

            Terminate();
        }

        mPart = part;

        if (mPart != null)
        {
            mPart.Voice.Modified.Subscribe(OnConfigChnaged, s);
            mPart.Notes.SelectionChanged.Subscribe(OnNoteSelectionChanged, s);
            // part 属性 commit（结果态）→ 重算 part 面板（自身联动）并沿链重算 note 面板（note config 依赖 part 值）。
            mPart.Properties.Modified.Subscribe(OnPartPropertiesModified, s);

            Setup(mPart);
        }
    }

    void Setup(IMidiPart part)
    {
        mAutomationController.Part = part;
        mPartFixedController.Part = part;
        RefreshPartController();
        mEffectsController.SetPart(part);
        RefreshNoteController();
    }

    void Terminate()
    {
        mAutomationController.Part = null;
        mPartFixedController.Part = null;
        mPartPropertiesController.ResetConfig();
        mNotePropertiesController.ResetConfig();
        mEffectsController.SetPart(null);
        mNoteSub.DisposeAll();
        mNoteData = null;
    }

    // part 值 commit：part 面板按当前值重算（数据对象不变，走 Reconcile），并沿链触发 note 面板重算。
    void OnPartPropertiesModified()
    {
        if (mPart == null)
            return;

        ReconcilePartController();
        ReconcileNoteController();
    }

    // ---- 条件属性面板：config = f(context)，按当前值重算并 keyed-diff 到控件树 ----
    // part config 仅依赖 part 自身值；note config 依赖 part 值 + 当前选中 note 的三态合并值。

    void RefreshPartController()
    {
        if (mPart == null)
            return;

        mPartPropertiesController.SetConfig(mPart.Voice.GetPropertyConfig(BuildPartContext()), mPart.Properties);
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
            if (mPart == null)
                return;
            mPartPropertiesController.Reconcile(mPart.Voice.GetPropertyConfig(BuildPartContext()));
        });
    }

    void ReconcileNoteController()
    {
        if (mNoteReconcilePending)
            return;
        mNoteReconcilePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            mNoteReconcilePending = false;
            if (mPart == null || mNoteData == null)
                return;
            mNotePropertiesController.Reconcile(mPart.Voice.GetNotePropertyConfig(BuildNoteContext()));
        });
    }

    IPartPropertyContext BuildPartContext()
        => new PartPropertyContext(mPart!.Properties.GetInfo());

    INotePropertyContext BuildNoteContext()
        => new NotePropertyContext(mPart!.Properties.GetInfo(), MergeNoteSnapshots());

    // 当前选中 note 的三态合并快照：同 key 各 note 全等给该值、不等给 Multiple、全缺则不出现（稀疏）。
    PropertyObject MergeNoteSnapshots()
    {
        var snapshots = mPart!.Notes.AllSelectedItems().Select(note => note.Properties.GetInfo()).ToList();
        if (snapshots.Count == 0)
            return PropertyObject.Empty;
        if (snapshots.Count == 1)
            return snapshots[0];

        var keys = new List<string>();
        var seen = new HashSet<string>();
        foreach (var snapshot in snapshots)
            foreach (var kvp in snapshot.Map)
                if (seen.Add(kvp.Key))
                    keys.Add(kvp.Key);

        var merged = new Map<string, PropertyValue>();
        foreach (var key in keys)
        {
            var value = PropertyValue.Null;
            bool first = true;
            bool multiple = false;
            foreach (var snapshot in snapshots)
            {
                // 缺该 key 视作 Invalid 占位参与比较：部分 note 设过、部分没设即算多值。
                var current = snapshot.Map.TryGetValue(key, out var v) ? v : PropertyValue.Null;
                if (first) { value = current; first = false; }
                else if (!value.Equals(current)) { multiple = true; break; }
            }
            merged.Add(key, multiple ? PropertyValue.Multiple : value);
        }
        return new PropertyObject(merged);
    }

    sealed class PartPropertyContext(PropertyObject partProperties) : IPartPropertyContext
    {
        public PropertyObject PartProperties => partProperties;
    }

    sealed class NotePropertyContext(PropertyObject partProperties, PropertyObject noteProperties) : INotePropertyContext
    {
        public PropertyObject PartProperties => partProperties;
        public PropertyObject NoteProperties => noteProperties;
    }

    void OnConfigChnaged()
    {
        Terminate();
        if (mPart == null)
            return;

        Setup(mPart);
    }

    void OnNoteSelectionChanged()
    {
        RefreshNoteController();
    }

    // 把 note 属性面板绑定到当前选中 note 集合（多选合一）。无选中则盖遮罩。
    // 值的下发/写回/撤销刷新由逐字段绑定承担，选中变化时整体重绑（数据对象变 → SetConfig 重建）。
    // 选中不变期间 note 值 commit 触发 ReconcileNoteController（数据对象不变 → keyed-diff 复用控件）。
    void RefreshNoteController()
    {
        mNoteSub.DisposeAll();
        if (mPart == null)
        {
            mNotePropertiesController.ResetConfig();
            mNoteData = null;
            mNoteContentMask.IsVisible = true;
            return;
        }

        // 无选中也绑空数据源（0 对象），让控件在遮罩下呈 Invalid 态而非被清空；
        // 遮罩仅压暗 + 挡交互、提示去选音符。
        var dataObjects = mPart.Notes.AllSelectedItems().Select(note => note.Properties).ToList();
        mNoteData = new MultipleDataPropertyObject(dataObjects);
        mNotePropertiesController.SetConfig(mPart.Voice.GetNotePropertyConfig(BuildNoteContext()), mNoteData);
        mNoteContentMask.IsVisible = dataObjects.Count == 0;
        mNoteData.Modified.Subscribe(ReconcileNoteController, mNoteSub);
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

        ResetPartPropertiesToDefaults(mPart.Voice.GetPropertyConfig(BuildPartContext()), mPart.Properties);
        ResetAutomationDefaults();
        mPart.Commit();
    }

    void ApplyPreset(PartPreset preset)
    {
        if (mPart == null)
            return;

        mPart.Voice.SetInfo(new VoiceInfo() { Type = preset.Voice.Type, ID = preset.Voice.ID });
        ResetPartPropertiesToDefaults(mPart.Voice.GetPropertyConfig(BuildPartContext()), mPart.Properties);
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
                ResetPartPropertiesToDefaults(objectConfig, node.Object(kvp.Key));
            }
            else if (kvp.Value is IValueConfig valueConfig)
            {
                node.SetValue(kvp.Key, valueConfig.DefaultValue);
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

        foreach (var kvp in mPart.Voice.AutomationConfigs)
        {
            if (mPart.Automations.TryGetValue(kvp.Key, out var automation))
                automation.DefaultValue.Set(kvp.Value.DefaultValue);
        }
    }

    void ApplyAutomationDefaults(PartPreset preset)
    {
        if (mPart == null)
            return;

        foreach (var kvp in mPart.Voice.AutomationConfigs)
        {
            double value = preset.Automations.GetValueOrDefault(kvp.Key, kvp.Value.DefaultValue);
            if (mPart.Automations.TryGetValue(kvp.Key, out var automation))
            {
                automation.DefaultValue.Set(value);
            }
            else if (value != kvp.Value.DefaultValue)
            {
                mPart.AddAutomation(kvp.Key)?.DefaultValue.Set(value);
            }
        }
    }

    async Task OnDeletePresetClicked()
    {
        var selectedPresetName = SelectedPresetName();
        if (selectedPresetName == null)
            return;

        if (!await ConfirmDeleteAsync(selectedPresetName))
            return;

        try
        {
            PresetConfigManager.DeletePreset(selectedPresetName);
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
        var dialog = new PresetNameDialog(initialName);
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
            Voice = mPart.Voice.GetInfo(),
            Properties = mPart.Properties.GetInfo(),
        };

        foreach (var kvp in mPart.Voice.AutomationConfigs)
        {
            var key = kvp.Key;
            if (mPart.Automations.TryGetValue(key, out var automation))
                preset.Automations[key] = automation.DefaultValue.Value;
            else
                preset.Automations[key] = kvp.Value.DefaultValue;
        }

        return preset;
    }

    void LoadPresets(string? selectedPresetName = null)
    {
        mPresets = PresetConfigManager.LoadPresets();
        var options = new List<string>() { NonePresetOption };
        options.AddRange(mPresets.Select(preset => preset.Name));
        mPresetComboBox.SetConfig(new ComboBoxConfig { Options = options.Select(o => (ComboBoxOption)o).ToList() });
        mPresetComboBox.Display(selectedPresetName ?? NonePresetOption);
    }

    string? SelectedPresetName()
    {
        var value = mPresetComboBox.Value.ToString() ?? NonePresetOption;
        return value.Equals(NonePresetOption, StringComparison.OrdinalIgnoreCase) ? null : value;
    }

    readonly Border mPresetContentContainer = new() { Background = Style.INTERFACE.ToBrush(), Padding = new(12, 0, 12, 12) };
    readonly StackPanel mPresetContent = new() { Orientation = Orientation.Vertical, Spacing = 8 };
    readonly StackPanel mAutomationContent = new() { Orientation = Orientation.Vertical };
    readonly StackPanel mEffectsContent = new() { Orientation = Orientation.Vertical };
    readonly StackPanel mPartContent = new() { Orientation = Orientation.Vertical };
    readonly StackPanel mNoteContent = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mPresetPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mAutomationPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mEffectsPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mPartPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mNotePanel = new() { Orientation = Orientation.Vertical };
    readonly LayerPanel mNoteContentPanel = new();

    readonly ComboBoxController mPresetComboBox = new() { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    readonly TuneLab.GUI.Components.Button mPresetMoreButton;
    readonly AutomationDefaultsController mAutomationController = new();
    readonly EffectsController mEffectsController = new();
    readonly MidiPartFixedController mPartFixedController = new();
    readonly PropertyObjectController mPartPropertiesController = new();
    readonly PropertyObjectController mNotePropertiesController = new();

    readonly Border mNoteContentMask = new() { Background = Colors.Black.Opacity(0.3).ToBrush() };

    const string NonePresetOption = "None";
    IMidiPart? mPart = null;
    List<PartPreset> mPresets = [];
    MultipleDataPropertyObject? mNoteData = null;
    bool mPartReconcilePending = false;
    bool mNoteReconcilePending = false;
    readonly DisposableManager s = new();
    readonly DisposableManager mNoteSub = new();
}
