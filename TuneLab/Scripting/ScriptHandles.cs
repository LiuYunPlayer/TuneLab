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

// 脚本面向的「句柄」（对象式 API 的核心）：每个句柄包装一个数据层对象（轨/part/note/vibrato/effect），向脚本暴露
//  · 可读写的【标量字段】（裸属性，如 note.pos / note.pitch）——读即实时读底层，写经宿主收口；
//  · 查询/创建/删除/计算的【方法】（带括号，如 part.notes() / track.addPart() / part.removeNote()）。
// 心智模型：裸属性 = 单个标量字段；带括号 = 一次查询或动作。
//
// 增删一律【挂父】：父建子（project.addTrack / track.addPart / part.addNote / part.addVibrato）、父删子
// （project.removeTrack / track.removePart / part.removeNote / part.removeVibrato）——对 LLM 对称、
// 不会照"增在父"脑补出不存在的 API。句柄持有回宿主（ScriptContext）的引用，写操作经它统一收口（merge 括号 +
// 改动计数 + 整段脚本末尾一次 Commit）；句柄本身不 Commit。句柄是【临时】的：仅当次运行有效、不可写死、不跨次
// 运行（数据层对象无持久 id，重启即失效）。
//
// ── 三段式与两条落地路 ──
// 数据层的创建一律：Info（纯数据，改它不进撤销栈）→ CreateX(info)（建游离实体）→ InsertX(entity)（入树，
// 这一步才进回退栈）。脚本面据此给出两条【语义不同、都必需】的路：
//   · addX(info[, index])   = 复制 / 新建（新身份），中间物是纯数据 info，可落地任意多次；
//   · insertX(entity[, i])  = 移动（保持对象身份），中间物是游离实体，只能落地一次（一个对象一个父）。
// 调序 / 跨轨迁移必须走后者：note / 曲线 / effect 都挂在那个对象身上，undo 栈记录的也是它；走 info 路重建
// 出来的是另一个对象（新身份，且 remove+add 两条命令而非一次移动）。info 的读出侧是各句柄的 getInfo()。
//
// ── 句柄两态：在树上 / 游离 ──
// removeX 只是把子对象从父容器【摘出】，不销毁——句柄随之转入【游离】态：仍可读、仍可 getInfo()、仍可插回
// （保持身份）。「删除」就是「摘出后不插回」，一个机制覆盖两种用法。游离态【不可写】：数据层纪律是未 Attach
// 的对象其属性 Set 不记录命令，改了回退也回不掉（静默漂移），故写入一律在写 accessor 处拦下并指路"先插回"。
// 每个句柄因此有两个 accessor：读用的（两态都放行）与写用的（游离即报错）。
//
// 跨父迁移只对 part 成立（IPart.Track 可改）；note / vibrato / effect 的所属 part 在数据层由构造决定、
// 不可改，故它们的 insertX 只接受"插回原父"，跨父请走 info 路（other.addX(x.getInfo())）。
//
// 坐标铁律：对外位置/时长一律【绝对（全局）tick】——note/vibrato.pos 已加回所属 part 起点，落库时减回。音高 = MIDI。

// 一个音符句柄。
internal sealed class ScriptNote(ScriptContext ctx, INote note)
{
    internal INote Note { get; } = note;
    // 已从所属 part 摘出（游离）：可读可插回，不可写。
    internal bool Detached { get; set; }

    // 读 accessor（在树上 / 游离都放行）。
    INote N => Note;
    // 写 accessor（游离态拒绝，见类头「句柄两态」）。
    INote W => Detached
        ? throw new ScriptApiException("this note is detached (it was removed from its part) and can't be modified; insert it back with part.insertNote(note) first, or build a new one from note.getInfo().")
        : Note;

    public double Pos { get => N.GlobalStartPos(); set => Move(absPos: value); }   // 绝对 tick
    public double Dur { get => N.Dur.Value; set => Move(dur: value); }
    // 以下均【非排序键】：直接 Set，不套 MoveNote——NoteList 只按 StartPos/EndPos 排序，让非键字段白走
    // 一次摘除-重插是纯浪费（还多一条无意义的结构变更通知）。
    public int Pitch
    {
        get => N.Pitch.Value;
        set { var n = W; ctx.EnsureBracket(n.Part); n.Pitch.Set(Math.Clamp(value, MusicTheory.MIN_PITCH, MusicTheory.MAX_PITCH)); ctx.Bump(); }
    }
    public string Lyric
    {
        get => N.Lyric.Value;
        set { var n = W; ctx.EnsureBracket(n.Part); n.Lyric.Set(value ?? string.Empty); ctx.Bump(); }
    }
    // 发音覆盖（voice 专属）：非空则强制该 note 用此发音（G2P 输入），空串 = 回到按歌词自动派生。
    public string Pronunciation
    {
        get => N.Pronunciation.Value;
        set { var n = W; ctx.EnsureBracket(n.Part); n.Pronunciation.Set(value ?? string.Empty); ctx.Bump(); }
    }
    public string PitchName => MusicTheory.PitchName(N.Pitch.Value);                 // 只读，如 "C4"

    // 本 note 的完整快照（纯数据 JS 对象；改它不动工程）。喂 part.addNote(info) 即复制出一个新 note。
    public JsValue GetInfo() => ScriptInfo.ToJs(ctx.Engine, N.GetInfo(), N.Part.Pos.Value);

    // 所属 part（对齐 C# INote.Part；只读，且数据层就不可改——音符归属它被创建时的那个 part）。
    public ScriptPart Part() => ctx.WrapPart(N.Part);

