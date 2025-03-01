namespace TuneLab.Foundation.Document;

internal interface IDataObjectMap<TKey, TValue> : IDataMap<TKey, TValue>, IReadOnlyDataObjectMap<TKey, TValue> where TKey : notnull where TValue : IDataObject
{

}
