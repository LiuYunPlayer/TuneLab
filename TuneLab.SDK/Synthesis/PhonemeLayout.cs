using System;
using System.Collections.Generic;

namespace TuneLab.SDK;

// 一个 note 的音素布局输入：几何锚点 + 该 note 的音素描述符（顺序：前置辅音 → 核 → 后辅音，与 IsLead 前缀一致）。
// 音素位置不传——由布局按时长模型从「核起点 + 标称时长 + IsLead + 权重」派生。
public readonly struct PhonemeLayoutNote
{
    // 填充区间左界 = 核起点 = 音符头（绝对秒）。核从这里往右填充、前置音素(IsLead)从这里往左累积（可越界到 note 之前）。
    public double FillStart { get; init; }

    // 填充区间右界 = 核填充终点（绝对秒）= 自己的末 + 仅铺过延续乘客（IsContinuation）的 melisma。
    // [FillStart, FillEnd] 即本 note 音素的「可用空间」；延音乘客在调用方预处理时已并入本区间，布局不再自行组合。
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

// 音素布局（合成域共享纯函数）：把每 note 的标称时长 + 几何锚点解析为真实绝对时序。
// 确定性、无状态、无副作用——可在合成的同步前缀（数据线程）直接调。
//
// 此前布局算法不进 SDK（顾虑"塞进 ABI 逼插件版本对齐"）；现冻结的只是 I/O 形状（数据契约），分配内部逻辑仍
// 可宿主侧自由演进——插件运行时绑定宿主的这一份，故随宿主算法演进永不漂移（音频 == 显示，WYSIWYG）。想自定义
// 音频摆放（交叉淡入等）的引擎可不调，仍按 SynthesizedPhoneme 自由放置；调用方也可不传整段、对任意连续 note 区间成立。
//
// 单一逻辑、note 驱动（无"单音符 / 跨音符"两条路，仅音素集合大小随相接而变）：逐 note 取可用空间 [FillStart, FillEnd]，
// 收集落在该空间内的音素——本 note 的非前置音素（核 + 后辅音），外加**仅当与后 note 相接时**后 note 的前置音素——
// 交给 Distribute 统一分配（拉伸 / 一级压缩 / 二级压缩）。前置音素若其所属 note 前方无相接音符，则按原长往左堆叠
// （不拉不压）；前方相接时其前置已并入前 note 的可用空间一并分配，不重复摆放。
// 相接判据 = FillEnd[i] >= FillStart[i+1]：锚点为同一乐谱 tick 经确定性换算的秒、相接时精确相等，故严格比较无需容差。
public static class PhonemeLayout
{
    const double Epsilon = 1e-9;

    // 标称时长 + note 几何 → 真实绝对时序。返回与输入同构的交错数组：notes[i].Phonemes[j] 的真实落点 = result[i][j]。
    public static PhonemeTiming[][] Resolve(IReadOnlyList<PhonemeLayoutNote> notes)
    {
        int count = notes.Count;
        var result = new PhonemeTiming[count][];
        for (int i = 0; i < count; i++)
            result[i] = new PhonemeTiming[notes[i].Phonemes?.Count ?? 0];

        for (int i = 0; i < count; i++)
        {
            var phonemes = notes[i].Phonemes;
            int n = phonemes?.Count ?? 0;
            if (n == 0)
                continue;

            int lead = 0;
            while (lead < n && phonemes![lead].IsLead) lead++;   // 前置辅音前缀

            // 可用空间内的音素集合 = 本 note 非前置（核 + 后辅音） + （相接时）后 note 的前置。
            // idxNote / idxPho 记录每个 member 的回写坐标——后 note 的前置写回它自己那一行。
            var idxNote = new List<int>();
            var idxPho = new List<int>();
            var dur = new List<double>();
            var weight = new List<double>();
            for (int k = lead; k < n; k++)
            {
                idxNote.Add(i); idxPho.Add(k);
                dur.Add(Math.Max(0, phonemes![k].Duration)); weight.Add(phonemes[k].StretchWeight);
            }
            if (i + 1 < count && Connected(notes, i))
            {
                var next = notes[i + 1].Phonemes;
                int nn = next?.Count ?? 0;
                for (int k = 0; k < nn && next![k].IsLead; k++)
                {
                    idxNote.Add(i + 1); idxPho.Add(k);
                    dur.Add(Math.Max(0, next[k].Duration)); weight.Add(next[k].StretchWeight);
                }
            }

            double space = notes[i].FillEnd - notes[i].FillStart;
            var len = Distribute(dur, weight, space);
            double pos = notes[i].FillStart;
            for (int m = 0; m < len.Length; m++)
            {
                double end = pos + Math.Max(0, len[m]);
                result[idxNote[m]][idxPho[m]] = new PhonemeTiming(pos, end);
                pos = end;
            }

            // 本 note 前置：前方无相接音符时按原长往左堆（前一个 note 相接本 note 则其前置已由前 note 分配、不重复）。
            bool borrowedByPrev = i > 0 && Connected(notes, i - 1);
            if (!borrowedByPrev)
            {
                double p = notes[i].FillStart;
                for (int k = lead - 1; k >= 0; k--)
                {
                    double s = p - Math.Max(0, phonemes![k].Duration);
                    result[i][k] = new PhonemeTiming(s, p);
                    p = s;
                }
            }
        }
        return result;
    }

    // 相接判据：前 note 填充末 >= 后 note 核起点。两锚点同源于乐谱 tick 经确定性换算的秒、相接时精确相等，
    // 故严格比较无需容差（note 级去重叠是前置步骤，喂进来的 note 不交叠 → 相接即相等、有空隙即 FillEnd < FillStart）。
    static bool Connected(IReadOnlyList<PhonemeLayoutNote> notes, int i)
    {
        if ((notes[i].Phonemes?.Count ?? 0) == 0 || (notes[i + 1].Phonemes?.Count ?? 0) == 0)
            return false;
        return notes[i].FillEnd >= notes[i + 1].FillStart;
    }

    // 把一组音素的标称时长按权重 + 可用空间分配为最终长度（确定性纯函数，三档无单跨之分）：
    //   · 可用空间 ≥ 原长和：拉伸——超出部分按权重摊给各音素（w=0 分到 0、不动）；全 w=0 退化为按原长等比拉伸占满。
    //   · 可用空间 < w=0 原长和：二级压缩——w>0 全归 0，w=0 按标称长等比压缩占满可用空间。
    //   · 介于两者：一级压缩——w=0 不变，剩余空间由 w>0 按权重水填分配（分到负长者出局钳 0、余者重算，直到无出局）。
    // 不变量：Σ最终长度 ≤ 可用空间（拉伸时取等），音素绝不溢出可用空间。
    static double[] Distribute(IReadOnlyList<double> dur, IReadOnlyList<double> weight, double space)
    {
        int n = dur.Count;
        var len = new double[n];
        if (n == 0) return len;
        if (space < 0) space = 0;

        double naturalTotal = 0, totalWeight = 0, rigidTotal = 0;
        for (int k = 0; k < n; k++)
        {
            naturalTotal += dur[k];
            if (weight[k] > 0) totalWeight += weight[k];
            else rigidTotal += dur[k];
        }

        // 拉伸：超出原长的部分按权重分摊。
        if (space >= naturalTotal)
        {
            double extra = space - naturalTotal;
            if (totalWeight > Epsilon)
                for (int k = 0; k < n; k++)
                    len[k] = dur[k] + (weight[k] > 0 ? extra * (weight[k] / totalWeight) : 0);
            else if (naturalTotal > Epsilon)   // 全 w=0 退化：按原长等比拉伸占满
                for (int k = 0; k < n; k++)
                    len[k] = dur[k] * (space / naturalTotal);
            // naturalTotal≈0：无原长可分，全 0
            return len;
        }

        // 二级压缩：w=0 原长都塞不下 → w>0 全归 0，w=0 等比压缩占满。
        if (space < rigidTotal)
        {
            double scale = rigidTotal > Epsilon ? space / rigidTotal : 0;
            for (int k = 0; k < n; k++)
                len[k] = weight[k] > 0 ? 0 : dur[k] * scale;
            return len;
        }

        // 一级压缩：w=0 保持原长，w>0 按权重水填到剩余空间。
        for (int k = 0; k < n; k++)
            len[k] = dur[k];                 // w=0 终值；w>0 在水填中覆盖
        double coreSpace = space - rigidTotal;
        var dropped = new bool[n];
        while (true)
        {
            double activeWeight = 0, activeBase = 0;
            for (int k = 0; k < n; k++)
                if (weight[k] > 0 && !dropped[k]) { activeWeight += weight[k]; activeBase += dur[k]; }
            if (activeWeight <= Epsilon) break;

            double delta = coreSpace - activeBase;   // ≤ 0：按权重分摊的收缩量
            bool anyDropped = false;
            for (int k = 0; k < n; k++)
                if (weight[k] > 0 && !dropped[k] && dur[k] + delta * (weight[k] / activeWeight) < 0)
                {
                    dropped[k] = true; len[k] = 0; anyDropped = true;   // 压到负 → 出局钳 0
                }
            if (!anyDropped)
            {
                for (int k = 0; k < n; k++)
                    if (weight[k] > 0 && !dropped[k])
                        len[k] = dur[k] + delta * (weight[k] / activeWeight);
                break;
            }
        }
        return len;
    }
}
