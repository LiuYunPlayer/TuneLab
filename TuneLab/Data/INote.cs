using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.Utils;
using TuneLab.SDK;
using PhonemeInfo = TuneLab.SDK.PhonemeInfo;
using NoteInfo = TuneLab.SDK.NoteInfo;

namespace TuneLab.Data;

// 宿主显示侧的解析后音素（绝对秒、已跨 note 去重叠后的"落点"）：供音素带绘制 / 命中测试 / 拖拽 / 拆分消费。
// 与 SDK 的 VoicePhoneme（音素描述符契约，只报标称时长）区分——位置由宿主按时长模型派生到此。
internal readonly record struct DisplayPhoneme(string Symbol, double StartTime, double EndTime, double StretchWeight, bool IsLead);

// 宿主业务层 note。不直接实现 SDK 的 IVoiceNote——插件经会话级 context 的 note 代理
// 订阅（中间层隔离）；本接口只服务宿主自身（编辑/UI/序列化）。
internal interface INote : IDataObject<NoteInfo>, ISelectable, ILinkedNode<INote>
{
    new INote? Next { get; }
    new INote? Last { get; }
    IMidiPart Part { get; }
    IDataProperty<double> Pos { get; }
    IDataProperty<double> Dur { get; }
    IDataProperty<int> Pitch { get; }
    IDataProperty<string> Lyric { get; }
    IDataProperty<string> Pronunciation { get; }
    DataPropertyObject Properties { get; }
    IDataObjectList<IPhoneme> Phonemes { get; }
    SynthesizedPhoneme[]? SynthesizedPhonemes { get; set; }
    IReadOnlyCollection<string> Pronunciations { get; }

    double StartTime => Part.TempoManager.GetTime(this.GlobalStartPos());
    double EndTime => Part.TempoManager.GetTime(this.GlobalEndPos());

    // 音素带显示 / 编辑的单一口径（绝对秒，与合成产物同一时间系，已跨 note 去重叠）：
    // 固定音素用钉死几何，合成音素用引擎回报的绝对位置；**两者都进同一个 PhonemeLayout 推挤窗口**。
    // 防御性——清除合成音素的责任虽交给插件，但宿主显示不假设插件守约：即便插件未及时清除 / 未自行去重叠
    // 合成音素，本算法也把相接 / 重叠的相邻音素去重叠（无重叠时 PhonemeLayout 为 no-op，故守约插件显示不变）。
    // 无内容（合成前 / 乘客被铺过 / 空 note）返回空。
    IReadOnlyList<DisplayPhoneme> DisplayPhonemes
    {
        get
        {
            // 乘客透明：插件判定为延续的 note 不渲染自己的音素——判定优先级最高（布局第一步就是
            // 延音判定，过了这层才读音素数据），钉死、违约回显都不例外：钉死在延续 note 上的语义
            // 归引擎解释；违约回显如实落账但不被读取（忽略即兜底）。legacy 适配器判定恒 false
            // （老模型无乘客机制，忠实降级），其占位回显走本判据之外的普通内容显示。
            if (this.IsEffectiveContinuation())
                return [];

            bool pinned = !Phonemes.IsEmpty();
            var synth = SynthesizedPhonemes;
            int n = pinned ? Phonemes.Count : (synth?.Length ?? 0);
            if (n == 0)
                return [];

            // 显示门控：本 note 的最终音素布局依赖相接邻居的音素几何（跨 note 推挤）。若某侧有「相接、非乘客、却尚无
            // 音素数据」的邻居（正在合成 / 待合成 → 留白），本 note 的边界无法确定，强行按现状铺会在邻居数据到达后跳变，
            // 故本 note 一并留白、待邻居音素就绪。相接才依赖——有空隙 / 到 part 末尾则各自独立、照常显示。
            if (PrevNeighborUnresolved() || NextNeighborUnresolved())
                return [];

            var window = new List<PhonemeLayoutNote>();
            var prev = PrevContentNeighbor();
            var next = NextContentNeighbor();
            if (prev != null)
                window.Add(BuildLayoutNote(prev));
            int baseIndex = window.Count;
            window.Add(BuildLayoutNote(this));
            if (next != null)
                window.Add(BuildLayoutNote(next));

            var selfTimes = PhonemeLayout.Resolve(window)[baseIndex];
            var list = new DisplayPhoneme[n];
            for (int i = 0; i < n; i++)
            {
                var (start, end) = selfTimes[i];
                list[i] = pinned
                    ? new DisplayPhoneme(Phonemes[i].Symbol.Value, start, end, Phonemes[i].StretchWeight.Value, Phonemes[i].IsLead.Value)
                    : new DisplayPhoneme(synth![i].Symbol, start, end, synth[i].StretchWeight, synth[i].IsLead);
            }
            return list;
        }
    }

