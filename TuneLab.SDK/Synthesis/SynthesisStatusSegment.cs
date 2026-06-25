namespace TuneLab.SDK;

public enum SynthesisSegmentStatus
{
    Pending,
    Synthesizing,
    Synthesized,
    Failed,
}

// 合成状态时间线（插件托管，宿主经 GetStatus 拉取）：把"待合成/合成中/已完成/失败"
// 统一成一条按时间分段的状态带，同时充当"哪段更新了"的区域信息——宿主据范围+Status
// 着色、显示进度、在失败段显示错误，并知道哪段已合成可去拉音频。
// 范围平铺为两个 double（秒，与音频产物同一时间系），不引入冻结的区间类型——
// 区间运算（合并/相交等）是宿主侧能力，按需在宿主内部封装。
public struct SynthesisStatusSegment
{
    public double StartTime;
    public double EndTime;
    public SynthesisSegmentStatus Status;
    // 状态文案：Failed 时为错误信息；Synthesizing 时可选报管线阶段（如"正在合成音高""正在计算音素时长"），
    // 宿主原样展示、不解析语义。
    public string? Message;
    // Synthesizing 时该段进度 [0,1]，不报进度的插件保持 0。
    // （将来若需区分"无进度"与"0%"，加 bool HasProgress 字段即可，纯加性不破本面。）
    public double Progress;
}
