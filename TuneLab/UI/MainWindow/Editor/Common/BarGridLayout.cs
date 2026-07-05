using System;
using TuneLab.Foundation;
using TuneLab.Data;

namespace TuneLab.UI;

// 小节线抽稀布局。缩小时不再逐小节画线/标号，而是按「拍号段落」分段决定省略：每段用本段小节的像素宽独立取
// 步幅（2 的幂逐级翻倍），段首（变拍号处）恒画、余数从段首归零 —— 变拍号那一小节永不被省略，段后小节宽度
// 不同也各自适应，不会用一个全局步幅错切。
//
// 两个消费端需求不同、各走一套，故分两个方法：
//  · ForEachGridLine（钢琴窗内部网格）：纯小节线、无号，按像素间距连续淡出 [12,24]，与紧邻的量化网格同套
//    （可停半淡——线没有文字，半透不难看，且与量化网格观感一致）。
//  · ForEachBarLine（时间线标尺）：线+号一体，按离散缩放档淡出、严格锁在相邻两档间，每个静止档 opacity 必为
//    0 或 1（半隐的小节号很难看）——淡入淡出只在两档间的缩放动画里出现。
internal static class BarGridLayout
{
    // 钢琴窗网格小节线全实阈值：某档线间距 ≥ 此值全实、≤ 其半为 0，之间线性（与量化网格 [MIN_GRID_GAP,MIN_REALITY_GRID_GAP]=[12,24] 同）。
    public const double GAP_GRID = 24;
    // 时间线小节线+号全实阈值：某档每小节像素宽 ≥ 此值则该档全实、往外缩一档淡尽。30 使编排区默认档 -8（每小节 30px）仍每根实显。
    public const double GAP_BAR = 30;
    // 每缩放 1 档，每小节像素宽变化的倍数关系：TickAxis 每档 ×√2（见 ScaleLevel2Factor：16^(1/8)=2^0.5），故每小节宽每翻倍 = 2 档。
    const double LEVELS_PER_OCTAVE = 2;

    public readonly struct BarLine
    {
        public readonly double Tick;
        public readonly int BarIndex;        // 全局小节 index；标号 = BarIndex + 1
        public readonly bool IsSegmentStart;  // 段首（变拍号地标）：线恒实，标尺处号让位给拍号标记不重复标
        public readonly double Opacity;      // 该根小节线（时间线上含其号）的不透明度

        public BarLine(double tick, int barIndex, bool isSegmentStart, double opacity)
        {
            Tick = tick;
            BarIndex = barIndex;
            IsSegmentStart = isSegmentStart;
            Opacity = opacity;
        }
    }

    public delegate void BarLineHandler(in BarLine line);

    // 钢琴窗内部网格：纯小节线（无号），像素间距连续淡出，与相邻量化网格同套 [12,24]。
    public static void ForEachGridLine(ITimeSignatureManager manager, TickAxis tickAxis, BarLineHandler handler)
    {
        double minVisibleTick = tickAxis.MinVisibleTick;
        double maxVisibleTick = tickAxis.MaxVisibleTick;

        var startMeter = manager.GetMeterStatus(minVisibleTick);
        var endMeter = manager.GetMeterStatus(maxVisibleTick);
        int startIndex = startMeter.TimeSignatureIndex;
        int endIndex = endMeter.TimeSignatureIndex;
        var timeSignatures = manager.TimeSignatures;

        for (int i = startIndex; i <= endIndex; i++)
        {
            var ts = timeSignatures[i];
            int segStartBar = ts.BarIndex;
            int nextBar = i + 1 == timeSignatures.Count ? endMeter.BarIndex.Ceil() : timeSignatures[i + 1].BarIndex;
            int firstBar = Math.Max(segStartBar, startMeter.BarIndex.Floor());

            double gapPerBar = ts.TicksPerBar() * tickAxis.PixelsPerTick;
            if (gapPerBar <= 0)
                continue;

            int stride = Pow2AtLeast(GAP_GRID / gapPerBar);
            // 被丢弃的更细一档（stride/2）在切档时渐隐，像素间距落在 [GAP_GRID/2, GAP_GRID)，与档跳变处连续。
            int finerStride = stride > 1 ? stride / 2 : 0;
            double fadeOpacity = finerStride == 0 ? 0
                : MathUtility.LineValue(GAP_GRID * 0.5, 0, GAP_GRID, 1, gapPerBar * finerStride).Limit(0, 1);

            for (int bar = firstBar; bar < nextBar; bar++)
            {
                int local = bar - segStartBar;
                bool isSegmentStart = local == 0;

                double opacity;
                if (isSegmentStart || local % stride == 0)
                {
                    opacity = 1;
                }
                else if (finerStride != 0 && local % finerStride == 0)
                {
                    if (fadeOpacity <= 0)
                        continue;

                    opacity = fadeOpacity;
                }
                else
                {
                    continue;
                }

                handler(new BarLine(ts.GetTickByBarIndex(bar), bar, isSegmentStart, opacity));
            }
        }
    }

