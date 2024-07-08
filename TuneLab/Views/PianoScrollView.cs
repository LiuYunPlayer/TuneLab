using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using DynamicData;
using TuneLab.Audio;
using TuneLab.Base.Event;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Input;
using TuneLab.Data;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Extensions.Voices;
using TuneLab.Utils;
using TuneLab.Base.Science;
using TuneLab.Base.Utils;

namespace TuneLab.Views;

internal partial class PianoScrollView : View, IPianoScrollView
{
    public interface IDependency
    {
        IActionEvent PianoToolChanged { get; }
        PianoTool PianoTool { get; }
        IPlayhead Playhead { get; }
        TickAxis TickAxis { get; }
        PitchAxis PitchAxis { get; }
        IQuantization Quantization { get; }
        IProvider<MidiPart> PartProvider { get; }
        ParameterButton PitchButton { get; }
        double WaveformBottom { get; }
        IActionEvent WaveformBottomChanged { get; }
    }

    public bool CanPaste => mDependency.PianoTool switch 
    { 
        PianoTool.Note => !mNoteClipboard.IsEmpty(), 
        PianoTool.Vibrato => !mVibratoClipboard.IsEmpty(),
        _ => false 
    };
    public State OperationState => mState;

    public PianoScrollView(IDependency dependency)
    {
        mDependency = dependency;

        mMiddleDragOperation = new(this);
        mNoteSelectOperation = new(this);
        mPitchDrawOperation = new(this);
        mPitchClearOperation = new(this);
        mPitchLockOperation = new(this);
        mNoteMoveOperation = new(this); 
        mNoteStartResizeOperation = new(this);
        mNoteEndResizeOperation = new(this);
        mVibratoSelectOperation = new(this);
        mVibratoStartResizeOperation = new(this);
        mVibratoEndResizeOperation = new(this);
        mVibratoAmplitudeOperation = new(this);
        mVibratoFrequencyOperation = new(this);
        mVibratoPhaseOperation = new(this);
        mVibratoAttackOperation = new(this);
        mVibratoReleaseOperation = new(this);
        mVibratoMoveOperation = new(this);
        mWaveformNoteResizeOperation = new(this);
        mWaveformPhonemeResizeOperation = new(this);
        mSelectionOperation = new(this);
        mAnchorSelectOperation = new(this);
        mAnchorDeleteOperation = new(this);
        mAnchorMoveOperation = new(this);

        mDependency.PartProvider.ObjectChanged.Subscribe(Update, s);
        mDependency.PartProvider.When(p => p.Modified).Subscribe(Update, s);
        mDependency.PartProvider.When(p => p.SynthesisStatusChanged).Subscribe(OnSynthesisStatusChanged, s);
        mDependency.PartProvider.When(p => p.Notes.SelectionChanged).Subscribe(InvalidateVisual, s);
        mDependency.PartProvider.When(p => p.Vibratos.Any(vibrato => vibrato.SelectionChanged)).Subscribe(InvalidateVisual, s);
        mDependency.PartProvider.When(p => p.Pitch.Modified).Subscribe(InvalidateVisual, s); 
        mDependency.PartProvider.When(p => p.Track.Project.Tracks.Any(track => track.AsRefer.Modified)).Subscribe(InvalidateVisual, s);
        mDependency.PartProvider.When(p => p.Track.Project.Tracks.Any(track => track.Color.Modified)).Subscribe(InvalidateVisual, s);
        mDependency.WaveformBottomChanged.Subscribe(InvalidateVisual, s);
        mDependency.PianoToolChanged.Subscribe(InvalidateVisual);
        TickAxis.AxisChanged += Update;
        PitchAxis.AxisChanged += Update;
        Quantization.QuantizationChanged += InvalidateVisual;
        PitchButton.StateChanged += InvalidateVisual;

        mLyricInput = new TextInput()
        {
            Padding = new Thickness(8, LyricInputVerticalPadding),
            Background = Brushes.White,
            Foreground = Brushes.Black,
            BorderThickness = new(0),
            FontSize = LyricInputFontSize,
            CaretBrush = Brushes.Black,
            IsVisible = false
        };
        mLyricInput.EndInput.Subscribe(OnLyricInputComplete);
        Children.Add(mLyricInput);

        ClipToBounds = true;

        TickAxis.AxisChanged += InvalidateArrange;
        PitchAxis.AxisChanged += InvalidateArrange;
    }

    ~PianoScrollView()
    {
        s.DisposeAll();
        mDependency.PianoToolChanged.Unsubscribe(InvalidateVisual);
        TickAxis.AxisChanged -= Update;
        PitchAxis.AxisChanged -= Update;
        Quantization.QuantizationChanged -= InvalidateVisual;
        PitchButton.StateChanged -= InvalidateVisual;
    }

