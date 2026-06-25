using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jint.Native;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Scripting;

// 脚本面向的「句柄」（对象式 API 的核心）：每个句柄包装一个数据层对象（轨/part/note/vibrato），向脚本暴露
//  · 可读写的【标量字段】（裸属性，如 note.pos / note.pitch）——读即实时读底层，写经宿主收口；
//  · 查询/创建/删除/计算的【方法】（带括号，如 part.notes() / track.addPart() / part.removeNote()）。
// 心智模型：裸属性 = 单个标量字段；带括号 = 一次查询或动作。
//
// 增删一律【挂父】：父建子（project.addTrack / track.addPart / part.addNote / part.addVibrato）、父删子
// （project.removeTrack / track.removePart / part.removeNote / part.removeVibrato）——与 SV 一致，且对 LLM 对称、
// 不会照"增在父"脑补出不存在的 API。句柄持有回宿主（ScriptContext）的引用，写操作经它统一收口（merge 括号 +
// 改动计数 + 整段脚本末尾一次 Commit）；句柄本身不 Commit。句柄是【临时】的：仅当次运行有效、不可写死、不跨次
// 运行（数据层对象无持久 id，重启即失效）；被删除后再用会报错。
//
// 坐标铁律：对外位置/时长一律【绝对（全局）tick】——note/vibrato.pos 已加回所属 part 起点，落库时减回。音高 = MIDI。

// 一个音符句柄。
internal sealed class ScriptNote(ScriptContext ctx, INote note)
{
    internal INote Note { get; } = note;
    internal bool Removed { get; set; }

    INote N => Removed ? throw new ScriptApiException("this note handle was removed and is no longer valid.") : Note;

    public double Pos { get => N.GlobalStartPos(); set => Apply(absPos: value); }   // 绝对 tick
    public double Dur { get => N.Dur.Value; set => Apply(dur: value); }
    public int Pitch { get => N.Pitch.Value; set => Apply(pitch: value); }          // MIDI 0..127
    public string Lyric { get => N.Lyric.Value; set => Apply(lyric: value ?? string.Empty); }
    public string PitchName => MusicTheory.PitchName(N.Pitch.Value);                 // 只读，如 "C4"

    // 批量原子改：{pos?, dur?, pitch?, lyric?}（改 pos/dur 只重排一次）。
    public void Set(JsValue props)
    {
        var o = ScriptArgs.Obj(props, "props");
        Apply(
            pitch: ScriptArgs.OptInt(o, "pitch"),
            absPos: ScriptArgs.OptNum(o, "pos"),
            dur: ScriptArgs.OptNum(o, "dur"),
            lyric: ScriptArgs.OptStr(o, "lyric"));
    }

    // 单字段属性 setter 与 Set() 共用：改 pos/dur 经 MoveNote 摘除-重插维持有序（通知合并由数据层收口）。
    void Apply(int? pitch = null, double? absPos = null, double? dur = null, string? lyric = null)
    {
        var n = N;
        var midi = n.Part;
        if (dur is { } vd && vd <= 0) throw new ScriptApiException("dur must be positive.");
        ctx.EnsureBracket(midi);
        double? relPos = absPos is { } ap ? ap - midi.Pos.Value : null;
        midi.MoveNote(n, () =>
        {
            if (relPos is { } p2) n.Pos.Set(p2);
            if (dur is { } d2) n.Dur.Set(d2);
            if (pitch is { } pit) n.Pitch.Set(Math.Clamp(pit, MusicTheory.MIN_PITCH, MusicTheory.MAX_PITCH));
            if (lyric != null) n.Lyric.Set(lyric);
        });
        ctx.Bump();
    }

    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "Note(pos={0:0}, dur={1:0}, pitch={2}/{3}, lyric=\"{4}\")",
            Pos, Dur, Pitch, PitchName, Lyric);
}

// 一个 part 句柄（midi 或 audio）。音符/曲线/颤音只对 midi part 有效。
internal sealed class ScriptPart(ScriptContext ctx, IPart part)
{
    internal IPart Part { get; } = part;
    internal bool Removed { get; set; }

    IPart P => Removed ? throw new ScriptApiException("this part handle was removed and is no longer valid.") : Part;
    IMidiPart Midi => P is IMidiPart m ? m : throw new ScriptApiException("this part is not a MIDI part; notes/curves require a MIDI part.");

