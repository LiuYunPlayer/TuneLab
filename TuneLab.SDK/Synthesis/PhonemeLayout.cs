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
// 交给 Distribute 统一分配（乘法 / 等比模型：可伸音素缩放比 = r^w，刚性音素不动）。前置音素若其所属 note 前方无相接音符，则按原长往左堆叠
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

    // 把一组音素的标称时长按权重 + 可用空间分配为最终长度（确定性纯函数，乘法 / 等比模型）：
    // 每个可伸音素(w>0)的缩放比 len/d = r^w，r 是由「分配后总长 = 可用空间」唯一确定的全局基准缩放比
    // （r>1 拉伸 / r<1 压缩 / r=1 不变）；刚性音素(w=0) 恒为原长、不参与（r^0=1）。同权重 ⇒ 同缩放比（等比保形）。
    //   · 无可伸音素(全 w=0)：全部按原长等比缩放占满（拉伸 / 压缩皆均匀，复刻无弹性数据的整体缩放）。
    //   · 核空间(= 可用空间 − Σ刚性原长) ≤ 0：连刚性音素都塞不下 → 可伸音素全归 0、刚性音素按原长等比压占满。
    //   · 同权重(含单核)：闭式 len = d·(核空间/Σ核原长)（r^w 整体即此因子，无需解 r）。
    //   · 异权重：二分解 Σ_{w>0} d·r^w = 核空间，夹到相邻 double（r 不再变化）为止，len = d·r^w。
    // 不变量：Σ最终长度 = 可用空间，音素绝不溢出可用空间。
    static double[] Distribute(IReadOnlyList<double> dur, IReadOnlyList<double> weight, double space)
    {
        int n = dur.Count;
        var len = new double[n];
        if (n == 0) return len;
        if (space < 0) space = 0;

        double naturalTotal = 0, totalWeight = 0, coreNatural = 0;
        double firstWeight = 0;
        bool hasCore = false, uniformWeight = true;
        for (int k = 0; k < n; k++)
        {
            naturalTotal += dur[k];
            if (weight[k] > 0)
            {
                totalWeight += weight[k];
                coreNatural += dur[k];
                if (!hasCore) { firstWeight = weight[k]; hasCore = true; }
                else if (weight[k] != firstWeight) uniformWeight = false;
            }
        }
        double rigidTotal = naturalTotal - coreNatural;

        // 无可伸音素（全 w=0）：按原长等比缩放占满。
        if (!hasCore)
        {
            if (naturalTotal > Epsilon)
                for (int k = 0; k < n; k++) len[k] = dur[k] * (space / naturalTotal);
            return len;
        }

        double coreSpace = space - rigidTotal;

        // 二级压缩：刚性音素都塞不下 → 可伸音素全归 0、刚性按原长等比压占满。
        if (coreSpace <= Epsilon)
        {
            double scale = rigidTotal > Epsilon ? space / rigidTotal : 0;
            for (int k = 0; k < n; k++) len[k] = weight[k] > 0 ? 0 : dur[k] * scale;
            return len;
        }

        // 核原长全 0（数据异常、乘法奇点 d·r^w≡0）：按权重把核空间线性分给可伸音素。
        if (coreNatural <= Epsilon)
        {
            for (int k = 0; k < n; k++)
                len[k] = weight[k] > 0 ? coreSpace * (weight[k] / totalWeight) : dur[k];
            return len;
        }

        // 同权重（含单核）→ 闭式等比：r^w 整体 = 核空间/核原长和。
        if (uniformWeight)
        {
            double factor = coreSpace / coreNatural;
            for (int k = 0; k < n; k++) len[k] = weight[k] > 0 ? dur[k] * factor : dur[k];
            return len;
        }

        // 异权重 → 二分解 CoreSum(r) = Σ_{w>0} d·r^w = coreSpace（CoreSum 对 r 单调增、根唯一）。
        double CoreSum(double r)
        {
            double s = 0;
            for (int k = 0; k < n; k++) if (weight[k] > 0) s += dur[k] * Math.Pow(r, weight[k]);
            return s;
        }
        double lo = 0, hi = 1;
        while (CoreSum(hi) < coreSpace) hi *= 2;   // 倍增找上界（CoreSum(0)=0 < coreSpace 故 lo=0 有效）
        double mid = hi;
        while (true)
        {
            double next = (lo + hi) * 0.5;
            mid = next;
            if (next <= lo || next >= hi) break;   // 夹到相邻 double，r 不再变化为止（解到 double 精度）
            if (CoreSum(next) < coreSpace) lo = next; else hi = next;
        }
        for (int k = 0; k < n; k++) len[k] = weight[k] > 0 ? dur[k] * Math.Pow(mid, weight[k]) : dur[k];
        return len;
    }
}