    void OnSynthesisStatusChanged(ISynthesisPiece piece)
    {
        if (Part == null)
            return;

        var tempoManager = Part.TempoManager;
        double startTime = tempoManager.GetTime(TickAxis.MinVisibleTick);
        double endTime = tempoManager.GetTime(TickAxis.MaxVisibleTick);
        if (piece.StartTime() > endTime || piece.EndTime() < startTime)
            return;

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        context.FillRectangle(WhiteKeyColor.ToBrush(), this.Rect());

        IBrush blackKeyBrush = BlackKeyColor.ToBrush();
        int minBlack = (int)Math.Floor(PitchAxis.MinVisiblePitch);
        int maxBlack = (int)Math.Ceiling(PitchAxis.MaxVisiblePitch);
        for (int i = minBlack; i < maxBlack; i++)
        {
            if (MusicTheory.IsBlack(i))
            {
                double top = PitchAxis.Pitch2Y(i + 1);
                double bottom = PitchAxis.Pitch2Y(i);
                context.FillRectangle(blackKeyBrush, new Rect(0, top, Bounds.Width, bottom - top));
            }
            else if (MusicTheory.IsEorB(i))
            {
                double top = PitchAxis.Pitch2Y(i + 1) - 0.5f;
                context.FillRectangle(blackKeyBrush, new Rect(0, top, Bounds.Width, 1));
            }
        }

        if (Part == null)
            return;

        var timeSignatureManager = Part.TimeSignatureManager;

        double minVisibleTick = TickAxis.MinVisibleTick;
        double maxVisibleTick = TickAxis.MaxVisibleTick;

        var startMeter = timeSignatureManager.GetMeterStatus(minVisibleTick);
        var endMeter = timeSignatureManager.GetMeterStatus(maxVisibleTick);

        int startIndex = startMeter.TimeSignatureIndex;
        int endIndex = endMeter.TimeSignatureIndex;

        var timeSignatures = timeSignatureManager.TimeSignatures;
        IBrush lineBrush = LineColor.ToBrush();
        for (int i = startIndex; i <= endIndex; i++)
        {
            // draw bar
            int nextTimeSignatureBarIndex = i + 1 == timeSignatures.Count ? (int)Math.Ceiling(endMeter.BarIndex) : timeSignatures[i + 1].BarIndex;
            int thisTimeSignatureBarIndex = Math.Max(timeSignatures[i].BarIndex, (int)Math.Floor(startMeter.BarIndex));
            for (int barIndex = thisTimeSignatureBarIndex; barIndex < nextTimeSignatureBarIndex; barIndex++)
            {
                double xBarIndex = TickAxis.Tick2X(timeSignatures[i].GetTickByBarIndex(barIndex));
                context.FillRectangle(lineBrush, new Rect(xBarIndex, 0, 1, Bounds.Height));
            }

            // draw beat
            double pixelsPerBeat = timeSignatures[i].TicksPerBeat() * TickAxis.PixelsPerTick;
            double beatOpacity = MathUtility.LineValue(6, 0, 12, 1, pixelsPerBeat).Limit(0, 1);
            if (beatOpacity == 0)
                continue;

            IPen beatLinePen = new Pen(LineColor.Opacity(beatOpacity).ToUInt32(), LineWidth, new DashStyle(new double[] { PitchAxis.KeyHeight * 0.4 / LineWidth, PitchAxis.KeyHeight * 0.6 / LineWidth }, 0));
            for (int barIndex = thisTimeSignatureBarIndex; barIndex < nextTimeSignatureBarIndex; barIndex++)
            {
                for (int beatIndex = 1; beatIndex < timeSignatures[i].Numerator; beatIndex++)
                {
                    double xBeatIndex = TickAxis.Tick2X(timeSignatures[i].GetTickByBarAndBeat(barIndex, beatIndex));
                    double x = xBeatIndex + LineWidth / 2;
                    context.DrawLine(beatLinePen, new Point(x, PitchAxis.Pitch2Y(maxBlack - 0.3)), new Point(x, Bounds.Height));
                }
            }

            // draw quantization
            int quantizationBase = (int)Quantization.Base;
            int ticksPerBase = timeSignatures[i].TicksPerBeat() / quantizationBase;
            double pixelsPerBase = ticksPerBase * TickAxis.PixelsPerTick;
            double baseOpacity = MathUtility.LineValue(MIN_GRID_GAP, 0, MIN_REALITY_GRID_GAP, 1, pixelsPerBase).Limit(0, 1);
            if (baseOpacity == 0)
                continue;

            IPen baseLinePen = new Pen(LineColor.Opacity(baseOpacity).ToUInt32(), LineWidth, new DashStyle(new double[] { PitchAxis.KeyHeight * 0.2 / LineWidth, PitchAxis.KeyHeight * 0.8 / LineWidth }, 0));
            for (int barIndex = thisTimeSignatureBarIndex; barIndex < nextTimeSignatureBarIndex; barIndex++)
            {
                for (int beatIndex = 0; beatIndex < timeSignatures[i].Numerator; beatIndex++)
                {
                    double beatPos = timeSignatures[i].GetTickByBarAndBeat(barIndex, beatIndex);
                    for (int baseIndex = 1; baseIndex < quantizationBase; baseIndex++)
                    {
                        double xBase = TickAxis.Tick2X(beatPos + baseIndex * ticksPerBase);
                        double x = xBase + LineWidth / 2;
                        context.DrawLine(baseLinePen, new Point(x, PitchAxis.Pitch2Y(maxBlack - 0.4)), new Point(x, Bounds.Height));
                    }
                }
            }

            int quantizationDivision = (int)Quantization.Division;
            int noteDivision = Math.Max(quantizationDivision * 4, timeSignatures[i].Denominator);
            int beatDivision = noteDivision / timeSignatures[i].Denominator;
            double thisTimeSignaturePos = timeSignatures[i].GetTickByBarIndex(thisTimeSignatureBarIndex);
            for (int cellsPerBase = 2; cellsPerBase <= beatDivision; cellsPerBase *= 2)
            {
                int ticksPerCell = ticksPerBase / cellsPerBase;
                double pixelsPerCell = ticksPerCell * TickAxis.PixelsPerTick;
                double cellOpacity = MathUtility.LineValue(MIN_GRID_GAP, 0, MIN_REALITY_GRID_GAP, 1, pixelsPerCell).Limit(0, 1);
                if (cellOpacity == 0)
                    break;

                IPen cellLinePen = new Pen(LineColor.Opacity(cellOpacity).ToUInt32(), LineWidth, new DashStyle(new double[] { PitchAxis.KeyHeight * 0.2 / LineWidth, PitchAxis.KeyHeight * 0.8 / LineWidth }, 0));
                int cellCount = (nextTimeSignatureBarIndex - thisTimeSignatureBarIndex) * timeSignatures[i].Numerator * quantizationBase * cellsPerBase / 2;
                for (int cellIndex = 0; cellIndex < cellCount; cellIndex++)
                {
                    double cellPos = thisTimeSignaturePos + (cellIndex * 2 + 1) * ticksPerCell;
                    double xCell = TickAxis.Tick2X(cellPos);
                    double x = xCell + LineWidth / 2;
                    context.DrawLine(cellLinePen, new Point(x, PitchAxis.Pitch2Y(maxBlack - 0.4)), new Point(x, Bounds.Height));
                }
            }
        }
        // draw refer note
        if (Part.Track != null && Part.Track.Project != null)
            foreach (var track in Part.Track.Project.Tracks)
            {
                if (track == Part.Track) continue;
                if (!track.AsRefer.Value) continue;
                IBrush referBrush = track.GetColor().Opacity(0.5).ToBrush();
                foreach (var part in track.Parts)
                {
                    if (part.EndPos() < minVisibleTick) continue;
                    if (part.StartPos() > maxVisibleTick) continue;
                    if (!(part is MidiPart midiPart)) continue;
                    foreach(var note in midiPart.Notes)
                    {
                        if (note.GlobalEndPos() < minVisibleTick)
                            continue;

                        if (note.GlobalStartPos() > maxVisibleTick)
                            break;

                        var rect = this.ReferNoteRect(note);
                        context.FillRectangle(referBrush, rect);
                    }
                }
            }
        // draw note
        double round = 4;
        IBrush noteBrush = Style.ITEM.ToBrush();
        IBrush selectedNoteBrush = Style.HIGH_LIGHT.ToBrush();
        IBrush lyricBrush = Colors.White.Opacity(0.7).ToBrush();
        IBrush pronunciationBrush = Style.LIGHT_WHITE.ToBrush();
        foreach (var note in Part.Notes)
        {
            if (note.GlobalEndPos() < minVisibleTick)
                continue;

            if (note.GlobalStartPos() > maxVisibleTick)
                break;

            var rect = this.NoteRect(note);
            //context.FillRectangle(getPartColor(Part.Track,note.IsSelected).ToBrush(), rect, (float)round);
            context.FillRectangle(note.IsSelected ? selectedNoteBrush : noteBrush, rect, (float)round);

            rect = rect.Adjusted(8, -28, -8, 0);
            if (rect.Width <= 0)
                continue;

            var clip = context.PushClip(rect);
            context.DrawString(note.Lyric.Value, rect, lyricBrush, 12, Alignment.LeftCenter, Alignment.LeftCenter, new(0, 14));
            var pronunciation = note.FinalPronunciation();
            if (!string.IsNullOrEmpty(pronunciation))
            {
                context.DrawString(pronunciation, rect, pronunciationBrush, 12, Alignment.LeftTop, Alignment.LeftCenter, new(0, 14));
            }
            clip.Dispose();
        }

        var tempoManager = Part.TempoManager;

        foreach (var piece in Part.SynthesisPieces)
        {
            IBrush brush = piece.SynthesisStatus switch
            {
                SynthesisStatus.NotSynthesized => Colors.Gray.Opacity(0.5).ToBrush(),
                SynthesisStatus.SynthesisFailed => Colors.Red.Opacity(0.5).ToBrush(),
                SynthesisStatus.SynthesisSucceeded => Colors.Green.Opacity(0.5).ToBrush(),
                SynthesisStatus.Synthesizing => Colors.Orange.Opacity(0.5).ToBrush(),
                _ => throw new UnreachableException(),
            };
            double left = TickAxis.Tick2X(tempoManager.GetTick(piece.StartTime()));
            double right = TickAxis.Tick2X(tempoManager.GetTick(piece.EndTime()));
            if (piece.SynthesisStatus == SynthesisStatus.Synthesizing)
            {
                double center = MathUtility.LineValue(0, left, 1, right, piece.SynthesisProgress);
                context.DrawRectangle(Colors.Green.Opacity(0.5).ToBrush(), null, new RoundedRect(new(left, 12, center - left, 8), 2, 0, 0, 2));
                context.DrawRectangle(Colors.Orange.Opacity(0.5).ToBrush(), null, new RoundedRect(new(center, 12, right - center, 8), 0, 2, 2, 0));
            }
            else if (piece.SynthesisStatus == SynthesisStatus.SynthesisFailed && !string.IsNullOrEmpty(piece.LastError))
            {
                var rect = new Rect(left, 8, right - left, 16);
                using var clip = context.PushClip(rect);
                context.DrawString(piece.LastError, rect, Colors.Red.ToBrush(), 12, Alignment.LeftCenter, Alignment.LeftCenter);
            }
            else
            {
                context.FillRectangle(brush, new Rect(left, 12, right - left, 8), 2);
            }
        }

        // draw pitch
        double pitchOpacity = MathUtility.LineValue(-6.7, 0, -4.3, 1, TickAxis.ScaleLevel).Limit(0, 1);
        if (pitchOpacity == 0)
            goto FinishDrawPitch;

        Color pitchColor = mDependency.PianoTool == PianoTool.Note ? Colors.White.Opacity(pitchOpacity * 0.3) : Color.Parse(ConstantDefine.PitchColor).Opacity(pitchOpacity);

        DrawSynthesizedPitch(context, pitchColor);

        if (mDependency.PianoTool == PianoTool.Pitch || mDependency.PianoTool == PianoTool.Lock || mDependency.PianoTool == PianoTool.Anchor)
            context.FillRectangle(Colors.Black.Opacity(0.25).ToBrush(), this.Rect());

        DrawVibratos(context);

        if (mDependency.PianoTool == PianoTool.Pitch || mDependency.PianoTool == PianoTool.Lock || mDependency.PianoTool == PianoTool.Vibrato)
        {
            foreach (var vibrato in Part.Vibratos)
            {
                if (vibrato.GlobalEndPos() <= minVisibleTick)
                    continue;

                if (vibrato.GlobalStartPos() >= maxVisibleTick)
                    break;

                DrawPitch(context, TickAxis.Tick2X(vibrato.GlobalStartPos()), TickAxis.Tick2X(vibrato.GlobalEndPos()), Part.Pitch.GetValues, pitchColor.Opacity(0.5), 1);
            }
        }
        DrawPitch(context, 0, Bounds.Width, Part.GetFinalPitch, pitchColor, mDependency.PianoTool == PianoTool.Note ? 1 : 2);
    FinishDrawPitch:

        // draw select
        if (mNoteSelectOperation.IsOperating)
        {
            var rect = mNoteSelectOperation.SelectionRect();
            context.DrawRectangle(SelectionColor.Opacity(0.25).ToBrush(), new Pen(SelectionColor.ToUInt32()), rect);
        }

        if (mVibratoSelectOperation.IsOperating)
        {
            var rect = mVibratoSelectOperation.SelectionRect();
            context.DrawRectangle(SelectionColor.Opacity(0.25).ToBrush(), new Pen(SelectionColor.ToUInt32()), rect);
        }

        if (mAnchorSelectOperation.IsOperating)
        {
            var rect = mAnchorSelectOperation.SelectionRect();
            context.DrawRectangle(SelectionColor.Opacity(0.25).ToBrush(), new Pen(SelectionColor.ToUInt32()), rect);
        }

        double start = TickAxis.Tick2X(Part.StartPos);
        if (start > 0)
        {
            context.FillRectangle(Colors.Black.Opacity(0.3).ToBrush(), this.Rect().Adjusted(0, 0, start - Bounds.Width, 0));
        }

        double end = TickAxis.Tick2X(Part.EndPos);
        if (end < Bounds.Width)
        {
            context.FillRectangle(Colors.Black.Opacity(0.3).ToBrush(), this.Rect().Adjusted(end, 0, 0, 0));
        }

        // draw selection
        if (mSelection.IsAcitve)
        {
            double left = TickAxis.Tick2X(mSelection.Start);
            double right = TickAxis.Tick2X(mSelection.End);
            context.DrawRectangle(SelectionColor.Opacity(0.25).ToBrush(), new Pen(SelectionColor.ToUInt32()), new Rect(left, -2, right - left, Bounds.Height + 4));
        }

        DrawWaveform(context);
    }

