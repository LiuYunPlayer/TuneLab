using System;
using System.Collections.Generic;

namespace TuneLab.SDK;

// 一个 note 的音素布局输入：几何锚点 + 该 note 的音素描述符（顺序：前置辅音 → 核 → 后辅音，与 IsLead 前缀一致）。
// 音素位置不传——由布局按时长模型从「核起点 + 标称时长 + IsLead + 权重」派生。
public readonly struct PhonemeLayoutNote
{
    // 填充区间左界 = 核起点 = 音符头（绝对秒）。核从这里往右填充、前置音素(IsLead)从这里往左累积（可越界到 note 之前）。
    public double FillStart { get; init; }

    // 填充区间右界 = 核填充终点（绝对秒）= 自己的末 + 仅铺过延续乘客（IsContinuation）的 melisma。核(w>0)（以标称时长为原长、余量按权重分摊）共同填到这里、后辅音(w=0)用其标称时长。
    // 由调用方按自己的数据模型算（宿主走 continuation 前向铺末；引擎走延续跳过逻辑）——布局数学不掺和。
    // 这是 WYSIWYG 口径：要音频 == 宿主显示，FillEnd 须与宿主同口径——自己有效末 + 仅延续乘客 melisma；
    // **真发声 note 间的空隙停在自己末、不把元音铺过空隙到下一发声 note**（空隙是静音）。
    // 用 Resolve 驱动音频帧时 FillEnd 直接塑造音频：偏离此口径（如填过空隙）→ 音频与显示分叉、且**听得见**，
    // 不是"显示错位非致命"那条 escape hatch（那条只对纯显示成立）。
    public double FillEnd { get; init; }

    // 本 note 的音素，顺序 = 前置辅音 → 核 → 后辅音。布局只读其 Duration / StretchWeight / IsLead。
    public IReadOnlyList<SynthesizedPhoneme> Phonemes { get; init; }
}

// 一个音素去重叠后的真实时间区间（绝对秒）。Resolve 的返回元素。
public readonly struct PhonemeTiming
{
    public double Start { get; }
    public double End { get; }
    public double Duration => End - Start;

    public PhonemeTiming(double start, double end)
    {
        Start = start;
        End = end;
    }

    public void Deconstruct(out double start, out double end)
    {
        start = Start;
        end = End;
    }

    public override string ToString() => $"[{Start:F3}..{End:F3}]";
}

// 音素布局（合成域共享纯函数）：把每 note 的标称时长 + 几何锚点解析为跨 note 去重叠压缩后的真实绝对时序。
// 确定性、无状态、无副作用——可在合成的同步前缀（数据线程）直接调。
//
// 此前布局算法不进 SDK（顾虑"塞进 ABI 逼插件版本对齐"）；现冻结的只是 I/O 形状（数据契约），压缩内部逻辑仍
// 可宿主侧自由演进——插件运行时绑定宿主的这一份，故随宿主算法演进永不漂移（音频 == 显示，WYSIWYG）。想自定义
// 音频摆放（交叉淡入等）的引擎可不调，仍按 SynthesizedPhoneme 自由放置；调用方也可不传整段、对任意连续 note 区间成立。
//
// 派生（每 note，自然几何）：
//   · 核起点 = FillStart；IsLead 音素从核起点往左依次累积各自标称时长（可越界到 note 之前）；
//   · 核(w>0)以标称时长为原长、填充剩余空间到 FillEnd——可用余量(空间−各核原长和)按权重分摊到各核（单核时原长被抵消、退化为整段填充）；
//     后辅音(w=0)用其标称时长。
// 再做跨 note 去重叠：
//   · 只在相接 / 重叠的 note 边界压缩，左锚 = 前 note 首核起点（音符头，恒不动）、右锚 = 后 note 核起点（核不压不动）；
//   · ① 核(w>0)先让位（跨度内全部核按各自权重分摊收缩、各到 0）；② 仍超则辅音簇按标称长等比压；退化反序就地塌缩 + 单调钳制兜底。
//   · note 间有空隙时两侧互不影响（前置辅音可自然探入空隙、不被推挤）。
public static class PhonemeLayout
{
    // 标称时长 + note 几何 → 跨 note 去重叠后的真实绝对时序。
    // 返回与输入同构的交错数组：notes[i].Phonemes[j] 的真实落点 = result[i][j]。
    public static PhonemeTiming[][] Resolve(IReadOnlyList<PhonemeLayoutNote> notes)
    {
        var segments = new List<Segment>();
        var offsets = new int[notes.Count];
        for (int i = 0; i < notes.Count; i++)
        {
            offsets[i] = segments.Count;
            var phonemes = notes[i].Phonemes;
            int count = phonemes?.Count ?? 0;
            if (count == 0)
                continue;

            var bound = NaturalBoundaries(notes[i]);
            for (int k = 0; k < count; k++)
                segments.Add(new Segment(i, phonemes![k].StretchWeight, bound[k], bound[k + 1]));
        }

        var resolved = ResolveSegments(segments);

        var result = new PhonemeTiming[notes.Count][];
        for (int i = 0; i < notes.Count; i++)
        {
            int count = notes[i].Phonemes?.Count ?? 0;
            var times = new PhonemeTiming[count];
            for (int k = 0; k < count; k++)
                times[k] = resolved[offsets[i] + k];
            result[i] = times;
        }
        return result;
    }

