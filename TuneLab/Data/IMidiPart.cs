using DynamicData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Utils;
using TuneLab.Audio;


namespace TuneLab.Data;

internal interface IMidiPart : IPart, IDataObject<MidiPartInfo>
{
    // 合成状态/产物有更新（含会话重建），UI 收到直接刷新；区域信息看 GetSynthesisStatus()。
    IActionEvent SynthesisStatusChanged { get; }
    // 自动化轨集合因参数 commit 而变（条件轨随值显隐）：UI 收到重建参数栏/默认值面板。仅实际变化时触发。
    IActionEvent AutomationConfigsModified { get; }
    INoteList Notes { get; }
    IReadOnlyDataObjectLinkedList<Vibrato> Vibratos { get; }
    DataPropertyObject Properties { get; }
    ISoundSource SoundSource { get; }
    IDataProperty<double> Gain { get; }
    IReadOnlyDataObjectMap<string, IAutomation> Automations { get; }
    // 声明分段轨（除 Pitch 外的可编辑分段曲线），按轨 id 存；Pitch 是专属常驻通道、不入此 map。
    IReadOnlyDataObjectMap<string, IPiecewiseAutomation> PiecewiseAutomations { get; }
    IReadOnlyDataObjectList<IEffect> Effects { get; }
    IPiecewiseAutomation Pitch { get; }

    // —— 合成消费面（session 模型，插件托管状态与产物，宿主拉取展示）——
    Synthesis.ISynthesisPipeline? SynthesisPipeline { get; }
    bool IsSynthesisBatching { get; }
    IReadOnlyList<SynthesisStatusSegment> GetSynthesisStatus();
    IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch { get; }
    // 引擎合成出的参数曲线（按轨 id 键、与音频/音高同一秒时间系）：key 与 Voice.SynthesizedParameterConfigs
    // 对齐，宿主把它们作一等只读回显轨绘制（参数区填充面积、可独立显隐），非叠加到同名编辑轨。
    IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters { get; }
    // 某个 effect 合成出的参数回显曲线（聚合其各段 processor 的回显）：key 与 effect.SynthesizedParameterConfigs
    // 对齐，宿主作一等只读回显轨绘制（与 voice 回显同构、按 AutomationKey 分源）。非本 part 的 effect 返回空 map。
    IReadOnlyMap<string, SynthesizedParameter> GetEffectSynthesizedParameters(IEffect effect);
    IReadOnlyList<Synthesis.SynthesizedSegment> SynthesizedSegments { get; }

    IAutomation? AddAutomation(string automationID);
    IPiecewiseAutomation? AddPiecewiseAutomation(string automationID);
    double[] GetFinalPitch(IReadOnlyList<double> ticks);
    // 有效基线（绘制优先、否则音符半音）：颤音控制柄/波形在未绘制 pitch 时也能锚在音符上即时显示。
    double GetEffectivePitchValue(double tick);
    // 颤音覆盖区内、未绘制 pitch 处的兜底虚线波（音符基线 + 颤音偏差），实线区段交给 GetFinalPitch。
    double[] GetVibratoFallbackPitch(IReadOnlyList<double> ticks);
    // 悬浮添加颤音的预览虚线波（有效基线 + 待建颤音偏移）。
    double[] GetVibratoAddPreviewPitch(IReadOnlyList<double> ticks, VibratoInfo info);
    void LockPitch(double start, double end, double extension);
    double[] GetAutomationValues(IReadOnlyList<double> ticks, string automationID);
    double[] GetFinalAutomationValues(IReadOnlyList<double> ticks, string automationID);
    INote CreateNote(NoteInfo info);
    void InsertNote(INote note);
    bool RemoveNote(INote note);
    // 改排序键（pos/dur）统一走 move：摘除→跑 mutate→按新键重插，调用方只在 mutate 内改属性。
    void MoveNote(INote note, Action mutate);
    void MoveNotes(IReadOnlyCollection<INote> notes, Action mutate);
    IEffect CreateEffect(EffectInfo info);
    void InsertEffect(int index, IEffect effect);
    bool RemoveEffect(IEffect effect);
    Vibrato CreateVibrato(VibratoInfo info);
    void InsertVibrato(Vibrato note);
    bool RemoveVibrato(Vibrato note);
    void MoveVibrato(Vibrato vibrato, Action mutate);
    void MoveVibratos(IReadOnlyCollection<Vibrato> vibratos, Action mutate);
    // 批量变更括号（含 undo/redo 重放）：插件把重活延迟到括号收口。
    void BeginMergeDirty();
    void EndMergeDirty();
}

internal static class IMidiPartExtension
{
    public static bool IsEffectiveAutomation(this IMidiPart part, string id)
    {
        return part.SoundSource.AutomationConfigs.ContainsKey(id);
    }

