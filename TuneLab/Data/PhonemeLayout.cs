using System;
using System.Collections.Generic;

namespace TuneLab.Data;

// 全局音素布局（宿主显示侧权威算法）：把各音素的「自然几何位置」（标称起讫秒 + 弹性权重 + 出身 note）解析为去重叠后的
// 真实时序。纯函数、确定性。
//
// 【非 SDK 契约】此算法不进插件 SDK 的 ABI——它形态仍在演进，塞进公开 ABI 会逼插件版本对齐。SDK 只冻结数据契约
// （VoicePhoneme + note 几何）。想要与宿主显示完全一致的插件可**照抄本算法**作参考实现
// （见 tests/plugins/V1.Voice），否则自由放置——错位非致命（仅音素线与波形不齐）。
//
// 输入是「自然几何」（标称位置）：调用方先按时长累积布局算出自然位置——音节核(元音,w>0)起点 = 音符头；
// 前置辅音从核起点往左累积；核 + 后辅音往右、核填充到满末。本算法只负责「跨 note 去重叠」。
//
// 去重叠语义（两阶分级，逐 note 边界、从左到右独立解析；V1 无最小地板、单调钳制兜底）：
// 重叠只可能发生在 note 边界（同 note 内连续无隙不会重叠）。后一个 note 的前置辅音自然起点落在前一个 note 末之前时即重叠。
//   · **不变量：音符头垂直投影到音素带上的那个点恒不动。** 实现上：吸收跨度只从「核起点」往右取，**核起点及其左侧
//     （前置辅音、间隙——含音符头投影点）一律不进跨度、绝不移动**；核起点 = 音符头故音符头投影点亦钉死。
//     压缩只发生在核及其右侧：元音从**尾部**收缩让位、辅音簇压缩。
//   · 吸收跨度 = [前 note 核起点（固定左锚）… 后 note 核起点（固定右锚，核不压不动）)。
//   · ① 元音先让（w>0，从尾收缩，最多到 0）；② 元音耗尽仍超 → 辅音簇（w=0，前 note 尾辅音 ∪ 后 note 前辅音）按标称长度等比压。
//   · 退化重叠（两核起点反序、可用空间为负）：就地塌缩到前 note 核起点；单调钳制兜底。
//
// 仅**相接 / 重叠**的相邻 note 才跨 note 协同（上面的两阶压缩）。两 note 间**有空隙**（前 note 内容末 < 后 note
// 核起点）时**音素互不影响**：各自保持自然几何，后 note 的前置辅音可自然探入空隙、在显示上与前 note 重叠，但谁都
// 不被推挤 / 压缩。这样固定音素不因邻居在空隙内移动而跳变；用户想要可调（前置辅音推挤前 note 元音）就把音符拉到相接。
internal static class PhonemeLayout
{
    // 单个音素的自然几何输入。
    public readonly struct Segment
    {
        public readonly int Note;          // 出身 note 分组序号（同 note 连续且共享内部边界）
        public readonly double Weight;       // >0 = 可伸（元音/核）；0 = 刚性（辅音）
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

    // 解析：返回去重叠后各音素的真实 [Start, End]（绝对秒），与输入同序、单调不重叠。
    public static (double Start, double End)[] Resolve(IReadOnlyList<Segment> segments)
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

        var result = new (double, double)[m];
        for (int i = 0; i < m; i++)
            result[i] = (rs[i], re[i]);
        return result;
    }

    // 解析 note 边界 i|i+1 的重叠（A=Note[i]，B=Note[i+1]，re[i] > rs[i+1]）。
    static void ResolveBoundary(IReadOnlyList<Segment> segments, double[] rs, double[] re, int i)
    {
        int aNote = segments[i].Note;
        int bNote = segments[i + 1].Note;

        // 左锚 = A 的核起点（A 内最后一个 w>0）；A 无核（纯辅音）则取 A 首音素。核起点固定、不移动。
        int left = i;
        for (int j = i; j >= 0 && segments[j].Note == aNote; j--)
        {
            left = j;
            if (segments[j].Weight > 0)
                break;
        }
        double leftPos = rs[left];   // 固定左锚（前 note 核起点 = 音符头）

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

        // note 间有空隙（A 内容末 < B 核起点 = 两 note 不相接）：两 note 音素**互不影响**——各自保持自然几何、
        // 不推挤不压缩（B 前置辅音可自然探入空隙、与 A 在显示上重叠，但谁都不动）。仅相接 / 重叠才跨 note 协同。
        // 用户要可调（前置辅音推挤前 note 元音）就自行把音符拉到相接。
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
            // ① 元音先让（跨度内至多一个核 = left，从尾收缩）。
            if (segments[left].Weight > 0)
            {
                double shrink = Math.Min(need, len[0]);
                len[0] -= shrink;
                need -= shrink;
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
