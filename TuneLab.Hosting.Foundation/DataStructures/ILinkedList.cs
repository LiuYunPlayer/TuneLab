namespace TuneLab.Foundation;

// 无序链表的可变契约：写入口为"两端（AddFirst/AddLast）+ 相对节点（InsertAfter/InsertBefore）"，对元素顺序无约定。
// 按键自动定位的有序插入见 ISortedLinkedList<T>。
public interface ILinkedList<T> : IReadOnlyLinkedList<T> where T : class, ILinkedNode<T>
{
    void AddFirst(T item);
    void AddLast(T item);
    void InsertAfter(T last, T item);
    void InsertBefore(T next, T item);
    bool Remove(T item);
    void Clear();
    bool Contains(T item);
}
