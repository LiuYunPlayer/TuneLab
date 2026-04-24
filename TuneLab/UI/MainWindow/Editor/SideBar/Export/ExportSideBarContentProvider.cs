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
        mPathInput.ValueCommited.Subscribe(SaveExportConfigToProject);
        mContentPanel.Children.Add(mPathInput);

        AddSeparator();

        // --- File Name ---
        AddSectionLabel("File Name".Tr(TC.Dialog));
        mFileNameInput = new SingleLineTextController
        {
            Height = 28,
            Margin = new Thickness(12, 0, 12, 8),
        };
        mFileNameInput.Display("export");
        mFileNameInput.ValueCommited.Subscribe(SaveExportConfigToProject);
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

        // Select All / Deselect All row (above track list)
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
                {
                    item.CheckBox.IsChecked = true;
                    if (item.Track != null) item.Track.ExportEnabled = true;
                }
            };
            Grid.SetColumn(selectAllBtn, 0);
            selectAllPanel.Children.Add(selectAllBtn);

            var deselectAllBtn = CreateStyledButton("Deselect All".Tr(TC.Dialog));
            deselectAllBtn.Clicked += () =>
            {
                foreach (var item in mTrackItems)
                {
                    item.CheckBox.IsChecked = false;
                    if (item.Track != null) item.Track.ExportEnabled = false;
                }
            };
            Grid.SetColumn(deselectAllBtn, 2);
            selectAllPanel.Children.Add(deselectAllBtn);
        }
        mContentPanel.Children.Add(selectAllPanel);

        // Track list container
        mTrackListPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(12, 0, 12, 4),
        };
        mContentPanel.Children.Add(mTrackListPanel);

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
        mSampleRateDropDown.SelectedIndex = Array.IndexOf(SampleRates, 44100);
        mSampleRateDropDown.SelectionChanged += (s, e) => SaveExportConfigToProject();
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
        mBitDepthDropDown.SelectedIndex = Array.IndexOf(BitDepths, 16);
        mBitDepthDropDown.SelectionChanged += (s, e) => SaveExportConfigToProject();
        mContentPanel.Children.Add(mBitDepthDropDown);
    }

    public void SetProject(IProject? project)
    {
        mProject = project;
        LoadExportConfigFromProject();
        RefreshTrackList();
    }

    public void SetDocument(ProjectDocument document)
    {
        mDocument = document;
        mDocument.ProjectNameChanged.Subscribe(RefreshFileName);
    }

    public void RefreshFileName()
    {
        if (mProject == null)
            return;

        if (string.IsNullOrWhiteSpace(mProject.ExportFileName))
        {
            var fallbackName = Path.GetFileNameWithoutExtension(mDocument?.Name ?? "");
            mFileNameInput.Display(!string.IsNullOrWhiteSpace(fallbackName) ? fallbackName : "Export");
        }
    }

    public void RefreshTrackList()
    {
        mTrackListPanel.Children.Clear();
        mTrackItems.Clear();

        // Master track option - settings stored in project ExportConfig
        bool masterEnabled = mProject?.MasterExportEnabled ?? true;
        int masterChannels = mProject?.MasterExportChannels ?? 2;
        var masterItem = CreateTrackItem("Master", Style.HIGH_LIGHT, true, null, masterEnabled, masterChannels);
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

            var trackItem = CreateTrackItem(trackName, trackColor, false, track, track.ExportEnabled, track.ExportChannels);
            mTrackItems.Add(trackItem);
            mTrackListPanel.Children.Add(trackItem.Panel);
        }
    }

    void LoadExportConfigFromProject()
    {
        mIsLoading = true;
        try
        {
            if (mProject == null)
            {
                mPathInput.Display(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                mFileNameInput.Display("export");
                mSampleRateDropDown.SelectedIndex = Array.IndexOf(SampleRates, 44100);
                mBitDepthDropDown.SelectedIndex = Array.IndexOf(BitDepths, 16);
                return;
            }

            mPathInput.Display(!string.IsNullOrWhiteSpace(mProject.ExportPath)
                ? mProject.ExportPath
                : Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            var fallbackName = Path.GetFileNameWithoutExtension(mDocument?.Name ?? "");
            mFileNameInput.Display(!string.IsNullOrWhiteSpace(mProject.ExportFileName)
                ? mProject.ExportFileName
                : !string.IsNullOrWhiteSpace(fallbackName)
                    ? fallbackName
                    : "export");

            var srIndex = Array.IndexOf(SampleRates, mProject.ExportSampleRate);
            mSampleRateDropDown.SelectedIndex = srIndex >= 0 ? srIndex : Array.IndexOf(SampleRates, 44100);

            var bdIndex = Array.IndexOf(BitDepths, mProject.ExportBitDepth);
            mBitDepthDropDown.SelectedIndex = bdIndex >= 0 ? bdIndex : Array.IndexOf(BitDepths, 16);
        }
        finally
        {
            mIsLoading = false;
        }
    }

    void SaveExportConfigToProject()
    {
        if (mIsLoading || mProject == null)
            return;

        mProject.ExportPath = mPathInput.Value;
        mProject.ExportFileName = mFileNameInput.Value;
        mProject.ExportSampleRate = SampleRates[Math.Max(0, mSampleRateDropDown.SelectedIndex)];
        mProject.ExportBitDepth = BitDepths[Math.Max(0, mBitDepthDropDown.SelectedIndex)];

        // Save master track settings
        if (mTrackItems.Count > 0 && mTrackItems[0].IsMaster)
        {
            mProject.MasterExportEnabled = mTrackItems[0].CheckBox.IsChecked;
            mProject.MasterExportChannels = mTrackItems[0].GetChannels();
        }
    }

    void OnExportClicked()
    {
        SaveExportConfigToProject();

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

        var selectedTracks = new List<ExportTrackInfo>();
        for (int i = 0; i < mTrackItems.Count; i++)
        {
            if (mTrackItems[i].CheckBox.IsChecked)
            {
                selectedTracks.Add(new ExportTrackInfo
                {
                    TrackIndex = mTrackItems[i].TrackIndex,
                    Channels = mTrackItems[i].GetChannels(),
                });
            }
        }

        if (selectedTracks.Count == 0)
            return;

        ExportRequested?.Invoke(new ExportOptions
        {
            ExportPath = resolvedPath,
            FileName = fileName,
            SelectedTracks = selectedTracks,
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

    TrackItem CreateTrackItem(string name, Color trackColor, bool isMaster, ITrack? track, bool initialEnabled, int initialChannels)
    {
        // Track row: [ColorBar] [TrackName ...] [M/S Segment] [CheckBox]
        var panel = new DockPanel
        {
            Height = 26,
            Margin = new Thickness(0, 1),
        };

        // Checkbox on the right
        var checkBox = new CheckBox();
        checkBox.IsChecked = initialEnabled;
        checkBox.Switched.Subscribe(() =>
        {
            if (isMaster && mProject != null) mProject.MasterExportEnabled = checkBox.IsChecked;
            else if (track != null) track.ExportEnabled = checkBox.IsChecked;
        });
        var checkBoxContainer = new Border
        {
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            Child = checkBox,
        };
        DockPanel.SetDock(checkBoxContainer, Avalonia.Controls.Dock.Right);
        panel.Children.Add(checkBoxContainer);

        // Mono / Stereo switch on the right (before the checkbox).
        // IsChecked == true  => Stereo (OnIcon, right side highlighted)
        // IsChecked == false => Mono   (OffIcon, left  side highlighted)
        bool isStereo = initialChannels >= 2;

        var channelSwitch = new Switch
        {
            OffIcon = Assets.Mono,
            OnIcon = Assets.Stereo,
            Width = 52,
            Height = 16,
        };
        channelSwitch.Display(isStereo);
        channelSwitch.Switched.Subscribe(() =>
        {
            int channels = channelSwitch.IsChecked ? 2 : 1;
            if (isMaster && mProject != null) mProject.MasterExportChannels = channels;
            else if (track != null) track.ExportChannels = channels;
        });

        var switchContainer = new Border
        {
            VerticalAlignment = AvaloniaVerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
            Child = channelSwitch,
        };
        DockPanel.SetDock(switchContainer, Avalonia.Controls.Dock.Right);
        panel.Children.Add(switchContainer);

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

        int trackIndex = isMaster ? -1 : (mProject != null ? mProject.Tracks.ToList().IndexOf(track!) : -1);

        return new TrackItem
        {
            Panel = panel,
            CheckBox = checkBox,
            ChannelSwitch = channelSwitch,
            IsMaster = isMaster,
            TrackIndex = trackIndex,
            Track = track,
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
        public required Switch ChannelSwitch { get; init; } // IsChecked => stereo (2), else mono (1)
        public required bool IsMaster { get; init; }
        public required int TrackIndex { get; init; } // -1 for master
        public required ITrack? Track { get; init; }

        public int GetChannels()
        {
            return ChannelSwitch.IsChecked ? 2 : 1;
        }
    }

    readonly StackPanel mContentPanel = new();
    readonly PathInput mPathInput;
    readonly SingleLineTextController mFileNameInput;
    readonly DropDown mSampleRateDropDown;
    readonly DropDown mBitDepthDropDown;
    readonly StackPanel mTrackListPanel;
    readonly List<TrackItem> mTrackItems = new();

    ProjectDocument? mDocument;
    IProject? mProject;
    bool mIsLoading;

    static readonly int[] SampleRates = [32000, 44100, 48000, 88200, 96000];
    static readonly int[] BitDepths = [16, 24, 32];
}

internal class ExportTrackInfo
{
    public required int TrackIndex { get; init; } // -1 for master
    public required int Channels { get; init; } // 1 = mono, 2 = stereo
}

internal class ExportOptions
{
    public required string ExportPath { get; init; }
    public required string FileName { get; init; }
    public required List<ExportTrackInfo> SelectedTracks { get; init; }
    public required int SampleRate { get; init; }
    public required int BitDepth { get; init; }
}
