using DynamicData;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.GUI.Input;
using TuneLab.Data;
using TuneLab.SDK;
using Rect = Avalonia.Rect;
using ContextMenu = Avalonia.Controls.ContextMenu;
using MenuItem = Avalonia.Controls.MenuItem;
using Avalonia.Input;
using TuneLab.Utils;
using TuneLab.I18N;
using TuneLab.Configs;

namespace TuneLab.UI;

internal partial class PianoScrollView
{
    protected override void OnScroll(WheelEventArgs e)
    {
        switch (e.KeyModifiers)
        {
            case ModifierKeys.None:
                PitchAxis.AnimateMove(70 * e.Delta.Y);
                break;
            case ModifierKeys.Shift:
                TickAxis.AnimateMove(240 * e.Delta.Y);
                break;
            case ModifierKeys.Ctrl:
                TickAxis.AnimateScale(TickAxis.Coor2Pos(e.Position.X), e.Delta.Y);
                break;
            case ModifierKeys.Ctrl | ModifierKeys.Shift:
                PitchAxis.AnimateScale(PitchAxis.Coor2Pos(e.Position.Y), e.Delta.Y);
                break;
        }
    }

    // 合成状态条右键复制：命中某段且该段值得复制（失败 / 带阶段文案）→ 直接把文案拷进剪贴板，返回 true 拦下事件。
    // 只一个动作不弹菜单（弹也只有「复制」一项）；鼠标仍在条上，hover pill 不消失，正好当“复制了啥”的回显。
    bool TrySynthesisStripCopy(Avalonia.Point position)
    {
        if (Part == null)
            return false;

        if (position.Y < SynthesisStripTop || position.Y > SynthesisStripTop + SynthesisStripHeight + SynthesisHoverPadding)
            return false;

        var tempoManager = Part.TempoManager;
        foreach (var seg in Part.GetSynthesisStatus())
        {
            double left = TickAxis.Tick2X(tempoManager.GetTick(seg.StartTime));
            double right = TickAxis.Tick2X(tempoManager.GetTick(seg.EndTime));
            if (position.X < left || position.X > right)
                continue;

            bool worthCopying = seg.Status == SynthesisSegmentStatus.Failed || !string.IsNullOrEmpty(seg.Message);
            if (!worthCopying)
                return false;

            string text = SynthesisStatusText(seg);
            if (string.IsNullOrEmpty(text))
                return false;

            _ = Avalonia.Controls.TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text);
            ShowSynthesisCopyFeedback(seg.StartTime);
            return true;
        }