    // 排序键（pos/dur）经 MoveNote 摘除-重插维持有序（通知合并由数据层收口）。
    void Move(double? absPos = null, double? dur = null)
    {
        var n = W;
        var midi = n.Part;
        if (dur is { } vd && vd <= 0) throw new ScriptApiException("dur must be positive.");
        ctx.EnsureBracket(midi);
        double? relPos = absPos is { } ap ? ap - midi.Pos.Value : null;
        midi.MoveNote(n, () =>
        {
            if (relPos is { } p) n.Pos.Set(p);
            if (dur is { } d) n.Dur.Set(d);
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
        var n = W;
        ctx.EnsureBracket(n.Part);
        n.Properties.SetValue(key, pv);
        ctx.Bump();
    }

    // ── 音素（voice 专属；引导 / 主体双列表 + BodyOffset） ──
    // 未编辑过的 note 音素是【引擎回填的合成产物】（只读、合成后才有）；一旦编辑即【固定】成用户数据（可增删改）。
    // 这与曲线侧的 lockPitch / lockAutomation 是同一个范式（引擎产物只读、用户数据可写、固定是唯一显式桥），
    // 故脚本面同用 lock 一个动词——数据层的中文注释在音素侧沿用"钉死"，指的是同一件事。
    // 脚本侧：读随时可用（固定后读真数据、否则读合成快照）；写自动先固定（物化合成产物为可编辑数据，与侧栏面板首次
    // 编辑一致）——故 agent 直接改音素即可、无需手动 lock。音素句柄按【位置】(引导/主体 + 列表内下标) 定址、跨固定稳定，
    // 但增删会改变其后下标：结构变更后请重新 note.phonemes()。

    // 是否已固定（有归用户的音素数据）；false = 用合成产物（G2P 派生）。
    public bool HasLockedPhonemes => N.HasPinnedPhonemes;

    // 引导 / 主体结合线相对 note 头的有符号偏移（秒；junction = noteStart + bodyOffset）。写自动固定。
    public double BodyOffset { get => N.BodyOffset.Value; set { EnsureLockedForEdit(); W.BodyOffset.Set(value); ctx.Bump(); } }

    // 全序列音素句柄（引导 ++ 主体，时间序）。合成 / 固定态皆可读；无音素（未合成的空 note）返回空数组。
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

    // 显式把合成产物固定为可编辑数据（幂等；无合成音素时 no-op）。写操作会自动调用，一般无需手动 lock。
    // 与 part.lockPitch / lockAutomation 同名同义，只是作用面是本 note 的音素。
    public void LockPhonemes() { EnsureLockedForEdit(); ctx.Bump(); }

    // 清除固定音素、回到合成产物口径（清空双列表）。与曲线侧的 clearPitch / clearAutomation 对位。
    public void ClearPhonemes() { var n = W; ctx.EnsureBracket(n.Part); n.ClearLockedPhonemes(); ctx.Bump(); }

    // 追加一个音素到【引导】列表末（核前前置辅音）。info: {symbol, duration?(秒), stretchWeight?, properties?}。
    // 引导 / 主体在数据层是两个独立列表，故这里是两个方法而非一个布尔参数——那个"默认进哪个列表"的假想值
    // 也随之消失（写入方必须明说往哪个容器加）。自动固定后追加，返回句柄。
    public ScriptPhoneme AddLeadingPhoneme(JsValue info) => AddPhonemeTo(true, info);

    // 追加一个音素到【主体】列表末（核 + 尾辅音）。参数同 addLeadingPhoneme。
    public ScriptPhoneme AddBodyPhoneme(JsValue info) => AddPhonemeTo(false, info);

    ScriptPhoneme AddPhonemeTo(bool leading, JsValue info)
    {
        var phonemeInfo = ScriptInfo.ReadPhonemeInfo(info);
        EnsureLockedForEdit();
        var list = leading ? W.LeadingPhonemes : W.BodyPhonemes;
        list.Add(Phoneme.Create(phonemeInfo));
        ctx.Bump();
        return new ScriptPhoneme(ctx, this, leading, list.Count - 1);
    }

    // 从所在列表里删掉一个音素。音素句柄按位置定址，故删除后其后音素下标前移——请重新 note.phonemes()。
    // 音素在数据层没有父指针（列表成员即其唯一归属、无游离态可言），跨 note 搬运走 info 路：
    // other.addBodyPhoneme(ph.getInfo()) 再 note.removePhoneme(ph)。
    public void RemovePhoneme(ScriptPhoneme phoneme)
    {
        if (phoneme == null || phoneme.Removed)
            throw new ScriptApiException("expected a live phoneme handle (from note.phonemes()/note.addLeadingPhoneme()/note.addBodyPhoneme()).");
        EnsureLockedForEdit();
        var list = phoneme.IsLeading ? W.LeadingPhonemes : W.BodyPhonemes;
        if (phoneme.LocalIndex < 0 || phoneme.LocalIndex >= list.Count)
            throw new ScriptApiException("this phoneme is no longer present (structure changed); re-fetch note.phonemes().");
        list.RemoveAt(phoneme.LocalIndex);
        phoneme.Removed = true;
        ctx.Bump();
    }

    // 首次音素写入的固定守卫（供本 note 音素写方法 + 音素句柄共用）：开 merge 括号 + 物化合成产物成固定数据（幂等）。
    internal void EnsureLockedForEdit()
    {
        var n = W;
        ctx.EnsureBracket(n.Part);
        n.LockPhonemes();
    }

    // 供音素句柄解析底层音素（读向，游离态放行）。
    internal INote Require() => N;

    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "Note(pos={0:0}, dur={1:0}, pitch={2}/{3}, lyric=\"{4}\")",
            Pos, Dur, Pitch, PitchName, Lyric);
}

// 一个音素句柄（挂在某 note 的引导 / 主体列表上）。按【位置】(引导/主体 + 列表内下标) 定址，跨固定稳定；
// 结构增删（addLeadingPhoneme/addBodyPhoneme/removePhoneme）会改变其后音素下标，变更后请重新 note.phonemes()。
// 读随时可用（固定→真数据 / 合成→只读快照）；写自动先固定（EnsureLockedForEdit），故 agent 直接改即可。
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

    // 本音素的完整快照（纯数据）。合成态（未固定）也能读——那时 properties 为 null（合成产物无属性容器）。
    public JsValue GetInfo()
    {
        if (LockedPhoneme() is { } p)
            return ScriptInfo.ToJs(ctx.Engine, p.GetInfo());
        var s = SynthPhoneme() ?? throw new ScriptApiException("this phoneme is no longer present (structure changed); re-fetch note.phonemes().");
        return ScriptInfo.ToJs(ctx.Engine, new PhonemeInfo { Symbol = s.Symbol, Duration = s.Duration, StretchWeight = s.StretchWeight });
    }

    // 固定态的真 IPhoneme（可写）；合成态返回 null（走快照读）。越界返回 null。
    IPhoneme? LockedPhoneme()
    {
        var n = note.Require();
        if (!n.HasPinnedPhonemes) return null;
        var list = isLeading ? n.LeadingPhonemes : n.BodyPhonemes;
        return localIndex >= 0 && localIndex < list.Count ? list[localIndex] : null;
    }

    // 合成快照（未固定时读）。越界 / 无合成产物返回 null。
    SynthesizedPhoneme? SynthPhoneme()
    {
        var syl = note.Require().SynthesizedSyllable;
        if (syl is null) return null;
        var list = isLeading ? syl.LeadingPhonemes : syl.BodyPhonemes;
        return localIndex >= 0 && localIndex < list.Count ? list[localIndex] : null;
    }

    T Read<T>(Func<IPhoneme, T> fromLocked, Func<SynthesizedPhoneme, T> fromSynth)
    {
        if (Removed) throw new ScriptApiException("this phoneme handle was removed and is no longer valid.");
        if (LockedPhoneme() is { } p) return fromLocked(p);
        if (SynthPhoneme() is { } s) return fromSynth(s);
        throw new ScriptApiException("this phoneme is no longer present (structure changed); re-fetch note.phonemes().");
    }

    void Write(string? symbol = null, double? duration = null, double? weight = null)
    {
        if (Removed) throw new ScriptApiException("this phoneme handle was removed and is no longer valid.");
        note.EnsureLockedForEdit();
        var p = LockedPhoneme() ?? throw new ScriptApiException("this phoneme is no longer present (structure changed); re-fetch note.phonemes().");
        if (symbol != null) p.Symbol.Set(symbol);
        if (duration is { } d) p.Duration.Set(Math.Max(0, d));
        if (weight is { } w) p.StretchWeight.Set(Math.Max(0, w));
        ctx.Bump();
    }

    // 音素级自定义属性（voice 声明的 per-phoneme 参数；schema 见 list_sound_sources 的音素 slot config）。
    // 读：未固定 / 无属性容器返回 null（属性只在固定后作为可编辑数据存在）。写：自动固定。
    public object? GetProperty(string key)
    {
        var p = LockedPhoneme();
        return p == null || !p.HasProperties ? null : ScriptArgs.ReadScalarProperty(p.Properties, key);
    }

