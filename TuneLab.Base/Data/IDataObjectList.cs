namespace TuneLab.Base.Data;

public interface IDataObjectList<T> : IDataList<T>, IReadOnlyDataObjectList<T> where T : IDataObject
{

}
