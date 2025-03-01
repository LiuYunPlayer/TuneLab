using TuneLab.Foundation.DataStructures;

namespace TuneLab.Foundation.Document;

public interface IDataList<T> : IDataObject<IEnumerable<T>>, IReadOnlyDataObject<List<T>>, IReadOnlyDataList<T>, IMutableList<T>
{
    new List<T> GetInfo();
    List<T> IReadOnlyDataObject<List<T>>.GetInfo() => GetInfo();
    IEnumerable<T> IReadOnlyDataObject<IEnumerable<T>>.GetInfo() => GetInfo();
}
