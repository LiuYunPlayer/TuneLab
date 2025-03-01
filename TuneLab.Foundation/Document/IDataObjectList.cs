namespace TuneLab.Foundation.Document;

public interface IDataObjectList<T> : IDataList<T>, IReadOnlyDataObjectList<T> where T : IDataObject
{

}
