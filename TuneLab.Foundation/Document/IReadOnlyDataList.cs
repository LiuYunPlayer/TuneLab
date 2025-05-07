using TuneLab.Foundation.Event;

namespace TuneLab.Foundation.Document;

public interface IReadOnlyDataList<out T> : IReadOnlyDataObject<IEnumerable<T>>, IReadOnlyDataCollection<T>, IReadOnlyList<T>
{
    IActionEvent<T, T> ItemReplaced { get; }
}
