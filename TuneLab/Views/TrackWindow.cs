using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.GUI.Components;
using TuneLab.GUI.Input;
using TuneLab.Data;
using TuneLab.Base.Science;
using TuneLab.GUI;
using TuneLab.Utils;
using TuneLab.Extensions.Formats.DataInfo;

namespace TuneLab.Views;

internal class TrackWindow : DockPanel, TimelineView.IDependency, TrackScrollView.IDependency, PlayheadLayer.IDependency, TrackVerticalAxis.IDependency, TrackHeadList.IDependency
{
    public IProvider<IProject> ProjectProvider => mDependency.ProjectProvider;
    public IProvider<ITimeline> TimelineProvider => mDependency.ProjectProvider;
    public IQuantization Quantization => mQuantization;
    public TickAxis TickAxis => mTickAxis;
    public TrackVerticalAxis TrackVerticalAxis => mTrackVerticalAxis;
    public IPlayhead Playhead => mDependency.Playhead;
    public IProvider<Part> EditingPart => mDependency.EditingPart;
    public IProject? Project => ProjectProvider.Object;
    public TrackScrollView TrackScrollView => mTrackScrollView;
    public bool IsAutoPage => mDependency.IsAutoPage;

    public interface IDependency
    {
        IProvider<IProject> ProjectProvider { get; }
        IPlayhead Playhead { get; }
        IProvider<Part> EditingPart { get; }
        void SwitchEditingPart(IPart? part);
        bool IsAutoPage { get; }
    }

    public TrackWindow(IDependency dependency)
    {
        mDependency = dependency;

        mQuantization = new(MusicTheory.QuantizationBase.Base_1, MusicTheory.QuantizationDivision.Division_8);
        mTickAxis = new();
        mTrackVerticalAxis = new(this);

        mTrackHeadList = new(this) { Width = 232, Margin = new(1, 0) };
        var headArea = new DockPanel();
        {
            var title = new DockPanel() { Height = 48, Background = Style.INTERFACE.ToBrush(), Margin = new(1, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            {
                var border = new Border() { Height = 1, Background = Style.BACK.ToBrush() };
                title.AddDock(border, Dock.Bottom);
                var icon = new Image() { Source = Assets.Track.GetImage(Style.LIGHT_WHITE), Width = 24, Height = 24, Margin = new(16, 12, 12, 12) };
                title.AddDock(icon, Dock.Left);
                var name = new Label() { Content = "Track", FontSize = 16, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.TEXT_LIGHT.ToBrush() };
                title.AddDock(name);
            }
            headArea.AddDock(title, Dock.Top);

            headArea.AddDock(mTrackHeadList);
        }
        this.AddDock(headArea, Dock.Right);

        var layerPanel = new LayerPanel();
        {
            var layer = new DockPanel();
            {
                mTimelineView = new(this) { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
                layer.AddDock(mTimelineView, Dock.Top);

                mTrackScrollView = new(this);
                layer.AddDock(mTrackScrollView);
            }
            layerPanel.Children.Add(layer);

            mPlayheadLayer = new(this);
            layerPanel.Children.Add(mPlayheadLayer);
        }
        this.AddDock(layerPanel);

        TickAxis.ScaleLevel = -8;
    }

    public void SwitchEditingPart(IPart part)
    {
        mDependency.SwitchEditingPart(part);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        mTickAxis.ViewLength = e.NewSize.Width - mTrackHeadList.Width;
        mTrackVerticalAxis.ViewLength = e.NewSize.Height - mTimelineView.Height;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.IsHandledByTextBox())
            return;

        switch (TrackScrollView.OperationState)
        {
            case TrackScrollView.State.None:
                e.Handled = true;
                if (e.Match(Key.Delete))
                {
                    TrackScrollView.DeleteAllSelectedParts();
                }
                else if (e.Match(Key.C, ModifierKeys.Ctrl))
                {
                    TrackScrollView.Copy();
                }
                else if (e.Match(Key.X, ModifierKeys.Ctrl))
                {
                    TrackScrollView.Cut();
                }
                else if (e.Match(Key.V, ModifierKeys.Ctrl))
                {
                    TrackScrollView.PasteAt(TrackScrollView.GetQuantizedTick(Playhead.Pos));
                }
                else if (e.Match(Key.A, ModifierKeys.Ctrl))
                {
                    Project?.Tracks.SelectMany(track => track.Parts).SelectAllItems();
                }
                else
                {
                    e.Handled = false;
                }
                break;
        } 
    }

    readonly Quantization mQuantization;
    readonly TickAxis mTickAxis;
    readonly TrackVerticalAxis mTrackVerticalAxis;

    readonly TrackHeadList mTrackHeadList;
    readonly TimelineView mTimelineView;
    readonly TrackScrollView mTrackScrollView;
    readonly PlayheadLayer mPlayheadLayer;

    readonly IDependency mDependency;
}
