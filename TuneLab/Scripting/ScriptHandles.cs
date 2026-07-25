using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jint;
using Jint.Native;
using TuneLab.Data;
using TuneLab.Extensions.Effect;
using TuneLab.Extensions.Instruments;
using TuneLab.Extensions.Voices;
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
    // 发音覆盖（voice 专属）：非空则强制该 note 用此发音（G2P 输入），空串 = 回到按歌词自动派生。
    public string Pronunciation { get => N.Pronunciation.Value; set => Apply(pronunciation: value ?? string.Empty); }
    public string PitchName => MusicTheory.PitchName(N.Pitch.Value);                 // 只读，如 "C4"

    // 批量原子改：{pos?, dur?, pitch?, lyric?, pronunciation?}（改 pos/dur 只重排一次）。
    public void Set(JsValue props)
    {
        var o = ScriptArgs.Obj(props, "props");
        Apply(
            pitch: ScriptArgs.OptInt(o, "pitch"),
            absPos: ScriptArgs.OptNum(o, "pos"),
            dur: ScriptArgs.OptNum(o, "dur"),
            lyric: ScriptArgs.OptStr(o, "lyric"),
            pronunciation: ScriptArgs.OptStr(o, "pronunciation"));
    }

    // 单字段属性 setter 与 Set() 共用：改 pos/dur 经 MoveNote 摘除-重插维持有序（通知合并由数据层收口）。
    void Apply(int? pitch = null, double? absPos = null, double? dur = null, string? lyric = null, string? pronunciation = null)
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
            if (pronunciation != null) n.Pronunciation.Set(pronunciation);
        });
        ctx.Bump();
    }

    // ── note 级自定义属性（voice/instrument 声明的 per-note 参数；schema 见 list_sound_sources 的 note 级 config） ──
    // 值存在 note.Properties 容器里、只在音源声明了该键时有意义。与 effect 的 getProperty/setProperty 同范式。

    // 读一个 note 属性当前值（number / boolean / string）；未设返回 null（默认值 / 可用键见 list_sound_sources 的 note schema）。
    public object? GetProperty(string key) => ScriptArgs.ReadScalarProperty(N.Properties, key);

    // 写一个 note 属性（值须是 number / boolean / string）。键 / 取值范围见 list_sound_sources 的 note schema。
    public void SetProperty(string key, JsValue value)
    {
        if (string.IsNullOrEmpty(key)) throw new ScriptApiException("note property key is required.");
        var pv = ScriptArgs.ToScalarProperty(value, "note property");
        ctx.EnsureBracket(N.Part);
        N.Properties.SetValue(key, pv);
        ctx.Bump();
    }

    // ── 音素（voice 专属；引导 / 主体双列表 + BodyOffset） ──
    // 未编辑过的 note 音素是【引擎回填的合成产物】（只读、合成后才有）；一旦编辑即【钉死】成用户数据（可增删改）。
    // 脚本侧：读随时可用（钉死读真数据、否则读合成快照）；写自动先钉死（物化合成产物为可编辑数据，与侧栏面板首次
    // 编辑一致）——故 agent 直接改音素即可、无需手动 pin。音素句柄按【位置】(引导/主体 + 列表内下标) 定址、跨钉死稳定，
    // 但增删会改变其后下标：结构变更后请重新 note.phonemes()。

    // 是否已钉死（有用户固定的音素数据）；false = 用合成产物（G2P 派生）。
    public bool HasPinnedPhonemes => N.HasPinnedPhonemes;

    // 引导 / 主体结合线相对 note 头的有符号偏移（秒；junction = noteStart + bodyOffset）。写自动钉死。
    public double BodyOffset { get => N.BodyOffset.Value; set { EnsurePinnedForEdit(); N.BodyOffset.Set(value); ctx.Bump(); } }

    // 全序列音素句柄（引导 ++ 主体，时间序）。合成 / 钉死态皆可读；无音素（未合成的空 note）返回空数组。
    public ScriptPhoneme[] Phonemes()
    {
        var n = N;
        if (n.HasPinnedPhonemes)
            return BuildPhonemeHandles(n.LeadingPhonemes.Count, n.BodyPhonemes.Count);
        var syl = n.SynthesizedSyllable;
        return syl is null ? [] : BuildPhonemeHandles(syl.LeadingPhonemes.Count, syl.BodyPhonemes.Count);
    }

    ScriptPhoneme[] BuildPhonemeHandles(int leadingCount, int bodyCount)
    {
        var result = new ScriptPhoneme[leadingCount + bodyCount];
        for (int i = 0; i < leadingCount; i++) result[i] = new ScriptPhoneme(ctx, this, true, i);
        for (int i = 0; i < bodyCount; i++) result[leadingCount + i] = new ScriptPhoneme(ctx, this, false, i);
        return result;
    }

    // 显式把合成产物固定为可编辑数据（幂等；无合成音素时 no-op）。写操作会自动调用，一般无需手动 pin。
    public void PinPhonemes() { EnsurePinnedForEdit(); ctx.Bump(); }

    // 清除钉死音素、回到合成产物口径（清空双列表）。
    public void ClearPhonemes() { ctx.EnsureBracket(N.Part); N.ClearLockedPhonemes(); ctx.Bump(); }

    // info: {symbol, duration?(秒,默认0), stretchWeight?(默认0=刚性辅音), leading?(默认 false=主体)}。
    // 自动钉死后追加到引导 / 主体列表末，返回句柄。stretchWeight>0 = 可伸元音（时长为派生填充量、布局时忽略）。
    public ScriptPhoneme AddPhoneme(JsValue info)
    {
        var n = N;
        var o = ScriptArgs.Obj(info, "info");
        string symbol = ScriptArgs.OptStr(o, "symbol") ?? throw new ScriptApiException("field \"symbol\" is required.");
        double dur = ScriptArgs.OptNum(o, "duration") ?? 0;
        double weight = ScriptArgs.OptNum(o, "stretchWeight") ?? 0;
        bool leading = ScriptArgs.OptBool(o, "leading") ?? false;
        EnsurePinnedForEdit();
        var list = leading ? n.LeadingPhonemes : n.BodyPhonemes;
        list.Add(Phoneme.Create(new PhonemeInfo { Symbol = symbol, Duration = Math.Max(0, dur), StretchWeight = Math.Max(0, weight) }));
        ctx.Bump();
        return new ScriptPhoneme(ctx, this, leading, list.Count - 1);
    }

    public void RemovePhoneme(ScriptPhoneme phoneme)
    {
        if (phoneme == null || phoneme.Removed) throw new ScriptApiException("expected a live phoneme handle (from note.phonemes()/note.addPhoneme()).");
        var n = N;
        EnsurePinnedForEdit();
        var list = phoneme.IsLeading ? n.LeadingPhonemes : n.BodyPhonemes;
        if (phoneme.LocalIndex < 0 || phoneme.LocalIndex >= list.Count)
            throw new ScriptApiException("this phoneme is no longer present (structure changed); re-fetch note.phonemes().");
        list.RemoveAt(phoneme.LocalIndex);
        phoneme.Removed = true;
        ctx.Bump();
    }

    // 首次音素写入的钉死守卫（供本 note 音素写方法 + 音素句柄共用）：开 merge 括号 + 物化合成产物成钉死数据（幂等）。
    internal void EnsurePinnedForEdit()
    {
        var n = N;
        ctx.EnsureBracket(n.Part);
        n.LockPhonemes();
    }

    // 供音素句柄解析底层音素（带 removed 校验）。
    internal INote Require() => N;

    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "Note(pos={0:0}, dur={1:0}, pitch={2}/{3}, lyric=\"{4}\")",
            Pos, Dur, Pitch, PitchName, Lyric);
}

