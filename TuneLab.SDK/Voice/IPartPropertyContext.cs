using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 属性面板 config 求值上下文（注入式只读）：插件据此返回当前应呈现的 ObjectConfig。
// 只承载"用户改过的稀疏值"——未改过的字段不出现，其默认值由插件自己知道（默认 = 字段不存在）。
//
// 多选：属性以**各选中成员的稀疏快照列表**注入（单选 = 1 个元素，无选中 = 空列表），宿主不替插件擅自合并。
// 不在乎多选的插件直接 `context.XxxProperties.Merge()`（Foundation 的 PropertyMerge 扩展方法）还原成单个
// 三态快照（同 key 全等给值、不等给 Multiple、缺于部分成员给 Multiple），即可按单选写法处理；需要逐成员真值
// （如把不等长数组的 seed 合成对、按各成员实际值算条件 config）的插件则直接遍历列表，避免合并丢失的真值。
//
// 分两级、单向依赖：
//   part 级（IPartPropertyContext）—— 输入只有 part 自身稀疏值，即面板内部联动（某控件影响同面板另一控件）；
//     part 面板/自动化轨声明据此求值，不依赖 note 选择。
//   note 级（INotePropertyContext）—— part 稀疏值 + 选中 note 各自的稀疏值（note config 可同时依赖 part 设置与 note 值）。
// 上游 commit 沿链触发下游重算（part 改 → 重算 part + note），下游不影响上游 → 无环、方向确定。
// 冻结接口纯加性扩展（以后加只读属性不破坏旧插件）。
public interface IPartPropertyContext
{
    // 选定声库（= 宿主 Voice.ID，IVoiceEngine.VoiceSourceInfos 的 key）：声明类 config 是
    // f(voiceId, part 稀疏值) 的纯函数，voiceId 随 context 一并注入，引擎据此知道求值的是哪个模型。
    // 这使 voice 的 context 必然带身份、与 effect 的 IEffectPropertyContext（单类型、无此对等物）永久分叉。
    string VoiceId { get; }
    // 各选中 part 的稀疏快照（当前单选 part，恒 1 个；保留列表形以备多 part 编辑与 note 端对齐）。
    IReadOnlyList<PropertyObject> PartProperties { get; }
}

// note 级 = part 级 + 各选中 note 的稀疏值（note config 可同时依赖 part 设置与 note 值）。
public interface INotePropertyContext : IPartPropertyContext
{
    // 各选中 note 的稀疏快照（多选时逐 note 一个；空列表 = 无选中）。
    IReadOnlyList<PropertyObject> NoteProperties { get; }
}
