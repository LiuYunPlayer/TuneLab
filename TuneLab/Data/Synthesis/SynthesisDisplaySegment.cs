using System.Collections.Generic;

namespace TuneLab.Data.Synthesis;

// 状态带显示状态（宿主侧词汇，与 SDK 的声称词汇 SynthesisSegmentStatus 分离）：
// 声称层状态（Pending/Synthesizing/Claimed/Failed）来自引擎自报，事实层状态（Interim/Final/Degraded）
// 由宿主从管线音频事实派生。核心不变量：Final（亮绿=听到的即最终）只能由事实层产生，声称至多软色。
internal enum SynthesisDisplayState
{
    Pending,        // 声称：待合成（灰）
    Synthesizing,   // 声称：合成中（橙 + 纵向水位表进度——进度是该范围整体的标量，不借时间轴）
    Claimed,        // 声称：自称完成（软绿；如流式引擎的前沿声称。非最终，z 序垫底、被事实覆盖）
    Interim,        // 事实：音频已提交但链未完（软绿；待/在下游处理，或重处理期间的陈旧内容）
    Final,          // 事实：链尾当前有效音频（亮绿=可听的最终结果）
    Degraded,       // 事实：链上有级失败定局、passthrough 降级（琥珀=可播但非应有结果）
    Failed,         // 声称：合成失败（红=无声）
}

// 状态带显示段：管线 GetStatus 产出「按 z 序排列（底层在前）」的平铺列表——画家算法，
// 绘制端自底向上依次铺色，重叠由覆盖解决，不做任何区间代数；hover 命中反向遍历取最上层。
internal struct SynthesisDisplaySegment
{
    public double StartTime;
    public double EndTime;
    public SynthesisDisplayState State;
    // Synthesizing 时该范围整体进度 [0,1]（0 = 不报进度，整段合成中色）。
    public double Progress;
    // pill 文案：Failed/Degraded 为错误信息；Synthesizing 可为阶段文案。宿主/引擎产出，原样展示。
    public string? Message;
}

// 会话声称的 z 序分档（voice / instrument 管线共用）：
//   ClaimedDone = Synthesized（映射为 Claimed，垫底、被事实覆盖——声称永不产生 Final）；
//   Pending     = 排队声明（盖过陈旧事实绿——「将重做」必须压住「旧最终」，否则亮绿谎报；
//                 但被一切活动声称覆盖——排队不该遮住正在跑的实时反馈）；
//   Active      = Synthesizing / Failed（真活动与失败，恒顶——最早级的活动最有信息量）。
internal enum SessionClaimLayer
{
    ClaimedDone,
    Pending,
    Active,
}

// 会话声称（SDK 词汇 SynthesisStatusSegment）→ 显示段的映射，按 z 序分档取用。
internal static class SynthesisDisplayLayers
{
    public static void AppendSessionClaims(List<SynthesisDisplaySegment> output, IReadOnlyList<SDK.SynthesisStatusSegment> claims, SessionClaimLayer layer)
    {
        foreach (var claim in claims)
        {
            if (claim.EndTime <= claim.StartTime)
                continue;
            var claimLayer = claim.Status switch
            {
                SDK.SynthesisSegmentStatus.Synthesized => SessionClaimLayer.ClaimedDone,
                SDK.SynthesisSegmentStatus.Pending => SessionClaimLayer.Pending,
                _ => SessionClaimLayer.Active,
            };
            if (claimLayer != layer)
                continue;
            output.Add(new SynthesisDisplaySegment
            {
                StartTime = claim.StartTime,
                EndTime = claim.EndTime,
                State = claim.Status switch
                {
                    SDK.SynthesisSegmentStatus.Synthesized => SynthesisDisplayState.Claimed,
                    SDK.SynthesisSegmentStatus.Pending => SynthesisDisplayState.Pending,
                    SDK.SynthesisSegmentStatus.Failed => SynthesisDisplayState.Failed,
                    _ => SynthesisDisplayState.Synthesizing,
                },
                Progress = double.IsNaN(claim.Progress) ? 0 : System.Math.Clamp(claim.Progress, 0, 1),
                Message = claim.Message,
            });
        }
    }
}