    public void SetProperty(string key, JsValue value)
    {
        if (Removed) throw new ScriptApiException("this phoneme handle was removed and is no longer valid.");
        if (string.IsNullOrEmpty(key)) throw new ScriptApiException("phoneme property key is required.");
        var pv = ScriptArgs.ToScalarProperty(value, "phoneme property");
        note.EnsureLockedForEdit();
        var p = LockedPhoneme() ?? throw new ScriptApiException("this phoneme is no longer present (structure changed); re-fetch note.phonemes().");
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
    // 已从所属轨摘出（游离）：可读可插回（含插到【另一条轨】＝跨轨迁移），不可写。
    internal bool Detached { get; set; }

    IPart P => Part;
    IPart W => Detached
        ? throw new ScriptApiException("this part is detached (it was removed from its track) and can't be modified; insert it back with track.insertPart(part) first, or build a new one from part.getInfo().")
        : Part;
    IMidiPart Midi => P is IMidiPart m ? m : throw new ScriptApiException("this part is not a MIDI part; notes/curves require a MIDI part.");
    IMidiPart MidiW => W is IMidiPart m ? m : throw new ScriptApiException("this part is not a MIDI part; notes/curves require a MIDI part.");

    public string Name
    {
        get => P.Name.Value;
        // 【非排序键】：PartList 只按 StartPos/EndPos 排序，改名不必走 MovePart。
        set { var p = W; ctx.EnsureWritable(); p.Name.Set(value ?? string.Empty); ctx.Bump(); }
    }

    // ── part 几何：三个【原始字段】可写、三个【派生量】只读，与数据层同形 ──
    // pos = 锚点在全局时间线上的位置，同时是 part 内一切内容（note / 曲线 / 颤音）的坐标原点；
    // startOffset / endOffset = 起点 / 终点相对锚点的有符号偏移。起点 = pos + startOffset，终点 = pos + endOffset。
    // 三个原始字段各对应一个原子操作：移动整段改 pos（内容跟随、长度不变）、拖左边缘改 startOffset
    // （>0 前向裁剪 / <0 前向扩展）、拖右边缘改 endOffset。裁剪只改偏移、不重排内容。
    public double Pos { get => P.Pos.Value; set => Move(pos: value); }                     // 绝对 tick（锚点）
    public double StartOffset { get => P.StartOffset.Value; set => Move(startOffset: value); }
    public double EndOffset { get => P.EndOffset.Value; set => Move(endOffset: value); }
    public double StartPos => P.StartPos();   // 只读派生 = pos + startOffset
    public double EndPos => P.EndPos();       // 只读派生 = pos + endOffset
    public double Dur => P.Dur;               // 只读派生 = endOffset - startOffset
    public string Type => P is IMidiPart ? "midi" : "audio";

    // 本 part 的完整快照（纯数据 JS 对象，含音源 / 音符 / 曲线 / 颤音 / effect 链 / 两级属性 / 音素）。
    // 喂 track.addPart(info) 即整段复制——保真由数据层的序列化路径保证，本方法一个字段都不碰。
    public JsValue GetInfo() => ScriptInfo.ToJs(ctx.Engine, P.GetInfo());

    // 所属轨（对齐 C# IPart.Track；只读——换父要走 track.removePart + 另一轨.insertPart，直接改归属会让
    // 对象声称属于新轨却仍留在旧轨的链表里）。游离期返回它摘出前那条轨。
    public ScriptTrack Track() => ctx.WrapTrack(P.Track);

    // 排序键（pos / startOffset / endOffset 都参与 PartList 的 StartPos/EndPos 排序）经 MovePart 摘除-重插维序。
    void Move(double? pos = null, double? startOffset = null, double? endOffset = null)
    {
        var p = W;
        double nextPos = pos ?? p.Pos.Value;
        double nextStart = startOffset ?? p.StartOffset.Value;
        double nextEnd = endOffset ?? p.EndOffset.Value;
        if (nextPos + nextStart < 0) throw new ScriptApiException("a part's start (pos + startOffset) must be >= 0.");
        if (nextEnd <= nextStart) throw new ScriptApiException("a part's endOffset must be greater than its startOffset.");
        ctx.EnsureWritable();
        p.Track.MovePart(p, () =>
        {
            if (pos is { } v1) p.Pos.Set(v1);
            if (startOffset is { } v2) p.StartOffset.Set(v2);
            if (endOffset is { } v3) p.EndOffset.Set(v3);
        });
        ctx.Bump();
    }

    // 本 part 的声源信息（只读快照）。仅 midi part。kind 区分 voice / instrument。
    public ScriptSoundSource SoundSource()
    {
        var v = Midi.SoundSource;
        return new ScriptSoundSource(v.Type, v.ID, v.Name, v.DefaultLyric, v.Kind == SourceKind.Voice ? "voice" : "instrument");
    }

    // 切换本 part 的音源（写；重建合成管线）。info = {kind:"voice"|"instrument"(默认 voice), type, id}——
    // type/id 取自 list_sound_sources / sandbox.voices()。这是"按名字指定一个引擎"的显式意图，故【校验存在性】：
    // 未知音源报错而非静默回退空源（诉求：显式而非假成功）；允许 type/id 皆空以清成「空声源」（无音源 part）。
    // 注：info 路（track.addPart 里嵌套的 soundSource）刻意【不】校验——那条路要能忠实搬运孤儿数据
    // （引擎卸载后工程照样能开、复制照样保真）。与只读 soundSource() 对偶。
    public void SetSoundSource(JsValue info)
    {
        var midi = MidiW;
        var source = ScriptInfo.ReadSoundSourceInfo(info);
        if (!(string.IsNullOrEmpty(source.Type) && string.IsNullOrEmpty(source.Id)))
        {
            bool exists = source.Kind == SourceKind.Voice
                ? VoicesManager.TryGetVoiceInfo(source.Type, source.Id, out _)
                : InstrumentsManager.TryGetInstrumentInfo(source.Type, source.Id, out _);
            if (!exists)
                throw new ScriptApiException(string.Format("no {0} source with type=\"{1}\" id=\"{2}\"; use list_sound_sources (or sandbox.voices()) to find valid type/id.",
                    source.Kind == SourceKind.Voice ? "voice" : "instrument", source.Type, source.Id));
        }

        ctx.EnsureWritable();
        midi.SoundSource.SetInfo(source);
        ctx.Bump();
    }

    // part 级增益（dB，0 = 不增不减）。与轨级 gain 平行、两者叠加。
    public double Gain
    {
        get => Midi.Gain.Value;
        set { var midi = MidiW; ctx.EnsureBracket(midi); midi.Gain.Set(value); ctx.Bump(); }
    }

    // ── 音符 ──

    public ScriptNote[] Notes() => Midi.Notes.Select(ctx.WrapNote).ToArray();

    // 钢琴窗里用户当前选中的音符；无选中返回空数组。
    public ScriptNote[] SelectedNotes() => Midi.Notes.AllSelectedItems().Select(ctx.WrapNote).ToArray();

    // 按完整 note info 新建一个音符并插入（新身份，同一份 info 可落地任意多次）。
    // info: {pos, dur, pitch, lyric?, pronunciation?, properties?, leadingPhonemes?, bodyPhonemes?, bodyOffset?}，pos 绝对 tick。
    public ScriptNote AddNote(JsValue info)
    {
        var midi = MidiW;
        var noteInfo = ScriptInfo.ReadNoteInfo(info, midi.Pos.Value);
        ctx.EnsureBracket(midi);
        var note = midi.CreateNote(noteInfo);
        midi.InsertNote(note);
        ctx.Bump();
        return ctx.WrapNote(note);
    }

    // 把一个【游离】音符插回本 part（保持对象身份，与 addNote 的"新建"相对）。
    // note 的所属 part 在数据层由构造决定、不可改，故只能插回原 part；跨 part 搬运走 info 路。
    public void InsertNote(ScriptNote note)
    {
        var midi = MidiW;
        if (note == null) throw new ScriptApiException("expected a note handle.");
        if (!note.Detached) throw new ScriptApiException("this note is already on a part; remove it first (part.removeNote(note)).");
        if (!ReferenceEquals(note.Note.Part, midi))
            throw new ScriptApiException("a note belongs to the part it was created on and can't be moved to another part; use otherPart.addNote(note.getInfo()) instead.");
        ctx.EnsureBracket(midi);
        midi.InsertNote(note.Note);
        note.Detached = false;
        ctx.Bump();
    }

    // 把音符从本 part 摘出，返回它的（现在游离的）句柄：不插回 = 删除，插回 = 移动。
    public ScriptNote RemoveNote(ScriptNote note)
    {
        var midi = MidiW;
        if (note == null || note.Detached) throw new ScriptApiException("expected a live note handle (from part.notes()/part.addNote()).");
        if (!ReferenceEquals(note.Note.Part, midi)) throw new ScriptApiException("this note is not on this part.");
        ctx.EnsureBracket(midi);
        midi.RemoveNote(note.Note);
        note.Detached = true;
        ctx.Bump();
        return note;
    }

    // ── 音高曲线（pitch，独立显眼，对齐 C# midi.Pitch） ──

    // 在绝对 tick 区间 [startTick, endTick] 上等距采样最终音高曲线（MIDI 标度）。
    public double[] SamplePitch(double startTick, double endTick, int samples)
        => Midi.GetFinalPitch(SampleTicks(Midi, startTick, endTick, samples));

    // 覆盖写音高曲线：清空 [startTick,endTick) 再落线。points=[{tick,value}]，value=绝对 MIDI 音高（可含小数）。
    public void SetPitchLine(double startTick, double endTick, JsValue points)
    {
        var midi = MidiW;
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
        var midi = MidiW;
        double rel = midi.Pos.Value;
        ctx.EnsureBracket(midi);
        midi.Pitch.Clear(startTick - rel, endTick - rel);
        ctx.Bump();
    }

    // ── 自动化曲线（automation，对齐 C# midi.Automations；不含 pitch） ──

    // 可编辑的【连续】自动化轨 id 列表（音源声明，如 "Volume"；有默认基线）。分段轨（无基线、段间关断）
    // 不在此列，见 piecewiseAutomationIds()——两族曲线读写口径不同，混在一张表里会让"取到的 id 用起来报错"。
    public string[] AutomationIds()
        => Midi.SoundSource.AutomationConfigs.Where(kvp => !kvp.Value.IsPiecewise).Select(kvp => kvp.Key.Id).ToArray();

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
        var midi = MidiW;
        double rel = midi.Pos.Value;
        var pts = ScriptArgs.ReadPoints(points);
        ctx.EnsureBracket(midi);
        var automation = midi.AddAutomation(id)
            ?? throw new ScriptApiException(string.Format("automation \"{0}\" is not available on this part (not declared by its sound source); use one of part.automationIds().", id));
        if (ScriptArgs.AsNumOrNull(defaultValue) is { } dv) automation.DefaultValue.Set(dv);
        automation.Clear(startTick - rel, endTick - rel, 0);
        if (pts.Count > 0)
            automation.AddLine(pts.OrderBy(p => p.X).Select(p => new AnchorPoint(p.X - rel, p.Y)).ToList(), 0);
        ctx.Bump();
    }

