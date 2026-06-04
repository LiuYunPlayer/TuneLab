using System;
using TuneLab.Foundation.Document;
using TuneLab.Primitives.Property;

namespace TuneLab.Foundation.Property;

// 属性面板的可绑定数据外观：把一个属性源（单个 DataPropertyObject，或多选时的多个对象合一）
// 暴露成"按 PropertyPath.Key 取/写值 + 提供一个文档连接对象作为撤销根"的统一面。
// 控件由此逐字段拿到 IDataProperty<T> 直接绑定，复用既有单属性撤销/刷新机制。
public interface IDataPropertyObject
{
    // 字段绑定所依附的文档连接对象：其 Head/Commit/DiscardTo/Modified 用于撤销分组与刷新。
    // 单对象即自身；多选时取其中之一（同一文档共享一个 Head，提交即归一个撤销单元）。
    IDataObject DataRoot { get; }
    PropertyValue GetValue(PropertyPath.Key key, PropertyValue defaultValue);
    void SetValue(PropertyPath.Key key, PropertyValue value);
}

public static class IDataPropertyObjectExtension
{
    public static IDataProperty<double> NumberField(this IDataPropertyObject dataObject, PropertyPath.Key key, double defaultValue)
    {
        return new PropertyField<double>(dataObject, key, PropertyValue.Create(defaultValue),
            v => v.ToDouble(out var value) ? value : defaultValue, value => PropertyValue.Create(value));
    }

    public static IDataProperty<string> StringField(this IDataPropertyObject dataObject, PropertyPath.Key key, string defaultValue)
    {
        return new PropertyField<string>(dataObject, key, PropertyValue.Create(defaultValue),
            v => v.ToString(out var value) ? value : defaultValue, value => PropertyValue.Create(value));
    }

    public static IDataProperty<bool> BoolField(this IDataPropertyObject dataObject, PropertyPath.Key key, bool defaultValue)
    {
        return new PropertyField<bool>(dataObject, key, PropertyValue.Create(defaultValue),
            v => v.ToBool(out var value) ? value : defaultValue, value => PropertyValue.Create(value));
    }

    // 字段适配器：包裹数据源的撤销根（Head/Commit/DiscardTo/Modified 全经 Wrapper 转发到文档），
    // 仅把读写转成具体类型并直接走 GetValue/SetValue（写入由底层 DataPropertyValue 自带的撤销命令承担，
    // 故 Set 直达 SetValue、不经基类命令机制——避免一次编辑产生两条撤销记录）。
    class PropertyField<T>(IDataPropertyObject dataObject, PropertyPath.Key key, PropertyValue defaultValue, Func<PropertyValue, T> read, Func<T, PropertyValue> write)
        : IDataObject.Wrapper(dataObject.DataRoot), IDataProperty<T> where T : notnull
    {
        public T Value => read(dataObject.GetValue(key, defaultValue));
        public T GetInfo() => Value;
        void IDataObject<T>.SetInfo(T value) => dataObject.SetValue(key, write(value));
        void IDataProperty<T>.Set(T value) => dataObject.SetValue(key, write(value));
    }
}