// 一个音素句柄（挂在某 note 的引导 / 主体列表上）。按【位置】(引导/主体 + 列表内下标) 定址，跨钉死稳定；
// 结构增删（addPhoneme/removePhoneme）会改变其后音素下标，变更后请重新 note.phonemes()。
// 读随时可用（钉死→真数据 / 合成→只读快照）；写自动先钉死（EnsurePinnedForEdit），故 agent 直接改即可。
internal sealed class ScriptPhoneme(ScriptContext ctx, ScriptNote note, bool isLeading, int localIndex)
{
    internal bool Removed { get; set; }
    internal bool IsLeading => isLeading;
    internal int LocalIndex => localIndex;

    // 引导（核前前置辅音）= true / 主体（核 + 尾辅音）= false。只读结构分类。
    public bool Leading => isLeading;
    public string Symbol { get => Read(p => p.Symbol.Value, s => s.Symbol); set => Write(symbol: value ?? string.Empty); }
    public double Duration { get => Read(p => p.Duration.Value, s => s.Duration); set => Write(duration: value); }   // 秒
    public double StretchWeight { get => Read(p => p.StretchWeight.Value, s => s.StretchWeight); set => Write(weight: value); }  // 0=刚性辅音 / >0=可伸元音

    // 钉死态的真 IPhoneme（可写）；合成态返回 null（走快照读）。越界返回 null。
    IPhoneme? PinnedPhoneme()
    {
        var n = note.Require();
        if (!n.HasPinnedPhonemes) return null;
        var list = isLeading ? n.LeadingPhonemes : n.BodyPhonemes;
        return localIndex >= 0 && localIndex < list.Count ? list[localIndex] : null;
    }