    public void ClearAutomation(string id, double startTick, double endTick)
    {
        var midi = MidiW;
        double rel = midi.Pos.Value;
        ctx.EnsureBracket(midi);
        if (midi.Automations.TryGetValue(id, out var automation))
            automation.Clear(startTick - rel, endTick - rel, 0);
        ctx.Bump();
    }

    // ── 分段自动化曲线（piecewise，对齐 C# midi.PiecewiseAutomations）──
    // 与连续轨的区别：没有默认基线，段与段之间是【关断】（无值）。pitch 就是这一族里的一条专属常驻通道，
    // 故这组方法与 samplePitch/setPitchLine/clearPitch 逐一同形。

    public string[] PiecewiseAutomationIds()
        => Midi.SoundSource.AutomationConfigs.Where(kvp => kvp.Value.IsPiecewise).Select(kvp => kvp.Key.Id).ToArray();

    public double[] SamplePiecewiseAutomation(string id, double startTick, double endTick, int samples)
    {
        var midi = Midi;
        // 判据与 piecewiseAutomationIds() 同一处（音源声明里 IsPiecewise 的轨）；IMidiPart 上没有按 plain id
        // 的分段轨判定扩展，故就地查 config。
        if (!(midi.SoundSource.AutomationConfigs.TryGetValue(id, out var config) && config.IsPiecewise))
            throw new ScriptApiException(string.Format("unknown piecewise automation \"{0}\"; use one of part.piecewiseAutomationIds().", id));
        var ticks = SampleTicks(midi, startTick, endTick, samples);
        return midi.PiecewiseAutomations.TryGetValue(id, out var automation) ? automation.GetValues(ticks) : new double[ticks.Length];
    }

    public void SetPiecewiseAutomationLine(string id, double startTick, double endTick, JsValue points)
    {
        var midi = MidiW;
        double rel = midi.Pos.Value;
        var pts = ScriptArgs.ReadPoints(points);
        ctx.EnsureBracket(midi);
        var automation = midi.AddPiecewiseAutomation(id)
            ?? throw new ScriptApiException(string.Format("piecewise automation \"{0}\" is not available on this part (not declared by its sound source); use one of part.piecewiseAutomationIds().", id));
        automation.Clear(startTick - rel, endTick - rel);
        if (pts.Count > 0)
            automation.AddLine(pts.OrderBy(p => p.X).Select(p => new AnchorPoint(p.X - rel, p.Y)).ToList(), 0);
        ctx.Bump();
    }

    public void ClearPiecewiseAutomation(string id, double startTick, double endTick)
    {
        var midi = MidiW;
        double rel = midi.Pos.Value;
        ctx.EnsureBracket(midi);
        if (midi.PiecewiseAutomations.TryGetValue(id, out var automation))
            automation.Clear(startTick - rel, endTick - rel);
        ctx.Bump();
    }

    // ── 固定（lock）：把只读的合成产物写成归用户的可编辑曲线（与工具栏那支固定笔刷同一份实现，见 SynthesisLock） ──
    //
    // 引擎产物恒只读、用户编辑恒落数据层，固定是二者之间唯一的显式桥：固定后那段数据归用户、可继续改、
    // 引擎不再覆盖它。这是"抓住模型这根线只改中间一段"的落地手段——没有它，脚本在未覆盖段落笔就是从空白起步、
    // 模型细节全丢。刻意是**一次性动作**、不做持续同步（自动跟随会形成 覆盖→重合成→合成参数变→再写入 的反馈环）。
    //
    // 两个区间参数【要么都给、要么都不给】：都不给 = 整条 part（SynthesisLock 的 ±∞ 全轨口径）。只给一个
    // 无从推断另一头，故报错而不是替脚本瞎猜一个默认边界。
    //
    // 返回值是【有没有真的固定到东西】：产物为空（该段还没合成过）时是 no-op 并返回 false，而不是假装成功——
    // 脚本/agent 那边没人盯着屏幕看结果，静默 no-op 会被当成已固定。用法错误（未知 id / 无配对合成参数）另走报错。

