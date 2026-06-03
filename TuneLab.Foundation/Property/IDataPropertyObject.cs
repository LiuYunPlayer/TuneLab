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
        return new NumberProperty(dataObject, key, defaultValue);
    }

    public static IDataProperty<string> StringField(this IDataPropertyObject dataObject, PropertyPath.Key key, string defaultValue)
    {
        return new StringProperty(dataObject, key, defaultValue);
    }

    public static IDataProperty<bool> BoolField(this IDataPropertyObject dataObject, PropertyPath.Key key, bool defaultValue)
    {
        return new BoolProperty(dataObject, key, defaultValue);
    }

    // 字段适配器：包裹数据源的撤销根（Head/Commit/DiscardTo/Modified 全经 Wrapper 转发到文档），
    // 仅把读写转成具体类型并直接走 GetValue/SetValue（写入由底层 DataPropertyValue 自带的撤销命令承担，
    // 故覆写 Set 绕过基类会重复入栈的命令机制——避免一次编辑产生两条撤销记录）。
    class NumberProperty(IDataPropertyObject dataObject, PropertyPath.Key key, double defaultValue) : IDataObject.Wrapper(dataObject.DataRoot), IDataProperty<double>
    {
        public double Value => dataObject.GetValue(key, PropertyValue.Create(defaultValue)).ToDouble(out var value) ? value : defaultValue;
        public double GetInfo() => Value;
        void IDataObject<double>.Set(double value) => dataObject.SetValue(key, PropertyValue.Create(value));
        void IDataObject<double>.SetInfo(double info) => dataObject.SetValue(key, PropertyValue.Create(info));
    }

    class StringProperty(IDataPropertyObject dataObject, PropertyPath.Key key, string defaultValue) : IDataObject.Wrapper(dataObject.DataRoot), IDataProperty<string>
    {
        public string Value => dataObject.GetValue(key, PropertyValue.Create(defaultValue)).ToString(out var value) ? value : defaultValue;
        public string GetInfo() => Value;
        void IDataObject<string>.Set(string value) => dataObject.SetValue(key, PropertyValue.Create(value));
        void IDataObject<string>.SetInfo(string info) => dataObject.SetValue(key, PropertyValue.Create(info));
    }

    class BoolProperty(IDataPropertyObject dataObject, PropertyPath.Key key, bool defaultValue) : IDataObject.Wrapper(dataObject.DataRoot), IDataProperty<bool>
    {
        public bool Value => dataObject.GetValue(key, PropertyValue.Create(defaultValue)).ToBool(out var value) ? value : defaultValue;
        public bool GetInfo() => Value;
        void IDataObject<bool>.Set(bool value) => dataObject.SetValue(key, PropertyValue.Create(value));
        void IDataObject<bool>.SetInfo(bool info) => dataObject.SetValue(key, PropertyValue.Create(info));
    }
}
