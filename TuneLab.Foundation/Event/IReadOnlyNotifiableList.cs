namespace TuneLab.Foundation;

// 可订阅只读集合（无索引承诺）：结构变更（增删）带成员引用逐项通知，Modified 是任何
// 结构变更后的聚合信号。成员自身的字段变化不经此处——订阅成员的
// IReadOnlyNotifiableProperty（WhenAny 自动管理接线）。
// 不承诺随机访问：宿主数据结构可能是链表（O(1) 索引给不出来），顺序消费用枚举、
// 邻居导航走成员自身的链（如 ISynthesisNote.Next/Last）。
public interface IReadOnlyNotifiableCollection<T> : IReadOnlyCollection<T>
{
    event Action<T>? ItemAdded;
    event Action<T>? ItemRemoved;
    event Action? Modified;
}

// 可索引特化：仅当背后数据结构真能 O(1) 随机访问时实现到这一层。
public interface IReadOnlyNotifiableList<T> : IReadOnlyNotifiableCollection<T>, IReadOnlyList<T>
{
}

// 链表特化：头尾 O(1) 可达（空集合为 null），中间导航走成员自身的链（如 Next/Last 邻居引用），
// 依旧无索引承诺。
public interface IReadOnlyNotifiableLinkedList<T> : IReadOnlyNotifiableCollection<T>
{
    T? First { get; }
    T? Last { get; }
}
