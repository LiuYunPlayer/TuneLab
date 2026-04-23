using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TuneLab.Audio;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Controllers;
using TuneLab.I18N;
using TuneLab.Utils;
using AvaloniaHorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using AvaloniaVerticalAlignment = Avalonia.Layout.VerticalAlignment;
using CheckBox = TuneLab.GUI.Components.CheckBox;
using Orientation = Avalonia.Layout.Orientation;

namespace TuneLab.UI;

internal class ExportSideBarContentProvider : ISideBarContentProvider
{
    public event Action<ExportOptions>? ExportRequested;

    public SideBar.SideBarContent Content => new()
    {
        Icon = Assets.Export.GetImage(Style.LIGHT_WHITE),
        Name = "Export".Tr(TC.Dialog),
        Items = [mContentPanel],
    };

    public ExportSideBarContentProvider()
    {
        mContentPanel.Orientation = Orientation.Vertical;
        mContentPanel.MaxWidth = 280;
        mContentPanel.ClipToBounds = true;
        mContentPanel.Background = Style.INTERFACE.ToBrush();

        // --- Export Path (folder) ---
        AddSectionLabel("Export Path".Tr(TC.Dialog));
        mPathInput = new PathInput
        {
            Options = new FolderPickerOpenOptions { Title = "Select Export Folder".Tr(TC.Dialog) },
            Height = 28,
            Margin = new Thickness(12, 0, 12, 8),
        };
        mPathInput.Display(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        mContentPanel.Children.Add(mPathInput);

        AddSeparator();

        // --- File Name ---
        AddSectionLabel("File Name".Tr(TC.Dialog));
        mFileNameInput = new SingleLineTextController
        {
            Height = 28,
            Margin = new Thickness(12, 0, 12, 8),
        };
        mFileNameInput.Display("export"); // will be updated when project is set
        mContentPanel.Children.Add(mFileNameInput);

        AddSeparator();

        // --- Export Button ---
        var exportBtnContainer = new Border
        {
            Padding = new Thickness(12, 8),
        };
        {
            var exportBtn = new GUI.Components.Button() { Height = 32 }
                .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { Color = Style.BUTTON_PRIMARY, HoveredColor = Style.BUTTON_PRIMARY_HOVER } })
                .AddContent(new() { Item = new TextItem() { Text = "Export".Tr(TC.Dialog), FontSize = 14, Alignment = Alignment.Center, PivotAlignment = Alignment.Center }, ColorSet = new() { Color = Colors.White } });
            exportBtn.Clicked += OnExportClicked;
            exportBtnContainer.Child = exportBtn;
        }
        mContentPanel.Children.Add(exportBtnContainer);

        AddSeparator();

        // --- Track Selection ---
        AddSectionLabel("Tracks".Tr(TC.Dialog));

