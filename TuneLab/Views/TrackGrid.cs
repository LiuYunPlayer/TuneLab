using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.GUI.Components;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.Base.Data;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Audio;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.IO;
using TuneLab.Utils;
using TuneLab.Base.Science;
using TuneLab.Base.Utils;

namespace TuneLab.Views;

internal partial class TrackGrid : View
{
    public interface IDependency
    {
        TickAxis TickAxis { get; }
        TrackVerticalAxis TrackVerticalAxis { get; }
        IQuantization Quantization { get; }
        IProvider<Project> ProjectProvider { get; }
        IProvider<Part> EditingPart { get; }
        void SwitchEditingPart(IPart part);
        void EnterInputPartName(IPart part, int trackIndex);
    }

    public State OperationState => mState;

    public TrackGrid(IDependency dependency)
    {
        mDependency = dependency;

        mMiddleDragOperation = new(this);
        mSelectOperation = new(this);
        mPartMoveOperation = new(this);
        mPartEndResizeOperation = new(this);
        mDragFileOperation = new(this);

        mDependency.ProjectProvider.ObjectChanged.Subscribe(Update, s);
        mDependency.ProjectProvider.When(project => project.Modified).Subscribe(Update, s);
        mDependency.EditingPart.ObjectChanged.Subscribe(InvalidateVisual, s);
        mDependency.ProjectProvider.When(project => project.Tracks.Any(track => track.Parts.Any(part => part.SelectionChanged))).Subscribe(InvalidateVisual, s);
        mDependency.ProjectProvider.When(project => project.Tracks.Any(track => track.Parts.Any(part => part is AudioPart audioPart ? audioPart.AudioChanged : new ActionEvent()))).Subscribe(InvalidateVisual, s); // TODO: 支持一下可空类型的event
        Quantization.QuantizationChanged += InvalidateVisual;
        TickAxis.AxisChanged += Update;
        TrackVerticalAxis.AxisChanged += Update;

        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    ~TrackGrid()
    {
        s.DisposeAll();
        Quantization.QuantizationChanged -= InvalidateVisual;
        TickAxis.AxisChanged -= Update;
        TrackVerticalAxis.AxisChanged -= Update;
    }

    protected override void OnRender(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, this.Rect());

        if (Project == null)
            return;

        var timeSignatureManager = Project.TimeSignatureManager;

        double startPos = TickAxis.MinVisibleTick;
        double endPos = TickAxis.MaxVisibleTick;

        var startMeter = timeSignatureManager.GetMeterStatus(startPos);
        var endMeter = timeSignatureManager.GetMeterStatus(endPos);

        int startIndex = startMeter.TimeSignatureIndex;
        int endIndex = endMeter.TimeSignatureIndex;

        var timeSignatures = timeSignatureManager.TimeSignatures;
        IBrush barLineBrush = BarLineColor.ToBrush();
        for (int timeSignatureIndex = startIndex; timeSignatureIndex <= endIndex; timeSignatureIndex++)
        {
            // draw bar
            int nextTimeSignatureBarIndex = timeSignatureIndex + 1 == timeSignatures.Count ? (int)Math.Ceiling(endMeter.BarIndex) : timeSignatures[timeSignatureIndex + 1].BarIndex;
            int thisTimeSignatureBarIndex = Math.Max(timeSignatures[timeSignatureIndex].BarIndex, (int)Math.Floor(startMeter.BarIndex));
            for (int barIndex = thisTimeSignatureBarIndex; barIndex < nextTimeSignatureBarIndex; barIndex++)
            {
                double xBarIndex = TickAxis.Tick2X(timeSignatures[timeSignatureIndex].GetTickByBarIndex(barIndex));
                context.FillRectangle(barLineBrush, new Rect(xBarIndex, 0, 1, Bounds.Height));
            }

            // draw beat
            double pixelsPerBeat = timeSignatures[timeSignatureIndex].TicksPerBeat() * TickAxis.PixelsPerTick;
            double beatOpacity = MathUtility.LineValue(MIN_GRID_GAP, 0, MIN_REALITY_GRID_GAP, 1, pixelsPerBeat).Limit(0, 1);
            if (beatOpacity == 0)
                continue;

            IPen beatLinePen = new Pen(BeatLineColor.Opacity(beatOpacity).ToUInt32(), LineWidth);
            for (int barIndex = thisTimeSignatureBarIndex; barIndex < nextTimeSignatureBarIndex; barIndex++)
            {
                for (int beatIndex = 1; beatIndex < timeSignatures[timeSignatureIndex].Numerator; beatIndex++)
                {
                    double xBeatIndex = TickAxis.Tick2X(timeSignatures[timeSignatureIndex].GetTickByBarAndBeat(barIndex, beatIndex));
                    double x = xBeatIndex + LineWidth / 2;
                    context.DrawLine(beatLinePen, new Point(x, 0), new Point(x, Bounds.Height));
                }
            }

            // draw quantization
            int quantizationBase = (int)Quantization.Base;
            int ticksPerBase = timeSignatures[timeSignatureIndex].TicksPerBeat() / quantizationBase;
            double pixelsPerBase = ticksPerBase * TickAxis.PixelsPerTick;
            double baseOpacity = MathUtility.LineValue(MIN_GRID_GAP, 0, MIN_REALITY_GRID_GAP, 1, pixelsPerBase).Limit(0, 1);
            if (baseOpacity == 0)
                continue;

            IPen baseLinePen = new Pen(CellLineColor.Opacity(baseOpacity).ToUInt32(), LineWidth);
            for (int barIndex = thisTimeSignatureBarIndex; barIndex < nextTimeSignatureBarIndex; barIndex++)
            {
                for (int beatIndex = 0; beatIndex < timeSignatures[timeSignatureIndex].Numerator; beatIndex++)
                {
                    double beatPos = timeSignatures[timeSignatureIndex].GetTickByBarAndBeat(barIndex, beatIndex);
                    for (int baseIndex = 1; baseIndex < quantizationBase; baseIndex++)
                    {
                        double xBase = TickAxis.Tick2X(beatPos + baseIndex * ticksPerBase);
                        double x = xBase + LineWidth / 2;
                        context.DrawLine(baseLinePen, new Point(x, 0), new Point(x, Bounds.Height));
                    }
                }
            }

            int quantizationDivision = (int)Quantization.Division;
            int noteDivision = Math.Max(quantizationDivision * 4, timeSignatures[timeSignatureIndex].Denominator);
            int beatDivision = noteDivision / timeSignatures[timeSignatureIndex].Denominator;
            double thisTimeSignaturePos = timeSignatures[timeSignatureIndex].GetTickByBarIndex(thisTimeSignatureBarIndex);
            for (int cellsPerBase = 2; cellsPerBase <= beatDivision; cellsPerBase *= 2)
            {
                int ticksPerCell = ticksPerBase / cellsPerBase;
                double pixelsPerCell = ticksPerCell * TickAxis.PixelsPerTick;
                double cellOpacity = MathUtility.LineValue(MIN_GRID_GAP, 0, MIN_REALITY_GRID_GAP, 1, pixelsPerCell).Limit(0, 1);
                if (cellOpacity == 0)
                    break;

                IPen cellLinePen = new Pen(CellLineColor.Opacity(cellOpacity).ToUInt32(), LineWidth);
                int cellCount = (nextTimeSignatureBarIndex - thisTimeSignatureBarIndex) * timeSignatures[timeSignatureIndex].Numerator * quantizationBase * cellsPerBase / 2;
                for (int cellIndex = 0; cellIndex < cellCount; cellIndex++)
                {
                    double cellPos = thisTimeSignaturePos + (cellIndex * 2 + 1) * ticksPerCell;
                    double xCell = TickAxis.Tick2X(cellPos);
                    double x = xCell + LineWidth / 2;
                    context.DrawLine(cellLinePen, new Point(x, 0), new Point(x, Bounds.Height));
                }
            }
        }

        var tempoManager = Project.TempoManager;

        // draw parts
        IBrush lineBrush = Style.DARK.ToBrush();
        IBrush partBrush = Style.ITEM.Opacity(0.25).ToBrush();
        IBrush selectedPartBrush = Style.HIGH_LIGHT.Opacity(0.25).ToBrush();
        double partLineWidth = 1;
        IPen editPartPen = new Pen(Style.LIGHT_WHITE.ToBrush(), partLineWidth);
        IPen partSelectPen = new Pen(Style.HIGH_LIGHT.ToBrush(), partLineWidth);
        IPen partPen = new Pen(Style.HIGH_LIGHT.Opacity(0.5).ToBrush(), partLineWidth);
        IBrush titleBrush = Colors.White.Opacity(0.7).ToBrush();
        IBrush noteBrush = Style.ITEM.ToBrush();
        IBrush noteSelectBrush = Style.HIGH_LIGHT.ToBrush();
        for (int trackIndex = 0; trackIndex < Project.Tracks.Count; trackIndex++)
        {
            double lineBottom = TrackVerticalAxis.GetTop(trackIndex + 1);
            if (lineBottom <= 0)
                continue;

            double top = TrackVerticalAxis.GetTop(trackIndex);
            if (top >= Bounds.Height)
                break;

            context.FillRectangle(lineBrush, new(0, lineBottom - 1, Bounds.Width, 1));

            double bottom = TrackVerticalAxis.GetBottom(trackIndex);
            if (bottom <= 0)
                continue;

            var track = Project.Tracks[trackIndex];
            foreach (var part in track.Parts)
            {
                if (part.EndPos() <= startPos)
                    continue;

                if (part.StartPos() >= endPos)
                    break;

                double left = Math.Max(TickAxis.Tick2X(part.StartPos()), -8);
                double right = Math.Min(TickAxis.Tick2X(part.EndPos()), Bounds.Width + 8);

                var partRect = new Rect(left, top, right - left, bottom - top);
                context.DrawRectangle(part.IsSelected ? selectedPartBrush : partBrush, part == mDependency.EditingPart.Object ? editPartPen : part.IsSelected ? partSelectPen : partPen, partRect.Inflate(-partLineWidth / 2));
                var titleRect = partRect.WithHeight(16).Adjusted(Math.Max(0, -partRect.Left) + 8, 0, -8, 0);
                var contentRect = partRect.Adjusted(0, 16, 0, 0);
                if (part is MidiPart midiPart)
                {
                    using (context.PushClip(titleRect))
                    {
                        context.DrawString(string.Format("{0}[{1}]", midiPart.Name, midiPart.Voice.Name), titleRect, titleBrush, 12, Alignment.LeftCenter, Alignment.LeftCenter);
                    }

                    if (midiPart.Notes.IsEmpty())
                        continue;

                    using (context.PushClip(contentRect))
                    {
                        var (minPitch, maxPitch) = midiPart.PitchRange();
                        double pitchGap = maxPitch - minPitch + 1;
                        double pitchHeight = Math.Min(contentRect.Height / pitchGap, 8);
                        double partStartPos = Math.Max(startPos, midiPart.StartPos) - midiPart.Pos;
                        double partEndPos = Math.Min(endPos, midiPart.EndPos) - midiPart.Pos;
                        IBrush brush = part.IsSelected ? noteSelectBrush : noteBrush;
                        foreach (var note in midiPart.Notes)
                        {
                            if (note.EndPos() <= partStartPos)
                                continue;

                            if (note.StartPos() >= partEndPos)
                                break;

                            double noteLeft = TickAxis.Tick2X(note.StartPos() + midiPart.Pos);
                            double noteRight = TickAxis.Tick2X(note.EndPos() + midiPart.Pos);
                            context.FillRectangle(brush, new(noteLeft, contentRect.Y + (maxPitch - note.Pitch.Value) * pitchHeight, noteRight - noteLeft, pitchHeight));
                        }
                    }
                }
                else if (part is AudioPart audioPart)
                {
                    using (context.PushClip(titleRect))
                    {
                        context.DrawString(string.Format("{0}[{1}]", audioPart.Name, audioPart.Path), titleRect, titleBrush, 12, Alignment.LeftCenter, Alignment.LeftCenter);
                    }

                    if (audioPart.ChannelCount > 0)
                    {
                        for (int channelIndex = 0; channelIndex < audioPart.ChannelCount; channelIndex++)
                        {
                            if (audioPart.EndPos < TickAxis.MinVisibleTick)
                                continue;

                            if (audioPart.StartPos > TickAxis.MaxVisibleTick)
                                break;

                            var waveform = audioPart.GetWaveform(channelIndex);
                            if (waveform == null)
                                continue;

                            double minTick = Math.Max(TickAxis.MinVisibleTick, audioPart.StartPos);
                            double maxTick = Math.Min(TickAxis.MaxVisibleTick, audioPart.EndPos);
                            double minX = TickAxis.Tick2X(minTick);
                            double maxX = TickAxis.Tick2X(maxTick);
                            var xs = new List<double>();
                            var positions = new List<double>();
                            double gap = 1;
                            double xp = minX - gap;
                            double startTime = audioPart.TempoManager.GetTime(audioPart.StartPos);
                            do
                            {
                                xp += gap;
                                xs.Add(xp);
                                double time = tempoManager.GetTime(TickAxis.X2Tick(xp));
                                positions.Add((time - startTime) * ((IAudioSource)audioPart).SamplingRate);
                            }
                            while (xp < maxX);

                            if (positions.Count < 2)
                                continue;

                            double channelHeight = contentRect.Height / audioPart.ChannelCount;
                            float channelTop = (float)(contentRect.Top + channelHeight * channelIndex);
                            float r = (float)channelHeight / 2;
                            float toY(float value) => channelTop + (1 - value) * r;

                            var values = waveform.GetValues(positions);
                            var peaks = waveform.GetPeaks(positions, values);
                            for (int i = 0; i < xs.Count; i++)
                            {
                                values[i] = toY(values[i]);
                            }
                            for (int i = 0; i < peaks.Length; i++)
                            {
                                peaks[i].min = toY(peaks[i].min);
                                peaks[i].max = toY(peaks[i].max);
                            }
                            // 性能优先先采用画矩形的方案
                            using (var mask = context.PushOpacity(0.5))
                            {
                                for (int i = 0; i < peaks.Length; i++)
                                {
                                    double x = xs[i];
                                    var peak = peaks[i];
                                    context.FillRectangle(Style.LIGHT_WHITE.ToBrush(), new(xs[i], peak.max, gap, peak.min - peak.max));
                                }
                            }
                            
                            /*
                            for (int i = 0; i < peaks.Length; i++)
                            {
                                double x = xs[i];
                                var peak = peaks[i];
                                var path = new PathGeometry();
                                using (var pathContext = path.Open())
                                {
                                    pathContext.BeginFigure(new Point(x, values[i]), true);
                                    pathContext.LineTo(new Point(x + gap * peak.minRatio, peak.min));
                                    pathContext.LineTo(new Point(xs[i + 1], values[i + 1]));
                                    pathContext.LineTo(new Point(x + gap * peak.maxRatio, peak.max));
                                    pathContext.EndFigure(true);
                                }
                                context.DrawGeometry(Style.LIGHT_WHITE.Opacity(0.5).ToBrush(), null, path);
                            }
                            */
                            /*
                            for (int i = 0; i < peaks.Length; i++)
                            {
                                var points = new List<Point>();
                                double x = xs[i];
                                var peak = peaks[i];
                                points.Add(new Point(x, values[i]));
                                points.Add(new Point(x + gap * peak.minRatio, peak.min));
                                points.Add(new Point(xs[i + 1], values[i + 1]));
                                points.Add(new Point(x + gap * peak.maxRatio, peak.max));
                                context.DrawCurve(points, Style.LIGHT_WHITE.Opacity(0.5), gap, true);
                            }
                            */
                            /*
                            var points = new List<Point>();
                            for (int i = 0; i < peaks.Length; i++)
                            {
                                double x = xs[i];
                                var peak = peaks[i];
                                points.Add(new Point(x, values[i]));
                                points.Add(new Point(x + gap * peak.minRatio, peak.min));
                            }
                            for (int i = peaks.Length; i > 0; i--)
                            {
                                double x = xs[i];
                                var peak = peaks[i - 1];
                                points.Add(new Point(x, values[i]));
                                points.Add(new Point(x + gap * peak.maxRatio, peak.max));
                            }
                            context.DrawCurve(points, Style.LIGHT_WHITE.Opacity(0.5), gap, true);
                            */
                        }
                    }
                }
            }
        }

        // draw import parts
        if (mDragFileOperation.IsOperating)
        {
            double left = Math.Max(TickAxis.Tick2X(mDragFileOperation.Pos), -8);
            for (int i = 0; i < mDragFileOperation.PreImportAudioInfos.Count; i++)
            {
                var info = mDragFileOperation.PreImportAudioInfos[i];
                double right = Math.Min(TickAxis.Tick2X(info.EndPos), Bounds.Width + 8);
                double top = TrackVerticalAxis.GetTop(mDragFileOperation.TrackIndex + i);
                double bottom = TrackVerticalAxis.GetBottom(mDragFileOperation.TrackIndex + i);
                var partRect = new Rect(left, top, right - left, bottom - top);
                context.FillRectangle(partBrush, partRect);

                var titleRect = partRect.WithHeight(16).Adjusted(Math.Max(0, -partRect.Left) + 8, 0, -8, 0);
                var contentRect = partRect.Adjusted(0, 16 + 8, 0, -8);
                using (context.PushClip(titleRect))
                {
                    context.DrawString(string.Format("{0}[{1}]", info.name, info.path), titleRect, titleBrush, 12, Alignment.LeftCenter, Alignment.LeftCenter);
                }
            }
        }

        // draw selection
        if (mSelectOperation.IsOperating)
        {
            var rect = mSelectOperation.SelectionRect();
            context.DrawRectangle(SelectionColor.Opacity(0.25).ToBrush(), new Pen(SelectionColor.ToUInt32()), rect);
        }
    }