    // 把合成音高固定进本 part 的 pitch 曲线。false = 该区间没有合成音高产物（通常是还没合成过）。
    public bool LockPitch(JsValue? startTick = null, JsValue? endTick = null)
    {
        var midi = MidiW;
        var (start, end) = ReadLockRange(startTick, endTick);
        ctx.EnsureBracket(midi);
        // 与笔刷同序：先冻结产物引用再写（写入即触发合成失效，引擎可能随即清掉产物）。
        if (!midi.WriteSynthesizedPitchLock(midi.CaptureSynthesizedPitch(), start, end))
            return false;

        ctx.Bump();
        return true;
    }

    // 把某条参数轨的【合成参数】固定进同 id 的可编辑轨（连续 / 分段轨由数据层按声明自动分派，脚本不必区分）。
    // false = 该区间没有合成参数产物（通常是还没合成过）。
    public bool LockAutomation(string id, JsValue? startTick = null, JsValue? endTick = null)
    {
        var midi = MidiW;
        var key = AutomationKey.Voice(id);
        RequirePairedSynthesizedParameter(midi, key, id, "part.automationIds() / part.piecewiseAutomationIds()");
        var (start, end) = ReadLockRange(startTick, endTick);
        ctx.EnsureBracket(midi);
        if (!midi.WriteSynthesizedParameterLock(key, midi.CaptureSynthesizedParameter(key), start, end))
            return false;

        ctx.Bump();
        return true;
    }

    // 该轨有没有【配对合成参数】——即音源除了这条可编辑轨，还发布了同 id 的合成参数。没有配对就没有模型输出可固定。
    // 供脚本先问后做（lockAutomation 对无配对轨是报错，那会让整段脚本回退）。
    public bool HasSynthesizedParameter(string id) => Midi.HasPairedSynthesizedParameter(AutomationKey.Voice(id));

    // 可选区间 → 全局 tick 对（两参同在同缺，见上）。
    internal static (double Start, double End) ReadLockRange(JsValue? startTick, JsValue? endTick)
    {
        double? start = ScriptArgs.AsNumOrNull(startTick);
        double? end = ScriptArgs.AsNumOrNull(endTick);
        if (start == null && end == null)
            return (SynthesisLock.WholeTrackStart, SynthesisLock.WholeTrackEnd);

        if (start == null || end == null)
            throw new ScriptApiException("pass BOTH startTick and endTick to lock a range, or neither to lock the whole part.");

        if (end.Value <= start.Value)
            throw new ScriptApiException("endTick must be greater than startTick.");

        return (start.Value, end.Value);
    }

    // 固定前的两道判据（part / effect 共用，只有文案里的 id 清单入口不同）：① 该 id 得是本载体上一条可编辑轨；
    // ② 引擎得声明了同 id 的合成参数轨。两者都是用法错误、报错指路，与"有轨有配对但还没合成"（返回 false）分开。
    internal static void RequirePairedSynthesizedParameter(IMidiPart midi, AutomationKey key, string id, string idsHint)
    {
        if (!midi.IsEffectiveAutomation(key) && !midi.IsEffectivePiecewiseAutomation(key))
            throw new ScriptApiException(string.Format("unknown automation \"{0}\"; use one of {1}.", id, idsHint));

        if (!midi.HasPairedSynthesizedParameter(key))
            throw new ScriptApiException(string.Format(
                "automation \"{0}\" has no paired synthesized parameter: its engine publishes no synthesized parameter with that id, so there is no model output to lock. Check hasSynthesizedParameter(\"{0}\") first.", id));
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

    // 按完整 vibrato info 新建一个颤音并插入。pos 绝对 tick；叠加在音高曲线之上。
    // info: {pos, dur, frequency?(6), amplitude?(1), phase?(0), attack?(0.2), release?(0.2),
    //        affectedAutomations?{轨id:振幅}, affectedEffectAutomations?{effect id:{轨id:振幅}}}。
    public ScriptVibrato AddVibrato(JsValue info)
    {
        var midi = MidiW;
        var vibratoInfo = ScriptInfo.ReadVibratoInfo(info, midi.Pos.Value);
        ctx.EnsureBracket(midi);
        var vibrato = midi.CreateVibrato(vibratoInfo);
        midi.InsertVibrato(vibrato);
        ctx.Bump();
        return ctx.WrapVibrato(vibrato);
    }

    // 把一个【游离】颤音插回本 part（保持身份）。同 note：颤音的所属 part 由构造决定、不可改。
    public void InsertVibrato(ScriptVibrato vibrato)
    {
        var midi = MidiW;
        if (vibrato == null) throw new ScriptApiException("expected a vibrato handle.");
        if (!vibrato.Detached) throw new ScriptApiException("this vibrato is already on a part; remove it first (part.removeVibrato(vibrato)).");
        if (!ReferenceEquals(vibrato.Vibrato.Part, midi))
            throw new ScriptApiException("a vibrato belongs to the part it was created on and can't be moved to another part; use otherPart.addVibrato(vibrato.getInfo()) instead.");
        ctx.EnsureBracket(midi);
        midi.InsertVibrato(vibrato.Vibrato);
        vibrato.Detached = false;
        ctx.Bump();
    }

    public ScriptVibrato RemoveVibrato(ScriptVibrato vibrato)
    {
        var midi = MidiW;
        if (vibrato == null || vibrato.Detached) throw new ScriptApiException("expected a live vibrato handle (from part.vibratos()/part.addVibrato()).");
        if (!ReferenceEquals(vibrato.Vibrato.Part, midi)) throw new ScriptApiException("this vibrato is not on this part.");
        ctx.EnsureBracket(midi);
        midi.RemoveVibrato(vibrato.Vibrato);
        vibrato.Detached = true;
        ctx.Bump();
        return vibrato;
    }

    // ── 效果器链（串行处理链，挂在本 midi part 上；顺序即数组下标 0-based） ──

    // 本 part 的效果器链（按处理顺序）。
    public ScriptEffect[] Effects() => Midi.Effects.Select(ctx.WrapEffect).ToArray();

    // 按完整 effect info 新建一个效果器并插入链中 index 位（省略 index = 链尾；越界钳到合法范围）。
    // info: {type, isEnabled?, properties?, automations?, piecewiseAutomations?, id?}。
    // type 是"按名字指定一个引擎"的显式意图，故【校验存在性】（未知类型报错）；嵌套在 part info 里的
    // effects 走 info 路、不校验（要能忠实搬运引擎已卸载的孤儿数据）。
    public ScriptEffect AddEffect(JsValue info, JsValue? index = null)
    {
        var midi = MidiW;
        var effectInfo = ScriptInfo.ReadEffectInfo(info, midi.Pos.Value);
        if (string.IsNullOrEmpty(effectInfo.Type))
            throw new ScriptApiException("effect info field \"type\" is required (an engine type id from list_effects).");
        if (!EffectManager.GetAllEffectEngines().Contains(effectInfo.Type))
            throw new ScriptApiException(string.Format("no effect engine with type \"{0}\"; use list_effects to find valid type ids.", effectInfo.Type));
        // id 在【本 part 链内】必须唯一（颤音影响表按它做外键）。复制同一条链里的 effect 会带来重复 id，
        // 清空即让宿主发新号——与 EffectInfo.Id 的约定一致（"克隆进同一条链须显式清空"）。
        if (!string.IsNullOrEmpty(effectInfo.Id) && midi.Effects.Any(e => e.Id == effectInfo.Id))
            effectInfo.Id = string.Empty;
        ctx.EnsureWritable();
        var effect = midi.CreateEffect(effectInfo);
        midi.InsertEffect(ClampEffectIndex(midi, index), effect);
        ctx.Bump();
        return ctx.WrapEffect(effect);
    }

    // 把一个【游离】效果器插回链中 index 位（保持身份，故其自动化曲线与颤音影响表的引用都还连着）。
    public void InsertEffect(ScriptEffect effect, JsValue? index = null)
    {
        var midi = MidiW;
        if (effect == null) throw new ScriptApiException("expected an effect handle.");
        if (!effect.Detached) throw new ScriptApiException("this effect is already on a chain; remove it first (part.removeEffect(effect)).");
        if (!ReferenceEquals(effect.Effect.Part, midi))
            throw new ScriptApiException("an effect belongs to the part it was created on and can't be moved to another part; use otherPart.addEffect(effect.getInfo()) instead.");
        ctx.EnsureWritable();
        midi.InsertEffect(ClampEffectIndex(midi, index), effect.Effect);
        effect.Detached = false;
        ctx.Bump();
    }

    public ScriptEffect RemoveEffect(ScriptEffect effect)
    {
        var midi = MidiW;
        if (effect == null || effect.Detached) throw new ScriptApiException("expected a live effect handle (from part.effects()/part.addEffect()).");
        if (!ReferenceEquals(effect.Effect.Part, midi)) throw new ScriptApiException("this effect is not on this part.");
        ctx.EnsureWritable();
        midi.RemoveEffect(effect.Effect);
        effect.Detached = true;
        ctx.Bump();
        return effect;
    }

    // 把某效果器移到链中的 index 位（0-based；越界钳到 [0, count-1]）——摘除重插维持串行顺序。
    // C# 侧无 MoveEffect，但这属"移动同一个对象"（EffectInfo.Id 是实例稳定身份，remove + add(info) 会换身份），
    // 故封装保留：一步完成、不让脚本自己拼摘除重插。
    public void MoveEffect(ScriptEffect effect, int index)
    {
        var midi = MidiW;
        if (effect == null || effect.Detached) throw new ScriptApiException("expected a live effect handle (from part.effects()/part.addEffect()).");
        if (!ReferenceEquals(effect.Effect.Part, midi)) throw new ScriptApiException("this effect is not on this part.");
        ctx.EnsureWritable();
        int target = Math.Clamp(index, 0, Math.Max(0, midi.Effects.Count - 1));
        midi.RemoveEffect(effect.Effect);
        midi.InsertEffect(target, effect.Effect);
        ctx.Bump();
    }

    // 省略 index = 追加到链尾；给了就钳到 [0, count]（count = 追加）。
    static int ClampEffectIndex(IMidiPart midi, JsValue? index)
        => ScriptArgs.AsIntOrNull(index) is { } i ? Math.Clamp(i, 0, midi.Effects.Count) : midi.Effects.Count;

    // ── part 级自定义属性（voice/instrument 声明的 per-part 参数；schema 见 list_sound_sources 的 part 级 config） ──
    // 值存在 part.Properties 容器里、只在音源声明了该键时有意义。与 note/effect 的 getProperty/setProperty 同范式。

    // 读一个 part 属性当前值（number / boolean / string）；未设返回 null（默认值 / 可用键见 list_sound_sources 的 part schema）。
    public object? GetProperty(string key) => ScriptArgs.ReadScalarProperty(Midi.Properties, key);

    // 写一个 part 属性（值须是 number / boolean / string）。键 / 取值范围见 list_sound_sources 的 part schema。
    public void SetProperty(string key, JsValue value)
    {
        if (string.IsNullOrEmpty(key)) throw new ScriptApiException("part property key is required.");
        var pv = ScriptArgs.ToScalarProperty(value, "part property");
        var midi = MidiW;
        ctx.EnsureBracket(midi);
        midi.Properties.SetValue(key, pv);
        ctx.Bump();
    }

    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "Part(\"{0}\", {1}, ticks [{2:0}..{3:0}])",
            P.Name.Value, Type, StartPos, EndPos);
}

