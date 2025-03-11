using System.Collections;

namespace TuneLab.Foundation.Property;

public class PropertyArray : IContainerValue, IList<PropertyValue>
{
    public PropertyType Type => PropertyType.Array;
    public PropertyValue this[int index] { get => ((IList<PropertyValue>)mList!)[index]; set => ((IList<PropertyValue>)mList!)[index] = value; }
    public int Count => mList == null ? 0 : ((ICollection<PropertyValue>)mList).Count;
    public bool IsReadOnly => mList == null ? false : ((ICollection<PropertyValue>)mList).IsReadOnly;

    public void Add(PropertyValue item)
    {
        mList ??= [];
        ((ICollection<PropertyValue>)mList).Add(item);
    }

    public void Clear()
    {
        mList?.Clear();
    }

    public bool Contains(PropertyValue item)
    {
        return mList != null && ((ICollection<PropertyValue>)mList).Contains(item);
    }

    public void CopyTo(PropertyValue[] array, int arrayIndex)
    {
        mList?.CopyTo(array, arrayIndex);
    }

    public IEnumerator<PropertyValue> GetEnumerator()
    {
        return mList == null ? Enumerable.Empty<PropertyValue>().GetEnumerator() : ((IEnumerable<PropertyValue>)mList).GetEnumerator();
    }

    public int IndexOf(PropertyValue item)
    {
        return mList == null ? -1 : ((IList<PropertyValue>)mList).IndexOf(item);
    }

    public void Insert(int index, PropertyValue item)
    {
        mList ??= [];
        ((IList<PropertyValue>)mList).Insert(index, item);
    }

    public bool Remove(PropertyValue item)
    {
        return mList != null && ((ICollection<PropertyValue>)mList).Remove(item);
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

    List<PropertyValue>? mList;
}
