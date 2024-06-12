using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Base.Event;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.Data;
using TuneLab.Base.Utils;
using TuneLab.Utils;
using Slider = TuneLab.GUI.Components.Slider;

namespace TuneLab.Views;

internal class TrackHead : DockPanel
{
    public TrackHead()
    {
        mName.EndInput.Subscribe(() => { if (Track == null) return; Track.Name.Set(mName.Text); Track.Name.Commit(); });
        mGainSlider.SetRange(-24, 6);
        mGainSlider.ValueChanged.Subscribe(() => { if (Track == null) return; var value = mGainSlider.Value; Track.Gain.Discard(); Track.Gain.Set(value); });
        mGainSlider.ValueCommited.Subscribe(() => { if (Track == null) return; var value = mGainSlider.Value; Track.Gain.Discard(); Track.Gain.Set(value); Track.Gain.Commit(); });
        mPanSlider.SetRange(-1, 1);
        mPanSlider.ValueChanged.Subscribe(() => { if (Track == null) return; var value = mPanSlider.Value; Track.Pan.Discard(); Track.Pan.Set(value); });
        mPanSlider.ValueCommited.Subscribe(() => { if (Track == null) return; var value = mPanSlider.Value; Track.Pan.Discard(); Track.Pan.Set(value); Track.Pan.Commit(); });
        mMuteToggle
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 3 }, CheckedColorSet = new() { Color = new(255, 0, 186, 173) }, UncheckedColorSet = new() { Color = Style.BACK } })
            .AddContent(new() { Item = new IconItem() { Icon = GUI.Assets.M }, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE } });
        mMuteToggle.Switched += () => { if (Track == null) return; Track.IsMute.Set(mMuteToggle.IsChecked); Track.IsMute.Commit(); };
        mSoloToggle
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 3 }, CheckedColorSet = new() { Color = new(255, 135, 84, 255) }, UncheckedColorSet = new() { Color = Style.BACK } })
            .AddContent(new() { Item = new IconItem() { Icon = GUI.Assets.S }, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE } });
        mSoloToggle.Switched += () => { if (Track == null) return; Track.IsSolo.Set(mSoloToggle.IsChecked); Track.IsSolo.Commit(); };

        var topArea = new DockPanel() { Margin = new(12, 12, 12, 0) };
        {
            topArea.AddDock(mBorder, Dock.Left);
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

        mTrackProvider.When(track => track.Name.Modified).Subscribe(() => { if (Track == null) return; mName.Text = Track.Name.Value; }, s);
        mTrackProvider.When(track => track.Gain.Modified).Subscribe(() => { if (Track == null) return; mGainSlider.Display(Track.Gain.Value); }, s);
        mTrackProvider.When(track => track.Pan.Modified).Subscribe(() => { if (Track == null) return; mPanSlider.Display(Track.Pan.Value); }, s);
        mTrackProvider.When(track => track.IsMute.Modified).Subscribe(() => { if (Track == null) return; mMuteToggle.Display(Track.IsMute.Value); }, s);
        mTrackProvider.When(track => track.IsSolo.Modified).Subscribe(() => { if (Track == null) return; mSoloToggle.Display(Track.IsSolo.Value); }, s);

        MinWidth = 200;

        var menu = new ContextMenu();
        {
            var menuItem = new MenuItem().SetName("Export Audio").SetAction(async () =>
            {
                if (Track == null)
                    return;

                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null)
                    return;

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save File",
                    DefaultExtension = ".wav",
                    SuggestedFileName = Track.Name.Value,
                    ShowOverwritePrompt = true,
                    FileTypeChoices = [new("WAVE File") { Patterns = ["*.wav"] }]
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
                    await this.ShowMessage("Error", "Export failed: \n" + ex.Message);
                }
            });
            menu.Items.Add(menuItem);
        }
        {
            var menuItem = new MenuItem().SetName("Delete").SetAction(() =>
            {
                if (Track == null)
                    return;

                var project = Track.Project;
                project.RemoveTrack(Track);
                project.Commit();
            }).SetInputGesture(new KeyGesture(Key.Delete));
            menu.Items.Add(menuItem);
        }

        ContextMenu = menu;
        Background = Brushes.Transparent;
    }

    ~TrackHead()
    {
        s.DisposeAll();
    }

    public void SetTrack(ITrack? track)
    {
        mTrackProvider.Set(track);
        if (Track != null)
        {
            mName.Text = Track.Name.Value;
            mGainSlider.Display(Track.Gain.Value);
            mPanSlider.Display(Track.Pan.Value);
            mMuteToggle.Display(Track.IsMute.Value);
            mSoloToggle.Display(Track.IsSolo.Value);
        }
    }

    Owner<ITrack> mTrackProvider = new();
    ITrack? Track => mTrackProvider.Object;

    readonly Border mBorder = new() { Background = Style.ITEM.ToBrush(), Width = 16, Height = 16, Margin = new(0, 0, 12, 0) };
    readonly EditableLabel mName= new() { FontSize = 12, CornerRadius = new(0), Padding = new(0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush(), Background = Style.INTERFACE.ToBrush(), InputBackground = Style.BACK.ToBrush(), Height = 16 };
    readonly GainSlider mGainSlider = new() { Height = 12 };
    readonly PanSlider mPanSlider = new() { Width = 40, Height = 12, Margin = new(8, 0, 0, 0) };
    readonly Toggle mMuteToggle = new() { Width = 16, Height = 16, Margin = new(8, 0, 0, 0) };
    readonly Toggle mSoloToggle = new() { Width = 16, Height = 16, Margin = new(8, 0, 0, 0) };
    readonly DisposableManager s = new();
}