        return false;
    }

    // 范围选区右键菜单：复制/粘贴 全部 + 各单类（平级展开，便于点击）。粘贴在光标处。单类型快捷键仍走当前工具的 Ctrl+C/V/Del。
    // public：参数区(AutomationRenderer)右键落在带内时也调它（共用同一 TickAxis/X，故传其 e.Position.X 即可）。
    public void OpenRegionMenu(double mouseX)
    {
        if (Part == null)
            return;

        double pastePos = GetQuantizedTick(TickAxis.X2Tick(mouseX)) - Part.Pos.Value;
        // 复制/删除针对选区本身 → 仅当右键落在选区带内才给；带外（多半是粘贴目标）或无选区 → 只给粘贴族。
        bool insideRegion = HasRegionSelection && IsInRegion(mouseX);
        var menu = new ContextMenu();

        // 选区操作（复制/剪切/删除，针对选区本身）：仅右键落在带内才给。各保留 "X Selection"（整段全类型）在一级、并标快捷键
        //（有选区时 Ctrl+C/X、Delete 正作用于全部，与之对应），低频的单类型收进 "X Only" 二级菜单。各族间用分隔线隔开。
        if (insideRegion)
        {
            menu.Items.Add(new MenuItem().SetName("Copy Selection".Tr(TC.Menu)).SetAction(() => CopyRegion(null)).SetInputGesture(Key.C, ModifierKeys.Ctrl));
            menu.Items.Add(RegionKindSubmenu("Copy Only", CopyRegion));
            menu.Items.Add(new Avalonia.Controls.Separator());
            menu.Items.Add(new MenuItem().SetName("Cut Selection".Tr(TC.Menu)).SetAction(() => CutRegion(null)).SetInputGesture(Key.X, ModifierKeys.Ctrl));
            menu.Items.Add(RegionKindSubmenu("Cut Only", CutRegion));
            menu.Items.Add(new Avalonia.Controls.Separator());
            menu.Items.Add(new MenuItem().SetName("Delete Selection".Tr(TC.Menu)).SetAction(() => DeleteRegion(null)).SetInputGesture(Key.Delete));
            menu.Items.Add(RegionKindSubmenu("Delete Only", DeleteRegion));
        }

        // 粘贴族（高频，留在一级、平铺）：只列剪贴板里有的，在光标处（无选区也能粘——复制后清了区仍想粘贴，正是 Shift+右键的用途）。
        var pasteItems = new List<MenuItem>();
        if (CanPaste)
            pasteItems.Add(new MenuItem().SetName("Paste".Tr(TC.Menu)).SetAction(() => PasteAt(pastePos)).SetInputGesture(Key.V, ModifierKeys.Ctrl));
        if (ClipboardHas(RegionDataKind.Notes))
            pasteItems.Add(new MenuItem().SetName("Paste Notes".Tr(TC.Menu)).SetAction(() => PasteRegion(RegionDataKind.Notes, pastePos)));
        if (ClipboardHas(RegionDataKind.Pitch))
            pasteItems.Add(new MenuItem().SetName("Paste Pitch".Tr(TC.Menu)).SetAction(() => PasteRegion(RegionDataKind.Pitch, pastePos)));
        if (ClipboardHas(RegionDataKind.Vibratos))
            pasteItems.Add(new MenuItem().SetName("Paste Vibratos".Tr(TC.Menu)).SetAction(() => PasteRegion(RegionDataKind.Vibratos, pastePos)));
        if (ClipboardHas(RegionDataKind.Automations))
            pasteItems.Add(new MenuItem().SetName("Paste Automations".Tr(TC.Menu)).SetAction(() => PasteRegion(RegionDataKind.Automations, pastePos)));
        if (pasteItems.Count > 0)
        {
            if (menu.Items.Count > 0)
                menu.Items.Add(new Avalonia.Controls.Separator());
            foreach (var item in pasteItems)
                menu.Items.Add(item);
        }

        this.OpenContextMenu(menu);
    }

    // "X Only" 二级菜单：单类型（Notes/Pitch/Vibratos/Automations）→ action(该类)。供 Copy/Cut/Delete Only 复用。
    MenuItem RegionKindSubmenu(string headerKey, Action<RegionDataKind?> action)
    {
        var sub = new MenuItem().SetName(headerKey.Tr(TC.Menu));
        sub.Items.Add(new MenuItem().SetName("Notes".Tr(TC.Menu)).SetAction(() => action(RegionDataKind.Notes)));
        sub.Items.Add(new MenuItem().SetName("Pitch".Tr(TC.Menu)).SetAction(() => action(RegionDataKind.Pitch)));
        sub.Items.Add(new MenuItem().SetName("Vibratos".Tr(TC.Menu)).SetAction(() => action(RegionDataKind.Vibratos)));
        sub.Items.Add(new MenuItem().SetName("Automations".Tr(TC.Menu)).SetAction(() => action(RegionDataKind.Automations)));
        return sub;
    }

    protected override void OnMouseDown(MouseDownEventArgs e)
    {
        switch (mState)
        {
            case State.None:
                bool alt = (e.KeyModifiers & ModifierKeys.Alt) != 0;
                bool ctrl = (e.KeyModifiers & ModifierKeys.Ctrl) != 0;
                var item = ItemAt(e.Position);

                // 合成状态条：右键命中失败段/带阶段文案的段 → 直接复制文案到剪贴板（报错全文）；其余段照常走下方菜单。
                if (e.MouseButtonType == MouseButtonType.SecondaryButton && TrySynthesisStripCopy(e.Position))
                    break;

                // 右键 → 弹范围选区菜单的两种入口：① 有激活选区（任意位置，复制/删/粘）② 按住 Shift（Shift 是"造区"修饰键，
                // 即便没区也给——典型：复制后清了区、想在某处粘贴，不必为粘贴再造一个区）。优先于各工具的右键行为（如 Pitch 擦除）。
                if (e.MouseButtonType == MouseButtonType.SecondaryButton && (HasRegionSelection || (e.KeyModifiers & ModifierKeys.Shift) != 0))
                {
                    OpenRegionMenu(e.Position.X);
                    break;
                }

                // Shift + 主键拖 = 画 DAW 式范围选区（tick 带，常驻、任意工具零切换；取代旧 Select 工具）。先于工具逻辑拦截。
                // 唯一例外：Note 工具下按在 note 本体上 = 约束移动（锁 pos 只变音高），放行给工具逻辑——Shift 落在对象上是
                // 约束对象操作、落在空处才是造区；选区是 1-D tick 带、y 无关，起笔点用 note 上下方空白/波形带即可，不损失。
                // 其余命中（波形、note 边缘、其他工具）一律进此分支——范围与对象框选正交。Alt 透传给 op 作免吸附。
                // 未拖(点击)不在此清，交给 OnMouseUp 的点击阈值统一判定（mPrimaryDownPos 记于此）。
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                {
                    mPrimaryDownPos = e.Position;
                    if ((e.KeyModifiers & ModifierKeys.Shift) != 0 && !IsShiftNoteMove(item))
                    {
                        if (Part != null)   // 无 part 时不建区（范围对空窗无意义）；Shift+主键仍吞掉，不落到工具动作
                            mSelectionOperation.Down(e.Position.X, alt);
                        break;
                    }
                }
                bool DetectWaveformPrimaryButton()
                {
                    // 波形/音素带：响应音素边界拖拽 + note 头/尾缩放（noteon/noteoff 落在音素分界，故可在带内直接缩放 note）。
                    if (item is WaveformPhonemeResizeItem waveformPhonemeResizeItem)
                    {
                        mWaveformPhonemeResizeOperation.Down(e.Position.X, waveformPhonemeResizeItem.Note, waveformPhonemeResizeItem.PhonemeIndex);
                    }
                    else if (item is WaveformNoteStartResizeItem waveformNoteStartResizeItem)
                    {
                        // 与上个（有音素的）note 相接（重叠/相邻）→ 共享边界：联动移上个 note 尾；否则只移本 note 头。
                        var sn = waveformNoteStartResizeItem.Note;
                        var prev = sn.Last;
                        INote? coupled = prev != null && WaveformHasPhonemes(prev) && prev.EndPos() >= sn.StartPos() - 1e-6 ? prev : null;
                        mNoteStartResizeOperation.Down(e.Position.X, sn, coupled);
                    }
                    else if (item is WaveformNoteEndResizeItem waveformNoteEndResizeItem)
                    {
                        mNoteEndResizeOperation.Down(e.Position.X, waveformNoteEndResizeItem.Note);
                    }
                    else if (item is WaveformBackItem)
                    {

                    }
                    else
                    {
                        return false;
                    }

                    return true;
                }
                switch (mDependency.PianoTool.Value)
                {
                    case PianoTool.Note:
                        switch (e.MouseButtonType)
                        {
                            case MouseButtonType.PrimaryButton:
                                {
                                    if (Part == null)
                                        break;

                                    if (DetectWaveformPrimaryButton()) {}
                                    else if (item is NoteItem noteItem)
                                    {
                                        var note = noteItem.Note;

                                        if (e.IsDoubleClick)
                                        {
                                            EnterInputLyric(note);
                                        }
                                        else
                                        {
                                            mNoteMoveOperation.Down(e.Position, ctrl, note);
                                        }
                                    }
                                    else if (item is NoteEndResizeItem noteEndResizeItem)
                                    {
                                        mNoteEndResizeOperation.Down(e.Position.X, noteEndResizeItem.Note);
                                    }
                                    else if (item is NoteStartResizeItem noteStartResizeItem)
                                    {
                                        mNoteStartResizeOperation.Down(e.Position.X, noteStartResizeItem.Note);
                                    }
                                    else if (item is NotePronunciationItem notePronunciationItem)
                                    {
                                        var note = notePronunciationItem.Note;
                                        if (!note.Pronunciations.IsEmpty())
                                        {
                                            var menu = new ContextMenu();
                                            foreach (var pronunciation in note.Pronunciations)
                                            {
                                                var menuItem = new MenuItem().SetName(pronunciation).SetAction(() =>
                                                {
                                                    note.Pronunciation.Set(pronunciation);
                                                    note.Commit();
                                                });
                                                menu.Items.Add(menuItem);
                                            }
                                            this.OpenContextMenu(menu);
                                        }
                                    }
                                    else
                                    {
                                        if (e.IsDoubleClick)
                                        {
                                            var pitch = (int)PitchAxis.Y2Pitch(e.Position.Y);
                                            var pos = TickAxis.X2Tick(e.Position.X);
                                            if (!alt) pos = GetQuantizedTick(pos);
                                            var note = Part.CreateNote(new NoteInfo() { Pos = pos - Part.Pos.Value, Dur = QuantizedCellTicks(), Pitch = pitch, Lyric = Part.SoundSource.DefaultLyric });
                                            Part.InsertNote(note);
                                            mNoteEndResizeOperation.Down(TickAxis.Tick2X(note.GlobalEndPos()), note);
                                        }
                                        else
                                        {
                                            mNoteSelectOperation.Down(e.Position, ctrl);
                                        }
                                    }
                                }
                                break;
                            case MouseButtonType.SecondaryButton:
                                {
                                    if (Part == null)
                                        break;

                                    var menu = new ContextMenu();
                                    if (item is NoteItem noteItem)
                                    {
                                        var note = noteItem.Note;
                                        if (!note.IsSelected)
                                        {
                                            Part.Notes.DeselectAllItems();
                                            note.Select();
                                        }

                                        var position = e.Position;
                                        var splitPos = TickAxis.X2Tick(position.X);
                                        if (!alt) splitPos = GetQuantizedTick(splitPos);
                                        splitPos -= Part.Pos.Value;
                                        if (splitPos > note.StartPos() && splitPos < note.EndPos())
                                        {
                                            var menuItem = new MenuItem().SetName("Split".Tr(TC.Menu)).SetAction(() =>
                                            {
                                                note.SplitAt(splitPos);
                                                Part.Commit();
                                            });
                                            menu.Items.Add(menuItem);
                                        }
                                        {
                                            var menuItem = new MenuItem().SetName("Split by Phonemes".Tr(TC.Menu)).SetAction(() =>
                                            {
                                                var selectedNotes = Part.Notes.AllSelectedItems();
                                                if (selectedNotes.IsEmpty())
                                                    return;

                                                var tempoManager = Part.TempoManager;
                                                double pos = Part.Pos.Value;
                                                List<NoteInfo> phonemeNoteInfos = new();
                                                List<INote> removeNotes = new();
                                                foreach (var note in selectedNotes)
                                                {
                                                    // 用显示侧解析后音素（绝对落点）来拆分：契约层的合成音素只报时长，位置由此派生。
                                                    var phonemes = note.DisplayPhonemes;
                                                    if (phonemes.IsEmpty())
                                                        continue;

                                                    foreach (var phoneme in phonemes)
                                                    {
                                                        var info = note.GetInfo();
                                                        info.Pos = tempoManager.GetTick(phoneme.StartTime) - pos;
                                                        info.Dur = tempoManager.GetTick(phoneme.EndTime) - pos - info.Pos;
                                                        info.Lyric = phoneme.Symbol;
                                                        info.Pronunciation = string.Empty;
                                                        // 单音素填满新 note：时长 = 该音素长（= 新 note 长）、权重沿用引擎产物（元音则布局填满、辅音则固定此长）。
                                                        info.Phonemes = [new() { Symbol = phoneme.Symbol, Duration = phoneme.EndTime - phoneme.StartTime, StretchWeight = phoneme.StretchWeight }];
                                                        phonemeNoteInfos.Add(info);
                                                    }

                                                    removeNotes.Add(note);
                                                }
                                                Part.BeginMergeDirty();
                                                foreach (var note in removeNotes)
                                                {
                                                    Part.RemoveNote(note);
                                                }
                                                foreach (var info in phonemeNoteInfos)
                                                {
                                                    var note = Part.CreateNote(info);
                                                    Part.InsertNote(note);
                                                }
                                                Part.EndMergeDirty();
                                                Part.Commit();
                                            });
                                            menu.Items.Add(menuItem);
                                        }
                                        // 仅当选中音符里有钉死（锁定）音素才提供「清除锁定音素」——清空后回到合成音素口径。
                                        if (Part.Notes.AllSelectedItems().Any(n => !n.Phonemes.IsEmpty()))
                                        {
                                            var menuItem = new MenuItem().SetName("Clear Locked Phonemes".Tr(TC.Menu)).SetAction(() =>
                                            {
                                                bool changed = false;
                                                Part.BeginMergeDirty();
                                                foreach (var selectedNote in Part.Notes.AllSelectedItems())
                                                {
                                                    if (selectedNote.Phonemes.IsEmpty())
                                                        continue;

                                                    selectedNote.ClearLockedPhonemes();
                                                    changed = true;
                                                }
                                                Part.EndMergeDirty();
                                                if (changed)
                                                    Part.Commit();
                                            });
                                            menu.Items.Add(menuItem);
                                        }

                                        menu.Items.Add(new Avalonia.Controls.Separator());
                                        {
                                            var menuItem = new MenuItem().SetName("Copy".Tr(TC.Menu)).SetAction(Copy).SetInputGesture(Key.C, ModifierKeys.Ctrl);
                                            menu.Items.Add(menuItem);
                                        }
                                        {
                                            var menuItem = new MenuItem().SetName("Cut".Tr(TC.Menu)).SetAction(Cut).SetInputGesture(Key.X, ModifierKeys.Ctrl);
                                            menu.Items.Add(menuItem);
                                        }

                                        menu.Items.Add(new Avalonia.Controls.Separator());
                                        {
                                            var menuItem = new MenuItem().SetName("Octave Up".Tr(TC.Menu)).SetAction(OctaveUp);
                                            menu.Items.Add(menuItem);
                                        }
                                        {
                                            var menuItem = new MenuItem().SetName("Octave Down".Tr(TC.Menu)).SetAction(OctaveDown);
                                            menu.Items.Add(menuItem);
                                        }

                                        menu.Items.Add(new Avalonia.Controls.Separator());
                                        if (note.Next != null)
                                        {
                                            var menuItem = new MenuItem().SetName("Move Lyrics Forward".Tr(TC.Menu)).SetAction(() =>
                                            {
                                                Part.BeginMergeDirty();
                                                var it = note;
                                                while (it.Next != null)
                                                {
                                                    var next = it.Next;
                                                    it.Lyric.Set(next.Lyric.Value);
                                                    it = next;
                                                }
                                                Part.EndMergeDirty();
                                                Part.Commit();
                                            });
                                            menu.Items.Add(menuItem);
                                        }
                                        if (note.Last != null && Part.Notes.Last != null)
                                        {
                                            var menuItem = new MenuItem().SetName("Move Lyrics Backward".Tr(TC.Menu)).SetAction(() =>
                                            {
                                                Part.BeginMergeDirty();
                                                var it = Part.Notes.Last;
                                                while (it != note && it.Last != null)
                                                {
                                                    var last = it.Last;
                                                    it.Lyric.Set(last.Lyric.Value);
                                                    it = last;
                                                }
                                                note.Lyric.Set("-");
                                                Part.EndMergeDirty();
                                                Part.Commit();
                                            });
                                            menu.Items.Add(menuItem);
                                        }
                                        {
                                            var menuItem = new MenuItem().SetName("Input Lyrics".Tr(TC.Menu)).SetAction(() => { LyricInput.EnterInput(Part.Notes.AllSelectedItems(), this.Window()); });
                                            menu.Items.Add(menuItem);
                                        }
                                        {
                                            var menuItem = new MenuItem().SetName("Remove Overlaps".Tr(TC.Menu)).SetAction(() =>
                                            {
                                                if (Part.RemoveOverlaps(Part.Notes.AllSelectedItems()))
                                                    Part.Commit();
                                            });
                                            menu.Items.Add(menuItem);
                                        }

                                        menu.Items.Add(new Avalonia.Controls.Separator());
                                        {
                                            var menuItem = new MenuItem().SetName("Delete".Tr(TC.Menu)).SetAction(Delete).SetInputGesture(Key.Delete);
                                            menu.Items.Add(menuItem);
                                        }
                                        ScriptToolMenu.AppendContextTools(menu.Items, Scripting.ScriptToolContext.Note, this);   // 命中音符 → note 工具（目标=选中音符）
                                    }
                                    else
                                    {
                                        if (CanPaste)
                                        {
                                            {
                                                var position = e.Position;
                                                var pos = GetQuantizedTick(TickAxis.X2Tick(position.X)) - Part.Pos.Value;
                                                var menuItem = new MenuItem().SetName("Paste".Tr(TC.Menu)).SetAction(() =>
                                                {
                                                    PasteAt(pos);
                                                }).SetInputGesture(Key.V, ModifierKeys.Ctrl);
                                                menu.Items.Add(menuItem);
                                            }
                                        }
                                        ScriptToolMenu.AppendContextTools(menu.Items, Scripting.ScriptToolContext.PartContent, this);   // 钢琴空白 → partContent 工具（目标=当前 part）
                                    }

                                    this.OpenContextMenu(menu);
                                }
                                break;
                            default:
                                break;
                        }
                        break;
                    case PianoTool.Pitch:
                        switch (e.MouseButtonType)
                        {
                            case MouseButtonType.PrimaryButton:
                                // 范围选区已统一为 Shift+拖（上方全局拦截）；Ctrl 改作"定值绘制"——锁住按下时的 y 画水平线（重要的保值画法）。
                                if (!DetectWaveformPrimaryButton())
                                    mPitchDrawOperation.Down(e.Position, ctrl);
                                break;
                            case MouseButtonType.SecondaryButton:
                                mPitchClearOperation.Down(e.Position.X);
                                break;
                        }
                        break;
                    case PianoTool.Anchor:
                        switch (e.MouseButtonType)
                        {
                            case MouseButtonType.PrimaryButton:
                                if (!DetectWaveformPrimaryButton())
                                {
                                    if (Part == null)
                                        break;

                                    Part.DeselectAllAutomationPoints();
                                    mDependency.AutomationRenderer.InvalidateVisual();
                                    mDependency.AutomationRenderer.RefreshAnchorValueInput();

                                    if (mPreviewPitchItem != null && mPreviewPitchItem.OnDown != null)
                                    {
                                        mPreviewPitchItem.OnDown.Invoke(e, ctrl);
                                    }
                                    else
                                    {
                                        if (item is AnchorItem anchorItem)
                                        {
                                            mAnchorMoveOperation.Down(e.Position, ctrl, anchorItem.AnchorPoint);
                                        }
                                        else
                                        {
                                            if (e.IsDoubleClick)
                                            {
                                                var anchor = new AnchorPoint(TickAxis.X2Tick(e.Position.X) - Part.Pos.Value, PitchAxis.Y2Pitch(e.Position.Y) - 0.5) { IsSelected = true };
                                                Part.Pitch.InsertPoint(anchor);
                                                Part.Pitch.DeselectAllAnchors();
                                                anchor.Select();
                                                mAnchorMoveOperation.Down(e.Position, ctrl, anchor);
                                            }
                                            else
                                            {
                                                mAnchorSelectOperation.Down(e.Position, ctrl);
                                            }
                                        }
                                    }
                                }
                                break;
                            case MouseButtonType.SecondaryButton:
                                {
                                    if (Part == null)
                                        break;

                                    Part.DeselectAllAutomationPoints();
                                    mDependency.AutomationRenderer.InvalidateVisual();
                                    mDependency.AutomationRenderer.RefreshAnchorValueInput();
                                    Part.Pitch.DeselectAllAnchors();
                                    if (item is AnchorItem anchorItem)
                                    {
                                        Part.Pitch.DeletePoints([anchorItem.AnchorPoint]);
                                    }
                                    mAnchorDeleteOperation.Down(e.Position.X);
                                }
                                break;
                        }
                        break;
                    case PianoTool.Lock:
                        switch (e.MouseButtonType)
                        {
                            case MouseButtonType.PrimaryButton:
                                // 范围选区已统一为 Shift+拖；锁定笔只按 x 作用（无 y 维），故 Ctrl 在此无特殊语义。
                                if (!DetectWaveformPrimaryButton())
                                    mPitchLockOperation.Down(e.Position.X);
                                break;
                            case MouseButtonType.SecondaryButton:
                                mPitchClearOperation.Down(e.Position.X);
                                break;
                        }
                        break;
                    case PianoTool.Vibrato:
                        switch (e.MouseButtonType)
                        {
                            case MouseButtonType.PrimaryButton:
                                if (Part == null)
                                    break;

                                if (DetectWaveformPrimaryButton()) { }
                                else if (item is VibratoItem vibratoItem)
                                {
                                    mVibratoMoveOperation.Down(e.Position, ctrl, vibratoItem.Vibrato);
                                }
                                else if (item is VibratoAmplitudeItem vibratoAmplitudeItem)
                                {
                                    mVibratoAmplitudeOperation.Down(e.Position.Y, vibratoAmplitudeItem);
                                }
                                else if (item is VibratoStartResizeItem vibratoStartResizeItem)
                                {
                                    mVibratoStartResizeOperation.Down(e.Position.X, vibratoStartResizeItem.Vibrato);
                                }
                                else if (item is VibratoEndResizeItem vibratoEndResizeItem)
                                {
                                    mVibratoEndResizeOperation.Down(e.Position.X, vibratoEndResizeItem.Vibrato);
                                }
                                else if (item is VibratoFrequencyItem vibratoFrequencyItem)
                                {
                                    mVibratoFrequencyOperation.Down(e.Position.X, vibratoFrequencyItem);
                                }
                                else if (item is VibratoPhaseItem vibratoPhaseItem)
                                {
                                    mVibratoPhaseOperation.Down(e.Position.X, vibratoPhaseItem);
                                }
                                else if (item is VibratoAttackItem vibratoAttackItem)
                                {
                                    mVibratoAttackOperation.Down(e.Position.X, vibratoAttackItem);
                                }
                                else if (item is VibratoReleaseItem vibratoReleaseItem)
                                {
                                    mVibratoReleaseOperation.Down(e.Position.X, vibratoReleaseItem);
                                }
                                else
                                {
                                    // 悬浮在无颤音覆盖的音符上 → 单击落实预览颤音（鼠标位置 → 音符结尾）；否则框选。
                                    var preview = GetVibratoAddPreview(e.Position);
                                    if (preview != null)
                                    {
                                        Part.Vibratos.DeselectAllItems();
                                        var vibrato = Part.CreateVibrato(preview);
                                        vibrato.Select();
                                        Part.InsertVibrato(vibrato);
                                        // 接拖拽起点边界微调（照搬 note 双击创建并拖拽）：create + 拖拽同属一次操作、一次提交。
                                        mVibratoStartResizeOperation.Down(TickAxis.Tick2X(vibrato.GlobalStartPos()), vibrato);
                                    }
                                    else
                                    {
                                        mVibratoSelectOperation.Down(e.Position, ctrl);
                                    }
                                }
                                break;
                            case MouseButtonType.SecondaryButton:
                                {
                                    if (Part == null)
                                        break;

                                    var menu = new ContextMenu();
                                    if (item is IVibratoItem vibratoItem)
                                    {
                                        var vibrato = vibratoItem.Vibrato;
                                        if (!vibrato.IsSelected)
                                        {
                                            Part.Vibratos.DeselectAllItems();
                                            vibrato.Select();
                                        }
                                        {
                                            var menuItem = new MenuItem().SetName("Copy").SetAction(Copy).SetInputGesture(Key.C, ModifierKeys.Ctrl);
                                            menu.Items.Add(menuItem);
                                        }
                                        {
                                            var menuItem = new MenuItem().SetName("Cut").SetAction(Cut).SetInputGesture(Key.X, ModifierKeys.Ctrl);
                                            menu.Items.Add(menuItem);
                                        }
                                        {
                                            var menuItem = new MenuItem().SetName("Delete").SetAction(Delete).SetInputGesture(Key.Delete);
                                            menu.Items.Add(menuItem);
                                        }
                                    }
                                    else
                                    {
                                        if (CanPaste)
                                        {
                                            {
                                                var position = e.Position;
                                                var pos = GetQuantizedTick(TickAxis.X2Tick(position.X)) - Part.Pos.Value;
                                                var menuItem = new MenuItem().SetName("Paste").SetAction(() =>
                                                {
                                                    PasteAt(pos);
                                                }).SetInputGesture(Key.V, ModifierKeys.Ctrl);
                                                menu.Items.Add(menuItem);
                                            }
                                        }
                                    }

                                    this.OpenContextMenu(menu);
                                }
                                break;
                            default:
                                break;
                        }
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
        bool ctrl = (e.KeyModifiers & ModifierKeys.Ctrl) != 0;
        bool shift = (e.KeyModifiers & ModifierKeys.Shift) != 0;
        switch (mState)
        {
            case State.NoteSelecting:
                mNoteSelectOperation.Move(e.Position);
                break;
            case State.PitchDrawing:
                mPitchDrawOperation.Move(e.Position, ctrl);
                break;
            case State.PitchClearing:
                mPitchClearOperation.Move(e.Position.X);
                break;
            case State.PitchLocking:
                mPitchLockOperation.Move(e.Position.X);
                break;
            case State.NoteMoving:
                mNoteMoveOperation.Move(e.Position, alt, shift);
                break;
            case State.NoteStartResizing:
                mNoteStartResizeOperation.Move(e.Position.X, alt);
                break;
            case State.NoteEndResizing:
                mNoteEndResizeOperation.Move(e.Position.X, alt);
                break;
            case State.VibratoSelecting:
                mVibratoSelectOperation.Move(e.Position);
                break;
            case State.VibratoStartResizing:
                mVibratoStartResizeOperation.Move(e.Position.X, alt);
                break;
            case State.VibratoEndResizing:
                mVibratoEndResizeOperation.Move(e.Position.X, alt);
                break;
            case State.VibratoAmplitudeAdjusting:
                mVibratoAmplitudeOperation.Move(e.Position.Y);
                break;
            case State.VibratoFrequencyAdjusting:
                mVibratoFrequencyOperation.Move(e.Position.X);
                break;
            case State.VibratoPhaseAdjusting:
                mVibratoPhaseOperation.Move(e.Position.X);
                break;
            case State.VibratoAttackAdjusting:
                mVibratoAttackOperation.Move(e.Position.X);
                break;
            case State.VibratoReleaseAdjusting:
                mVibratoReleaseOperation.Move(e.Position.X);
                break;
            case State.VibratoMoving:
                mVibratoMoveOperation.Move(e.Position, alt);
                break;
            case State.WaveformPhonemeResizing:
                mWaveformPhonemeResizeOperation.Move(e.Position.X, alt);
                break;
            case State.SelectionCreating:
                Cursor = new Cursor(StandardCursorType.Ibeam);
                mSelectionOperation.Move(e.Position.X, alt);
                break;
            case State.AnchorSelecting:
                mAnchorSelectOperation.Move(e.Position);
                break;
            case State.AnchorDeleting:
                mAnchorDeleteOperation.Move(e.Position.X);
                break;
            case State.AnchorMoving:
                mAnchorMoveOperation.Move(e.Position);
                break;
            default:
                UpdateSynthesisHover(e.Position);   // 合成状态条 hover 延时计时（进区域起表/划走取消）
                var item = ItemAt(e.Position);
                if (shift && !IsShiftNoteMove(item))
                {
                    Cursor = new Cursor(StandardCursorType.Ibeam);   // Shift = 画范围选区模式：光标即时提示（note 本体上是约束移动，不提示）
                    break;
                }
                if (item is WaveformPhonemeResizeItem || item is WaveformNoteStartResizeItem || item is WaveformNoteEndResizeItem)
                {
                    Cursor = new Cursor(StandardCursorType.SizeWestEast);
                }
                else if (item is WaveformBackItem)
                {
                    Cursor = null;
                }
                else
                {
                    switch (mDependency.PianoTool.Value)
                    {
                        case PianoTool.Note:
                            if (item is NoteStartResizeItem || item is NoteEndResizeItem)
                            {
                                Cursor = new Cursor(StandardCursorType.SizeWestEast);
                            }
                            else
                            {
                                Cursor = null;
                            }
                            break;
                        case PianoTool.Vibrato:
                            if (item is VibratoStartResizeItem || item is VibratoEndResizeItem || item is VibratoFrequencyItem || item is VibratoPhaseItem || item is VibratoAttackItem || item is VibratoReleaseItem)
                            {
                                Cursor = new Cursor(StandardCursorType.SizeWestEast);
                            }
                            else if (item is VibratoAmplitudeItem)
                            {
                                Cursor = new Cursor(StandardCursorType.SizeNorthSouth);
                            }
                            else
                            {
                                Cursor = null;
                            }
                            break;
                        case PianoTool.Pitch:
                        case PianoTool.Lock:
                            Cursor = null;
                            break;
                        default:
                            Cursor = null;
                            break;
                    }
                }
                break;
        }

        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseUpEventArgs e)
    {
        switch (mState)
        {
            case State.NoteSelecting:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mNoteSelectOperation.Up();
                break;
            case State.PitchDrawing:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mPitchDrawOperation.Up();
                break;
            case State.PitchClearing:
                if (e.MouseButtonType == MouseButtonType.SecondaryButton)
                    mPitchClearOperation.Up();
                break;
            case State.PitchLocking:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mPitchLockOperation.Up();
                break;
            case State.NoteMoving:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mNoteMoveOperation.Up();
                break;
            case State.NoteStartResizing:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mNoteStartResizeOperation.Up();
                break;
            case State.NoteEndResizing:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mNoteEndResizeOperation.Up();
                break;
            case State.VibratoSelecting:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mVibratoSelectOperation.Up();
                break;
            case State.VibratoStartResizing:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mVibratoStartResizeOperation.Up();
                break;
            case State.VibratoEndResizing:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mVibratoEndResizeOperation.Up();
                break;
            case State.VibratoAmplitudeAdjusting:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mVibratoAmplitudeOperation.Up();
                break;
            case State.VibratoFrequencyAdjusting:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mVibratoFrequencyOperation.Up();
                break;
            case State.VibratoPhaseAdjusting:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mVibratoPhaseOperation.Up();
                break;
            case State.VibratoAttackAdjusting:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mVibratoAttackOperation.Up();
                break;
            case State.VibratoReleaseAdjusting:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mVibratoReleaseOperation.Up();
                break;
            case State.VibratoMoving:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mVibratoMoveOperation.Up();
                break;
            case State.WaveformPhonemeResizing:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mWaveformPhonemeResizeOperation.Up();
                break;
            case State.SelectionCreating:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mSelectionOperation.Up();
                break;
            case State.AnchorSelecting:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mAnchorSelectOperation.Up();
                break;
            case State.AnchorDeleting:
                if (e.MouseButtonType == MouseButtonType.SecondaryButton)
                    mAnchorDeleteOperation.Up();
                break;
            case State.AnchorMoving:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mAnchorMoveOperation.Up();
                break;
            default:
                break;
        }

        // 主键点击(未拖，位移 ≤ 阈值)即清空范围选区：非 Shift 点击=工具动作(框选零矩形/落点)、Shift 点击=零宽选区，二者都该清。
        // 真拖(画区/框选/移动)位移超阈值不触发；右键永不参与(留给范围选区菜单 / 工具右键)。镜像编排区 TrackScrollView 的处理。
        if (e.MouseButtonType == MouseButtonType.PrimaryButton)
        {
            var d = e.Position - mPrimaryDownPos;
            if (d.X * d.X + d.Y * d.Y <= ClickThreshold * ClickThreshold)
                ClearRegionSelection();
        }

        if (e.MouseButtonType == MouseButtonType.MiddleButton)
            mMiddleDragOperation.Up();
    }

    // Shift+主键在此落点是否该走"约束移动"（拖动中锁 pos 只变音高）而非画范围选区：Note 工具 + note 本体。
    // 边缘缩放/波形带/其他工具不在内——那些场景 Shift 无移动语义，保持造区。光标提示与按下分流共用此判定。
    bool IsShiftNoteMove(Item? item) => mDependency.PianoTool.Value == PianoTool.Note && item is NoteItem;

    protected override void OnKeyDownEvent(KeyEventArgs e)
    {
        switch (mState)
        {
            case State.NoteStartResizing:
                if (e.Key == Key.LeftAlt)
                {
                    mNoteStartResizeOperation.Move(MousePosition.X, true);
                    e.Handled = true;
                }
                break;
            case State.NoteEndResizing:
                if (e.Key == Key.LeftAlt)
                {
                    mNoteEndResizeOperation.Move(MousePosition.X, true);
                    e.Handled = true;
                }
                break;
            case State.NoteMoving:
                if (e.Key == Key.LeftAlt || e.Key == Key.LeftShift)
                {
                    mNoteMoveOperation.Move(MousePosition, (e.KeyModifiers & KeyModifiers.Alt) != 0, (e.KeyModifiers & KeyModifiers.Shift) != 0);
                    e.Handled = true;
                }
                break;
            case State.VibratoStartResizing:
                if (e.Key == Key.LeftAlt)
                {
                    mVibratoStartResizeOperation.Move(MousePosition.X, true);
                    e.Handled = true;
                }
                break;
            case State.VibratoEndResizing:
                if (e.Key == Key.LeftAlt)
                {
                    mVibratoEndResizeOperation.Move(MousePosition.X, true);
                    e.Handled = true;
                }
                break;
            case State.VibratoMoving:
                if (e.Key == Key.LeftAlt)
                {
                    mVibratoMoveOperation.Move(MousePosition, true);
                    e.Handled = true;
                }
                break;
            case State.None:
                // Shift 按下即进"画范围选区"模式 → 光标变 I-beam（不依赖鼠标移动即时反馈）；悬停 note 本体时是约束移动，不变。
                if ((e.Key is Key.LeftShift or Key.RightShift) && Part != null)
                {
                    Cursor = IsShiftNoteMove(ItemAt(MousePosition)) ? null : new Cursor(StandardCursorType.Ibeam);
                    e.Handled = true;
                }
                break;
        }
    }

    protected override void OnKeyUpEvent(KeyEventArgs e)
    {
        switch (mState)
        {
            case State.NoteStartResizing:
                if (e.Key == Key.LeftAlt)
                {
                    mNoteStartResizeOperation.Move(MousePosition.X, false);
                    e.Handled = true;
                }
                break;
            case State.NoteEndResizing:
                if (e.Key == Key.LeftAlt)
                {
                    mNoteEndResizeOperation.Move(MousePosition.X, false);
                    e.Handled = true;
                }
                break;
            case State.NoteMoving:
                if (e.Key == Key.LeftAlt || e.Key == Key.LeftShift)
                {
                    mNoteMoveOperation.Move(MousePosition, (e.KeyModifiers & KeyModifiers.Alt) != 0, (e.KeyModifiers & KeyModifiers.Shift) != 0);
                    e.Handled = true;
                }
                break;
            case State.VibratoStartResizing:
                if (e.Key == Key.LeftAlt)
                {
                    mVibratoStartResizeOperation.Move(MousePosition.X, false);
                    e.Handled = true;
                }
                break;
            case State.VibratoEndResizing:
                if (e.Key == Key.LeftAlt)
                {
                    mVibratoEndResizeOperation.Move(MousePosition.X, false);
                    e.Handled = true;
                }
                break;
            case State.VibratoMoving:
                if (e.Key == Key.LeftAlt)
                {
                    mVibratoMoveOperation.Move(MousePosition, false);
                    e.Handled = true;
                }
                break;
            case State.None:
                if (e.Key is Key.LeftShift or Key.RightShift)
                {
                    Cursor = null;   // 松 Shift：复位（下次移动按 item/tool 重算）
                    e.Handled = true;
                }
                break;
        }
    }

    protected override void UpdateItems(IItemCollection items)
    {
        mPreviewPitchItem = null;
        if (Part == null)
            return;

        double startPos = TickAxis.MinVisibleTick;
        double endPos = TickAxis.MaxVisibleTick;
        var tempoManager = Part.TempoManager;
        var viewStartTime = tempoManager.GetTime(startPos);
        var viewEndTime = tempoManager.GetTime(endPos);
        double partPos = Part.Pos.Value;

        switch (mDependency.PianoTool.Value)
        {
            case PianoTool.Note:
                foreach (var note in Part.Notes)
                {
                    if (note.GlobalEndPos() < startPos)
                        continue;

                    if (note.GlobalStartPos() > endPos)
                        break;

                    items.Add(new NoteItem(this) { Note = note });
                    if (!string.IsNullOrEmpty(note.FinalPronunciation()))
                    {
                        items.Add(new NotePronunciationItem(this) { Note = note });
                    }
                    items.Add(new NoteEndResizeItem(this) { Note = note });
                    items.Add(new NoteStartResizeItem(this) { Note = note });
                }
                break;
            case PianoTool.Vibrato:
                foreach (var vibrato in Part.Vibratos)
                {
                    if (vibrato.GlobalEndPos() < startPos)
                        continue;

                    if (vibrato.GlobalStartPos() > endPos)
                        break;

                    items.Add(new VibratoItem(this) { Vibrato = vibrato });
                    if (!vibrato.IsSelected)
                        continue;

                    items.Add(new VibratoAmplitudeItem(this) { Vibrato = vibrato });
                    items.Add(new VibratoFrequencyItem(this) { Vibrato = vibrato });
                    items.Add(new VibratoPhaseItem(this) { Vibrato = vibrato });
                    items.Add(new VibratoEndResizeItem(this) { Vibrato = vibrato });
                    items.Add(new VibratoStartResizeItem(this) { Vibrato = vibrato });
                    items.Add(new VibratoAttackItem(this) { Vibrato = vibrato });
                    items.Add(new VibratoReleaseItem(this) { Vibrato = vibrato });
                }
                break;
            case PianoTool.Anchor:
                foreach (var anchorGroup in Part.Pitch.AnchorGroups)
                {
                    if (partPos + anchorGroup.End < startPos)
                        continue;

                    if (partPos + anchorGroup.Start > endPos)
                        break;

                    foreach (var anchor in anchorGroup)
                    {
                        if (partPos + anchor.Pos < startPos)
                            continue;

                        if (partPos + anchor.Pos > endPos)
                            break;

                        items.Add(new AnchorItem(this) { AnchorPoint = anchor });
                    }
                }
                if (mState != State.None)
                    break;

                if (!IsHover)
                    break;

                var hoverItem = HoverItem();
                var hoverAnchor = (hoverItem as AnchorItem)?.AnchorPoint;

                IAnchorGroup? hoverAnchorOnFirstGroup = null;
                IAnchorGroup? hoverAnchorOnLastGroup = null;
                int hoverAnchorGroupIndex = -1;
                for (int i = 0; i < Part.Pitch.AnchorGroups.Count; i++)
                {
                    var anchorGroup = Part.Pitch.AnchorGroups[i];
                    if (anchorGroup.IsEmpty())
                        continue;

                    if (anchorGroup.ConstFirst() == hoverAnchor)
                    {
                        hoverAnchorOnFirstGroup = anchorGroup;
                        hoverAnchorGroupIndex = i;
                    }
                    if (anchorGroup.ConstLast() == hoverAnchor)
                    {
                        hoverAnchorOnLastGroup = anchorGroup;
                        hoverAnchorGroupIndex = i;
                    }

                    if (hoverAnchorGroupIndex != -1)
                        break;
                }

                // 悬浮到非首尾锚点上，忽略预览
                if (hoverAnchor != null && hoverAnchorOnFirstGroup == null && hoverAnchorOnLastGroup == null)
                    break;

                var pos = TickAxis.X2Tick(MousePosition.X) - Part.Pos.Value;
                var areaID = Part.Pitch.GetAreaID(pos);
                int[] previewIndex = hoverAnchor == null ?
                    areaID.IsInGroup ? [areaID.Index] : [areaID.LeftIndex, areaID.RightIndex] :
                    [.. (hoverAnchorOnFirstGroup == null ? Array.Empty<int>() : [hoverAnchorGroupIndex - 1]), hoverAnchorGroupIndex, .. (hoverAnchorOnLastGroup == null ? Array.Empty<int>() : [hoverAnchorGroupIndex + 1])];
                var previewInfo = previewIndex
                    .Where(index => (uint)index < Part.Pitch.AnchorGroups.Count)
                    .Select(index => Part.Pitch.AnchorGroups[index])
                    .Where(anchorGroup => anchorGroup.HasSelectedItem() || anchorGroup == hoverAnchorOnFirstGroup || anchorGroup == hoverAnchorOnLastGroup)
                    .Select(anchorGroup => anchorGroup.GetInfo().Select(p => p.ToPoint()).ToList()).ToList();

                if (previewInfo.Count == 0)
                    break;

                mPreviewPitchItem = new PreviewAnchorGroupItem(this) { PiecewiseAutomation = new PiecewiseAutomation() };
                mPreviewPitchItem.PiecewiseAutomation.SetInfo(previewInfo);
                if (hoverAnchor == null)
                {
                    foreach (var anchorGroup in mPreviewPitchItem.PiecewiseAutomation.AnchorGroups)
                    {
                        anchorGroup[0].Select();
                    }
                    mPreviewPitchItem.PiecewiseAutomation.InsertPoint(hoverAnchor ?? new AnchorPoint(pos, PitchAxis.Y2Pitch(MousePosition.Y) - 0.5));
                    mPreviewPitchItem.OnDown += (e, ctrl) =>
                    {
                        var anchor = new AnchorPoint(TickAxis.X2Tick(e.Position.X) - Part.Pos.Value, PitchAxis.Y2Pitch(e.Position.Y) - 0.5) { IsSelected = true };
                        Part.Pitch.InsertPoint(anchor);
                        Part.Pitch.DeselectAllAnchors();
                        anchor.Select();
                        mAnchorMoveOperation.Down(e.Position, ctrl, anchor);
                    };
                }
                else
                {
                    // 先处理向后连接的，顺序不能乱！
                    if (hoverAnchorOnLastGroup != null)
                    {
                        mPreviewPitchItem.PiecewiseAutomation.ConnectAnchorGroup(0);
                        if (hoverAnchorGroupIndex + 1 < Part.Pitch.AnchorGroups.Count && Part.Pitch.AnchorGroups[hoverAnchorGroupIndex + 1].HasSelectedItem())
                            mPreviewPitchItem.OnDown += (_, _) => Part.Pitch.ConnectAnchorGroup(hoverAnchorGroupIndex);
                    }
                    if (hoverAnchorOnFirstGroup != null)
                    {
                        mPreviewPitchItem.PiecewiseAutomation.ConnectAnchorGroup(0);
                        if (hoverAnchorGroupIndex - 1 >= 0 && Part.Pitch.AnchorGroups[hoverAnchorGroupIndex - 1].HasSelectedItem())
                            mPreviewPitchItem.OnDown += (_, _) => Part.Pitch.ConnectAnchorGroup(hoverAnchorGroupIndex - 1);
                    }
                    if (mPreviewPitchItem.OnDown != null)
                        mPreviewPitchItem.OnDown += (e, ctrl) => mAnchorMoveOperation.Down(e.Position, ctrl, hoverAnchor);
                }
                items.Add(mPreviewPitchItem);

                break;
            default:
                break;
        }

        // 波形带收起时不绘制、也不暴露任何音素/note 边界拖拽热区。
        if (!mDependency.IsWaveformVisible)
            return;

        items.Add(new WaveformBackItem(this));

        // 波形/音素带只暴露音素边界拖拽热区；note 边界缩放归钢琴窗上方的 note 矩形，避免两类热区在边界处打架。
        foreach (var note in Part.Notes)
        {
            var phonemes = note.DisplayPhonemes;
            if (phonemes.IsEmpty())
                continue;

            var startTime = phonemes.ConstFirst().StartTime;
            var endTime = phonemes.ConstLast().EndTime;
            if (endTime < viewStartTime)
                continue;

            if (startTime > viewEndTime)
                break;

            // 只为 n 个起边界建拖拽柄（0..n-1）；末音素的尾是派生量（own 尾 + 乘客铺设 + 后盖前），不可拖、无柄。
            for (var i = 0; i < phonemes.Count; i++)
            {
                items.Add(new WaveformPhonemeResizeItem(this) { Note = note, PhonemeIndex = i });
            }

            // noteon 缩放柄恒有（音素带内短拉杆；后加，故在核起点处优先于 no-op 音素柄）。与上个 note 相接时为共享边界（见 Down）。
            items.Add(new WaveformNoteStartResizeItem(this) { Note = note });
            // noteoff 缩放柄仅当与下一个 note **不相接**（有空隙 / 无下个）才有——相接（重叠 / 相邻 / 延音符）时该边界归下个
            // note 的 noteon 共享柄、或被 melisma / 重叠覆盖，此处不画也不可拖。此时末音素恰好结束在 note 末、自带刻度即手柄视觉。
            var nextNote = note.Next;
            if (nextNote == null || nextNote.StartPos() > note.EndPos() + 1e-6)
                items.Add(new WaveformNoteEndResizeItem(this) { Note = note });
        }
    }

    // note 是否有可显示音素（钉死或合成）；无则音素带里它什么都没有、无任何拖柄。
    static bool WaveformHasPhonemes(INote note)
        => !note.Phonemes.IsEmpty() || (note.SynthesizedPhonemes != null && !note.SynthesizedPhonemes.IsEmpty());

    protected override void OnMouseEnter(MouseEnterEventArgs e)
    {
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseLeaveEventArgs e)
    {
        InvalidateVisual();
    }

    class Operation(PianoScrollView pianoScrollView)
    {
        public PianoScrollView PianoScrollView => pianoScrollView;
        public State State { get => PianoScrollView.mState; set => PianoScrollView.mState = value; }
    }

    class MiddleDragOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public bool IsOperating => mIsDragging;

        public void Down(Avalonia.Point point)
        {
            if (mIsDragging)
                return;

            mIsDragging = true;
            mDownTick = PianoScrollView.TickAxis.X2Tick(point.X);
            mDownPitch = PianoScrollView.PitchAxis.Y2Pitch(point.Y);
            PianoScrollView.TickAxis.StopMoveAnimation();
            PianoScrollView.PitchAxis.StopMoveAnimation();
        }

        public void Move(Avalonia.Point point)
        {
            if (!mIsDragging)
                return;

            PianoScrollView.TickAxis.MoveTickToX(mDownTick, point.X);
            PianoScrollView.PitchAxis.MovePitchToY(mDownPitch, point.Y);
        }

        public void Up()
        {
            if (!mIsDragging)
                return;

            mIsDragging = false;
        }

        double mDownTick;
        double mDownPitch;
        bool mIsDragging = false;
    }

    readonly MiddleDragOperation mMiddleDragOperation;

    abstract class SelectOperation<T>(PianoScrollView pianoScrollView) : Operation(pianoScrollView) where T : ISelectable
    {
        public bool IsOperating => State == SelectState;

        public void Down(Avalonia.Point point, bool ctrl)
        {
            if (State != State.None)
                return;

            if (Collection == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            State = SelectState;
            PianoScrollView.ClearRegionSelection();   // 框选对象即清掉范围选区（二者对 copy 互斥消歧）
            mDownTick = PianoScrollView.TickAxis.X2Tick(point.X) - PianoScrollView.Part.Pos.Value;
            mDownPitch = PianoScrollView.PitchAxis.Y2Pitch(point.Y);
            if (ctrl)
            {
                mSelectedItems = Collection.AllSelectedItems();
            }
            Move(point);
        }

        public void Move(Avalonia.Point point)
        {
            if (!IsOperating)
                return;

            if (PianoScrollView.Part == null)
                return;

            mTick = PianoScrollView.TickAxis.X2Tick(point.X) - PianoScrollView.Part.Pos.Value;
            mPitch = PianoScrollView.PitchAxis.Y2Pitch(point.Y);
            if (Collection == null)
                return;

            BeginSelect();
            Collection.DeselectAllItems();
            if (mSelectedItems != null)
            {
                foreach (var item in mSelectedItems)
                    item.Select();
            }
            double minTick = Math.Min(mTick, mDownTick);
            double maxTick = Math.Max(mTick, mDownTick);
            double minPitch = Math.Min(mPitch, mDownPitch);
            double maxPitch = Math.Max(mPitch, mDownPitch);
            Select(Collection, minTick, maxTick, minPitch, maxPitch);
            EndSelect();
            PianoScrollView.InvalidateVisual();
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            State = State.None;
            PianoScrollView.InvalidateVisual();
            mSelectedItems = null;
        }

        public Rect SelectionRect()
        {
            double pos = PianoScrollView.Part == null ? 0 : PianoScrollView.Part.Pos.Value;
            double minTick = Math.Min(mTick, mDownTick) + pos;
            double maxTick = Math.Max(mTick, mDownTick) + pos;
            double minPitch = Math.Min(mPitch, mDownPitch);
            double maxPitch = Math.Max(mPitch, mDownPitch);
            double left = PianoScrollView.TickAxis.Tick2X(minTick);
            double right = PianoScrollView.TickAxis.Tick2X(maxTick);
            double top = PianoScrollView.PitchAxis.Pitch2Y(maxPitch);
            double bottom = PianoScrollView.PitchAxis.Pitch2Y(minPitch);
            return new Rect(left, top, right - left, bottom - top);
        }

        protected abstract State SelectState { get; } 
        protected abstract IEnumerable<T>? Collection { get; }
        protected abstract void Select(IEnumerable<T> items, double minTick, double maxTick, double minPitch, double maxPitch);
        protected virtual void BeginSelect() { }
        protected virtual void EndSelect() { }

        IReadOnlyCollection<T>? mSelectedItems = null;
        double mDownTick;
        double mDownPitch;
        double mTick;
        double mPitch;
    }

    class NoteSelectOperation(PianoScrollView pianoScrollView) : SelectOperation<INote>(pianoScrollView)
    {
        protected override State SelectState => State.NoteSelecting;
        protected override IEnumerable<INote>? Collection => PianoScrollView.Part?.Notes;

        protected override void Select(IEnumerable<INote> notes, double minTick, double maxTick, double minPitch, double maxPitch)
        {
            foreach (var note in notes)
            {
                if (note.EndPos() < minTick)
                    continue;

                if (note.StartPos() > maxTick)
                    break;

                if (note.Pitch.Value + 1 > minPitch && note.Pitch.Value < maxPitch)
                    note.Select();
            }
        }

        protected override void BeginSelect()
        {
            PianoScrollView.Part?.Notes.SelectionChanged.BeginMerge();
        }

        protected override void EndSelect()
        {
            PianoScrollView.Part?.Notes.SelectionChanged.EndMerge();
        }
    }

    readonly NoteSelectOperation mNoteSelectOperation;

    class PitchDrawOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public bool IsOperating => State == State.PitchDrawing;

        public void Down(Avalonia.Point mousePosition, bool constantValue)
        {
            if (IsOperating)
                return;

            if (PianoScrollView.Part == null)
                return;

            State = State.PitchDrawing;
            PianoScrollView.Part.BeginMergeDirty();
            mHead = PianoScrollView.Part.Head;
            mDownValue = PianoScrollView.PitchAxis.Y2Pitch(mousePosition.Y) - 0.5;   // 锁定按下时的 y，供定值绘制保值

            mPointLines.Add([ToTickAndValue(mousePosition, constantValue)]);
            PianoScrollView.Part.Pitch.AddLine(mPointLines[0], Settings.ParameterBoundaryExtension);
        }

        public void Move(Avalonia.Point mousePosition, bool constantValue)
        {
            if (!IsOperating)
                return;

            if (PianoScrollView.Part == null)
                return;

            var point = ToTickAndValue(mousePosition, constantValue);
            var lastLine = mPointLines.Last();
            var lastPoint = mDirection ? lastLine.Last() : lastLine.First();
            if (lastPoint.X == point.X)
            {
                if (lastPoint.Y == point.Y)
                    return;

                lastLine[mDirection ? lastLine.Count - 1 : 0] = point;
            }
            else
            {
                bool direction = point.X > lastPoint.X;
                if (lastLine.Count == 1)
                {
                    lastLine.Insert(direction ? 1 : 0, point);
                }
                else
                {
                    if (direction == mDirection)
                        lastLine.Insert(direction ? lastLine.Count : 0, point);
                    else
                        mPointLines.Add(direction ? [lastPoint, point] : [point, lastPoint]);
                }

                mDirection = direction;
            }

            PianoScrollView.Part.Pitch.DiscardTo(mHead);
            foreach (var line in mPointLines)
            {
                PianoScrollView.Part.Pitch.AddLine(line.Simplify(5, 2), Settings.ParameterBoundaryExtension);
            }
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            State = State.None;

            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.Part.Pitch.DiscardTo(mHead);
            PianoScrollView.Part.EndMergeDirty();
            foreach (var line in mPointLines)
            {
                PianoScrollView.Part.Pitch.AddLine(line.Simplify(5, 2), Settings.ParameterBoundaryExtension);
            }
            PianoScrollView.Part.Pitch.Commit();
            mPointLines.Clear();
        }

        Point ToTickAndValue(Avalonia.Point mousePosition, bool constantValue)
        {
            double value = constantValue ? mDownValue : PianoScrollView.PitchAxis.Y2Pitch(mousePosition.Y) - 0.5;
            return new(PianoScrollView.TickAxis.X2Tick(mousePosition.X) - PianoScrollView.Part!.Pos.Value, value);
        }

        bool mDirection;
        double mDownValue;   // 定值绘制锁定的 y（按下时捕获）
        readonly List<List<Point>> mPointLines = new();
        Head mHead;
    }

    readonly PitchDrawOperation mPitchDrawOperation;

    class PitchClearOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public bool IsOperating => State == State.PitchClearing;

        public void Down(double x)
        {
            if (IsOperating)
                return;

            if (PianoScrollView.Part == null)
                return;

            State = State.PitchClearing;
            PianoScrollView.Part.BeginMergeDirty();
            mHead = PianoScrollView.Part.Head;
            double tick = PianoScrollView.TickAxis.X2Tick(x) - PianoScrollView.Part.Pos.Value;
            mStart = tick;
            mEnd = tick;
            PianoScrollView.Part.Pitch.Clear(mStart, mEnd);
        }

        public void Move(double x)
        {
            if (!IsOperating)
                return;

            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.Part.Pitch.DiscardTo(mHead);
            double tick = PianoScrollView.TickAxis.X2Tick(x) - PianoScrollView.Part.Pos.Value;
            mStart = Math.Min(mStart, tick);
            mEnd = Math.Max(mEnd, tick);
            PianoScrollView.Part.Pitch.Clear(mStart, mEnd);
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            State = State.None;

            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.Part.Pitch.DiscardTo(mHead);
            PianoScrollView.Part.Pitch.Clear(mStart, mEnd);
            PianoScrollView.Part.EndMergeDirty();
            PianoScrollView.Part.Pitch.Commit();
        }

        double mStart;
        double mEnd;
        Head mHead;
    }

    readonly PitchClearOperation mPitchClearOperation;

    class PitchLockOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public bool IsOperating => State == State.PitchLocking;

        public void Down(double x)
        {
            if (IsOperating)
                return;

            if (PianoScrollView.Part == null)
                return;

            State = State.PitchLocking;
            PianoScrollView.Part.BeginMergeDirty();
            mHead = PianoScrollView.Part.Head;
            double tick = PianoScrollView.TickAxis.X2Tick(x) - PianoScrollView.Part.Pos.Value;
            mStart = tick;
            mEnd = tick;
            PianoScrollView.Part.LockPitch(mStart, mEnd, Settings.ParameterBoundaryExtension);
        }

        public void Move(double x)
        {
            if (!IsOperating)
                return;

            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.Part.DiscardTo(mHead);
            double tick = PianoScrollView.TickAxis.X2Tick(x) - PianoScrollView.Part.Pos.Value;
            mStart = Math.Min(mStart, tick);
            mEnd = Math.Max(mEnd, tick);
            PianoScrollView.Part.LockPitch(mStart, mEnd, Settings.ParameterBoundaryExtension);
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            State = State.None;

            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.Part.DiscardTo(mHead);
            PianoScrollView.Part.LockPitch(mStart, mEnd, Settings.ParameterBoundaryExtension);
            PianoScrollView.Part.EndMergeDirty();
            PianoScrollView.Part.Commit();
        }

        double mStart;
        double mEnd;
        Head mHead;
    }

    readonly PitchLockOperation mPitchLockOperation;

    class NoteMoveOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public void Down(Avalonia.Point point, bool ctrl, INote note)
        {
            if (PianoScrollView.Part == null)
                return;

            mCtrl = ctrl;
            mIsSelected = note.IsSelected;
            if (!mCtrl && !mIsSelected)
            {
                PianoScrollView.Part.Notes.DeselectAllItems();
            }
            note.Select();

            var (minPitch, maxPitch) = PianoScrollView.Part.PitchRange();
            foreach (var selectedNote in PianoScrollView.Part.Notes.Where(note => note.IsSelected))
            {
                mMoveNotes.Add(selectedNote);

                if (selectedNote.Pitch.Value < minPitch)
                    minPitch = selectedNote.Pitch.Value;

                if (selectedNote.Pitch.Value > maxPitch)
                    maxPitch = selectedNote.Pitch.Value;
            }
            if (mMoveNotes.IsEmpty())
                return;

            State = State.NoteMoving;
            PianoScrollView.Part.BeginMergeDirty();
            mHead = PianoScrollView.Part.Head;
            mNote = note;
            mDownPartPos = mNote.GlobalStartPos();
            mTickOffset = PianoScrollView.TickAxis.X2Tick(point.X) - PianoScrollView.Part.Pos.Value - note.Pos.Value;
            mPitch = note.Pitch.Value;
            mMinPitch = minPitch;
            mMaxPitch = maxPitch;
        }

        public void Move(Avalonia.Point point, bool alt, bool shift)
        {
            var part = PianoScrollView.Part;
            if (part == null)
                return;

            if (mNote == null)
                return;

            if (mMoveNotes.IsEmpty())
                return;

            int pitch = (int)PianoScrollView.PitchAxis.Y2Pitch(point.Y);
            int pitchOffset = (pitch - mPitch).Limit(MusicTheory.MIN_PITCH - mMinPitch, MusicTheory.MAX_PITCH - mMaxPitch);
            double pos = PianoScrollView.TickAxis.X2Tick(point.X) - mTickOffset;
            if (!alt) pos = PianoScrollView.GetQuantizedTick(pos);
            double posOffset = pos - mDownPartPos;
            if (shift) posOffset = 0;
            if (posOffset == mLastPosOffset && pitchOffset == mLastPitchOffset)
                return;

            mLastPosOffset = posOffset;
            mLastPitchOffset = pitchOffset;
            mMoved = true;
            part.DiscardTo(mHead);
            part.BeginMergeDirty();
            part.Notes.BeginMergeNotify();
            List<List<List<Point>>> pitchInfos = new();
            Dictionary<string, List<List<Point>>> automationInfos = new();
            if (Settings.ParameterSyncMode)
            {
                foreach (var note in mMoveNotes)
                {
                    var pitchInfo = part.Pitch.RangeInfo(note.StartPos(), note.EndPos());
                    foreach (var line in pitchInfo)
                    {
                        for (int i = 0; i < line.Count; i++)
                        {
                            line[i] = new(line[i].X + note.StartPos() + posOffset, line[i].Y + pitchOffset);
                        }
                    }
                    pitchInfos.Add(pitchInfo);

                    foreach (var kvp in part.Automations)
                    {
                        var autoInfo = kvp.Value.RangeInfo(note.StartPos(), note.EndPos());
                        for (int i = 0; i < autoInfo.Count; i++)
                        {
                            autoInfo[i] = new(autoInfo[i].X + note.StartPos() + posOffset, autoInfo[i].Y);
                        }
                        if (!automationInfos.TryGetValue(kvp.Key, out var list))
                        {
                            list = new List<List<Point>>();
                            automationInfos[kvp.Key] = list;
                        }
                        list.Add(autoInfo);
                    }
                }
            }

            part.MoveNotes(mMoveNotes, () =>
            {
                foreach (var note in mMoveNotes)
                {
                    note.Pos.Set(note.Pos.Value + posOffset);
                    note.Pitch.Set(note.Pitch.Value + pitchOffset);
                }
                if (Settings.ParameterSyncMode)
                {
                    foreach (var note in mMoveNotes)
                    {
                        part.Pitch.Clear(note.StartPos(), note.EndPos());
                        foreach (var kvp in part.Automations)
                        {
                            kvp.Value.Clear(note.StartPos(), note.EndPos(), Settings.ParameterBoundaryExtension);
                        }
                    }
                }
            });
            if (Settings.ParameterSyncMode)
            {
                foreach (var info in pitchInfos)
                {
                    foreach (var line in info)
                    {
                        part.Pitch.AddLine(line, Settings.ParameterBoundaryExtension);
                    }
                }

                foreach (var kvp in automationInfos)
                {
                    if (!part.Automations.TryGetValue(kvp.Key, out var automation))
                        continue;

                    var defaultValue = automation.DefaultValue.Value;
                    foreach (var line in kvp.Value)
                    {
                        automation.AddLine(line.Convert(p => new Point(p.X, p.Y + defaultValue)), Settings.ParameterBoundaryExtension);
                    }
                }
            }
            part.Notes.EndMergeNotify();
            part.EndMergeDirty();
        }

        public void Up()
        {
            State = State.None;

            if (mNote == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.Part.EndMergeDirty();
            if (mMoved)
            {
                PianoScrollView.Part.Commit();
            }
            else
            {
                PianoScrollView.Part.Discard();
                if (mCtrl)
                {
                    if (mIsSelected)
                    {
                        mNote.Inselect();
                    }
                }
                else
                {
                    PianoScrollView.Part.Notes.DeselectAllItems();
                    mNote.Select();
                }
            }
            mMoved = false;
            mNote = null;
            mMoveNotes.Clear();
            mLastPosOffset = 0;
            mLastPitchOffset = 0;
        }

        INote? mNote;
        List<INote> mMoveNotes = new();

        bool mCtrl;
        bool mIsSelected;
        bool mMoved = false;
        double mDownPartPos;
        double mTickOffset;
        int mPitch;
        int mMinPitch;
        int mMaxPitch;

        double mLastPosOffset = 0;
        int mLastPitchOffset = 0;
        Head mHead;
    }

    readonly NoteMoveOperation mNoteMoveOperation;

    class NoteStartResizeOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        // coupledPrev 非空（音素带共享边界）：本 note 头移动时，联动把上个 note 的尾跟到同一处——拖的是两 note
        // 的分界线（外侧两端不动）。此模式下夹在 [上个 note 起点+一格, 本 note 末-一格]，两 note 均保正长、不删除。
        public void Down(double x, INote note, INote? coupledPrev = null)
        {
            if (PianoScrollView.Part == null)
                return;

            State = State.NoteStartResizing;
            PianoScrollView.Part.BeginMergeDirty();
            mHead = PianoScrollView.Part.Head;
            mNote = note;
            mCoupledPrev = coupledPrev;
            double start = PianoScrollView.TickAxis.Tick2X(mNote.GlobalStartPos());
            mOffset = x - start;
        }

        public void Move(double x, bool alt)
        {
            if (mNote == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.Part.DiscardTo(mHead);
            double start = x - mOffset;
            double startTick = PianoScrollView.TickAxis.X2Tick(start);
            if (!alt) startTick = Math.Min(PianoScrollView.GetQuantizedTick(startTick), PianoScrollView.GetQuantizedTick(mNote.GlobalEndPos()) - PianoScrollView.QuantizedCellTicks());
            startTick -= PianoScrollView.Part.Pos.Value;

            if (mCoupledPrev != null)
            {
                // 共享边界：夹在两 note 之间，均保正长、不删除任一。
                double cell = PianoScrollView.QuantizedCellTicks();
                double lo = mCoupledPrev.StartPos() + cell;
                double hi = mNote.EndPos() - cell;
                if (hi < lo) hi = lo;
                startTick = Math.Clamp(startTick, lo, hi);
            }
            else if (startTick >= mNote.EndPos())
            {
                PianoScrollView.Part.RemoveNote(mNote);
                return;
            }

            PianoScrollView.Part.BeginMergeDirty();
            double offsetTick = startTick - mNote.StartPos();
            // voice（去重叠单声部）：头部左拉推挤更早的邻居——起点被越过者删除、尾巴被盖到者截断到新起点。
            // instrument（可重叠）：不动邻居，自由拉出重叠。coupled 路径边界已钳位联动，无新重叠可推。
            // 每次 Move 从 mHead 重放，故基于原始状态先算定再施加（遍历中摘除链表会踩空）。
            List<(INote note, double dur)>? shrink = null;
            List<INote>? remove = null;
            if (mCoupledPrev == null && PianoScrollView.Part.SoundSource.Kind == SourceKind.Voice)
            {
                var note = mNote;
                while (note.Last != null)
                {
                    note = note.Last;
                    if (note.StartPos() >= startTick)
                        (remove ??= new()).Add(note);
                    else if (note.EndPos() > startTick)
                        (shrink ??= new()).Add((note, startTick - note.StartPos()));
                }
            }
            PianoScrollView.Part.MoveNote(mNote, () =>
            {
                mNote.Pos.Set(mNote.Pos.Value + offsetTick);
                mNote.Dur.Set(mNote.Dur.Value - offsetTick);
            });
            if (shrink != null)
            {
                foreach (var (note, dur) in shrink)
                    PianoScrollView.Part.MoveNote(note, () => note.Dur.Set(dur));
            }
            if (remove != null)
            {
                foreach (var note in remove)
                    PianoScrollView.Part.RemoveNote(note);
            }
            if (mCoupledPrev != null)
            {
                // 上个 note 尾跟到本 note 新起点（分界线联动）。
                double prevDur = startTick - mCoupledPrev.StartPos();
                PianoScrollView.Part.MoveNote(mCoupledPrev, () => mCoupledPrev.Dur.Set(prevDur));
            }
            PianoScrollView.Part.EndMergeDirty();
        }

        public void Up()
        {
            State = State.None;

            if (mNote == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            var head = PianoScrollView.Part.Head;
            PianoScrollView.Part.EndMergeDirty();
            if (head == mHead)
            {
                PianoScrollView.Part.Discard();
            }
            else
            {
                PianoScrollView.Part.Commit();
            }
            mNote = null;
            mCoupledPrev = null;
        }

        double mOffset;
        INote? mNote;
        INote? mCoupledPrev;
        Head mHead;
    }

    readonly NoteStartResizeOperation mNoteStartResizeOperation;

    class NoteEndResizeOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public void Down(double x, INote note)
        {
            if (PianoScrollView.Part == null)
                return;

            State = State.NoteEndResizing;
            PianoScrollView.Part.BeginMergeDirty();
            mHead = PianoScrollView.Part.Head;
            mNote = note;
            double end = PianoScrollView.TickAxis.Tick2X(mNote.GlobalEndPos());
            mOffset = x - end;
        }

        public void Move(double x, bool alt)
        {
            if (mNote == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.Part.DiscardTo(mHead);
            double end = x - mOffset;
            double endTick = PianoScrollView.TickAxis.X2Tick(end);
            if (!alt) endTick = Math.Max(PianoScrollView.GetQuantizedTick(endTick), PianoScrollView.GetQuantizedTick(mNote.GlobalStartPos()) + PianoScrollView.QuantizedCellTicks());
            endTick -= PianoScrollView.Part.Pos.Value;
            if (endTick <= mNote.StartPos())
            {
                PianoScrollView.Part.RemoveNote(mNote);
                return;
            }

            PianoScrollView.Part.BeginMergeDirty();
            // voice（去重叠单声部）：尾部右拉推挤后续的邻居——终点被越过者删除、头部被盖到者起点推到新终点。
            // instrument（可重叠）：不动邻居，自由拉出重叠。
            // 每次 Move 从 mHead 重放，故基于原始状态先算定再施加（遍历中摘除链表会踩空）。
            List<(INote note, double noteEnd)>? push = null;
            List<INote>? remove = null;
            if (PianoScrollView.Part.SoundSource.Kind == SourceKind.Voice)
            {
                var note = mNote;
                while (note.Next != null)
                {
                    note = note.Next;
                    if (note.EndPos() <= endTick)
                        (remove ??= new()).Add(note);
                    else if (note.StartPos() < endTick)
                        (push ??= new()).Add((note, note.EndPos()));
                }
            }
            PianoScrollView.Part.MoveNote(mNote, () => mNote.Dur.Set(endTick - mNote.Pos.Value));
            if (push != null)
            {
                foreach (var (note, noteEnd) in push)
                    PianoScrollView.Part.MoveNote(note, () => { note.Pos.Set(endTick); note.Dur.Set(noteEnd - endTick); });
            }
            if (remove != null)
            {
                foreach (var note in remove)
                    PianoScrollView.Part.RemoveNote(note);
            }
            PianoScrollView.Part.EndMergeDirty();
        }

        public void Up()
        {
            State = State.None;

            if (mNote == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            var head = PianoScrollView.Part.Head;
            PianoScrollView.Part.EndMergeDirty();
            if (head == mHead)
            {
                PianoScrollView.Part.Discard();
            }
            else
            {
                PianoScrollView.Part.Commit();
            }
            mNote = null;
        }

        double mOffset;
        INote? mNote;
        Head mHead;
    }

    readonly NoteEndResizeOperation mNoteEndResizeOperation;

    class VibratoSelectOperation(PianoScrollView pianoScrollView) : SelectOperation<Vibrato>(pianoScrollView)
    {
        protected override State SelectState => State.VibratoSelecting;

        protected override IEnumerable<Vibrato>? Collection => PianoScrollView.Part?.Vibratos;

        protected override void Select(IEnumerable<Vibrato> vibratos, double minTick, double maxTick, double minPitch, double maxPitch)
        {
            foreach (var vibrato in vibratos)
            {
                if (vibrato.EndPos() < minTick)
                    continue;

                if (vibrato.StartPos() > maxTick)
                    break;

                vibrato.Select();
            }
        }
    }

    readonly VibratoSelectOperation mVibratoSelectOperation;

    class VibratoStartResizeOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public void Down(double x, Vibrato vibrato)
        {
            if (PianoScrollView.Part == null)
                return;

            State = State.VibratoStartResizing;
            PianoScrollView.Part.BeginMergeDirty();
            mHead = PianoScrollView.Part.Head;
            mVibrato = vibrato;
            double start = PianoScrollView.TickAxis.Tick2X(mVibrato.GlobalStartPos());
            mOffset = x - start;
        }

        public void Move(double x, bool alt)
        {
            if (mVibrato == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.Part.DiscardTo(mHead);
            double start = x - mOffset;
            double startTick = PianoScrollView.TickAxis.X2Tick(start);
            if (!alt) startTick = Math.Min(PianoScrollView.GetQuantizedTick(startTick), PianoScrollView.GetQuantizedTick(mVibrato.GlobalEndPos()) - PianoScrollView.QuantizedCellTicks());
            startTick -= PianoScrollView.Part.Pos.Value;
            if (startTick >= mVibrato.EndPos())
            {
                PianoScrollView.Part.RemoveVibrato(mVibrato);
                return;
            }

            double offsetTick = startTick - mVibrato.StartPos();
            PianoScrollView.Part.BeginMergeDirty();
            PianoScrollView.Part.MoveVibrato(mVibrato, () =>
            {
                mVibrato.Pos.Set(mVibrato.Pos + offsetTick);
                mVibrato.Dur.Set(mVibrato.Dur - offsetTick);
            });
            PianoScrollView.Part.EndMergeDirty();
        }

        public void Up()
        {
            State = State.None;

            if (mVibrato == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            var head = PianoScrollView.Part.Head;
            PianoScrollView.Part.EndMergeDirty();
            if (head == mHead)
            {
                PianoScrollView.Part.Discard();
            }
            else
            {
                PianoScrollView.Part.Commit();
            }
            mVibrato = null;
        }

        double mOffset;
        Vibrato? mVibrato;
        Head mHead;
    }

    readonly VibratoStartResizeOperation mVibratoStartResizeOperation;

    class VibratoEndResizeOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public void Down(double x, Vibrato vibrato)
        {
            if (PianoScrollView.Part == null)
                return;

            State = State.VibratoEndResizing;
            PianoScrollView.Part.BeginMergeDirty();
            mHead = PianoScrollView.Part.Head;
            mVibrato = vibrato;
            double end = PianoScrollView.TickAxis.Tick2X(mVibrato.GlobalEndPos());
            mOffset = x - end;
        }

        public void Move(double x, bool alt)
        {
            if (mVibrato == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.Part.DiscardTo(mHead);
            double end = x - mOffset;
            double endTick = PianoScrollView.TickAxis.X2Tick(end);
            if (!alt) endTick = Math.Max(PianoScrollView.GetQuantizedTick(endTick), PianoScrollView.GetQuantizedTick(mVibrato.GlobalStartPos()) + PianoScrollView.QuantizedCellTicks());
            endTick -= PianoScrollView.Part.Pos.Value;
            if (endTick <= mVibrato.StartPos())
            {
                PianoScrollView.Part.RemoveVibrato(mVibrato);
                return;
            }

            PianoScrollView.Part.BeginMergeDirty();
            PianoScrollView.Part.MoveVibrato(mVibrato, () => mVibrato.Dur.Set(endTick - mVibrato.Pos));
            PianoScrollView.Part.EndMergeDirty();
        }

        public void Up()
        {
            State = State.None;

            if (mVibrato == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            var head = PianoScrollView.Part.Head;
            PianoScrollView.Part.EndMergeDirty();
            if (head == mHead)
            {
                PianoScrollView.Part.Discard();
            }
            else
            {
                PianoScrollView.Part.Commit();
            }
            mVibrato = null;
        }

        double mOffset;
        Vibrato? mVibrato;
        Head mHead;
    }

    readonly VibratoEndResizeOperation mVibratoEndResizeOperation;

    class VibratoAmplitudeOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public void Down(double y, IVibratoItem vibratoItem)
        {
            if (PianoScrollView.Part == null)
                return;

            var vibratos = PianoScrollView.Part.Vibratos.AllSelectedItems();
            if (vibratos.IsEmpty())
                return;

            State = State.VibratoAmplitudeAdjusting;
            PianoScrollView.Part.BeginMergeDirty();
            mHead = PianoScrollView.Part.Head;
            mVibratos = vibratos;
            PianoScrollView.mOperatingVibratoItem = vibratoItem;
            mPitch = PianoScrollView.PitchAxis.Y2Pitch(y);
        }

        public void Move(double y)
        {
            if (PianoScrollView.Part == null)
                return;

            if (mVibratos == null)
                return;

            PianoScrollView.Part.DiscardTo(mHead);
            double pitch = PianoScrollView.PitchAxis.Y2Pitch(y);
            double offset = pitch - mPitch;
            foreach (var vibrato in mVibratos)
            {
                vibrato.Amplitude.Set(Math.Max(0, vibrato.Amplitude + offset));
            }
        }

        public void Up()
        {
            if (PianoScrollView.Part == null)
                return;

            State = State.None;
            var head = PianoScrollView.Part.Head;
            PianoScrollView.Part.EndMergeDirty();
            if (head == mHead)
            {
                PianoScrollView.Part.Discard();
            }
            else
            {
                PianoScrollView.Part.Commit();
            }
            mVibratos = null;
            PianoScrollView.mOperatingVibratoItem = null;
        }

        IReadOnlyCollection<Vibrato>? mVibratos;
        double mPitch;
        Head mHead;
    }

    readonly VibratoAmplitudeOperation mVibratoAmplitudeOperation;

    class VibratoFrequencyOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public bool IsOperating => State == State.VibratoFrequencyAdjusting;

        public void Down(double x, IVibratoItem vibratoItem)
        {
            if (PianoScrollView.Part == null)
                return;

            var vibratos = PianoScrollView.Part.Vibratos.AllSelectedItems();
            if (vibratos.IsEmpty())
                return;

            State = State.VibratoFrequencyAdjusting;
            PianoScrollView.Part.BeginMergeDirty();
            mHead = PianoScrollView.Part.Head;
            mVibratos = vibratos;
            PianoScrollView.mOperatingVibratoItem = vibratoItem;
            mPos = PianoScrollView.TickAxis.X2Tick(x);
        }

        public void Move(double x)
        {
            if (PianoScrollView.Part == null)
                return;

            if (mVibratos == null)
                return;

            PianoScrollView.Part.DiscardTo(mHead);
            double mX = PianoScrollView.TickAxis.Tick2X(mPos);
            double offset = x - mX;
            foreach (var vibrato in mVibratos)
            {
                vibrato.Frequency.Set((vibrato.Frequency + offset / -30).Limit(3, 9));
            }
        }

        public void Up()
        {
            if (PianoScrollView.Part == null)
                return;

            State = State.None;
            var head = PianoScrollView.Part.Head;
            PianoScrollView.Part.EndMergeDirty();
            if (head == mHead)
            {
                PianoScrollView.Part.Discard();
            }
            else
            {
                PianoScrollView.Part.Commit();
            }
            mVibratos = null;
            PianoScrollView.mOperatingVibratoItem = null;
        }

        IReadOnlyCollection<Vibrato>? mVibratos;
        double mPos;
        Head mHead;
    }

    readonly VibratoFrequencyOperation mVibratoFrequencyOperation;

    class VibratoPhaseOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public bool IsOperating => State == State.VibratoPhaseAdjusting;

        public void Down(double x, IVibratoItem vibratoItem)
        {
            if (PianoScrollView.Part == null)
                return;

            var vibratos = PianoScrollView.Part.Vibratos.AllSelectedItems();
            if (vibratos.IsEmpty())
                return;

            State = State.VibratoPhaseAdjusting;
            PianoScrollView.Part.BeginMergeDirty();
            mHead = PianoScrollView.Part.Head;
            PianoScrollView.mOperatingVibratoItem = vibratoItem;
            mVibratos = vibratos;
            mPos = PianoScrollView.TickAxis.X2Tick(x);
        }

        public void Move(double x)
        {
            if (PianoScrollView.Part == null)
                return;

            if (mVibratos == null)
                return;

            PianoScrollView.Part.DiscardTo(mHead);
            double mX = PianoScrollView.TickAxis.Tick2X(mPos);
            double offset = x - mX;
            foreach (var vibrato in mVibratos)
            {
                vibrato.Phase.Set((vibrato.Phase + offset / 30).Limit(-1, 1));
            }
        }

        public void Up()
        {
            if (PianoScrollView.Part == null)
                return;

            State = State.None;
            var head = PianoScrollView.Part.Head;
            PianoScrollView.Part.EndMergeDirty();
            if (head == mHead)
            {
                PianoScrollView.Part.Discard();
            }
            else
            {
                PianoScrollView.Part.Commit();
            }
            mVibratos = null;
            PianoScrollView.mOperatingVibratoItem = null;
        }

        IReadOnlyCollection<Vibrato>? mVibratos;
        double mPos;
        Head mHead;
    }

    readonly VibratoPhaseOperation mVibratoPhaseOperation;

    class VibratoAttackOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public void Down(double x, IVibratoItem vibratoItem)
        {
            mPart = vibratoItem.Vibrato.Part;
            var vibratos = mPart.Vibratos.AllSelectedItems();
            if (vibratos.IsEmpty())
                return;

            State = State.VibratoAttackAdjusting;
            mPart.BeginMergeDirty();
            mHead = mPart.Head;
            PianoScrollView.mOperatingVibratoItem = vibratoItem;
            mVibratos = vibratos;
            mTime = vibratoItem.Vibrato.GlobalAttackTime();
        }

        public void Move(double x)
        {
            if (mPart == null)
                return;

            if (mVibratos == null)
                return;

            mPart.DiscardTo(mHead);
            double time = mPart.TempoManager.GetTime(PianoScrollView.TickAxis.X2Tick(x));
            double offset = time - mTime;
            foreach (var vibrato in mVibratos)
            {
                vibrato.Attack.Set(Math.Max(0, vibrato.Attack + offset));
            }
        }

        public void Up()
        {
            if (mPart == null)
                return;

            State = State.None;
            var head = mPart.Head;
            mPart.EndMergeDirty();
            if (head == mHead)
            {
                mPart.Discard();
            }
            else
            {
                mPart.Commit();
            }
            mVibratos = null;
            mPart = null;
            PianoScrollView.mOperatingVibratoItem = null;
        }

        IMidiPart? mPart;
        IReadOnlyCollection<Vibrato>? mVibratos;
        double mTime;
        Head mHead;
    }

    readonly VibratoAttackOperation mVibratoAttackOperation;

    class VibratoReleaseOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public void Down(double x, IVibratoItem vibratoItem)
        {
            mPart = vibratoItem.Vibrato.Part;
            var vibratos = mPart.Vibratos.AllSelectedItems();
            if (vibratos.IsEmpty())
                return;

            State = State.VibratoReleaseAdjusting;
            mPart.BeginMergeDirty();
            mHead = mPart.Head;
            PianoScrollView.mOperatingVibratoItem = vibratoItem;
            mVibratos = vibratos;
            mTime = vibratoItem.Vibrato.GlobalReleaseTime();
        }

        public void Move(double x)
        {
            if (mPart == null)
                return;

            if (mVibratos == null)
                return;

            mPart.DiscardTo(mHead);
            double time = mPart.TempoManager.GetTime(PianoScrollView.TickAxis.X2Tick(x));
            double offset = time - mTime;
            foreach (var vibrato in mVibratos)
            {
                vibrato.Release.Set(Math.Max(0, vibrato.Release - offset));
            }
        }

        public void Up()
        {
            if (mPart == null)
                return;

            State = State.None;
            var head = mPart.Head;
            mPart.EndMergeDirty();
            if (head == mHead)
            {
                mPart.Discard();
            }
            else
            {
                mPart.Commit();
            }
            mVibratos = null;
            mPart = null;
            PianoScrollView.mOperatingVibratoItem = null;
        }

        IMidiPart? mPart;
        IReadOnlyCollection<Vibrato>? mVibratos;
        double mTime;
        Head mHead;
    }

    readonly VibratoReleaseOperation mVibratoReleaseOperation;

    class VibratoMoveOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public void Down(Avalonia.Point point, bool ctrl, Vibrato vibrato)
        {
            if (PianoScrollView.Part == null)
                return;

            mCtrl = ctrl;
            mIsSelected = vibrato.IsSelected;
            if (!mCtrl && !mIsSelected)
            {
                PianoScrollView.Part.Vibratos.DeselectAllItems();
            }
            vibrato.Select();

            mMoveVibratos = PianoScrollView.Part.Vibratos.AllSelectedItems();
            if (mMoveVibratos.IsEmpty())
                return;

            State = State.VibratoMoving;
            PianoScrollView.Part.BeginMergeDirty();
            mHead = PianoScrollView.Part.Head;
            mVibrato = vibrato;
            mDownPos = mVibrato.GlobalStartPos();
            mTickOffset = PianoScrollView.TickAxis.X2Tick(point.X) - mDownPos;
        }

        public void Move(Avalonia.Point point, bool alt)
        {
            var part = PianoScrollView.Part;
            if (part == null)
                return;

            if (mVibrato == null)
                return;

            if (mMoveVibratos == null)
                return;

            double pos = PianoScrollView.TickAxis.X2Tick(point.X) - mTickOffset;
            if (!alt) pos = PianoScrollView.GetQuantizedTick(pos);
            double posOffset = pos - mDownPos;
            if (posOffset == mLastPosOffset)
                return;

            mLastPosOffset = posOffset;
            mMoved = true;
            part.DiscardTo(mHead);
            part.MoveVibratos(mMoveVibratos, () =>
            {
                foreach (var vibrato in mMoveVibratos)
                    vibrato.Pos.Set(vibrato.Pos.Value + posOffset);
            });
        }

        public void Up()
        {
            State = State.None;

            if (mVibrato == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.Part.EndMergeDirty();
            if (mMoved)
            {
                PianoScrollView.Part.Commit();
            }
            else
            {
                PianoScrollView.Part.Discard();
                if (mCtrl)
                {
                    if (mIsSelected)
                    {
                        mVibrato.Inselect();
                    }
                }
                else
                {
                    PianoScrollView.Part.Vibratos.DeselectAllItems();
                    mVibrato.Select();
                }
            }
            mMoved = false;
            mVibrato = null;
            mMoveVibratos = null;
            mLastPosOffset = 0;
        }

        Vibrato? mVibrato;
        IReadOnlyCollection<Vibrato>? mMoveVibratos = null;

        bool mCtrl;
        bool mIsSelected;
        bool mMoved = false;
        double mDownPos;
        double mTickOffset;

        double mLastPosOffset = 0;
        Head mHead;
    }

    readonly VibratoMoveOperation mVibratoMoveOperation;

    IVibratoItem? mOperatingVibratoItem = null;

    class AnchorDeleteOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public bool IsOperating => State == State.AnchorDeleting;

        public void Down(double x)
        {
            if (IsOperating)
                return;

            if (PianoScrollView.Part == null)
                return;

            State = State.AnchorDeleting;
            PianoScrollView.Part.BeginMergeDirty();
            mHead = PianoScrollView.Part.Head;
            double tick = PianoScrollView.TickAxis.X2Tick(x) - PianoScrollView.Part.Pos.Value;
            mStart = tick;
            mEnd = tick;
            PianoScrollView.Part.Pitch.DeletePoints(mStart, mEnd);
        }

        public void Move(double x)
        {
            if (!IsOperating)
                return;

            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.Part.Pitch.DiscardTo(mHead);
            double tick = PianoScrollView.TickAxis.X2Tick(x) - PianoScrollView.Part.Pos.Value;
            mStart = Math.Min(mStart, tick);
            mEnd = Math.Max(mEnd, tick);
            PianoScrollView.Part.Pitch.DeletePoints(mStart, mEnd);
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            State = State.None;

            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.Part.Pitch.DiscardTo(mHead);
            PianoScrollView.Part.Pitch.DeletePoints(mStart, mEnd);
            PianoScrollView.Part.EndMergeDirty();
            PianoScrollView.Part.Pitch.Commit();
        }

        double mStart;
        double mEnd;
        Head mHead;
    }

    readonly AnchorDeleteOperation mAnchorDeleteOperation;

    class AnchorMoveOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public void Down(Avalonia.Point point, bool ctrl, AnchorPoint anchor)
        {
            if (PianoScrollView.Part == null)
                return;

            mCtrl = ctrl;
            mIsSelected = anchor.IsSelected;
            if (!mCtrl && !mIsSelected)
            {
                PianoScrollView.Part.Pitch.DeselectAllAnchors();
            }
            anchor.Select();

            State = State.AnchorMoving;
            PianoScrollView.Part.BeginMergeDirty();
            mHead = PianoScrollView.Part.Head;
            mAnchor = anchor;
            mXOffset = point.X - PianoScrollView.TickAxis.Tick2X(PianoScrollView.Part.Pos.Value + anchor.Pos);
            mYOffset = point.Y - PianoScrollView.PitchAxis.Pitch2Y(anchor.Value + 0.5);
        }

        public void Move(Avalonia.Point point)
        {
            var part = PianoScrollView.Part;
            if (part == null)
                return;

            if (mAnchor == null)
                return;

            double pos = PianoScrollView.TickAxis.X2Tick(point.X - mXOffset) - part.Pos.Value;
            double posOffset = pos - mAnchor.Pos;
            double pitch = PianoScrollView.PitchAxis.Y2Pitch(point.Y - mYOffset) - 0.5;
            double pitchOffset = pitch - mAnchor.Value;

            mMoved = true;
            part.DiscardTo(mHead);
            part.Pitch.MoveSelectedPoints(posOffset, pitchOffset);
        }

        public void Up()
        {
            State = State.None;

            if (mAnchor == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.Part.EndMergeDirty();
            if (mMoved)
            {
                PianoScrollView.Part.Commit();
            }
            else
            {
                PianoScrollView.Part.Discard();
                if (mCtrl)
                {
                    if (mIsSelected)
                    {
                        mAnchor.Inselect();
                    }
                }
                else
                {
                    PianoScrollView.Part.Pitch.DeselectAllAnchors();
                    mAnchor.Select();
                }
            }
            mMoved = false;
            mAnchor = null;
        }

        AnchorPoint? mAnchor;

        bool mCtrl;
        bool mIsSelected;
        bool mMoved = false;
        double mXOffset;
        double mYOffset;

        Head mHead;
    }

    readonly AnchorMoveOperation mAnchorMoveOperation;

    class AnchorSelectOperation(PianoScrollView pianoScrollView) : SelectOperation<AnchorPoint>(pianoScrollView)
    {
        protected override State SelectState => State.AnchorSelecting;

        protected override IEnumerable<AnchorPoint>? Collection => PianoScrollView.Part?.Pitch.AnchorGroups.SelectMany(anchorGroup => anchorGroup);

        protected override void Select(IEnumerable<AnchorPoint> items, double minTick, double maxTick, double minPitch, double maxPitch)
        {
            foreach (var item in items)
            {
                if (item.Pos < minTick)
                    continue;

                if (item.Pos > maxTick)
                    break;

                var pitch = item.Value + 0.5;
                if (pitch >= minPitch && pitch <= maxPitch)
                    item.Select();
            }
        }
    }

    readonly AnchorSelectOperation mAnchorSelectOperation;


    class WaveformPhonemeResizeOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public void Down(double x, INote note, int index)
        {
            var head = note.Part.Head;
            // 只锁定被拖的 note（不锁邻居）：反解只需 3-note 窗口，且布局已支持合成邻居参与推挤（无需邻居钉死）。
            // 代价是提交触发重合成时，与本 note 同块的相邻合成音素会短暂留白再回显——可接受，换取不冻结全曲预测。
            note.LockPhonemes();
            // 被显示门控留白的 note 本就没有手柄、拖不动（DisplayPhonemes 为空）；此处仍兜底，避免极端时序下取空索引崩溃。
            var phonemes = note.DisplayPhonemes;
            if (note.Phonemes.IsEmpty() || phonemes.IsEmpty() || index > phonemes.Count)
            {
                note.DiscardTo(head);
                return;
            }

            State = State.WaveformPhonemeResizing;
            note.Part.BeginMergeDirty();
            mHead = note.Part.Head;
            mNote = note;
            mIndex = index;
            mOffset = x - PianoScrollView.TickAxis.Tick2X(note.Part.TempoManager.GetTick(index == phonemes.Count ? phonemes.ConstLast().EndTime : phonemes[index].StartTime));
        }

        public void Move(double x, bool alt)
        {
            if (mNote == null)
                return;

            mNote.Part.DiscardTo(mHead);
            double posX = x - mOffset;
            double pos = PianoScrollView.TickAxis.X2Tick(posX);
            if (alt) pos = PianoScrollView.GetQuantizedTick(pos);
            double effRel = mNote.Part.TempoManager.GetTime(pos) - mNote.StartTime;
            // effective（显示秒）→ 改写该边界 offset（保留 anchor 的伸缩跟随），
            // 共享边界两侧同步、钳在相邻边界之间。
            mNote.DragPinnedBoundary(mIndex, effRel);
        }

        public void Up()
        {
            State = State.None;

            if (mNote == null)
                return;

            var head = mNote.Part.Head;
            mNote.Part.EndMergeDirty();
            if (head == mHead)
            {
                mNote.Part.Discard();
            }
            else
            {
                mNote.Part.Commit();
            }

            mNote = null;
        }

        double mOffset;
        INote? mNote = null;
        int mIndex;
        Head mHead;
    }

    readonly WaveformPhonemeResizeOperation mWaveformPhonemeResizeOperation;

    class SelectionOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public void Down(double x, bool alt)
        {
            State = State.SelectionCreating;

            double pos = PianoScrollView.TickAxis.X2Tick(x);
            if (!alt) pos = PianoScrollView.GetQuantizedTick(pos);
            mDownPos = pos;
            PianoScrollView.SetRegionSelection(pos, pos);
        }

        public void Move(double x, bool alt)
        {
            double pos = PianoScrollView.TickAxis.X2Tick(x);
            if (!alt) pos = PianoScrollView.GetQuantizedTick(pos);
            PianoScrollView.SetRegionSelection(mDownPos, pos);
        }

        public void Up()
        {
            State = State.None;

            if (PianoScrollView.CurrentRegionSelection is { } sel && sel.EndTick <= sel.StartTick)
                PianoScrollView.ClearRegionSelection();
        }

        double mDownPos;
    }

    readonly SelectionOperation mSelectionOperation;

    // 主键按下点 + 点击判定阈值：抬起时位移 ≤ 阈值即视为"点击(未拖)"，用于清空范围选区。镜像编排区 TrackScrollViewOperation。
    Avalonia.Point mPrimaryDownPos;
    const double ClickThreshold = 4;

    PreviewAnchorGroupItem? mPreviewPitchItem = null;

    public enum State
    {
        None,
        NoteSelecting,
        PitchDrawing,
        PitchClearing,
        PitchLocking,
        NoteMoving,
        NoteStartResizing,
        NoteEndResizing,
        VibratoSelecting,
        VibratoStartResizing,
        VibratoEndResizing,
        VibratoAmplitudeAdjusting,
        VibratoFrequencyAdjusting,
        VibratoPhaseAdjusting,
        VibratoAttackAdjusting,
        VibratoReleaseAdjusting,
        VibratoMoving,
        WaveformPhonemeResizing,
        SelectionCreating,
        AnchorSelecting,
        AnchorDeleting,
        AnchorMoving,
    }

    State mState = State.None;
}
