using TuneLab.Foundation;

namespace TuneLab.SDK;

// 声库目录元数据（IVoiceSynthesisEngine.VoiceSourceInfos 的值；会话不重复承载这些）。
// 目录元数据必然增长（作者/版本/语种/标签/试听……），故取快照家族同款 sealed class + required init。
// 加字段本就加性（对 struct 亦然），换形态锁掉的是另两条冻结后无解的二进制破坏：
// public 字段→属性（老插件 ldfld 变 get_，MissingFieldException）、struct 值语义→class。
// 一上来就是 { get; init; } 属性 + 引用语义，日后换字段实现体（校验/惰性/计算）不破坏 ABI。不在热路径。
public sealed class VoiceSourceInfo
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public ImageResource? Portrait { get; init; }   // 可选立绘（显示在钢琴窗）；null = 无
}
