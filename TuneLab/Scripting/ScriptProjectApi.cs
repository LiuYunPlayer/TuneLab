using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using TuneLab.Audio;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Scripting;

// 脚本里抛出的"用法/参数错误"——宿主据其 Message 把清晰错误回报给脚本作者（含 agent 模型），不带 C# 栈噪声。
internal sealed class ScriptApiException(string message) : Exception(message);

// 脚本的动作面（注入脚本的全局 `tl`）。这是一个【独立模块】：只依赖数据层（TuneLab.Data/Foundation），
// 不依赖 agent。读方法返回句柄/快照（被 Jint 编组成 JS 对象/数组），写方法【立即改数据层但都不 Commit】——
// 整段脚本跑完由宿主（ScriptRunner）统一收口成一次 Commit（=一个可撤销单位）。
//
// merge / autoprepare / commit 这些"危险包裹"对脚本语言面【完全不可见】：
//  · Commit 是宿主在最外层 finally 里调一次；
//  · part 的 BeginMergeDirty/EndMergeDirty（合并通知 + 把合成重活延迟到括号收口，即 autoprepare 抑制）
//    与 Notes.BeginMergeNotify/EndMergeNotify 由本类【惰性按 part 开】、宿主在 Finish() 统一收口。
//
// 坐标铁律：对外位置/时长一律【绝对（全局）tick】，落数据层时本类减去 part 起点；读时句柄属性加回。
internal sealed class ScriptProjectApi
{
    readonly IProject mProject;
    readonly Func<IMidiPart?>? mCurrentPart;
    readonly Func<IQuantization?>? mQuantization;

    // 句柄缓存（按底层对象引用）：同一对象多次读取返回同一句柄，使脚本里 === 成立、并支持删除后置失效标记。
    readonly Dictionary<INote, ScriptNote> mNotes = new();
    readonly Dictionary<IPart, ScriptPart> mParts = new();
    readonly Dictionary<ITrack, ScriptTrack> mTracks = new();

    // 已开 merge 括号的 midi part；Finish() 时统一收口。
    readonly HashSet<IMidiPart> mBracketed = new();
    int mChanges;   // 发生的改动计数（>0 才 Commit）

    public ScriptProjectApi(IProject project, Func<IMidiPart?>? currentPart, Func<IQuantization?>? quantization)
    {
        mProject = project;
        mCurrentPart = currentPart;
        mQuantization = quantization;
    }

    // ───────────────── 宿主收口（脚本不可见） ─────────────────

    // 关闭所有 merge 括号；有改动则提交成一次可撤销单位。返回是否提交。宿主在 finally 调用（含脚本抛错路径，
    // 此时把"出错前已发生的改动"也作为一个可撤销单位落地——与 apply_edits 的"部分成功也落地"一致）。
    public bool Finish()
    {
        foreach (var part in mBracketed)
        {
            part.Notes.EndMergeNotify();
            part.EndMergeDirty();
        }
        mBracketed.Clear();
        if (mChanges > 0)
        {
            mProject.Commit();
            return true;
        }
        return false;
    }

    public int ChangeCount => mChanges;

    // ───────────────── 只读 ─────────────────

    public double Ppq => MusicTheory.RESOLUTION;

    public ScriptTrack[] Tracks() => mProject.Tracks.Select(WrapTrack).ToArray();

    public ScriptPart[] Parts(ScriptTrack track) => TrackOf(track).Parts.Select(WrapPart).ToArray();

    public ScriptNote[] Notes(ScriptPart part) => MidiOf(part).Notes.Select(WrapNote).ToArray();

    // 该 part 内当前被选中的音符（钢琴窗里用户选中的）；无选中返回空数组。
    public ScriptNote[] SelectedNotes(ScriptPart part) => MidiOf(part).Notes.AllSelectedItems().Select(WrapNote).ToArray();

    // 绝对 tick 区间 [startTick, endTick) 内（按起点判定）的音符。
    public ScriptNote[] NotesInRange(ScriptPart part, double startTick, double endTick)
    {
        var midi = MidiOf(part);
        double pos = midi.Pos.Value;
        return midi.Notes
            .Where(n => n.Pos.Value + pos >= startTick && n.Pos.Value + pos < endTick)
            .Select(WrapNote).ToArray();
    }