    // note 是否承载音素数据（钉死或合成）——布局意义上的"有几何可铺"。它不是乘客判据（乘客身份
    // = IsEffectiveContinuation，插件判定的唯一通道、宿主照单全收），只用于显示门控终判
    //（"非乘客邻居有没有数据可依"）。
    private static bool HasPhonemeContent(INote x)
    {
        return !x.Phonemes.IsEmpty() || (x.SynthesizedPhonemes != null && !x.SynthesizedPhonemes.IsEmpty());
    }

    // 本 note 钉死音素的核填充终点绝对秒（元音前向铺过乘客 melisma，不封顶、不压缩）：
    // 有延音符乘客时铺到**最后一个相接乘客**的末；无乘客时 = own 末。
    // 用**有效末**（去重叠后，= EffectiveEndTime）——与喂插件的快照同口径（snapshot 走 proxy 的有效末），
    // 故宿主与插件喂给共享 PhonemeLayout 的 FillEnd 同口径、WYSIWYG 由构造保证（不靠"满末≡有效末"的等价证明）。
    // note 级去重叠（pos/dur-only，钳到下一 note 起点）是**前置**步骤、与音素布局正交；音素布局在其下游、不见原始重叠。
    // （满末与有效末喂进布局其实结果等价：核是弹性吸收者，压缩量 = Σ辅音−available，与核自然长无关 → 与 fillEnd 无关。
    //  取有效末是为构造一致，非功能必需。）
    //
    // 乘客判据 = IsEffectiveContinuation，纯插件判定、无任何宿主合取——判定优先级最高：布局第一步
    // 就是延音判定，判定为延续的 note 其音素数据（钉死 / 回显）根本不被读取（钉死语义归引擎解释；
    // 违约回显落账但被忽略；legacy 适配器判定恒 false——老模型无乘客机制，其回显走普通内容显示）。
    // 原"合成前用 '-' 预测、合成后以 HasPhonemeContent 为权威"的双轨制退役，显示骨架合成前即终态。
    // 不设相接条件：空隙语义归插件——标准判定跨空隙断链（空隙 = 静音）；自定义判定若跨空隙延续，
    // 宿主照铺、音频照做，显示与音频一起走。
    private double ForwardFillEnd()
    {
        double fillEnd = this.EffectiveEndTime();   // 无乘客时 = own 有效末（与快照口径一致）
        INote? next = Next;
        while (next != null && next.IsEffectiveContinuation())
        {
            // 有乘客：容纳音素的长度由**最后一个乘客**（乘客组末）决定，覆盖而非取 max。
            fillEnd = next.EffectiveEndTime();
            next = next.Next;
        }
        return fillEnd;
    }

    // 把一个 note 物化成 SDK 布局输入（VoicePhonemeLayoutNote）：核起点=音符头、fillEnd=前向铺末（含 melisma），
    // 音素只报标称时长 / 权重 / IsLead，位置由布局派生。钉死 note 用钉死音素，否则用引擎回报的合成音素——关键是
    // **不**用引擎回报的绝对位置（那是已去重叠压缩的产物；喂给布局会让相接判据把"已压缩到核前"误判成"有空隙"而
    // 跳过压缩）。改用自然几何后合成 note 与钉死 note 在跨 note 推挤里行为一致：拖邻居前辅音时本 note 元音同步压缩。
    // overrideIdx≥0 时本 note 该钉死音素改用 overrideDur（拖拽反解用，不改数据）；核时长恒按填充派生、override 对其无效。
    private static PhonemeLayoutNote BuildLayoutNote(INote note, int overrideIdx = -1, double overrideDur = 0)
    {
        IReadOnlyList<SynthesizedPhoneme> phonemes;
        if (!note.Phonemes.IsEmpty())
        {
            var arr = new SynthesizedPhoneme[note.Phonemes.Count];
            for (int k = 0; k < note.Phonemes.Count; k++)
                arr[k] = new SynthesizedPhoneme
                {
                    Symbol = note.Phonemes[k].Symbol.Value,
                    Duration = k == overrideIdx ? overrideDur : note.Phonemes[k].Duration.Value,
                    StretchWeight = note.Phonemes[k].StretchWeight.Value,
                    IsLead = note.Phonemes[k].IsLead.Value,
                };
            phonemes = arr;
        }
        else if (note.SynthesizedPhonemes is { Length: > 0 } synth)
            phonemes = synth;
        else
            phonemes = [];

        return new PhonemeLayoutNote { FillStart = note.StartTime, FillEnd = note.ForwardFillEnd(), Phonemes = phonemes };
    }