    public double QuantizedCellTicks()
    {
        int quantizationBase = (int)Quantization.Base;
        double division = (int)Math.Pow(2, Math.Log2(TickAxis.PixelsPerTick * MusicTheory.RESOLUTION / quantizationBase / MIN_GRID_GAP).Floor()).Limit(1, 32);
        return MusicTheory.RESOLUTION / quantizationBase / division;
    }

    public double GetQuantizedTick(double tick)
    {
        double cell = QuantizedCellTicks();
        return (tick / cell).Round() * cell;
    }

    struct PartInfosWithTrackIndex(int trackIndex, IEnumerable<PartInfo> parts)
    {
        public int trackIndex = trackIndex;
        public IEnumerable<PartInfo> parts = parts;
    }

    struct PartClipboard()
    {
        public bool IsEmpty() => content.IsEmpty();

        public double pos = double.MaxValue;
        public List<PartInfosWithTrackIndex> content = new();
    }

    PartClipboard mPartClipboard = new();
    public void Copy()
    {
        if (Project == null)
            return;

        var clipboard = new PartClipboard();
        for (int trackIndex = 0; trackIndex < Project.Tracks.Count; trackIndex++)
        {
            var selectedParts = Project.Tracks[trackIndex].Parts.AllSelectedItems();
            if (selectedParts.IsEmpty())
                continue;

            clipboard.pos = Math.Min(clipboard.pos, selectedParts.First().Pos.Value);
            clipboard.content.Add(new(trackIndex, selectedParts.Select(part => part.GetInfo())));
        }

        if (clipboard.IsEmpty())
            return;

        mPartClipboard = clipboard;
    }

