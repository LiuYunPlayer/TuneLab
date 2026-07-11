using System;
using System.Collections.Generic;

namespace TuneLab.SDK;

// 一个 note 的音素布局输入：几何锚点（note 头 / 填充末）+ 结构化双列表（引导 / 主体）+ 有符号 BodyOffset。
// 音素位置不传——由布局按时长模型从「junction = note 头 + BodyOffset + 标称时长 + 权重」单次锚定派生。
public readonly struct PhonemeLayoutNote
{
    // 填充区间左界 = note 头（绝对秒）。这是压缩域的分界线：note 头之前的音素料归**前 note** 的域，
    // 之后的归本域 [FillStart, FillEnd]。note 头恒 = 乐谱起点确定性换算的秒，故相接判据无容差（见 Connected）。
    public double FillStart { get; init; }

    // 填充区间右界 = 核填充终点（绝对秒）= 自己有效末 + 仅铺过延续乘客（IsContinuation）的 melisma。
    // [FillStart, FillEnd] 即本 note 拍后音素的可用空间；延音乘客在调用方预处理时已并入，布局不再自行组合。
    // 这是 WYSIWYG 口径：要音频 == 宿主显示，FillEnd 须与宿主同口径。
    public double FillEnd { get; init; }

    // 引导音素（核前前置辅音），时间序。布局只读其 Duration / StretchWeight。
    public IReadOnlyList<SynthesizedPhoneme> LeadingPhonemes { get; init; }
    // 主体音素（核 + 尾辅音），时间序。
    public IReadOnlyList<SynthesizedPhoneme> BodyPhonemes { get; init; }

    // 主体起点（= 两列表结合线 junction）相对 note 头的有符号偏移：junction = FillStart + BodyOffset（左负右正）。
    // 布局以 junction 为唯一原点单次摆放：body 正向累加、leading 反向累加；BodyOffset=0 时 junction ≡ FillStart（同一数、
    // 无加减）→ 结合线零误差落头上。**绝不**走「Preutterance = Σleading − BodyOffset 再正向累加」（双求和亚帧漂移）。
    // 头切分（供跨 note 压缩）与本分类正交：切点恒是 note 头（FillStart），头落在哪个音素内由 junction 摆放后位置与
    // FillStart 直接比较得出（noteStart − junction = −BodyOffset，精确、严格比较无容差）；被切音素**未必**= 结合线那个。
    public double BodyOffset { get; init; }

    // 全序列音素数（引导 + 主体）。
    public int PhonemeCount => (LeadingPhonemes?.Count ?? 0) + (BodyPhonemes?.Count ?? 0);
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

// 音素布局（合成域共享纯函数）：把每 note 的标称时长 + 几何锚点 + 前置量解析为真实绝对时序。
// 确定性、无状态、无副作用——可在合成的同步前缀（数据线程）直接调。冻结的是 I/O 形状（数据契约）；
// 分配内部逻辑仍可宿主侧演进，插件运行时绑定宿主这一份、随之演进永不漂移（音频 == 显示，WYSIWYG）。
// 想自定义音频摆放的引擎可不调，仍按 SynthesizedPhoneme 自由放置；调用方也可不传整段、对任意连续 note 区间成立。
//
// 单一逻辑（note 头是压缩域边界）：note 头把每 note 的音素料切成「拍前 / 拍后」两半（切中的音素本体一分为二）。
// 拍后料填本 note 可用空间 [FillStart, FillEnd]，与（相接时借入的）后 note 拍前料一起交 Distribute 分配。
// 拍前料：前 note 相接则并入前 note 域一并分配；否则按原长往左堆（不拉不压）。跨拍音素的前半 / 后半分投两域、
// 各自压缩、锚 note 头处拼回一整块（跨域回装）。相接判据 = FillEnd[i] >= FillStart[i+1]：note 头同源乐谱 tick、
// 相接时精确相等，严格比较无需容差。
public static class PhonemeLayout
{
    const double Epsilon = 1e-9;

    // 标称时长 + note 几何 + BodyOffset → 真实绝对时序。返回与输入同构的交错数组：全序列（LeadingPhonemes ++ BodyPhonemes）
    // 第 j 个音素的真实落点 = result[i][j]。
    public static PhonemeTiming[][] Resolve(IReadOnlyList<PhonemeLayoutNote> notes)
    {
        int count = notes.Count;
        var starts = new double[count][];
        var ends = new double[count][];
        var split = new NoteSplit[count];
        for (int i = 0; i < count; i++)
        {
            split[i] = NoteSplit.Compute(notes[i]);
            int n = split[i].Count;
            starts[i] = new double[n];
            ends[i] = new double[n];
            for (int j = 0; j < n; j++) { starts[i][j] = double.NaN; ends[i][j] = double.NaN; }
        }

        // Pass 1：逐 note 分配拍后域 [FillStart, FillEnd]——本 note 拍后料 +（相接时）后 note 拍前料，填于 note 头右侧。
        for (int i = 0; i < count; i++)
        {
            var s = split[i];
            if (s.Count == 0)
                continue;

            var idxNote = new List<int>();
            var idxPho = new List<int>();
            var partKind = new List<int>();   // 0=整音素, 1=本 note 跨拍后半, 2=后 note 跨拍前半(借入)
            var dur = new List<double>();
            var weight = new List<double>();

            // 本 note 拍后料：跨拍音素后半（若有，最左、起于 note 头）+ 全拍后音素（时间序）。
            for (int k = 0; k < s.Count; k++)
            {
                if (s.Class[k] == 1)        // 跨拍 → 后半进本域
                {
                    idxNote.Add(i); idxPho.Add(k); partKind.Add(1);
                    dur.Add(s.BackLen); weight.Add(s.Weight[k]);
                }
                else if (s.Class[k] == 2)   // 全拍后
                {
                    idxNote.Add(i); idxPho.Add(k); partKind.Add(0);
                    dur.Add(s.Dur[k]); weight.Add(s.Weight[k]);
                }
            }

            // 相接借用：后 note 的拍前料（全拍前音素 → 跨拍前半，接在本 note 拍后料之后、作本域最右）。
            if (i + 1 < count && Connected(notes, i))
            {
                var sn = split[i + 1];
                for (int k = 0; k < sn.Count; k++)
                {
                    if (sn.Class[k] == 0)       // 后 note 全拍前音素
                    {
                        idxNote.Add(i + 1); idxPho.Add(k); partKind.Add(0);
                        dur.Add(sn.Dur[k]); weight.Add(sn.Weight[k]);
                    }
                    else if (sn.Class[k] == 1)  // 后 note 跨拍音素前半（借入，本域最右、末 = 后 note 头）
                    {
                        idxNote.Add(i + 1); idxPho.Add(k); partKind.Add(2);
                        dur.Add(sn.FrontLen); weight.Add(sn.Weight[k]);
                    }
                }
            }

            double space = notes[i].FillEnd - notes[i].FillStart;
            var len = Distribute(dur, weight, space);
            double pos = notes[i].FillStart;
            for (int m = 0; m < len.Length; m++)
            {
                double end = pos + Math.Max(0, len[m]);
                int ni = idxNote[m], pi = idxPho[m];
                switch (partKind[m])
                {
                    case 1:   // 本 note 跨拍后半：起于 note 头（此 pos），末由分配定；起点最终由前半写入
                        ends[ni][pi] = end;
                        break;
                    case 2:   // 后 note 跨拍前半（借入）：起由分配定，末 = FillEnd_i = 后 note 头；末由后半写入
                        starts[ni][pi] = pos;
                        break;
                    default:  // 整音素
                        starts[ni][pi] = pos;
                        ends[ni][pi] = end;
                        break;
                }
                pos = end;
            }
        }

        // Pass 2：未被前 note 借走的 note，其拍前料按自然长往左堆（不拉不压）。跨拍前半最右（右缘 = note 头），再全拍前音素往左。
        for (int i = 0; i < count; i++)
        {
            var s = split[i];
            if (s.Count == 0)
                continue;
            bool borrowedByPrev = i > 0 && Connected(notes, i - 1);
            if (borrowedByPrev)
                continue;

            double p = notes[i].FillStart;
            if (s.Straddler >= 0)
            {
                double st = p - s.FrontLen;
                starts[i][s.Straddler] = st;   // 末由 Pass1 后半已写
                p = st;
            }
            for (int k = s.Count - 1; k >= 0; k--)
            {
                if (s.Class[k] != 0)           // 只堆全拍前音素
                    continue;
                double st = p - s.Dur[k];
                starts[i][k] = st;
                ends[i][k] = p;
                p = st;
            }
        }

        // 组装（跨拍音素的 start / end 分别由前半 / 后半两域写入，此处拼回）。
        var result = new PhonemeTiming[count][];
        for (int i = 0; i < count; i++)
        {
            int n = split[i].Count;
            result[i] = new PhonemeTiming[n];
            for (int j = 0; j < n; j++)
            {
                double st = starts[i][j], en = ends[i][j];
                if (double.IsNaN(st)) st = double.IsNaN(en) ? notes[i].FillStart : en;   // 兜底（正常不触发）
                if (double.IsNaN(en)) en = st;
                result[i][j] = new PhonemeTiming(st, en);
            }
        }
        return result;
    }

    // 相接判据：前 note 填充末 >= 后 note 头。两锚点同源于乐谱 tick 经确定性换算的秒、相接时精确相等，
    // 故严格比较无需容差（note 级去重叠是前置步骤，喂进来的 note 不交叠 → 相接即相等、有空隙即 FillEnd < FillStart）。
    static bool Connected(IReadOnlyList<PhonemeLayoutNote> notes, int i)
    {
        if (notes[i].PhonemeCount == 0 || notes[i + 1].PhonemeCount == 0)
            return false;
        return notes[i].FillEnd >= notes[i + 1].FillStart;
    }

    // 一个 note 以 junction 为原点单次摆放、按 note 头切成拍前 / 跨拍 / 拍后的中间结果。索引 = 全序列（引导 ++ 主体）。
    readonly struct NoteSplit
    {
        public readonly int Count;
        public readonly double[] Dur;
        public readonly double[] Weight;
        public readonly byte[] Class;      // 0 = 全拍前, 1 = 跨拍, 2 = 全拍后
        public readonly int Straddler;     // 跨拍音素下标（被 note 头一分为二），无则 -1
        public readonly double FrontLen;   // 跨拍音素落在 note 头之前的自然长
        public readonly double BackLen;    // 跨拍音素落在 note 头之后的自然长

        NoteSplit(int count, double[] dur, double[] weight, byte[] cls, int straddler, double frontLen, double backLen)
        {
            Count = count; Dur = dur; Weight = weight; Class = cls;
            Straddler = straddler; FrontLen = frontLen; BackLen = backLen;
        }

        // junction-anchored（单次求和、无 Σ 往返）：以 junction（= note 头 + BodyOffset）为唯一原点，
        // body 从 junction 正向铺、leading 从 junction 反向铺，得每音素相对 note 头的自然位置；再以 note 头（rel 0）
        // 就地切拍前 / 跨拍 / 拍后。BodyOffset=0 时 body 首起点 = 0 + 0 精确落 note 头（不经 Σleading 相减，规避亚帧漂移）。
        public static NoteSplit Compute(in PhonemeLayoutNote note)
        {
            var leading = note.LeadingPhonemes;
            var body = note.BodyPhonemes;
            int lc = leading?.Count ?? 0;
            int bc = body?.Count ?? 0;
            int n = lc + bc;
            var dur = new double[n];
            var weight = new double[n];
            for (int k = 0; k < lc; k++) { dur[k] = Math.Max(0, leading![k].Duration); weight[k] = leading[k].StretchWeight; }
            for (int k = 0; k < bc; k++) { dur[lc + k] = Math.Max(0, body![k].Duration); weight[lc + k] = body[k].StretchWeight; }

            double offset = note.BodyOffset;   // junction 相对 note 头

            // 每音素相对 note 头的自然 [start, end]（note 头 = 0）：body 从 junction 正向、leading 从 junction 反向。
            var ps = new double[n];
            var pe = new double[n];
            double c = offset;
            for (int i = 0; i < bc; i++) { int k = lc + i; ps[k] = c; pe[k] = c + dur[k]; c = pe[k]; }
            c = offset;
            for (int j = lc - 1; j >= 0; j--) { pe[j] = c; ps[j] = c - dur[j]; c = ps[j]; }

            // 就地切拍前 / 跨拍 / 拍后（与 note 头 = 0 比较，严格无容差；链单调 ⇒ 类序为 0…0 [1] 2…2、至多一个跨拍）。
            var cls = new byte[n];
            int straddler = -1;
            double frontLen = 0, backLen = 0;
            for (int k = 0; k < n; k++)
            {
                double cs = ps[k], ce = pe[k];
                if (ce <= Epsilon)              // 整段在 note 头之前
                    cls[k] = 0;
                else if (cs >= -Epsilon)        // 整段在 note 头之后
                    cls[k] = 2;
                else                            // note 头落在本音素内部 → 跨拍（前半落拍前域、后半落拍后域）
                {
                    cls[k] = 1;
                    straddler = k;
                    frontLen = -cs;
                    backLen = ce;
                }
            }
            return new NoteSplit(n, dur, weight, cls, straddler, frontLen, backLen);
        }
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