    // 最近的、带音素内容（钉死或合成）的相邻 note（跨过乘客；落在非乘客的自由/空 note 或边界则无邻居）。
    // 用于全局布局的 3-note 窗口：相邻 note 间的去重叠相互独立，只需直接内容邻居即可正确解析本 note 的两条边界。
    // 含合成邻居（非仅钉死）——显示侧防御性去重叠须把合成音素也当作推挤参与方。
    private INote? PrevContentNeighbor()
    {
        INote? p = Last;
        while (p != null && p.IsEffectiveContinuation())
            p = p.Last;
        return p != null && HasPhonemeContent(p) ? p : null;
    }

    private INote? NextContentNeighbor()
    {
        INote? p = Next;
        while (p != null && p.IsEffectiveContinuation())
            p = p.Next;
        return p != null && HasPhonemeContent(p) ? p : null;
    }

    // 显示门控判据（见 DisplayPhonemes）：某侧是否存在「相接、非乘客、却无音素内容」的邻居 → 本 note 布局未决。
    // 向前/向后跨过相接的延音符乘客（透明，由本 note 元音铺过），落在中断点 note：
    // 内容 note → 已决；不相接 / 末尾 → 无依赖；非乘客且无音素且相接 → 未决（邻居待合成，本 note 须等其音素）。
    private bool NextNeighborUnresolved()
    {
        double fillEnd = this.EffectiveEndTime();   // 本 note 有效末（向前铺过乘客，同 ForwardFillEnd 口径）
        INote? next = Next;
        while (next != null && next.IsEffectiveContinuation())
        {
            fillEnd = next.EffectiveEndTime();
            next = next.Next;
        }
        // 终判仍看相接：这是布局依赖的几何前提（不相接的邻居音素推挤不到本 note），非延音判定。
        // 严格比较无容差：边界同源于 tick 经确定性换算的秒，相接即精确相等（PhonemeLayout.Connected 同论证）。
        return next != null && !HasPhonemeContent(next) && next.StartTime <= fillEnd;
    }

    private bool PrevNeighborUnresolved()
    {
        double reachStart = StartTime;
        INote? prev = Last;
        while (prev != null && prev.IsEffectiveContinuation())
        {
            reachStart = prev.StartTime;
            prev = prev.Last;
        }
        return prev != null && !HasPhonemeContent(prev) && prev.EndTime >= reachStart;
    }

    // 本 note 钉死音素的 effective 相对秒边界（拖拽反解 / EffectivePinnedPhonemeTimes 用）。3-note 窗口
    // （前/后最近**有内容**邻居 + 本，含合成邻居）喂全局两阶布局（SDK 的 VoicePhonemeLayout）：元音先让、辅音簇
    // 等比压（含跨 note 辅音簇）。相邻 note 间边界相互独立，故 3-note 窗口足以正确解析本 note 两条边界。
    // overrideIdx≥0：用 overrideDur 代替本 note 该音素时长（拖拽反解用，不改数据）。
    private (double Start, double End)[] ResolvedRelTimes(int overrideIdx, double overrideDur)
    {
        int n = Phonemes.Count;
        var window = new List<PhonemeLayoutNote>();
        var prev = PrevContentNeighbor();
        var next = NextContentNeighbor();
        if (prev != null)
            window.Add(BuildLayoutNote(prev));
        int baseIndex = window.Count;   // 本 note 在窗口中的下标
        window.Add(BuildLayoutNote(this, overrideIdx, overrideDur));
        if (next != null)
            window.Add(BuildLayoutNote(next));

        var selfTimes = PhonemeLayout.Resolve(window)[baseIndex];
        double start = StartTime;
        var result = new (double, double)[n];
        for (int i = 0; i < n; i++)
            result[i] = (selfTimes[i].Start - start, selfTimes[i].End - start);
        return result;
    }