    public void PasteAt(double pos)
    {
        if (Project == null)
            return;

        if (mPartClipboard.IsEmpty())
            return;

        double offset = pos - mPartClipboard.pos;
        Project.Tracks.SelectMany(track => track.Parts).DeselectAllItems();
        foreach (var partInfosWithTrackIndex in mPartClipboard.content)
        {
            var track = Project.Tracks[partInfosWithTrackIndex.trackIndex];
            foreach (var partInfo in partInfosWithTrackIndex.parts)
            {
                var part = track.CreatePart(partInfo);
                part.Select();
                part.Pos.Set(part.Pos.Value + offset);
                track.InsertPart(part);
            }
        }
        Project.Commit();
    }

    public bool CanPaste => !mPartClipboard.IsEmpty();

    public void Cut()
    {
        Copy();
        DeleteAllSelectedParts();
    }

    public void DeleteAllSelectedParts()
    {
        if (Project == null)
            return;

        foreach (var track in Project.Tracks)
        {
            var selectedParts = track.Parts.AllSelectedItems();
            foreach (var part in selectedParts)
            {
                track.RemovePart(part);
            }
        }
        Project.Commit();
    }

    public void DeleteTrackAt(int trackIndex)
    {
        if (Project == null)
            return;

        Project.RemoveTrackAt(trackIndex);
        Project.Commit();
    }

