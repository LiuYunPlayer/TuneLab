using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

// 有序可重复列表的导航式数据外观，承 IDataPropertyObject 的 token-as-key 范式：
// 元素以「稳定 token」寻址——token 是元素槽的身份，跨增删/undo/redo 不变（底层 DataObjectList 给每个元素槽稳定实例身份）。
// token 当 key 复用 Object(token)/Array(token)/GetValue/SetValue，面板用现成的字段绑定机制原位 live-bind 每个元素、无需感知 index。
// 与 IDataPropertyObject 的差别：键集有序（Tokens）、可结构性增删（Insert/Add/RemoveAt 按 index）、
// 结构变化单独成事件（MembershipModified，只在增删/重排触发；元素值原位编辑不触发）。
public interface IDataPropertyArray : IDataPropertyObject
{
    int Count { get; }
    // 有序稳定的元素寻址 token：第 i 个 token 对应第 i 个元素槽，顺序随列表，身份跨 undo/redo 稳定。
    IReadOnlyList<string> Tokens { get; }
    // 结构变化通知（增删/重排）；元素值原位编辑不触发——区别于借壳节点的 Modified（含值变更）。
    IModifiedEvent MembershipModified { get; }

    void Insert(int index, PropertyValue value);
    void Add(PropertyValue value);
    void RemoveAt(int index);
}
