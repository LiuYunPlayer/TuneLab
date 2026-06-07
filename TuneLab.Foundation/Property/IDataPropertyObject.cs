using System;
using TuneLab.Foundation.Document;
using TuneLab.Primitives.Property;

namespace TuneLab.Foundation.Property;

// 属性面板的导航式数据外观：路径上每个节点都是 IDataPropertyObject（对象节点）或 IDataProperty<T>（叶子），
// 拿到就当普通数据对象用——绑定/观察/撤销一视同仁。节点本身即撤销根（故继承 IDataObject）。
// 嵌套对象经 Object(key) 逐层导航（lazy：读不存在返回 default、写时按需建路径），不再用扁平深路径寻址。
public interface IDataPropertyObject : IDataObject
{
    // 导航到嵌套对象节点：返回可继续导航的子对象外观。
    IDataPropertyObject Object(string key);
    // 本层叶子读/写（裸 PropertyValue 进出；typed 视图由下方字段扩展提供）。
    PropertyValue GetValue(string key, PropertyValue defaultValue);
    void SetValue(string key, PropertyValue value);
}

// 字段向 UI 绑定暴露未 coerce 的原始值，使绑定能区分 具体值 / Multiple（多值）/ Invalid（无值）三态。
// 绑定按原始值的 IsMultiple()/IsInvalid() 分派到控件的 DisplayMultiple/DisplayNull，否则才 coerce 成 T 走 Display。
public interface IRawValueProperty
{
    PropertyValue RawValue { get; }
}

public static class IDataPropertyObjectExtension
{
    public static IDataProperty<double> NumberField(this IDataPropertyObject dataObject, string key, double defaultValue)
    {
        return new PropertyField<double>(dataObject, key, PropertyValue.Create(defaultValue),
            v => v.ToDouble(out var value) ? value : defaultValue, value => PropertyValue.Create(value));
    }

    public static IDataProperty<string> StringField(this IDataPropertyObject dataObject, string key, string defaultValue)
    {
        return new PropertyField<string>(dataObject, key, PropertyValue.Create(defaultValue),
            v => v.ToString(out var value) ? value : defaultValue, value => PropertyValue.Create(value));
    }

    public static IDataProperty<bool> BoolField(this IDataPropertyObject dataObject, string key, bool defaultValue)
    {
        return new PropertyField<bool>(dataObject, key, PropertyValue.Create(defaultValue),
            v => v.ToBool(out var value) ? value : defaultValue, value => PropertyValue.Create(value));
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
        void IDataObject<T>.SetInfo(T value) => dataObject.SetValue(key, write(value));
        void IDataProperty<T>.Set(T value) => dataObject.SetValue(key, write(value));
    }
}