    void DrawSynthesizedPitch(DrawingContext context, Color pitchColor)
    {
        if (Part == null)
            return;

        double minVisibleTick = TickAxis.MinVisibleTick;
        double maxVisibleTick = TickAxis.MaxVisibleTick;

        var tempoManager = Part.TempoManager;

        foreach (var piece in Part.SynthesisPieces)
        {
            var result = piece.SynthesisResult;
            if (result == null)
                continue;

            foreach (var pitch in result.SynthesizedPitch)
            {
                if (pitch.IsEmpty())
                    continue;

                double startTime = pitch[0].X;
                double endTime = pitch[pitch.Count - 1].X;
                double startTick = tempoManager.GetTick(startTime);
                double endTick = tempoManager.GetTick(endTime);
                if (endTick < minVisibleTick)
                    continue;

                if (startTick > maxVisibleTick)
                    break;

                int startX = (int)Math.Floor(TickAxis.Tick2X(Math.Max(startTick, minVisibleTick)));
                int endX = (int)Math.Ceiling(TickAxis.Tick2X(Math.Min(endTick, maxVisibleTick)));
                int n = endX - startX + 1;
                double[] times = new double[n];
                for (int i = 0; i < n; i++)
                {
                    times[i] = tempoManager.GetTime(TickAxis.X2Tick(i + startX));
                }

                var ys = pitch.LinearInterpolation(times);

                var points = new LinkedList<Point>();
                for (int i = 0; i < n; i++)
                {
                    points.AddLast(new Point(i + startX, PitchAxis.Pitch2Y(ys[i] + 0.5)));
                }

                context.DrawCurve(points, pitchColor, 1);
            }
        }
    }