    public IReadOnlyList<(double Start, double End)> EffectivePinnedPhonemeTimes() => ResolvedRelTimes(-1, 0);

    // 拖拽音素起边界（index = 音素 index 的起点；末边界 index==n 派生不可拖）：把该边界拖到【显示】相对秒 targetRel。
    // · index == L（前置分界线 = 首个非前置音素 = 核起点）：核起点恒为音符头（拍点），不可拖 → no-op。
    // · 其余 index（前置辅音 / 后辅音 / 第二个起的核的起点）：改的是音素 index 的 **时长**——累积布局令相邻音素整体平移（推挤）、核吸收。
    //   多核场景下改本核时长会经共享弹性余量改写前一核的长度，从而单调挪动本核起点（故"两元音间分界线"亦可拖，无需特判）。
    // 跟手：显示位 = 自然布局经 PhonemeLayout 压缩后的结果，故反解——bisect 被编辑量使该边界的【显示】起点落到 targetRel
    // （显示起点随该量单调变化）。压缩区里被布局钉住时，跟到钉点即止。改 pinned 即触发重合成。
    void DragPinnedBoundary(int index, double targetRel)
    {
        int n = Phonemes.Count;
        if (n == 0 || index < 0 || index >= n)
            return;

        int L = 0;
        while (L < n && Phonemes[L].IsLead.Value) L++;   // 前置音素前缀；L = 前置分界线（核起点）下标

        if (index == L)   // 核起点 = 音符头（拍点），不可拖
            return;

        // 下界放开（推挤语义：左侧邻居随之平移、无固定下界，可向 note 前自由伸展）；上界 = 本音素末（保正长、不与右反相）。
        var cur = ResolvedRelTimes(-1, 0);
        targetRel = Math.Min(targetRel, cur[index].End);

        // 时长手柄：bisect 时长使该边界显示起点落到 targetRel（显示起点随时长单调递减）。
        double DisplayedDur(double dur) => ResolvedRelTimes(index, dur)[index].Start;
        double loDur = 0;
        double hiDur = Math.Max(EndTime - StartTime, 0.05);
        for (int i = 0; i < 24 && DisplayedDur(hiDur) > targetRel; i++) hiDur *= 2;
        for (int it = 0; it < 32; it++)
        {
            double mid = (loDur + hiDur) / 2;
            if (DisplayedDur(mid) <= targetRel) hiDur = mid; else loDur = mid;
        }
        Phonemes[index].Duration.Set(hiDur);
    }
}

internal static class INoteExtension
{
    // 编辑器延音记号（**录入便利判据**，仅编辑器 UX 用：split 默认填 "-"、录词跳过等）。
    // "-" 是编辑器的默认延音记号约定，**不再是契约概念**——延音的结构判定已完整下放插件
    // （见 IsEffectiveContinuation），本判据不参与布局 / 手势 / 合成。
    public static bool IsEditorContinuationLyric(this INote note)
    {
        return note.Lyric.Value == "-";
    }

    // 生效延续（显示布局 + 编辑手势的统一判据）= 引擎完整判定的缓存值（IVoiceSynthesisSession.IsContinuation
    // 经 MidiPart 物化），宿主不叠加任何自己的判据。判定同步可知——显示骨架合成前即终态（硬 WYSIWYG）。
    // 判定无真空、也无宿主链回溯：voice part 恒有会话（无声源回退零引擎 EmptyVoiceSynthesisEngine，
    // 其判定 = 编辑器 "-" 约定——那是该引擎自有的语义）；instrument 无延音概念、恒 false。
    // 链 / 空隙 / 孤儿规则不再是契约资产——各引擎判定语义自有（SDK 无共享实现）。
    public static bool IsEffectiveContinuation(this INote note)
    {
        return note.Part.IsPluginContinuation(note);
    }

    public static double StartPos(this INote note)
    {
        return note.Pos.Value;
    }