// 一个轨道句柄。
internal sealed class ScriptTrack(ScriptContext ctx, ITrack track)
{
    internal ITrack Track { get; } = track;
    // 已从工程摘出（游离）：可读可插回，不可写。
    internal bool Detached { get; set; }

    ITrack T => Track;
    ITrack W => Detached
        ? throw new ScriptApiException("this track is detached (it was removed from the project) and can't be modified; insert it back with project.insertTrack(track) first, or build a new one from track.getInfo().")
        : Track;

    public string Name { get => T.Name.Value; set => Set(t => t.Name.Set(value ?? string.Empty)); }
    public bool IsMute { get => T.IsMute.Value; set => Set(t => t.IsMute.Set(value)); }
    public bool IsSolo { get => T.IsSolo.Value; set => Set(t => t.IsSolo.Set(value)); }
    public double Gain { get => T.Gain.Value; set => Set(t => t.Gain.Set(value)); }   // dB（0 = 原始电平）
    public double Pan { get => T.Pan.Value; set => Set(t => t.Pan.Set(Math.Clamp(value, -1, 1))); }
    // 是否可被其它音源当作参考音轨（合成时"听见"这条轨）。
    public bool AsRefer { get => T.AsRefer.Value; set => Set(t => t.AsRefer.Set(value)); }
    // 轨色（十六进制串，如 "#FF8800"；空串 = 用主题默认色）。
    public string Color { get => T.Color.Value; set => Set(t => t.Color.Set(value ?? string.Empty)); }

    // ── 逐轨导出设置（与工程级的 project.exportPath 等同族） ──
    // 【设置项、不入撤销栈】：与导出侧栏里勾选一致，改完按 Ctrl+Z 不会退回；但脚本出错 / preview 会还原
    // （ScriptContext 写前留底）。故它们也【不在】track.getInfo() 里——复制一条轨不带导出开关。
    public bool ExportEnabled { get => T.ExportEnabled; set => SetExport(t => t.ExportEnabled = value); }
    public int ExportChannels { get => T.ExportChannels; set => SetExport(t => t.ExportChannels = RequireChannels(value, "exportChannels")); }

    void SetExport(Action<ITrack> mutate)
    {
        var t = W;
        ctx.EnsureWritable();
        ctx.CaptureTrackExport(t);   // 首次写入时留底，供出错 / preview 还原
        mutate(t);
        ctx.Bump();
    }

    internal static int RequireChannels(int value, string what)
        => value is 1 or 2 ? value : throw new ScriptApiException(string.Format("{0} must be 1 (mono) or 2 (stereo).", what));

    // 轨没有排序键（project.tracks() 是按下标的有序表，位置由 insertTrack 的 index 决定），故所有字段直接 Set。
    void Set(Action<ITrack> mutate)
    {
        var t = W;
        ctx.EnsureWritable();
        mutate(t);
        ctx.Bump();
    }

    // 本轨的完整快照（纯数据 JS 对象，含全部 part 及各 part 的所有维度）。喂 project.addTrack(info) 即整轨复制。
    public JsValue GetInfo() => ScriptInfo.ToJs(ctx.Engine, T.GetInfo());

    public ScriptPart[] Parts() => T.Parts.Select(ctx.WrapPart).ToArray();

    // 按完整 part info 在本轨新建一个 part 并插入（新身份，同一份 info 可落地任意多次）。
    // info: {type?("midi"|"audio")，name?, pos?, startOffset?, endOffset, …}——几何见 part 的 pos/startOffset/endOffset。
    // midi 型还可给 gain / soundSource / notes / vibratos / effects / automations / piecewiseAutomations / pitch / properties；
    // audio 型给 path。PartList 按起点自排，故无 index 参数（位置由 pos 决定）。
    public ScriptPart AddPart(JsValue info)
    {
        var t = W;
        var partInfo = ScriptInfo.ReadPartInfo(info);
        if (partInfo.Pos + partInfo.StartOffset < 0) throw new ScriptApiException("a part's start (pos + startOffset) must be >= 0.");
        ctx.EnsureWritable();
        var part = t.CreatePart(partInfo);
        t.InsertPart(part);
        ctx.Bump();
        return ctx.WrapPart(part);
    }

