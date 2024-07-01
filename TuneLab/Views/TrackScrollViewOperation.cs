using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Base.Data;
using TuneLab.Base.Science;
using TuneLab.Base.Structures;
using TuneLab.Base.Utils;
using TuneLab.GUI.Input;
using TuneLab.Data;
using TuneLab.Utils;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Extensions.Voices;
using Part = TuneLab.Data.Part;
using Rect = Avalonia.Rect;
using TuneLab.I18N;

namespace TuneLab.Views;

internal partial class TrackScrollView
{
    protected override void OnScroll(WheelEventArgs e)
    {
        switch (e.KeyModifiers)
        {
            case ModifierKeys.None:
                TrackVerticalAxis.AnimateMove(70 * e.Delta.Y);
                break;
            case ModifierKeys.Shift:
                TickAxis.AnimateMove(240 * e.Delta.Y);
                break;
            case ModifierKeys.Ctrl:
                TickAxis.AnimateScale(TickAxis.Coor2Pos(e.Position.X), e.Delta.Y);
                break;
            default:
                break;
        }
    }

    protected override void OnMouseDown(MouseDownEventArgs e)
    {
        switch (mState)
        {
            case State.None:
                bool alt = (e.KeyModifiers & ModifierKeys.Alt) != 0;
                switch (e.MouseButtonType)
                {
                    case MouseButtonType.PrimaryButton:
                        {
                            if (Project == null)
                                break;

                            bool ctrl = e.KeyModifiers == ModifierKeys.Ctrl;
                            var item = ItemAt(e.Position);
                            if (item is PartItem partItem)
                            {
                                var part = partItem.Part;
                                
                                if (e.IsDoubleClick)
                                {
                                    mDependency.SwitchEditingPart(part);
                                }
                                else
                                {
                                    mPartMoveOperation.Down(e.Position, ctrl, part);
                                }
                            }
                            else if (e.IsDoubleClick && item is PartNameItem partNameItem)
                            {
                                EnterInputPartName(partNameItem.Part, partNameItem.TrackIndex);
                            }
                            else if (item is PartEndResizeItem partEndResizeItem)
                            {
                                mPartEndResizeOperation.Down(e.Position.X, partEndResizeItem.Part, Project.Tracks[partEndResizeItem.TrackIndex]);
                            }
                            else
                            {
                                if (e.IsDoubleClick)
                                {
                                    var trackIndex = TrackVerticalAxis.GetPosition(e.Position.Y).TrackIndex;
                                    while (Project.Tracks.Count <= trackIndex)
                                    {
                                        Project.NewTrack();
                                    }
                                    var track = Project.Tracks[trackIndex];
                                    var pos = GetQuantizedTick(TickAxis.X2Tick(e.Position.X));
                                    var part = track.CreatePart(new MidiPartInfo() { Pos = pos, Dur = QuantizedCellTicks() });
                                    track.InsertPart(part);
                                    mPartEndResizeOperation.Down(TickAxis.Tick2X(part.EndPos), part, track);
                                }
                                else
                                {
                                    mSelectOperation.Down(e.Position, ctrl);
                                }
                            }
                        }
                        break;
                    case MouseButtonType.SecondaryButton:
                        {
                            if (Project == null)
                                return;

                            var item = ItemAt(e.Position);
                            var position = e.Position;
                            var menu = new ContextMenu();
                            if (item is PartItem partItem)
                            {
                                {
                                    var part = partItem.Part;
                                    if (!part.IsSelected)
                                    {
                                        Project.Tracks.SelectMany(track => track.Parts).DeselectAllItems();
                                        part.Select();
                                    }
                                    {
                                        var menuItem = new MenuItem().SetName("Copy".Tr(TC.Menu)).SetAction(Copy).SetInputGesture(Key.C, ModifierKeys.Ctrl);
                                        menu.Items.Add(menuItem);
                                    }
                                    {
                                        var menuItem = new MenuItem().SetName("Cut".Tr(TC.Menu)).SetAction(Cut).SetInputGesture(Key.X, ModifierKeys.Ctrl);
                                        menu.Items.Add(menuItem);
                                    }
                                    
                                    if (part is IMidiPart midiPart)
                                    {
                                        {
                                            var splitPos = TickAxis.X2Tick(position.X);
                                            if (!alt) splitPos = GetQuantizedTick(splitPos);
                                            if (splitPos > part.StartPos() && splitPos < part.EndPos())
                                            {
                                                var menuItem = new MenuItem().SetName("Split".Tr(TC.Menu)).SetAction(() =>
                                                {
                                                    double pos = midiPart.Pos.Value;
                                                    var leftInfo = midiPart.RangeInfo(midiPart.StartPos() - pos, splitPos - pos);
                                                    var rightInfo = midiPart.RangeInfo(splitPos - pos, midiPart.EndPos() - pos);
                                                    var trackIndex = TrackVerticalAxis.GetPosition(e.Position.Y).TrackIndex;
                                                    var track = Project.Tracks[trackIndex];
                                                    track.RemovePart(part);
                                                    track.InsertPart(track.CreatePart(leftInfo));
                                                    track.InsertPart(track.CreatePart(rightInfo));
                                                    track.Commit();
                                                });
                                                menu.Items.Add(menuItem);
                                            }
                                        }
                                        {
                                            var menuItem = new MenuItem() { Header = "Set Voice".Tr(TC.Menu) };
                                            var allEngines = VoicesManager.GetAllVoiceEngines();
                                            for (int i = 0; i < allEngines.Count; i++)
                                            {
                                                var type = allEngines[i];
                                                var infos = VoicesManager.GetAllVoiceInfos(type);
                                                if (infos == null)
                                                    continue;

                                                var engine = new MenuItem() { Header = string.IsNullOrEmpty(type) ? "Built-In".Tr(TC.Menu) : type };
                                                {
                                                    foreach (var info in infos)
                                                    {
                                                        var voice = new MenuItem().
                                                            SetName(info.Value.Name).
                                                            SetAction(() =>
                                                            {
                                                                foreach (var part in Project.Tracks.SelectMany(track => track.Parts).AllSelectedItems())
                                                                {
                                                                    if (part is MidiPart midiPart)
                                                                    {
                                                                        midiPart.Voice.Set(new VoiceInfo() { Type = type, ID = info.Key });
                                                                    }
                                                                }
                                                                Project.Commit();
                                                            });
                                                        engine.Items.Add(voice);
                                                    }
                                                }
                                                menuItem.Items.Add(engine);
                                            }
                                            menu.Items.Add(menuItem);
                                        }
                                    }
                                    {
                                        var menuItem = new MenuItem() { Header = "Append Voice".Tr(TC.Menu) };
                                        var allEngines = VoicesManager.GetAllVoiceEngines();
                                        for (int i = 0; i < allEngines.Count; i++)
                                        {
                                            var type = allEngines[i];
                                            var infos = VoicesManager.GetAllVoiceInfos(type);
                                            if (infos == null)
                                                continue;

                                            var engine = new MenuItem() { Header = string.IsNullOrEmpty(type) ? "Built-In" : type };
                                            {
                                                foreach (var info in infos)
                                                {
                                                    var voice = new MenuItem().
                                                        SetName(info.Value.Name).
                                                        SetAction(() =>
                                                        {
                                                            foreach (var part in Project.Tracks.SelectMany(track => track.Parts).AllSelectedItems())
                                                            {
                                                                if (part is MidiPart midiPart)
                                                                {
                                                                    if (midiPart.Voice is Voice && ((Voice)midiPart.Voice).isEmptyVoice)
                                                                    {
                                                                        midiPart.Voice.Set(new VoiceInfo() { Type = type, ID = info.Key });
                                                                    }
                                                                    else
                                                                    {
                                                                        midiPart.Voice2.Set(new VoiceInfo() { Type = type, ID = info.Key });
                                                                    }
                                                                }
                                                            }
                                                            Project.Commit();
                                                        });
                                                    engine.Items.Add(voice);
                                                }
                                            }
                                            menuItem.Items.Add(engine);
                                        }
                                        menu.Items.Add(menuItem);
                                    }
                                    { 
                                        var menuItem = new MenuItem().SetName("Delete".Tr(TC.Menu)).SetAction(DeleteAllSelectedParts).SetInputGesture(Key.Delete);
                                        menu.Items.Add(menuItem);
                                    }
                                }
                            }
                            else
                            {
                                var trackIndex = TrackVerticalAxis.GetPosition(position.Y).TrackIndex;
                                {
                                    var pos = GetQuantizedTick(TickAxis.X2Tick(position.X));
                                    var menuItem = new MenuItem().SetName("Import Audio".Tr(TC.Menu)).SetAction(() =>
                                    {
                                        ImportAudioAt(pos, trackIndex);
                                    });
                                    menu.Items.Add(menuItem);
                                    menuItem = new MenuItem().SetName("Import Track".Tr(TC.Menu)).SetAction(() =>
                                    {
                                        ImportTrack();
                                    });
                                    menu.Items.Add(menuItem);
                                }
                                if (CanPaste)
                                {
                                    {
                                        var pos = GetQuantizedTick(TickAxis.X2Tick(position.X));
                                        var menuItem = new MenuItem().SetName("Paste".Tr(TC.Menu)).SetAction(() =>
                                        {
                                            PasteAt(pos);
                                        }).SetInputGesture(Key.V, ModifierKeys.Ctrl);
                                        menu.Items.Add(menuItem);
                                    }
                                }
                                if ((uint)trackIndex < Project.Tracks.Count)
                                {
                                    var menuItem = new MenuItem().SetName("Delete".Tr(TC.Menu)).SetAction(() =>
                                    {
                                        DeleteTrackAt(trackIndex);
                                    }).SetInputGesture(Key.Delete);
                                    menu.Items.Add(menuItem);
                                }
                            }

                            if (menu.ItemCount != 0)
                            {
                                menu.Open(this);
                            }
                        }
                        break;
                    default:
                        break;
                }
                break;
            default:
                break;
        }

        if (e.MouseButtonType == MouseButtonType.MiddleButton)
            mMiddleDragOperation.Down(e.Position);
    }

