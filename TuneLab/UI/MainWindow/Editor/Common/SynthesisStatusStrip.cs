using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.SDK;
using TuneLab.Data;
using TuneLab.Utils;

namespace TuneLab.UI;

// 合成状态条绘制（钢琴窗顶沿 + 编排视图 part 底缝共用）：
// 灰=待合成 橙=合成中 绿=已合成 红=失败；合成中段 = 绿[0→progress] + 橙[progress→末]，
// 故 Progress=0 时整段为橙（天然区别于灰），不需要额外的“无进度”特例。
// 形状：上沿两角拍直（贴边），下沿外端 radius 小圆角，内部接缝（含绿/橙交界）保持直角。
// shimmerPhase∈[0,1) 时给橙段叠一道循环流光表“正在动”；<0 关闭（编排视图静态）。
internal static class SynthesisStatusStrip
{
    public static void Draw(
        DrawingContext context,
        IReadOnlyList<SynthesisStatusSegment> segments,
        ITempoManager tempoManager,
        TickAxis tickAxis,
        double top, double height, double radius,
        double shimmerPhase = -1)
    {
        var pendingBrush = Style.SYNTHESIS_PENDING.ToBrush();
        var orangeBrush = Style.SYNTHESIS_SYNTHESIZING.ToBrush();
        var greenBrush = Style.SYNTHESIS_SYNTHESIZED.ToBrush();
        var redBrush = Style.SYNTHESIS_FAILED.ToBrush();

        foreach (var seg in segments)
        {
            double left = tickAxis.Tick2X(tempoManager.GetTick(seg.StartTime));
            double right = tickAxis.Tick2X(tempoManager.GetTick(seg.EndTime));
            if (right <= left)
                continue;

            switch (seg.Status)
            {
                case SynthesisSegmentStatus.Pending:
                    FillSeg(context, pendingBrush, left, right, top, height, radius, radius);
                    break;
                case SynthesisSegmentStatus.Synthesized:
                    FillSeg(context, greenBrush, left, right, top, height, radius, radius);
                    break;
                case SynthesisSegmentStatus.Failed:
                    FillSeg(context, redBrush, left, right, top, height, radius, radius);
                    break;
                case SynthesisSegmentStatus.Synthesizing:
                    double mid = left + (right - left) * seg.Progress.Limit(0, 1);
                    bool hasGreen = mid > left + 0.5;
                    bool hasOrange = right > mid + 0.5;
                    if (hasGreen)
                        FillSeg(context, greenBrush, left, mid, top, height, radius, hasOrange ? 0 : radius);
                    if (hasOrange)
                    {
                        double bl = hasGreen ? 0 : radius;
                        FillSeg(context, orangeBrush, mid, right, top, height, bl, radius);
                        if (shimmerPhase >= 0)
                            DrawShimmer(context, mid, right, top, height, bl, radius, shimmerPhase);
                    }
                    break;
            }
        }
    }

    // 粗粒度版（编排区 part 用）：只忠实标出“非可播放”的脏/错区间——待合成 & 合成中=灰（合成中叠灰色流光表“在跑”），
    // 失败=红；已合成 / 无内容不画（绿=可播放=干净无标记）。故空 part、全合成 part 都不显条，最忠实、最不打扰。
    // 不需要合并——要合并的“已合成”本来就不画。比钢琴窗粗：不画绿橙进度细分。
    // shimmerPhase∈[0,1) 时给合成中段叠灰色流光；<0 关闭（无 part 在合成时）。
    public static void DrawCoarse(
        DrawingContext context,
        IReadOnlyList<SynthesisStatusSegment> segments,
        ITempoManager tempoManager,
        TickAxis tickAxis,
        double top, double height, double radius,
        double shimmerPhase = -1)
    {
        var grayBrush = Style.SYNTHESIS_DIRTY_PART.ToBrush();   // 比钢琴窗更亮（抗白罩、对轨道色更跳）
        var redBrush = Style.SYNTHESIS_FAILED.ToBrush();

        // 合并相邻同类区间（灰 = 待合成/合成中，红 = 失败；已合成跳过），画成一条连续段，避免逐段圆角留缺口。
        int n = segments.Count;
        for (int i = 0; i < n;)
        {
            if (segments[i].Status == SynthesisSegmentStatus.Synthesized)
            {
                i++;
                continue;
            }

            bool red = segments[i].Status == SynthesisSegmentStatus.Failed;
            double left = Math.Round(tickAxis.Tick2X(tempoManager.GetTick(segments[i].StartTime)));
            double right = left;
            int j = i;
            for (; j < n; j++)
            {
                var st = segments[j].Status;
                if (st == SynthesisSegmentStatus.Synthesized)
                    break;
                if ((st == SynthesisSegmentStatus.Failed) != red)
                    break;   // 不同类（红 vs 灰），收束当前 run
                right = Math.Round(tickAxis.Tick2X(tempoManager.GetTick(segments[j].EndTime)));
            }
            if (right < left + 1)
                right = left + 1;

            FillSeg(context, red ? redBrush : grayBrush, left, right, top, height, radius, radius);

            // 流光只叠在“合成中”的原始片上（不是整条合并灰条）；触及 run 端的片跟随其圆角，免得方角溢出。
            if (!red && shimmerPhase >= 0)
            {
                for (int k = i; k < j; k++)
                {
                    if (segments[k].Status != SynthesisSegmentStatus.Synthesizing)
                        continue;
                    double sl = Math.Round(tickAxis.Tick2X(tempoManager.GetTick(segments[k].StartTime)));
                    double sr = Math.Round(tickAxis.Tick2X(tempoManager.GetTick(segments[k].EndTime)));
                    if (sr < sl + 1)
                        sr = sl + 1;
                    DrawShimmer(context, sl, sr, top, height, sl <= left ? radius : 0, sr >= right ? radius : 0, shimmerPhase);
                }
            }

            i = j;
        }
    }

    // 填一段：上沿两角拍直，下沿左/右角圆角分别为 blRadius/brRadius。
    static void FillSeg(DrawingContext context, IBrush brush, double left, double right, double top, double height, double blRadius, double brRadius)
    {
        var rect = new Rect(left, top, right - left, height);
        context.DrawRectangle(brush, null, new RoundedRect(rect, 0, 0, brRadius, blRadius));
    }

    // 一道从左外扫到右外的白色高光带，裁到橙段形状内。
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
