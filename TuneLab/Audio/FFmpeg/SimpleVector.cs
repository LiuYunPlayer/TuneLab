using System;

namespace TuneLab.Audio.FFmpeg;

public class SimpleVector<T>
{
    private T[] _data;
    private int _size;
    private int _capacity;

    public SimpleVector(int capacity = 10)
    {
        _size = 0;
        _capacity = capacity;
        _data = new T[_capacity];
    }

    public int Size => _size;
    public int Capacity => _capacity;
    public T this[int index] => _data[index];
    public T[] Data => _data;

    public void Append(T item)
    {
        var newSize = _size + 1;
        if (newSize > _capacity)
        {
            AllocateSpace(newSize * 2);
        }

        _data[_size] = item;
        _size = newSize;
    }

    public void Append(params T[] items)
    {
        var newSize = _size + items.Length;
        if (newSize > _capacity)
        {
            AllocateSpace(newSize * 2);
        }

        Array.Copy(items, 0, _data, _size, items.Length);
        _size = newSize;
    }

    public void Resize(int newSize)
    {
        if (newSize < 0)
            throw new ArgumentOutOfRangeException(nameof(newSize), "New size must be non-negative.");
        if (newSize > _capacity)
        {
            AllocateSpace(newSize);
        }

        _size = newSize;
    }

    public void Reserve(int newCapacity)
    {
        if (newCapacity > _capacity)
        {
            AllocateSpace(newCapacity);
        }
    }

    private void AllocateSpace(int capacity)
    {
        var newItems = new T[capacity];
        Array.Copy(_data, 0, newItems, 0, Math.Min(_size, capacity));
        _data = newItems;
        _capacity = capacity;
    }
}