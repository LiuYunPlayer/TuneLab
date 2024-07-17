using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Audio;
using TuneLab.Base.Event;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.Data;
using TuneLab.Base.Utils;
using TuneLab.Utils;
using TuneLab.Base.Science;
using TuneLab.Base.Properties;
using Avalonia.Controls.Primitives;
using Slider = TuneLab.GUI.Components.Slider;
using TuneLab.I18N;

namespace TuneLab.UI;

internal class TrackHead : DockPanel
{
    public TrackHead()
    {
        mName.Bind(mTrackProvider.Select(track => track.Name), s);
        mGainSlider.SetRange(-24, 6);
        mGainSlider.Select((double value) => value <= mGainSlider.MinValue ? double.NegativeInfinity : value).Bind(mTrackProvider.Select(track => track.Gain), s);
        mPanSlider.SetRange(-1, 1);
        mPanSlider.Bind(mTrackProvider.Select(track => track.Pan), s);
        mMuteToggle
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 3 }, CheckedColorSet = new() { Color = new(255, 0, 186, 173) }, UncheckedColorSet = new() { Color = Style.BACK } })
            .AddContent(new() { Item = new IconItem() { Icon = Assets.M }, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE } });
        mMuteToggle.Bind(mTrackProvider.Select(track => track.IsMute), s);
        mSoloToggle
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 3 }, CheckedColorSet = new() { Color = new(255, 135, 84, 255) }, UncheckedColorSet = new() { Color = Style.BACK } })
            .AddContent(new() { Item = new IconItem() { Icon = Assets.S }, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE } });
        mSoloToggle.Bind(mTrackProvider.Select(track => track.IsSolo), s);
        mIndexLabel.EndInput.Subscribe(() => { if (Track == null) return; if (!int.TryParse(mIndexLabel.Text, out int newIndex)) mIndexLabel.Text = mTrackIndex.ToString(); newIndex = newIndex.Limit(1, Track.Project.Tracks.Count()); newIndex--; MoveToIndex(newIndex); });
        var leftArea = new DockPanel() { Margin = new(6, 2, 0, 3) };
        {
            leftArea.AddDock(mAmplitudeViewer);
        }
        this.AddDock(leftArea, Dock.Left);
        var rightArea = new DockPanel() { Margin = new(0, 0, 0, 0) };
        {
            mIndexPanel.Children.Add(mIndexLabel);
            rightArea.AddDock(mIndexPanel);
        }
        this.AddDock(rightArea, Dock.Right);
        var topArea = new DockPanel() { Margin = new(12, 12, 12, 0) };
        {
            topArea.AddDock(mSoloToggle, Dock.Right);
            topArea.AddDock(mMuteToggle, Dock.Right);
            topArea.AddDock(mName);
        }
        this.AddDock(topArea, Dock.Top);
        var bottomArea = new DockPanel() { Margin = new(12, 12, 12, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        {
            bottomArea.AddDock(mPanSlider, Dock.Right);
            bottomArea.AddDock(mGainSlider);
        }
        this.AddDock(bottomArea);

        mTrackProvider.When(track => track.Color.Modified).Subscribe(() => { if (Track == null) return; mIndexLabel.Background = Track.GetColor().ToBrush(); mIndexPanel.Background = Track.GetColor().ToBrush(); }, s);
        mTrackProvider.ObjectWillChange.Subscribe(() =>
        {
            if (Track == null)
                return;

            AudioEngine.ProgressChanged -= AudioEngine_ProgressChanged;
            AudioEngine.PlayStateChanged -= AudioEngine_PlayStateChanged;
        }, s);
        mTrackProvider.ObjectChanged.Subscribe(() =>
        {
            if (Track == null)
                return;

            AudioEngine.ProgressChanged += AudioEngine_ProgressChanged;
            AudioEngine.PlayStateChanged += AudioEngine_PlayStateChanged;
        }, s);

        MinWidth = 200;

        var menu = new ContextMenu();
        {
            var menuItem = new MenuItem().SetTrName("Move Up").SetAction(() =>
            {
                var track = Track;
                if (track == null)
                    return;

                var project = track.Project;
                int index = project.Tracks.IndexOf(track);
                if (index == 0)
                    return;

                project.RemoveTrackAt(index);
                project.InsertTrack(index - 1, track);
                project.Commit();
            });
            menu.Items.Add(menuItem);
            menu.Opening += (s, e) =>
            {
                menuItem.IsEnabled = false;
                if (Track == null)
                    return;

                var project = Track.Project;
                int index = project.Tracks.IndexOf(Track);
                if (index == 0)
                    return;

                menuItem.IsEnabled = true;
            };
        }
        {
            var menuItem = new MenuItem().SetTrName("Move Down").SetAction(() =>
            {
                var track = Track;
                if (track == null)
                    return;

                var project = track.Project;
                int index = project.Tracks.IndexOf(track);
                if (index == project.Tracks.Count - 1)
                    return;

                project.RemoveTrackAt(index);
                project.InsertTrack(index + 1, track);
                project.Commit();
            });
            menu.Items.Add(menuItem);
            menu.Opening += (s, e) =>
            {
                menuItem.IsEnabled = false;
                if (Track == null)
                    return;

                var project = Track.Project;
                int index = project.Tracks.IndexOf(Track);
                if (index == project.Tracks.Count - 1)
                    return;

                menuItem.IsEnabled = true;
            };
        }
        {
            var menuItem = new MenuItem().SetTrName("Set Color");
            {
                foreach (var color in Style.TRACK_COLORS.Select(Color.Parse))
                {
                    MenuItem colorItem = new MenuItem
                    {
                        Header = new Border
                        {
                            Background = new SolidColorBrush(color),
                            Width = 20,
                            Height = 20
                        },
                        Tag = color
                    };
                    colorItem.Width = 20;
                    colorItem.Height = 20;
                    colorItem.Margin = new Avalonia.Thickness(5);
                    colorItem.SetAction(() =>
                    {
                        if (Track == null)
                            return;

                        Track.Color.Set(((Color)colorItem.Tag).ToString());
                        Track.Color.Commit();
                    });
                    menuItem.Items.Add(colorItem);
                }
            }
            menu.Items.Add(menuItem);
        }
        {
            var menuItem = new MenuItem().SetTrName("As Refer").SetAction(() =>
            {
                var track = Track;
                if (track == null)
                    return;

                track.AsRefer.Set(!track.AsRefer.GetInfo());
                track.AsRefer.Commit();
            });
            menu.Items.Add(menuItem);
            menu.Opening += (s, e) =>
            {
                if (Track == null)
                    return;

                menuItem.SetName(!Track.AsRefer.GetInfo() ? "Visible as Refer".Tr() : "Hidden as Refer".Tr());
            };
        }
        {
            var menuItem = new MenuItem().SetTrName("Export Audio").SetAction(async () =>
            {
                if (Track == null)
                    return;

                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null)
                    return;

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save File".Tr(TC.Dialog),
                    DefaultExtension = ".wav",
                    SuggestedFileName = Track.Name.Value,
                    ShowOverwritePrompt = true,
                    FileTypeChoices = [new("WAVE File".Tr(TC.Dialog)) { Patterns = ["*.wav"] }]
                });
                var result = file?.TryGetLocalPath();
                if (result == null)
                    return;

                try
                {
                    AudioEngine.ExportTrack(result, Track, false);
                }
                catch (Exception ex)
                {
                    await this.ShowMessage("Error".Tr(TC.Dialog), "Export failed: \n".Tr(TC.Dialog) + ex.Message);
                }
            });
            menu.Items.Add(menuItem);
        }
        {
            var menuItem = new MenuItem().SetTrName("Delete").SetAction(() =>
            {
                if (Track == null)
                    return;

                var project = Track.Project;
                project.RemoveTrack(Track);
                project.Commit();
            });
            menu.Items.Add(menuItem);
        }

        ContextMenu = menu;
        Background = Brushes.Transparent;

        
    }

    ~TrackHead()
    {
        s.DisposeAll();
    }

    private void MoveToIndex(int newIndex)
    {
        if (Track == null) return;
        var track = Track;
        var project = track.Project;
        int index = project.Tracks.IndexOf(track);
        if (index == newIndex) mIndexLabel.Text = mTrackIndex.ToString();
        else
        {
            project.RemoveTrackAt(index);
            project.InsertTrack(newIndex, track);
            project.Commit();
        }
    }

    public void SetTrack(ITrack? track, int index=0)
    {
        mTrackIndex = index;

        mIndexLabel.Text = mTrackIndex.ToString();
        mTrackProvider.Set(track);
        if (Track != null)
        {
            mName.Text = Track.Name.Value;
            mGainSlider.Display(Track.Gain.Value);
            mPanSlider.Display(Track.Pan.Value);
            mMuteToggle.Display(Track.IsMute.Value);
            mSoloToggle.Display(Track.IsSolo.Value);
            mIndexLabel.Background = Track.GetColor().ToBrush();
            mIndexPanel.Background = Track.GetColor().ToBrush();
        }
    }

    private void AudioEngine_PlayStateChanged()
    {
        try
        {
            if (!AudioEngine.IsPlaying)
            {
                mAmplitudeViewer.Reset();
            }
        }
        catch {; }
    }

    private void AudioEngine_ProgressChanged()
    {
        try
        {
            if (Track != null && AudioEngine.IsPlaying)
            {
                AudioEngine.InvokeRealtimeAmplitude(Track, out var amp);
                if (amp == null) mAmplitudeViewer.Reset(); else mAmplitudeViewer.SetValue(amp);
            }
        }
        catch {; }
    }

    Owner<ITrack> mTrackProvider = new();
    ITrack? Track => mTrackProvider.Object;
    int mTrackIndex = -1;

    readonly LayerPanel mIndexPanel = new() { Background = Style.ITEM.ToBrush(), Width = 24, Margin = new(0, 0, 0, 1) };
    readonly EditableLabel mIndexLabel = new() { MinWidth = 16, Foreground = Brushes.Black, CornerRadius = new(0), Padding = new(0), FontSize = 12, VerticalAlignment =Avalonia.Layout.VerticalAlignment.Center,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
    readonly EditableLabel mName = new() { FontSize = 12, CornerRadius = new(0), Padding = new(0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), InputBackground = Style.BACK.ToBrush(), Height = 16 };
    readonly GainSlider mGainSlider = new() { Height = 12 };
    readonly PanSlider mPanSlider = new() { Width = 40, Height = 12, Margin = new(8, 0, 0, 0) };
    readonly Toggle mMuteToggle = new() { Width = 16, Height = 16, Margin = new(8, 0, 0, 0) };
    readonly Toggle mSoloToggle = new() { Width = 16, Height = 16, Margin = new(8, 0, 0, 0) };
    readonly DisposableManager s = new();
    readonly StereoAmplitudeViewer mAmplitudeViewer = new();
}
