using TuneLab.Base.Event;

namespace TuneLab.Base.Data;

public interface IReadOnlyDataObjectList<out T> : IReadOnlyDataList<T>, IReadOnlyDataObjectCollection<T> where T : IDataObject
{
    IActionEvent ListModified { get; }
}
