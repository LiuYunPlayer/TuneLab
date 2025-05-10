using System.Collections;

namespace TuneLab.Foundation.Property;

public class PropertyArray : IList<IPropertyValue>
{
    public PropertyType Type => PropertyType.Array;
    public IPropertyValue this[int index] { get => ((IList<IPropertyValue>)mList!)[index]; set => ((IList<IPropertyValue>)mList!)[index] = value; }
    public int Count => mList == null ? 0 : ((ICollection<IPropertyValue>)mList).Count;
    public bool IsReadOnly => mList == null ? false : ((ICollection<IPropertyValue>)mList).IsReadOnly;

    public void Add(IPropertyValue item)
    {
        mList ??= [];
        ((ICollection<IPropertyValue>)mList).Add(item);
    }

    public void Clear()
    {
        mList?.Clear();
    }

    public bool Contains(IPropertyValue item)
    {
        return mList != null && ((ICollection<IPropertyValue>)mList).Contains(item);
    }

    public void CopyTo(IPropertyValue[] array, int arrayIndex)
    {
        mList?.CopyTo(array, arrayIndex);
    }

    public IEnumerator<IPropertyValue> GetEnumerator()
    {
        return mList == null ? Enumerable.Empty<IPropertyValue>().GetEnumerator() : ((IEnumerable<IPropertyValue>)mList).GetEnumerator();
    }

    public int IndexOf(IPropertyValue item)
    {
        return mList == null ? -1 : ((IList<IPropertyValue>)mList).IndexOf(item);
    }

    public void Insert(int index, IPropertyValue item)
    {
        mList ??= [];
        ((IList<IPropertyValue>)mList).Insert(index, item);
    }

    public bool Remove(IPropertyValue item)
    {
        return mList != null && ((ICollection<IPropertyValue>)mList).Remove(item);
    }

    public void RemoveAt(int index)
    {
        mList?.RemoveAt(index);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /*bool IEquatable<IPropertyValue>.Equals(IPropertyValue? other)
    {
        if (other is not PropertyArray property)
            return false;

        if (mList == property.mList)
            return true;

        if (mList == null || property.mList == null)
            return false;

        return mList.SequenceEqual(property.mList);
    }*/

    List<IPropertyValue>? mList;
}
