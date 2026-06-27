namespace TuneLab.Foundation;

// 有序链表的可变契约：唯一插入口是按序定位的 Insert(item)，刻意不提供任何"两端/相对节点"的定位口，
// 从类型上保证有序不变量无法被旁路破坏。与无序的 ILinkedList<T> 互为 IReadOnlyLinkedList<T> 下的平行支。
public interface ISortedLinkedList<T> : IReadOnlyLinkedList<T> where T : class, ILinkedNode<T>
{
    void Insert(T item);
    bool Remove(T item);
    void Clear();
    bool Contains(T item);
}