    public static AutomationConfig GetEffectiveAutomationConfig(this IMidiPart part, string id)
    {
        if (part.SoundSource.AutomationConfigs.ContainsKey(id))
            return part.SoundSource.AutomationConfigs[id];

        throw new ArgumentException(string.Format("Automation {0} is not effective!", id));
    }

    // ── 按 AutomationKey 路由（voice/part 级，或某个 effect）。数据层仍按 plain id 存，这里只做来源分派。 ──
    // 连续/分段由同一张 config map 承载，kind 由 AutomationConfig.IsPiecewise 现解析（AutomationKey 本身不带 kind）。

    // 从对应来源（voice / 某 effect）取该 key 的 config（不分 kind），不存在返回 false。
    static bool TryGetSourceConfig(IMidiPart part, AutomationKey key, out AutomationConfig config)
    {
        if (key.IsEffect)
        {
            if (key.EffectIndex < part.Effects.Count)
                return part.Effects[key.EffectIndex].AutomationConfigs.TryGetValue(key.Id, out config!);
            config = null!;
            return false;
        }

        return part.SoundSource.AutomationConfigs.TryGetValue(key.Id, out config!);
    }

    public static bool IsEffectiveAutomation(this IMidiPart part, AutomationKey key)
    {
        return TryGetSourceConfig(part, key, out var config) && !config.IsPiecewise;
    }

    public static AutomationConfig GetEffectiveAutomationConfig(this IMidiPart part, AutomationKey key)
    {
        if (TryGetSourceConfig(part, key, out var config) && !config.IsPiecewise)
            return config;

        throw new ArgumentException(string.Format("Automation {0} is not effective!", key.Id));
    }

    // 取已存在的自动化数据对象（voice 或对应 effect），不存在返回 null。
    public static IAutomation? GetEffectiveAutomation(this IMidiPart part, AutomationKey key)
    {
        if (key.IsEffect)
        {
            if (key.EffectIndex < part.Effects.Count && part.Effects[key.EffectIndex].Automations.TryGetValue(key.Id, out var effectAutomation))
                return effectAutomation;
            return null;
        }

        return part.Automations.TryGetValue(key.Id, out var voiceAutomation) ? voiceAutomation : null;
    }

    // 取或创建自动化数据对象（按需在对应来源里 Add）。
    public static IAutomation? AddEffectiveAutomation(this IMidiPart part, AutomationKey key)
    {
        if (key.IsEffect)
            return key.EffectIndex < part.Effects.Count ? part.Effects[key.EffectIndex].AddAutomation(key.Id) : null;

        return part.AddAutomation(key.Id);
    }

    // ── 分段轨按 AutomationKey 路由（与连续轨对偶；同一张 config map，kind 由 IsPiecewise 现解析）。 ──

    public static bool IsEffectivePiecewiseAutomation(this IMidiPart part, AutomationKey key)
    {
        return TryGetSourceConfig(part, key, out var config) && config.IsPiecewise;
    }

    public static AutomationConfig GetEffectivePiecewiseAutomationConfig(this IMidiPart part, AutomationKey key)
    {
        if (TryGetSourceConfig(part, key, out var config) && config.IsPiecewise)
            return config;

        throw new ArgumentException(string.Format("Piecewise automation {0} is not effective!", key.Id));
    }

    // 取已存在的分段轨数据对象（voice 或对应 effect），不存在返回 null。
    public static IPiecewiseAutomation? GetEffectivePiecewiseAutomation(this IMidiPart part, AutomationKey key)
    {
        if (key.IsEffect)
        {
            if (key.EffectIndex < part.Effects.Count && part.Effects[key.EffectIndex].PiecewiseAutomations.TryGetValue(key.Id, out var effectAutomation))
                return effectAutomation;
            return null;
        }

        return part.PiecewiseAutomations.TryGetValue(key.Id, out var voiceAutomation) ? voiceAutomation : null;
    }

    // 取或创建分段轨数据对象（按需在对应来源里 Add）。
    public static IPiecewiseAutomation? AddEffectivePiecewiseAutomation(this IMidiPart part, AutomationKey key)
    {
        if (key.IsEffect)
            return key.EffectIndex < part.Effects.Count ? part.Effects[key.EffectIndex].AddPiecewiseAutomation(key.Id) : null;

        return part.AddPiecewiseAutomation(key.Id);
    }

    // 最终曲线取值：voice 走含 vibrato 的最终值；effect 走其自身曲线（无 vibrato）。
    public static double[] GetFinalAutomationValues(this IMidiPart part, IReadOnlyList<double> ticks, AutomationKey key)
    {
        if (key.IsEffect)
        {
            if (key.EffectIndex < part.Effects.Count)
                return part.Effects[key.EffectIndex].GetAutomationValues(ticks, key.Id);

            var values = new double[ticks.Count];
            return values;
        }

        return part.GetFinalAutomationValues(ticks, key.Id);
    }

