using TuneLab.Foundation;

namespace TuneLab.Foundation;

// 属性树的只读订阅外观：对象节点可导航、叶子可读值，节点本身是事件面
// （IReadOnlyNotifiable，节点内任何字段/嵌套子树变更均触发）。
// 跨 SDK 边界递给插件：插件经下方字段扩展把某个 key 取成 IReadOnlyNotifiableProperty<T>，
// 当普通可订阅属性读值/订阅——与宿主数据层 IDataPropertyObject 的字段扩展同构，但只读
// （无 Set、无撤销面）。宿主侧的可写 property object 实现本接口即可直接递入。
public interface IReadOnlyNotifiablePropertyObject : IReadOnlyNotifiable
{
    // 导航到嵌套对象节点（lazy：key 不存在返回空外观，订阅依然有效——将来该 key 下建出对象时照常通知）。
    IReadOnlyNotifiablePropertyObject Object(string key);
    // 本层叶子读（裸 PropertyValue 出；typed 视图由字段扩展提供）。
    PropertyValue GetValue(string key, PropertyValue defaultValue);
}

public static class IReadOnlyNotifiablePropertyObjectExtension
{
    public static IReadOnlyNotifiableProperty<double> NumberField(this IReadOnlyNotifiablePropertyObject propertyObject, string key, double defaultValue)
    {
        return new ReadOnlyPropertyField<double>(propertyObject, key, PropertyValue.Create(defaultValue),
            v => v.ToDouble(out var value) ? value : defaultValue);
    }

    public static IReadOnlyNotifiableProperty<string> StringField(this IReadOnlyNotifiablePropertyObject propertyObject, string key, string defaultValue)
    {
        return new ReadOnlyPropertyField<string>(propertyObject, key, PropertyValue.Create(defaultValue),
            v => v.ToString(out var value) ? value : defaultValue);
    }

    public static IReadOnlyNotifiableProperty<bool> BoolField(this IReadOnlyNotifiablePropertyObject propertyObject, string key, bool defaultValue)
    {
        return new ReadOnlyPropertyField<bool>(propertyObject, key, PropertyValue.Create(defaultValue),
            v => v.ToBool(out var value) ? value : defaultValue);
    }

    // 裸值字段：读未 coerce 的 PropertyValue，供值类型不定的消费方自行判型。
    public static IReadOnlyNotifiableProperty<PropertyValue> ValueField(this IReadOnlyNotifiablePropertyObject propertyObject, string key, PropertyValue defaultValue)
    {
        return new ReadOnlyPropertyField<PropertyValue>(propertyObject, key, defaultValue, v => v);
    }

    // 字段适配器：借壳节点事件（节点内兄弟字段变更同样触发，要精确过滤的订阅者自行比较值），
    // 读时按需 coerce 成 T、失败回 default。
    sealed class ReadOnlyPropertyField<T>(IReadOnlyNotifiablePropertyObject propertyObject, string key, PropertyValue defaultValue, Func<PropertyValue, T> read)
        : IReadOnlyNotifiableProperty<T>
    {
        public T Value => read(propertyObject.GetValue(key, defaultValue));
        public IActionEvent WillModify => propertyObject.WillModify;
        public IActionEvent Modified => propertyObject.Modified;
    }
}