    public string Name { get => P.Name.Value; set => Apply(name: value); }
    public double Pos { get => P.Pos.Value; set => Apply(pos: value); }   // 绝对 tick
    public double Dur { get => P.Dur.Value; set => Apply(dur: value); }
    public string Type => P is IMidiPart ? "midi" : "audio";

    // 本 part 的声源信息（只读快照）。仅 midi part。kind 区分 voice / instrument。
    public ScriptVoice Voice()
    {
        var v = Midi.SoundSource;
        return new ScriptVoice(v.Type, v.ID, v.Name, v.DefaultLyric, v.Kind == SourceKind.Voice ? "voice" : "instrument");
    }

    // ── 音符 ──

    public ScriptNote[] Notes() => Midi.Notes.Select(ctx.WrapNote).ToArray();

    // 钢琴窗里用户当前选中的音符；无选中返回空数组。
    public ScriptNote[] SelectedNotes() => Midi.Notes.AllSelectedItems().Select(ctx.WrapNote).ToArray();

    // 绝对 tick 区间 [startTick, endTick) 内（按起点判定）的音符。
    public ScriptNote[] NotesInRange(double startTick, double endTick)
    {
        var midi = Midi;
        double pos = midi.Pos.Value;
        return midi.Notes
            .Where(n => n.Pos.Value + pos >= startTick && n.Pos.Value + pos < endTick)
            .Select(ctx.WrapNote).ToArray();
    }

    // info: {pos, dur, pitch, lyric?}。pos 绝对 tick。
    public ScriptNote AddNote(JsValue info)
    {
        var midi = Midi;
        var o = ScriptArgs.Obj(info, "info");
        double pos = ScriptArgs.ReqNum(o, "pos");
        double dur = ScriptArgs.ReqNum(o, "dur");
        int pitch = Math.Clamp(ScriptArgs.ReqInt(o, "pitch"), MusicTheory.MIN_PITCH, MusicTheory.MAX_PITCH);
        if (dur <= 0) throw new ScriptApiException("dur must be positive.");
        ctx.EnsureBracket(midi);
        var note = midi.CreateNote(new NoteInfo { Pos = pos - midi.Pos.Value, Dur = dur, Pitch = pitch, Lyric = ScriptArgs.OptStr(o, "lyric") ?? string.Empty });
        midi.InsertNote(note);
        ctx.Bump();
        return ctx.WrapNote(note);
    }

    public void RemoveNote(ScriptNote note)
    {
        if (note == null || note.Removed) throw new ScriptApiException("expected a live note handle (from part.notes()/part.addNote()).");
        var midi = Midi;
        ctx.EnsureBracket(midi);
        midi.RemoveNote(note.Note);
        note.Removed = true;
        ctx.Bump();
    }

    // ── 音高曲线（pitch，独立显眼，对齐 C# midi.Pitch） ──

    // 在绝对 tick 区间 [startTick, endTick] 上等距采样最终音高曲线（MIDI 标度）。
    public double[] SamplePitch(double startTick, double endTick, int samples)
        => Midi.GetFinalPitch(SampleTicks(Midi, startTick, endTick, samples));

    // 覆盖写音高曲线：清空 [startTick,endTick) 再落线。points=[{tick,value}]，value=绝对 MIDI 音高（可含小数）。
    public void SetPitchLine(double startTick, double endTick, JsValue points)
    {
        var midi = Midi;
        double rel = midi.Pos.Value;
        var pts = ScriptArgs.ReadPoints(points);
        ctx.EnsureBracket(midi);
        midi.Pitch.Clear(startTick - rel, endTick - rel);
        if (pts.Count > 0)
            midi.Pitch.AddLine(pts.OrderBy(p => p.X).Select(p => new AnchorPoint(p.X - rel, p.Y)).ToList(), 0);
        ctx.Bump();
    }

    public void ClearPitch(double startTick, double endTick)
    {
        var midi = Midi;
        double rel = midi.Pos.Value;
        ctx.EnsureBracket(midi);
        midi.Pitch.Clear(startTick - rel, endTick - rel);
        ctx.Bump();
    }

    // ── 自动化曲线（automation，对齐 C# midi.Automations；不含 pitch） ──

    // 可编辑的自动化轨 id 列表（voice 声明，如 "Volume"；不含 pitch）。
    public string[] AutomationIds() => Midi.SoundSource.AutomationConfigs.Keys.Select(k => k.Id).ToArray();

