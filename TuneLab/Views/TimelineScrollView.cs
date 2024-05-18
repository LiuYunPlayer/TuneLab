using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DynamicData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.Base.Science;
using TuneLab.Data;
using TuneLab.GUI.Components;

namespace TuneLab.Views;

internal class TimelineScrollView : Panel, TimelineView.IDependency
{
    public TimelineView TimelineView => mTimelineView;
    public TickAxis TickAxis => mDependency.TickAxis;
    public IQuantization Quantization => mDependency.Quantization;
    public IProvider<ITimeline> TimelineProvider => mDependency.TimelineProvider;
    public IPlayhead Playhead => mDependency.Playhead;
    public bool IsAutoPage => mDependency.IsAutoPage;

    public interface IDependency
    {
        TickAxis TickAxis { get; }
        IQuantization Quantization { get; }
        IProvider<ITimeline> TimelineProvider { get; }
        IPlayhead Playhead { get; }
        bool IsAutoPage { get; }
    }

    public TimelineScrollView(IDependency dependency)
    {
        mDependency = dependency;

        mTimelineView = new(this);
        Children.Add(mTimelineView);

        mBpmInput = new TextInput()
        {
            Padding = new Thickness(8, 4),
            Background = Brushes.White,
            Foreground = Brushes.Black,
            BorderThickness = new(0),
            FontSize = 12,
            CaretBrush = Brushes.Black,
            MinWidth = 24,
            CornerRadius = new CornerRadius(0),
            IsVisible = false
        };
        mBpmInput.EndInput.Subscribe(OnBpmInputComplete);
        Children.Add(mBpmInput);

        Height = 48;
        ClipToBounds = true;

        TickAxis.AxisChanged += InvalidateArrange;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        mTimelineView.Arrange(new Rect(finalSize));
        if (mBpmInput.IsVisible)
            mBpmInput.Arrange(BpmInputRect());

        return finalSize;
    }

    public void EnterInputBpm(ITempo tempo)
    {
        if (mInputBpmTempo != null)
            return;

        mInputBpmTempo = tempo;
        mBpmInput.Text = mTimelineView.BpmString(tempo);
        mBpmInput.IsVisible = true;
        mBpmInput.Focus();
        mBpmInput.SelectAll();
    }

    void OnBpmInputComplete()
    {
        if (mInputBpmTempo == null)
            return;

        if (Timeline == null)
            return;

        if (!double.TryParse(mBpmInput.Text, out var newBpm))
        {
            newBpm = mInputBpmTempo.Bpm.Value;
        }
        newBpm = newBpm.Limit(10, 960);
        if (newBpm != mInputBpmTempo.Bpm.Value)
        {
            Timeline.TempoManager.SetBpm(mInputBpmTempo, newBpm);
            mInputBpmTempo.Commit();
        }

        mBpmInput.IsVisible = false;
        mInputBpmTempo = null;
    }

    Rect BpmInputRect()
    {
        if (mInputBpmTempo == null)
            return new Rect();

        return new Rect(mDependency.TickAxis.Tick2X(mInputBpmTempo.Pos.Value), 24, 54, 24);
    }

    ITempo? mInputBpmTempo;
    ITimeline? Timeline => mDependency.TimelineProvider.Object;

    readonly TimelineView mTimelineView;
    readonly TextInput mBpmInput;

    readonly IDependency mDependency;
}