    // 钢琴窗当前打开编辑的 midi part；未打开返回 null。
    public ScriptPart? CurrentPart()
    {
        var part = mCurrentPart?.Invoke();
        return part == null ? null : WrapPart(part);
    }

    // 把绝对 tick 吸附到当前量化网格；无网格时原样返回。
    public double Snap(double tick)
    {
        var q = mQuantization?.Invoke();
        int cell = q?.TicksPerCell() ?? 0;
        return cell <= 0 ? tick : Math.Round(tick / cell) * cell;
    }

    public ScriptTempo[] Tempos()
        => mProject.TempoManager.Tempos.Select(t => new ScriptTempo(t.Bpm, t.Pos)).ToArray();

    public ScriptTimeSignature[] TimeSignatures()
        => mProject.TimeSignatureManager.TimeSignatures.Select(s => new ScriptTimeSignature(s.Numerator, s.Denominator, s.BarIndex + 1)).ToArray();

    public ScriptPlayhead Playhead()
    {
        double sec = AudioEngine.CurrentTime;
        double tick = mProject.TempoManager.GetTick(sec);
        var (bar, beat) = mProject.TimeSignatureManager.GetBarAndBeatIndexForTick(tick);
        return new ScriptPlayhead(tick, sec, bar + 1, beat + 1, AudioEngine.IsPlaying);
    }

    // 该 part 可编辑的参数 id 列表（"pitch" + voice 级自动化轨）。
    public string[] ParameterIds(ScriptPart part)
    {
        var midi = MidiOf(part);
        var ids = new List<string> { "pitch" };
        ids.AddRange(midi.Voice.AutomationConfigs.Keys);
        return ids.ToArray();
    }

    // 在绝对 tick 区间 [startTick, endTick] 上等距采样某参数曲线（"pitch" 或自动化轨 id）。NaN 表示无曲线。
    public double[] SampleParameter(ScriptPart part, string id, double startTick, double endTick, int samples)
    {
        var midi = MidiOf(part);
        if (samples < 2) samples = 2;
        if (samples > 1000) samples = 1000;
        if (endTick <= startTick) throw new ScriptApiException("endTick must be greater than startTick.");
        double pos = midi.Pos.Value;
        var ticks = new double[samples];
        double step = (endTick - startTick) / (samples - 1);
        for (int i = 0; i < samples; i++)
            ticks[i] = startTick + step * i - pos;   // part 相对

        if (string.Equals(id, "pitch", StringComparison.OrdinalIgnoreCase))
            return midi.GetFinalPitch(ticks);
        if (midi.IsEffectiveAutomation(id))
            return midi.GetAutomationValues(ticks, id);
        throw new ScriptApiException(string.Format("unknown parameter \"{0}\"; use \"pitch\" or one of parameterIds(part).", id));
    }

    // ───────────────── 轨道写 ─────────────────

    public ScriptTrack AddTrack(JsValue name)
    {
        mProject.AddTrack(new TrackInfo { Name = AsStrOrNull(name) ?? "Track" });
        mChanges++;
        return WrapTrack(mProject.Tracks[mProject.Tracks.Count - 1]);
    }

    public void RemoveTrack(ScriptTrack track)
    {
        var t = TrackOf(track);
        mProject.RemoveTrack(t);
        track.Removed = true;
        mChanges++;
    }

    // props: {name?, mute?, solo?, gainDb?, pan?}
    public void SetTrack(ScriptTrack track, JsValue props)
    {
        var t = TrackOf(track);
        var o = Obj(props, "props");
        if (OptStr(o, "name") is { } name) t.Name.Set(name);
        if (OptBool(o, "mute") is { } mute) t.IsMute.Set(mute);
        if (OptBool(o, "solo") is { } solo) t.IsSolo.Set(solo);
        if (OptNum(o, "gainDb") is { } gain) t.Gain.Set(gain);
        if (OptNum(o, "pan") is { } pan) t.Pan.Set(Math.Clamp(pan, -1, 1));
        mChanges++;
    }

    // ───────────────── part 写 ─────────────────

