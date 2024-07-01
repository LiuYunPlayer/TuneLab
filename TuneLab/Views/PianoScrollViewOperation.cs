using DynamicData;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Base.Data;
using TuneLab.Base.Structures;
using TuneLab.GUI.Input;
using TuneLab.Data;
using TuneLab.Extensions.Formats.DataInfo;
using Rect = Avalonia.Rect;
using ContextMenu = Avalonia.Controls.ContextMenu;
using MenuItem = Avalonia.Controls.MenuItem;
using Avalonia.Input;
using TuneLab.Extensions.Voices;
using TuneLab.Utils;
using TuneLab.Base.Science;
using TuneLab.Base.Utils;
using TuneLab.I18N;
using ZstdSharp.Unsafe;

namespace TuneLab.Views;

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

    protected override void OnMouseDown(MouseDownEventArgs e)
    {
        switch (mState)
        {
            case State.None:
                bool alt = (e.KeyModifiers & ModifierKeys.Alt) != 0;
                bool ctrl = (e.KeyModifiers & ModifierKeys.Ctrl) != 0;
                var item = ItemAt(e.Position);
                bool DetectWaveformPrimaryButton()
                {
                    if (e.IsDoubleClick && item is WaveformBackItem)
                    {
                        if (Part == null)
                            return false;

                        if (e.IsDoubleClick)
                        {
                            var pos = TickAxis.X2Tick(e.Position.X);
                            if (alt) pos = GetQuantizedTick(pos);
                            pos -= Part.Pos;
                            foreach (var note in Part.Notes)
                            {
                                if (note.StartPos() < pos && note.EndPos() > pos)
                                {
                                    Cursor = new Cursor(StandardCursorType.SizeWestEast);
                                    mWaveformNoteResizeOperation.Down(e.Position.X, note, note.SplitAt(pos));
                                    break;
                                }
                            }
                        }
                    }
                    else if (item is WaveformNoteResizeItem waveformNoteResizeItem)
                    {
                        mWaveformNoteResizeOperation.Down(e.Position.X, waveformNoteResizeItem.Left, waveformNoteResizeItem.Right);
                    }
                    else if (item is WaveformPhonemeResizeItem waveformPhonemeResizeItem)
                    {
                        mWaveformPhonemeResizeOperation.Down(e.Position.X, waveformPhonemeResizeItem.Note, waveformPhonemeResizeItem.PhonemeIndex);
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
                switch (mDependency.PianoTool)
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
                                            menu.Open(this);
                                        }
                                    }
                                    else
                                    {
                                        if (e.IsDoubleClick)
                                        {
                                            var pitch = (int)PitchAxis.Y2Pitch(e.Position.Y);
                                            var pos = TickAxis.X2Tick(e.Position.X);
                                            if (!alt) pos = GetQuantizedTick(pos);
                                            var note = Part.CreateNote(new NoteInfo() { Pos = pos - Part.Pos, Dur = QuantizedCellTicks(), Pitch = pitch, Lyric = Part.Voice.DefaultLyric });
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
                                        {
                                            var menuItem = new MenuItem().SetName("Copy".Tr(TC.Menu)).SetAction(Copy).SetInputGesture(Key.C, ModifierKeys.Ctrl);
                                            menu.Items.Add(menuItem);
                                        }
                                        {
                                            var menuItem = new MenuItem().SetName("Cut".Tr(TC.Menu)).SetAction(Cut).SetInputGesture(Key.X, ModifierKeys.Ctrl);
                                            menu.Items.Add(menuItem);
                                        }

                                        menu.Items.Add(new Avalonia.Controls.Separator());
                                        var position = e.Position;
                                        var splitPos = TickAxis.X2Tick(position.X);
                                        if (!alt) splitPos = GetQuantizedTick(splitPos);
                                        splitPos -= Part.Pos;
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
                                                double pos = Part.Pos;
                                                List<NoteInfo> phonemeNoteInfos = new();
                                                List<INote> removeNotes = new();
                                                foreach (var note in selectedNotes)
                                                {
                                                    if (note.SynthesizedPhonemes == null)
                                                        continue;

                                                    foreach (var phoneme in note.SynthesizedPhonemes)
                                                    {
                                                        var info = note.GetInfo();
                                                        info.Pos = tempoManager.GetTick(phoneme.StartTime) - pos;
                                                        info.Dur = tempoManager.GetTick(phoneme.EndTime) - pos - info.Pos;
                                                        info.Lyric = phoneme.Symbol;
                                                        info.Pronunciation = string.Empty;
                                                        info.Phonemes = [new() { StartTime = 0, EndTime = phoneme.EndTime - phoneme.StartTime, Symbol = phoneme.Symbol }];
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
                                        if (note.Last != null && Part.Notes.End != null)
                                        {
                                            var menuItem = new MenuItem().SetName("Move Lyrics Backward".Tr(TC.Menu)).SetAction(() =>
                                            {
                                                Part.BeginMergeDirty();
                                                var it = Part.Notes.End;
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
                                            var menuItem = new MenuItem().SetName("Input Lyrics".Tr(TC.Menu)).SetAction(() => { LyricInput.EnterInput(Part.Notes.AllSelectedItems()); });
                                            menu.Items.Add(menuItem);
                                        }

                                        menu.Items.Add(new Avalonia.Controls.Separator());
                                        {
                                            var menuItem = new MenuItem().SetName("Delete".Tr(TC.Menu)).SetAction(Delete).SetInputGesture(Key.Delete);
                                            menu.Items.Add(menuItem);
                                        }
                                    }
                                    else
                                    {
                                        if (CanPaste)
                                        {
                                            {
                                                var position = e.Position;
                                                var pos = GetQuantizedTick(TickAxis.X2Tick(position.X)) - Part.Pos;
                                                var menuItem = new MenuItem().SetName("Paste".Tr(TC.Menu)).SetAction(() =>
                                                {
                                                    PasteAt(pos);
                                                }).SetInputGesture(Key.V, ModifierKeys.Ctrl);
                                                menu.Items.Add(menuItem);
                                            }
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
                    case PianoTool.Pitch:
                        switch (e.MouseButtonType)
                        {
                            case MouseButtonType.PrimaryButton:
                                if (!DetectWaveformPrimaryButton())
                                {
                                    if (ctrl)
                                        mSelectionOperation.Down(e.Position.X, alt);
                                    else
                                        mPitchDrawOperation.Down(e.Position);
                                }
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
                                    if (item is AnchorItem anchorItem)
                                    {

                                    }
                                    else
                                    {
                                        mAnchorSelectOperation.Down(e.Position, ctrl);
                                    }
                                }
                                break;
                            case MouseButtonType.SecondaryButton:
                                break;
                        }
                        break;
                    case PianoTool.Lock:
                        switch (e.MouseButtonType)
                        {
                            case MouseButtonType.PrimaryButton:
                                if (!DetectWaveformPrimaryButton())
                                {
                                    if (ctrl)
                                        mSelectionOperation.Down(e.Position.X, alt);
                                    else
                                        mPitchLockOperation.Down(e.Position.X);
                                }
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
                                    if (e.IsDoubleClick)
                                    {
                                        var pos = GetQuantizedTick(TickAxis.X2Tick(e.Position.X)) - Part.Pos;
                                        var vibrato = Part.CreateVibrato(new VibratoInfo() { Pos = pos, Dur = QuantizedCellTicks(), Amplitude = 0.5, Frequency = 6, Phase = 0, Attack = 0.2, Release = 0.2 });
                                        vibrato.Select();
                                        Part.InsertVibrato(vibrato);
                                        mVibratoEndResizeOperation.Down(TickAxis.Tick2X(vibrato.GlobalEndPos()), vibrato);
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
                                                var pos = GetQuantizedTick(TickAxis.X2Tick(position.X)) - Part.Pos;
                                                var menuItem = new MenuItem().SetName("Paste").SetAction(() =>
                                                {
                                                    PasteAt(pos);
                                                }).SetInputGesture(Key.V, ModifierKeys.Ctrl);
                                                menu.Items.Add(menuItem);
                                            }
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
                    case PianoTool.Select:
                        switch (e.MouseButtonType)
                        {
                            case MouseButtonType.PrimaryButton:
                                if (!DetectWaveformPrimaryButton())
                                    mSelectionOperation.Down(e.Position.X, alt);
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
                mPitchDrawOperation.Move(e.Position);
                break;
            case State.PitchClearing:
                mPitchClearOperation.Move(e.Position.X);
                break;
            case State.PitchLocking:
                mPitchLockOperation.Move(e.Position.X);
                break;
            case State.NoteMoving:
                mNoteMoveOperation.Move(e.Position, ctrl, alt, shift);
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
            case State.WaveformNoteResizing:
                mWaveformNoteResizeOperation.Move(e.Position.X, alt);
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
            default:
                var item = ItemAt(e.Position);
                if (item is WaveformNoteResizeItem || item is WaveformPhonemeResizeItem)
                {
                    Cursor = new Cursor(StandardCursorType.SizeWestEast);
                }
                else if (item is WaveformBackItem)
                {
                    Cursor = null;
                }
                else
                {
                    switch (mDependency.PianoTool)
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
                            if (ctrl)
                                Cursor = new Cursor(StandardCursorType.Ibeam);
                            else
                                Cursor = null;
                            break;
                        case PianoTool.Select:
                            Cursor = new Cursor(StandardCursorType.Ibeam);
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
            case State.WaveformNoteResizing:
                if (e.MouseButtonType == MouseButtonType.PrimaryButton)
                    mWaveformNoteResizeOperation.Up();
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
            default:
                break;
        }

        if (e.MouseButtonType == MouseButtonType.MiddleButton)
            mMiddleDragOperation.Up();
    }

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
                if (e.Key == Key.LeftAlt || e.Key == Key.LeftShift || e.Key == Key.LeftCtrl)
                {
                    mNoteMoveOperation.Move(MousePosition, (e.KeyModifiers & KeyModifiers.Control) != 0, (e.KeyModifiers & KeyModifiers.Alt) != 0, (e.KeyModifiers & KeyModifiers.Shift) != 0);
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
            case State.WaveformNoteResizing:
                if (e.Key == Key.LeftAlt)
                {
                    mWaveformNoteResizeOperation.Move(MousePosition.X, true);
                    e.Handled = true;
                }
                break;
            case State.None:
                if (e.Key == Key.LeftCtrl && (mDependency.PianoTool == PianoTool.Pitch || mDependency.PianoTool == PianoTool.Lock))
                {
                    Cursor = new Cursor(StandardCursorType.Ibeam);
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
                if (e.Key == Key.LeftAlt || e.Key == Key.LeftShift || e.Key == Key.LeftCtrl)
                {
                    mNoteMoveOperation.Move(MousePosition, (e.KeyModifiers & KeyModifiers.Control) != 0, (e.KeyModifiers & KeyModifiers.Alt) != 0, (e.KeyModifiers & KeyModifiers.Shift) != 0);
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
            case State.WaveformNoteResizing:
                if (e.Key == Key.LeftAlt)
                {
                    mWaveformNoteResizeOperation.Move(MousePosition.X, false);
                    e.Handled = true;
                }
                break;
            case State.None:
                if (e.Key == Key.LeftCtrl && (mDependency.PianoTool == PianoTool.Pitch || mDependency.PianoTool == PianoTool.Lock))
                {
                    Cursor = null;
                    e.Handled = true;
                }
                break;
        }
    }

    protected override void UpdateItems(IItemCollection items)
    {
        if (Part == null)
            return;

        double startPos = TickAxis.MinVisibleTick;
        double endPos = TickAxis.MaxVisibleTick;
        var tempoManager = Part.TempoManager;
        var viewStartTime = tempoManager.GetTime(startPos);
        var viewEndTime = tempoManager.GetTime(endPos);
        double partPos = Part.Pos;

        switch (mDependency.PianoTool)
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
                break;
            default:
                break;
        }

        items.Add(new WaveformBackItem(this));

        WaveformNoteResizeItem? lastItem = null;
        foreach (var note in Part.Notes)
        {
            IReadOnlyList<SynthesizedPhoneme>? phonemes = ((ISynthesisNote)note).Phonemes;
            if (phonemes.IsEmpty())
                phonemes = note.SynthesizedPhonemes;

            if (phonemes == null || phonemes.IsEmpty())
                continue;

            var startTime = phonemes.ConstFirst().StartTime;
            var endTime = phonemes.ConstLast().EndTime;
            if (endTime < viewStartTime)
                continue;

            if (startTime > viewEndTime)
                break;

            for (var i = 0; i <= phonemes.Count; i++)
            {
                items.Add(new WaveformPhonemeResizeItem(this) { Note = note, PhonemeIndex = i });
            }
        }

        foreach (var note in Part.Notes)
        {
            if (note.GlobalEndPos() < startPos)
                continue;

            if (note.GlobalStartPos() > endPos)
                break;

            if (lastItem != null && lastItem.Left!.EndPos() == note.StartPos())
            {
                lastItem.Right = note;
            }
            else
            {
                items.Add(new WaveformNoteResizeItem(this) { Left = null, Right = note });
            }
            var item = new WaveformNoteResizeItem(this) { Left = note, Right = null };
            items.Add(item);
            lastItem = item;
        }
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
            PianoScrollView.mSelection.IsAcitve = false;
            mDownTick = PianoScrollView.TickAxis.X2Tick(point.X) - PianoScrollView.Part.Pos;
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

            mTick = PianoScrollView.TickAxis.X2Tick(point.X) - PianoScrollView.Part.Pos;
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
            double pos = PianoScrollView.Part == null ? 0 : PianoScrollView.Part.Pos;
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

        public void Down(Avalonia.Point mousePosition)
        {
            if (IsOperating)
                return;

            if (PianoScrollView.Part == null)
                return;

            State = State.PitchDrawing;
            PianoScrollView.Part.BeginMergeDirty();
            mHead = PianoScrollView.Part.Head;

            mPointLines.Add([ToTickAndValue(mousePosition)]);
            PianoScrollView.Part.Pitch.AddLine(mPointLines[0], 5);
        }

        public void Move(Avalonia.Point mousePosition)
        {
            if (!IsOperating)
                return;

            if (PianoScrollView.Part == null)
                return;

            var point = ToTickAndValue(mousePosition);
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
                PianoScrollView.Part.Pitch.AddLine(line.Simplify(5, 2), 5);
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
                PianoScrollView.Part.Pitch.AddLine(line.Simplify(5, 2), 5);
            }
            PianoScrollView.Part.Pitch.Commit();
            mPointLines.Clear();
        }

        Point ToTickAndValue(Avalonia.Point mousePosition)
        {
            return new(PianoScrollView.TickAxis.X2Tick(mousePosition.X) - PianoScrollView.Part!.Pos, PianoScrollView.PitchAxis.Y2Pitch(mousePosition.Y) - 0.5);
        }

        bool mDirection;
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
            double tick = PianoScrollView.TickAxis.X2Tick(x) - PianoScrollView.Part.Pos;
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
            double tick = PianoScrollView.TickAxis.X2Tick(x) - PianoScrollView.Part.Pos;
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
            double tick = PianoScrollView.TickAxis.X2Tick(x) - PianoScrollView.Part.Pos;
            mStart = tick;
            mEnd = tick;
            PianoScrollView.Part.LockPitch(mStart, mEnd, 5);
        }

        public void Move(double x)
        {
            if (!IsOperating)
                return;

            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.Part.DiscardTo(mHead);
            double tick = PianoScrollView.TickAxis.X2Tick(x) - PianoScrollView.Part.Pos;
            mStart = Math.Min(mStart, tick);
            mEnd = Math.Max(mEnd, tick);
            PianoScrollView.Part.LockPitch(mStart, mEnd, 5);
        }

        public void Up()
        {
            if (!IsOperating)
                return;

            State = State.None;

            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.Part.DiscardTo(mHead);
            PianoScrollView.Part.LockPitch(mStart, mEnd, 5);
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
            PianoScrollView.Part.DisableAutoPrepare();
            mHead = PianoScrollView.Part.Head;
            mNote = note;
            mDownPartPos = mNote.GlobalStartPos();
            mTickOffset = PianoScrollView.TickAxis.X2Tick(point.X) - PianoScrollView.Part.Pos - note.Pos.Value;
            mPitch = note.Pitch.Value;
            mMinPitch = minPitch;
            mMaxPitch = maxPitch;
        }

        public void Move(Avalonia.Point point, bool ctrl, bool alt, bool shift)
        {
            var part = PianoScrollView.Part;
            if (part == null)
                return;

            if (mNote == null)
                return;

            if (mMoveNotes.IsEmpty())
                return;

            bool movePitchLine = (ctrl && shift);//定义同时按下Shift和Ctrl为带Pitch移动
            if (movePitchLine) { shift = false; }//当触发Pitch移动时无视Shift事件，避免冲突
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
            part.BeginMergeReSegment();
            part.Notes.ListModified.BeginMerge();
            List<Tuple<double, double>> ranges = new List<Tuple<double, double>>();
            foreach (var note in mMoveNotes)
            {
                ranges.Add(new Tuple<double, double>(note.StartPos(), note.EndPos()));
                note.Pos.Set(note.Pos.Value + posOffset);
                note.Pitch.Set(note.Pitch.Value + pitchOffset);
                part.RemoveNote(note);
            }

            foreach (var note in mMoveNotes)
            {
                part.InsertNote(note);
            }

            if (movePitchLine) part.Pitch.MoveLine(ranges.ToArray(), posOffset, pitchOffset);//跟随Note移动Pitch
            part.Notes.ListModified.EndMerge();
            part.EndMergeReSegment();
        }

        public void Up()
        {
            State = State.None;

            if (mNote == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.Part.EnableAutoPrepare();
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
        public void Down(double x, INote note)
        {
            if (PianoScrollView.Part == null)
                return;

            State = State.NoteStartResizing;
            PianoScrollView.Part.DisableAutoPrepare();
            mHead = PianoScrollView.Part.Head;
            mNote = note;
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
            startTick -= PianoScrollView.Part.Pos;
            if (startTick >= mNote.EndPos())
            {
                PianoScrollView.Part.RemoveNote(mNote);
                return;
            }

            List<INote> modifiedNotes = new();
            PianoScrollView.Part.BeginMergeReSegment();
            double offsetTick = startTick - mNote.StartPos();
            mNote.Pos.Set(mNote.Pos.Value + offsetTick);
            mNote.Dur.Set(mNote.Dur.Value - offsetTick);
            modifiedNotes.Add(mNote);
            {
                var note = mNote;
                while (note.Last != null)
                {
                    note = note.Last;
                    if (note.StartPos() >= startTick)
                    {
                        PianoScrollView.Part.RemoveNote(note);
                        continue;
                    }

                    if (note.EndPos() > startTick)
                    {
                        note.Dur.Set(startTick - note.Pos.Value);
                        modifiedNotes.Add(note);
                    }
                }
            }
            foreach (var note in modifiedNotes)
            {
                PianoScrollView.Part.RemoveNote(note);
            }
            foreach (var note in modifiedNotes)
            {
                PianoScrollView.Part.InsertNote(note);
            }
            PianoScrollView.Part.EndMergeReSegment();
        }

        public void Up()
        {
            State = State.None;

            if (mNote == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            var head = PianoScrollView.Part.Head;
            PianoScrollView.Part.EnableAutoPrepare();
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

    readonly NoteStartResizeOperation mNoteStartResizeOperation;

    class NoteEndResizeOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public void Down(double x, INote note)
        {
            if (PianoScrollView.Part == null)
                return;

            State = State.NoteEndResizing;
            PianoScrollView.Part.DisableAutoPrepare();
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
            endTick -= PianoScrollView.Part.Pos;
            if (endTick <= mNote.StartPos())
            {
                PianoScrollView.Part.RemoveNote(mNote);
                return;
            }

            List<INote> modifiedNotes = new();
            int index = PianoScrollView.Part.Notes.IndexOf(mNote);
            PianoScrollView.Part.BeginMergeReSegment();
            mNote.Dur.Set(endTick - mNote.Pos.Value);
            modifiedNotes.Add(mNote);
            {
                var note = mNote;
                while (note.Next != null)
                {
                    note = note.Next;
                    if (note.EndPos() <= endTick)
                    {
                        PianoScrollView.Part.RemoveNote(note);
                        index--;
                        continue;
                    }

                    if (note.StartPos() < endTick)
                    {
                        note.Dur.Set(note.EndPos() - endTick);
                        note.Pos.Set(endTick);
                        modifiedNotes.Add(note);
                    }
                }
            }
            foreach (var note in modifiedNotes)
            {
                PianoScrollView.Part.RemoveNote(note);
            }
            foreach (var note in modifiedNotes)
            {
                PianoScrollView.Part.InsertNote(note);
            }
            PianoScrollView.Part.EndMergeReSegment();
        }

        public void Up()
        {
            State = State.None;

            if (mNote == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            var head = PianoScrollView.Part.Head;
            PianoScrollView.Part.EnableAutoPrepare();
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
            PianoScrollView.Part.DisableAutoPrepare();
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
            startTick -= PianoScrollView.Part.Pos;
            if (startTick >= mVibrato.EndPos())
            {
                PianoScrollView.Part.RemoveVibrato(mVibrato);
                return;
            }

            double offsetTick = startTick - mVibrato.StartPos();
            PianoScrollView.Part.BeginMergeReSegment();
            mVibrato.Pos.Set(mVibrato.Pos + offsetTick);
            mVibrato.Dur.Set(mVibrato.Dur - offsetTick);
            PianoScrollView.Part.RemoveVibrato(mVibrato);
            PianoScrollView.Part.InsertVibrato(mVibrato);
            PianoScrollView.Part.EndMergeReSegment();
        }

        public void Up()
        {
            State = State.None;

            if (mVibrato == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            var head = PianoScrollView.Part.Head;
            PianoScrollView.Part.EnableAutoPrepare();
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
            PianoScrollView.Part.DisableAutoPrepare();
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
            endTick -= PianoScrollView.Part.Pos;
            if (endTick <= mVibrato.StartPos())
            {
                PianoScrollView.Part.RemoveVibrato(mVibrato);
                return;
            }

            PianoScrollView.Part.BeginMergeReSegment();
            mVibrato.Dur.Set(endTick - mVibrato.Pos);
            PianoScrollView.Part.RemoveVibrato(mVibrato);
            PianoScrollView.Part.InsertVibrato(mVibrato);
            PianoScrollView.Part.EndMergeReSegment();
        }

        public void Up()
        {
            State = State.None;

            if (mVibrato == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            var head = PianoScrollView.Part.Head;
            PianoScrollView.Part.EnableAutoPrepare();
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
            PianoScrollView.Part.DisableAutoPrepare();
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
            PianoScrollView.Part.EnableAutoPrepare();
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
            PianoScrollView.Part.DisableAutoPrepare();
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
            PianoScrollView.Part.EnableAutoPrepare();
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
            PianoScrollView.Part.DisableAutoPrepare();
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
            PianoScrollView.Part.EnableAutoPrepare();
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
            mPart.DisableAutoPrepare();
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
            mPart.EnableAutoPrepare();
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
            mPart.DisableAutoPrepare();
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
            mPart.EnableAutoPrepare();
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
            PianoScrollView.Part.DisableAutoPrepare();
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
            part.BeginMergeReSegment();
            foreach (var vibrato in mMoveVibratos)
            {
                vibrato.Pos.Set(vibrato.Pos.Value + posOffset);
                part.RemoveVibrato(vibrato);
            }

            foreach (var vibrato in mMoveVibratos)
            {
                part.InsertVibrato(vibrato);
            }
            part.EndMergeReSegment();
        }

        public void Up()
        {
            State = State.None;

            if (mVibrato == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.Part.EnableAutoPrepare();
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

    class WaveformNoteResizeOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public void Down(double x, INote? left, INote? right)
        {
            if (left == null && right == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            State = State.WaveformNoteResizing;
            PianoScrollView.Part.DisableAutoPrepare();
            mHead = PianoScrollView.Part.Head;
            mLeft = left;
            mRight = right;
            mOffset = x - PianoScrollView.TickAxis.Tick2X(mLeft == null ? mRight!.GlobalStartPos() : mLeft.GlobalEndPos()); ;
        }

        public void Move(double x, bool alt)
        {
            if (mLeft == null && mRight == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            PianoScrollView.Part.DiscardTo(mHead);
            double posX = x - mOffset;
            double pos = PianoScrollView.TickAxis.X2Tick(posX);
            if (alt) pos = PianoScrollView.GetQuantizedTick(pos);
            pos -= PianoScrollView.Part.Pos;
            var last = mLeft == null ? mRight?.Last : mLeft;
            if (last != null) pos = Math.Max(pos, last.StartPos());
            var next = mRight == null ? mLeft?.Next : mRight;
            if (next != null) pos = Math.Min(pos, next.EndPos());

            PianoScrollView.Part.BeginMergeReSegment();
            if (mLeft != null)
            {
                if (pos == mLeft.StartPos())
                {
                    PianoScrollView.Part.RemoveNote(mLeft);
                }
                else 
                { 
                    mLeft.Dur.Set(pos - mLeft.Pos.Value); 
                }

            }
            if (mRight != null)
            {
                if (pos == mRight.EndPos())
                {
                    PianoScrollView.Part.RemoveNote(mRight);
                }
                else
                {
                    mRight.Dur.Set(mRight.EndPos() - pos);
                    mRight.Pos.Set(pos);
                }
            }
            PianoScrollView.Part.EndMergeReSegment();
        }

        public void Up()
        {
            State = State.None;

            if (mLeft == null && mRight == null)
                return;

            if (PianoScrollView.Part == null)
                return;

            var head = PianoScrollView.Part.Head;
            PianoScrollView.Part.EnableAutoPrepare();
            if (head == mHead)
            {
                PianoScrollView.Part.Discard();
            }
            else
            {
                PianoScrollView.Part.Commit();
            }
            mLeft = null;
            mRight = null;
        }

        double mOffset;
        INote? mLeft;
        INote? mRight;
        Head mHead;
    }

    readonly WaveformNoteResizeOperation mWaveformNoteResizeOperation;

    class WaveformPhonemeResizeOperation(PianoScrollView pianoScrollView) : Operation(pianoScrollView)
    {
        public void Down(double x, INote note, int index)
        {
            var head = note.Part.Head;
            note.LockPhonemes();
            if (note.Phonemes.IsEmpty() || index > note.Phonemes.Count)
            {
                note.DiscardTo(head);
                return;
            }

            var last = note.LastInSegment;
            while (last != null)
            {
                last.LockPhonemes();
                last = last.LastInSegment;
            }
            var next = note.NextInSegment;
            while (next != null)
            {
                next.LockPhonemes();
                next = next.NextInSegment;
            }

            State = State.WaveformPhonemeResizing;
            note.Part.DisableAutoPrepare();
            mHead = note.Part.Head;
            mNote = note;
            mIndex = index;
            var phonemes = ((ISynthesisNote)note).Phonemes;
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
            double time = mNote.Part.TempoManager.GetTime(pos) - mNote.StartTime;
            double ratio = time <= 0 ? mNote.StartPhonemeRatio : mNote.EndPhonemeRatio;
            if (ratio == 0)
                return;

            time /= ratio;
            if (mIndex != mNote.Phonemes.Count)
                time = Math.Min(time, mNote.Phonemes[mIndex].EndTime.Value);
            if (mIndex != 0)
                time = Math.Max(time, mNote.Phonemes[mIndex - 1].StartTime.Value);

            if (mIndex != mNote.Phonemes.Count)
                mNote.Phonemes[mIndex].StartTime.Set(time);
            if (mIndex != 0)
                mNote.Phonemes[mIndex - 1].EndTime.Set(time);
        }

        public void Up()
        {
            State = State.None;

            if (mNote == null)
                return;

            var head = mNote.Part.Head;
            mNote.Part.EnableAutoPrepare();
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
            PianoScrollView.mSelection.IsAcitve = true;
            PianoScrollView.mSelection.Start = pos;
            PianoScrollView.mSelection.End = pos;
            PianoScrollView.InvalidateVisual();
        }

        public void Move(double x, bool alt)
        {
            double pos = PianoScrollView.TickAxis.X2Tick(x);
            if (!alt) pos = PianoScrollView.GetQuantizedTick(pos);
            var min = Math.Min(pos, mDownPos);
            var max = Math.Max(pos, mDownPos);
            PianoScrollView.mSelection.Start = min;
            PianoScrollView.mSelection.End = max;
            PianoScrollView.InvalidateVisual();
        }

        public void Up()
        {
            State = State.None;

            if (PianoScrollView.mSelection.Duration == 0)
                PianoScrollView.mSelection.IsAcitve = false;

            PianoScrollView.InvalidateVisual();
        }

        double mDownPos;
    }

    readonly SelectionOperation mSelectionOperation;

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
        WaveformNoteResizing,
        WaveformPhonemeResizing,
        SelectionCreating,
        AnchorSelecting,
    }

    State mState = State.None;
}