    // 在绝对 tick 区间 [startTick, endTick] 上等距采样某自动化曲线。NaN = 该处无曲线。
    public double[] SampleAutomation(string id, double startTick, double endTick, int samples)
    {
        var midi = Midi;
        if (!midi.IsEffectiveAutomation(id))
            throw new ScriptApiException(string.Format("unknown automation \"{0}\"; use one of part.automationIds().", id));
        return midi.GetAutomationValues(SampleTicks(midi, startTick, endTick, samples), id);
    }

    // 覆盖写自动化曲线：清空 [startTick,endTick) 再落线。points=[{tick,value}]，value=参数绝对值；轨不存在按需创建，defaultValue 可选。
    public void SetAutomation(string id, double startTick, double endTick, JsValue points, JsValue defaultValue)
    {
        var midi = Midi;
        double rel = midi.Pos.Value;
        var pts = ScriptArgs.ReadPoints(points);
        ctx.EnsureBracket(midi);
        var automation = GetOrAddAutomation(midi, id);
        if (ScriptArgs.AsNumOrNull(defaultValue) is { } dv) automation.DefaultValue.Set(dv);
        automation.Clear(startTick - rel, endTick - rel, 0);
        if (pts.Count > 0)
            automation.AddLine(pts.OrderBy(p => p.X).Select(p => new AnchorPoint(p.X - rel, p.Y)).ToList(), 0);
        ctx.Bump();
    }

    public void ClearAutomation(string id, double startTick, double endTick)
    {
        var midi = Midi;
        double rel = midi.Pos.Value;
        ctx.EnsureBracket(midi);
        if (midi.Automations.TryGetValue(id, out var automation))
            automation.Clear(startTick - rel, endTick - rel, 0);
        ctx.Bump();
    }

    // 等距采样 tick 序列（part 相对），供 samplePitch/sampleAutomation 共用。
    static double[] SampleTicks(IMidiPart midi, double startTick, double endTick, int samples)
    {
        if (samples < 2) samples = 2;
        if (samples > 1000) samples = 1000;
        if (endTick <= startTick) throw new ScriptApiException("endTick must be greater than startTick.");
        double pos = midi.Pos.Value;
        var ticks = new double[samples];
        double step = (endTick - startTick) / (samples - 1);
        for (int i = 0; i < samples; i++)
            ticks[i] = startTick + step * i - pos;   // part 相对
        return ticks;
    }

    // ── 颤音 ──

    public ScriptVibrato[] Vibratos() => Midi.Vibratos.Select(ctx.WrapVibrato).ToArray();

    // info: {pos, dur, frequency?, amplitude?, phase?, attack?, release?}。pos 绝对 tick；叠加在音高曲线之上。
    public ScriptVibrato AddVibrato(JsValue info)
    {
        var midi = Midi;
        var o = ScriptArgs.Obj(info, "info");
        double pos = ScriptArgs.ReqNum(o, "pos");
        double dur = ScriptArgs.ReqNum(o, "dur");
        if (dur <= 0) throw new ScriptApiException("dur must be positive.");
        ctx.EnsureBracket(midi);
        var vibrato = midi.CreateVibrato(new VibratoInfo
        {
            Pos = pos - midi.Pos.Value,
            Dur = dur,
            Frequency = ScriptArgs.OptNum(o, "frequency") ?? 6,
            Amplitude = ScriptArgs.OptNum(o, "amplitude") ?? 1,
            Phase = ScriptArgs.OptNum(o, "phase") ?? 0,
            Attack = ScriptArgs.OptNum(o, "attack") ?? 0.2,
            Release = ScriptArgs.OptNum(o, "release") ?? 0.2,
        });
        midi.InsertVibrato(vibrato);
        ctx.Bump();
        return ctx.WrapVibrato(vibrato);
    }

    public void RemoveVibrato(ScriptVibrato vibrato)
    {
        if (vibrato == null || vibrato.Removed) throw new ScriptApiException("expected a live vibrato handle (from part.vibratos()/part.addVibrato()).");
        var midi = Midi;
        ctx.EnsureBracket(midi);
        midi.RemoveVibrato(vibrato.Vibrato);
        vibrato.Removed = true;
        ctx.Bump();
    }

    // ── part 自身 ──

    public void Set(JsValue props)
    {
        var o = ScriptArgs.Obj(props, "props");
        Apply(ScriptArgs.OptStr(o, "name"), ScriptArgs.OptNum(o, "pos"), ScriptArgs.OptNum(o, "dur"));
    }

