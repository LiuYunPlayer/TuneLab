using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

public interface IReadOnlyDataObjectLinkedList<out T> : IReadOnlyDataLinkedList<T> where T : IDataObject
{
    // 仅链表结构变更（节点增删）；元素内容的深层变更走继承自 IDataObject 的 Modified。
    // 用 new 强化继承自 IReadOnlyNotifiableEnumerable 的 IActionEvent StructureModified，给宿主消费者拿带 canIgnore 的富版。
    new IModifiedEvent StructureModified { get; }

    // 最小面适配（DIM）：SDK 的 IActionEvent 面直接由富事件 IModifiedEvent 满足（IModifiedEvent : IActionEvent），
    // 实现类只需提供富版即可同时满足 Enumerable 的 IActionEvent 面。
    IActionEvent IReadOnlyNotifiableEnumerable<T>.StructureModified => StructureModified;
}
