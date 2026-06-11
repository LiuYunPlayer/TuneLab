using System.Collections;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Event;
using TuneLab.Primitives.Event;
using Xunit;

namespace TuneLab.Tests;

// SDK 最小订阅面（IReadOnlyNotifiableProperty / IReadOnlyNotifiableList + WhenAny）的语义钉死：
// WillModified 改前读旧值 / Modified 改后读新值；merge 中间态不外漏到最小面；
// WhenAny 对现有/新增/移除成员的订阅生命周期管理。
public class NotifiableSubscriptionTests
{
    [Fact]
    public void DataStruct_WillModifiedSeesOldValue_ModifiedSeesNewValue()
    {
        var property = new DataStruct<double>();
        IReadOnlyNotifiableProperty<double> readOnly = property;

        double oldSeen = double.NaN, newSeen = double.NaN;
        readOnly.WillModified += () => oldSeen = readOnly.Value;
        readOnly.Modified += () => newSeen = readOnly.Value;

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
        readOnly.WillModified += () => fired++;
        readOnly.Modified += () => fired++;

        property.Set(5);
        Assert.Equal(0, fired);
    }

    [Fact]
    public void MergeNotify_IntermediateStatesNotLeakedToMinimalSurface()
    {
        var property = new DataStruct<double>();
        IReadOnlyNotifiableProperty<double> readOnly = property;

        int willCount = 0, modifiedCount = 0;
        readOnly.WillModified += () => willCount++;
        readOnly.Modified += () => modifiedCount++;

        property.BeginMergeNotify();
        property.Set(1);
        property.Set(2);
        Assert.Equal(2, willCount);        // 改前事件不可合并：每次变更前都触发（旧值须可读）
        Assert.Equal(0, modifiedCount);    // 中间态（canIgnore=true）不进最小订阅面

        property.EndMergeNotify();
        Assert.Equal(1, modifiedCount);    // merge 收口：一次结果态
        Assert.Equal(2.0, readOnly.Value);
    }

    [Fact]
    public void NotifiableProperty_MinimalInterfaceAdapter()
    {
        var property = new NotifiableProperty<double>(3);
        IReadOnlyNotifiableProperty<double> readOnly = property;

        double oldSeen = double.NaN, newSeen = double.NaN;
        readOnly.WillModified += () => oldSeen = readOnly.Value;
        readOnly.Modified += () => newSeen = readOnly.Value;

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
        Action onChanged = () => fired++;
        var subscription = list.WhenAny(
            p => ((IReadOnlyNotifiableProperty<double>)p).Modified += onChanged,
            p => ((IReadOnlyNotifiableProperty<double>)p).Modified -= onChanged);

        a.Value = 1;                      // 既有成员已接线
        Assert.Equal(1, fired);

        list.Add(b);
        b.Value = 1;                      // 新增成员自动接线
        Assert.Equal(2, fired);

        list.Remove(a);
        a.Value = 2;                      // 移除成员自动退订
        Assert.Equal(2, fired);

        subscription.Dispose();
        b.Value = 2;                      // 整体退订后全部静默
        Assert.Equal(2, fired);
    }

    class FakeNotifiableList<T> : IReadOnlyNotifiableList<T>
    {
        public event Action<T>? ItemAdded;
        public event Action<T>? ItemRemoved;
        public event Action? Modified;

        public T this[int index] => mItems[index];
        public int Count => mItems.Count;
        public IEnumerator<T> GetEnumerator() => mItems.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Add(T item) { mItems.Add(item); ItemAdded?.Invoke(item); Modified?.Invoke(); }
        public void Remove(T item) { mItems.Remove(item); ItemRemoved?.Invoke(item); Modified?.Invoke(); }

        readonly List<T> mItems = [];
    }
}