    // 单字段属性 setter 与 Set() 共用：改 pos/dur 经 MovePart 摘除-重插维持轨内有序。
    void Apply(string? name = null, double? pos = null, double? dur = null)
    {
        var p = P;
        if (pos is { } vp && vp < 0) throw new ScriptApiException("pos must be >= 0.");
        if (dur is { } vd && vd <= 0) throw new ScriptApiException("dur must be positive.");
        p.Track.MovePart(p, () =>
        {
            if (name != null) p.Name.Set(name);
            if (pos is { } p2) p.Pos.Set(p2);
            if (dur is { } d2) p.Dur.Set(d2);
        });
        ctx.Bump();
    }

    IAutomation GetOrAddAutomation(IMidiPart part, string id)
    {
        if (part.Automations.TryGetValue(id, out var existing))
            return existing;
        var created = part.AddAutomation(id);
        if (created == null)
            throw new ScriptApiException(string.Format("automation \"{0}\" is not available on this part (not declared by its voice).", id));
        return created;
    }

    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "Part(\"{0}\", {1}, ticks [{2:0}..{3:0}])",
            P.Name.Value, Type, Pos, Pos + Dur);
}

// 一个轨道句柄。
internal sealed class ScriptTrack(ScriptContext ctx, ITrack track)
{
    internal ITrack Track { get; } = track;
    internal bool Removed { get; set; }

    ITrack T => Removed ? throw new ScriptApiException("this track handle was removed and is no longer valid.") : Track;

    public string Name { get => T.Name.Value; set => Apply(name: value); }
    public bool IsMute { get => T.IsMute.Value; set => Apply(mute: value); }
    public bool IsSolo { get => T.IsSolo.Value; set => Apply(solo: value); }
    public double Gain { get => T.Gain.Value; set => Apply(gain: value); }   // dB（0 = 原始电平）
    public double Pan { get => T.Pan.Value; set => Apply(pan: value); }

    public ScriptPart[] Parts() => T.Parts.Select(ctx.WrapPart).ToArray();

    // info: {pos, dur, name?}。在本轨新建空 midi part（绝对 tick）。
    public ScriptPart AddPart(JsValue info)
    {
        var t = T;
        var o = ScriptArgs.Obj(info, "info");
        double pos = ScriptArgs.ReqNum(o, "pos");
        double dur = ScriptArgs.ReqNum(o, "dur");
        if (pos < 0) throw new ScriptApiException("pos must be >= 0.");
        if (dur <= 0) throw new ScriptApiException("dur must be positive.");
        var part = t.CreatePart(new MidiPartInfo { Name = ScriptArgs.OptStr(o, "name") ?? "Part", Pos = pos, Dur = dur });
        t.InsertPart(part);
        ctx.Bump();
        return ctx.WrapPart(part);
    }

    public void RemovePart(ScriptPart part)
    {
        if (part == null || part.Removed) throw new ScriptApiException("expected a live part handle (from track.parts()/track.addPart()).");
        T.RemovePart(part.Part);
        part.Removed = true;
        ctx.Bump();
    }

    public void Set(JsValue props)
    {
        var o = ScriptArgs.Obj(props, "props");
        Apply(ScriptArgs.OptStr(o, "name"), ScriptArgs.OptBool(o, "isMute"), ScriptArgs.OptBool(o, "isSolo"),
            ScriptArgs.OptNum(o, "gain"), ScriptArgs.OptNum(o, "pan"));
    }

    void Apply(string? name = null, bool? mute = null, bool? solo = null, double? gain = null, double? pan = null)
    {
        var t = T;
        if (name != null) t.Name.Set(name);
        if (mute is { } m) t.IsMute.Set(m);
        if (solo is { } s) t.IsSolo.Set(s);
        if (gain is { } g) t.Gain.Set(g);
        if (pan is { } p) t.Pan.Set(Math.Clamp(p, -1, 1));
        ctx.Bump();
    }

    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "Track(\"{0}\", parts={1})", Track.Name.Value, Track.Parts.Count());
}

// 一个颤音句柄。坐标：pos 绝对 tick。叠加在音高曲线之上。
internal sealed class ScriptVibrato(ScriptContext ctx, Vibrato vibrato)
{
    internal Vibrato Vibrato { get; } = vibrato;
    internal bool Removed { get; set; }

    Vibrato V => Removed ? throw new ScriptApiException("this vibrato handle was removed and is no longer valid.") : Vibrato;

