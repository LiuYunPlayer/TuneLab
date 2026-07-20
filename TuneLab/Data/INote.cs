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
    new INote? Previous { get; }
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
    // 精度：常态显示 / 音频路径（无覆盖）直取存储 BodyOffset 喂布局（offset=0 恒等精确）。拖拽反解经覆盖集试算、不改数据：
    // · durOverrides：按全序列下标覆盖标称时长（NaN = 不覆盖），供统一域模型的多点写入（线后 + 被吸收弹性 + 相 B 刚性）；
    // · bodyOffsetOverride：直接覆盖 BodyOffset（NaN = 不覆盖），供"平移越 junction 量"试算。
    private static PhonemeLayoutNote BuildLayoutNote(INote note, double[]? durOverrides = null, double bodyOffsetOverride = double.NaN)
    {
        IReadOnlyList<SynthesizedPhoneme> leading, body;
        double bodyOffset;
        double DurOf(IPhoneme p, int globalIdx) =>
            durOverrides != null && globalIdx < durOverrides.Length && !double.IsNaN(durOverrides[globalIdx]) ? durOverrides[globalIdx] : p.Duration.Value;

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
            bodyOffset = double.IsNaN(bodyOffsetOverride) ? note.BodyOffset.Value : bodyOffsetOverride;
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
        INote? p = Previous;
        while (p != null && p.IsEffectiveContinuation())
            p = p.Previous;
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
        INote? prev = Previous;
        while (prev != null && prev.IsEffectiveContinuation())
        {
            reachStart = prev.StartTime;
            prev = prev.Previous;
        }
        return prev != null && !HasPhonemeContent(prev) && prev.EndTime >= reachStart;
    }

    // 3-note 窗口（前/后最近**有内容**邻居 + 本，含合成邻居）喂全局布局，返回本 note 与前内容邻居的解析边界
    // （相对秒，均以**本 note 头**为原点；前邻居值多为负）。相邻 note 间边界相互独立，故 3-note 窗口足以正确
    // 解析本 note 两条边界；前邻居时序供拖拽反解读取前域各部分的显示长/容量。覆盖集试算不改数据：
    // selfDurs/prevDurs 按全序列下标覆盖标称（NaN 不覆盖）、selfBodyOffset 覆盖本 note BodyOffset（NaN 不覆盖）。
    private ((double Start, double End)[] Self, (double Start, double End)[] Prev) ResolveWindowRel(double[]? selfDurs = null, double selfBodyOffset = double.NaN, double[]? prevDurs = null)
    {
        var window = new List<PhonemeLayoutNote>();
        var prev = PrevContentNeighbor();
        var next = NextContentNeighbor();
        int prevIndex = -1;
        if (prev != null)
        {
            prevIndex = window.Count;
            window.Add(BuildLayoutNote(prev, prevDurs));
        }
        int baseIndex = window.Count;   // 本 note 在窗口中的下标
        window.Add(BuildLayoutNote(this, selfDurs, selfBodyOffset));
        if (next != null)
            window.Add(BuildLayoutNote(next));

        var resolved = PhonemeLayout.Resolve(window);
        double origin = StartTime;
        return (ToRel(resolved[baseIndex], origin), prevIndex >= 0 ? ToRel(resolved[prevIndex], origin) : []);

        static (double Start, double End)[] ToRel(PhonemeTiming[] times, double origin)
        {
            var result = new (double Start, double End)[times.Length];
            for (int i = 0; i < times.Length; i++)
                result[i] = (times[i].Start - origin, times[i].End - origin);
            return result;
        }
    }

    // 拖音素边界起手的钉死（统一域模型 v4）：拖拽写入可触及"归属 note 的域 + 前域"内一切参与方——左拖的弹性
    // 消解份额立即落到前域弹性上，故**前内容邻居相接即钉**（不再等跨头）；线基线在本域（拍后）时，后内容邻居的
    // 拍前料共享本域空间，一并钉死冻结域几何（拖完不因其重合成而漂移）。相接才共域，有空隙不钉。
    // 由波形拖杆 op 在 Down 时调用（钉死须先于 mHead 快照，DiscardTo 才保钉死态）。
    public void LockPhonemesForBoundaryDrag(int index)
    {
        this.LockPhonemes();
        var display = DisplayPhonemes;
        if (index >= display.Count)
            return;   // 末边界（index==n）派生不可拖，不钉邻居
        if (PrevContentNeighbor() is { } prev && prev.ForwardFillEnd() >= StartTime)
            prev.LockPhonemes();
        if (display[index].StartTime >= StartTime && NextContentNeighbor() is { } next && this.ForwardFillEnd() >= next.StartTime)
            next.LockPhonemes();
    }

    // 拖拽音素起边界（index = phoneme[index] 起点这条"线"；末边界 index==n 派生不可拖）：把线拖到【显示】相对秒 targetRel。
    //
    // 统一域模型 v4（2026-07-14 定稿）：**刚性平移、弹性吸收、如实入账**。
    // 作用域 = 线后音素**归属** note 的域 + 前域（归属看列表成员、不看时间位置；作用域整场拖拽恒定，
    // 域边界跨越无感，junction 不是边界、随材料平移）。显示语义（左移 p>0，右移镜像）：
    //   · 线后音素显示 +p，其末端与其后一切冻结；
    //   · 线与被消解弹性之间的刚性**整体平移**（长度不变、不写标称）；
    //   · 线前作用域内的弹性核按 ∝(w·显示长) 分担吸收 p（触底出局水填；标称地板 1ms 防乘法奇点 d·r^w≡0 弹不回），
    //     **按 junction 分两梯队**：近侧（线到 junction 间）先吸收、耗尽才轮到越 junction 侧——junction 是有音乐
    //     意义的锚，近侧有弹性可吸收时不被推动（右移镜像同理：近侧弹性优先回胀）；
    //   · 弹性全触底才进相 B：刚性**均分收缩**（水填至 0）。
    // 写入 = 如实入账（每笔标称改动 = 该部分真实显示变化按其压缩因子 disp/nom 折算，故总标称在同权情形守恒、
    // 一般情形有界可逆）：线后 nominal += p/因子；被吸收弹性 / 相 B 刚性 nominal −= 份额/因子；只平移的不写；
    // **平移越过 junction 的量写进 BodyOffset**（= junction 左侧参与方份额合计，仅 body 侧线 index≥lc——
    // 引导线反向锚天然平移不写 offset）。结合线（index==lc）不再特例：它就是"左侧全为 junction 左侧参与方"的一般线。
    // 钳制：右 = 线后音素显示归 0（末端冻结处；首音素额外不越 note 头——本域从头起排的打包结构极限，
    // 整簇已在头右侧时右向原地）；左 = 前域起点（打包极限）与作用域消解容量耗尽二者先到为准。
    // 求解：鼠标 → 目标显示位 → 对 p 在**有界闭区间**一次 bisect（writes(p) 确定性水填、显示位单调减），
    // 无扩界循环、无相位探测缝；平区收敛取"改动最小端"（不写无视觉变化的数据）。
    // 前 note 引导跨自身头的拍后半反向锚定、收缩会动错端（编码歧义），容量按 0 处理。改 pinned 即触发重合成。
    void DragPinnedBoundary(int index, double targetRel)
    {
        int n = PhonemeCount;
        if (n == 0 || index < 0 || index >= n)
            return;

        int lc = LeadingPhonemes.Count;
        var phonemes = Phonemes;
        var (baseline, prevBaseline) = ResolveWindowRel();
        var prev = PrevContentNeighbor();
        bool prevConnected = prev != null && prev.ForwardFillEnd() >= StartTime;
        var prevPhonemes = prevConnected && prev!.HasPinnedPhonemes ? prev.Phonemes : null;
        double prevHeadRel = prevConnected ? prev!.StartTime - StartTime : double.NegativeInfinity;

        // —— 参与方表：线前作用域内各部分（own 全序列 [0, index) + 前 note 的前域/拍后部分）。 ——
        const double NominalFloor = 0.001;   // 弹性标称地板（1ms）
        var parties = new List<(bool IsPrev, int Idx, double W, double Disp, double Nom, double Factor, double Cap, bool LeftOfJunction)>();
        for (int i = 0; i < index; i++)
        {
            double nom = Math.Max(0, phonemes[i].Duration.Value);
            double disp = Math.Max(0, baseline[i].End - baseline[i].Start);
            double w = phonemes[i].StretchWeight.Value;
            double factor = nom > 1e-9 && disp > 1e-9 ? disp / nom : 1;
            double cap = w > 0 ? Math.Max(0, disp - NominalFloor * factor) : disp;
            parties.Add((false, i, w, disp, nom, factor, cap, i < lc));
        }
        if (prevPhonemes != null)
        {
            int plc = prev!.LeadingPhonemes.Count;
            for (int j = 0; j < prevPhonemes.Count && j < prevBaseline.Length; j++)
            {
                double start = prevBaseline[j].Start, end = prevBaseline[j].End;
                double dispInScope = Math.Max(0, end - Math.Max(start, prevHeadRel));   // 只算前域部分；前 note 拍前料在更前域、域外不动
                if (dispInScope <= 1e-9)
                    continue;
                if (j < plc && start < prevHeadRel)
                    continue;   // 前 note 引导跨自身头：容量 0（见方法注释）
                double nom = Math.Max(0, prevPhonemes[j].Duration.Value);
                double w = prevPhonemes[j].StretchWeight.Value;
                double dispWhole = Math.Max(0, end - start);
                double factor = nom > 1e-9 && dispWhole > 1e-9 ? dispWhole / nom : 1;
                double cap = w > 0 ? Math.Max(0, dispInScope - NominalFloor * factor) : dispInScope;
                parties.Add((true, j, w, dispInScope, nom, factor, cap, true));
            }
        }

        double cur = baseline[index].Start;
        double totalCap = 0;
        foreach (var pt in parties) totalCap += pt.Cap;

        // —— 钳制 ——
        // 前无相接邻居时拍前是**自由堆叠空间**（无共域、无人付账）：junction 及其左侧的线向左自由生长/平移（左向无界），
        // 引导线经反向锚自动平移、junction 平移量走 BodyOffset；右移先免费回撤拍前堆叠（上限 prevGainCap）。
        bool freeSpace = !prevConnected && index <= lc;
        double leftLimit = freeSpace ? double.NegativeInfinity : Math.Max(prevHeadRel, cur - totalCap);
        double rightLimit = baseline[index].End;
        if (index == 0)
            rightLimit = Math.Min(rightLimit, Math.Max(cur, 0));
        double target = Math.Clamp(targetRel, Math.Min(leftLimit, cur), Math.Max(rightLimit, cur));
        if (Math.Abs(target - cur) < 1e-9)
            return;

        double idxNom = Math.Max(0, phonemes[index].Duration.Value);
        double idxDisp = Math.Max(0, baseline[index].End - baseline[index].Start);
        double idxFactor = idxNom > 1e-9 && idxDisp > 1e-9 ? idxDisp / idxNom : 1;

        // 右移时 prev 侧增益的封顶 = 我方拍前显示总量（线及其前）：前域池只在我方拍前材料回撤时变大，
        // 无回撤则前域弹性显示不动、无从入账（把增益记给它=饿死有效通道+暗账）。
        double prevGainCap = 0;
        for (int i = 0; i <= index; i++)
            prevGainCap += Math.Max(0, Math.Min(baseline[i].End, 0) - baseline[i].Start);

        // 跨拍锚 = **最后一个显示起点在拍前的音素**（不假设是 body[0]——全 body 分类下可以是任意音素；
        // 显示末恰好贴头的 class-0 音素也是锚——它的重分类阈值同样存在，漏掉它会走退路的跳变路径，2026-07-14 实测悬崖）。
        // BodyOffset 的重分类阈值必须锚定它：其自然前半随意图显示前半等比缩放，重分类恰与显示到达拍点同步
        // （否则前方标称增益会把它的自然起点先推过 0 → 前半提前消失 → 线吸附跳变）。
        int straddler = -1;
        double fDisp0 = 0, fNat0 = 0;
        for (int i = n - 1; i >= 0; i--)
            if (baseline[i].Start < -1e-9) { straddler = i; break; }
        if (straddler >= 0)
        {
            fDisp0 = -baseline[straddler].Start;
            double natStart0 = BodyOffset.Value;
            if (straddler >= lc)
                for (int k = lc; k < straddler; k++) natStart0 += Math.Max(0, phonemes[k].Duration.Value);
            else
                for (int k = straddler; k < lc; k++) natStart0 -= Math.Max(0, phonemes[k].Duration.Value);
            fNat0 = -natStart0;
            if (fNat0 <= 1e-6)
                fNat0 = fDisp0;   // 退化编码（标称前半≈0 而显示前半为实，如恰压 Epsilon 阈值的历史数据）：
                                  // 重标定为 1:1——独核前半显示=池、与标称无关，重标定显示不变，拖动中渐进修复数据。
        }

        // 全刚性拉伸态（作用域参与方全 w=0 且因子 > 1 ⇔ 前域欠满、无弹性显示、Distribute 等比拉伸填满）：
        // 跨拍者向左进入此域是布局模型的不连续点——ε 宽前半作为唯一弹性会瞬间接管全部拉伸富余。
        // 左向改走**烘焙帧**：刚性按**显示长**入账（显示不变的重标定，把拉伸富余记进标称），前半自 0 宽
        // 1:1 起步，(−富余, 0) 缺口被连续桥接（2026-07-15 用户真实工程复现定位）。
        bool stretchedRigid = parties.Count > 0;
        foreach (var pt in parties)
            if (pt.W > 0 || pt.Factor <= 1 + 1e-6) { stretchedRigid = false; break; }

        // —— writes(p)：确定性分配（p>0 左移挤压 / p<0 右移释放），生成覆盖集；返回 BodyOffset 覆盖（NaN=不覆盖）。 ——
        var selfDurs = new double[n];
        var prevDurs = prevPhonemes != null ? new double[prevPhonemes.Count] : null;
        var deltas = new double[parties.Count];   // 各参与方显示份额（左移损失为正、右移增益为负）
        double Build(double p)
        {
            Array.Fill(selfDurs, double.NaN);
            if (prevDurs != null) Array.Fill(prevDurs, double.NaN);
            Array.Clear(deltas);
            bool baked = stretchedRigid && p > 0 && index >= lc;   // 烘焙帧（见 stretchedRigid 注释）
            double freeShift = 0;   // 自由平移量（不入账；junction 线经 BodyOffset 落笔、引导线反向锚自动）
            if (p > 0 && freeSpace)
            {
                freeShift = p;   // 向左自由生长/平移，无人付账
            }
            else if (p > 0)
            {
                // 弹性 ∝ w·disp 水填，**按 junction 分两梯队**：近侧（线到 junction 之间的弹性）先吸收——
                // junction 是有音乐意义的锚（元音起点），近侧还有弹性可吸收时不被推动；近侧耗尽才轮到
                // 越 junction 一侧（引导 / 前 note 弹性），junction 自此才开始平移（2026-07-15 用户裁定）。
                double WaterFill(List<int> active, double amount)
                {
                    while (amount > 1e-12 && active.Count > 0)
                    {
                        double denom = 0;
                        foreach (int k in active) denom += parties[k].W * parties[k].Disp;
                        if (denom <= 1e-12) break;
                        bool anyCapped = false;
                        for (int m = active.Count - 1; m >= 0; m--)
                        {
                            int k = active[m];
                            double room = parties[k].Cap - deltas[k];
                            if (amount * parties[k].W * parties[k].Disp / denom >= room)
                            {
                                deltas[k] += room; amount -= room; active.RemoveAt(m); anyCapped = true;
                            }
                        }
                        if (!anyCapped)
                        {
                            foreach (int k in active) deltas[k] += amount * parties[k].W * parties[k].Disp / denom;
                            amount = 0;
                        }
                    }
                    return amount;
                }
                var nearSide = new List<int>();
                var farSide = new List<int>();
                for (int k = 0; k < parties.Count; k++)
                    if (parties[k].W > 0 && parties[k].Cap > 1e-12)
                        (parties[k].LeftOfJunction ? farSide : nearSide).Add(k);
                double remaining = WaterFill(nearSide, p);
                remaining = WaterFill(farSide, remaining);
                // 相 B：残余 → 刚性均分水填
                if (remaining > 1e-12)
                {
                    var rigids = new List<int>();
                    for (int k = 0; k < parties.Count; k++)
                        if (parties[k].W <= 0 && parties[k].Cap > 1e-12) rigids.Add(k);
                    while (remaining > 1e-12 && rigids.Count > 0)
                    {
                        double share = remaining / rigids.Count;
                        bool anyCapped = false;
                        for (int m = rigids.Count - 1; m >= 0; m--)
                        {
                            int k = rigids[m];
                            double room = parties[k].Cap - deltas[k];
                            if (share >= room)
                            {
                                deltas[k] += room; remaining -= room; rigids.RemoveAt(m); anyCapped = true;
                            }
                        }
                        if (!anyCapped)
                        {
                            foreach (int k in rigids) deltas[k] += share;
                            remaining = 0;
                        }
                    }
                }
            }
            else if (p < 0 && freeSpace)
            {
                // 右移：先免费回撤拍前堆叠（平移回来，上限 prevGainCap）；回撤尽（满打包）后转 own 刚性均分增益
                // （junction 线经 BodyOffset 耦合右伸吃核；引导线 = 与线后的刚性守恒 roll）。
                double retreat = Math.Min(-p, prevGainCap);
                freeShift = -retreat;
                double remaining = -p - retreat;
                if (remaining > 1e-12)   // 回撤尽（满打包）后：own 刚性均分增益（junction 线经 bo 耦合右伸吃核；引导线 = 与线后守恒 roll）
                {
                    int ownRigidCount = 0;
                    foreach (var pt in parties) if (!pt.IsPrev && pt.W <= 0) ownRigidCount++;
                    if (ownRigidCount > 0)
                        for (int k = 0; k < parties.Count; k++)
                            if (!parties[k].IsPrev && parties[k].W <= 0) deltas[k] -= remaining / ownRigidCount;
                }
            }
            else if (p < 0)
            {
                // 右移镜像（同样按 junction 分梯队）：**近侧弹性优先受益**（junction 不被拉动）；近侧无弹性才轮到
                // 越 junction 通道——own 远侧弹性经本域池受益、prev 侧共享"可回撤量"封顶（prevGainCap），溢出转 own；
                // 弹性无处安放的残余 → own 刚性均分增益（引导经 BodyOffset 耦合右伸、body 刚性经池右伸；
                // prev 刚性无有效显示机制、不参与）。
                double gain = -p;
                double nearDenom = 0;
                foreach (var pt in parties)
                    if (pt.W > 0 && !pt.LeftOfJunction) nearDenom += pt.W * pt.Disp;
                double remaining = gain;
                if (nearDenom > 1e-12)
                {
                    for (int k = 0; k < parties.Count; k++)
                        if (parties[k].W > 0 && !parties[k].LeftOfJunction)
                            deltas[k] = p * parties[k].W * parties[k].Disp / nearDenom;
                    remaining = 0;
                }
                else
                {
                    double ownDenom = 0, prevDenom = 0;
                    foreach (var pt in parties)
                    {
                        if (pt.W <= 0) continue;
                        if (pt.IsPrev) prevDenom += pt.W * pt.Disp; else ownDenom += pt.W * pt.Disp;
                    }
                    if (ownDenom + prevDenom > 1e-12)
                    {
                        double prevShare = gain * prevDenom / (ownDenom + prevDenom);
                        if (prevShare > prevGainCap) prevShare = prevGainCap;
                        double ownShare = ownDenom > 1e-12 ? gain - prevShare : 0;
                        for (int k = 0; k < parties.Count; k++)
                        {
                            var pt = parties[k];
                            if (pt.W <= 0) continue;
                            deltas[k] = pt.IsPrev
                                ? (prevDenom > 1e-12 ? -prevShare * pt.W * pt.Disp / prevDenom : 0)
                                : -ownShare * pt.W * pt.Disp / ownDenom;
                        }
                        remaining = gain - prevShare - ownShare;
                    }
                }
                if (remaining > 1e-12)
                {
                    // 刚性均分增益（own + prev，镜像左移相 B）：刚性标称增长 = 显示增长（一级态 disp=nom），
                    // 与线后收缩构成守恒 roll（同 note 引导对 / 跨 note 对均可：线前 +δ / 线后 −δ、刚性总量不变
                    // ⇒ 池内弹性显示冻结、线 1:1 右移）；未压缩/自由堆叠情形此通道平坦，由可达性护栏兜底不写数据。
                    int rigidCount = 0;
                    foreach (var pt in parties) if (pt.W <= 0) rigidCount++;
                    if (rigidCount > 0)
                        for (int k = 0; k < parties.Count; k++)
                            if (parties[k].W <= 0) deltas[k] -= remaining / rigidCount;
                }
            }

            double leftShare = freeShift;        // junction 左侧份额（跨拍者缺席时的退路换算用）
            double straddlerShift = freeShift;   // 跨拍者左侧份额 = 其显示前半的意图变化量
            for (int k = 0; k < parties.Count; k++)
            {
                if (deltas[k] == 0) continue;
                var pt = parties[k];
                double newNom = baked
                    ? Math.Max(0, pt.Disp - deltas[k])   // 烘焙帧：按显示长入账（拉伸富余记进标称）
                    : Math.Max(pt.W > 0 ? NominalFloor : 0, pt.Nom - deltas[k] / pt.Factor);
                if (pt.IsPrev) prevDurs![pt.Idx] = newNom; else selfDurs[pt.Idx] = newNom;
                if (pt.LeftOfJunction) leftShare += deltas[k];
                if (straddler >= 0 && (pt.IsPrev || pt.Idx < straddler)) straddlerShift += deltas[k];
            }
            selfDurs[index] = Math.Max(0, idxNom + p / idxFactor);   // 线后如实入账
            if (index < lc)
                return double.NaN;   // 引导线反向锚天然平移，不动 offset
            if (baked)
                return -leftShare;   // 烘焙帧：前半自 0 宽 1:1 起步（刚性已按显示长入账、富余归零）

            // BodyOffset 落笔：锚定跨拍者——其自然前半按（意图显示前半 / 基线显示前半）等比缩放，恰在显示
            // 到达拍点时归零（重分类与显示同步、无吸附跳变）；已过头则拍后 1:1。再经锚定链（body 用其前 body
            // 标称、leading 用其后 leading 标称，取**覆盖后**值）换算回 BodyOffset。
            double NomEff(int k) => !double.IsNaN(selfDurs[k]) ? selfDurs[k] : Math.Max(0, phonemes[k].Duration.Value);
            if (straddler >= 0)
            {
                if (Math.Abs(straddlerShift) <= 1e-12)
                    return double.NaN;
                double fIntended = fDisp0 + straddlerShift;
                double natStart = fIntended > 0
                    ? -(fNat0 * (fDisp0 > 1e-9 ? fIntended / fDisp0 : 1))
                    : -fIntended;
                double bo = natStart;
                if (straddler >= lc)
                    for (int k = lc; k < straddler; k++) bo -= NomEff(k);
                else
                    for (int k = straddler; k < lc; k++) bo += NomEff(k);
                return bo;
            }

            // 无跨拍锚（内容全在拍后）：junction 按左侧份额**增量** 1:1 平移（绝不绝对赋值——bo 与显示可能
            // 有历史差量，绝对赋值会瞬间跳变，2026-07-14 实测悬崖）。
            if (Math.Abs(leftShare) <= 1e-12)
                return double.NaN;
            return BodyOffset.Value - leftShare;
        }
        double lastBodyOffset = double.NaN;
        double LinePos(double p)
        {
            lastBodyOffset = Build(p);
            return ResolveWindowRel(selfDurs, lastBodyOffset, prevDurs).Self[index].Start;
        }

        // —— 方向性推进 + 悬崖防护：从 p=0 向目标方向按二次密度分步推进，逐步校验"显示位移与推进量同量级"。
        // 模型的不连续点（重分类悬崖等）表现为步间跳变——线**止步崖前**、绝不落笔"越崖"的写入（吸附/清空类
        // bug 的总闸，不依赖悬崖成因）；平坦不可达段自然停在原地不写数据。崖前/目标区间内再 bisect 精确钉线。 ——
        double pMax = target < cur
            ? (freeSpace ? Math.Max(totalCap, cur - target) + 0.05 : totalCap)
            : -(idxDisp + 0.05);
        const int ScanSteps = 32;
        double pGood = 0, lGood = cur, pFar = 0;
        double pFinal = double.NaN;
        bool crossed = false;
        int step = 1, refines = 0;
        while (step <= ScanSteps)
        {
            double t = (double)step / ScanSteps;
            double pi = pMax * t * t;   // 近端更细
            double li = LinePos(pi);
            if (Math.Abs(li - lGood) > Math.Abs(pi - pGood) * 4 + 0.004)
            {
                // 疑似悬崖：二分逼近突变点，再看残余落差——落差消失 = 连续陡坡（从彼侧续推），仍在 = 真悬崖（止步崖前）。
                double preGood = pGood, preL = lGood;
                double lo = pGood, hi = pi;
                for (int it = 0; it < 30; it++)
                {
                    double mid = (lo + hi) / 2;
                    if (Math.Abs(LinePos(mid) - preL) <= Math.Abs(mid - preGood) * 4 + 0.004) lo = mid; else hi = mid;
                }
                double lHi = LinePos(hi);
                if (Math.Abs(lHi - LinePos(lo)) > 0.004)
                {
                    pFinal = lo;   // 真悬崖：止步崖前
                    break;
                }
                if (target < cur ? lHi <= target : lHi >= target)
                {
                    pGood = preGood; lGood = preL; pFar = hi; crossed = true;   // 陡坡内已越目标：括住后钉线
                    break;
                }
                pGood = hi; lGood = lHi;
                if (++refines > 8) { pFinal = pGood; break; }   // 防陡坡链拖慢：够多次就停在可达处
                continue;   // 重试当前步
            }
            if (target < cur ? li <= target : li >= target)
            {
                pFar = pi; crossed = true;
                break;
            }
            pGood = pi; lGood = li;
            step++;
        }

        if (double.IsNaN(pFinal))
        {
            if (crossed)
            {
                // 正常段：在 [pGood, pFar] 内 bisect 钉线；平区收敛到改动最小端
                double lo = pGood, hi = pFar;
                for (int it = 0; it < 40; it++)
                {
                    double mid = (lo + hi) / 2;
                    bool ok = target < cur ? LinePos(mid) <= target : LinePos(mid) >= target;
                    if (ok) hi = mid; else lo = mid;
                }
                pFinal = hi;
            }
            else
            {
                pFinal = pGood;   // 目标不可达（平坦到界）：停在实际可达处
            }
        }
        if (Math.Abs(LinePos(pFinal) - cur) < 1e-9)
            return;   // 原地/平区：无视觉变化不写数据（此调用已把 pFinal 覆盖集留在 selfDurs/prevDurs/lastBodyOffset）

        // —— 落笔 ——
        for (int i = 0; i < n; i++)
            if (!double.IsNaN(selfDurs[i])) phonemes[i].Duration.Set(selfDurs[i]);
        if (!double.IsNaN(lastBodyOffset)) BodyOffset.Set(lastBodyOffset);
        if (prevDurs != null && prevPhonemes != null)
            for (int j = 0; j < prevDurs.Length; j++)
                if (!double.IsNaN(prevDurs[j])) prevPhonemes[j].Duration.Set(prevDurs[j]);
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
