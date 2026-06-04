using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Event;

namespace TuneLab.Foundation.Document;

public interface IReadOnlyDataObjectLinkedList<out T> : IReadOnlyDataLinkedList<T>, IReadOnlyDataObjectCollection<T> where T : IDataObject
{
    // 仅链表结构变更（节点增删）；元素内容的深层变更走继承自 IDataObject 的 Modified。
    IModifiedEvent ListModified { get; }
}