    public static (int, int) PitchRange(this IMidiPart part)
    {
        var note = part.Notes.First;
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

    // select：是否取消当前选中、改选中粘贴出来的对象（默认 true=标准粘贴行为）。
    // 传 false 则完全不动选中态（用于"有范围选区时粘贴不抢选中态、保持选区高亮"）。
    public static void PasteAt(this IMidiPart part, NoteClipboard clipboard, double pos, bool select = true)
    {
        if (clipboard.IsEmpty())
            return;

        if (select)
            part.Notes.DeselectAllItems();
        part.BeginMergeDirty();
        part.Notes.BeginMergeNotify();
        foreach (var noteInfo in clipboard)
        {
            var note = part.CreateNote(noteInfo);
            if (select)
                note.Select();
            note.Pos.Set(note.Pos.Value + pos);
            part.InsertNote(note);
        }
        part.Notes.EndMergeNotify();
        part.EndMergeDirty();
    }

    public static void PasteAt(this IMidiPart part, VibratoClipboard clipboard, double pos, bool select = true)
    {
        if (clipboard.IsEmpty())
            return;

        if (select)
            part.Vibratos.DeselectAllItems();
        part.BeginMergeDirty();
        foreach (var vibratoInfo in clipboard)
        {
            var vibrato = part.CreateVibrato(vibratoInfo);
            if (select)
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
        part.Notes.BeginMergeNotify();
        foreach (var note in part.Notes.AllSelectedItems())
        {
            part.RemoveNote(note);
        }
        part.Notes.EndMergeNotify();
        part.EndMergeDirty();
    }

    // 把给定 note 集合整理为单声部（去重叠），镜像合成侧"后盖前"钳位：按数据序
    // （StartPos 升 → EndPos 降）逐个把尾巴钳到下一 note 起点；钳后时长归零/为负者
    // ——被完全覆盖的前者、同起点的较长和弦兄弟——直接删除。起点从不移动、只缩尾，
    // 故可对原始位置一遍算定再施加。下一 note 取 scope 内排序相邻者：对子集整理时只在
    // 子集内消重叠，不触动 scope 外的 note。不自行提交，返回是否有改动供调用方决定 Commit。
    public static bool RemoveOverlaps(this IMidiPart part, IEnumerable<INote> scope)
    {
        var ordered = scope.OrderBy(note => note.StartPos()).ThenByDescending(note => note.EndPos()).ToList();
        if (ordered.Count < 2)
            return false;

        var shrink = new List<(INote note, double dur)>();
        var remove = new List<INote>();
        for (int i = 0; i < ordered.Count - 1; i++)
        {
            var note = ordered[i];
            double maxEnd = ordered[i + 1].StartPos();
            if (note.EndPos() <= maxEnd)
                continue;

            double newDur = maxEnd - note.StartPos();
            if (newDur <= 0)
                remove.Add(note);
            else
                shrink.Add((note, newDur));
        }

        if (shrink.Count == 0 && remove.Count == 0)
            return false;

        part.BeginMergeDirty();
        part.Notes.BeginMergeNotify();
        foreach (var (note, dur) in shrink)
        {
            part.MoveNote(note, () => note.Dur.Set(dur));
        }
        foreach (var note in remove)
        {
            part.RemoveNote(note);
        }
        part.Notes.EndMergeNotify();
        part.EndMergeDirty();
        return true;
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
        part.Notes.BeginMergeNotify();
        foreach (var note in part.AllNotesInSelection(start, end))
        {
            part.RemoveNote(note);
        }
        part.Notes.EndMergeNotify();
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

    public static void DeselectAllAutomationPoints(this IMidiPart part)
    {
        foreach (var automation in part.Automations.Values)
        {
            automation.Points.DeselectAllItems();
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
        ret.SoundSource = basePart.SoundSource;
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
                List<TuneLab.Foundation.Point> line= new List<TuneLab.Foundation.Point>();
                foreach (var point in item)
                {
                    var X = PosAxisTrans(curPart, point.X);
                    if (X < curPartPos) continue;
                    if (X >= nextPartPos) break;
                    line.Add(new TuneLab.Foundation.Point(X, point.Y));
                }
                ret.Pitch.Add(line);
            }
            foreach (var kvp in curPart.Automations)
            {
                if (!ret.Automations.ContainsKey(kvp.Key)) { ret.Automations.Add(kvp.Key, new AutomationInfo() { DefaultValue = kvp.Value.DefaultValue, Points = new List<TuneLab.Foundation.Point>() }); }

                foreach (var point in kvp.Value.Points)
                {
                    var X = PosAxisTrans(curPart, point.X);
                    if (X < curPartPos) continue;
                    if (X >= nextPartPos) break;
                    ret.Automations[kvp.Key].Points.Add(new TuneLab.Foundation.Point(X, point.Y));
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
            SoundSource = part.SoundSource.GetInfo(),
            Properties = part.Properties.GetInfo(),
            Pitch = parameters.Pitch,
            Notes = notes,
            Vibratos = vibratos,
            Automations = automations
        };
    }
}
