using System.Collections;
using TuneLab.Foundation;
using Xunit;

namespace TuneLab.Tests;

// SDK 最小订阅面（IReadOnlyNotifiableProperty / IReadOnlyNotifiableList + WhenAny）的语义钉死：
// WillModify 改前读旧值 / Modified 改后读新值；merge 中间态不外漏到最小面；
// WhenAny 对现有/新增/移除成员的订阅生命周期管理。
public class NotifiableSubscriptionTests
{
    [Fact]
    public void DataStruct_WillModifySeesOldValue_ModifiedSeesNewValue()
    {
        var property = new DataStruct<double>();
        IReadOnlyNotifiableProperty<double> readOnly = property;

        double oldSeen = double.NaN, newSeen = double.NaN;
        readOnly.WillModify.Subscribe(() => oldSeen = readOnly.Value);
        readOnly.Modified.Subscribe(() => newSeen = readOnly.Value);

        property.Set(5);
        Assert.Equal(0.0, oldSeen);
        Assert.Equal(5.0, newSeen);

        property.Set(7);
        Assert.Equal(5.0, oldSeen);
        Assert.Equal(7.0, newSeen);
    }

    [Fact]
    public void DataStruct_SettingSameValue_FiresNothing()
    {
        var property = new DataStruct<double>();
        IReadOnlyNotifiableProperty<double> readOnly = property;
        property.Set(5);

        int fired = 0;
        readOnly.WillModify.Subscribe(() => fired++);
        readOnly.Modified.Subscribe(() => fired++);

        property.Set(5);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void MergeNotify_WillAndModified_AreDualUnderMerge()
    {
        var property = new DataStruct<double>();
        IReadOnlyNotifiableProperty<double> readOnly = property;

        int willCount = 0, modifiedCount = 0, willAllCount = 0;
        double firstWillSaw = double.NaN;
        readOnly.WillModify.Subscribe(() => { willCount++; firstWillSaw = readOnly.Value; });
        readOnly.Modified.Subscribe(() => modifiedCount++);
        property.WillModify.Subscribe((bool _) => willAllCount++);   // 全量形状收到每次改前

        property.BeginMergeNotify();
        property.Set(1);
        property.Set(2);
        // 改前事件的 merge 对偶语义：作用域内首次 canIgnore=false 必达（此时抓旧值 0），
        // 其余 canIgnore=true 不进最小面——Modified 折叠掉的中间态，其旧值同样无需作废。
        Assert.Equal(1, willCount);
        Assert.Equal(0.0, firstWillSaw);
        Assert.Equal(2, willAllCount);
        Assert.Equal(0, modifiedCount);    // 中间态（canIgnore=true）不进最小订阅面

        property.EndMergeNotify();
        Assert.Equal(1, modifiedCount);    // merge 收口：一次结果态
        Assert.Equal(2.0, readOnly.Value);

        property.Set(3);                   // 收口已重置：新变更的改前通知再次必达
        Assert.Equal(2, willCount);
        Assert.Equal(2, modifiedCount);
    }

    [Fact]
    public void NotifiableProperty_MinimalInterfaceAdapter()
    {
        var property = new NotifiableProperty<double>(3);
        IReadOnlyNotifiableProperty<double> readOnly = property;

        double oldSeen = double.NaN, newSeen = double.NaN;
        readOnly.WillModify.Subscribe(() => oldSeen = readOnly.Value);
        readOnly.Modified.Subscribe(() => newSeen = readOnly.Value);

        property.Value = 7;
        Assert.Equal(3.0, oldSeen);
        Assert.Equal(7.0, newSeen);
    }

    [Fact]
    public void WhenAny_SubscribesExistingAndFutureItems_UnsubscribesRemovedAndOnDispose()
    {
        var list = new FakeNotifiableList<NotifiableProperty<double>>();
        var a = new NotifiableProperty<double>(0);
        var b = new NotifiableProperty<double>(0);
        list.Add(a);

        int fired = 0;
        Action<NotifiableProperty<double>> onChanged = _ => fired++;
        var aggregate = list.WhenAnyItem(p => ((IReadOnlyNotifiableProperty<double>)p).Modified);
        aggregate.Subscribe(onChanged);

        a.Value = 1;                      // 既有成员已接线
        Assert.Equal(1, fired);

        list.Add(b);
        b.Value = 1;                      // 新增成员自动接线
        Assert.Equal(2, fired);

        list.Remove(a);
        a.Value = 2;                      // 移除成员自动退订
        Assert.Equal(2, fired);

        aggregate.Unsubscribe(onChanged);
        b.Value = 2;                      // 退订后全部静默
        Assert.Equal(2, fired);
    }

    class FakeNotifiableList<T> : IReadOnlyNotifiableList<T>
    {
        public IActionEvent<T> ItemAdded => mItemAdded;
        public IActionEvent<T> ItemRemoved => mItemRemoved;
        public IActionEvent StructureModified => mStructureModified;
        public IEnumerable<T> Items => this;

        public T this[int index] => mItems[index];
        public int Count => mItems.Count;
        public IEnumerator<T> GetEnumerator() => mItems.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Add(T item) { mItems.Add(item); mItemAdded.Invoke(item); mStructureModified.Invoke(); }
        public void Remove(T item) { mItems.Remove(item); mItemRemoved.Invoke(item); mStructureModified.Invoke(); }

        readonly ActionEvent<T> mItemAdded = new();
        readonly ActionEvent<T> mItemRemoved = new();
        readonly ActionEvent mStructureModified = new();
        readonly List<T> mItems = [];
    }
}
