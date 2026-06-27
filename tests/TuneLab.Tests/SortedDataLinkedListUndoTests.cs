using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using Xunit;

namespace TuneLab.Tests;

// 有序数据链表的 undo/redo 必须按「结构位置」精确复位，而非按「当前排序键」重插。
// 关键场景：一次「改键 + 重排」的 Move 会按序记下 [Set 键][Remove][Insert]；undo 逆序回放时
// [Remove].Undo（重新放回元素）发生在 [Set 键].Undo 之前——此刻元素的键仍是改后的新值。
// 若重放走有序 Insert，会按新键落到错误槽位，随后键被改回却不重排 → 链表失序。
// 故重放必须按 Move 时记录的前驱锚点原样复位（与当前键无关）。本组测试把这条钉死。
public class SortedDataLinkedListUndoTests
{
    // 最小元素：带一个可记录命令的排序键 Pos，并自挂为链表节点。
    sealed class Item : DataObject, ILinkedNode<Item>
    {
        public Item? Next { get; set; }
        public Item? Last { get; set; }
        public ILinkedList<Item>? LinkedList { get; set; }
        public readonly DataStruct<int> Pos = new();

        public Item(int pos)
        {
            Pos.Attach(this);
            Pos.Set(pos);
        }
    }

    static SortedDataObjectLinkedList<Item> NewList() => new((a, b) => a.Pos.Value <= b.Pos.Value);

    static int[] Keys(IEnumerable<Item> list) => list.Select(i => i.Pos.Value).ToArray();

    // 链表枚举顺序与各节点键单调非降一致——失序的硬判据。
    static void AssertSortedAndConsistent(SortedDataObjectLinkedList<Item> list)
    {
        var items = list.ToList();
        for (int i = 1; i < items.Count; i++)
            Assert.True(items[i - 1].Pos.Value <= items[i].Pos.Value,
                $"out of order at {i}: {string.Join(",", Keys(items))}");
    }

    [Fact]
    public void Undo_MoveThatReorders_RestoresExactOrderAndKey()
    {
        var doc = new DataDocument();
        var list = NewList();
        list.Attach(doc);

        var a = new Item(10);
        var b = new Item(20);
        var c = new Item(30);
        list.Insert(a);
        list.Insert(b);
        list.Insert(c);
        doc.Commit();

        Assert.Equal(new[] { a, b, c }, list.ToArray());

        // a: 10 -> 25，越过 b 落到 b 与 c 之间 → [b, a, c]。
        list.Move(a, () => a.Pos.Set(25));
        doc.Commit();

        Assert.Equal(new[] { b, a, c }, list.ToArray());
        Assert.Equal(25, a.Pos.Value);
        AssertSortedAndConsistent(list);

        // 撤销：必须回到 [a, b, c] 且 a 的键回到 10，位置与键一致。
        doc.Undo();
        Assert.Equal(new[] { a, b, c }, list.ToArray());
        Assert.Equal(10, a.Pos.Value);
        AssertSortedAndConsistent(list);

        // 重做：回到移动后的态。
        doc.Redo();
        Assert.Equal(new[] { b, a, c }, list.ToArray());
        Assert.Equal(25, a.Pos.Value);
        AssertSortedAndConsistent(list);
    }

    [Fact]
    public void Undo_MoveToFront_RestoresExactPosition()
    {
        var doc = new DataDocument();
        var list = NewList();
        list.Attach(doc);

        var a = new Item(10);
        var b = new Item(20);
        var c = new Item(30);
        list.Insert(a);
        list.Insert(b);
        list.Insert(c);
        doc.Commit();

        // c: 30 -> 5，落到表头 → [c, a, b]。
        list.Move(c, () => c.Pos.Set(5));
        doc.Commit();
        Assert.Equal(new[] { c, a, b }, list.ToArray());

        doc.Undo();
        Assert.Equal(new[] { a, b, c }, list.ToArray());
        Assert.Equal(30, c.Pos.Value);
        AssertSortedAndConsistent(list);
    }

    [Fact]
    public void Undo_PlainInsertAndRemove_RoundTrips()
    {
        var doc = new DataDocument();
        var list = NewList();
        list.Attach(doc);

        var a = new Item(10);
        var b = new Item(30);
        list.Insert(a);
        list.Insert(b);
        doc.Commit();

        // 中间插入 c(20) → [a, c, b]。
        var c = new Item(20);
        list.Insert(c);
        doc.Commit();
        Assert.Equal(new[] { a, c, b }, list.ToArray());

        doc.Undo();
        Assert.Equal(new[] { a, b }, list.ToArray());

        doc.Redo();
        Assert.Equal(new[] { a, c, b }, list.ToArray());
    }
}