    // info: {pos, dur, name?}。在某轨新建空 midi part。
    public ScriptPart AddPart(ScriptTrack track, JsValue info)
    {
        var t = TrackOf(track);
        var o = Obj(info, "info");
        double pos = ReqNum(o, "pos");
        double dur = ReqNum(o, "dur");
        if (pos < 0) throw new ScriptApiException("pos must be >= 0.");
        if (dur <= 0) throw new ScriptApiException("dur must be positive.");
        var part = t.CreatePart(new MidiPartInfo { Name = OptStr(o, "name") ?? "Part", Pos = pos, Dur = dur });
        t.InsertPart(part);
        mChanges++;
        return WrapPart(part);
    }

    public void RemovePart(ScriptPart part)
    {
        var p = PartOf(part);
        p.Track.RemovePart(p);
        part.Removed = true;
        mChanges++;
    }

    // props: {name?, pos?, dur?}。改 pos/dur 走摘除-重插维持轨内有序。
    public void SetPart(ScriptPart part, JsValue props)
    {
        var p = PartOf(part);
        var o = Obj(props, "props");
        string? name = OptStr(o, "name");
        double? pos = OptNum(o, "pos");
        double? dur = OptNum(o, "dur");
        if (pos is { } vp && vp < 0) throw new ScriptApiException("pos must be >= 0.");
        if (dur is { } vd && vd <= 0) throw new ScriptApiException("dur must be positive.");
        if (name != null) p.Name.Set(name);
        bool reorder = (pos is { } np && np != p.Pos.Value) || (dur is { } nd && nd != p.Dur.Value);
        if (reorder) p.Track.RemovePart(p);
        if (pos is { } p2) p.Pos.Set(p2);
        if (dur is { } d2) p.Dur.Set(d2);
        if (reorder) p.Track.InsertPart(p);
        mChanges++;
    }

    // ───────────────── 音符写 ─────────────────

    // info: {pos, dur, pitch, lyric?}。pos 绝对 tick。
    public ScriptNote AddNote(ScriptPart part, JsValue info)
    {
        var midi = MidiOf(part);
        var o = Obj(info, "info");
        double pos = ReqNum(o, "pos");
        double dur = ReqNum(o, "dur");
        int pitch = Math.Clamp(ReqInt(o, "pitch"), MusicTheory.MIN_PITCH, MusicTheory.MAX_PITCH);
        if (dur <= 0) throw new ScriptApiException("dur must be positive.");
        EnsureBracket(midi);
        var note = midi.CreateNote(new NoteInfo { Pos = pos - midi.Pos.Value, Dur = dur, Pitch = pitch, Lyric = OptStr(o, "lyric") ?? string.Empty });
        midi.InsertNote(note);
        mChanges++;
        return WrapNote(note);
    }

    // props: {pitch?, pos?, dur?, lyric?}。改 pos/dur 走摘除-重插维持有序。
    public void SetNote(ScriptNote note, JsValue props)
    {
        var n = NoteOf(note);
        var midi = n.Part;
        var o = Obj(props, "props");
        int? pitch = OptInt(o, "pitch");
        double? pos = OptNum(o, "pos");
        double? dur = OptNum(o, "dur");
        string? lyric = OptStr(o, "lyric");
        if (dur is { } vd && vd <= 0) throw new ScriptApiException("dur must be positive.");
        EnsureBracket(midi);
        double? relPos = pos is { } ap ? ap - midi.Pos.Value : null;
        bool reorder = (relPos is { } rp && rp != n.Pos.Value) || (dur is { } nd && nd != n.Dur.Value);
        if (reorder) midi.RemoveNote(n);
        if (relPos is { } p2) n.Pos.Set(p2);
        if (dur is { } d2) n.Dur.Set(d2);
        if (pitch is { } pit) n.Pitch.Set(Math.Clamp(pit, MusicTheory.MIN_PITCH, MusicTheory.MAX_PITCH));
        if (lyric != null) n.Lyric.Set(lyric);
        if (reorder) midi.InsertNote(n);
        mChanges++;
    }

    public void RemoveNote(ScriptNote note)
    {
        var n = NoteOf(note);
        EnsureBracket(n.Part);
        n.Part.RemoveNote(n);
        note.Removed = true;
        mChanges++;
    }

    // ───────────────── 曲线写 ─────────────────