    void DrawPitch(DrawingContext context, double left, double right, Func<IReadOnlyList<double>, double[]> getPitch, Color pitchColor, double thickness)
    {
        if (Part == null)
            return;

        double pos = Part.Pos;
        double[] ticks = new double[(int)(right - left) + 1];
        for (int i = 0; i < ticks.Length; i++)
        {
            ticks[i] = TickAxis.X2Tick(left + i) - pos;
        }
        var pitchValues = getPitch(ticks);
        List<List<Point>> pitchLines = new();
        List<Point> pitchLine = new();
        for (int i = 0; i < ticks.Length; i++)
        {
            var pitchValue = pitchValues[i];
            if (double.IsNaN(pitchValue))
            {
                if (pitchLine.Count == 0)
                    continue;

                pitchLines.Add(pitchLine);
                pitchLine = new();
                continue;
            }

            pitchLine.Add(new Point(left + i, PitchAxis.Pitch2Y(pitchValue + 0.5)));
        }
        if (pitchLine.Count != 0)
            pitchLines.Add(pitchLine);

        var start = TickAxis.X2Tick(left) - pos;
        var end = TickAxis.X2Tick(right) - pos;
        foreach (var pitchPoints in pitchLines)
        {
            context.DrawCurve(pitchPoints, pitchColor, thickness);
        }
    }

