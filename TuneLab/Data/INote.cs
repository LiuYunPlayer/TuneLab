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
// 与 SDK 的 SynthesizedPhoneme（音素描述符契约，只报标称时长）区分——位置由宿主按时长模型派生到此。
// IsLeading = 该音素属引导列表（结构化分类，非几何派生）：波形带明暗、侧栏对齐直接读它。
internal readonly record struct DisplayPhoneme(string Symbol, double StartTime, double EndTime, double StretchWeight, bool IsLeading);

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
    // 钉死音素结构化双列表：引导（核前前置辅音）/ 主体（核 + 尾辅音），时间序。两者皆空 = 非钉死（引擎 G2P）。
    // 分类即列表成员（抗抖）；写入方显式选列表（编译期强制 revisit）。持久化 / undo。
    IDataObjectList<IPhoneme> LeadingPhonemes { get; }
    IDataObjectList<IPhoneme> BodyPhonemes { get; }
    // 钉死音素的主体起点（= 两列表结合线）相对 note 头的有符号偏移：junction = noteStart + BodyOffset（左负右正）。
    // 仅有音素时有意义；元音起手 / 无钉死时 = 0。持久化（NoteInfo.BodyOffset）、undo。
    IDataProperty<double> BodyOffset { get; }
    // 合成产物壳（引擎回填的 SynthesizedSyllable：引导/主体双列表 + BodyOffset）；LockPhonemes 据此固化为钉死数据。
    SynthesizedSyllable? SynthesizedSyllable { get; set; }
    IReadOnlyCollection<string> Pronunciations { get; }

    // 全序列音素只读视图 = LeadingPhonemes ++ BodyPhonemes（时间序，Q5 每次拼接）：供只读消费（显示/计数/索引/序列化）。
    // **不可变**——增删 / 钉死 / 清除等写入方改用具体列表（LeadingPhonemes / BodyPhonemes）。元素是共享的真 IPhoneme 对象，
    // 故 phonemes[i].Duration.Set(...) 这类**改音素本体**仍可经视图操作（改的是列表里那一个）。
    IReadOnlyList<IPhoneme> Phonemes => [.. LeadingPhonemes, .. BodyPhonemes];
    // 全序列音素个数（引导 + 主体）。
    int PhonemeCount => LeadingPhonemes.Count + BodyPhonemes.Count;
    // 是否有钉死音素（任一列表非空）。
    bool HasPinnedPhonemes => LeadingPhonemes.Count > 0 || BodyPhonemes.Count > 0;

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

            bool pinned = HasPinnedPhonemes;
            var syllable = SynthesizedSyllable;
            var pinnedPhonemes = pinned ? Phonemes : null;                    // 全序列（引导 ++ 主体）
            var synthPhonemes = pinned ? null : syllable?.Phonemes;
            int leadingCount = pinned ? LeadingPhonemes.Count : (syllable?.LeadingPhonemes.Count ?? 0);
            int n = pinned ? pinnedPhonemes!.Count : (synthPhonemes?.Count ?? 0);
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
                // 引导 / 主体归属 = 结构化分类（前 leadingCount 个属引导），非几何派生。
                bool isLeading = i < leadingCount;
                list[i] = pinned
                    ? new DisplayPhoneme(pinnedPhonemes![i].Symbol.Value, start, end, pinnedPhonemes[i].StretchWeight.Value, isLeading)
                    : new DisplayPhoneme(synthPhonemes![i].Symbol, start, end, synthPhonemes[i].StretchWeight, isLeading);
            }
            return list;
        }
    }

    // note 是否承载音素数据（钉死或合成）——布局意义上的"有几何可铺"。它不是乘客判据（乘客身份
    // = IsEffectiveContinuation，插件判定的唯一通道、宿主照单全收），只用于显示门控终判
    //（"非乘客邻居有没有数据可依"）。
    private static bool HasPhonemeContent(INote x)
    {
        return x.HasPinnedPhonemes || (x.SynthesizedSyllable is { } s && s.Phonemes.Count > 0);
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

    // 把一个 note 物化成 SDK 布局输入（PhonemeLayoutNote）：note 头 / fillEnd（含 melisma）+ 引导 / 主体双列表 + BodyOffset，
    // 音素只报标称时长 / 权重，位置由布局按 junction 单次锚定派生。钉死 note 用钉死双列表，否则用引擎回报的合成音节——关键是
    // **不**用引擎回报的绝对位置（那是已去重叠压缩的产物；喂给布局会让相接判据把"已压缩到核前"误判成"有空隙"而跳过压缩）。
    //
    // 精度：常态显示 / 音频路径（无 override）直取存储 BodyOffset 喂布局，绝不经 Σleading 中转（offset=0 恒等精确）。
    // 拖拽反解（override 非默认）走内部「有效前置量 P = Σleading − BodyOffset」等价参数化，仅在交互迭代中转换 BodyOffset = Σleading − P
    // （复用与旧模型等价的反解数学、行为一致；迭代收敛到目标显示位，无帧精度顾虑）：
    // · overrideIdx≥0（全序列下标）时本 note 该钉死音素改用 overrideDur；核时长恒按填充派生、override 对其无效。
    // · overridePreutter 非 NaN 时本 note 有效前置量改用它（拖首音素/纯前置边界反解用，转 BodyOffset），不改数据。
    // · overrideIdx2/overrideDur2：第二处时长覆盖（同 note 内 roll 需同时改被拖线两侧两个音素，反解用）。
    private static PhonemeLayoutNote BuildLayoutNote(INote note, int overrideIdx = -1, double overrideDur = 0, double overridePreutter = double.NaN, int overrideIdx2 = -1, double overrideDur2 = 0)
    {
        IReadOnlyList<SynthesizedPhoneme> leading, body;
        double bodyOffset;
        double DurOf(IPhoneme p, int globalIdx) => globalIdx == overrideIdx ? overrideDur : globalIdx == overrideIdx2 ? overrideDur2 : p.Duration.Value;

        if (note.HasPinnedPhonemes)
        {
            int lc = note.LeadingPhonemes.Count;
            var leadArr = new SynthesizedPhoneme[lc];
            for (int k = 0; k < lc; k++)
                leadArr[k] = new SynthesizedPhoneme { Symbol = note.LeadingPhonemes[k].Symbol.Value, Duration = DurOf(note.LeadingPhonemes[k], k), StretchWeight = note.LeadingPhonemes[k].StretchWeight.Value };
            int bc = note.BodyPhonemes.Count;
            var bodyArr = new SynthesizedPhoneme[bc];
            for (int k = 0; k < bc; k++)
                bodyArr[k] = new SynthesizedPhoneme { Symbol = note.BodyPhonemes[k].Symbol.Value, Duration = DurOf(note.BodyPhonemes[k], lc + k), StretchWeight = note.BodyPhonemes[k].StretchWeight.Value };
            leading = leadArr;
            body = bodyArr;
            if (double.IsNaN(overridePreutter))
                bodyOffset = note.BodyOffset.Value;                              // 常态：直取存储 BodyOffset（无 Σ 往返）
            else
            {
                double leadSum = 0;                                             // 反解：BodyOffset = Σleading(含 override) − P
                foreach (var p in leadArr) leadSum += Math.Max(0, p.Duration);
                bodyOffset = leadSum - overridePreutter;
            }
        }
        else if (note.SynthesizedSyllable is { } syl && syl.Phonemes.Count > 0)
        {
            leading = syl.LeadingPhonemes;
            body = syl.BodyPhonemes;
            bodyOffset = syl.BodyOffset;   // 合成回填（未钉死 note 不被拖拽反解，override 不适用）
        }
        else
        {
            leading = [];
            body = [];
            bodyOffset = 0;
        }

        return new PhonemeLayoutNote { FillStart = note.StartTime, FillEnd = note.ForwardFillEnd(), LeadingPhonemes = leading, BodyPhonemes = body, BodyOffset = bodyOffset };
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
    private (double Start, double End)[] ResolvedRelTimes(int overrideIdx, double overrideDur, double overridePreutter = double.NaN)
    {
        int n = PhonemeCount;
        var window = new List<PhonemeLayoutNote>();
        var prev = PrevContentNeighbor();
        var next = NextContentNeighbor();
        if (prev != null)
            window.Add(BuildLayoutNote(prev));
        int baseIndex = window.Count;   // 本 note 在窗口中的下标
        window.Add(BuildLayoutNote(this, overrideIdx, overrideDur, overridePreutter));
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

    // 有效前置量（拍前发声量）= Σ(引导音素时长) − BodyOffset：note 头到全序列首音素起点的自然距离（拖拽反解的等价旋钮）。
    // 与旧 Preutterance 标量同义（junction = noteStart + BodyOffset ⇔ 头切点在链上累积偏移 = Σleading − BodyOffset）。
    private double EffectivePreutterance()
    {
        double sum = 0;
        foreach (var p in LeadingPhonemes) sum += Math.Max(0, p.Duration.Value);
        return sum - BodyOffset.Value;
    }

    // 反解提交口：给定目标有效前置量 P，按当前引导时长换算并写回 BodyOffset = Σleading − P（须在相关时长已 Set 之后调用）。
    private void SetEffectivePreutterance(double p)
    {
        double sum = 0;
        foreach (var ph in LeadingPhonemes) sum += Math.Max(0, ph.Duration.Value);
        BodyOffset.Set(sum - p);
    }

    // 拖音素边界起手的钉死：只钉死本 note。DragPinnedBoundary 一律「改线后」——只编辑本 note 的 phoneme[index] 时长（或结合线改本 note
    // BodyOffset），从不编辑邻居数据（邻居显示的变化由布局按本 note 新几何重解出、非改其数据），故无需钉死邻居。
    // 由波形拖杆 op 在 Down 时调用（钉死须先于 mHead 快照，DiscardTo 才保钉死态）。index 保留供签名兼容、当前不再据它分派。
    public void LockPhonemesForBoundaryDrag(int index)
    {
        this.LockPhonemes();
    }

    // 拖拽音素起边界（index = 音素 index 的起点；末边界 index==n 派生不可拖）：把该边界拖到【显示】相对秒 targetRel。
    //
    // 最根本原则（2026-07-09 定）：**拖某条线，只动其线后（右侧）那个音素 phoneme[index] 的 Duration**；弹性核经 Distribute
    // 自动吸收、不写其数据。双列表模型使其比 Preutterance 版更干净——**调任何单个音素时长都不碰 BodyOffset**：
    //   · 引导反向锚 junction ⇒ 编辑引导音素只动其左侧、其后（朝主体/junction）恒不动（旧版改拍前音素才需同步 Preutterance，此处天然免除）；
    //   · 主体正向锚 junction ⇒ 编辑主体音素由弹性核吸收补偿、junction 与两侧其余不动。
    // BodyOffset 只由**结合线句柄**（junction = 主体起点，线后是弹性核、resize 无意义）单独改，见下首分支——**唯一**动 offset 的拖拽。
    // 全刚性域（无核吸收）例外：改单个音素会「全 w=0 整体等比」把改动摊给全体、灵敏度发散，改走 roll（两侧转移守恒、BodyOffset 仍不动）。
    // 改 pinned 即触发重合成。
    void DragPinnedBoundary(int index, double targetRel)
    {
        int n = PhonemeCount;
        if (n == 0 || index < 0 || index >= n)
            return;

        int lc = LeadingPhonemes.Count;
        int bc = BodyPhonemes.Count;

        // 结合线（junction = 主体起点，index == 引导数且有主体）：线后是弹性核（时长由填充派生、resize 无意义）——
        // 拖它 = 移动核起点相对拍点 = **改 BodyOffset**（引导反向 / 主体正向随 junction 一起平移，内容整体相对拍点滑动）。
        // 这是**唯一**改 BodyOffset 的拖拽。内部经等价参数 P bisect（BodyOffset = Σleading − P，Σleading 不变 ⇒ 单调），末转回 BodyOffset。
        // **解析式 clamp（防突变）**：不许把核（body[0]）整段拖到 note 头之前——否则它退出填充域、其拍后半从填满 note 末**突降**到 note 头
        // （核猛缩"跑到前面"）。故 BodyOffset ≥ −body[0].nom（留一丝拍后半、核仍跨头填充）⇔ P ≤ Σleading + body[0].nom。核起点更靠前无意义。
        if (index == lc && bc > 0)
        {
            double leadSum = 0;
            foreach (var ph in LeadingPhonemes) leadSum += Math.Max(0, ph.Duration.Value);
            double maxP = leadSum + Math.Max(0, BodyPhonemes[0].Duration.Value) - 1e-6;   // 留 1e-6 拍后半：核恒判为跨拍、填充不塌
            double p0 = EffectivePreutterance();
            double StartAt(double p) => ResolvedRelTimes(-1, 0, p)[index].Start;   // 核起点显示位（含跨拍压缩）；P↑ ⇒ junction 左移 ⇒ 起点↓（单调减）
            double span = Math.Max(EndTime - StartTime, 0.05);
            double lo = p0, hi = Math.Min(p0, maxP);
            for (int i = 0; i < 40 && StartAt(hi) > targetRel && hi < maxP; i++) hi = Math.Min(maxP, hi + span);   // 左扩（BodyOffset↓），封顶 maxP
            for (int i = 0; i < 40 && StartAt(lo) < targetRel; i++) lo -= span;                                    // 右扩（核起点右移 / lead-in 空白，Q2 不 clamp）
            for (int it = 0; it < 48; it++)
            {
                double mid = (lo + hi) / 2;
                if (StartAt(mid) <= targetRel) hi = mid; else lo = mid;
            }
            SetEffectivePreutterance(Math.Min((lo + hi) / 2, maxP));   // 转回 BodyOffset；封顶保核跨头（右向不 clamp）
            return;
        }

        // 改线后音素（**BodyOffset 全程不动**）：编辑被拖线**线后**那个音素 phoneme[index] 的 Duration，bisect 令其显示起点落 targetRel。
        // 引导反向锚 junction ⇒ 编辑引导音素只动其左侧、其后（朝主体）恒不动；主体则由弹性核吸收 ⇒ 其后由核补偿。junction、两侧其余音素均不动。
        // 不钳 targetRel 到「当前末端」——过满时该末端被压到≈起点会误钳、导致往右拖不动无法还原；正长由 bisect 区间 [0,·]（时长≥0）自然保证。
        double oldDur = Math.Max(0, Phonemes[index].Duration.Value);
        double StartOf(double dur) => ResolvedRelTimes(index, dur)[index].Start;   // dur↑ ⇒ 起点↓（引导反向长 / 主体被核吸收左推），单调减

        // 被拖的刚性音素（辅音）在域有弹性松弛时显示长恒 = nominal（1:1 跟手、其后不动）；核让到 0（过满）后再增 nominal 会被二级压缩
        // （显示长 < nominal），同域其它音素被等比压走→突变。故分两态：
        //  · 有松弛（当前未被压缩）：**精确 clamp** 在「被拖音素仍未被压缩」的最大 nominal 处（= 核耗尽点）——填满松弛量为止、其后恒不动、无突变。
        //  · 已过满（当前已被压缩，如短音符塞多辅音）：无松弛可锁，退化为普通二级压缩反解（WYSIWYG，邻辅音随之等比压——过满时不可避免），
        //    但用「翻倍移动 < 0.1ms 即停」限幅防 nominal 发散/畸大。既不锁死（可继续调整/退回），也不畸变。超此极限想更靠左应改拖结合线（BodyOffset）。
        double DispLen(double dur) { var t = ResolvedRelTimes(index, dur)[index]; return t.End - t.Start; }
        bool NotCompressed(double dur) => dur <= 1e-9 || DispLen(dur) >= dur - 1e-6;
        double loD = 0, hiD = Math.Max(oldDur, 0.05);
        if (NotCompressed(oldDur))
        {
            // 有松弛：向上找上界——**一到目标可达即停**（勿一路翻倍到 2^N 巨值，否则末次 bisect 区间过大、精度退化成整数倍跳跃）；
            // 若在够到目标前先撞压缩临界（核耗尽），则 clamp 到临界（自然态最大 nominal、其后不动无突变）。
            for (int i = 0; i < 40 && NotCompressed(hiD) && StartOf(hiD) > targetRel; i++) hiD *= 2;
            if (!NotCompressed(hiD))   // 先撞压缩临界 → 二分 cap（精确落在核耗尽处）
            {
                double lo = Math.Max(oldDur, 0.05), hi = hiD;
                for (int it = 0; it < 40; it++) { double mid = (lo + hi) / 2; if (NotCompressed(mid)) lo = mid; else hi = mid; }
                hiD = lo;
            }
            // else：目标在压缩前即可达 → hiD 是紧上界（~2× 所需），直接进最终 bisect（精度足）。
        }
        else
        {
            // 已过满（无松弛可锁）：普通反解 + 限幅（防发散），不锁死。同样一到目标可达即停。
            for (int i = 0; i < 40 && StartOf(hiD) > targetRel; i++)
            {
                double next = hiD * 2;
                if (StartOf(hiD) - StartOf(next) < 1e-4) break;   // 移动 < 0.1ms/翻倍：接近极限、停
                hiD = next;
            }
        }
        for (int it = 0; it < 40; it++)   // 在 [0, hiD] 内解 targetRel；超出可达则收敛到 hiD（clamp）
        {
            double mid = (loD + hiD) / 2;
            if (StartOf(mid) <= targetRel) hiD = mid; else loD = mid;
        }
        Phonemes[index].Duration.Set(hiD);
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
        if (note.HasPinnedPhonemes)
            return;

        if (note.SynthesizedSyllable is not { } syllable || syllable.Phonemes.Count == 0)
            return;

        // 锁定 = 把合成产物固定为用户数据：按引导 / 主体双列表分别存各音素的【时长 + 权重】（位置由布局派生、不存），
        // 并把合成 BodyOffset 固化为 note.BodyOffset（引导 / 主体归属即列表成员，故无需每音素标志）。
        // 辅音(w=0)时长即固定长；核(w>0)时长记录但布局忽略（恒按填充派生），故无需「反压缩」——
        // 显示侧 PhonemeLayout 会按当前邻居重新去重叠（核重新填充并再让位），与合成同源、常态下不双重压缩。
        note.BodyOffset.Set(syllable.BodyOffset);
        static Phoneme Materialize(SynthesizedPhoneme p) => Phoneme.Create(new PhonemeInfo
        {
            Symbol = p.Symbol,
            Duration = Math.Max(0, p.Duration),
            StretchWeight = p.StretchWeight,
        });
        foreach (var p in syllable.LeadingPhonemes)
            note.LeadingPhonemes.Add(Materialize(p));
        foreach (var p in syllable.BodyPhonemes)
            note.BodyPhonemes.Add(Materialize(p));
    }

    // 清除锁定（钉死）音素：清空两列表后该 note 回到合成音素口径（与改歌词 / 改发音时的清空同源）。
    public static void ClearLockedPhonemes(this INote note)
    {
        note.LeadingPhonemes.Clear();
        note.BodyPhonemes.Clear();
    }

    // 全序列下标 → 所属列表 + 列表内局部下标（引导在前、主体在后）。用于按显示位增删 / 定位既有音素（改音素本体走 Phonemes 视图即可）。
    public static (IDataObjectList<IPhoneme> List, int Local) LocatePhoneme(this INote note, int globalIndex)
    {
        int lc = note.LeadingPhonemes.Count;
        return globalIndex < lc ? (note.LeadingPhonemes, globalIndex) : (note.BodyPhonemes, globalIndex - lc);
    }

    public static string? FinalPronunciation(this INote note)
    {
        if (!string.IsNullOrEmpty(note.Pronunciation.Value))
            return note.Pronunciation.Value;

        return note.Pronunciations.FirstOrDefault();
    }
}
