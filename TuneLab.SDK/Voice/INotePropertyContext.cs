using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// note 属性面板的 config 求值上下文（注入式只读）：插件据此返回当前选中 note 应呈现的 ObjectConfig。
// 不继承 IPartPropertyContext——选中的 note 必同属**一个** part，故 part 维度是单数（单个 PropertyObject），
// 与 part 面板可多选 part（IPartPropertyContext.PartProperties 为列表）的语义不同，强行继承会被迫拿到列表形 part 值。
//
// note config 可同时依赖 part 设置（单 part）与各选中 note 的值；part 改 commit 沿链触发 note 重算。
// 冻结接口纯加性扩展。
public interface INotePropertyContext
{
    // 选定声库（= 宿主 Voice.ID）：同 IPartPropertyContext.VoiceId，引擎据此知道求值的是哪个模型。
    string VoiceId { get; }
    // 选中 note 所属 part 的稀疏快照（单数——note 必属单 part）。
    PropertyObject PartProperties { get; }
    // 各选中 note 的稀疏快照（多选时逐 note 一个；空列表 = 无选中）。不在乎多选的插件 `NoteProperties.Merge()`
    // 还原成单个三态快照按单选写；需要逐成员真值的插件直接遍历列表。
    IReadOnlyList<PropertyObject> NoteProperties { get; }
}
