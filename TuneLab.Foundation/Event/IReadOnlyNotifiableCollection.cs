namespace TuneLab.Foundation;

// 名副其实的可观察集合：在可枚举通知（IReadOnlyNotifiableEnumerable，含成员增删与聚合信号
// MembershipModified）之上加 Count（IReadOnlyCollection）。
public interface IReadOnlyNotifiableCollection<out T> : IReadOnlyNotifiableEnumerable<T>, IReadOnlyCollection<T>
{
}
