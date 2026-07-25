using TuneLab.Foundation;

namespace TuneLab.Extensions.Derivers;

// 一次派生在工程里的持久记录（引用，非所有者）：键 = 本地内容寻址缓存的 key（见 NativeAudioPartInfo.DerivationRecords）。
// 只承载溯源信息——产出引擎 + 参数快照 + 启动时刻 + 展示标签；结果本身留在本地缓存、不入工程。
// 缓存命中 => 可应用；缓存缺失（换机器 / 被淘汰 / 未跑完就存了工程）=> 显示为「已失效」，源在则可重跑。
// 缓存内容寻址、跨 part/工程共享，故一条记录被删只移除本引用、绝不删缓存文件。
//
// 【宿主内部类型】刻意不进 SDK 公共面：记录形态随派生功能演进（会加字段/改形），不该被冻结 ABI 约束；
// 且通用格式插件无需关心派生。经 NativeAudioPartInfo（AudioPartInfo 的宿主内部子类）随 part 多态流转、仅 native 格式持久化。
internal sealed class DerivationRecordInfo
{
    public string EngineId { get; set; } = string.Empty;
    // 产出引擎的展示名（引擎已卸载时仍可读地呈现记录来源）。
    public string EngineDisplayName { get; set; } = string.Empty;
    // 提交时传入引擎的参数快照（供用户回看"当时用了什么设置"）。
    public PropertyObject Parameters { get; set; } = PropertyObject.Empty;
    // 启动（提交）时刻，Unix 秒。列表展示的排序键（不用完成时间——完成可能乱序）。
    public double StartTimestamp { get; set; } = 0;
    public string Label { get; set; } = string.Empty;
}
