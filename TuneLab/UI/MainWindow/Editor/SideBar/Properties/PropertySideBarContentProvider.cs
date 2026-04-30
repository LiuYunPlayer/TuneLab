using Avalonia.Controls;
using Avalonia.Layout;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.GUI.Controllers;
using TuneLab.Base.Properties;
using TuneLab.Base.Structures;
using DynamicData;
using Avalonia.Media;
using TuneLab.GUI.Components;
using TuneLab.Base.Data;
using TuneLab.Utils;
using TuneLab.Base.Utils;
using TuneLab.I18N;
using TuneLab.Configs;
using TuneLab.Extensions.Formats.DataInfo;
using static TuneLab.GUI.Dialog;

namespace TuneLab.UI;

internal class PropertySideBarContentProvider : ISideBarContentProvider
{
    public SideBar.SideBarContent Content => new() { Icon = Assets.Properties.GetImage(Style.LIGHT_WHITE), Name = "Properties".Tr(TC.Property), Items = [mTrackPanel, mPresetPanel, mPartPanel, mAutomationPanel, mNotePanel] };

    public PropertySideBarContentProvider()
    {
        var trackName = new Label() { Content = "Track".Tr(TC.Property), Height = 38, FontSize = 14, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), Padding = new(24, 0) };
        mTrackPanel.Title = trackName;
        mTrackContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        mTrackContent.Children.Add(mTrackPluginController);
        mTrackPanel.Content = mTrackContent;

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

        mPresetComboBox.ValueCommited.Subscribe(OnPresetComboBoxValueCommited);

        mPresetContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush(), Margin = new(-12, 0) });
        mPresetContentContainer.Child = mPresetContent;
        mPresetPanel.Content = mPresetContentContainer;

        var partName = new Label() { Content = "Part".Tr(TC.Property), Height = 38, FontSize = 14, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), Padding = new(24, 0) };
        mPartPanel.Title = partName;
        mPartContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        mPartContent.Children.Add(mPartFixedController);
        mPartContent.Children.Add(mPartPropertiesController);
        mPartPanel.Content = mPartContent;

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

    void OnPresetComboBoxValueCommited()
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
        
        // Update track plugin controller with the current track
        mTrackPluginController.Track = part?.Track;

        if (mPart != null)
        {
            mPart.Voice.Modified.Subscribe(OnConfigChnaged, s);

            mPart.Automations.Any(automation => automation.DefaultValue.Modified).Subscribe(OnAutomationDefaultValueModified, s);
            mAutomationController.ValueWillChange.Subscribe(OnAutomationValueWillChange, s);
            mAutomationController.ValueChanged.Subscribe(OnAutomationValueChanged, s);
            mAutomationController.ValueCommited.Subscribe(OnAutomationValueCommited, s);

            mPart.Properties.PropertyModified.Subscribe(OnPartPropertyModified, s);
            mPartPropertiesController.ValueCommited.Subscribe(OnPartValueCommited, s);

            mPart.Notes.SelectionChanged.Subscribe(OnNoteSelectionChanged, s);
            mPart.Notes.Any(note => note.Properties.PropertyModified).Subscribe(OnNotePropertyModified, s);
            mNotePropertiesController.ValueCommited.Subscribe(OnNoteValueCommited, s);

            Setup(mPart);
        }
    }

    void Setup(IMidiPart part)
    {
        mAutomationController.SetConfig(new(part.Voice.AutomationConfigs));
        mPartFixedController.Part = part;
        mPartPropertiesController.SetConfig(part.Voice.PartProperties);
        mNotePropertiesController.SetConfig(part.Voice.NoteProperties);

        RefreshAutomationController();
        RefreshPartPropertiesController();
        RefreshNotePropertiesController();
    }

    void Terminate()
    {
        mAutomationController.ResetConfig();
        mPartFixedController.Part = null;
        mPartPropertiesController.ResetConfig();
        mNotePropertiesController.ResetConfig();
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
        RefreshNotePropertiesController();
    }

    void OnNotePropertyModified(PropertyPath path)
    {
        RefreshNotePropertiesController();
    }

    void RefreshAutomationController()
    {
        if (mPart == null)
            return;

        foreach (var kvp in mPart.Voice.AutomationConfigs)
        {
            var key = kvp.Key;
            if (mPart.Automations.TryGetValue(key, out var automation))
            {
                mAutomationController.Display(new PropertyPath(key).GetKey(), automation.DefaultValue.Value);
            }
            else
            {
                mAutomationController.Display(new PropertyPath(key).GetKey(), kvp.Value.DefaultValue);
            }
        }
    }

    void RefreshPartPropertiesController()
    {
        if (mPart == null)
            return;

        DisplayPartProperties(mPart.Voice.PartProperties, new());
    }

    void DisplayPartProperties(ObjectConfig config, PropertyPath path)
    {
        if (mPart == null)
            return;

        foreach (var kvp in config.Properties)
        {
            var propertyPath = path.Combine(kvp.Key);
            if (kvp.Value is ObjectConfig objectConfig)
            {
                DisplayPartProperties(objectConfig, propertyPath);
            }
            else if (kvp.Value is IValueConfig valueConfig)
            {
                var key = propertyPath.GetKey();
                var value = mPart.Properties.GetValue(key, valueConfig.DefaultValue);
                mPartPropertiesController.Display(key, value);
            }
        }
    }

    void RefreshNotePropertiesController()
    {
        mNoteContentMask.IsVisible = true;
        if (mPart == null)
            return;

        var selectedNotes = mPart.Notes.AllSelectedItems();
        DisplaySelectedNotesProperties(selectedNotes, mPart.Voice.NoteProperties, new());
        mNoteContentMask.IsVisible = selectedNotes.IsEmpty();
    }

    void DisplaySelectedNotesProperties(IReadOnlyCollection<INote> notes, ObjectConfig config, PropertyPath path)
    {
        foreach (var kvp in config.Properties)
        {
            var propertyPath = path.Combine(kvp.Key);
            if (kvp.Value is ObjectConfig objectConfig)
            {
                DisplaySelectedNotesProperties(notes, objectConfig, propertyPath);
            }
            else if (kvp.Value is IValueConfig valueConfig)
            {
                var key = propertyPath.GetKey();
                if (notes.IsEmpty())
                {
                    mNotePropertiesController.DisplayNull(key);
                }
                else
                {
                    var first = notes.First();
                    PropertyValue firstValue = first.Properties.GetValue(key, valueConfig.DefaultValue);
                    foreach (var note in notes)
                    {
                        var value = note.Properties.GetValue(key, valueConfig.DefaultValue);
                        if (!firstValue.Equals(value))
                        {
                            firstValue = PropertyValue.Invalid;
                            break;
                        }
                    }
                    mNotePropertiesController.Display(key, firstValue);
                }
            }
        }
    }

    void OnPartPropertyModified(PropertyPath path)
    {
        RefreshPartPropertiesController();
    }

    void OnPartValueCommited(PropertyPath path, PropertyValue value)
    {
        mPart?.Properties.SetValue(path.GetKey(), value).Commit();
    }

    void OnNoteValueCommited(PropertyPath path, PropertyValue value)
    {
        if (mPart == null)
            return;

        foreach (var note in mPart.Notes.AllSelectedItems())
        {
            note.Properties.SetValue(path.GetKey(), value);
        }

        mPart.Notes.Commit();
    }

    void OnAutomationDefaultValueModified()
    {
        RefreshAutomationController();
    }

    Head mAutomationHead;
    void OnAutomationValueWillChange(PropertyPath path)
    {
        if (mPart == null) 
            return;

        mPart.BeginMergeDirty();
        mAutomationHead = mPart.Head;
    }

    void OnAutomationValueChanged(PropertyPath path, PropertyValue value)
    {
        if (mPart == null)
            return;

        if (!value.ToDouble(out var number))
            return;

        mPart.DiscardTo(mAutomationHead);
        string key = path.GetKey();
        if (!mPart.Automations.TryGetValue(key, out var automation))
        {
            automation = mPart.AddAutomation(key);
        }

        if (automation == null)
            return;

        automation.DefaultValue.Set(number);
    }

    void OnAutomationValueCommited(PropertyPath path, PropertyValue value)
    {
        if (mPart == null)
            return;

        OnAutomationValueChanged(path, value);
        mPart.EndMergeDirty();
        mPart.Commit();
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

        ResetPartPropertiesToDefaults(mPart.Voice.PartProperties, new PropertyPath());
        ResetAutomationDefaults();
        mPart.Commit();
    }

    void ApplyPreset(PartPreset preset)
    {
        if (mPart == null)
            return;

        mPart.Voice.Set(new VoiceInfo() { Type = preset.Voice.Type, ID = preset.Voice.ID });
        ResetPartPropertiesToDefaults(mPart.Voice.PartProperties, new PropertyPath());
        ApplyPresetProperties(preset.Properties, new PropertyPath());
        ApplyAutomationDefaults(preset);
        mPart.Commit();
    }

    void ResetPartPropertiesToDefaults(ObjectConfig config, PropertyPath path)
    {
        if (mPart == null)
            return;

        foreach (var kvp in config.Properties)
        {
            var propertyPath = path.Combine(kvp.Key);
            if (kvp.Value is ObjectConfig objectConfig)
            {
                ResetPartPropertiesToDefaults(objectConfig, propertyPath);
            }
            else if (kvp.Value is IValueConfig valueConfig)
            {
                mPart.Properties.SetValue(propertyPath.GetKey(), valueConfig.DefaultValue);
            }
        }
    }

    void ApplyPresetProperties(PropertyObject properties, PropertyPath path)
    {
        if (mPart == null)
            return;

        foreach (var property in properties.Map)
        {
            var propertyPath = path.Combine(property.Key);
            if (property.Value.ToObject(out var propertyObject))
            {
                ApplyPresetProperties(propertyObject, propertyPath);
            }
            else
            {
                mPart.Properties.SetValue(propertyPath.GetKey(), property.Value);
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
        mPresetComboBox.SetConfig(new EnumConfig(options, 0));
        mPresetComboBox.Display(selectedPresetName ?? NonePresetOption);
    }

    string? SelectedPresetName()
    {
        var value = mPresetComboBox.Value;
        return value.Equals(NonePresetOption, StringComparison.OrdinalIgnoreCase) ? null : value;
    }

    readonly StackPanel mTrackContent = new() { Orientation = Orientation.Vertical };
    readonly Border mPresetContentContainer = new() { Background = Style.INTERFACE.ToBrush(), Padding = new(12, 0, 12, 12) };
    readonly StackPanel mPresetContent = new() { Orientation = Orientation.Vertical, Spacing = 8 };
    readonly StackPanel mAutomationContent = new() { Orientation = Orientation.Vertical };
    readonly StackPanel mPartContent = new() { Orientation = Orientation.Vertical };
    readonly StackPanel mNoteContent = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mTrackPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mPresetPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mAutomationPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mPartPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mNotePanel = new() { Orientation = Orientation.Vertical };
    readonly LayerPanel mNoteContentPanel = new();

    readonly TrackPluginController mTrackPluginController = new();
    readonly ComboBoxController mPresetComboBox = new() { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
    readonly TuneLab.GUI.Components.Button mPresetMoreButton;
    readonly ObjectController mAutomationController = new();
    readonly MidiPartFixedController mPartFixedController = new();
    readonly ObjectController mPartPropertiesController = new();
    readonly ObjectController mNotePropertiesController = new();

    readonly Border mNoteContentMask = new() { Background = Colors.Black.Opacity(0.3).ToBrush() };

    const string NonePresetOption = "None";
    IMidiPart? mPart = null;
    List<PartPreset> mPresets = [];
    readonly DisposableManager s = new();
}
