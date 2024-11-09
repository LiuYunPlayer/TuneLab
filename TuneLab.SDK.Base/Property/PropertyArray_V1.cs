using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base;

public class PropertyArray_V1 : IContainerValue_V1, IList<PropertyValue_V1>
{
    public PropertyValue_V1 this[int index] { get => ((IList<PropertyValue_V1>)mList!)[index]; set => ((IList<PropertyValue_V1>)mList!)[index] = value; }
    public int Count => mList == null ? 0: ((ICollection<PropertyValue_V1>)mList).Count;
    public bool IsReadOnly => mList == null ? true: ((ICollection<PropertyValue_V1>)mList).IsReadOnly;

    public void Add(PropertyValue_V1 item)
    {
        mList ??= [];
        ((ICollection<PropertyValue_V1>)mList).Add(item);
    }

    public void Clear()
    {
        mList?.Clear();
    }

    public bool Contains(PropertyValue_V1 item)
    {
        return mList != null && ((ICollection<PropertyValue_V1>)mList).Contains(item);
    }

    public void CopyTo(PropertyValue_V1[] array, int arrayIndex)
    {
        mList?.CopyTo(array, arrayIndex);
    }

    public IEnumerator<PropertyValue_V1> GetEnumerator()
    {
        return mList == null ? Enumerable.Empty<PropertyValue_V1>().GetEnumerator() : ((IEnumerable<PropertyValue_V1>)mList).GetEnumerator();
    }

    public int IndexOf(PropertyValue_V1 item)
    {
        return mList == null ? -1: ((IList<PropertyValue_V1>)mList).IndexOf(item);
    }

    public void Insert(int index, PropertyValue_V1 item)
    {
        mList ??= [];
        ((IList<PropertyValue_V1>)mList).Insert(index, item);
    }

    public bool Remove(PropertyValue_V1 item)
    {
        return mList != null && ((ICollection<PropertyValue_V1>)mList).Remove(item);
    }

    public void RemoveAt(int index)
    {
        mList?.RemoveAt(index);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    bool IEquatable<IPropertyValue_V1>.Equals(IPropertyValue_V1? other)
    {
        if (other is not PropertyArray_V1 property)
            return false;

        if (mList == property.mList)
            return true;

        if (mList == null || property.mList == null)
            return false;

        return mList.SequenceEqual(property.mList);
    }

    List<PropertyValue_V1>? mList;
}