        // Track list container
        mTrackListPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(12, 0, 12, 4),
        };
        mContentPanel.Children.Add(mTrackListPanel);

        // Select All / Deselect All row (below track list)
        var selectAllPanel = new Grid
        {
            Margin = new Thickness(12, 4, 12, 8),
            ColumnDefinitions = new ColumnDefinitions("*,8,*"),
        };
        {
            var selectAllBtn = CreateStyledButton("Select All".Tr(TC.Dialog));
            selectAllBtn.Clicked += () =>
            {
                foreach (var item in mTrackItems)
                    item.CheckBox.IsChecked = true;
            };
            Grid.SetColumn(selectAllBtn, 0);
            selectAllPanel.Children.Add(selectAllBtn);

            var deselectAllBtn = CreateStyledButton("Deselect All".Tr(TC.Dialog));
            deselectAllBtn.Clicked += () =>
            {
                foreach (var item in mTrackItems)
                    item.CheckBox.IsChecked = false;
            };
            Grid.SetColumn(deselectAllBtn, 2);
            selectAllPanel.Children.Add(deselectAllBtn);
        }
        mContentPanel.Children.Add(selectAllPanel);

        AddSeparator();

        // --- Sample Rate ---
        AddSectionLabel("Sample Rate".Tr(TC.Dialog));
        mSampleRateDropDown = new DropDown
        {
            Height = 28,
            Margin = new Thickness(12, 0, 12, 8),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        foreach (var rate in SampleRates)
        {
            mSampleRateDropDown.Items.Add(new ComboBoxItem { Content = rate.ToString() + " Hz" });
        }
        mSampleRateDropDown.SelectedIndex = Array.IndexOf(SampleRates, 44100); // default 44100
        mContentPanel.Children.Add(mSampleRateDropDown);

        AddSeparator();

        // --- Bit Depth ---
        AddSectionLabel("Bit Depth".Tr(TC.Dialog));
        mBitDepthDropDown = new DropDown
        {
            Height = 28,
            Margin = new Thickness(12, 0, 12, 8),
            HorizontalAlignment = AvaloniaHorizontalAlignment.Stretch,
        };
        foreach (var depth in BitDepths)
        {
            mBitDepthDropDown.Items.Add(new ComboBoxItem { Content = depth.ToString() + " bit" });
        }
        mBitDepthDropDown.SelectedIndex = Array.IndexOf(BitDepths, 16); // default 16
        mContentPanel.Children.Add(mBitDepthDropDown);
    }

    public void SetProject(IProject? project)
    {
        mProject = project;
        RefreshTrackList();
    }

    public void SetProjectName(string name)
    {
        var fileName = Path.GetFileNameWithoutExtension(name);
        if (!string.IsNullOrWhiteSpace(fileName))
            mFileNameInput.Display(fileName);
    }

    public void RefreshTrackList()
    {
        mTrackListPanel.Children.Clear();
        mTrackItems.Clear();

        // Master track option
        var masterItem = CreateTrackItem("Master", Style.HIGH_LIGHT, true);
        mTrackItems.Add(masterItem);
        mTrackListPanel.Children.Add(masterItem.Panel);

        if (mProject == null)
            return;

        // Individual tracks
        for (int i = 0; i < mProject.Tracks.Count; i++)
        {
            var track = mProject.Tracks[i];
            var trackName = track.Name.Value;
            if (string.IsNullOrEmpty(trackName))
                trackName = $"Track {i + 1}";

            Color trackColor;
            try { trackColor = Color.Parse(track.Color.Value); }
            catch { trackColor = Style.DefaultTrackColor; }

            var trackItem = CreateTrackItem(trackName, trackColor, false, i);
            mTrackItems.Add(trackItem);
            mTrackListPanel.Children.Add(trackItem.Panel);
        }
    }

    void OnExportClicked()
    {
        var exportPath = mPathInput.Value;
        var fileName = mFileNameInput.Value;

        if (string.IsNullOrWhiteSpace(exportPath))
            return;

        if (string.IsNullOrWhiteSpace(fileName))
            return;

        // Resolve relative path (supports .. notation)
        string resolvedPath;
        try
        {
            resolvedPath = Path.GetFullPath(exportPath);
        }
        catch
        {
            return;
        }

        var selectedSampleRate = SampleRates[Math.Max(0, mSampleRateDropDown.SelectedIndex)];
        var selectedBitDepth = BitDepths[Math.Max(0, mBitDepthDropDown.SelectedIndex)];

        var selectedTracks = new List<int>(); // -1 for master, 0..N for track indices
        for (int i = 0; i < mTrackItems.Count; i++)
        {
            if (mTrackItems[i].CheckBox.IsChecked)
            {
                selectedTracks.Add(mTrackItems[i].TrackIndex);
            }
        }

        if (selectedTracks.Count == 0)
            return;

        ExportRequested?.Invoke(new ExportOptions
        {
            ExportPath = resolvedPath,
            FileName = fileName,
            SelectedTrackIndices = selectedTracks,
            SampleRate = selectedSampleRate,
            BitDepth = selectedBitDepth,
        });
    }

    GUI.Components.Button CreateStyledButton(string text)
    {
        var btn = new GUI.Components.Button() { Height = 24 }
            .AddContent(new()
            {
                Item = new BorderItem() { CornerRadius = 4 },
                ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER }
            })
            .AddContent(new()
            {
                Item = new TextItem() { Text = text, FontSize = 11, Alignment = Alignment.Center, PivotAlignment = Alignment.Center },
                ColorSet = new() { Color = Style.LIGHT_WHITE }
            });
        return btn;
    }

    TrackItem CreateTrackItem(string name, Color trackColor, bool isMaster, int trackIndex = -1)
    {
        // Layout: [ColorBar] [TrackName ...] [CheckBox]
        var panel = new DockPanel
        {
            Margin = new Thickness(0, 1),
            Height = 26,
        };

        // Checkbox on the right
        var checkBox = new CheckBox();
        checkBox.IsChecked = isMaster; // Master is checked by default
        var checkBoxContainer = new Border
        {
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Child = checkBox,
        };
        DockPanel.SetDock(checkBoxContainer, Avalonia.Controls.Dock.Right);
        panel.Children.Add(checkBoxContainer);

        // Color bar on the left
        var colorBar = new Border
        {
            Width = 4,
            CornerRadius = new CornerRadius(0),
            Background = trackColor.ToBrush(),
            VerticalAlignment = AvaloniaVerticalAlignment.Stretch,
            Margin = new Thickness(0, 3, 8, 3),
        };
        DockPanel.SetDock(colorBar, Avalonia.Controls.Dock.Left);
        panel.Children.Add(colorBar);

        // Track name label fills remaining space
        var label = new TextBlock
        {
            Text = name,
            FontSize = 12,
            Foreground = Style.LIGHT_WHITE.ToBrush(),
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        panel.Children.Add(label);

        return new TrackItem
        {
            Panel = panel,
            CheckBox = checkBox,
            IsMaster = isMaster,
            TrackIndex = isMaster ? -1 : trackIndex,
        };
    }

    void AddSectionLabel(string text)
    {
        var label = new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = Style.LIGHT_WHITE.Opacity(0.7).ToBrush(),
            Margin = new Thickness(12, 8, 12, 4),
        };
        mContentPanel.Children.Add(label);
    }

    void AddSeparator()
    {
        mContentPanel.Children.Add(new Border
        {
            Height = 1,
            Background = Style.BACK.ToBrush(),
            Margin = new Thickness(0, 2),
        });
    }

    class TrackItem
    {
        public required DockPanel Panel { get; init; }
        public required CheckBox CheckBox { get; init; }
        public required bool IsMaster { get; init; }
        public required int TrackIndex { get; init; } // -1 for master
    }

    readonly StackPanel mContentPanel = new();
    readonly PathInput mPathInput;
    readonly SingleLineTextController mFileNameInput;
    readonly DropDown mSampleRateDropDown;
    readonly DropDown mBitDepthDropDown;
    readonly StackPanel mTrackListPanel;
    readonly List<TrackItem> mTrackItems = new();

    IProject? mProject;

    static readonly int[] SampleRates = [32000, 44100, 48000, 88200, 96000];
    static readonly int[] BitDepths = [16, 24, 32];
}

internal class ExportOptions
{
    public required string ExportPath { get; init; }
    public required string FileName { get; init; }
    public required List<int> SelectedTrackIndices { get; init; } // -1 for master
    public required int SampleRate { get; init; }
    public required int BitDepth { get; init; }
}
