using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data;

// 数据层构造的 part 条件属性面板 / 自动化轨求值上下文（承载 part 当前参数稀疏快照）。
// 用于宿主在 part 参数 commit 时按当前值重算声明（GetPartPropertyConfig / GetAutomationConfigs）。
// 属性侧栏另有一份同义实现（条件面板由 UI 现场构造，作用域不同，不强行共用）。
// part 单选，故 PartProperties 恒 0/1 个元素（无声库/无值 = 空列表）。
internal sealed class PartPropertyContext(string voiceId, IReadOnlyList<PropertyObject> partProperties) : IVoicePartPropertyContext
{
    public static readonly PartPropertyContext Empty = new(string.Empty, []);
    public string VoiceId => voiceId;
    public IReadOnlyList<PropertyObject> PartProperties => partProperties;
}
