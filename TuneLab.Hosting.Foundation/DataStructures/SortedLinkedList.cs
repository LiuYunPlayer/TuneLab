using System;
using System.Collections;

namespace TuneLab.Foundation;

// 有序链表：组合持有一个纯 LinkedList<T> 作为拼接底座，对外只暴露按序定位的 Insert + Remove + 只读视图。
// 底座的两端/相对节点定位口因 mList 是私有字段而只对本类可见（比 protected 更紧的封装），有序不变量无法被外部旁路。
// 约定 isInOrder(prev, next) 在 prev 应排在 next 之前或与之并列时返回 true（"非降序"，相等也算有序，等键之后稳定追加）。
//
// 游标优化：mCursor 指向最近一次插入处，定位扫描从该处就近双向展开，故"在同一位置连续灌入大量元素"接近 O(1)；
// 游标在其元素被移除/清空后失效，下次 Insert 惰性退回从表尾重定位。
public sealed class SortedLinkedList<T> : ISortedLinkedList<T> where T : class, ILinkedNode<T>
{
    public SortedLinkedList(Func<T, T, bool> isInOrder)
    {
        mIsInOrder = isInOrder;
    }

    public T? First => mList.First;
    public T? Last => mList.Last;
    public int Count => mList.Count;

    public bool Contains(T item) => mList.Contains(item);

    public void Insert(T item)
    {
        if (mList.Count == 0)
        {
            mList.AddLast(item);
            mCursor = item;
            return;
        }

        // 游标在其指向的元素被移除/清空后会失效（脱离底座），失效则退回从表尾重新定位。
        if (mCursor == null || mCursor.LinkedList != mList)
            mCursor = mList.Last;

        if (mIsInOrder(mCursor!, item))
        {
            T last = mCursor!;
            while (last.Next != null && mIsInOrder(last.Next, item))
            {
                last = last.Next;
            }

            mList.InsertAfter(last, item);
        }
        else
        {
            T next = mCursor!;
            while (next.Last != null && !mIsInOrder(next.Last, item))
            {
                next = next.Last;
            }

            mList.InsertBefore(next, item);
        }

        mCursor = item;
    }

    public bool Remove(T item) => mList.Remove(item);

    // 精确复位：按记录的前驱锚点原样放回（after==null 即放到表头），绕过排序定位。仅供同程序集的
    // undo/redo 重放用——复合撤销时元素的排序键可能尚未回滚（如"改键+重排"逆序回放，重排先于改键被撤），
    // 此时只能按结构位置而非当前键还原。internal 故域层（他程序集）取不到，对外的有序不变量仍封死。
    internal void Reinsert(T? after, T item)
    {
        if (after == null)
            mList.AddFirst(item);
        else
            mList.InsertAfter(after, item);

        mCursor = item;
    }

    public void Clear()
    {
        mList.Clear();
        mCursor = null;
    }

    public IEnumerator<T> GetEnumerator() => mList.GetEnumerator();

    public IEnumerator<T> Inverse() => mList.Inverse();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    readonly LinkedList<T> mList = new();
    readonly Func<T, T, bool> mIsInOrder;
    T? mCursor = null;
}
