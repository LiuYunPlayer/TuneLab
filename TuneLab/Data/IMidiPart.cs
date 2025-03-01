using DynamicData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TuneLab.Base.Properties;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Extensions.Voices;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.Utils;

namespace TuneLab.Data;

internal interface IMidiPart : IPart, IDataObject<MidiPartInfo>
{
    IActionEvent<ISynthesisPiece> SynthesisStatusChanged { get; }
    INoteList Notes { get; }
    IReadOnlyDataObjectList<Vibrato> Vibratos { get; }
    DataPropertyObject Properties { get; }
    IVoice Voice { get; }
    IReadOnlyDataObjectList<IEffect> Effects { get; }
    IEffect CreateEffect(EffectInfo info);
    void InsertEffect(int index, IEffect effect);
    void RemoveEffect(IEffect effect);
    void RemoveEffectAt(int index);
    IDataProperty<double> Gain { get; }
    IReadOnlyDataObjectMap<string, IAutomation> Automations { get; }
    IPiecewiseCurve Pitch { get; }
    IReadOnlyList<ISynthesisPiece> SynthesisPieces { get; }
    IAutomation? AddAutomation(string automationID);
    double[] GetFinalPitch(IReadOnlyList<double> ticks);
    void LockPitch(double start, double end, double extension);
    double[] GetAutomationValues(IReadOnlyList<double> ticks, string automationID);
    double[] GetFinalAutomationValues(IReadOnlyList<double> ticks, string automationID);
    double[] GetVibratoDeviation(IReadOnlyList<double> ticks, string automationID = "");
    INote CreateNote(NoteInfo info);
    void InsertNote(INote note);
    bool RemoveNote(INote note);
    Vibrato CreateVibrato(VibratoInfo info);
    void InsertVibrato(Vibrato note);
    bool RemoveVibrato(Vibrato note);
    void BeginMergeReSegment();
    void EndMergeReSegment();
    void DisableAutoPrepare();
    void EnableAutoPrepare();
}

internal static class IMidiPartExtension
{
    public static bool IsEffectiveAutomation(this IMidiPart part, string id)
    {
        return part.Voice.AutomationConfigs.ContainsKey(id);
    }

    public static AutomationConfig GetEffectiveAutomationConfig(this IMidiPart part, string id)
    {
        if (part.Voice.AutomationConfigs.ContainsKey(id))
            return part.Voice.AutomationConfigs[id];

        throw new ArgumentException(string.Format("Automation {0} is not effective!", id));
    }

    public static ISynthesisPiece? FindNextNotCompletePiece(this IMidiPart part, double time)
    {
        ISynthesisPiece? result = null;

        foreach (var piece in part.SynthesisPieces)
        {
            if (piece.SynthesisStatus == SynthesisStatus.SynthesisSucceeded || piece.SynthesisStatus == SynthesisStatus.SynthesisFailed)
                continue;

            if (result == null)
            {
                result = piece;
                continue;
            }

            if (result.EndTime() < time)
            {
                if (piece.EndTime() < time && piece.StartTime() > result.StartTime())
                {
                    continue;
                }
            }
            else
            {
                if (piece.EndTime() < time || piece.StartTime() > result.StartTime())
                {
                    continue;
                }
            }

            result = piece;
        }

        return result;
    }

    public static (int, int) PitchRange(this IMidiPart part)
    {
        var note = part.Notes.Begin;
        if (note == null)
            return (0, 0);

        int min = note.Pitch.Value;
        int max = note.Pitch.Value;
        while (note.Next != null)
        {
            note = note.Next;
            var pitch = note.Pitch.Value;

            if (pitch > max)
                max = pitch;
            else if (pitch < min)
                min = pitch;
        }

        return (min, max);
    }

    public static void BeginMergeDirty(this IMidiPart part)
    {
        part.BeginMergeReSegment();
        part.DisableAutoPrepare();
    }

    public static void EndMergeDirty(this IMidiPart part)
    {
        part.EnableAutoPrepare();
        part.EndMergeReSegment();
    }

    public static void SetAllPieceDirty(this IMidiPart part, string dirtyType)
    {
        foreach (var piece in part.SynthesisPieces)
            piece.SetDirty(dirtyType);
    }

    public static List<INote> AllNotesInSelection(this IMidiPart part, double start, double end)
    {
        var result = new List<INote>();
        foreach (var note in part.Notes)
        {
            if (note.StartPos() < start)
                continue;

            if (note.StartPos() >= end)
                break;

            result.Add(note);
        }
        return result;
    }

