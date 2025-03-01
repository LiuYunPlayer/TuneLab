using Avalonia;
using Avalonia.Media;
using System;
using TuneLab.Audio;
using TuneLab.GUI.Components;
using TuneLab.Animation;
using TuneLab.Data;
using TuneLab.Utils;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.Science;
using TuneLab.Foundation.Utils;

namespace TuneLab.UI;

internal partial class TimelineView : View
{
    public interface IDependency
    {
        TickAxis TickAxis { get; }
        IQuantization Quantization { get; }
        IProvider<ITimeline> TimelineProvider { get; }
        IPlayhead Playhead { get; }
        INotifiableProperty<PlayScrollTarget> PlayScrollTarget { get; }
    }

    public TimelineView(IDependency dependency)
    {
        mDependency = dependency;

        mMiddleDragOperation = new(this);
        mSeekOperation = new(this);
        mTempoMovingOperation = new(this);
        mTimeSignatureMovingOperation = new(this);
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

        mMeterInput = new TextInput()
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
        mMeterInput.EndInput.Subscribe(OnMeterInputComplete);
        Children.Add(mMeterInput);

        Height = 48;
        ClipToBounds = true;

        TickAxis.AxisChanged += InvalidateArrange;

        mDependency.PlayScrollTarget.Modified.Subscribe(() => mFixedPlayheadX = TickAxis.Tick2X(Playhead.Pos));
        mDependency.TimelineProvider.ObjectChanged.Subscribe(OnTickAxisChanged, s);
        mDependency.Playhead.PosChanged.Subscribe(() =>
        {
            if (AudioEngine.IsPlaying)
            {
                // Auto Page
                if (mDependency.PlayScrollTarget.Value == PlayScrollTarget.None)
                    return;

                if (Timeline == null)
                    return;

                if (mDependency.PlayScrollTarget.Value == PlayScrollTarget.View)
                {
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
                    TickAxis.MoveTickToX(Playhead.Pos, mFixedPlayheadX);
                }           
            }
            else
            {
                mFixedPlayheadX = TickAxis.Tick2X(Playhead.Pos);
                if (!mIsSeeking)
                    return;

                if (mState == State.Seeking)
                    return;

                // Move view
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
        mDependency.TimelineProvider.When(timeline => timeline.TimeSignatureManager.Modified).Subscribe(InvalidateVisual, s);
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
                if (barIndex != thisTimeSignatureBarIndex)
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
    }

    public string BpmString(ITempo tempo)
    {
        return tempo.Bpm.ToString("F2");
    }

    public double TempoWidth(ITempo tempo)
    {
        return new FormattedText(BpmString(tempo), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, null).Width + 16;
    }

    public string MeterString(ITimeSignature timeSignature)
    {
        return $"{timeSignature.Numerator}/{timeSignature.Denominator}";
    }

    public double TimeSignatureWidth(ITimeSignature timeSignature)
    {
        return new FormattedText(MeterString(timeSignature), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, null).Width + 16;
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

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (mBpmInput.IsVisible)
            mBpmInput.Arrange(BpmInputRect());

        if (mMeterInput.IsVisible)
            mMeterInput.Arrange(MeterInputRect());

        return finalSize;
    }

    public void EnterInputBpm(ITempo tempo)
    {
        if (mInputBpmTempo != null)
            return;

        mInputBpmTempo = tempo;
        mBpmInput.Display(BpmString(tempo));
        mBpmInput.IsVisible = true;
        mBpmInput.Focus();
        mBpmInput.SelectAll();
    }

    public void EnterInputMeter(ITimeSignature timeSignature)
    {
        if (mInputMeterTimeSignature != null)
            return;

        mInputMeterTimeSignature = timeSignature;
        mMeterInput.Display(MeterString(timeSignature));
        mMeterInput.IsVisible = true;
        mMeterInput.Focus();
        mMeterInput.SelectAll();
    }

    void OnBpmInputComplete()
    {
        if (mInputBpmTempo == null)
            return;

        if (Timeline == null)
            return;

        if (!double.TryParse(mBpmInput.Text, out var newBpm))
        {
            newBpm = mInputBpmTempo.Bpm;
        }
        newBpm = newBpm.Limit(10, 960);
        if (newBpm != mInputBpmTempo.Bpm)
        {
            Timeline.TempoManager.SetBpm(mInputBpmTempo, newBpm);
            mInputBpmTempo.Commit();
        }

        mBpmInput.IsVisible = false;
        mInputBpmTempo = null;
    }

    void OnMeterInputComplete()
    {
        if (mInputMeterTimeSignature == null)
            return;

        if (Timeline == null)
            return;

        var numbers = mMeterInput.Text.Split('/');
        if (numbers.Length != 2 || !int.TryParse(numbers[0], out var numerator) || !int.TryParse(numbers[1], out var denominator))
        {
            numerator = mInputMeterTimeSignature.Numerator;
            denominator = mInputMeterTimeSignature.Denominator;
        }
        numerator = numerator.Limit(1, 16);
        denominator = denominator >= 16 ? 16 : denominator >= 8 ? 8 : denominator >= 4 ? 4 : denominator >= 2 ? 2 : 1;
        if (numerator != mInputMeterTimeSignature.Numerator || denominator != mInputMeterTimeSignature.Denominator)
        {
            Timeline.TimeSignatureManager.SetMeter(mInputMeterTimeSignature, numerator, denominator);
            mInputMeterTimeSignature.Commit();
        }

        mMeterInput.IsVisible = false;
        mInputMeterTimeSignature = null;
    }

    Rect BpmInputRect()
    {
        if (mInputBpmTempo == null)
            return new Rect();

        return new Rect(mDependency.TickAxis.Tick2X(mInputBpmTempo.Pos), 24, 54, 24);
    }

    Rect MeterInputRect()
    {
        if (mInputMeterTimeSignature == null)
            return new Rect();

        return new Rect(mDependency.TickAxis.Tick2X(mInputMeterTimeSignature.Pos), 0, 54, 24);
    }

    ITempo? mInputBpmTempo;
    ITimeSignature? mInputMeterTimeSignature;
    ITimeline? Timeline => mDependency.TimelineProvider.Object;

    readonly TextInput mBpmInput;
    readonly TextInput mMeterInput;

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
    double mFixedPlayheadX;

    readonly IDependency mDependency;
    readonly DisposableManager s = new();
}