    void DrawVibratos(DrawingContext context)
    {
        if (mDependency.PianoTool != PianoTool.Vibrato)
            return;

        if (Part == null)
            return;

        double minVisibleTick = TickAxis.MinVisibleTick;
        double maxVisibleTick = TickAxis.MaxVisibleTick;

        IBrush vibratoBrush = Colors.Black.Opacity(0.25).ToBrush();
        IPen vibratoSelectedPen = new Pen(Colors.White.ToUInt32(), 1);

        foreach (var vibrato in Part.Vibratos)
        {
            if (vibrato.GlobalEndPos() < minVisibleTick)
                continue;

            if (vibrato.GlobalStartPos() > maxVisibleTick)
                break;

            double x = TickAxis.Tick2X(vibrato.GlobalStartPos());
            double width = TickAxis.PixelsPerTick * vibrato.Dur;
            context.DrawRectangle(vibratoBrush, vibrato.IsSelected ? vibratoSelectedPen : null, new Rect(x, 0, width, Bounds.Height));
        }
        IBrush frequencyBrush = Colors.White.ToBrush();
        IBrush phaseBrush = Colors.White.ToBrush();
        IPen frequencyPen = new Pen(frequencyBrush, 1);
        IPen phasePen = new Pen(phaseBrush, 1);
        IBrush arBrush = Colors.White.ToBrush();
        IPen arPen = new Pen(arBrush, 1);
        IBrush textBrush = Brushes.White;
        var raycastItem = ItemAt(MousePosition);
        IVibratoItem? hoverVibratoItem = mOperatingVibratoItem;
        if (hoverVibratoItem == null && raycastItem is IVibratoItem vibratoItem) hoverVibratoItem = vibratoItem;
        if (hoverVibratoItem != null)
        {
            var hoverVibrato = hoverVibratoItem.Vibrato;

            var centerX = hoverVibratoItem.CenterX();
            var pitch = hoverVibratoItem.Pitch();
            if (double.IsNaN(pitch))
            {
                context.DrawString("Please draw pitch first in the area.", new Point(centerX, 32), textBrush, 12, Alignment.CenterTop);
            }

            var frequencyPosition = hoverVibratoItem.FrequencyPosition();
            if (!double.IsNaN(frequencyPosition.Y))
            {
                context.DrawEllipse(hoverVibratoItem is VibratoFrequencyItem || mVibratoFrequencyOperation.IsOperating ? frequencyBrush : null, frequencyPen, frequencyPosition, 6, 6);
                context.DrawString("Frequency: " + hoverVibrato.Frequency.Value.ToString("F2"), frequencyPosition - new Point(0, 18), textBrush, new Typeface(Assets.NotoMono), 12, Alignment.Center);
            }

            var phasePosition = hoverVibratoItem.PhasePosition();
            if (!double.IsNaN(phasePosition.Y))
            {
                context.DrawEllipse(hoverVibratoItem is VibratoPhaseItem || mVibratoPhaseOperation.IsOperating ? phaseBrush : null, phasePen, phasePosition, 6, 6);
                context.DrawString("Phase: " + hoverVibrato.Phase.Value.ToString("+0.00;-0.00"), phasePosition + new Point(0, 18), textBrush, new Typeface(Assets.NotoMono), 12, Alignment.Center);
            }

            var attackPosition = hoverVibratoItem.AttackPosition();
            if (!double.IsNaN(attackPosition.Y))
            {
                context.DrawGeometry(arBrush, null, new PolylineGeometry([ 
                    attackPosition + new Point(-4, 0), 
                    attackPosition + new Point(0, -12), 
                    attackPosition + new Point(0, 12), 
                ], true));
            }

            var releasePosition = hoverVibratoItem.ReleasePosition();
            if (!double.IsNaN(releasePosition.Y))
            {
                context.DrawGeometry(arBrush, null, new PolylineGeometry([
                    releasePosition + new Point(0, -12),
                    releasePosition + new Point(4, 0),
                    releasePosition + new Point(0, 12),
                ], true));
            }
        }
    }