    // 把一个【游离】part 插入本轨（保持对象身份）。目标轨可以【不是】它原来那条——这就是跨轨迁移，也是
    // part 上挂的音源 / 音符 / 曲线 / effect / 音素整体搬家而不换身份（undo 记的是同一个对象）的唯一路径。
    public void InsertPart(ScriptPart part)
    {
        var t = W;
        if (part == null) throw new ScriptApiException("expected a part handle.");
        if (!part.Detached) throw new ScriptApiException("this part is already on a track; remove it first (track.removePart(part)).");
        ctx.EnsureWritable();
        // 换父不必手动改 part.Track：集合的 ItemAdded 会置它（Track.cs 里 mParts.ItemAdded 订阅了
        // part.Track = this + Activate()）。撤销时另一条轨的 ItemAdded 同样会把它置回去，故这一路无需额外记录。
        t.InsertPart(part.Part);
        part.Detached = false;
        ctx.Bump();
    }

    // 把 part 从本轨摘出，返回它的（现在游离的）句柄：不插回 = 删除，插到别的轨 = 跨轨迁移。
    public ScriptPart RemovePart(ScriptPart part)
    {
        var t = W;
        if (part == null || part.Detached) throw new ScriptApiException("expected a live part handle (from track.parts()/track.addPart()).");
        if (!ReferenceEquals(part.Part.Track, t)) throw new ScriptApiException("this part is not on this track.");
        ctx.EnsureWritable();
        t.RemovePart(part.Part);
        part.Detached = true;
        ctx.Bump();
        return part;
    }

    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "Track(\"{0}\", parts={1})", Track.Name.Value, Track.Parts.Count());
}

// 一个颤音句柄。坐标：pos 绝对 tick。叠加在音高曲线之上。
internal sealed class ScriptVibrato(ScriptContext ctx, Vibrato vibrato)
{
    internal Vibrato Vibrato { get; } = vibrato;
    // 已从所属 part 摘出（游离）：可读可插回，不可写。
    internal bool Detached { get; set; }

    Vibrato V => Vibrato;
    Vibrato W => Detached
        ? throw new ScriptApiException("this vibrato is detached (it was removed from its part) and can't be modified; insert it back with part.insertVibrato(vibrato) first, or build a new one from vibrato.getInfo().")
        : Vibrato;

    public double Pos { get => V.GlobalStartPos(); set => Move(absPos: value); }   // 绝对 tick
    public double Dur { get => V.Dur.Value; set => Move(dur: value); }
    // 以下均【非排序键】（VibratoList 只按 Pos↑ / 同 Pos 时 Dur↓ 排序）：直接 Set，不套 MoveVibrato。
    public double Frequency { get => V.Frequency.Value; set => Set(v => v.Frequency.Set(value)); }   // Hz
    public double Amplitude { get => V.Amplitude.Value; set => Set(v => v.Amplitude.Set(value)); }   // 半音
    public double Phase { get => V.Phase.Value; set => Set(v => v.Phase.Set(value)); }               // 单位 = π
    public double Attack { get => V.Attack.Value; set => Set(v => v.Attack.Set(value)); }            // 秒
    public double Release { get => V.Release.Value; set => Set(v => v.Release.Set(value)); }         // 秒

    // 本颤音的完整快照（纯数据，含两张影响表）。喂 part.addVibrato(info) 即复制出一个新颤音。
    public JsValue GetInfo() => ScriptInfo.ToJs(ctx.Engine, V.GetInfo(), V.Part.Pos.Value);

    // 所属 part（对齐 C# Vibrato.Part；只读，数据层不可改）。
    public ScriptPart Part() => ctx.WrapPart(V.Part);

    void Set(Action<Vibrato> mutate)
    {
        var v = W;
        ctx.EnsureBracket(v.Part);
        mutate(v);
        ctx.Bump();
    }

    // 排序键（pos/dur）经 MoveVibrato 摘除-重插维持列表有序。
    void Move(double? absPos = null, double? dur = null)
    {
        var v = W;
        var midi = v.Part;
        if (dur is { } vd && vd <= 0) throw new ScriptApiException("dur must be positive.");
        ctx.EnsureBracket(midi);
        double? relPos = absPos is { } ap ? ap - midi.Pos.Value : null;
        midi.MoveVibrato(v, () =>
        {
            if (relPos is { } p) v.Pos.Set(p);
            if (dur is { } d) v.Dur.Set(d);
        });
        ctx.Bump();
    }

    // ── 影响表：本颤音把振幅施加到哪些参数轨上（对齐 C# Vibrato.AffectedAutomations /
    //    AffectedEffectAutomations 两张平行表，两个命名空间互不相扰） ──

    // 音源级轨的影响表快照：{ 轨 id: 振幅 }。
    public JsValue AffectedAutomations() => ScriptInfo.AmplitudesToJs(ctx.Engine, V.AffectedAutomations);

    // effect 级轨的影响表快照：{ effect 实例 id: { 轨 id: 振幅 } }。外层键是 effect.id（实例稳定身份、
    // 不是链内位置），故重排效果链不会打乱这张表；effect 被删则留孤儿条目，undo 恢复同 id 即自然重连。
    public JsValue AffectedEffectAutomations() => ScriptInfo.EffectAmplitudesToJs(ctx.Engine, V.AffectedEffectAutomations);

    // 写一条轨的影响振幅（无关联即建立关联）。effect 省略 = 音源级轨；给了 effect 句柄 = 该 effect 的轨
    // （effect 须与本颤音同属一个 part）。
    public void SetAmplitude(string id, double amplitude, ScriptEffect? effect = null)
        => Set(v => v.SetAmplitude(ResolveKey(id, effect), amplitude));

    // 解除一条轨的关联（与 setAmplitude 对偶）。
    public void RemoveAmplitude(string id, ScriptEffect? effect = null)
        => Set(v => v.RemoveAssociation(ResolveKey(id, effect)));

    AutomationKey ResolveKey(string id, ScriptEffect? effect)
    {
        if (string.IsNullOrEmpty(id)) throw new ScriptApiException("an automation id is required.");
        if (effect == null) return AutomationKey.Voice(id);
        if (effect.Detached) throw new ScriptApiException("expected a live effect handle (from part.effects()).");
        if (!ReferenceEquals(effect.Effect.Part, V.Part))
            throw new ScriptApiException("the effect must be on the same part as this vibrato.");
        return AutomationKey.Effect(effect.Index, id);
    }

    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "Vibrato(pos={0:0}, dur={1:0}, freq={2:0.##}Hz, amp={3:0.##})",
            Pos, Dur, Frequency, Amplitude);
}

// 一个效果器句柄（挂在 midi part 的串行效果链上）。type 不可变（换类型 = 删了重加）。
internal sealed class ScriptEffect(ScriptContext ctx, IEffect effect)
{
    internal IEffect Effect { get; } = effect;
    // 已从效果链摘出（游离）：可读可插回，不可写。
    internal bool Detached { get; set; }

    IEffect E => Effect;
    IEffect W => Detached
        ? throw new ScriptApiException("this effect is detached (it was removed from its chain) and can't be modified; insert it back with part.insertEffect(effect) first, or build a new one from effect.getInfo().")
        : Effect;

