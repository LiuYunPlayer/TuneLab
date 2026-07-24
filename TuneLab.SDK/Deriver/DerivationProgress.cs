namespace TuneLab.SDK;

// deriver 的进度上报（引擎→宿主）：一次性长任务只需「进度 + 一句文案」，不套合成域的时间分段状态带
//（SynthesisStatusSegment 是反应式合成的 z 序声称，语义与此差异大，故 deriver 独立此极简类型）。
public readonly struct DerivationProgress
{
    // 完成度 [0,1]；不报进度的引擎保持 0。
    public double Progress { get; init; }
    // 可选的阶段文案（如「正在检测 onset」）；宿主原样展示、不解析。
    public string? Message { get; init; }
}
