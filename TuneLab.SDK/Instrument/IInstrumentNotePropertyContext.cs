using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// note 属性面板的 config 求值上下文（注入式只读）——instrument 专属面。
// 不继承 IInstrumentPartPropertyContext——选中的 note 必同属一个 part，故 part 维度是单数（单个 PropertyObject），
// 与 part 面板可多选 part（列表）的语义不同，强行继承会被迫拿到列表形 part 值。
public interface IInstrumentNotePropertyContext
{
    // 选定音源（= 宿主侧音源 ID）：同 IInstrumentPartPropertyContext.InstrumentId。
    string InstrumentId { get; }
    // 选中 note 所属 part 的稀疏快照（单数——note 必属单 part）。
    PropertyObject PartProperties { get; }
    // 各选中 note 的稀疏快照（多选时逐 note 一个；空列表 = 无选中）。
    IReadOnlyList<PropertyObject> NoteProperties { get; }
}