    public double Pos { get => V.GlobalStartPos(); set => Apply(absPos: value); }   // 绝对 tick
    public double Dur { get => V.Dur.Value; set => Apply(dur: value); }
    public double Frequency { get => V.Frequency.Value; set => Apply(frequency: value); }   // Hz
    public double Amplitude { get => V.Amplitude.Value; set => Apply(amplitude: value); }   // 半音
    public double Phase { get => V.Phase.Value; set => Apply(phase: value); }
    public double Attack { get => V.Attack.Value; set => Apply(attack: value); }            // 秒
    public double Release { get => V.Release.Value; set => Apply(release: value); }          // 秒

    public void Set(JsValue props)
    {
        var o = ScriptArgs.Obj(props, "props");
        Apply(
            absPos: ScriptArgs.OptNum(o, "pos"),
            dur: ScriptArgs.OptNum(o, "dur"),
            frequency: ScriptArgs.OptNum(o, "frequency"),
            amplitude: ScriptArgs.OptNum(o, "amplitude"),
            phase: ScriptArgs.OptNum(o, "phase"),
            attack: ScriptArgs.OptNum(o, "attack"),
            release: ScriptArgs.OptNum(o, "release"));
    }

    // 改 pos/dur 经 MoveVibrato 摘除-重插维持列表有序（与 note/part 一致，通知合并由数据层收口）。
    void Apply(double? absPos = null, double? dur = null, double? frequency = null, double? amplitude = null,
        double? phase = null, double? attack = null, double? release = null)
    {
        var v = V;
        var midi = v.Part;
        if (dur is { } vd && vd <= 0) throw new ScriptApiException("dur must be positive.");
        ctx.EnsureBracket(midi);
        double? relPos = absPos is { } ap ? ap - midi.Pos.Value : null;
        midi.MoveVibrato(v, () =>
        {
            if (relPos is { } p) v.Pos.Set(p);
            if (dur is { } d) v.Dur.Set(d);
            if (frequency is { } f) v.Frequency.Set(f);
            if (amplitude is { } a) v.Amplitude.Set(a);
            if (phase is { } ph) v.Phase.Set(ph);
            if (attack is { } at) v.Attack.Set(at);
            if (release is { } re) v.Release.Set(re);
        });
        ctx.Bump();
    }

    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "Vibrato(pos={0:0}, dur={1:0}, freq={2:0.##}Hz, amp={3:0.##})",
            Pos, Dur, Frequency, Amplitude);
}

// 一个 part 的声源信息（只读快照）。kind = "voice" | "instrument"。
internal sealed class ScriptVoice(string type, string id, string name, string defaultLyric, string kind)
{
    public string Type { get; } = type;
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Kind { get; } = kind;
    // 默认歌词（instrument 恒 "a"，无意义；保留以兼容既有脚本字段）。
    public string DefaultLyric { get; } = defaultLyric;
    public override string ToString() => string.Format(CultureInfo.InvariantCulture, "SoundSource(\"{0}\", kind={1}, type={2}, id={3})", Name, Kind, Type, Id);
}

// 一个速度标记（只读快照）。
internal sealed class ScriptTempo(double bpm, double tick)
{
    public double Bpm { get; } = bpm;
    public double Tick { get; } = tick;
    public override string ToString() => string.Format(CultureInfo.InvariantCulture, "Tempo({0:0.##}bpm@{1:0})", Bpm, Tick);
}

// 一个拍号标记（只读快照）；Bar 为 1-based 小节号。
internal sealed class ScriptTimeSignature(int numerator, int denominator, int bar)
{
    public int Numerator { get; } = numerator;
    public int Denominator { get; } = denominator;
    public int Bar { get; } = bar;
    public override string ToString() => string.Format(CultureInfo.InvariantCulture, "TimeSig({0}/{1}@bar{2})", Numerator, Denominator, Bar);
}

// 播放线位置（只读快照）。
internal sealed class ScriptPlayhead(double tick, double seconds, int bar, double beat, bool playing)
{
    public double Tick { get; } = tick;
    public double Seconds { get; } = seconds;
    public int Bar { get; } = bar;          // 1-based
    public double Beat { get; } = beat;     // 1-based
    public bool Playing { get; } = playing;
    public override string ToString() => string.Format(CultureInfo.InvariantCulture, "Playhead(tick={0:0}, bar {1}:{2:0.##}, playing={3})", Tick, Bar, Beat, Playing);
}