    public static void ForEachBarLine(ITimeSignatureManager manager, TickAxis tickAxis, BarLineHandler handler)
    {
        ForEachBarLine(manager, tickAxis, tickAxis.MinVisibleTick, tickAxis.MaxVisibleTick, handler);
    }

    // 时间线标尺：线+号一体，离散档淡出。显式区间重载——标尺需向左多留一点（如 -48px）好让贴左边缘的号先探入。
    public static void ForEachBarLine(ITimeSignatureManager manager, TickAxis tickAxis, double minVisibleTick, double maxVisibleTick, BarLineHandler handler)
    {
        var startMeter = manager.GetMeterStatus(minVisibleTick);
        var endMeter = manager.GetMeterStatus(maxVisibleTick);
        int startIndex = startMeter.TimeSignatureIndex;
        int endIndex = endMeter.TimeSignatureIndex;
        var timeSignatures = manager.TimeSignatures;

        for (int i = startIndex; i <= endIndex; i++)
        {
            var ts = timeSignatures[i];
            int segStartBar = ts.BarIndex;
            int nextBar = i + 1 == timeSignatures.Count ? endMeter.BarIndex.Ceil() : timeSignatures[i + 1].BarIndex;
            int firstBar = Math.Max(segStartBar, startMeter.BarIndex.Floor());

            double gapPerBar = ts.TicksPerBar() * tickAxis.PixelsPerTick;
            if (gapPerBar <= 0)
                continue;

            // cullBase：本段每小节恰好缩到 GAP_BAR 宽的整数档（向上取整；-1e-6 防浮点把整解误抬一档）。
            // 该档起每小节全实，往外缩一档奇数小节淡尽；valuation 每高一级淘汰延后 2 档（步幅每 2 档翻倍）。
            double scaleLevel = tickAxis.ScaleLevel;
            double baseLevel = scaleLevel - LEVELS_PER_OCTAVE * Math.Log2(gapPerBar / GAP_BAR);
            int cullBase = (int)Math.Ceiling(baseLevel - 1e-6);

            // 当前最细可见档 valuation（其以下小节全隐、不遍历）：opacity>0 要求 valuation > (cullBase-1-scaleLevel)/2。
            int vMin = Math.Max(0, (int)Math.Floor((cullBase - 1 - scaleLevel) / 2.0) + 1);
            int step = 1 << vMin;
            // 该最细档渐隐中的 opacity（更粗档恒 1）。两档间线性、整数档必为 0 或 1（绝不停半淡）。
            double fineOpacity = Math.Clamp(scaleLevel - cullBase + 2 * vMin + 1, 0, 1);

            int firstLocal = firstBar - segStartBar;
            int alignedLocal = (firstLocal + step - 1) / step * step;   // ≥ firstLocal 的最近 step 倍数
            for (int local = alignedLocal; segStartBar + local < nextBar; local += step)
            {
                bool isSegmentStart = local == 0;   // 段首 = 变拍号地标，恒实
                // valuation 恰为 vMin（local/step 为奇数）→ 渐隐档；否则更粗档 → 恒实。
                double opacity = (isSegmentStart || local % (step << 1) == 0) ? 1 : fineOpacity;
                if (opacity <= 0)
                    continue;

                handler(new BarLine(ts.GetTickByBarIndex(segStartBar + local), segStartBar + local, isSegmentStart, opacity));
            }
        }
    }

    // 最小的、使 x 单位跨度 ≥1 的 2 的幂：2^max(0, ceil(log2(x)))。
    static int Pow2AtLeast(double x)
    {
        if (x <= 1)
            return 1;

        return (int)Math.Pow(2, Math.Ceiling(Math.Log2(x)));
    }
}
