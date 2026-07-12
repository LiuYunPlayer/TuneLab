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
using TuneLab.Configs;
using TuneLab.Foundation;
using TuneLab.GUI.Input;
using TuneLab.Data;
using TuneLab.Utils;
using TuneLab.SDK;
using Part = TuneLab.Data.Part;
using Rect = Avalonia.Rect;
using TuneLab.I18N;
using Splat;

using TuneLab.Extensions.Voices;
using TuneLab.Extensions.Instruments;
namespace TuneLab.UI;

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

    protected override async void OnMouseDown(MouseDownEventArgs e)
    {
        // 点击即夺取键盘焦点：Component 默认不 focus，焦点会一直停在 Editor 上、导致本面板经 TrackWindow.OnKeyDown
        // 的快捷键（Ctrl+C/X/V、Delete 等）永不触发。与钢琴窗对称（下方 PianoScrollView 亦然）实现"点哪个面板哪个生效"。
        Focus();
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

                            // 在内容区交互即视为焦点离开轨道头 → 清掉轨道头选中（省去滚到底找空白取消的麻烦）。
                            Project.Tracks.DeselectAllItems();

                            mPrimaryDownPos = e.Position;   // 供抬起时判定"点击(未拖)→清空范围选区"

                            // Shift + 拖 = 画 DAW 式范围选区（常驻、零工具切换；素拖仍走下方 part 框选）。范围与对象正交，
                            // 故命中 part 与否一律进此分支。Alt 透传给 op 作免吸附。清空交给抬起的点击阈值判定统一处理。
                            if ((e.KeyModifiers & ModifierKeys.Shift) != 0)
                            {
                                mRegionSelectionOperation.Down(e.Position, alt);
                                break;
                            }

                            bool ctrl = e.KeyModifiers == ModifierKeys.Ctrl;
                            var item = ItemAt(e.Position);
                            if (item is PartItem partItem)
                            {
                                var part = partItem.Part;
                                
                                if (e.IsDoubleClick)
                                {
                                    if (part is IAudioPart audioPart && audioPart.Status.Value == AudioPartStatus.Unlinked)
                                    {
                                        var path = await this.OpenFile(new FilePickerOpenOptions
                                        {
                                            Title = "Open File",
                                            AllowMultiple = false,
                                            FileTypeFilter = [new("Audio Formats") { Patterns = AudioUtils.AllDecodableFormats.Select(format => "*." + format).ToList() }]
                                        });
                                        if (File.Exists(path))
                                        {
                                            audioPart.Path.Set(path);
                                            audioPart.Commit();
                                        }
                                    }
                                    else
                                    {
                                        mDependency.SwitchEditingPart(part);
                                    }
                                }
                                else
                                {
                                    mSelectOperation.Down(e.Position, ctrl);
                                }
                            }
                            else if (item is PartNameItem partNameItem)
                            {
                                if (e.IsDoubleClick)
                                {
                                    EnterInputPartName(partNameItem.Part, partNameItem.TrackIndex);
                                }
                                else
                                {
                                    mPartMoveOperation.Down(e.Position, ctrl, partNameItem.Part);
                                }
                            }
                            else if (item is PartEndResizeItem partEndResizeItem)
                            {
                                mPartEndResizeOperation.Down(e.Position.X, partEndResizeItem.Part, Project.Tracks[partEndResizeItem.TrackIndex]);
                            }
                            else if (item is PartStartResizeItem partStartResizeItem)
                            {
                                mPartStartResizeOperation.Down(e.Position.X, partStartResizeItem.Part, Project.Tracks[partStartResizeItem.TrackIndex]);
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
                                    var part = track.CreatePart(new MidiPartInfo() { Name = "Part".Tr(TC.Document) + "_" + (track.Project.PartsCount() + 1), Pos = pos, EndOffset = QuantizedCellTicks(), SoundSource = RecentSoundSourceManager.DefaultVoiceSoundSource() });
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

                            var position = e.Position;

                            // 右键落在当前范围选区内 → 选区菜单绝对优先，先于命中 part/轨道的判定、直接接管并返回，
                            // 绝不落到点中 part/轨道的那套菜单（两者操作对象不同、互斥）。选区菜单各项均"闸刀语义"（选区边界切开横跨的 part）：
                            //   合并（仅 MidiPart，多源音频并不了）：选区内片段每轨合并成一个、区外切下的保留；
                            //   复制/剪切/删除（任意 part 类型）：作用于裁到选区的片段、区外切下的保留；粘贴在光标处。
                            if (CurrentSelection is { } regionSelection && IsPointInRegionSelection(position, regionSelection))
                            {
                                var regionMenu = new ContextMenu();
                                if (CollectRegionMergeGroups(regionSelection).Count > 0)
                                    regionMenu.Items.Add(new MenuItem().SetName("Merge".Tr(TC.Menu)).SetAction(() => MergeRegionPerTrack(regionSelection)));

                                if (RegionCoversAnyPart(regionSelection))
                                {
                                    if (regionMenu.Items.Count > 0)
                                        regionMenu.Items.Add(new Separator());
                                    regionMenu.Items.Add(new MenuItem().SetName("Copy Selection".Tr(TC.Menu)).SetAction(() => CopyRegion(regionSelection)).SetInputGesture(Key.C, ModifierKeys.Ctrl));
                                    regionMenu.Items.Add(new MenuItem().SetName("Cut Selection".Tr(TC.Menu)).SetAction(() => CutRegion(regionSelection)).SetInputGesture(Key.X, ModifierKeys.Ctrl));
                                    regionMenu.Items.Add(new MenuItem().SetName("Delete Selection".Tr(TC.Menu)).SetAction(() => DeleteRegion(regionSelection)).SetInputGesture(Key.Delete));
                                }

                                if (CanPaste)
                                {
                                    if (regionMenu.Items.Count > 0)
                                        regionMenu.Items.Add(new Separator());
                                    var pastePos = GetQuantizedTick(TickAxis.X2Tick(position.X));
                                    var pasteTrackIndex = TrackVerticalAxis.GetPosition(position.Y).TrackIndex;
                                    regionMenu.Items.Add(new MenuItem().SetName("Paste".Tr(TC.Menu)).SetAction(() => PasteAt(pastePos, pasteTrackIndex)).SetInputGesture(Key.V, ModifierKeys.Ctrl));
                                }

                                // 命中编排区选区 → trackSelection 工具（目标=tl.trackSelection()）。
                                ScriptToolMenu.AppendContextTools(regionMenu.Items, Scripting.ScriptToolContext.TrackSelection, this);

                                if (regionMenu.Items.Count > 0)
                                    this.OpenContextMenu(regionMenu);
                                return;
                            }

                            var item = ItemAt(e.Position);
                            var menu = new ContextMenu();
                            if (item is IPartItem partItem)
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

                                    {
                                        var menuItem = new MenuItem().SetName("Rename".Tr(TC.Menu)).SetAction(() => { EnterInputPartName(partItem.Part, partItem.TrackIndex); });
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
                                            var trackIndex = TrackVerticalAxis.GetPosition(e.Position.Y).TrackIndex;
                                            var track = Project.Tracks[trackIndex];
                                            if (part.IsSelected && track.Parts.Count(p => p.IsSelected) > 1)
                                            {
                                                var partArray = track.Parts.OrderBy(p => p.StartTime).ToArray();
                                                int partIndex = Array.FindIndex(partArray, p => p == part);
                                                int prevIndex = partIndex;
                                                int nextIndex = partIndex;
                                                while (prevIndex > 0) { if (!partArray[prevIndex - 1].IsSelected || partArray[prevIndex - 1] is not MidiPart) break;prevIndex--; }
                                                while (nextIndex < partArray.Length-1) { if (!partArray[nextIndex + 1].IsSelected || partArray[nextIndex+1] is not MidiPart) break; nextIndex++; }
                                                if (nextIndex > prevIndex)
                                                {
                                                    var menuItem = new MenuItem().SetName("Merge".Tr(TC.Menu)).SetAction(() =>
                                                    {
                                                        var oldParts = partArray.Skip(prevIndex).Take(nextIndex - prevIndex + 1);
                                                        var oldPartInfos = oldParts.Select(p=>(MidiPartInfo)p.GetInfo()).ToArray();
                                                        var newPartInfo = IMidiPartExtension.MergePartInfos(oldPartInfos);
                                                        foreach(var oldPart in oldParts) track.RemovePart(oldPart);
                                                        track.InsertPart(track.CreatePart(newPartInfo));
                                                        track.Commit();
                                                    });
                                                    menu.Items.Add(menuItem);
                                                }
                                            }
                                        }
                                        {
                                            var menuItem = new MenuItem() { Header = "Set Voice".Tr(TC.Menu) };

                                            // 选用某 voice：扇出到全部选中 part + 记入最近使用并存盘。
                                            void ApplyVoice(string type, string id)
                                            {
                                                foreach (var part in Project.Tracks.SelectMany(track => track.Parts).AllSelectedItems())
                                                {
                                                    if (part is MidiPart midiPart)
                                                    {
                                                        midiPart.SoundSource.SetInfo(new SoundSourceInfo() { Type = type, ID = id });
                                                    }
                                                }
                                                RecentSoundSourceManager.PushVoice(type, id);
                                                Project.Commit();
                                            }

                                            // 最近栏：列最近使用的 voice，选项写「引擎名 - voice 名」，身份失效（卸载/改 id）的项跳过。
                                            {
                                                var recentMenu = new MenuItem() { Header = "Recent".Tr(TC.Menu) };
                                                foreach (var recent in RecentSoundSourceManager.Voices)
                                                {
                                                    if (!VoicesManager.TryGetVoiceInfo(recent.Type, recent.ID, out var recentInfo))
                                                        continue;

                                                    var engineName = string.IsNullOrEmpty(recent.Type) ? "Built-In".Tr(TC.Menu) : VoicesManager.GetDisplayName(recent.Type);
                                                    var recentItem = new MenuItem().
                                                        SetName(engineName + " - " + recentInfo.Name).
                                                        SetAction(() => ApplyVoice(recent.Type, recent.ID));
                                                    recentMenu.Items.Add(recentItem);
                                                }
                                                if (recentMenu.Items.Count > 0)
                                                {
                                                    menuItem.Items.Add(recentMenu);
                                                    menuItem.Items.Add(new Separator());
                                                }
                                            }

                                            var allEngines = VoicesManager.GetAllVoiceEngines();
                                            for (int i = 0; i < allEngines.Count; i++)
                                            {
                                                var type = allEngines[i];
                                                var infos = VoicesManager.GetAllVoiceInfos(type);
                                                if (infos == null)
                                                    continue;

                                                var engine = new MenuItem() { Header = string.IsNullOrEmpty(type) ? "Built-In".Tr(TC.Menu) : VoicesManager.GetDisplayName(type) };
                                                {
                                                    foreach (var info in infos)
                                                    {
                                                        var voice = new MenuItem().
                                                            SetName(info.Value.Name).
                                                            SetAction(() => ApplyVoice(type, info.Key));
                                                        engine.Items.Add(voice);
                                                    }
                                                }
                                                menuItem.Items.Add(engine);
                                            }
                                            menu.Items.Add(menuItem);
                                        }
                                        {
                                            // 设置 instrument 音源（与 Set Voice 同构、二选一；当前沿用急切式二级菜单）。
                                            var menuItem = new MenuItem() { Header = "Set Instrument".Tr(TC.Menu) };

                                            // 选用某 instrument：扇出到全部选中 part + 记入最近使用并存盘。
                                            void ApplyInstrument(string type, string id)
                                            {
                                                foreach (var part in Project.Tracks.SelectMany(track => track.Parts).AllSelectedItems())
                                                {
                                                    if (part is MidiPart midiPart)
                                                    {
                                                        midiPart.SoundSource.SetInfo(new SoundSourceInfo() { Kind = SourceKind.Instrument, Type = type, ID = id });
                                                    }
                                                }
                                                RecentSoundSourceManager.PushInstrument(type, id);
                                                Project.Commit();
                                            }

                                            // 最近栏：列最近使用的 instrument，选项写「引擎名 - instrument 名」，身份失效（卸载/改 id）的项跳过。
                                            {
                                                var recentMenu = new MenuItem() { Header = "Recent".Tr(TC.Menu) };
                                                foreach (var recent in RecentSoundSourceManager.Instruments)
                                                {
                                                    if (!InstrumentsManager.TryGetInstrumentInfo(recent.Type, recent.ID, out var recentInfo))
                                                        continue;

                                                    var engineName = string.IsNullOrEmpty(recent.Type) ? "Built-In".Tr(TC.Menu) : InstrumentsManager.GetDisplayName(recent.Type);
                                                    var recentItem = new MenuItem().
                                                        SetName(engineName + " - " + recentInfo.Name).
                                                        SetAction(() => ApplyInstrument(recent.Type, recent.ID));
                                                    recentMenu.Items.Add(recentItem);
                                                }
                                                if (recentMenu.Items.Count > 0)
                                                {
                                                    menuItem.Items.Add(recentMenu);
                                                    menuItem.Items.Add(new Separator());
                                                }
                                            }

                                            var allEngines = InstrumentsManager.GetAllInstrumentEngines();
                                            for (int i = 0; i < allEngines.Count; i++)
                                            {
                                                var type = allEngines[i];
                                                var infos = InstrumentsManager.GetAllInstrumentInfos(type);
                                                if (infos == null)
                                                    continue;

                                                var engine = new MenuItem() { Header = string.IsNullOrEmpty(type) ? "Built-In".Tr(TC.Menu) : InstrumentsManager.GetDisplayName(type) };
                                                {
                                                    foreach (var info in infos)
                                                    {
                                                        var instrument = new MenuItem().
                                                            SetName(info.Value.Name).
                                                            SetAction(() => ApplyInstrument(type, info.Key));
                                                        engine.Items.Add(instrument);
                                                    }
                                                }
                                                menuItem.Items.Add(engine);
                                            }
                                            menu.Items.Add(menuItem);
                                        }
                                        {
                                            var menuItem = new MenuItem().SetName("Remove Overlaps".Tr(TC.Menu)).SetAction(() =>
                                            {
                                                bool changed = false;
                                                foreach (var selectedPart in Project.Tracks.SelectMany(track => track.Parts).AllSelectedItems())
                                                {
                                                    if (selectedPart is MidiPart selectedMidiPart)
                                                        changed |= selectedMidiPart.RemoveOverlaps(selectedMidiPart.Notes);
                                                }
                                                if (changed)
                                                    Project.Commit();
                                            });
                                            menu.Items.Add(menuItem);
                                        }
                                    }
                                    {
                                        var menuItem = new MenuItem().SetName("Delete".Tr(TC.Menu)).SetAction(DeleteAllSelectedParts).SetInputGesture(Key.Delete);
                                        menu.Items.Add(menuItem);
                                    }
                                    ScriptToolMenu.AppendContextTools(menu.Items, Scripting.ScriptToolContext.Part, this);   // 命中 part → part 工具（目标=选中 parts）
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
                                            PasteAt(pos, trackIndex);
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

                                    // 用户脚本工具（context=trackContent，轨道容器/泳道）：右键空白泳道命中某轨 → 选中该轨（镜像 part/轨道头），
                                    // 工具目标 = tl.selectedTracks()。
                                    var track = Project.Tracks[trackIndex];
                                    if (!track.IsSelected)
                                    {
                                        Project.Tracks.DeselectAllItems();
                                        track.Select();
                                    }
                                    ScriptToolMenu.AppendContextTools(menu.Items, Scripting.ScriptToolContext.TrackContent, this);
                                }
                            }

                            this.OpenContextMenu(menu);
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
        bool alt = (e.KeyModifiers & ModifierKeys.Alt) != 0;
        switch (mState)
        {
            case State.Selecting:
                mSelectOperation.Move(e.Position);
                break;
            case State.RegionSelecting:
                mRegionSelectionOperation.Move(e.Position, alt);
                break;
            case State.PartMoving:
                mPartMoveOperation.Move(e.Position, alt);
                break;
            case State.PartEndResizing:
                mPartEndResizeOperation.Move(e.Position.X, alt);
                break;
            case State.PartStartResizing:
                mPartStartResizeOperation.Move(e.Position.X, alt);
                break;
            default:
                var item = ItemAt(e.Position);
                if (item is PartEndResizeItem or PartStartResizeItem)
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
            case State.RegionSelecting:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mRegionSelectionOperation.Up();
                break;
            case State.PartMoving:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mPartMoveOperation.Up();
                break;
            case State.PartEndResizing:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mPartEndResizeOperation.Up();
                break;
            case State.PartStartResizing:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mPartStartResizeOperation.Up();
                break;
            default:
                break;
        }

        // 主键点击(未拖，位移 < 阈值)即清空范围选区：非 Shift 点击=框选零矩形、Shift 点击=零宽选区，二者都该清。
        // 真拖(框选/移动 part/Shift 画区)位移超阈值不触发；右键永不参与(留给常驻右键菜单)。
        if (e.MouseButtonType == MouseButtonType.PrimaryButton)
        {
            var d = e.Position - mPrimaryDownPos;
            if (d.X * d.X + d.Y * d.Y <= ClickThreshold * ClickThreshold)
                ClearSelection();
        }

        if (e.MouseButtonType == MouseButtonType.MiddleButton)
            mMiddleDragOperation.Up();
    }

    protected override void OnKeyDownEvent(KeyEventArgs e)
    {
        switch (mState)
        {
            case State.RegionSelecting:
                if (e.Key == Key.LeftAlt)
                {
                    mRegionSelectionOperation.Move(MousePosition, true);
                    e.Handled = true;
                }
                break;
            case State.PartMoving:
                if (e.Key == Key.LeftAlt)
                {
                    mPartMoveOperation.Move(MousePosition, true);
                    e.Handled = true;
                }
                break;
            case State.PartEndResizing:
                if (e.Key == Key.LeftAlt)
                {
                    mPartEndResizeOperation.Move(MousePosition.X, true);
                    e.Handled = true;
                }
                break;
            case State.PartStartResizing:
                if (e.Key == Key.LeftAlt)
                {
                    mPartStartResizeOperation.Move(MousePosition.X, true);
                    e.Handled = true;
                }
                break;
        }
    }

    protected override void OnKeyUpEvent(KeyEventArgs e)
    {
        switch (mState)
        {
            case State.RegionSelecting:
                if (e.Key == Key.LeftAlt)
                {
                    mRegionSelectionOperation.Move(MousePosition, false);
                    e.Handled = true;
                }
                break;
            case State.PartMoving:
                if (e.Key == Key.LeftAlt)
                {
                    mPartMoveOperation.Move(MousePosition, false);
                    e.Handled = true;
                }
                break;
            case State.PartEndResizing:
                if (e.Key == Key.LeftAlt)
                {
                    mPartEndResizeOperation.Move(MousePosition.X, false);
                    e.Handled = true;
                }
                break;
            case State.PartStartResizing:
                if (e.Key == Key.LeftAlt)
                {
                    mPartStartResizeOperation.Move(MousePosition.X, false);
                    e.Handled = true;
                }
                break;
        }
    }

    protected override void UpdateItems(IItemCollection items)
    {
        if (Project == null)
            return;

        // 拖拽时被拖轨竖向位置乱序（GetTop 非单调），故按轨逐个做可见性裁剪、不靠顺序 break；
        // 并把被拖（选中）轨放到最后添加 → 其 PartItem 渲染时压在最上层（置顶）。
        int count = Project.Tracks.Count;
        for (int trackIndex = 0; trackIndex < count; trackIndex++)
        {
            if (!TrackVerticalAxis.IsDraggedTrack(trackIndex))
                AddTrackPartItems(items, trackIndex);
        }
        if (TrackVerticalAxis.IsTrackDragging)
            for (int trackIndex = 0; trackIndex < count; trackIndex++)
                if (TrackVerticalAxis.IsDraggedTrack(trackIndex))
                    AddTrackPartItems(items, trackIndex);
    }

    void AddTrackPartItems(IItemCollection items, int trackIndex)
    {
        if (Project == null)
            return;

        double top = TrackVerticalAxis.GetTop(trackIndex);
        double bottom = TrackVerticalAxis.GetBottom(trackIndex);
        if (bottom <= 0 || top >= Bounds.Height)
            return;

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

        // 左边缘手柄：按可见起点裁剪可见性（parts 按 StartPos 升序）。放在 part 体之后 → ItemAt 反向遍历时优先命中。
        // midi/audio 均开放：音频样本 0 锚在锚点 Pos，左裁 = 揭示后段音频、锚点前 = 静音（见 AudioPart.GetAudioData）。
        foreach (var part in track.Parts)
        {
            double left = TickAxis.Tick2X(part.StartPos());

            if (left < -8)
                continue;

            if (left > Bounds.Width + 8)
                break;

            items.Add(new PartStartResizeItem(this) { Part = part, TrackIndex = trackIndex });
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

    // Shift+拖 画的 DAW 式范围选区操作：与 SelectOperation(框选 parts)并列、但与对象无关——只把 tick×轨道矩形
    // 写进编辑器态(TrackScrollView.SetSelection)。tick 默认吸量化网格(Alt 免吸附)、纵向吸整轨并钳到现存轨道。
    // 未拖(点击)不建区——清/留交给 OnMouseUp 的点击阈值判定，故 Down 不动旧选区、Up 也不收尾。
    class RegionSelectionOperation(TrackScrollView trackScrollView) : Operation(trackScrollView)
    {
        public bool IsOperating => State == State.RegionSelecting;

        public void Down(Avalonia.Point point, bool alt)
        {
            if (State != State.None)
                return;

            if (TrackScrollView.Project == null)
                return;

            State = State.RegionSelecting;
            mDownTick = TickAt(point.X, alt);
            mDownTrackIndex = TrackIndexAt(point.Y);
        }

        public void Move(Avalonia.Point point, bool alt)
        {
            if (!IsOperating)
                return;

            if (TrackScrollView.Project == null)
                return;

            double tick = TickAt(point.X, alt);
            int trackIndex = TrackIndexAt(point.Y);
            TrackScrollView.SetSelection(new(
                Math.Min(tick, mDownTick), Math.Max(tick, mDownTick),
                Math.Min(trackIndex, mDownTrackIndex), Math.Max(trackIndex, mDownTrackIndex)));
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            State = State.None;
        }

        double TickAt(double x, bool alt)
        {
            double tick = TrackScrollView.TickAxis.X2Tick(x);
            if (!alt)
                tick = TrackScrollView.GetQuantizedTick(tick);
            return Math.Max(0, tick);
        }

        int TrackIndexAt(double y)
        {
            int count = TrackScrollView.Project!.Tracks.Count;
            int index = TrackScrollView.TrackVerticalAxis.GetPosition(y).TrackIndex;
            return Math.Clamp(index, 0, Math.Max(0, count - 1));
        }

        double mDownTick;
        int mDownTrackIndex;
    }

    readonly RegionSelectionOperation mRegionSelectionOperation;

    // 主键按下点 + 点击判定阈值：抬起时位移 ≤ 阈值即视为"点击(未拖)"，用于清空范围选区。
    Avalonia.Point mPrimaryDownPos;
    const double ClickThreshold = 4;

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
                    midiPart.BeginMergeDirty();
            }
            mHead = part.Head;
            mPart = part;
            mDownPartPos = mPart.Pos.Value;
            mMinStartPos = mMoveParts.SelectMany(p => p.parts).Min(p => p.StartPos());
            mTickOffset = TrackScrollView.TickAxis.X2Tick(point.X) - part.Pos.Value;
            mTrackIndex = TrackScrollView.TrackVerticalAxis.GetPosition(point.Y).TrackIndex;
            TrackScrollView.TrackVerticalAxis.SetAutoContentSize(false);
        }

        public void Move(Avalonia.Point point, bool alt)
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
            double pos = TrackScrollView.TickAxis.X2Tick(point.X) - mTickOffset;
            if (!alt)
            {
                pos = TrackScrollView.GetQuantizedTick(pos);
            }
            double posOffset = pos - mDownPartPos;
            // 钳制整体左移：最左 part 的可见起点不越过时间轴 0（否则会拖出视野外拉不回来）。
            posOffset = Math.Max(posOffset, -mMinStartPos);
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
                    midiPart.EndMergeDirty();
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
        double mMinStartPos;   // 本次拖动全部选中 part 中最小的可见起点：用于钳制整体左移不越过时间轴 0
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

        public void Move(double x, bool alt)
        {
            if (mPart == null)
                return;

            if (mTrack == null)
                return;

            mPart.DiscardTo(mHead);
            double end = x - mOffset;
            double endTick = TrackScrollView.TickAxis.X2Tick(end);
            if (!alt)
            {
                endTick = TrackScrollView.GetQuantizedTick(endTick);
            }
            // 拖右边缘 = 只改 EndOffset（终点相对锚点的偏移）；下限保证可见长度 ≥ 一个量化格。
            mTrack.MovePart(mPart, () => mPart.EndOffset.Set(Math.Max(endTick - mPart.Pos.Value, mPart.StartOffset.Value + TrackScrollView.QuantizedCellTicks())));
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

    // 拖左边缘 = 前向裁剪/扩展：只改 StartOffset（起点相对锚点的偏移），锚点与内容不动、非破坏（窗外内容保留）。
    // StartOffset>0 裁剪、<0 扩展；下限保证起点不越过时间轴 0，上限保证可见长度 ≥ 一个量化格。
    class PartStartResizeOperation(TrackScrollView trackScrollView) : Operation(trackScrollView)
    {
        public void Down(double x, IPart part, ITrack track)
        {
            State = State.PartStartResizing;
            mPart = part;
            mTrack = track;
            double start = TrackScrollView.TickAxis.Tick2X(mPart.StartPos());
            mOffset = x - start;
            mHead = mPart.Head;
        }

        public void Move(double x, bool alt)
        {
            if (mPart == null)
                return;

            if (mTrack == null)
                return;

            mPart.DiscardTo(mHead);
            double start = x - mOffset;
            double startTick = TrackScrollView.TickAxis.X2Tick(start);
            if (!alt)
            {
                startTick = TrackScrollView.GetQuantizedTick(startTick);
            }
            double target = Math.Max(0, startTick);   // 起点不越过时间轴 0（越 0 的前向扩展被钳住）
            mTrack.MovePart(mPart, () => mPart.StartOffset.Set(Math.Min(target - mPart.Pos.Value, mPart.EndOffset.Value - TrackScrollView.QuantizedCellTicks())));
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

    readonly PartStartResizeOperation mPartStartResizeOperation;

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

            TrackScrollView.Project.Tracks.SelectMany(track => track.Parts).DeselectAllItems();

            while (TrackScrollView.Project.Tracks.Count < mLastTrackIndex + mPreImportAudioInfos.Count)
                TrackScrollView.Project.NewTrack();

            var trackIndex = mLastTrackIndex;
            foreach (var info in mPreImportAudioInfos)
            {
                var track = TrackScrollView.Project.Tracks[trackIndex];
                var part = track.CreatePart(new AudioPartInfo() { Pos = mLastPos, EndOffset = info.Dur, Name = info.name, Path = info.path });
                part.Select();
                track.InsertPart(part);
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
        RegionSelecting,
        PartMoving,
        PartEndResizing,
        PartStartResizing,
        FileDragging,
    }

    State mState = State.None;
}
