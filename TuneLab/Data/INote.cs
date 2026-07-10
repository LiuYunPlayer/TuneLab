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
internal readonly record struct DisplayPhoneme(string Symbol, double StartTime, double EndTime, double StretchWeight);

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
    // 钉死音素的前置量（拍前发声量，自然秒）：note 头之前音素的占位长度，决定拍前 / 拍后归属（见 PhonemeLayout）。
    // 仅 Phonemes 非空时有意义；元音起手 / 无钉死时 = 0。持久化（NoteInfo.Preutterance）、undo。
    IDataProperty<double> Preutterance { get; }
    SynthesizedPhoneme[]? SynthesizedPhonemes { get; set; }
    // 合成产物的前置量（拍前发声量）：与 SynthesizedPhonemes 同源回填（引擎回报的 SynthesizedSyllable.Preutterance）。
    double SynthesizedPreutterance { get; set; }
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
                // 前后归属（拍前/拍后/跨拍）不再落每音素标志——需要时由消费方按 StartTime/EndTime 与 note 头几何派生。
                list[i] = pinned
                    ? new DisplayPhoneme(Phonemes[i].Symbol.Value, start, end, Phonemes[i].StretchWeight.Value)
                    : new DisplayPhoneme(synth![i].Symbol, start, end, synth[i].StretchWeight);
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
        double preutterance;
        if (!note.Phonemes.IsEmpty())
        {
            var arr = new SynthesizedPhoneme[note.Phonemes.Count];
            for (int k = 0; k < note.Phonemes.Count; k++)
                arr[k] = new SynthesizedPhoneme
                {
                    Symbol = note.Phonemes[k].Symbol.Value,
                    Duration = k == overrideIdx ? overrideDur : note.Phonemes[k].Duration.Value,
                    StretchWeight = note.Phonemes[k].StretchWeight.Value,
                };
            phonemes = arr;
            preutterance = note.Preutterance.Value;   // 钉死前置量（拍前发声量），note 本体直接提供
        }
        else if (note.SynthesizedPhonemes is { Length: > 0 } synth)
        {
            phonemes = synth;
            preutterance = note.SynthesizedPreutterance;   // 合成回填的前置量
        }
        else
        {
            phonemes = [];
            preutterance = 0;
        }

        return new PhonemeLayoutNote { FillStart = note.StartTime, FillEnd = note.ForwardFillEnd(), Preutterance = preutterance, Phonemes = phonemes };
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
    // 规则「编辑相邻的刚性音素」——刚性音素时长直接=屏幕长度、可直接设；弹性音素长度是派生的（由 Distribute 吸收）：
    // · onset（恰好落在 note 头的边界）：走独立 onset 手柄（改 Preutterance），不在此拖 → no-op。
    // · index 落在拍后（自然offset ≥ Preutterance）：**线前刚性→改线前音素**（其末端=该边界，线性）、否则改线后音素；
    //   bisect 反解使该边界显示起点落到 targetRel（弹性核吸收、拍后其余整体平移）；Preutterance 不动。
    // · index 落在拍前（自然offset < Preutterance）：拍前音素按自然长往左堆，改其 Duration 会顺累积推动 note 头切点/拍后音素，
    //   故**同步改 Preutterance（同量）**把变化吸进 pre-roll、拍后不动（右缘固定，newDur = 显示右缘 − targetRel，线性）。
    // 改 pinned 即触发重合成。
    void DragPinnedBoundary(int index, double targetRel)
    {
        int n = Phonemes.Count;
        if (n == 0 || index < 0 || index >= n)
            return;

        double preutter = Preutterance.Value;
        double cum = 0;                                  // 音素 index 起点的自然 offset
        for (int k = 0; k < index; k++) cum += Math.Max(0, Phonemes[k].Duration.Value);

        // 首音素(index==0)的左边界 = 其拍前部分(front)的起点。index==0 时 cum=0 → front 自然长 = Preutterance
        // （拍前未压缩，1:1，故 front 显示长 = 自然长 = −targetRel）。前面无音素可编辑，统一按「改 front(=Preutterance)、
        // 保持拍后部分(back)不变」处理，右缘随之：左缘越左 → front 增（纯前置更长 / 跨拍前半更多）；越过 note 头 → front→0（全拍后）。
        // 纯前置 lead(back=0)与跨拍首音素(back>0)都线性跟手、无跳；避免走拍后 bisect 去改被 front 钉死的首音素起点（无杠杆→发散→挤没邻居）。
        if (index == 0)
        {
            double firstDur = Math.Max(0, Phonemes[0].Duration.Value);
            if (preutter <= firstDur + 1e-9)   // note 头落在首音素内（跨拍 / 纯前置末 / 全拍后）：改 front(=Preutterance)、保拍后部分(back)、右缘随之
            {
                double back = Math.Max(0, firstDur - preutter);
                double newPre = Math.Max(0, -targetRel);       // 左缘位置 → 新 front（拍前未压缩 1:1）
                Preutterance.Set(newPre);
                Phonemes[0].Duration.Set(newPre + back);
                return;
            }
            // 否则首音素整段拍前、note 头更深（其后才有 lead/跨拍音素）：落到下面纯前置分支——grow 首音素 + Preutterance 同步，
            // 保后续切点/跨拍不变（不把 note 头处的边界吸附走）。
        }
        // 落在音符头的边界（index>0）不特殊处理：走下面的拍后规则改**线前**音素（如最后一个 lead），
        // 线前音素随拖动越过音符头自然形成跨拍、Preutterance 不变、方向与光标一致。

        var cur = ResolvedRelTimes(-1, 0);
        targetRel = Math.Min(targetRel, cur[index].End); // 上界 = 本音素末（保正长）

        double oldDur = Math.Max(0, Phonemes[index].Duration.Value);

        if (cum + oldDur < preutter + 1e-9)              // **整段**在拍前（音素末 ≤ note 头）= 纯前置：右缘固定、线性求长 + Preutterance 同步吸收
        {
            double newDur = Math.Max(0, cur[index].End - targetRel);
            Phonemes[index].Duration.Set(newDur);
            Preutterance.Set(Math.Max(0, preutter + (newDur - oldDur)));
            return;
        }
        // 到这里 index 音素末 > note 头：要么整段拍后、要么**本音素就是跨拍音素**（其左边界虽在拍前，整段却跨过 note 头）。
        // 跨拍音素的左边界不能走上面的纯前置公式（其 End 在拍后很右、newDur 会突变致跳），改走下面「编辑相邻刚性音素」——
        // 编辑线前刚性音素（其末端=该边界），跨拍音素前半随之增减、线性跟手、Preutterance 不变。

        // 拍后：一条边界 = 线前那个音素的末端。编辑相邻的**刚性**音素（其时长可直接决定该边界；弹性由 Distribute 吸收）：
        // 线前刚性→改线前（末端即该边界，改它线性单调）；否则改线后（弹性核经比例吸收，仍单调可解）。
        int editIdx = (index > 0 && Phonemes[index - 1].StretchWeight.Value == 0) ? index - 1 : index;
        if (editIdx == index - 1)                          // 改线前音素：下界保其正长
            targetRel = Math.Max(targetRel, cur[index - 1].Start);
        double BoundaryAt(double dur) => ResolvedRelTimes(editIdx, dur)[index].Start;
        double loDur = 0, hiDur = Math.Max(EndTime - StartTime, 0.05);
        bool inc = BoundaryAt(hiDur) >= BoundaryAt(0);     // 改线前刚性→随时长单调增；改线后→单调减
        for (int i = 0; i < 24; i++)
        {
            if (inc ? BoundaryAt(hiDur) >= targetRel : BoundaryAt(hiDur) <= targetRel) break;
            hiDur *= 2;
        }
        for (int it = 0; it < 32; it++)
        {
            double mid = (loDur + hiDur) / 2;
            if (inc ? BoundaryAt(mid) >= targetRel : BoundaryAt(mid) <= targetRel) hiDur = mid; else loDur = mid;
        }
        Phonemes[editIdx].Duration.Set(hiDur);
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

        // 锁定 = 把合成产物固定为用户数据：直接存各音素的【时长 + 权重】（位置由布局派生、不存），
        // 并把合成前置量固化为 note.Preutterance（拍前 / 拍后归属由它派生，故无需每音素 IsLead）。
        // 辅音(w=0)时长即固定长；核(w>0)时长记录但布局忽略（恒按填充派生），故无需「反压缩」——
        // 显示侧 PhonemeLayout 会按当前邻居重新去重叠（核重新填充并再让位），与合成同源、常态下不双重压缩。
        note.Preutterance.Set(note.SynthesizedPreutterance);
        foreach (var p in note.SynthesizedPhonemes)
            note.Phonemes.Add(Phoneme.Create(new PhonemeInfo
            {
                Symbol = p.Symbol,
                Duration = Math.Max(0, p.Duration),
                StretchWeight = p.StretchWeight,
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