    // 覆盖写音高曲线：清空 [startTick,endTick) 再落线。points=[{tick,value}]，value=绝对 MIDI 音高（可含小数）。
    public void SetPitchLine(ScriptPart part, double startTick, double endTick, JsValue points)
    {
        var midi = MidiOf(part);
        double rel = midi.Pos.Value;
        var pts = ReadPoints(points);
        EnsureBracket(midi);
        midi.Pitch.Clear(startTick - rel, endTick - rel);
        if (pts.Count > 0)
            midi.Pitch.AddLine(pts.OrderBy(p => p.X).Select(p => new AnchorPoint(p.X - rel, p.Y)).ToList(), 0);
        mChanges++;
    }

    public void ClearPitch(ScriptPart part, double startTick, double endTick)
    {
        var midi = MidiOf(part);
        double rel = midi.Pos.Value;
        EnsureBracket(midi);
        midi.Pitch.Clear(startTick - rel, endTick - rel);
        mChanges++;
    }

    // 覆盖写自动化曲线：清空再落线。points=[{tick,value}]，value=参数绝对值。轨不存在按需创建。
    public void SetAutomation(ScriptPart part, string id, double startTick, double endTick, JsValue points, JsValue defaultValue)
    {
        var midi = MidiOf(part);
        double rel = midi.Pos.Value;
        var pts = ReadPoints(points);
        EnsureBracket(midi);
        var automation = GetOrAddAutomation(midi, id);
        if (AsNumOrNull(defaultValue) is { } dv) automation.DefaultValue.Set(dv);
        automation.Clear(startTick - rel, endTick - rel, 0);
        if (pts.Count > 0)
            automation.AddLine(pts.OrderBy(p => p.X).Select(p => new AnchorPoint(p.X - rel, p.Y)).ToList(), 0);
        mChanges++;
    }

    public void ClearAutomation(ScriptPart part, string id, double startTick, double endTick)
    {
        var midi = MidiOf(part);
        double rel = midi.Pos.Value;
        EnsureBracket(midi);
        if (midi.Automations.TryGetValue(id, out var automation))
            automation.Clear(startTick - rel, endTick - rel, 0);
        mChanges++;
    }

    // info: {start, end, frequency?, amplitude?}。在 [start,end) 上叠加颤音（叠加在音高曲线之上）。
    public void AddVibrato(ScriptPart part, JsValue info)
    {
        var midi = MidiOf(part);
        var o = Obj(info, "info");
        double start = ReqNum(o, "start");
        double end = ReqNum(o, "end");
        if (end <= start) throw new ScriptApiException("end must be greater than start.");
        double rel = midi.Pos.Value;
        EnsureBracket(midi);
        var vibrato = midi.CreateVibrato(new VibratoInfo
        {
            Pos = start - rel,
            Dur = end - start,
            Frequency = OptNum(o, "frequency") ?? 6,
            Amplitude = OptNum(o, "amplitude") ?? 1,
            Phase = 0,
            Attack = 0.2,
            Release = 0.2,
        });
        midi.InsertVibrato(vibrato);
        mChanges++;
    }

    // ───────────────── 速度 / 拍号写 ─────────────────

    public void SetTempo(double bpm, JsValue atTick)
    {
        if (bpm <= 0) throw new ScriptApiException("bpm must be positive.");
        double tick = AsNumOrNull(atTick) ?? 0;
        var manager = mProject.TempoManager;
        int existing = -1;
        for (int i = 0; i < manager.Tempos.Count; i++)
            if (Math.Abs(manager.Tempos[i].Pos - tick) < 0.5) { existing = i; break; }
        if (existing >= 0) manager.SetBpm(existing, bpm);
        else manager.AddTempo(tick, bpm);
        mChanges++;
    }

    public void SetTimeSignature(int numerator, int denominator, JsValue atBar)
    {
        if (numerator < 1 || denominator < 1) throw new ScriptApiException("numerator/denominator must be >= 1.");
        int barIndex = (AsIntOrNull(atBar) ?? 1) - 1;   // 1-based 小节号 → 0-based index
        if (barIndex < 0) throw new ScriptApiException("atBar must be >= 1.");
        var manager = mProject.TimeSignatureManager;
        int existing = -1;
        for (int i = 0; i < manager.TimeSignatures.Count; i++)
            if (manager.TimeSignatures[i].BarIndex == barIndex) { existing = i; break; }
        if (existing >= 0) manager.SetMeter(existing, numerator, denominator);
        else manager.AddTimeSignature(barIndex, numerator, denominator);
        mChanges++;
    }

