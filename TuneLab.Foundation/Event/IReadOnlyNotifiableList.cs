namespace TuneLab.Foundation;

// 可索引特化：仅当背后数据结构真能 O(1) 随机访问时实现到这一层。
public interface IReadOnlyNotifiableList<out T> : IReadOnlyNotifiableCollection<T>, IReadOnlyList<T>
{
}

// 链表特化：头尾 O(1) 可达（空集合为 null），中间导航走成员自身的链（如 Next/Previous 邻居引用）。
// 额外继承 IReadOnlyCollection 以支持直接枚举与计数，但无随机索引承诺。
public interface IReadOnlyNotifiableLinkedList<out T> : IReadOnlyNotifiableCollection<T>
{
    T? First { get; }
    T? Last { get; }
}