    public string Type => E.Type;                                  // 引擎 type id（不可变）
    public string Name => EffectManager.GetDisplayName(E.Type);    // 显示名（只读）
    public string Id => E.Id;                                      // 实例稳定 id（本 part 链内唯一）
    public int Index                                              // 在链中的 0-based 位置（游离时 -1）
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
        set { var e = W; ctx.EnsureWritable(); e.IsEnabled.Set(value); ctx.Bump(); }
    }

    // 本效果器的完整快照（纯数据，含参数与自动化曲线）。喂 part.addEffect(info) 即复制出一个新实例
    // （落到同一条链时 id 会重新发号，避免与源撞身份）。
    public JsValue GetInfo() => ScriptInfo.ToJs(ctx.Engine, E.GetInfo(), E.Part.Pos.Value);

    // 所属 part（对齐 C# IEffect.Part；只读，数据层不可改）。
    public ScriptPart Part() => ctx.WrapPart(E.Part);

    // 读一个参数的当前值（number / boolean / string）；未设返回 null（默认值与可用键见 list_effects 的参数 schema）。
    public object? GetProperty(string key) => ScriptArgs.ReadScalarProperty(E.Properties, key);

    // 写一个参数（值须是 number / boolean / string）。键/取值范围见 list_effects。
    public void SetProperty(string key, JsValue value)
    {
        if (string.IsNullOrEmpty(key)) throw new ScriptApiException("effect property key is required.");
        var pv = ScriptArgs.ToScalarProperty(value, "effect property");
        var e = W;
        ctx.EnsureWritable();
        e.Properties.SetValue(key, pv);
        ctx.Bump();
    }

    // ── 本 effect 的参数自动化曲线（对齐 C# IEffect.Automations / PiecewiseAutomations，与 part 级逐一平行；
    // 曲线在 part 相对 tick 空间，读写口径同 part.sampleAutomation/setAutomation）。可编辑轨 id 由引擎声明。 ──

    public string[] AutomationIds()
        => E.AutomationConfigs.Where(kvp => !kvp.Value.IsPiecewise).Select(kvp => kvp.Key.Id).ToArray();

    // 在绝对 tick 区间 [startTick, endTick] 上等距采样本 effect 某自动化曲线。NaN = 该处无曲线。
    public double[] SampleAutomation(string id, double startTick, double endTick, int samples)
    {
        var effect = E;
        if (!effect.AutomationConfigs.TryGetValue(id, out var config) || config.IsPiecewise)
            throw new ScriptApiException(string.Format("unknown effect automation \"{0}\"; use one of effect.automationIds().", id));
        return effect.GetAutomationValues(ScriptPart.SampleTicks(effect.Part, startTick, endTick, samples), id);
    }

    // 覆盖写本 effect 某自动化曲线：清空 [startTick,endTick) 再落线。points=[{tick,value}]，value=参数绝对值；轨不存在按需创建，defaultValue 可选。
    public void SetAutomation(string id, double startTick, double endTick, JsValue points, JsValue? defaultValue = null)
    {
        var effect = W;
        double rel = effect.Part.Pos.Value;
        var pts = ScriptArgs.ReadPoints(points);
        ctx.EnsureBracket(effect.Part);
        var automation = effect.AddAutomation(id)
            ?? throw new ScriptApiException(string.Format("automation \"{0}\" is not available on this effect (not declared by its engine); use one of effect.automationIds().", id));
        if (ScriptArgs.AsNumOrNull(defaultValue) is { } dv) automation.DefaultValue.Set(dv);
        automation.Clear(startTick - rel, endTick - rel, 0);
        if (pts.Count > 0)
            automation.AddLine(pts.OrderBy(p => p.X).Select(p => new AnchorPoint(p.X - rel, p.Y)).ToList(), 0);
        ctx.Bump();
    }

    public void ClearAutomation(string id, double startTick, double endTick)
    {
        var effect = W;
        double rel = effect.Part.Pos.Value;
        ctx.EnsureBracket(effect.Part);
        if (effect.Automations.TryGetValue(id, out var automation))
            automation.Clear(startTick - rel, endTick - rel, 0);
        ctx.Bump();
    }

    // ── 本 effect 的分段自动化曲线（无默认基线、段间关断；与 part 级 piecewise 一族同形） ──

    public string[] PiecewiseAutomationIds()
        => E.AutomationConfigs.Where(kvp => kvp.Value.IsPiecewise).Select(kvp => kvp.Key.Id).ToArray();

    public double[] SamplePiecewiseAutomation(string id, double startTick, double endTick, int samples)
    {
        var effect = E;
        if (!effect.AutomationConfigs.TryGetValue(id, out var config) || !config.IsPiecewise)
            throw new ScriptApiException(string.Format("unknown effect piecewise automation \"{0}\"; use one of effect.piecewiseAutomationIds().", id));
        var ticks = ScriptPart.SampleTicks(effect.Part, startTick, endTick, samples);
        return effect.PiecewiseAutomations.TryGetValue(id, out var automation) ? automation.GetValues(ticks) : new double[ticks.Length];
    }

    public void SetPiecewiseAutomationLine(string id, double startTick, double endTick, JsValue points)
    {
        var effect = W;
        double rel = effect.Part.Pos.Value;
        var pts = ScriptArgs.ReadPoints(points);
        ctx.EnsureBracket(effect.Part);
        var automation = effect.AddPiecewiseAutomation(id)
            ?? throw new ScriptApiException(string.Format("piecewise automation \"{0}\" is not available on this effect (not declared by its engine); use one of effect.piecewiseAutomationIds().", id));
        automation.Clear(startTick - rel, endTick - rel);
        if (pts.Count > 0)
            automation.AddLine(pts.OrderBy(p => p.X).Select(p => new AnchorPoint(p.X - rel, p.Y)).ToList(), 0);
        ctx.Bump();
    }

    public void ClearPiecewiseAutomation(string id, double startTick, double endTick)
    {
        var effect = W;
        double rel = effect.Part.Pos.Value;
        ctx.EnsureBracket(effect.Part);
        if (effect.PiecewiseAutomations.TryGetValue(id, out var automation))
            automation.Clear(startTick - rel, endTick - rel);
        ctx.Bump();
    }

    // ── 固定：把本 effect 某条轨的合成参数固定进同 id 的可编辑轨（口径同 part.lockAutomation，作用域是本 effect） ──

    public bool LockAutomation(string id, JsValue? startTick = null, JsValue? endTick = null)
    {
        var effect = W;
        var part = effect.Part;
        var key = AutomationKey.Effect(Index, id);
        ScriptPart.RequirePairedSynthesizedParameter(part, key, id, "effect.automationIds() / effect.piecewiseAutomationIds()");
        var (start, end) = ScriptPart.ReadLockRange(startTick, endTick);
        ctx.EnsureBracket(part);
        if (!part.WriteSynthesizedParameterLock(key, part.CaptureSynthesizedParameter(key), start, end))
            return false;

        ctx.Bump();
        return true;
    }

    // 同 part.hasSynthesizedParameter，作用域是本 effect。游离期恒 false——链上没有位置就没有合成参数（effect 的合成参数按【链中下标】
    // 定址，见 AutomationKey.Effect），此时连 key 都构不出来。
    public bool HasSynthesizedParameter(string id)
    {
        int index = Index;
        return index >= 0 && E.Part.HasPairedSynthesizedParameter(AutomationKey.Effect(index, id));
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
