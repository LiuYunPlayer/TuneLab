using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

public interface IReadOnlyDataObjectList<out T> : IReadOnlyDataList<T> where T : IDataObject
{
    // 仅列表结构变更（成员增删/替换）；元素内容的深层变更走继承自 IDataObject 的 Modified。
    IModifiedEvent ListModified { get; }
}
