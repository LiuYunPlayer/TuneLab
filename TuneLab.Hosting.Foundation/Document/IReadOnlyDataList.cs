using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

public interface IReadOnlyDataList<out T> : IReadOnlyDataObject<IEnumerable<T>>, IReadOnlyDataCollection<T>, IReadOnlyList<T>
{
    // 成员增删事件（ItemAdded / ItemRemoved）+ 当前成员（Items）继承自 IReadOnlyDataCollection。
    IActionEvent<T, T> ItemReplaced { get; }
}