    // 由「时长 + 权重 + IsLead」派生单 note 的自然绝对秒边界（n+1 个，未经跨 note 压缩）：前置音素(IsLead)从核起点
    // (=FillStart)往左累积各自时长；核(w>0)以标称时长为原长、按权重分摊填充剩余空间到 FillEnd（余量=空间−各核原长和）；
    // 后辅音(w=0)用固定时长。位置不存、纯派生。
    static double[] NaturalBoundaries(PhonemeLayoutNote note)
    {
        var phonemes = note.Phonemes;
        int n = phonemes?.Count ?? 0;
        var pos = new double[n + 1];
        if (n == 0)
            return pos;

        double noteStart = note.FillStart;
        double fillEnd = note.FillEnd;

        int L = 0;
        while (L < n && phonemes![L].IsLead) L++;   // 前置音素前缀

        pos[L] = noteStart;
        for (int k = L - 1; k >= 0; k--) pos[k] = pos[k + 1] - Math.Max(0, phonemes![k].Duration);

        // 核(w>0)以标称时长为原长、按权重分摊弹性余量（余量 = 可用空间 − 各核原长之和）：
        // 单核退化为整段填充（原长被余量抵消，恒等于可用空间）；多核时原长决定彼此分界、权重只决定拉伸/压缩的分摊，
        // 故相邻两核间的边界随各自标称时长移动（拖拽手柄即靠改时长反解此边界）。权重是音素固有属性、编辑器不改。
        double rigidAfter = 0, elasticWeight = 0, elasticBase = 0;
        for (int k = L; k < n; k++)
        {
            if (phonemes![k].StretchWeight > 0) { elasticWeight += phonemes[k].StretchWeight; elasticBase += Math.Max(0, phonemes[k].Duration); }
            else rigidAfter += Math.Max(0, phonemes[k].Duration);
        }
        double elasticSpace = Math.Max(0, (fillEnd - noteStart) - rigidAfter);
        double slack = elasticSpace - elasticBase;   // >0 拉伸 / <0 压缩，按权重分摊到各核
        double p = noteStart;
        for (int k = L; k < n; k++)
        {
            double w = phonemes![k].StretchWeight;
            double len = w > 0
                ? (elasticWeight > 0 ? Math.Max(0, phonemes[k].Duration) + slack * (w / elasticWeight) : 0)
                : Math.Max(0, phonemes[k].Duration);
            if (len < 0) len = 0;   // 过压（核被权重分摊压到负）就地钉死，保单调；拖拽至此即止
            pos[k] = p;
            p += len;
            pos[k + 1] = p;
        }
        return pos;
    }

    // —— 跨 note 去重叠（自然几何 → 真实时序）——

    // 单个音素的自然几何输入。
    readonly struct Segment
    {
        public readonly int Note;            // 出身 note 分组序号（同 note 连续且共享内部边界）
        public readonly double Weight;       // >0 = 可伸（元音 / 核）；0 = 刚性（辅音）
        public readonly double NaturalStart; // 标称起（绝对秒）
        public readonly double NaturalEnd;   // 标称讫（绝对秒）

        public Segment(int note, double weight, double naturalStart, double naturalEnd)
        {
            Note = note;
            Weight = weight;
            NaturalStart = naturalStart;
            NaturalEnd = naturalEnd;
        }
    }

    const double Epsilon = 1e-9;

    // 返回去重叠后各音素的真实 [Start, End]（绝对秒），与输入同序、单调不重叠。
    // 重叠只可能发生在 note 边界（同 note 内连续无隙不会重叠）。逐边界从左到右独立解析；相邻边界的音素集两两不相交、
    // 互不影响（核起点恒不动 = 共同固定锚），故 3-note 窗口或整段同一结果。
    //   · **不变量：音符头垂直投影到音素带上的那个点恒不动**——首核起点及其左侧（前置辅音、间隙）一律不进压缩跨度。
    //   · 吸收跨度 = [前 note 首核起点（固定左锚）… 后 note 核起点（固定右锚，核不压不动)）；前 note 首核到末尾的全部核都在跨度内。
    //   · ① 核先让（w>0，跨度内全部核按各自权重分摊收缩，各最多到 0）；② 核耗尽仍超 → 辅音簇（w=0，前 note 尾辅音 ∪ 后 note 前辅音）按标称长等比压。
    //   · 退化重叠（两核起点反序、可用空间为负）：就地塌缩到前 note 核起点；单调钳制兜底。
    // 仅相接 / 重叠的相邻 note 才跨 note 协同。两 note 间有空隙时音素互不影响（后 note 前置辅音可自然探入空隙、显示上与
    // 前 note 重叠，但谁都不被推挤 / 压缩）——固定音素不因邻居在空隙内移动而跳变。
    static PhonemeTiming[] ResolveSegments(IReadOnlyList<Segment> segments)
    {
        int m = segments.Count;
        var rs = new double[m];
        var re = new double[m];
        for (int i = 0; i < m; i++)
        {
            rs[i] = segments[i].NaturalStart;
            re[i] = segments[i].NaturalEnd;
        }

        for (int i = 0; i < m - 1; i++)
        {
            if (segments[i].Note == segments[i + 1].Note)
                continue;   // 同 note 内连续无隙
            if (re[i] <= rs[i + 1] + Epsilon)
                continue;   // 跨 note 但间隙 / 相邻，无重叠
            ResolveBoundary(segments, rs, re, i);
        }

        for (int i = 1; i < m; i++)   // 单调钳制兜底
        {
            if (rs[i] < rs[i - 1]) rs[i] = rs[i - 1];
            if (re[i] < rs[i]) re[i] = rs[i];
        }

        var result = new PhonemeTiming[m];
        for (int i = 0; i < m; i++)
            result[i] = new PhonemeTiming(rs[i], re[i]);
        return result;
    }

