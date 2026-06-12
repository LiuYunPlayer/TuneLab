using TuneLab.Primitives.Property;

namespace TuneLab.SDK;

// 属性面板 config 求值上下文（注入式只读）：插件的 GetPartConfig/GetNoteConfig 据此返回当前应呈现的 ObjectConfig。
// 只承载"用户改过的稀疏值"——未改过的字段不出现在快照里，其默认值由插件自己知道（默认 = 字段不存在）。
// 多选 note 时 NoteProperties 是各 note 的三态合并：同 key 全等给该值、不等给 PropertyValue.Multiple。
// 求 part config 时 NoteProperties 为空对象（part 不依赖 note）。读不到的 key 取到 Invalid，插件按自身默认 fallback。
// 冻结接口纯加性扩展：以后加只读属性（如 project 级 tempo/采样率）不破坏既有插件。
public interface IPropertyContext
{
    PropertyObject PartProperties { get; }
    PropertyObject NoteProperties { get; }
}
