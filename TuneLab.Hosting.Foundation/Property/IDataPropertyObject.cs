using System;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

// 属性面板的导航式数据外观：路径上每个节点都是 IDataPropertyObject（对象节点）或 IDataProperty<T>（叶子），
// 拿到就当普通数据对象用——绑定/观察/撤销一视同仁。节点本身即撤销根（故继承 IDataObject）。
// 嵌套对象经 Object(key) 逐层导航（lazy：读不存在返回 default、写时按需建路径），不再用扁平深路径寻址。
// 同时继承 SDK 最小订阅面（只读属性树外观）：富导航与只读导航同名共存，
// cast 到 IReadOnlyNotifiablePropertyObject 得只读面——适配由下方 DIM 一次完成，实现类零样板。
public interface IDataPropertyObject : IDataObject, IReadOnlyNotifiablePropertyObject
{
    // 导航到嵌套对象节点：返回可继续导航的子对象外观。
    new IDataPropertyObject Object(string key);
    // 导航到嵌套数组节点（有序可重复列表）：返回可继续导航/增删的子数组外观（lazy：key 不存在返回空外观，写时按需建路径）。
    IDataPropertyArray Array(string key);
    // 本层叶子读/写（裸 PropertyValue 进出；typed 视图由下方字段扩展提供）。
    new PropertyValue GetValue(string key, PropertyValue defaultValue);
    void SetValue(string key, PropertyValue value);
    // 移除本层某键（presence：把 present 翻回 absent）。缺键 = no-op；不下钻。变长键控容器（ExtensibleObjectConfig）删条目用。
    void RemoveValue(string key);

    IReadOnlyNotifiablePropertyObject IReadOnlyNotifiablePropertyObject.Object(string key) => Object(key);
    PropertyValue IReadOnlyNotifiablePropertyObject.GetValue(string key, PropertyValue defaultValue) => GetValue(key, defaultValue);
}

// 字段向 UI 绑定暴露未 coerce 的原始值，使绑定能区分 具体值 / Multiple（多值）/ Invalid（无值）三态。
// 绑定按原始值的 IsMultiple()/IsNull() 分派到控件的 DisplayMultiple/DisplayNull，否则才 coerce 成 T 走 Display。
public interface IRawValueProperty
{
    PropertyValue RawValue { get; }
}

public static class IDataPropertyObjectExtension
{
    public static IDataProperty<double> DoubleField(this IDataPropertyObject dataObject, string key, double defaultValue)
    {
        return new PropertyField<double>(dataObject, key, PropertyValue.Create(defaultValue),
            v => v.ToDouble(out var value) ? value : defaultValue, value => PropertyValue.Create(value));
    }

    public static IDataProperty<string> StringField(this IDataPropertyObject dataObject, string key, string defaultValue)
    {
        return new PropertyField<string>(dataObject, key, PropertyValue.Create(defaultValue),
            v => v.ToString(out var value) ? value : defaultValue, value => PropertyValue.Create(value));
    }

    public static IDataProperty<bool> BooleanField(this IDataPropertyObject dataObject, string key, bool defaultValue)
    {
        return new PropertyField<bool>(dataObject, key, PropertyValue.Create(defaultValue),
            v => v.ToBoolean(out var value) ? value : defaultValue, value => PropertyValue.Create(value));
    }

    // 裸值字段：读写都是未 coerce 的 PropertyValue（identity 适配器），供值类型不定的控件（如 option 可为任意基础类型的
    // ComboBox）按原始值绑定。三态（Multiple/Invalid）经 IRawValueProperty.RawValue 由绑定层分派，Display 只收具体值。
    public static IDataProperty<PropertyValue> ValueField(this IDataPropertyObject dataObject, string key, PropertyValue defaultValue)
    {
        return new PropertyField<PropertyValue>(dataObject, key, defaultValue, v => v, value => value);
    }

    // 字段适配器：借壳节点本身（Head/Commit/DiscardTo/Modified 经 Wrapper 转发到文档撤销根），
    // 仅把读写转成具体类型并直接走节点的 GetValue/SetValue（写入由底层 DataPropertyValue 自带的撤销命令承担，
    // 故 Set 直达 SetValue、不经基类命令机制——避免一次编辑产生两条撤销记录）。
    class PropertyField<T>(IDataPropertyObject dataObject, string key, PropertyValue defaultValue, Func<PropertyValue, T> read, Func<T, PropertyValue> write)
        : IDataObject.Wrapper(dataObject), IDataProperty<T>, IRawValueProperty where T : notnull
    {
        public PropertyValue RawValue => dataObject.GetValue(key, defaultValue);
        public T Value => read(RawValue);
        public T GetInfo() => Value;
        public void SetInfo(T value) => dataObject.SetValue(key, write(value));
        public void Set(T value) => dataObject.SetValue(key, write(value));
    }
}
