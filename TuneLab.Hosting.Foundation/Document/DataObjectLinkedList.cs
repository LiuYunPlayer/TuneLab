using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

public class DataObjectLinkedList<T> : DataObject, IDataObjectLinkedList<T> where T : class, IDataObject, ILinkedNode<T>
{
    public IModifiedEvent MembershipModified => mDataLinkedList.Modified;
    public IActionEvent<T> ItemAdded => mDataLinkedList.ItemAdded;
    public IActionEvent<T> ItemRemoved => mDataLinkedList.ItemRemoved;
    public IEnumerable<T> Items => this;

    public T? First => mDataLinkedList.First;
    public T? Last => mDataLinkedList.Last;
    public int Count => mDataLinkedList.Count;

    public DataObjectLinkedList()
    {
        mDataLinkedList = new(this);
        mDataLinkedList.Attach(this);
        mDataLinkedList.ItemAdded.Subscribe(OnAdd);
        mDataLinkedList.ItemRemoved.Subscribe(OnRemove);
    }

    public List<T> GetInfo()
    {
        return mDataLinkedList.GetInfo();
    }

    public void Insert(T item)
    {
        mDataLinkedList.Insert(item);
    }

    public bool Remove(T item)
    {
        return mDataLinkedList.Remove(item);
    }

    // 统一维序：排序键（pos/dur 等）由 IsInOrder 决定，就地改键会破坏链表有序性。Move 把
    // 「跑 mutate（可改键及任意属性）→ 按需维序」收在一个合并通知作用域里，调用方只需在 mutate
    // 内改属性。惰性维序：mutate 后仅当元素相对其前后邻居失序才摘除重插——纯非键编辑（如只改
    // pitch）不产生 ItemRemoved/ItemAdded，避免下游（合成重连线/选择）的多余副作用；真改键且越过
    // 邻居才重排。元素不在表中则只跑 mutate（与 Remove 的宽容语义一致），对已摘除元素调用安全。
    public void Move(T item, Action mutate)
    {
        using var _ = MergeNotify();
        mutate();
        Reorder(item);
    }

    // 批量重排：先跑 mutate、再全摘除、按各自新键全重插。批量场景（如多选拖动）所有键齐变、
    // 中间态全失序，逐个惰性判定会相互干扰，故用全摘除-全重插。
    // 【关键】mutate 必须在摘除之前：Remove 会 Detach 元素（parent 置空），脱离文档树后其属性 Set
    // 无 DataDocument 祖先、命令只应用不记录，导致 DiscardTo/Undo 回滚不掉（拖动会逐帧累加漂移）。
    // mutate 时元素仍挂在表上 → Set 命令被记录、可回滚。摘除是按指针、与排序键无关，故改键后再摘安全。
    public void Move(IReadOnlyCollection<T> items, Action mutate)
    {
        using var _ = MergeNotify();
        mutate();
        var contained = new List<T>();
        foreach (var item in items)
        {
            if (Remove(item))
                contained.Add(item);
        }
        foreach (var item in contained)
            Insert(item);
    }

    // 改键后按需维序：相对前后邻居仍有序则不动；失序才摘除重插（重插按 IsInOrder 定位到正确处）。
    void Reorder(T item)
    {
        if (!Contains(item))
            return;

        var node = (ILinkedNode<T>)item;
        bool inOrder = (node.Last == null || IsInOrder(node.Last, item))
                    && (node.Next == null || IsInOrder(item, node.Next));
        if (inOrder)
            return;

        Remove(item);
        Insert(item);
    }

    public void InsertAfter(T last, T item)
    {
        mDataLinkedList.InsertAfter(last, item);
    }

    public void InsertBefore(T next, T item)
    {
        mDataLinkedList.InsertBefore(next, item);
    }

    public void Clear()
    {
        mDataLinkedList.Clear();
    }

    public bool Contains(T item)
    {
        return mDataLinkedList.Contains(item);
    }

    public IEnumerator<T> Inverse()
    {
        return mDataLinkedList.Inverse();
    }

    public IEnumerator<T> GetEnumerator()
    {
        return mDataLinkedList.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void SetInfo(IEnumerable<T> info)
    {
        mDataLinkedList.SetInfo(info);
    }

    protected virtual bool IsInOrder(T prev, T next)
    {
        return true;
    }

    void OnAdd(T item)
    {
        item.Attach(this);
    }

    void OnRemove(T item)
    {
        item.Detach();
    }

    class DataLinkedList(DataObjectLinkedList<T> dataObjectLinkedList) : DataLinkedList<T>
    {
        protected override bool IsInOrder(T prev, T next)
        {
            return dataObjectLinkedList.IsInOrder(prev, next);
        }
    }

    readonly DataLinkedList mDataLinkedList;
}
