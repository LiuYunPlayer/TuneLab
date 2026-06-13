using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

public interface IReadOnlyDataLinkedList<out T> : IReadOnlyDataObject<IEnumerable<T>>, IReadOnlyDataCollection<T>, IReadOnlyLinkedList<T>
{
    // 成员增删事件（ItemAdded / ItemRemoved）+ 当前成员（Items）继承自 IReadOnlyDataCollection。
}