    void DrawWaveform(DrawingContext context)
    {
        if (Part == null)
            return;

        double height = WAVEFORM_HEIGHT;
        context.FillRectangle(Colors.Black.Opacity(0.5).ToBrush(), new(0, WaveformTop, Bounds.Width, WAVEFORM_HEIGHT));
        var tempoManager = Part.TempoManager;
        var viewStartTime = tempoManager.GetTime(TickAxis.X2Tick(0));
        var viewEndTime = tempoManager.GetTime(TickAxis.X2Tick(Bounds.Width));

        foreach (var piece in Part.SynthesisPieces)
        {
            double startTime = piece.AudioStartTime();
            double endTime = piece.AudioEndTime();
            if (endTime < viewStartTime)
                continue;

            if (startTime > viewEndTime)
                break;

            var waveform = piece.Waveform;
            if (waveform == null)
                continue;

            var result = piece.SynthesisResult;
            if (result == null)
                continue;

            double minTime = Math.Max(viewStartTime, startTime);
            double maxTime = Math.Min(viewEndTime, endTime);
            double minX = TickAxis.Tick2X(tempoManager.GetTick(minTime));
            double maxX = TickAxis.Tick2X(tempoManager.GetTick(maxTime));
            var xs = new List<double>();
            var positions = new List<double>();
            double gap = 1;
            double xp = minX - gap;
            do
            {
                xp += gap;
                xs.Add(xp);
                double time = tempoManager.GetTime(TickAxis.X2Tick(xp));
                positions.Add((time - result.StartTime) * result.SamplingRate);
            }
            while (xp < maxX);

            if (positions.Count < 2)
                continue;

            float level = (float)MusicTheory.dB2Level(Part.Gain);
            float r = (float)height / 2;
            float top = (float)WaveformTop;
            float toY(float value) => (1 - value * level) * r + top;

            var values = waveform.GetValues(positions);
            var peaks = waveform.GetPeaks(positions, values);

            double pos = Part.Pos;
            var ticks = new double[xs.Count];
            for (int i = 0; i < ticks.Length; i++)
            {
                ticks[i] = TickAxis.X2Tick(xs[i]) - pos;
            }
            var volumes = Part.GetFinalAutomationValues(ticks, ConstantDefine.VolumeID);
            for (int i = 0; i < volumes.Length; i++)
            {
                volumes[i] = MidiPart.Volume2Level(volumes[i]);
            }
            for (int i = 0; i < values.Length; i++)
            {
                values[i] *= (float)volumes[i];
            }
            for (int i = 0; i < peaks.Length; i++)
            {
                peaks[i].min *= (float)volumes[i];
                peaks[i].max *= (float)volumes[i];
            }

            for (int i = 0; i < xs.Count; i++)
            {
                values[i] = toY(values[i]);
            }
            for (int i = 0; i < peaks.Length; i++)
            {
                peaks[i].min = toY(peaks[i].min);
                peaks[i].max = toY(peaks[i].max);
            }
            using var _ = context.PushOpacity(0.5);
            var points = new List<Avalonia.Point>();
            for (int i = 0; i < peaks.Length; i++)
            {
                double x = xs[i];
                var peak = peaks[i];
                points.Add(new(x, values[i]));
                points.Add(new(x + gap * peak.minRatio, peak.min));
            }
            for (int i = peaks.Length; i > 0; i--)
            {
                double x = xs[i];
                var peak = peaks[i - 1];
                points.Add(new(x, values[i]));
                points.Add(new(x + gap * peak.maxRatio, peak.max));
            }
            context.DrawCurve(points, Style.LIGHT_WHITE, gap, true);
        }

        double opacity = MathUtility.LineValue(-4.7, 0, -2.3, 1, TickAxis.ScaleLevel).Limit(0, 1);
        if (opacity <= 0)
            return;

        if (opacity < 1)
            context.PushOpacity(opacity);

        double yCenter = height / 2 + WaveformTop;
        IBrush brush = Style.WHITE.ToBrush();
        IPen pen = new Pen(Style.LIGHT_WHITE.Opacity(0.5).ToBrush(), 1);

        foreach (var note in Part.Notes)
        {
            IReadOnlyList<SynthesizedPhoneme>? phonemes = ((ISynthesisNote)note).Phonemes;
            if (phonemes.IsEmpty())
                phonemes = note.SynthesizedPhonemes;

            if (phonemes == null)
            {
                var startTime = note.StartTime;
                var endTime = note.EndTime;
                if (endTime < viewStartTime)
                    continue;

                if (startTime > viewEndTime)
                    break;

                double left = TickAxis.Tick2X(tempoManager.GetTick(note.StartTime));
                double right = TickAxis.Tick2X(tempoManager.GetTick(note.EndTime));
                context.DrawLine(pen, new(left, yCenter - 12), new(left, yCenter + 12));
                context.DrawLine(pen, new(right, yCenter - 12), new(right, yCenter + 12));
                context.DrawString(note.Lyric.Value, new((left + right) / 2, yCenter), brush, 12, Alignment.Center);
            }
            else
            {
                if (phonemes.IsEmpty())
                    continue;

                var startTime = phonemes.ConstFirst().StartTime;
                var endTime = phonemes.ConstLast().EndTime;
                if (endTime < viewStartTime)
                    continue;

                if (startTime > viewEndTime)
                    break;

                double right = double.NaN;
                foreach (var phoneme in phonemes)
                {
                    double left = TickAxis.Tick2X(tempoManager.GetTick(phoneme.StartTime));
                    if (left != right)
                    {
                        context.DrawLine(pen, new(left, yCenter - 8), new(left, yCenter + 8));
                    }
                    right = TickAxis.Tick2X(tempoManager.GetTick(phoneme.EndTime));
                    context.DrawLine(pen, new(right, yCenter - 8), new(right, yCenter + 8));
                    context.DrawString(phoneme.Symbol, new((left + right) / 2, yCenter), brush, 12, Alignment.Center);
                }
            }
        }
    }

