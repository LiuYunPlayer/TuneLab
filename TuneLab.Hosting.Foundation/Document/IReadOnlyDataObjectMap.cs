using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

public interface IReadOnlyDataObjectMap<TKey, TValue> : IReadOnlyDataMap<TKey, TValue>, IReadOnlyDataCollection<TValue> where TKey : notnull where TValue : IDataObject
{
    // 仅映射结构变更（键增删/值替换）；元素内容的深层变更走继承自 IDataObject 的 Modified。
    IModifiedEvent MapModified { get; }
}
