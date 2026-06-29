using TuneLab.Foundation;

namespace TuneLab.SDK;

// 复合型：一组字段（PropertyKey→config，声明序即呈现序）。标签随 key 走、value 是纯 config。
// 不实现 IValueConfig——默认值由宿主递归各字段默认值求得。构造函数全封，只走静态工厂。
public sealed class ObjectConfig : IControllerConfig
{
    public IReadOnlyOrderedMap<PropertyKey, IControllerConfig> Properties { get; private init; } = null!;

    private ObjectConfig() { }

    public static ObjectConfig Create(IReadOnlyOrderedMap<PropertyKey, IControllerConfig> properties)
        => new() { Properties = properties };
}
