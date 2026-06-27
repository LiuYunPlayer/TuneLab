using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

// 成员增删/聚合通知（ItemAdded/ItemRemoved/StructureModified）+ 计数 + 随机索引继承自 SDK 的 IReadOnlyNotifiableList。
public interface IReadOnlyDataList<out T> : IReadOnlyNotifiableList<T>, IReadOnlyDataObject<IEnumerable<T>>
{
    IActionEvent<T, T> ItemReplaced { get; }
}