    public async void ImportAudioAt(double pos, int trackIndex)
    {
        var project = Project;
        if (project == null)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File",
            AllowMultiple = false,
            FileTypeFilter = [new("Audio Formats") { Patterns = AudioUtils.AllDecodableFormats.Select(format => "*." + format).ToList() }]
        });
        var path = files.IsEmpty() ? null : files[0].TryGetLocalPath();
        if (path == null)
            return;

        if (!File.Exists(path))
            return;

        if (!AudioUtils.TryGetAudioInfo(path, out var audioInfo))
            return;

        double startTime = project.TempoManager.GetTime(pos);
        double dur = project.TempoManager.GetTick(startTime + audioInfo.duration) - pos;
        var name = new FileInfo(path).Name;

        bool isNewTrack = project.Tracks.Count <= trackIndex;
        while (project.Tracks.Count <= trackIndex)
        {
            project.NewTrack();
        }
        var track = project.Tracks[trackIndex];
        var part = track.CreatePart(new AudioPartInfo() { Name = name, Pos = pos, Dur = dur, Path = path });
        track.InsertPart(part);
        if (isNewTrack)
        {
            track.Name.Set(name);
        }
        project.Commit();
    }

    TickAxis TickAxis => mDependency.TickAxis;
    TrackVerticalAxis TrackVerticalAxis => mDependency.TrackVerticalAxis;
    IQuantization Quantization => mDependency.Quantization;
    Project? Project => mDependency.ProjectProvider.Object;

    const double MIN_GRID_GAP = 12;
    const double MIN_REALITY_GRID_GAP = MIN_GRID_GAP * 2;
    const double LineWidth = 1;

    Color BarLineColor => Style.LINE.Opacity(1.5);
    Color BeatLineColor => Style.LINE.Opacity(1);
    Color CellLineColor => Style.LINE.Opacity(0.5);
    Color SelectionColor => Style.HIGH_LIGHT;

    readonly IDependency mDependency;
    readonly DisposableManager s = new();
}
