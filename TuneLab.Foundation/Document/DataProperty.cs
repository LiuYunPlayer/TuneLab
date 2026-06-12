using System;
using TuneLab.Primitives.Event;

namespace TuneLab.Foundation.Document;

// 叶子属性面：读值 O(1)，故继承到 IReadOnlyNotifiableProperty<T>（Value 由其声明）；
// 复合对象停在 IDataObject : IReadOnlyNotifiable（只可订阅，不伪装 O(n) GetInfo 为属性值）。
public interface IReadOnlyDataProperty<out T> : IReadOnlyDataObject<T>, IReadOnlyNotifiableProperty<T> where T : notnull
{
}

public interface IDataProperty<T> : IDataObject<T>, IReadOnlyDataProperty<T> where T : notnull
{
    // 交互式设值：可带额外逻辑/副作用（见 DataProperty<T>.Set 的可覆写点，如 DataLyric 改词清音素）。
    // 用户编辑走 Set；纯应用 / 装载 / 复合扇出走 IDataObject<T>.SetInfo（无副作用）。
    void Set(T value);
}

// 叶子数据对象：读/写原语是方法 GetInfo / SetValue（虚/抽象，子类按需定制），Value 是非虚只读 getter 指向 GetInfo。
// 拥有自己的 ModifyCommand（SetValue 是裸写：仅落值、不去重/不记命令/不判副作用，只被命令与装载用）。
public abstract class DataProperty<T>(DataObject? parent = null) : DataObject(parent), IDataProperty<T> where T : notnull
{
    public static implicit operator T(DataProperty<T> dataProperty) => dataProperty.Value;

    public T Value => GetInfo();

    public abstract T GetInfo();

    protected abstract void SetValue(T value);

    // 纯契约设值：去重 + 记命令，无交互副作用。构造期未接到 document 时命令被丢弃（仅应用）。
    public void SetInfo(T value)
    {
        var before = GetInfo();
        if (Equals(before, value))
            return;

        PushAndDo(new ModifyCommand(this, before, value));
    }

    // 交互式设值：默认等同 SetInfo；带副作用的叶子覆写此处叠加逻辑（叠加后调 base.Set 落值）。
    public virtual void Set(T value) => SetInfo(value);

    public override string? ToString() => Value.ToString();

    void SetValueAndNotify(T value)
    {
        NotifyWill();
        SetValue(value);
        Notify();
    }

    class ModifyCommand(DataProperty<T> property, T before, T after) : ICommand
    {
        public void Redo() => property.SetValueAndNotify(after);
        public void Undo() => property.SetValueAndNotify(before);
    }
}

public class DataStruct<T>(DataObject? parent = null) : DataProperty<T>(parent) where T : struct
{
    public override T GetInfo() => mValue;
    protected override void SetValue(T value) => mValue = value;

    T mValue;
}

public class DataString(DataObject? parent = null, string value = "") : DataProperty<string>(parent)
{
    public override string GetInfo() => mValue;
    protected override void SetValue(string value) => mValue = value;

    string mValue = value;
}
