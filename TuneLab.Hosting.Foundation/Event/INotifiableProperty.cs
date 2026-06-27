using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation;

// 可写可通知属性 = 只读可通知属性 + 写。WillModify/Modified 继承自 IReadOnlyNotifiable。
public interface INotifiableProperty<T> : IReadOnlyNotifiableProperty<T>
{
    new T Value { get; set; }
}
