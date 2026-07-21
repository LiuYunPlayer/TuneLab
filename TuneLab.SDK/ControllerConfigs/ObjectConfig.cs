using TuneLab.Foundation;

namespace TuneLab.SDK;

// 复合型：一组字段（PropertyKey→config，声明序即呈现序）。标签随 key 走、value 是纯 config。
// 不实现 IValueConfig——默认值由宿主递归各字段默认值求得。构造函数全封，只走静态工厂。
// 构造即拷贝传入 map（[.. properties] 经 IReadOnlyOrderedMap 的 CollectionBuilder 物化）：值语义由构造保证，
// 与调用方构造后对原 map 的改动无关；空态自动落到真不可变空 map 单例。
public sealed class ObjectConfig : IControllerConfig
{
    public IReadOnlyOrderedMap<PropertyKey, IControllerConfig> Properties { get; private init; } = null!;

    private ObjectConfig() { }

    public static ObjectConfig Create(IReadOnlyOrderedMap<PropertyKey, IControllerConfig> properties)
        => new() { Properties = [.. properties] };
}