    // 合成快照（未钉死时读）。越界 / 无合成产物返回 null。
    SynthesizedPhoneme? SynthPhoneme()
    {
        var syl = note.Require().SynthesizedSyllable;
        if (syl is null) return null;
        var list = isLeading ? syl.LeadingPhonemes : syl.BodyPhonemes;
        return localIndex >= 0 && localIndex < list.Count ? list[localIndex] : null;
    }

    T Read<T>(Func<IPhoneme, T> fromPinned, Func<SynthesizedPhoneme, T> fromSynth)
    {
        if (Removed) throw new ScriptApiException("this phoneme handle was removed and is no longer valid.");
        if (PinnedPhoneme() is { } p) return fromPinned(p);
        if (SynthPhoneme() is { } s) return fromSynth(s);
        throw new ScriptApiException("this phoneme is no longer present (structure changed); re-fetch note.phonemes().");
    }

    void Write(string? symbol = null, double? duration = null, double? weight = null)
    {
        if (Removed) throw new ScriptApiException("this phoneme handle was removed and is no longer valid.");
        note.EnsurePinnedForEdit();
        var p = PinnedPhoneme() ?? throw new ScriptApiException("this phoneme is no longer present (structure changed); re-fetch note.phonemes().");
        if (symbol != null) p.Symbol.Set(symbol);
        if (duration is { } d) p.Duration.Set(Math.Max(0, d));
        if (weight is { } w) p.StretchWeight.Set(Math.Max(0, w));
        ctx.Bump();
    }

    // 音素级自定义属性（voice 声明的 per-phoneme 参数；schema 见 list_sound_sources 的音素 slot config）。
    // 读：未钉死 / 无属性容器返回 null（属性只在钉死后作为可编辑数据存在）。写：自动钉死。
    public object? GetProperty(string key)
    {
        var p = PinnedPhoneme();
        return p == null || !p.HasProperties ? null : ScriptArgs.ReadScalarProperty(p.Properties, key);
    }

    public void SetProperty(string key, JsValue value)
    {
        if (Removed) throw new ScriptApiException("this phoneme handle was removed and is no longer valid.");
        if (string.IsNullOrEmpty(key)) throw new ScriptApiException("phoneme property key is required.");
        var pv = ScriptArgs.ToScalarProperty(value, "phoneme property");
        note.EnsurePinnedForEdit();
        var p = PinnedPhoneme() ?? throw new ScriptApiException("this phoneme is no longer present (structure changed); re-fetch note.phonemes().");
        p.Properties.SetValue(key, pv);
        ctx.Bump();
    }

    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "Phoneme(\"{0}\", {1}, dur={2:0.###}s, weight={3:0.##})",
            Read(p => p.Symbol.Value, s => s.Symbol), isLeading ? "leading" : "body",
            Read(p => p.Duration.Value, s => s.Duration), Read(p => p.StretchWeight.Value, s => s.StretchWeight));
}

