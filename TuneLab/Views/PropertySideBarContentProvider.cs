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

namespace TuneLab.Views;

internal class PropertySideBarContentProvider : ISideBarContentProvider
{
    public SideBar.SideBarContent Content => new() { Icon = Assets.Properties.GetImage(Style.LIGHT_WHITE), Name = "Properties".Tr(TC.Property), Items = [mPartContent, mAutomationContent, mNotePanel] };

    public PropertySideBarContentProvider()
    {
        var partName = new Label() { Content = "Part".Tr(TC.Property), Height = 38, FontSize = 14, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), Padding = new(24, 0) };
        mPartContent.Children.Add(partName);
        mPartContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        mPartContent.Children.Add(mPartFixedController);
        mPartContent.Children.Add(mPartPropertiesController);

        var automationName = new Label() { Content = "Automation".Tr(TC.Property), Height = 38, FontSize = 14, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), Padding = new(24, 0) };
        mAutomationContent.Children.Add(automationName);
        mAutomationContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        mAutomationContent.Children.Add(mAutomationController);

        var noteName = new Label() { Content = "Note".Tr(TC.Property), Height = 38, FontSize = 14, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), Padding = new(24, 0) };
        mNoteContent.Children.Add(noteName);
        mNoteContent.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        mNoteContent.Children.Add(mNotePropertiesController);

        mNotePanel.Children.Add(mNoteContent);
        mNotePanel.Children.Add(mNoteContentMask);
    }

    public void SetPart(MidiPart? part)
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

    void Setup(MidiPart part)
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

    readonly StackPanel mAutomationContent = new() { Orientation = Orientation.Vertical };
    readonly StackPanel mPartContent = new() { Orientation = Orientation.Vertical };
    readonly StackPanel mNoteContent = new() { Orientation = Orientation.Vertical };
    readonly LayerPanel mNotePanel = new();

    readonly ObjectController mAutomationController = new();
    readonly MidiPartFixedController mPartFixedController = new();
    readonly ObjectController mPartPropertiesController = new();
    readonly ObjectController mNotePropertiesController = new();

    readonly Border mNoteContentMask = new() { Background = Colors.Black.Opacity(0.3).ToBrush() };

    MidiPart? mPart = null;
    readonly DisposableManager s = new();
}
