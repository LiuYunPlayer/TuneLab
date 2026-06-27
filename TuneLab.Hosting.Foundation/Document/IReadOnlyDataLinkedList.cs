using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

// 成员增删/聚合通知（ItemAdded/ItemRemoved/StructureModified）+ 计数继承自 SDK 的 IReadOnlyNotifiableLinkedList；
// 反向枚举（Inverse）由宿主 IReadOnlyLinkedList 补充。两个基接口都声明了 First/Last（签名一致），用 new 在此合一，
// 消除消费者侧的二义（CS0229），实现类仍只需提供一份 First/Last。
public interface IReadOnlyDataLinkedList<out T> : IReadOnlyNotifiableLinkedList<T>, IReadOnlyLinkedList<T>, IReadOnlyDataObject<IEnumerable<T>>
{
    new T? First { get; }
    new T? Last { get; }
}