    public static List<Vibrato> AllVibratosInSelection(this IMidiPart part, double start, double end)
    {
        var result = new List<Vibrato>();
        foreach (var vibrato in part.Vibratos)
        {
            if (vibrato.StartPos() < start)
                continue;

            if (vibrato.StartPos() >= end)
                break;

            result.Add(vibrato);
        }
        return result;
    }

    public static NoteClipboard CopyNotes(this IMidiPart part)
    {
        var clipboard = new NoteClipboard();
        var selectedNotes = part.Notes.AllSelectedItems();
        if (selectedNotes.IsEmpty())
            return clipboard;

        var pos = selectedNotes.First().Pos.Value;
        foreach (var note in selectedNotes)
        {
            var info = note.GetInfo();
            info.Pos -= pos;
            clipboard.Add(info);
        }
        return clipboard;
    }

    public static NoteClipboard CopyNotes(this IMidiPart part, double start, double end)
    {
        var clipboard = new NoteClipboard();
        foreach (var note in part.AllNotesInSelection(start, end))
        {
            var info = note.GetInfo();
            info.Pos -= start;
            clipboard.Add(info);
        }
        return clipboard;
    }

    public static VibratoClipboard CopyVibratos(this IMidiPart part)
    {
        var clipboard = new VibratoClipboard();
        var selectedVibratos = part.Vibratos.AllSelectedItems();
        if (selectedVibratos.IsEmpty())
            return clipboard;

        var pos = selectedVibratos.First().Pos.Value;
        foreach (var vibrato in selectedVibratos)
        {
            var info = vibrato.GetInfo();
            info.Pos -= pos;
            clipboard.Add(info);
        }
        return clipboard;
    }

    public static VibratoClipboard CopyVibratos(this IMidiPart part, double start, double end)
    {
        var clipboard = new VibratoClipboard();
        foreach (var vibrato in part.AllVibratosInSelection(start, end))
        {
            var info = vibrato.GetInfo();
            info.Pos -= start;
            clipboard.Add(info);
        }
        return clipboard;
    }

    public static ParameterClipboard CopyParameters(this IMidiPart part, double start, double end)
    {
        var pitch = part.Pitch.RangeInfo(start, end);
        var automations = new Map<string, List<Point>>();
        foreach (var kvp in part.Automations)
        {
            automations.Add(kvp.Key, kvp.Value.RangeInfo(start, end));
        }
        return new ParameterClipboard() { Pitch = pitch, Automations = automations };
    }

    public static void PasteAt(this IMidiPart part, NoteClipboard clipboard, double pos)
    {
        if (clipboard.IsEmpty())
            return;

        part.Notes.DeselectAllItems();
        part.BeginMergeDirty();
        part.Notes.ListModified.BeginMerge();
        foreach (var noteInfo in clipboard)
        {
            var note = part.CreateNote(noteInfo);
            note.Select();
            note.Pos.Set(note.Pos.Value + pos);
            part.InsertNote(note);
        }
        part.Notes.ListModified.EndMerge();
        part.EndMergeDirty();
    }

    public static void PasteAt(this IMidiPart part, VibratoClipboard clipboard, double pos)
    {
        if (clipboard.IsEmpty())
            return;

        part.Vibratos.DeselectAllItems();
        part.BeginMergeDirty();
        foreach (var vibratoInfo in clipboard)
        {
            var vibrato = part.CreateVibrato(vibratoInfo);
            vibrato.Select();
            vibrato.Pos.Set(vibrato.Pos + pos);
            part.InsertVibrato(vibrato);
        }
        part.EndMergeDirty();
    }

    public static void PasteAt(this IMidiPart part, ParameterClipboard clipboard, double pos, double extend)
    {
        if (clipboard.IsEmpty)
            return;

        foreach (var kvp in clipboard.Automations)
        {
            if (!part.Automations.TryGetValue(kvp.Key, out var automation))
            {
                automation = part.AddAutomation(kvp.Key);
            }

            if (automation == null)
                continue;

            var defaultValue = automation.DefaultValue.Value;
            automation.AddLine(kvp.Value.Convert(point => new AnchorPoint(point.X + pos, point.Y + defaultValue)), extend);
        }

        foreach (var points in clipboard.Pitch)
        {
            part.Pitch.AddLine(points.Convert(point => new AnchorPoint(point.X + pos, point.Y)), extend);
        }
    }

    public static void DeleteAllSelectedNotes(this IMidiPart part)
    {
        part.BeginMergeDirty();
        part.Notes.ListModified.BeginMerge();
        foreach (var note in part.Notes.AllSelectedItems())
        {
            part.RemoveNote(note);
        }
        part.Notes.ListModified.EndMerge();
        part.EndMergeDirty();
    }