    protected override void OnMouseAbsoluteMove(MouseMoveEventArgs e)
    {
        if (mMiddleDragOperation.IsOperating)
            mMiddleDragOperation.Move(e.Position);
    }

    protected override void OnMouseRelativeMoveToView(MouseMoveEventArgs e)
    {
        switch (mState)
        {
            case State.Selecting:
                mSelectOperation.Move(e.Position);
                break;
            case State.PartMoving:
                mPartMoveOperation.Move(e.Position);
                break;
            case State.PartEndResizing:
                mPartEndResizeOperation.Move(e.Position.X);
                break;
            default:
                var item = ItemAt(e.Position);
                if (item is PartEndResizeItem)
                {
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeWestEast);
                }
                else
                {
                    Cursor = null;
                }
                break;
        }
    }

    protected override void OnMouseUp(MouseUpEventArgs e)
    {
        switch (mState)
        {
            case State.Selecting:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mSelectOperation.Up();
                break;
            case State.PartMoving:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mPartMoveOperation.Up();
                break;
            case State.PartEndResizing:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mPartEndResizeOperation.Up();
                break;
            default:
                break;
        }

        if (e.MouseButtonType == MouseButtonType.MiddleButton)
            mMiddleDragOperation.Up();
    }

    protected override void UpdateItems(IItemCollection items)
    {
        if (Project == null)
            return;

        for (int trackIndex = 0; trackIndex < Project.Tracks.Count; trackIndex++)
        {
            double lineBottom = TrackVerticalAxis.GetTop(trackIndex + 1);
            if (lineBottom <= 0)
                continue;

            double top = TrackVerticalAxis.GetTop(trackIndex);
            if (top >= Bounds.Height)
                break;

            double bottom = TrackVerticalAxis.GetBottom(trackIndex);
            if (bottom <= 0)
                continue;

            var track = Project.Tracks[trackIndex];
            foreach (var part in track.Parts)
            {
                if (part.EndPos() <= TickAxis.MinVisibleTick)
                    continue;

                if (part.StartPos() >= TickAxis.MaxVisibleTick)
                    break;

                items.Add(new PartItem(this) { Part = part, TrackIndex = trackIndex });
                items.Add(new PartNameItem(this) { Part = part, TrackIndex = trackIndex });
            }

            foreach (var part in track.Parts)
            {
                double right = TickAxis.Tick2X(part.EndPos());

                if (right < -8)
                    continue;

                if (right > Bounds.Width + 8)
                    break;

                items.Add(new PartEndResizeItem(this) { Part = part, TrackIndex = trackIndex });
            }
        }
    }

    void OnDragEnter(object? sender, DragEventArgs e)
    {
        switch (mState)
        {
            case State.None:
                var files = e.Data.GetFiles();
                if (files == null)
                    return;

                mDragFileOperation.Enter(files);
                e.Handled = mState != State.None;
                break;
            default:
                break;
        }
    }

    void OnDragOver(object? sender, DragEventArgs e)
    {
        switch (mState)
        {
            case State.FileDragging:
                mDragFileOperation.Over(e.GetPosition(this));
                e.Handled = true;
                break;
            default:
                break;
        }
    }

    void OnDrop(object? sender, DragEventArgs e)
    {
        switch (mState)
        {
            case State.FileDragging:
                mDragFileOperation.Drop();
                e.Handled = true;
                break;
            default:
                break;
        }
    }

    void OnDragLeave(object? sender, DragEventArgs e)
    {
        switch (mState)
        {
            case State.FileDragging:
                mDragFileOperation.Leave();
                e.Handled = true;
                break;
            default:
                break;
        }
    }

    class Operation(TrackScrollView trackScrollView)
    {
        public TrackScrollView TrackScrollView => trackScrollView;
        public State State { get => TrackScrollView.mState; set => TrackScrollView.mState = value; }
    }

    class MiddleDragOperation(TrackScrollView trackScrollView) : Operation(trackScrollView)
    {
        public bool IsOperating => mIsDragging;

        public void Down(Avalonia.Point point)
        {
            if (mIsDragging)
                return;

            mIsDragging = true;
            mDownTick = TrackScrollView.TickAxis.X2Tick(point.X);
            mDownYPos = TrackScrollView.TrackVerticalAxis.Coor2Pos(point.Y);
            TrackScrollView.TickAxis.StopMoveAnimation();
            TrackScrollView.TrackVerticalAxis.StopMoveAnimation();
        }

        public void Move(Avalonia.Point point)
        {
            if (!mIsDragging)
                return;

            TrackScrollView.TickAxis.MoveTickToX(mDownTick, point.X);
            TrackScrollView.TrackVerticalAxis.MovePosToCoor(mDownYPos, point.Y);
        }

        public void Up()
        {
            if (!mIsDragging)
                return;

            mIsDragging = false;
        }

        double mDownTick;
        double mDownYPos;
        bool mIsDragging = false;
    }

    readonly MiddleDragOperation mMiddleDragOperation;

    class SelectOperation(TrackScrollView trackScrollView) : Operation(trackScrollView)
    {
        public bool IsOperating => State == State.Selecting;

        public void Down(Avalonia.Point point, bool ctrl)
        {
            if (State != State.None)
                return;

            if (TrackScrollView.Project == null)
                return;

            State = State.Selecting;
            mDownTick = TrackScrollView.TickAxis.X2Tick(point.X);
            mDownPosition = TrackScrollView.TrackVerticalAxis.GetPosition(point.Y);
            if (ctrl)
            {
                mSelectedParts = TrackScrollView.Project.Tracks.SelectMany(track => track.Parts).AllSelectedItems();
            }
            Move(point);
        }

        public void Move(Avalonia.Point point)
        {
            if (!IsOperating)
                return;

            mTick = TrackScrollView.TickAxis.X2Tick(point.X);
            mPosition = TrackScrollView.TrackVerticalAxis.GetPosition(point.Y);
            if (TrackScrollView.Project == null)
                return;

            TrackScrollView.Project.Tracks.SelectMany(track => track.Parts).DeselectAllItems();
            if (mSelectedParts != null)
            {
                foreach (var part in mSelectedParts)
                    part.Select();
            }
            double minTick = Math.Min(mTick, mDownTick);
            double maxTick = Math.Max(mTick, mDownTick);
            double minY = Math.Min(mPosition.Y, mDownPosition.Y);
            double maxY = Math.Max(mPosition.Y, mDownPosition.Y);
            for (int i = 0; i < TrackScrollView.Project.Tracks.Count; i++)
            {
                double bottom = TrackScrollView.TrackVerticalAxis.GetBottom(i);
                if (bottom <= minY)
                    continue;

                double top = TrackScrollView.TrackVerticalAxis.GetTop(i);
                if (top >= maxY)
                    break;

                var track = TrackScrollView.Project.Tracks[i];
                foreach (var part in track.Parts)
                {
                    if (part.EndPos() <= minTick)
                        continue;

                    if (part.StartPos() >= maxTick)
                        break;

                    part.Select();
                }
            }
            TrackScrollView.InvalidateVisual();
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            State = State.None;
            mSelectedParts = null;
            TrackScrollView.InvalidateVisual();
        }

        public Rect SelectionRect()
        {
            double minTick = Math.Min(mTick, mDownTick);
            double maxTick = Math.Max(mTick, mDownTick);
            double left = TrackScrollView.TickAxis.Tick2X(minTick);
            double right = TrackScrollView.TickAxis.Tick2X(maxTick);
            double top = Math.Min(mPosition.Y, mDownPosition.Y);
            double bottom = Math.Max(mPosition.Y, mDownPosition.Y);
            return new Rect(left, top, right - left, bottom - top);
        }

        IReadOnlyCollection<IPart>? mSelectedParts = null;
        double mDownTick;
        TrackVerticalAxis.Position mDownPosition;
        double mTick;
        TrackVerticalAxis.Position mPosition;
    }

    readonly SelectOperation mSelectOperation;

    class PartMoveOperation(TrackScrollView trackScrollView) : Operation(trackScrollView)
    {
        public void Down(Avalonia.Point point, bool ctrl, IPart part)
        {
            if (TrackScrollView.Project == null)
                return;

            mCtrl = ctrl;
            mIsSelected = part.IsSelected;
            if (!mCtrl && !mIsSelected)
            {
                TrackScrollView.Project.Tracks.SelectMany(track => track.Parts).DeselectAllItems();
            }
            part.Select();

            for (int trackIndex = 0; trackIndex < TrackScrollView.Project.Tracks.Count; trackIndex++)
            {
                var selectedParts = TrackScrollView.Project.Tracks[trackIndex].Parts.AllSelectedItems();
                if (selectedParts.IsEmpty())
                    continue;

                mMoveParts.Add(new(trackIndex, selectedParts));
            }
            if (mMoveParts.IsEmpty())
                return;

            State = State.PartMoving;
            foreach (var movePart in mMoveParts.SelectMany(p => p.parts))
            {
                if (movePart is MidiPart midiPart)
                    midiPart.DisableAutoPrepare();
            }
            mHead = part.Head;
            mPart = part;
            mDownPartPos = mPart.Pos.Value;
            mTickOffset = TrackScrollView.TickAxis.X2Tick(point.X) - part.Pos.Value;
            mTrackIndex = TrackScrollView.TrackVerticalAxis.GetPosition(point.Y).TrackIndex;
            TrackScrollView.TrackVerticalAxis.SetAutoContentSize(false);
        }

        public void Move(Avalonia.Point point)
        {
            var project = TrackScrollView.Project;
            if (project == null)
                return;

            if (mPart == null)
                return;

            if (mMoveParts.IsEmpty())
                return;

            var position = TrackScrollView.TrackVerticalAxis.GetPosition(point.Y);
            var trackIndex = position.TrackIndex;
            int trackIndexOffset = Math.Max(-mMoveParts.First().trackIndex, trackIndex - mTrackIndex);
            double pos = TrackScrollView.GetQuantizedTick(TrackScrollView.TickAxis.X2Tick(point.X) - mTickOffset);
            double posOffset = pos - mDownPartPos;
            if (posOffset == mLastPosOffset && trackIndexOffset == mLastTrackIndexOffset)
                return;

            mLastPosOffset = posOffset;
            mLastTrackIndexOffset = trackIndexOffset;
            mMoved = true;
            project.DiscardTo(mHead);
            foreach (var partsWithTrackIndex in mMoveParts)
            {
                var parts = partsWithTrackIndex.parts;

                var track = project.Tracks[partsWithTrackIndex.trackIndex];
                foreach (var part in parts)
                {
                    part.Pos.Set(part.Pos.Value + posOffset);
                    track.RemovePart(part);
                }
            }

            foreach (var partsWithTrackIndex in mMoveParts)
            {
                var parts = partsWithTrackIndex.parts;
                if (parts.IsEmpty())
                    continue;

                int dstTrackIndex = partsWithTrackIndex.trackIndex + trackIndexOffset;
                while (project.Tracks.Count <= dstTrackIndex)
                {
                    project.NewTrack();
                }
                foreach (var part in parts)
                    project.Tracks[dstTrackIndex].InsertPart(part);
            }
        }

        public void Up()
        {
            State = State.None;

            if (mPart == null)
                return;

            if (TrackScrollView.Project == null)
                return;

            foreach (var movePart in mMoveParts.SelectMany(p => p.parts))
            {
                if (movePart is MidiPart midiPart)
                    midiPart.EnableAutoPrepare();
            }
            if (mMoved)
            {
                TrackScrollView.Project.Commit();
            }
            else
            {
                mPart.Discard();
                if (mCtrl)
                {
                    if (mIsSelected)
                    {
                        mPart.Inselect();
                    }
                }
                else
                {
                    TrackScrollView.Project.Tracks.SelectMany(track => track.Parts).DeselectAllItems();
                    mPart.Select();
                }
            }
            TrackScrollView.TrackVerticalAxis.SetAutoContentSize(true);
            mMoved = false;
            mPart = null;
            mMoveParts.Clear();
            mLastPosOffset = 0;
            mLastTrackIndexOffset = 0;
        }

        struct PartsWithTrackIndex(int trackIndex, IReadOnlyCollection<IPart> parts)
        {
            public int trackIndex = trackIndex;
            public IReadOnlyCollection<IPart> parts = parts;
        }

        IPart? mPart;
        List<PartsWithTrackIndex> mMoveParts = new();

        bool mCtrl;
        bool mIsSelected;
        bool mMoved = false;
        double mDownPartPos;
        double mTickOffset;
        int mTrackIndex;

        double mLastPosOffset = 0;
        int mLastTrackIndexOffset = 0;
        Head mHead;
    }

    readonly PartMoveOperation mPartMoveOperation;

    class PartEndResizeOperation(TrackScrollView trackScrollView) : Operation(trackScrollView)
    {
        public void Down(double x, IPart part, ITrack track)
        {
            State = State.PartEndResizing;
            mPart = part;
            mTrack = track;
            double end = TrackScrollView.TickAxis.Tick2X(mPart.EndPos());
            mOffset = x - end;
            mHead = mPart.Head;
        }

        public void Move(double x)
        {
            if (mPart == null)
                return;

            if (mTrack == null)
                return;

            mPart.DiscardTo(mHead);
            double end = x - mOffset;
            double endTick = TrackScrollView.TickAxis.X2Tick(end);
            mPart.Dur.Set(Math.Max(TrackScrollView.GetQuantizedTick(endTick) - mPart.Pos.Value, TrackScrollView.QuantizedCellTicks()));
            mTrack.RemovePart(mPart);
            mTrack.InsertPart(mPart);
        }

        public void Up()
        {
            State = State.None;

            if (mPart == null)
                return;

            mPart.Commit();
            mPart = null;
            mTrack = null;
        }

        Head mHead;
        double mOffset;
        IPart? mPart;
        ITrack? mTrack;
    }

    readonly PartEndResizeOperation mPartEndResizeOperation;

    class DragFileOperation(TrackScrollView trackScrollView) : Operation(trackScrollView)
    {
        public bool IsOperating => State == State.FileDragging;
        public double Pos => mLastPos;
        public int TrackIndex => mLastTrackIndex;
        public IReadOnlyList<PreImportAudioInfo> PreImportAudioInfos => mPreImportAudioInfos;

        public void Enter(IEnumerable<IStorageItem> files)
        {
            foreach (var file in files)
            {
                var path = file?.TryGetLocalPath();
                if (path == null)
                    return;

                if (!File.Exists(path))
                    return;

                var name = Path.GetFileName(path);

                if (!AudioUtils.TryGetAudioInfo(path, out var audioInfo))
                    continue;

                mPreImportAudioInfos.Add(new(this) { path = path, name = name, duration = audioInfo.duration });
            }

            if (!mPreImportAudioInfos.IsEmpty())
                State = State.FileDragging;
        }

        public void Over(Avalonia.Point point)
        {
            mLastTrackIndex = TrackScrollView.TrackVerticalAxis.GetPosition(point.Y).TrackIndex;
            mLastPos = TrackScrollView.GetQuantizedTick(TrackScrollView.TickAxis.X2Tick(point.X));

            TrackScrollView.InvalidateVisual();
        }

        public void Leave()
        {
            State = State.None;

            mPreImportAudioInfos.Clear();
        }

        public void Drop()
        {
            State = State.None;

            if (mPreImportAudioInfos.IsEmpty())
                return;

            if (TrackScrollView.Project == null)
                return;

            while (TrackScrollView.Project.Tracks.Count < mLastTrackIndex + mPreImportAudioInfos.Count)
                TrackScrollView.Project.NewTrack();

            var trackIndex = mLastTrackIndex;
            foreach (var info in mPreImportAudioInfos)
            {
                var track = TrackScrollView.Project.Tracks[trackIndex];
                track.InsertPart(track.CreatePart(new AudioPartInfo() { Pos = mLastPos, Dur = info.Dur, Name = info.name, Path = info.path }));
                trackIndex++;
            }
            TrackScrollView.Project.Commit();
            mPreImportAudioInfos.Clear();
        }

        public class PreImportAudioInfo(DragFileOperation operation)
        {
            public double EndPos => operation.TrackScrollView.Project!.TempoManager.GetTick(operation.TrackScrollView.Project.TempoManager.GetTime(operation.mLastPos) + duration);
            public double Dur => EndPos - operation.mLastPos;
            public required string path;
            public required string name;
            public required double duration;
        }

        double mLastPos;
        int mLastTrackIndex;
        readonly List<PreImportAudioInfo> mPreImportAudioInfos = new();
    }

    readonly DragFileOperation mDragFileOperation;

    public enum State
    {
        None,
        Selecting,
        PartMoving,
        PartEndResizing,
        FileDragging,
    }

    State mState = State.None;
}
