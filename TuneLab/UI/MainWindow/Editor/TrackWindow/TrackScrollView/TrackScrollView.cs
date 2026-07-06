using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.GUI.Components;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.SDK;
using TuneLab.Audio;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System.Diagnostics;
using System.IO;
using TuneLab.Utils;
using TuneLab.I18N;

using TuneLab.Extensions.Formats;
using Point = Avalonia.Point;

namespace TuneLab.UI;

// 编排区范围选区（DAW 式 tick×轨道矩形）：纯编辑器态、不入工程。轨道为 0-based 连续 index，已规范化 start≤end。
// 概念面（脚本 API / 用户）称 selection / 选区；代码内用 RegionSelection 与既有 part 框选 SelectOperation 消歧。
internal readonly record struct RegionSelection(double StartTick, double EndTick, int StartTrackIndex, int EndTrackIndex);

internal partial class TrackScrollView : View
{
    public interface IDependency
    {
        TickAxis TickAxis { get; }
        TickAxis PianoTickAxis { get; }
        PitchAxis PianoPitchAxis { get; }
        TrackVerticalAxis TrackVerticalAxis { get; }
        IQuantization Quantization { get; }
        IHolder<IProject> ProjectHolder { get; }
        IHolder<IPart> EditingPart { get; }
        void SwitchEditingPart(IPart part);
    }

    public State OperationState => mState;

    public TrackScrollView(IDependency dependency)
    {
        mDependency = dependency;

        mMiddleDragOperation = new(this);
        mSelectOperation = new(this);
        mRegionSelectionOperation = new(this);
        mPartMoveOperation = new(this);
        mPartEndResizeOperation = new(this);
        mDragFileOperation = new(this);
        mNameInput = new TextInput()
        {
            Padding = new(8, 0),
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            CornerRadius = new(4),
            Background = Brushes.White,
            Foreground = Brushes.Black,
            BorderThickness = new(0),
            FontSize = NameInputFontSize,
            CaretBrush = Brushes.Black,
            IsVisible = false
        };
        mNameInput.EndInput.Subscribe(OnNameInputComplete);
        Children.Add(mNameInput);

        ClipToBounds = true;

        TickAxis.AxisChanged += InvalidateArrange;
        TrackVerticalAxis.AxisChanged += InvalidateArrange;

        mDependency.ProjectHolder.Modified.Subscribe(Update, s);
        mDependency.ProjectHolder.Modified.Subscribe(ClearSelection, s);   // 切工程清掉范围选区（纯编辑器态、不跨工程）
        mDependency.ProjectHolder.When(project => project.Modified).Subscribe(Update, s);
        mDependency.EditingPart.Modified.Subscribe(InvalidateVisual, s);
        mDependency.ProjectHolder.When(project => project.Tracks.WhenAny(track => track.Parts.WhenAny(part => part.SelectionChanged))).Subscribe(InvalidateVisual, s);
        mDependency.ProjectHolder.When(project => project.Tracks.WhenAny(track => track.Parts.WhenAny(part => part is AudioPart audioPart ? audioPart.AudioChanged : new ActionEvent()))).Subscribe(InvalidateVisual, s); // TODO: 支持一下可空类型的event
        mDependency.ProjectHolder.When(project => project.Tracks.WhenAny(track => track.Parts.WhenAny(part => part is AudioPart audioPart ? audioPart.Status.Modified : new ActionEvent()))).Subscribe(InvalidateVisual, s);
        // midi part 合成状态/进度变化 → 刷新 part 底缝那条状态条（否则当前 part 跑进度时编排视图不动）。
        mDependency.ProjectHolder.When(project => project.Tracks.WhenAny(track => track.Parts.WhenAny(part => part is IMidiPart midiPart ? midiPart.SynthesisStatusChanged : new ActionEvent()))).Subscribe(InvalidateVisual, s);
        Quantization.QuantizationChanged += InvalidateVisual;
        TickAxis.AxisChanged += Update;
        TrackVerticalAxis.AxisChanged += Update;
        PianoTickAxis.AxisChanged += InvalidateVisual;   // 钢琴窗滚动/缩放 → 编辑 part 上的视野白框跟随
        PianoPitchAxis.AxisChanged += InvalidateVisual;

        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);

