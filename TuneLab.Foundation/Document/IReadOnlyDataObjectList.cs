using TuneLab.Foundation.Event;

namespace TuneLab.Foundation.Document;

public interface IReadOnlyDataObjectList<out T> : IReadOnlyDataList<T> where T : IDataObject
{
    IActionEvent ListModified { get; }
}