    // ───────────────── 内部：括号 / 解析 / 句柄 ─────────────────

    void EnsureBracket(IMidiPart midi)
    {
        if (mBracketed.Add(midi))
        {
            midi.BeginMergeDirty();
            midi.Notes.BeginMergeNotify();
        }
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

    ITrack TrackOf(ScriptTrack track)
    {
        if (track == null) throw new ScriptApiException("expected a track handle (from tl.tracks()/tl.addTrack()).");
        if (track.Removed) throw new ScriptApiException("this track handle was removed and is no longer valid.");
        return track.Track;
    }

    IPart PartOf(ScriptPart part)
    {
        if (part == null) throw new ScriptApiException("expected a part handle (from tl.parts()/tl.addPart()/tl.currentPart()).");
        if (part.Removed) throw new ScriptApiException("this part handle was removed and is no longer valid.");
        return part.Part;
    }

    IMidiPart MidiOf(ScriptPart part)
    {
        if (PartOf(part) is not IMidiPart midi)
            throw new ScriptApiException("this part is not a MIDI part; notes/curves require a MIDI part.");
        return midi;
    }

    INote NoteOf(ScriptNote note)
    {
        if (note == null) throw new ScriptApiException("expected a note handle (from tl.notes()/tl.addNote()).");
        if (note.Removed) throw new ScriptApiException("this note handle was removed and is no longer valid.");
        return note.Note;
    }

    ScriptNote WrapNote(INote note) => mNotes.TryGetValue(note, out var h) ? h : mNotes[note] = new ScriptNote(note);
    ScriptPart WrapPart(IPart part) => mParts.TryGetValue(part, out var h) ? h : mParts[part] = new ScriptPart(part);
    ScriptTrack WrapTrack(ITrack track) => mTracks.TryGetValue(track, out var h) ? h : mTracks[track] = new ScriptTrack(track);

    // ───────────────── 内部：JsValue 选项袋解析 ─────────────────

    static ObjectInstance Obj(JsValue v, string what)
    {
        if (v is null || !v.IsObject())
            throw new ScriptApiException(string.Format("{0} must be an object literal.", what));
        return v.AsObject();
    }

    static bool Has(ObjectInstance o, string name, out JsValue v)
    {
        v = o.Get(name);
        return !v.IsUndefined() && !v.IsNull();
    }

    static double ReqNum(ObjectInstance o, string name)
    {
        if (!Has(o, name, out var v) || !v.IsNumber())
            throw new ScriptApiException(string.Format("field \"{0}\" must be a number.", name));
        return v.AsNumber();
    }

    static int ReqInt(ObjectInstance o, string name) => (int)Math.Round(ReqNum(o, name));
    static double? OptNum(ObjectInstance o, string name) => Has(o, name, out var v) && v.IsNumber() ? v.AsNumber() : null;
    static int? OptInt(ObjectInstance o, string name) => OptNum(o, name) is { } d ? (int)Math.Round(d) : null;
    static bool? OptBool(ObjectInstance o, string name) => Has(o, name, out var v) && v.IsBoolean() ? v.AsBoolean() : null;

    static string? OptStr(ObjectInstance o, string name)
    {
        if (!Has(o, name, out var v)) return null;
        return v.IsString() ? v.AsString() : v.ToString();
    }

    static string? AsStrOrNull(JsValue v) => v is null || v.IsUndefined() || v.IsNull() ? null : (v.IsString() ? v.AsString() : v.ToString());
    static double? AsNumOrNull(JsValue v) => v is not null && v.IsNumber() ? v.AsNumber() : null;
    static int? AsIntOrNull(JsValue v) => AsNumOrNull(v) is { } d ? (int)Math.Round(d) : null;

    List<Point> ReadPoints(JsValue points)
    {
        var o = Obj(points, "points");
        var lenVal = o.Get("length");
        if (!lenVal.IsNumber())
            throw new ScriptApiException("points must be an array of {tick, value}.");
        int len = (int)lenVal.AsNumber();
        var list = new List<Point>(len);
        for (int i = 0; i < len; i++)
        {
            var p = Obj(o.Get(i.ToString(CultureInfo.InvariantCulture)), "point");
            list.Add(new Point(ReqNum(p, "tick"), ReqNum(p, "value")));
        }
        return list;
    }
}
