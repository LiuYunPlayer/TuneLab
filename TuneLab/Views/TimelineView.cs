using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using NAudio.CoreAudioApi;
using System;
using TuneLab.Audio;
using TuneLab.Base.Event;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.Animation;
using TuneLab.Data;
using TuneLab.Base.Science;
using TuneLab.Utils;
using TuneLab.Base.Utils;

namespace TuneLab.Views;

internal partial class TimelineView : View
{
    public interface IDependency
    {
        TickAxis TickAxis { get; }
        IQuantization Quantization { get; }
        IProvider<ITimeline> TimelineProvider { get; }
        IPlayhead Playhead { get; }
        bool IsAutoPage { get; }
        void EnterInputBpm(ITempo tempo);
    }

    public TimelineView(IDependency dependency)
    {
        mDependency = dependency;

        mMiddleDragOperation = new(this);
        mSeekOperation = new(this);
        mTempoMovingOperation = new(this);

        Height = 48;

        mDependency.TimelineProvider.ObjectChanged.Subscribe(OnTickAxisChanged, s);
        mDependency.Playhead.PosChanged.Subscribe(() =>
        {
            if (AudioEngine.IsPlaying)
            {
                if (!mDependency.IsAutoPage)
                    return;

                if (Timeline == null)
                    return;

                if (TickAxis.IsMoveAnimating)
                    return;

                const double autoPageTime = 500;
                var minVisibeTime = Timeline.TempoManager.GetTime(TickAxis.MinVisibleTick);
                var maxVisibeTime = Timeline.TempoManager.GetTime(TickAxis.MaxVisibleTick);

                var time = Timeline.TempoManager.GetTime(Playhead.Pos);
                if (time + autoPageTime / 1000 >= maxVisibeTime)
                {
                    TickAxis.AnimateMoveTickToX(TickAxis.MaxVisibleTick, 0, autoPageTime, mPageCurve);
                }
            }
            else
            {
                if (mState == State.Seeking)
                    return;

                if (Playhead.Pos < TickAxis.MinVisibleTick)
                {
                    TickAxis.MoveTickToX(Playhead.Pos, 0);
                }
                if (Playhead.Pos > TickAxis.MaxVisibleTick)
                {
                    TickAxis.MoveTickToX(Playhead.Pos, TickAxis.ViewLength);
                }
            }
        }, s);
        TickAxis.AxisChanged += Update;
        Quantization.QuantizationChanged += InvalidateVisual;
        mDependency.TimelineProvider.When(timeline => timeline.TempoManager.Modified).Subscribe(InvalidateVisual, s);
        // TODO: Subscribe TimeSignatureManger like TempoManager.
    }

    ~TimelineView()
    {
        s.DisposeAll();
        TickAxis.AxisChanged -= Update;
        Quantization.QuantizationChanged -= InvalidateVisual;
    }

    void OnTickAxisChanged()
    {
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        IBrush barLineBrush = new Color(178, 255, 255, 255).ToBrush();
        IBrush textBrush = new Color(178, 255, 255, 255).ToBrush();

        context.FillRectangle(Back.ToBrush(), this.Rect());

        if (Timeline == null)
            return;

        double startPos = TickAxis.X2Tick(-48);
        double endPos = TickAxis.MaxVisibleTick;

        // draw time signatures
        var timeSignatureManager = Timeline.TimeSignatureManager;

        var startMeter = timeSignatureManager.GetMeterStatus(startPos);
        var endMeter = timeSignatureManager.GetMeterStatus(endPos);

        int startIndex = startMeter.TimeSignatureIndex;
        int endIndex = endMeter.TimeSignatureIndex;

        var timeSignatures = timeSignatureManager.TimeSignatures;
        for (int timeSignatureIndex = startIndex; timeSignatureIndex <= endIndex; timeSignatureIndex++)
        {
            int nextTimeSignatureBarIndex = timeSignatureIndex + 1 == timeSignatures.Count ? (int)Math.Ceiling(endMeter.BarIndex) : timeSignatures[timeSignatureIndex + 1].BarIndex;
            int thisTimeSignatureBarIndex = Math.Max(timeSignatures[timeSignatureIndex].BarIndex, (int)Math.Floor(startMeter.BarIndex));
            double pixelsPerBeat = TickAxis.PixelsPerTick * timeSignatures[timeSignatureIndex].TicksPerBeat();
            double beatOpacity = MathUtility.LineValue(12, 0, 24, 1, pixelsPerBeat).Limit(0, 1);
            IBrush beatLineBrush = new Color(127, 255, 255, 255).Opacity(beatOpacity).ToBrush();
            for (int barIndex = thisTimeSignatureBarIndex; barIndex < nextTimeSignatureBarIndex; barIndex++)
            {
                double xBarIndex = TickAxis.Tick2X(timeSignatures[timeSignatureIndex].GetTickByBarIndex(barIndex));
                context.FillRectangle(barLineBrush, new Rect(xBarIndex, 0, 1, 12));
                context.DrawText(new FormattedText((barIndex + 1).ToString(), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, textBrush), new Point(xBarIndex + 8, 8));
                // draw beat
                if (beatOpacity == 0)
                    continue;

                for (int beatIndex = 1; beatIndex < timeSignatures[timeSignatureIndex].Numerator; beatIndex++)
                {
                    double xBeatIndex = TickAxis.Tick2X(timeSignatures[timeSignatureIndex].GetTickByBarAndBeat(barIndex, beatIndex));
                    context.FillRectangle(beatLineBrush, new Rect(xBeatIndex, 0, 1, 8));
                }
            }
        }

        // draw tempos
        foreach (var item in Items)
        {
            if (item is TempoItem tempoItem)
            {
                var tempo = tempoItem.Tempo;
                if (mState == State.TempoMoving && tempo == mTempoMovingOperation.Tempo)
                {
                    context.FillRectangle(Style.BACK.ToBrush(), tempoItem.Rect());
                }

                context.DrawString(BpmString(tempo), tempoItem.Rect(), textBrush, 12, Alignment.Center, Alignment.Center);
            }
        }
    }

    public string BpmString(ITempo tempo)
    {
        return tempo.Bpm.Value.ToString("F2");
    }

    public double TempoWidth(ITempo tempo)
    {
        return new FormattedText(BpmString(tempo), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, null).Width + 16;
    }

    double QuantizedCellTicks()
    {
        int quantizationBase = (int)Quantization.Base;
        double division = (int)Math.Pow(2, Math.Log2(TickAxis.PixelsPerTick * MusicTheory.RESOLUTION / quantizationBase / 12).Floor()).Limit(1, 32);
        return MusicTheory.RESOLUTION / quantizationBase / division;
    }

    double GetQuantizedTick(double tick)
    {
        double cell = QuantizedCellTicks();
        return (tick / cell).Round() * cell;
    }

    class PageCurve : IAnimationCurve
    {
        public double GetRatio(double timeRatio)
        {
            var c = Math.Cos(AnimationCurve.CubicOut.GetRatio(timeRatio) * Math.PI);
            return (c < 0) ? 0.5 * (1 + Math.Pow(-c, 1 / 3.0)) : 0.5 * (1 - Math.Pow(c, 1 / 3.0));
        }
    }

    readonly static PageCurve mPageCurve = new();

    Color Back => GUI.Style.DARK;

    TickAxis TickAxis => mDependency.TickAxis;
    IQuantization Quantization => mDependency.Quantization;
    IPlayhead Playhead => mDependency.Playhead;
    ITimeline? Timeline => mDependency.TimelineProvider.Object;

    readonly IDependency mDependency;
    readonly DisposableManager s = new();
}
