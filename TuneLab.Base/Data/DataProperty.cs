using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Utils;

namespace TuneLab.Base.Data;

public interface IReadOnlyDataProperty<out T> : IReadOnlyDataObject<T> where T : notnull
{
    T Value { get; }
}

public interface IDataProperty<T> : IDataObject<T>, IReadOnlyDataProperty<T> where T : notnull
{
    internal new class Wrapper(IDataProperty<T> dataProperty) : IDataObject<T>.Wrapper(dataProperty), IDataProperty<T>
    {
        public T Value => dataProperty.Value;
    }
}

public abstract class DataProperty<T>(DataObject? parent = null) : DataObject(parent), IDataProperty<T> where T : notnull
{
    public static implicit operator T(DataProperty<T> dataProperty) => dataProperty.GetInfo();
    public T Value => GetInfo();
    public abstract T GetInfo();
    protected abstract void SetInfo(T info);
    protected virtual void Set(T value) => IDataObject<T>.Set(this, value);
    void IDataObject<T>.SetInfo(T info) => SetInfo(info);
    void IDataObject<T>.Set(T value) => Set(value);
}

public class DataStruct<T>(DataObject? parent = null) : DataProperty<T>(parent) where T : struct
{
    public override T GetInfo()
    {
        return mValue;
    }

    protected override void SetInfo(T info)
    {
        mValue = info;
    }

    public override string? ToString()
    {
        return mValue.ToString();
    }

    T mValue = new();
}

public class DataString(DataObject? parent = null, string value = "") : DataProperty<string>(parent)
{
    public override string GetInfo()
    {
        return mValue;
    }

    protected override void SetInfo(string info)
    {
        mValue = info;
    }

    public override string? ToString()
    {
        return mValue.ToString();
    }

    string mValue = value;
}
