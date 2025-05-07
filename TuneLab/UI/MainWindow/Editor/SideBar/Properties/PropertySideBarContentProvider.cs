using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using TuneLab.Data;
using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.Utils;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Controllers;
using TuneLab.I18N;
using TuneLab.Utils;

namespace TuneLab.UI;

internal class PropertySideBarContentProvider : ISideBarContentProvider
{
    public SideBar.SideBarContent Content => new() { Icon = Assets.Properties.GetImage(Style.LIGHT_WHITE), Name = "Properties".Tr(TC.Property), Items = [mPartPanel, mAutomationPanel, mNotePanel] };

    public PropertySideBarContentProvider()
    {
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

        mPart.When(part => part.Voice.Modified).Subscribe(() => 
        {
            RefreshPartPropertiesController();
            RefreshNotePropertiesController();
            RefreshAutomationController();
        }, s);

        mPart.When(part => part.Automations.Any(automation => automation.DefaultValue.Modified)).Subscribe(RefreshAutomationController, s);
        mPart.When(part => part.Properties.Modified).Subscribe(RefreshPartPropertiesController, s);
        mPart.When(part => part.Notes.SelectionChanged).Subscribe(RefreshNotePropertiesController, s);
        mPart.When(part => part.Notes.Any(note => note.Properties.Modified)).Subscribe(RefreshNotePropertiesController, s);
        mPart.ObjectChanged.Subscribe(() => mPartFixedController.Part = mPart.Object, s);
    }

    ~PropertySideBarContentProvider()
    {
        s.DisposeAll();
    }

    public void SetPart(IMidiPart? part)
    {
        mPart.Set(part);
    }

    void OnNoteSelectionChanged()
    {
        RefreshNotePropertiesController();
    }

    void RefreshAutomationController()
    {
        if (Part == null)
            return;

        var config = new ObjectConfig(Part.Voice.AutomationConfigs);
        // mAutomationController.SetConfig(config, Part.Automations); TODO: Fix this
    }

    void RefreshPartPropertiesController()
    {
        if (Part == null)
            return;

        var config = Part.Voice.PropertyConfig;
        mPartPropertiesController.SetConfig(config, Part.Properties);
    }

    void RefreshNotePropertiesController()
    {
        mNoteContentMask.IsVisible = true;
        if (Part == null)
            return;

        var selectedNotes = Part.Notes.AllSelectedItems();
        var config = Part.Voice.GetNotePropertyConfig(selectedNotes);
        mNotePropertiesController.SetConfig(config, new MultipleDataPropertyObject(Part.Notes, selectedNotes.Select(note => note.Properties)));
        mNoteContentMask.IsVisible = !selectedNotes.Any();
    }

    IMidiPart? Part => mPart.Object;

    readonly StackPanel mAutomationContent = new() { Orientation = Orientation.Vertical };
    readonly StackPanel mPartContent = new() { Orientation = Orientation.Vertical };
    readonly StackPanel mNoteContent = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mAutomationPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mPartPanel = new() { Orientation = Orientation.Vertical };
    readonly CollapsiblePanel mNotePanel = new() { Orientation = Orientation.Vertical };
    readonly LayerPanel mNoteContentPanel = new();

    readonly PropertyObjectController mAutomationController = new();
    readonly MidiPartFixedController mPartFixedController = new();
    readonly PropertyObjectController mPartPropertiesController = new();
    readonly PropertyObjectController mNotePropertiesController = new();

    readonly Border mNoteContentMask = new() { Background = Colors.Black.Opacity(0.3).ToBrush() };

    Owner<IMidiPart> mPart = new();
    readonly DisposableManager s = new();
}