        this.RegisterOnTrackColorUpdated(InvalidateVisual);
    }

    ~TrackScrollView()
    {
        s.DisposeAll();
        Quantization.QuantizationChanged -= InvalidateVisual;
        TickAxis.AxisChanged -= Update;
        TrackVerticalAxis.AxisChanged -= Update;
        PianoTickAxis.AxisChanged -= InvalidateVisual;
        PianoPitchAxis.AxisChanged -= InvalidateVisual;
    }

    // —— 合成中流光：扫描是否有 part 正在合成，驱动一个轻量定时器并算相位；PartItem 画脏区间时取用 SynthesisShimmerPhase。 —— //
    public double SynthesisShimmerPhase => mSynthesisShimmerPhase;
    double mSynthesisShimmerPhase = -1;
    DispatcherTimer? mSynthesisShimmerTimer;
    readonly Stopwatch mSynthesisShimmerClock = new();
    const double SynthesisShimmerPeriod = 1.25;

    void UpdateSynthesisShimmer()
    {
        bool anySynthesizing = Project != null && Project.Tracks.Any(
            t => t.Parts.Any(p => p is MidiPart mp && mp.GetSynthesisStatus().Any(s => s.Status == SynthesisSegmentStatus.Synthesizing)));

        if (anySynthesizing)
        {
            mSynthesisShimmerTimer ??= CreateSynthesisShimmerTimer();
            if (!mSynthesisShimmerTimer.IsEnabled)
            {
                mSynthesisShimmerClock.Restart();
                mSynthesisShimmerTimer.Start();
            }
            mSynthesisShimmerPhase = (mSynthesisShimmerClock.Elapsed.TotalSeconds / SynthesisShimmerPeriod) % 1.0;
        }
        else
        {
            if (mSynthesisShimmerTimer is { IsEnabled: true })
                mSynthesisShimmerTimer.Stop();
            mSynthesisShimmerClock.Reset();
            mSynthesisShimmerPhase = -1;
        }
    }

    DispatcherTimer CreateSynthesisShimmerTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => InvalidateVisual();
        return timer;
    }

    protected override void OnRender(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, this.Rect());

        if (Project == null)
            return;

        UpdateSynthesisShimmer();

        var timeSignatureManager = Project.TimeSignatureManager;

        double startPos = TickAxis.MinVisibleTick;
        double endPos = TickAxis.MaxVisibleTick;

        var startMeter = timeSignatureManager.GetMeterStatus(startPos);
        var endMeter = timeSignatureManager.GetMeterStatus(endPos);

        int startIndex = startMeter.TimeSignatureIndex;
        int endIndex = endMeter.TimeSignatureIndex;

        var timeSignatures = timeSignatureManager.TimeSignatures;

        // draw bar（小节线抽稀：像素连续淡出 [12,24]，与相邻量化网格同套、无号；照钢琴窗内部网格那套。
        // 与时间线标尺各走一套——标尺是线+号一体的离散档淡出，此处是无号网格线，故用 ForEachGridLine）
        IBrush barLineBrush = BarLineColor.ToBrush();
        BarGridLayout.ForEachGridLine(timeSignatureManager, TickAxis, (in BarGridLayout.BarLine line) =>
        {
            double x = TickAxis.Tick2X(line.Tick);
            var brush = line.Opacity >= 1 ? barLineBrush : BarLineColor.Opacity(line.Opacity).ToBrush();
            context.FillRectangle(brush, new Rect(x, 0, 1, Bounds.Height));
        });

        for (int timeSignatureIndex = startIndex; timeSignatureIndex <= endIndex; timeSignatureIndex++)
        {
            int nextTimeSignatureBarIndex = timeSignatureIndex + 1 == timeSignatures.Count ? (int)Math.Ceiling(endMeter.BarIndex) : timeSignatures[timeSignatureIndex + 1].BarIndex;
            int thisTimeSignatureBarIndex = Math.Max(timeSignatures[timeSignatureIndex].BarIndex, (int)Math.Floor(startMeter.BarIndex));

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

        // 轨道分隔线（背景层）；parts 本身由各 PartItem 在 base View.Render 阶段绘制于其上。
        IBrush lineBrush = Style.DARK.ToBrush();
        for (int trackIndex = 0; trackIndex < Project.Tracks.Count; trackIndex++)
        {
            double lineBottom = TrackVerticalAxis.GetTop(trackIndex + 1);
            if (lineBottom <= 0 || lineBottom - 1 >= Bounds.Height)
                continue;

            context.FillRectangle(lineBrush, new(0, lineBottom - 1, Bounds.Width, 1));
        }

        // draw import parts
        if (mDragFileOperation.IsOperating)
        {
            IBrush titleBrush = Brushes.Black;
            double partLineWidth = 1;
            double left = Math.Max(TickAxis.Tick2X(mDragFileOperation.Pos), -8);
            for (int i = 0; i < mDragFileOperation.PreImportAudioInfos.Count; i++)
            {
                int trackIndex = mDragFileOperation.TrackIndex + i;
                var info = mDragFileOperation.PreImportAudioInfos[i];
                double right = Math.Min(TickAxis.Tick2X(info.EndPos), Bounds.Width + 8);
                double top = TrackVerticalAxis.GetTop(trackIndex);
                double bottom = TrackVerticalAxis.GetBottom(trackIndex);
                var partRect = new Rect(left, top, right - left, bottom - top);

                var trackColor = (uint)trackIndex < Project.Tracks.Count ? Project.Tracks[trackIndex].GetColor() : Color.Parse(Style.GetNewColor(trackIndex));

                var frameColor = trackColor;
                context.DrawRectangle(trackColor.Opacity(0.25).ToBrush(), null, partRect, 4, 4);

                var titleRect = partRect.WithHeight(16).Adjusted(Math.Max(0, -partRect.Left) + 8, 0, -8, 0);
                context.DrawRectangle(frameColor.ToBrush(), null, partRect.WithHeight(16).ToRoundedRect(new(4, 4, 0, 0)));
                using (context.PushClip(titleRect))
                {
                    context.DrawString(string.Format("{0}[{1}]", info.name, info.path), titleRect, titleBrush, 12, Alignment.LeftCenter, Alignment.LeftCenter);
                }
                context.DrawRectangle(
                    null,
                    new Pen(frameColor.ToBrush(), partLineWidth),
                    partRect.Inflate(-partLineWidth / 2),
                    4, 4);
            }
        }

        // draw selection
        if (mSelectOperation.IsOperating)
        {
            var rect = mSelectOperation.SelectionRect();
            context.DrawRectangle(SelectionColor.Opacity(0.25).ToBrush(), new Pen(SelectionColor.ToUInt32()), rect);
        }

        // 范围选区(常驻)不在此画：它由 RegionSelectionLayer 覆盖层画在 parts 之上（避免被 part 遮挡）。
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
        var part = track.CreatePart(new AudioPartInfo() { Name = name, Pos = pos, EndOffset = dur, Path = path });
        track.InsertPart(part);
        if (isNewTrack)
        {
            track.Name.Set(name);
        }
        project.Commit();
    }


    public async void ImportTrack()
    {
        var dstProject = Project;
        if (dstProject == null)
            return;

        var formats = FormatsManager.GetAllImportFormats();
        var patterns = new List<string>();
        foreach (var format in formats)
        {
            patterns.Add("*." + format);
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File",
            AllowMultiple = false,
            FileTypeFilter = [new("Importable Formats") { Patterns = patterns }]
        });
        var path = files.IsEmpty() ? null : files[0].TryGetLocalPath();
        if (path == null)
            return;

        if (!File.Exists(path))
            return;

        if (!FormatsManager.Deserialize(path, out var srcProjectInfo, out var error))
        {
            Log.Error("Open file error: " + error);
            return;
        }
        //SelectOne
       // 
        ImportTrackSelector trackSelector = new ImportTrackSelector(
            dstProject.TempoManager.Tempos[0].Bpm,
            dstProject.TimeSignatureManager.TimeSignatures[0].Numerator,
            dstProject.TimeSignatureManager.TimeSignatures[0].Denominator
        );
        for (int i = 0; i < srcProjectInfo.Tracks.Count; i++)
        {
            var trackItem = new ListBoxItem()
            {
                Content = String.Format("Track {0} : {1}", i, srcProjectInfo.Tracks[i].Name),
                FontSize = 12,
                Tag = srcProjectInfo.Tracks[i]
            };
            trackSelector.TrackList.Items.Add(trackItem);
        }
        await trackSelector.ShowDialog(this.Window());
        if (!trackSelector.isOK) return;
        bool keepTempo = trackSelector.IsKeepTempo;
        bool importTempo = trackSelector.IsImportTempo;
        bool importTimeSignature = trackSelector.IsImportTimeSignature;
        if (importTempo)
        {
            ReplaceTempos(dstProject.TempoManager, srcProjectInfo.Tempos);
        }
        if (importTimeSignature)
        {
            ReplaceTimeSignatures(dstProject.TimeSignatureManager, srcProjectInfo.TimeSignatures);
        }
        ITempoManager? srcTempoManager = null;
        if (!importTempo)
        {
            srcTempoManager = new Project(new ProjectInfo() { Tempos = srcProjectInfo.Tempos }).TempoManager;
        }

        static void ReplaceTempos(ITempoManager manager, List<TempoInfo> tempos)
        {
            for (int i = manager.Tempos.Count - 1; i > 0; i--)
            {
                manager.RemoveTempoAt(i);
            }

            if (tempos.Count == 0)
            {
                manager.SetBpm(0, TempoManager.DefaultBpm);
                return;
            }

            manager.SetBpm(0, tempos[0].Bpm);
            for (int i = 1; i < tempos.Count; i++)
            {
                manager.AddTempo(tempos[i].Pos, tempos[i].Bpm);
            }
        }

        static void ReplaceTimeSignatures(ITimeSignatureManager manager, List<TimeSignatureInfo> timeSignatures)
        {
            for (int i = manager.TimeSignatures.Count - 1; i > 0; i--)
            {
                manager.RemoveTimeSignatureAt(i);
            }

            if (timeSignatures.Count == 0)
            {
                manager.SetMeter(0, TimeSignatureManager.DefaultNumerator, TimeSignatureManager.DefaultDenominator);
                return;
            }

            manager.SetMeter(0, timeSignatures[0].Numerator, timeSignatures[0].Denominator);
            for (int i = 1; i < timeSignatures.Count; i++)
            {
                manager.AddTimeSignature(timeSignatures[i].BarIndex, timeSignatures[i].Numerator, timeSignatures[i].Denominator);
            }
        }

        foreach (var selectedTrack in trackSelector.TrackList.SelectedItems)
        {
            TrackInfo srcTrackInfo = (TrackInfo)((ListBoxItem)selectedTrack).Tag;
            List<PartInfo> parts = new List<PartInfo>();
            foreach (var partInfo in srcTrackInfo.Parts)
            {
                bool isMidiPart =(partInfo.GetType() == typeof(MidiPartInfo));
                if (!importTempo && !keepTempo)
                {
                    double SyncTick(double src)
                    {
                        return dstProject.TempoManager.GetTick(srcTempoManager!.GetTime(src));
                    }
                    //SyncPartTick：锚点/起点/终点三点各自换算到目标时基，再重算相对偏移（StartOffset=0 时等价旧逻辑）。
                    double newPos = SyncTick(partInfo.Pos);
                    double newStart = SyncTick(partInfo.Pos + partInfo.StartOffset);
                    double newEnd = SyncTick(partInfo.Pos + partInfo.EndOffset);
                    partInfo.Pos = newPos;
                    partInfo.StartOffset = newStart - newPos;
                    partInfo.EndOffset = newEnd - newPos;
                    if (isMidiPart)
                    {
                        var midiPartInfo = (MidiPartInfo)partInfo;
                        //SyncPitchTick
                        for (var i = 0; i < midiPartInfo.Pitch.Count; i++)
                        {
                            for (var j = 0; j < midiPartInfo.Pitch[i].Count; j++)
                            {
                                midiPartInfo.Pitch[i][j] = new TuneLab.Foundation.Point() { X = SyncTick(midiPartInfo.Pitch[i][j].X), Y = midiPartInfo.Pitch[i][j].Y };
                            }
                        }
                        //SyncNoteTick
                        for (var i = 0; i < midiPartInfo.Notes.Count; i++)
                        {
                            midiPartInfo.Notes[i].Dur = SyncTick(midiPartInfo.Notes[i].Pos + midiPartInfo.Notes[i].Dur);
                            midiPartInfo.Notes[i].Pos = SyncTick(midiPartInfo.Notes[i].Pos);
                            midiPartInfo.Notes[i].Dur -= midiPartInfo.Notes[i].Pos;
                        }
                        //SyncVib
                        for (var i = 0; i < midiPartInfo.Vibratos.Count; i++)
                        {
                            midiPartInfo.Vibratos[i].Dur = SyncTick(midiPartInfo.Vibratos[i].Pos + midiPartInfo.Vibratos[i].Dur);
                            midiPartInfo.Vibratos[i].Pos = SyncTick(midiPartInfo.Vibratos[i].Pos);
                            midiPartInfo.Vibratos[i].Dur -= midiPartInfo.Vibratos[i].Pos;
                        }
                        //SyncAutomn
                        var automationKeys = midiPartInfo.Automations.Keys.ToList();
                        for (var i = 0; i < automationKeys.Count; i++)
                        {
                            for (var j = 0; j < midiPartInfo.Automations[automationKeys[i]].Points.Count; j++)
                            {
                                midiPartInfo.Automations[automationKeys[i]].Points[j] = new TuneLab.Foundation.Point() { X = SyncTick(midiPartInfo.Automations[automationKeys[i]].Points[j].X), Y = midiPartInfo.Automations[automationKeys[i]].Points[j].Y };
                            }
                        }
                    }
                }
                parts.Add(partInfo);
            }
            srcTrackInfo.Parts=parts;
            dstProject.AddTrack(srcTrackInfo);
        }
        dstProject.Commit();
    }

    public void EnterInputPartName(IPart part, int trackIndex)
    {
        if (mInputNamePart != null)
            return;

        mInputNamePart = part;
        mInputNameTrackIndex = trackIndex;
        mNameInput.Display(part.Name.Value);
        mNameInput.IsVisible = true;
        mNameInput.Focus();
        mNameInput.SelectAll();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (mNameInput.IsVisible)
            mNameInput.Arrange(NameInputRect());

        return finalSize;
    }

    void OnNameInputComplete()
    {
        if (mInputNamePart == null)
            return;

        var newLyric = mNameInput.Text;
        if (!string.IsNullOrEmpty(newLyric) && newLyric != mInputNamePart.Name.Value)
        {
            mInputNamePart.Name.Set(newLyric);
            mInputNamePart.Commit();
        }

        mNameInput.IsVisible = false;
        mInputNamePart = null;
    }

    Rect NameInputRect()
    {
        if (mInputNamePart == null)
            return new Rect();

        double x = Math.Max(0, TickAxis.Tick2X(mInputNamePart.StartPos()));
        double y = TrackVerticalAxis.GetTop(mInputNameTrackIndex);
        double w = TickAxis.Tick2X(mInputNamePart.EndPos()) - x;
        double h = NameInputHeight;
        return new Rect(x, y, w.Limit(NameInputMinWidth, NameInputMaxWidth), h);
    }

    IPart? mInputNamePart = null;
    int mInputNameTrackIndex;

    const int NameInputFontSize = 12;
    const double NameInputHeight = 16;
    const double NameInputMinWidth = 60;
    const double NameInputMaxWidth = 600;

    readonly TextInput mNameInput;

    TickAxis TickAxis => mDependency.TickAxis;
    TickAxis PianoTickAxis => mDependency.PianoTickAxis;
    PitchAxis PianoPitchAxis => mDependency.PianoPitchAxis;
    TrackVerticalAxis TrackVerticalAxis => mDependency.TrackVerticalAxis;
    IQuantization Quantization => mDependency.Quantization;
    IProject? Project => mDependency.ProjectHolder.Value;

    const double MIN_GRID_GAP = 12;
    const double MIN_REALITY_GRID_GAP = MIN_GRID_GAP * 2;
    const double LineWidth = 1;

    Color BarLineColor => Style.LINE.Opacity(1.5);
    Color BeatLineColor => Style.LINE.Opacity(1);
    Color CellLineColor => Style.LINE.Opacity(0.5);
    Color SelectionColor => Style.HIGH_LIGHT;

    // 范围选区变化通知：覆盖层 RegionSelectionLayer 据此重绘（选区状态归本类、渲染在覆盖层）。
    public event Action? SelectionChanged;

    // 当前范围选区（编辑器态，读时按现存轨道数钳制：行越界则收窄，整体出界则视为无选区）。
    public RegionSelection? CurrentSelection
    {
        get
        {
            if (mRegionSelection is not { } r || Project == null)
                return null;
            int max = Project.Tracks.Count - 1;
            if (max < 0 || r.StartTrackIndex > max)
                return null;
            return r.EndTrackIndex > max ? r with { EndTrackIndex = max } : r;
        }
    }

    internal void SetSelection(RegionSelection selection)
    {
        mRegionSelection = selection;
        SelectObjectsInRegion(selection);   // 同步高亮覆盖的 part + 跨到的轨道头
        SelectionChanged?.Invoke();
    }

    // 选区覆盖的 part（头在 [startTick,endTick) 左闭右开、且轨道在 [start,end]）设为选中 + 选区跨到的轨道头设为选中。
    // 让"选区覆盖了哪些 part / 哪些轨道"可见；编排区 Ctrl+C/Delete 作用于选中 part，故与选区一致（选区即选中这些 part）。
    // 轨道头选中纯视觉反馈：Delete 键只删选中 part（删轨只在轨道头右键菜单），故选中轨道头不会误删轨。
    void SelectObjectsInRegion(RegionSelection selection)
    {
        if (Project == null)
            return;

        int max = Project.Tracks.Count - 1;
        if (max < 0)
            return;

        int t0 = Math.Clamp(selection.StartTrackIndex, 0, max);
        int t1 = Math.Clamp(selection.EndTrackIndex, 0, max);

        Project.Tracks.SelectMany(track => track.Parts).DeselectAllItems();
        for (int i = t0; i <= t1; i++)
        {
            foreach (var part in Project.Tracks[i].Parts)
            {
                double start = part.StartPos();
                if (start < selection.StartTick)
                    continue;

                if (start >= selection.EndTick)
                    break;

                part.Select();
            }
        }

        Project.Tracks.DeselectAllItems();
        for (int i = t0; i <= t1; i++)
            Project.Tracks[i].Select();
    }

    internal void ClearSelection()
    {
        if (mRegionSelection == null)
            return;

        mRegionSelection = null;
        SelectionChanged?.Invoke();
    }

    RegionSelection? mRegionSelection;

    readonly IDependency mDependency;
    readonly DisposableManager s = new();
}