// 一个 part 句柄（midi 或 audio）。音符/曲线/颤音只对 midi part 有效。
internal sealed class ScriptPart(ScriptContext ctx, IPart part)
{
    internal IPart Part { get; } = part;
    internal bool Removed { get; set; }

    IPart P => Removed ? throw new ScriptApiException("this part handle was removed and is no longer valid.") : Part;
    IMidiPart Midi => P is IMidiPart m ? m : throw new ScriptApiException("this part is not a MIDI part; notes/curves require a MIDI part.");

    public string Name { get => P.Name.Value; set => Apply(name: value); }
    // 只暴露 part 的真实几何（可见窗口的绝对 tick 起止），不暴露内部锚点/偏移三元组。
    public double StartPos { get => P.StartPos(); set => Apply(startPos: value); }   // 绝对 tick（可见起点）
    public double EndPos { get => P.EndPos(); set => Apply(endPos: value); }         // 绝对 tick（可见终点）
    public string Type => P is IMidiPart ? "midi" : "audio";

    // 本 part 的声源信息（只读快照）。仅 midi part。kind 区分 voice / instrument。
    public ScriptSoundSource SoundSource()
    {
        var v = Midi.SoundSource;
        return new ScriptSoundSource(v.Type, v.ID, v.Name, v.DefaultLyric, v.Kind == SourceKind.Voice ? "voice" : "instrument");
    }

