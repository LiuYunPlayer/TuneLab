using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// part 属性面板 / 自动化轨声明的 config 求值上下文（注入式只读）：插件据此返回当前应呈现的 ObjectConfig / 轨集合。
// 只承载"用户改过的稀疏值"——未改过的字段不出现，其默认值由插件自己知道（默认 = 字段不存在）。
//
// 多选：`PartProperties` 是**各选中 part 的稀疏快照列表**（单选 = 1 个、无选中 = 空），宿主不替插件擅自合并。
// 不在乎多选的插件 `context.PartProperties.Merge()`（Foundation 的 PropertyObjectExtensions 扩展方法）还原成单个三态快照
//（同 key 全等给值、不等/缺于部分成员给 Multiple），按单选写法处理；需要逐成员真值的插件直接遍历列表。
//
// part 级只依赖 part 自身值（面板内部联动 / 自动化轨随 part 参数显隐），不依赖 note 选择。
// note 级是另一个独立上下文（INotePropertyContext，单独成接口/文件，不继承本接口）——因 note 必属单个 part，
// 其 part 维度是单数，与本接口的多 part 列表语义不同，故不复用继承。
// 上游 commit 沿链触发下游重算（part 改 → 重算 part + note），下游不影响上游 → 无环、方向确定。
// 冻结接口纯加性扩展（以后加只读属性不破坏旧插件）。
public interface IPartPropertyContext
{
    // 选定声库（= 宿主 Voice.ID，IVoiceEngine.VoiceSourceInfos 的 key）：声明类 config 是
    // f(voiceId, part 稀疏值) 的纯函数，voiceId 随 context 一并注入，引擎据此知道求值的是哪个模型。
    // 这使 voice 的 context 必然带身份、与 effect 的 IEffectPropertyContext（单类型、无此对等物）永久分叉。
    string VoiceId { get; }
    // 各选中 part 的稀疏快照（多选 part 编辑时逐 part 一个；无选中 = 空列表）。
    IReadOnlyList<PropertyObject> PartProperties { get; }
}
