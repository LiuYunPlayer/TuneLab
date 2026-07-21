namespace TuneLab.SDK;

public enum SynthesisSegmentStatus
{
    Pending,
    Synthesizing,
    Synthesized,
    Failed,
}

// 合成状态时间线（插件托管的"声称"词汇，宿主经 GetStatus 拉取）：voice 会话与 effect 处理器共用——
// 把"待合成/合成中/已完成/失败"统一成一条按时间分段的状态带。每个状态都是引擎对**自己产物**的
// 第一人称陈述（声明主语归产物的生产者，范围不受输入几何约束）；宿主把各级声称与音频事实分层呈现
//（声称永远不直接产生"最终"显示——最终绿只能来自链尾真实音频）。
// 范围平铺为两个 double（秒，与音频产物同一时间系），不引入冻结的区间类型。
//
// 形态 = readonly struct + init 属性（合成域值 DTO 房规默认）：成员经属性访问器暴露、backing 可演进，
// 且不可变——引擎在 GetStatus 里 new SynthesisStatusSegment { StartTime = …, … } 现造整段状态带、宿主只读。
public readonly struct SynthesisStatusSegment
{
    public double StartTime { get; init; }
    public double EndTime { get; init; }
    public SynthesisSegmentStatus Status { get; init; }
    // 状态文案：Failed 时为错误信息；Synthesizing 时可选报管线阶段（如"正在合成音高""正在计算音素时长"），
    // 宿主原样展示、不解析语义。
    public string? Message { get; init; }
    // Synthesizing 时该段进度 [0,1]，不报进度的插件保持 0。
    // （将来若需区分"无进度"与"0%"，加 bool HasProgress 属性即可，纯加性不破本面。）
    public double Progress { get; init; }
}
