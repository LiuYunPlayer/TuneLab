using TuneLab.Foundation.Event;

namespace TuneLab.Foundation.Document;

public interface IReadOnlyDataObjectMap<TKey, TValue> : IReadOnlyDataMap<TKey, TValue>, IReadOnlyDataCollection<TValue> where TKey : notnull where TValue : IDataObject
{
    IActionEvent MapModified { get; }
}
