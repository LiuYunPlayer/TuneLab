using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.Data;
using TuneLab.Data.Synthesis;
using TuneLab.Utils;

namespace TuneLab.UI;

// 合成状态带绘制（钢琴窗顶沿 + 编排视图 part 底缝共用）：输入是管线产出的 z 序图层列表（底层在前），
// 画家算法自底向上依次铺色、重叠由覆盖解决——本文件零区间代数。
//
// 视觉词汇（每个维度只背一个语义）：
//   横向位置 = 时间；色相 = 状态类别（灰=待 橙=在跑 绿=有货 琥珀=降级 红=失败）；
//   明度档 = 声称/最终（软绿=声称完成/待下游的非最终内容，亮绿=链尾事实最终）；
//   纵向水位 = Synthesizing 段的整体进度（标量，不借时间轴——横向推进语义归引擎的前沿状态段切分）。
// shimmerPhase∈[0,1) 时给在跑段叠一道循环流光表"正在动"；<0 关闭。
internal static class SynthesisStatusStrip
{
    public static void Draw(
        DrawingContext context,
        IReadOnlyList<SynthesisDisplaySegment> segments,
        ITempoManager tempoManager,
        TickAxis tickAxis,
        double top, double height, double radius,
        double shimmerPhase = -1)
    {
        var pendingBrush = Style.SYNTHESIS_PENDING.ToBrush();
        var orangeBrush = Style.SYNTHESIS_SYNTHESIZING.ToBrush();
        var softGreenBrush = Style.SYNTHESIS_INTERIM.ToBrush();
        var brightGreenBrush = Style.SYNTHESIS_SYNTHESIZED.ToBrush();
        var amberBrush = Style.SYNTHESIS_DEGRADED.ToBrush();
        var redBrush = Style.SYNTHESIS_FAILED.ToBrush();

        foreach (var seg in segments)
        {
            double left = tickAxis.Tick2X(tempoManager.GetTick(seg.StartTime));
            double right = tickAxis.Tick2X(tempoManager.GetTick(seg.EndTime));
            if (right <= left)
                continue;

            switch (seg.State)
            {
                case SynthesisDisplayState.Pending:
                    FillSeg(context, pendingBrush, left, right, top, height, radius, radius);
                    break;
                case SynthesisDisplayState.Claimed:
                case SynthesisDisplayState.Interim:
                    FillSeg(context, softGreenBrush, left, right, top, height, radius, radius);
                    break;
                case SynthesisDisplayState.Final:
                    FillSeg(context, brightGreenBrush, left, right, top, height, radius, radius);
                    break;
                case SynthesisDisplayState.Degraded:
                    FillSeg(context, amberBrush, left, right, top, height, radius, radius);
                    break;
                case SynthesisDisplayState.Failed:
                    FillSeg(context, redBrush, left, right, top, height, radius, radius);
                    break;
                case SynthesisDisplayState.Synthesizing:
                    FillSeg(context, orangeBrush, left, right, top, height, radius, radius);
                    // 纵向水位：软绿自底部涨至 progress 高度（该范围整体完成度；0 = 无进度、整段橙）。
                    double p = seg.Progress.Limit(0, 1);
                    if (p > 0)
                    {
                        double waterHeight = height * p;
                        FillSeg(context, softGreenBrush, left, right, top + height - waterHeight, waterHeight, radius, radius);
                    }
                    if (shimmerPhase >= 0)
                        DrawShimmer(context, left, right, top, height, radius, radius, shimmerPhase);
                    break;
            }
        }
    }

    // 粗粒度版（编排区 part 用）：只忠实标出"非最终"区间——待/在跑/待下游=灰（在跑叠灰色流光），
    // 降级=琥珀，失败=红；最终亮绿与声称完成不画（绿=最终=干净无标记；Claimed 是细节声称，粗视图略）。
    // 故空 part、全合成 part 都不显条，最忠实、最不打扰。
    public static void DrawCoarse(
        DrawingContext context,
        IReadOnlyList<SynthesisDisplaySegment> segments,
        ITempoManager tempoManager,
        TickAxis tickAxis,
        double top, double height, double radius,
        double shimmerPhase = -1)
    {
        var grayBrush = Style.SYNTHESIS_DIRTY_PART.ToBrush();       // 比钢琴窗更亮（抗白罩、对轨道色更跳）
        var amberBrush = Style.SYNTHESIS_DEGRADED_PART.ToBrush();   // 同理用高饱和亮黄
        var redBrush = Style.SYNTHESIS_FAILED_PART.ToBrush();       // 同理用高饱和亮红

        foreach (var seg in segments)
        {
            if (seg.State is SynthesisDisplayState.Final or SynthesisDisplayState.Claimed)
                continue;

            double left = Math.Round(tickAxis.Tick2X(tempoManager.GetTick(seg.StartTime)));
            double right = Math.Round(tickAxis.Tick2X(tempoManager.GetTick(seg.EndTime)));
            if (right < left + 1)
                right = left + 1;

            var brush = seg.State switch
            {
                SynthesisDisplayState.Failed => redBrush,
                SynthesisDisplayState.Degraded => amberBrush,
                _ => grayBrush,
            };
            FillSeg(context, brush, left, right, top, height, radius, radius);

            if (seg.State == SynthesisDisplayState.Synthesizing && shimmerPhase >= 0)
                DrawShimmer(context, left, right, top, height, radius, radius, shimmerPhase);
        }
    }

    // 填一段：上沿两角拍直，下沿左/右角圆角分别为 blRadius/brRadius。
    static void FillSeg(DrawingContext context, IBrush brush, double left, double right, double top, double height, double blRadius, double brRadius)
    {
        var rect = new Rect(left, top, right - left, height);
        context.DrawRectangle(brush, null, new RoundedRect(rect, 0, 0, brRadius, blRadius));
    }

    // 一道从左外扫到右外的白色高光带，裁到段形状内。
    static void DrawShimmer(DrawingContext context, double left, double right, double top, double height, double blRadius, double brRadius, double phase)
    {
        double width = right - left;
        double bandW = Math.Max(16, width * 0.4);
        double bandLeft = left - bandW + phase * (width + bandW);

        using var clip = context.PushClip(new RoundedRect(new Rect(left, top, width, height), 0, 0, brRadius, blRadius));
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Colors.White.Opacity(0), 0),
                new GradientStop(Colors.White.Opacity(0.5), 0.5),
                new GradientStop(Colors.White.Opacity(0), 1),
            }
        };
        context.FillRectangle(brush, new Rect(bandLeft, top, bandW, height));
    }
}