    double QuantizedCellTicks()
    {
        int quantizationBase = (int)Quantization.Base;
        double division = (int)Math.Pow(2, Math.Log2(TickAxis.PixelsPerTick * MusicTheory.RESOLUTION / quantizationBase / MIN_GRID_GAP).Floor()).Limit(1, 32);
        return MusicTheory.RESOLUTION / quantizationBase / division;
    }

    double GetQuantizedTick(double tick)
    {
        double cell = QuantizedCellTicks();
        return (tick / cell).Round() * cell;
    }

    class Selection
    {
        public double Start { get; set; } = 0;
        public double End { get; set; } = 0;
        public double Duration => End - Start;

        public bool IsAcitve { get; set; } = false;
    }

    Selection mSelection = new();

    NoteClipboard mNoteClipboard = new();
    VibratoClipboard mVibratoClipboard = new();
    ParameterClipboard mParameterClipboard = new() { Pitch = [], Automations = [] };
    public void Copy()
    {
        if (Part == null)
            return;

        double pos = Part.Pos.Value;
        switch (mDependency.PianoTool)
        {
            case PianoTool.Note:
                mNoteClipboard = mSelection.IsAcitve ? Part.CopyNotes(mSelection.Start - pos, mSelection.End - pos) : Part.CopyNotes();
                break;
            case PianoTool.Vibrato:
                mVibratoClipboard = mSelection.IsAcitve ? Part.CopyVibratos(mSelection.Start - pos, mSelection.End - pos) : Part.CopyVibratos();
                break;
            case PianoTool.Pitch:
            case PianoTool.Lock:
                if (mSelection.IsAcitve)
                {
                    mParameterClipboard = Part.CopyParameters(mSelection.Start - pos, mSelection.End - pos);
                }
                break;
            case PianoTool.Select:
                if (mSelection.IsAcitve)
                {
                    mNoteClipboard = Part.CopyNotes(mSelection.Start - pos, mSelection.End - pos);
                    mVibratoClipboard = Part.CopyVibratos(mSelection.Start - pos, mSelection.End - pos);
                    mParameterClipboard = Part.CopyParameters(mSelection.Start - pos, mSelection.End - pos);
                }
                break;
        }
    }

    public void Paste()
    {
        if (Part == null)
            return;

        PasteAt(GetQuantizedTick(mDependency.Playhead.Pos) - Part.Pos);
    }

    public void PasteAt(double pos)
    {
        if (Part == null)
            return;

        switch (mDependency.PianoTool)
        {
            case PianoTool.Note:
                Part.PasteAt(mNoteClipboard, pos);
                Part.Commit();
                break;
            case PianoTool.Vibrato:
                Part.PasteAt(mVibratoClipboard, pos);
                Part.Commit();
                break;
            case PianoTool.Pitch:
            case PianoTool.Lock:
                Part.PasteAt(mParameterClipboard, pos, 5);
                Part.Commit();
                break;
            case PianoTool.Select:
                Part.PasteAt(mNoteClipboard, pos);
                Part.PasteAt(mVibratoClipboard, pos);
                Part.PasteAt(mParameterClipboard, pos, 5);
                Part.Commit();
                break;
        }
    }

    public void Cut()
    {
        Copy();
        Delete();
    }