    public static void DeleteAllSelectedVibratos(this IMidiPart part)
    {
        part.BeginMergeDirty();
        foreach (var vibrato in part.Vibratos.AllSelectedItems())
        {
            part.RemoveVibrato(vibrato);
        }
        part.EndMergeDirty();
    }

    public static void DeleteAllNotesInSelection(this IMidiPart part, double start, double end)
    {
        part.BeginMergeDirty();
        part.Notes.ListModified.BeginMerge();
        foreach (var note in part.AllNotesInSelection(start, end))
        {
            part.RemoveNote(note);
        }
        part.Notes.ListModified.EndMerge();
        part.EndMergeDirty();
    }

    public static void DeleteAllVibratosInSelection(this IMidiPart part, double start, double end)
    {
        part.BeginMergeDirty();
        foreach (var vibrato in part.AllVibratosInSelection(start, end))
        {
            part.RemoveVibrato(vibrato);
        }
        part.EndMergeDirty();
    }

    public static void ClearParameters(this IMidiPart part, double start, double end)
    {
        part.Pitch.Clear(start, end);
        foreach (var automation in part.Automations.Values)
        {
            automation.Clear(start, end, 5);
        }
    }
    internal static MidiPartInfo MergePartInfos(MidiPartInfo[] SortedPartInfos)
    {
        double PosAxisTrans(PartInfo curPart, double pos)
        {
            var startPos = SortedPartInfos[0].Pos;
            var absPos = pos + curPart.Pos;
            return absPos - startPos;
        }
        var ret = new MidiPartInfo();
        var basePart = SortedPartInfos[0];
        ret.Pos = SortedPartInfos.First().Pos;
        ret.Dur = SortedPartInfos.Last().Dur + SortedPartInfos.Last().Pos - ret.Pos;
        ret.Voice = basePart.Voice;
        ret.Gain = basePart.Gain;
        ret.Name = basePart.Name;
        ret.Properties = basePart.Properties;

        for (int i = 0; i < SortedPartInfos.Length; i++)
        {
            var curPart = SortedPartInfos[i];
            var curPartPos = PosAxisTrans(curPart, 0);
            var nextPartPos = i + 1 < SortedPartInfos.Length ? PosAxisTrans(SortedPartInfos[i + 1], 0) : double.MaxValue;
            foreach(var item in curPart.Notes)
            {
                item.Pos=PosAxisTrans(curPart,item.Pos);
                if (item.Pos < curPartPos) continue;
                if (item.Pos >= nextPartPos) break;
                ret.Notes.Add(item);
            }
            foreach (var item in curPart.Vibratos)
            {
                item.Pos = PosAxisTrans(curPart, item.Pos);
                if (item.Pos < curPartPos) continue;
                if (item.Pos >= nextPartPos) break;
                ret.Vibratos.Add(item);
            }
            foreach (var item in curPart.Pitch)
            {
                List<Point> line= new List<Point>();
                foreach (var point in item)
                {
                    var X = PosAxisTrans(curPart, point.X);
                    if (X < curPartPos) continue;
                    if (X >= nextPartPos) break;
                    line.Add(new Point(X, point.Y));
                }
                ret.Pitch.Add(line);
            }
            foreach (var kvp in curPart.Automations)
            {
                if (!ret.Automations.ContainsKey(kvp.Key)) { ret.Automations.Add(kvp.Key, new AutomationInfo() { DefaultValue = kvp.Value.DefaultValue, Points = new List<Point>() }); }

                foreach (var point in kvp.Value.Points)
                {
                    var X = PosAxisTrans(curPart, point.X);
                    if (X < curPartPos) continue;
                    if (X >= nextPartPos) break;
                    ret.Automations[kvp.Key].Points.Add(new Point(X, point.Y));
                }
            }
        }
        return ret;
    }

    public static MidiPartInfo RangeInfo(this IMidiPart part, double start, double end)
    {
        var notes = part.CopyNotes(start, end);
        var vibratos = part.CopyVibratos(start, end);
        var parameters = part.CopyParameters(start, end);
        var automations = new Map<string, AutomationInfo>();
        foreach (var kvp in parameters.Automations)
        {
            automations.Add(kvp.Key, new() { DefaultValue = part.Automations[kvp.Key].DefaultValue.GetInfo(), Points = kvp.Value });
        }
        return new MidiPartInfo()
        {
            Pos = start + part.Pos.Value,
            Dur = end - start,
            Gain = part.Gain.GetInfo(),
            Name = part.Name.GetInfo(),
            Voice = part.Voice.GetInfo(),
            Properties = part.Properties.GetInfo(),
            Pitch = parameters.Pitch,
            Notes = notes,
            Vibratos = vibratos,
            Automations = automations
        };
    }
}