    // 切换本 part 的音源（写；重建合成管线）。info = {kind:"voice"|"instrument"(默认 voice), type, id}——
    // type/id 取自 list_sound_sources / sandbox.voices()。未知音源【报错】而非静默回退空源（诉求：显式而非假成功）；
    // 允许 type/id 皆空以清成「空声源」（无音源 part）。与只读 soundSource() 对偶。
    public void SetSoundSource(JsValue info)
    {
        var midi = Midi;
        var o = ScriptArgs.Obj(info, "info");
        string kindStr = ScriptArgs.OptStr(o, "kind") ?? "voice";
        string type = ScriptArgs.OptStr(o, "type") ?? string.Empty;
        string id = ScriptArgs.OptStr(o, "id") ?? string.Empty;

        SourceKind kind;
        if (string.Equals(kindStr, "voice", StringComparison.OrdinalIgnoreCase)) kind = SourceKind.Voice;
        else if (string.Equals(kindStr, "instrument", StringComparison.OrdinalIgnoreCase)) kind = SourceKind.Instrument;
        else throw new ScriptApiException("kind must be \"voice\" or \"instrument\".");

        // 非空音源校验存在（空 = 清成空声源，合法、跳过校验）。校验会惰性 Init 该引擎。
        if (!(string.IsNullOrEmpty(type) && string.IsNullOrEmpty(id)))
        {
            bool exists = kind == SourceKind.Voice
                ? VoicesManager.TryGetVoiceInfo(type, id, out _)
                : InstrumentsManager.TryGetInstrumentInfo(type, id, out _);
            if (!exists)
                throw new ScriptApiException(string.Format("no {0} source with type=\"{1}\" id=\"{2}\"; use list_sound_sources (or sandbox.voices()) to find valid type/id.", kindStr, type, id));
        }

        ctx.EnsureWritable();
        midi.SoundSource.SetInfo(new SoundSourceInfo { Kind = kind, Type = type, Id = id });
        ctx.Bump();
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
    public void SetAutomation(string id, double startTick, double endTick, JsValue points, JsValue? defaultValue = null)
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

    // 等距采样 tick 序列（part 相对），供 samplePitch/sampleAutomation 及 effect 自动化采样共用。
    internal static double[] SampleTicks(IMidiPart midi, double startTick, double endTick, int samples)
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

    // ── 效果器链（串行处理链，挂在本 midi part 上；顺序即数组下标 0-based） ──

    // 本 part 的效果器链（按处理顺序）。
    public ScriptEffect[] Effects() => Midi.Effects.Select(ctx.WrapEffect).ToArray();

    // 在链尾追加一个指定类型的效果器（type 取自 list_effects 的引擎 type id）。未知类型报错。
    public ScriptEffect AddEffect(string type)
    {
        var midi = Midi;
        if (string.IsNullOrEmpty(type))
            throw new ScriptApiException("effect type is required (an engine type id from list_effects).");
        if (!EffectManager.GetAllEffectEngines().Contains(type))
            throw new ScriptApiException(string.Format("no effect engine with type \"{0}\"; use list_effects to find valid type ids.", type));
        ctx.EnsureWritable();
        var effect = midi.CreateEffect(new EffectInfo { Type = type });
        midi.InsertEffect(midi.Effects.Count, effect);
        ctx.Bump();
        return ctx.WrapEffect(effect);
    }

    public void RemoveEffect(ScriptEffect effect)
    {
        if (effect == null || effect.Removed) throw new ScriptApiException("expected a live effect handle (from part.effects()/part.addEffect()).");
        var midi = Midi;
        ctx.EnsureWritable();
        midi.RemoveEffect(effect.Effect);
        effect.Removed = true;
        ctx.Bump();
    }

    // 把某效果器移到链中的 index 位（0-based；越界钳到 [0, count-1]）——摘除重插维持串行顺序。
    public void MoveEffect(ScriptEffect effect, int index)
    {
        if (effect == null || effect.Removed) throw new ScriptApiException("expected a live effect handle (from part.effects()/part.addEffect()).");
        var midi = Midi;
        ctx.EnsureWritable();
        int target = Math.Clamp(index, 0, Math.Max(0, midi.Effects.Count - 1));
        midi.RemoveEffect(effect.Effect);
        midi.InsertEffect(target, effect.Effect);
        ctx.Bump();
    }

    // ── part 级自定义属性（voice/instrument 声明的 per-part 参数；schema 见 list_sound_sources 的 part 级 config） ──
    // 值存在 part.Properties 容器里、只在音源声明了该键时有意义。与 note/effect 的 getProperty/setProperty 同范式。

    // 读一个 part 属性当前值（number / boolean / string）；未设返回 null（默认值 / 可用键见 list_sound_sources 的 part schema）。
    public object? GetProperty(string key) => ScriptArgs.ReadScalarProperty(Midi.Properties, key);

    // 写一个 part 属性（值须是 number / boolean / string）。键 / 取值范围见 list_sound_sources 的 part schema。
    public void SetProperty(string key, JsValue value)
    {
        var midi = Midi;
        if (string.IsNullOrEmpty(key)) throw new ScriptApiException("part property key is required.");
        var pv = ScriptArgs.ToScalarProperty(value, "part property");
        ctx.EnsureBracket(midi);
        midi.Properties.SetValue(key, pv);
        ctx.Bump();
    }

    // ── part 自身 ──

    public void Set(JsValue props)
    {
        var o = ScriptArgs.Obj(props, "props");
        Apply(ScriptArgs.OptStr(o, "name"), ScriptArgs.OptNum(o, "startPos"), ScriptArgs.OptNum(o, "endPos"));
    }

    // 单字段属性 setter 与 Set() 共用：改 startPos/endPos 经 MovePart 摘除-重插维持轨内有序。
    // startPos = 移动整段（平移锚点、内容跟随、长度不变）；endPos = 缩放右边缘（移末端、内容不动）。
    void Apply(string? name = null, double? startPos = null, double? endPos = null)
    {
        var p = P;
        if (startPos is { } vs && vs < 0) throw new ScriptApiException("startPos must be >= 0.");
        if (endPos is { } ve && ve <= (startPos ?? p.StartPos())) throw new ScriptApiException("endPos must be greater than startPos.");
        ctx.EnsureWritable();
        p.Track.MovePart(p, () =>
        {
            if (name != null) p.Name.Set(name);
            // 先移动（若给了 startPos）再缩放右边缘（endPos 用移动后的锚点换算）。
            if (startPos is { } s) p.Pos.Set(p.Pos.Value + (s - p.StartPos()));
            if (endPos is { } e) p.EndOffset.Set(e - p.Pos.Value);
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
            P.Name.Value, Type, StartPos, EndPos);
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

    // info: {startPos, endPos, name?}。在本轨新建空 midi part（绝对 tick 的可见起止）。
    public ScriptPart AddPart(JsValue info)
    {
        var t = T;
        var o = ScriptArgs.Obj(info, "info");
        double startPos = ScriptArgs.ReqNum(o, "startPos");
        double endPos = ScriptArgs.ReqNum(o, "endPos");
        if (startPos < 0) throw new ScriptApiException("startPos must be >= 0.");
        if (endPos <= startPos) throw new ScriptApiException("endPos must be greater than startPos.");
        ctx.EnsureWritable();
        // 新建 part 锚点落在起点、无前向裁剪（StartOffset=0），可见窗口 = [startPos, endPos]。
        var part = t.CreatePart(new MidiPartInfo { Name = ScriptArgs.OptStr(o, "name") ?? "Part", Pos = startPos, EndOffset = endPos - startPos });
        t.InsertPart(part);
        ctx.Bump();
        return ctx.WrapPart(part);
    }

    public void RemovePart(ScriptPart part)
    {
        if (part == null || part.Removed) throw new ScriptApiException("expected a live part handle (from track.parts()/track.addPart()).");
        ctx.EnsureWritable();
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
        ctx.EnsureWritable();
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

// 一个效果器句柄（挂在 midi part 的串行效果链上）。type 不可变（换类型 = 删了重加）。
internal sealed class ScriptEffect(ScriptContext ctx, IEffect effect)
{
    internal IEffect Effect { get; } = effect;
    internal bool Removed { get; set; }

    IEffect E => Removed ? throw new ScriptApiException("this effect handle was removed and is no longer valid.") : Effect;

    public string Type => E.Type;                                  // 引擎 type id（不可变）
    public string Name => EffectManager.GetDisplayName(E.Type);    // 显示名（只读）
    public string Id => E.Id;                                      // 实例稳定 id（本 part 链内唯一）
    public int Index                                              // 在链中的 0-based 位置
    {
        get
        {
            var list = E.Part.Effects;
            for (int i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], E)) return i;
            return -1;
        }
    }
    // bypass 开关：false = 旁路（不处理）。可读写标量字段。
    public bool IsEnabled
    {
        get => E.IsEnabled.Value;
        set { ctx.EnsureWritable(); E.IsEnabled.Set(value); ctx.Bump(); }
    }

    // 读一个参数的当前值（number / boolean / string）；未设返回 null（默认值与可用键见 list_effects 的参数 schema）。
    public object? GetProperty(string key) => ScriptArgs.ReadScalarProperty(E.Properties, key);

    // 写一个参数（值须是 number / boolean / string）。键/取值范围见 list_effects。
    public void SetProperty(string key, JsValue value)
    {
        if (string.IsNullOrEmpty(key)) throw new ScriptApiException("effect property key is required.");
        var pv = ScriptArgs.ToScalarProperty(value, "effect property");
        ctx.EnsureWritable();
        E.Properties.SetValue(key, pv);
        ctx.Bump();
    }

    // ── 本 effect 的参数自动化曲线（对齐 C# IEffect.Automations，与 part 级 automation 逐一平行；曲线在 part 相对
    // tick 空间，读写口径同 part.sampleAutomation/setAutomation）。可编辑轨 id 由引擎声明（见 list_effects 的参数 schema）。 ──

    public string[] AutomationIds() => E.AutomationConfigs.Keys.Select(k => k.Id).ToArray();

    // 在绝对 tick 区间 [startTick, endTick] 上等距采样本 effect 某自动化曲线。NaN = 该处无曲线。
    public double[] SampleAutomation(string id, double startTick, double endTick, int samples)
    {
        var effect = E;
        if (!effect.AutomationConfigs.ContainsKey(id))
            throw new ScriptApiException(string.Format("unknown effect automation \"{0}\"; use one of effect.automationIds().", id));
        return effect.GetAutomationValues(ScriptPart.SampleTicks(effect.Part, startTick, endTick, samples), id);
    }

    // 覆盖写本 effect 某自动化曲线：清空 [startTick,endTick) 再落线。points=[{tick,value}]，value=参数绝对值；轨不存在按需创建，defaultValue 可选。
    public void SetAutomation(string id, double startTick, double endTick, JsValue points, JsValue? defaultValue = null)
    {
        var effect = E;
        double rel = effect.Part.Pos.Value;
        var pts = ScriptArgs.ReadPoints(points);
        ctx.EnsureBracket(effect.Part);
        var automation = GetOrAddAutomation(effect, id);
        if (ScriptArgs.AsNumOrNull(defaultValue) is { } dv) automation.DefaultValue.Set(dv);
        automation.Clear(startTick - rel, endTick - rel, 0);
        if (pts.Count > 0)
            automation.AddLine(pts.OrderBy(p => p.X).Select(p => new AnchorPoint(p.X - rel, p.Y)).ToList(), 0);
        ctx.Bump();
    }

    public void ClearAutomation(string id, double startTick, double endTick)
    {
        var effect = E;
        double rel = effect.Part.Pos.Value;
        ctx.EnsureBracket(effect.Part);
        if (effect.Automations.TryGetValue(id, out var automation))
            automation.Clear(startTick - rel, endTick - rel, 0);
        ctx.Bump();
    }

    static IAutomation GetOrAddAutomation(IEffect effect, string id)
    {
        if (effect.Automations.TryGetValue(id, out var existing))
            return existing;
        var created = effect.AddAutomation(id);
        if (created == null)
            throw new ScriptApiException(string.Format("automation \"{0}\" is not available on this effect (not declared by its engine).", id));
        return created;
    }

    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "Effect(\"{0}\", type={1}, index={2}, enabled={3})", Name, Type, Index, IsEnabled);
}

// 一个 part 的声源信息（只读快照）。kind = "voice" | "instrument"。
internal sealed class ScriptSoundSource(string type, string id, string name, string defaultLyric, string kind)
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

// 编排区范围选区（DAW 式 tick×轨道矩形，编辑器态、不入工程）的只读快照。轨道号 1-based、连续区间，start≤end。
internal sealed class ScriptSelection(double startTick, double endTick, int startTrackNumber, int endTrackNumber)
{
    public double StartTick { get; } = startTick;
    public double EndTick { get; } = endTick;
    public int StartTrackNumber { get; } = startTrackNumber;   // 1-based
    public int EndTrackNumber { get; } = endTrackNumber;       // 1-based
    public override string ToString() => string.Format(CultureInfo.InvariantCulture, "Selection(ticks {0:0}..{1:0}, tracks {2}..{3})", StartTick, EndTick, StartTrackNumber, EndTrackNumber);
}

// 钢琴窗范围选区（DAW 式 tick 带，限当前 part、贯穿全音高，编辑器态、不入工程）的只读快照。
// 与编排区 ScriptSelection 正交且独立并存——它只有时间维（无轨道、无音高），脚本据其 tick 跨度批量处理当前 part 里落在区间内的东西。
internal sealed class ScriptPianoSelection(double startTick, double endTick)
{
    public double StartTick { get; } = startTick;
    public double EndTick { get; } = endTick;
    public override string ToString() => string.Format(CultureInfo.InvariantCulture, "PianoSelection(ticks {0:0}..{1:0})", StartTick, EndTick);
}