    public void Delete()
    {
        if (Part == null)
            return;

        double pos = Part.Pos.Value;
        switch (mDependency.PianoTool)
        {
            case PianoTool.Note:
                if (mSelection.IsAcitve)
                {
                    Part.DeleteAllNotesInSelection(mSelection.Start - pos, mSelection.End - pos);
                    Part.Commit();
                }
                else
                {
                    Part.DeleteAllSelectedNotes();
                    Part.Commit();
                }
                break;
            case PianoTool.Vibrato:
                if (mSelection.IsAcitve)
                {
                    Part.DeleteAllVibratosInSelection(mSelection.Start - pos, mSelection.End - pos);
                    Part.Commit();
                }
                else
                {
                    Part.DeleteAllSelectedVibratos();
                    Part.Commit();
                }
                break;
            case PianoTool.Pitch:
            case PianoTool.Lock:
                if (mSelection.IsAcitve)
                {
                    Part.ClearParameters(mSelection.Start - pos, mSelection.End - pos);
                    Part.Commit();
                }
                break;
            case PianoTool.Anchor:
                if (mSelection.IsAcitve)
                {
                    Part.ClearParameters(mSelection.Start - pos, mSelection.End - pos);
                    Part.Commit();
                }
                else
                {
                    Part.Pitch.DeleteAllSelectedAnchors();
                    Part.Commit();
                }
                break;
            case PianoTool.Select:
                if (mSelection.IsAcitve)
                {
                    Part.DeleteAllNotesInSelection(mSelection.Start - pos, mSelection.End - pos);
                    Part.DeleteAllVibratosInSelection(mSelection.Start - pos, mSelection.End - pos);
                    Part.ClearParameters(mSelection.Start - pos, mSelection.End - pos);
                    Part.Commit();
                }
                break;
            default:
                break;
        }
    }

    public void ChangeKey(int offset)
    {
        if (Part == null)
            return;

        if (offset == 0)
            return;

        var selectedNotes = Part.Notes.AllSelectedItems();
        if (selectedNotes.IsEmpty())
            return;

        Part.BeginMergeDirty();
        foreach (var note in selectedNotes)
        {
            note.Pitch.Set(note.Pitch.Value + offset);
        }
        Part.EndMergeDirty();
        Part.Commit();
    }

    public void OctaveUp()
    {
        ChangeKey(+12);
    }

    public void OctaveDown()
    {
        ChangeKey(-12);
    }

    public void EnterInputLyric(INote note)
    {
        if (mInputLyricNote != null)
            return;

        mInputLyricNote = note;
        mLyricInput.Text = note.Lyric.Value;
        mLyricInput.IsVisible = true;
        mLyricInput.Focus();
        mLyricInput.SelectAll();
    }

    protected override Size OnArrangeOverride(Size finalSize)
    {
        if (mLyricInput.IsVisible)
            mLyricInput.Arrange(LyricInputRect());

        return finalSize;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            if (Part != null && mInputLyricNote != null)
            {
                var x = TickAxis.Tick2X(mInputLyricNote.GlobalStartPos());
                var y = PitchAxis.Pitch2Y(mInputLyricNote.Pitch.Value);
                var note = e.HasModifiers(ModifierKeys.Shift) ? mInputLyricNote.Last : mInputLyricNote.Next;
                mLyricInput.Unfocus();
                if (note != null)
                {
                    EnterInputLyric(note);
                    TickAxis.AnimateMove(x - TickAxis.Tick2X(note.GlobalStartPos()));
                    PitchAxis.AnimateMove(y - PitchAxis.Pitch2Y(note.Pitch.Value));
                }
                e.Handled = true;
            }
        }

        if (e.IsHandledByTextBox())
            return;
    }

    void OnLyricInputComplete()
    {
        if (mInputLyricNote == null)
            return;

        var newLyric = mLyricInput.Text;
        if (!string.IsNullOrEmpty(newLyric) && newLyric != mInputLyricNote.Lyric.Value)
        {
            mInputLyricNote.Lyric.Set(newLyric);
            mInputLyricNote.Commit();
        }

        mLyricInput.IsVisible = false;
        mInputLyricNote = null;
    }

    Rect LyricInputRect()
    {
        if (mInputLyricNote == null)
            return new Rect();

        double x = TickAxis.Tick2X(mInputLyricNote.GlobalStartPos());
        double y = PitchAxis.Pitch2Y(mInputLyricNote.Pitch.Value + 0.5);
        double w = mInputLyricNote.Dur.Value * TickAxis.PixelsPerTick;
        double h = LyricInputHeight;
        return new Rect(x, y - h / 2, Math.Max(w, LyricInputMinWidth), h);
    }

    INote? mInputLyricNote = null;

    const int LyricInputFontSize = 12;
    const double LyricInputVerticalPadding = 8;
    const double LyricInputHeight = LyricInputFontSize + 2 * LyricInputVerticalPadding;
    const double LyricInputMinWidth = 60;

    readonly TextInput mLyricInput;

    Color WhiteKeyColor => GUI.Style.WHITE_KEY;
    Color BlackKeyColor => GUI.Style.BLACK_KEY;
    Color LineColor => GUI.Style.LINE;
    Color SelectionColor => GUI.Style.HIGH_LIGHT;
    const double MIN_GRID_GAP = 12;
    const double MIN_REALITY_GRID_GAP = MIN_GRID_GAP * 2;
    const double LineWidth = 1;
    const double WAVEFORM_HEIGHT = 64;

    double WaveformTop => mDependency.WaveformBottom - WAVEFORM_HEIGHT;
    double WaveformBottom => mDependency.WaveformBottom;

    readonly DisposableManager s = new();

    readonly IDependency mDependency;
    public TickAxis TickAxis => mDependency.TickAxis;
    public PitchAxis PitchAxis => mDependency.PitchAxis;
    IQuantization Quantization => mDependency.Quantization;
    MidiPart? Part => mDependency.PartProvider.Object;
    ParameterButton PitchButton => mDependency.PitchButton;
}