    // 解析 note 边界 i|i+1 的重叠（A=Note[i]，B=Note[i+1]，re[i] > rs[i+1]）。
    static void ResolveBoundary(IReadOnlyList<Segment> segments, double[] rs, double[] re, int i)
    {
        int aNote = segments[i].Note;
        int bNote = segments[i + 1].Note;

        // 左锚 = A 的第一个核起点（A 内首个 w>0 = 音符头）；A 无核（纯辅音）则取 A 首音素。核起点固定、不移动。
        // 退到首核（而非末核）是为让 A 从首核到末尾的所有核都进入压缩跨度、按各自权重共同让位；
        // 首核之前的 IsLead 前置辅音照旧留在左侧、不参与压缩。
        int aStart = i;
        while (aStart > 0 && segments[aStart - 1].Note == aNote) aStart--;
        int left = aStart;   // A 无核则取首音素
        for (int j = aStart; j <= i; j++)
            if (segments[j].Weight > 0) { left = j; break; }
        double leftPos = rs[left];   // 固定左锚（前 note 首核起点 = 音符头）

        // 右锚 = B 核起点（核不压、不动）；B 无核则取 B 末讫。跨度 = [left .. spanEnd]。
        int spanEnd;
        double rightPos;
        {
            int v = -1, lastB = i + 1;
            for (int j = i + 1; j < segments.Count && segments[j].Note == bNote; j++)
            {
                lastB = j;
                if (segments[j].Weight > 0) { v = j; break; }
            }
            if (v >= 0) { spanEnd = v - 1; rightPos = rs[v]; }
            else { spanEnd = lastB; rightPos = re[lastB]; }
        }

        // note 间有空隙（A 内容末 < B 核起点 = 两 note 不相接）：两 note 音素互不影响——各自保持自然几何、不推挤不压缩。
        if (re[i] < rightPos - Epsilon)
            return;

        if (spanEnd < left)
            return;

        int count = spanEnd - left + 1;
        var len = new double[count];
        double naturalTotal = 0;
        for (int k = 0; k < count; k++) { len[k] = re[left + k] - rs[left + k]; naturalTotal += len[k]; }

        double available = Math.Max(0, rightPos - leftPos);   // 退化（反序）则 0、就地塌缩；核起点 leftPos 不动
        double need = naturalTotal - available;

        if (need > Epsilon)
        {
            // ① 核(w>0)先让：把 need 按权重分摊到跨度内所有核，各自最多收缩到 0。
            //    多轮分摊——某核被压到 0 后退出，其残余在仍有余量的核间按权重重新分摊（权重大者让得多）。
            while (need > Epsilon)
            {
                double roundWeight = 0;
                for (int k = 0; k < count; k++)
                    if (segments[left + k].Weight > 0 && len[k] > Epsilon) roundWeight += segments[left + k].Weight;
                if (roundWeight <= Epsilon) break;

                double roundNeed = need;
                for (int k = 0; k < count; k++)
                {
                    double w = segments[left + k].Weight;
                    if (w <= 0 || len[k] <= Epsilon) continue;
                    double shrink = Math.Min(len[k], roundNeed * (w / roundWeight));
                    len[k] -= shrink;
                    need -= shrink;
                }
            }
            // ② 辅音簇按标称长度等比压（可到 0）。
            if (need > Epsilon)
            {
                double consTotal = 0;
                for (int k = 0; k < count; k++)
                    if (segments[left + k].Weight <= 0) consTotal += len[k];
                if (consTotal > Epsilon)
                {
                    double ratio = Math.Min(1.0, need / consTotal);
                    for (int k = 0; k < count; k++)
                        if (segments[left + k].Weight <= 0) len[k] -= len[k] * ratio;
                }
            }
        }

        // 从固定左锚（核起点）起依压缩后长度连续铺放——核起点不动，元音从尾缩、辅音簇相接。
        double pos = leftPos;
        for (int k = 0; k < count; k++)
        {
            rs[left + k] = pos;
            pos += Math.Max(0, len[k]);
            re[left + k] = pos;
        }
    }
}
