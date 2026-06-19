using TuneLab.Foundation;

namespace TuneLab.SDK;

// 复合型：一组字段（PropertyKey→config，声明序即呈现序）。标签随 key 走、value 是纯 config。
// 不实现 IValueConfig——默认值由宿主递归各字段默认值求得。
public class ObjectConfig : IControllerConfig
{
    public required IReadOnlyOrderedMap<PropertyKey, IControllerConfig> Properties { get; init; }
}