    public static double EndPos(this INote note)
    {
        return note.Pos.Value + note.Dur.Value;
    }

    // 去重叠（后盖前，非破坏）：voice 单声部约束下 note 的有效结束 = 自身末与下一 note 起点的较小者。
    // 起点从不移动、只缩尾；下一 note 取数据序相邻者（StartPos 升 / EndPos 降）。同起点和弦里较长的兄弟
    // 被钳到自身起点（有效时长归零、被覆盖）。note 的 Dur 不动——这是派生量，供 voice 快照与钢琴窗暗色显示
    // 共用一份定义；挂 instrument 引擎时调用方不取它即保留重叠多声部。part 相对 tick。
    public static double EffectiveEndPos(this INote note)
    {
        var next = note.Next;
        return next != null ? Math.Min(note.EndPos(), next.StartPos()) : note.EndPos();
    }

    // 有效时长是否归零（被完全覆盖：同起点和弦中排在前、非存活的兄弟）。voice 快照据此滤除、钢琴窗全暗显示。
    public static bool IsOverlapZeroed(this INote note)
    {
        return note.EffectiveEndPos() <= note.StartPos();
    }

    public static double GlobalStartPos(this INote note)
    {
        return note.Part.Pos.Value + note.StartPos();
    }

    public static double GlobalEndPos(this INote note)
    {
        return note.Part.Pos.Value + note.EndPos();
    }

    // 全局有效结束（去重叠后盖前，见 EffectiveEndPos）：钢琴窗据此把 [有效结束, 画出末] 画暗。
    public static double GlobalEffectiveEndPos(this INote note)
    {
        return note.Part.Pos.Value + note.EffectiveEndPos();
    }

    // 有效结束的全局秒（去重叠后）：音素乘客铺设与喂插件 / audio 同口径（snapshot 用 proxy 的有效末），
    // 故乘客铺设须用此而非 INote.EndTime（画出末），否则乘客本体 dur 过长时元音末铺到画出末、与 audio 不符。
    public static double EffectiveEndTime(this INote note)
    {
        return note.Part.TempoManager.GetTime(note.GlobalEffectiveEndPos());
    }

    public static INote SplitAt(this INote note, double pos)
    {
        note.Part.BeginMergeDirty();
        var newNote = note.Part.CreateNote(new NoteInfo() { Pos = pos, Dur = note.EndPos() - pos, Pitch = note.Pitch.Value, Lyric = "-" });   // "-" 延音软约定（编辑器默认）
        note.Part.MoveNote(note, () => note.Dur.Set(pos - note.StartPos()));
        note.Part.InsertNote(newNote);
        note.Part.EndMergeDirty();
        note.Part.Notes.DeselectAllItems();
        return newNote;
    }

    public static void LockPhonemes(this INote note)
    {
        if (!note.Phonemes.IsEmpty())
            return;

        if (note.SynthesizedPhonemes == null)
            return;

        if (note.SynthesizedPhonemes.IsEmpty())
            return;

        // 锁定 = 把合成产物固定为用户数据：直接存各音素的【时长 + 权重 + IsLead】（位置由布局派生、不存）。
        // 辅音(w=0)时长即固定长；核(w>0)时长记录但布局忽略（恒按填充派生），故无需「反压缩」——
        // 显示侧 PhonemeLayout 会按当前邻居重新去重叠（核重新填充并再让位），与合成同源、常态下不双重压缩。
        // 核起点恒在音符头，故无需记录前置分界线偏移。
        foreach (var p in note.SynthesizedPhonemes)
            note.Phonemes.Add(Phoneme.Create(new PhonemeInfo
            {
                Symbol = p.Symbol,
                Duration = Math.Max(0, p.Duration),
                StretchWeight = p.StretchWeight,
                IsLead = p.IsLead,
            }));
    }

    // 清除锁定（钉死）音素：清空后该 note 回到合成音素口径（与改歌词 / 改发音时的清空同源）。
    public static void ClearLockedPhonemes(this INote note)
    {
        note.Phonemes.Clear();
    }

    public static string? FinalPronunciation(this INote note)
    {
        if (!string.IsNullOrEmpty(note.Pronunciation.Value))
            return note.Pronunciation.Value;

        return note.Pronunciations.FirstOrDefault();
    }
}
