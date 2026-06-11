namespace TuneLab.Primitives.Event;

// 可订阅只读列表：结构变更（增删）带成员引用逐项通知，Modified 是任何结构变更后的聚合信号。
// 成员自身的字段变化不经此处——订阅成员的 IReadOnlyNotifiableProperty（WhenAny 自动管理接线）。
public interface IReadOnlyNotifiableList<T> : IReadOnlyList<T>
{
    event Action<T>? ItemAdded;
    event Action<T>? ItemRemoved;
    event Action? Modified;
}
