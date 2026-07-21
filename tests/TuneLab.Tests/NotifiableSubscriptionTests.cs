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
        property.WillModify.AsEverytime().Subscribe((bool _) => willAllCount++);   // 全量脸收到每次改前

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

    // 引用计数拆除契约：组合子对 source 成员增删的订阅，生命周期须随下游订阅数 0↔1 切换，
    // 而非构造期 eager 订上、永不摘下（否则长寿 source 会把组合子永久 pin 住）。
    [Fact]
    public void WhenAnyItem_RefCounts_SourceSubscription_NoEagerNoLeak()
    {
        var list = new FakeNotifiableList<NotifiableProperty<double>>();
        var aggregate = list.WhenAnyItem(p => ((IReadOnlyNotifiableProperty<double>)p).Modified);

        Assert.Equal(0, list.ItemAddedSubscriberCount);     // 仅构造：不 eager 挂 source
        Assert.Equal(0, list.ItemRemovedSubscriberCount);

        Action<NotifiableProperty<double>> h1 = _ => { };
        Action<NotifiableProperty<double>> h2 = _ => { };

        aggregate.Subscribe(h1);
        Assert.Equal(1, list.ItemAddedSubscriberCount);     // 0→1：挂上
        aggregate.Subscribe(h2);
        Assert.Equal(1, list.ItemAddedSubscriberCount);     // 已挂，不重复

        aggregate.Unsubscribe(h1);
        Assert.Equal(1, list.ItemAddedSubscriberCount);     // 仍有 h2
        aggregate.Unsubscribe(h2);
        Assert.Equal(0, list.ItemAddedSubscriberCount);     // 1→0：摘下，无悬挂订阅
        Assert.Equal(0, list.ItemRemovedSubscriberCount);

        aggregate.Subscribe(h1);                            // 0↔1 抖动后复订仍能重新挂上
        Assert.Equal(1, list.ItemAddedSubscriberCount);
    }

    // 同一 handler 对同一 WhenAnyItem 重复订阅：账本 mWires 须按次堆叠（对齐 mDownstreams 的 List 容忍重复、
    // 原生事件"订 N 次触发 N 次"），退订对称、全退订后无悬挂转发器。曾因覆盖式记账致后订抹掉先订的转发器记录，
    // 全退订后仍有一份转发器悬在成员事件上（泄漏 + 退订后回调）。
    [Fact]
    public void WhenAnyItem_DuplicateHandler_SubscribeUnsubscribeSymmetric_NoLeak()
    {
        var list = new FakeNotifiableList<NotifiableProperty<double>>();
        var a = new NotifiableProperty<double>(0);
        list.Add(a);

        int fired = 0;
        Action<NotifiableProperty<double>> onChanged = _ => fired++;
        var aggregate = list.WhenAnyItem(p => ((IReadOnlyNotifiableProperty<double>)p).Modified);

        aggregate.Subscribe(onChanged);
        aggregate.Subscribe(onChanged);                     // 同一 handler 订两次

        a.Value = 1;
        Assert.Equal(2, fired);                             // 两份接线各触发一次

        aggregate.Unsubscribe(onChanged);                   // 退一次 → 剩一份
        a.Value = 2;
        Assert.Equal(3, fired);                             // 仍触发一次

        aggregate.Unsubscribe(onChanged);                   // 退第二次 → 全清
        Assert.Equal(0, list.ItemAddedSubscriberCount);     // source 接线归零
        a.Value = 3;
        Assert.Equal(3, fired);                             // 全退订后静默——无悬挂转发器再回调
    }

    [Fact]
    public void WhenAny_RefCounts_SourceSubscription_NoEagerNoLeak()
    {
        var list = new FakeNotifiableList<NotifiableProperty<double>>();
        var aggregate = list.WhenAny<NotifiableProperty<double>, Action>(
            p => ((IReadOnlyNotifiableProperty<double>)p).Modified);

        Assert.Equal(0, list.ItemAddedSubscriberCount);     // 不 eager

        Action h = () => { };
        aggregate.Subscribe(h);
        Assert.Equal(1, list.ItemAddedSubscriberCount);     // 0→1 挂上

        aggregate.Unsubscribe(h);
        Assert.Equal(0, list.ItemAddedSubscriberCount);     // 1→0 摘下
    }

    // WhenAny 匿名/多选择器对偶：合并多个 0 参选择器，任一触发即触发（不带成员标识），且同样引用计数。
    [Fact]
    public void WhenAny_Anonymous_MergesSelectors_NoPayload_AndRefCounts()
    {
        var list = new FakeNotifiableList<NotifiableProperty<double>>();
        var a = new NotifiableProperty<double>(0);
        list.Add(a);

        int fired = 0;
        Action onAny = () => fired++;
        var aggregate = list.WhenAny(
            p => ((IReadOnlyNotifiableProperty<double>)p).WillModify,
            p => ((IReadOnlyNotifiableProperty<double>)p).Modified);

        Assert.Equal(0, list.ItemAddedSubscriberCount);     // 不 eager
        aggregate.Subscribe(onAny);
        Assert.Equal(1, list.ItemAddedSubscriberCount);     // 0→1 挂上

        a.Value = 1;                                        // 一次改动 → WillModify + Modified 各触发一次
        Assert.Equal(2, fired);

        aggregate.Unsubscribe(onAny);
        Assert.Equal(0, list.ItemAddedSubscriberCount);     // 1→0 摘下
        a.Value = 2;
        Assert.Equal(2, fired);                             // 退订后静默
    }

    // Where 视图：谓词翻转合成 ItemAdded/ItemRemoved；且追踪生命周期随对外订阅引用计数（全退订即停，预测变化也不再合成）。
    [Fact]
    public void Where_PredicateFlip_Synthesizes_AndStopsTrackingAfterAllUnsubscribed()
    {
        var list = new FakeNotifiableList<NotifiableProperty<bool>>();
        var a = new NotifiableProperty<bool>(true);
        list.Add(a);

        var filtered = list.Where(
            p => p.Value,
            p => ((IReadOnlyNotifiableProperty<bool>)p).Modified);

        Assert.Equal(0, list.ItemAddedSubscriberCount);     // 不 eager 追踪 source

        int added = 0, removed = 0;
        Action<NotifiableProperty<bool>> onAdd = _ => added++;
        Action<NotifiableProperty<bool>> onRemove = _ => removed++;
        filtered.ItemAdded.Subscribe(onAdd);
        Assert.Equal(1, list.ItemAddedSubscriberCount);     // 首个观察者 → 开始追踪
        filtered.ItemRemoved.Subscribe(onRemove);

        a.Value = false;                                    // 谓词翻假 → 合成 ItemRemoved
        Assert.Equal(0, added);
        Assert.Equal(1, removed);

        a.Value = true;                                     // 谓词翻真 → 合成 ItemAdded
        Assert.Equal(1, added);
        Assert.Equal(1, removed);

        filtered.ItemAdded.Unsubscribe(onAdd);
        Assert.Equal(1, list.ItemAddedSubscriberCount);     // 还有 ItemRemoved 观察者 → 维持追踪
        filtered.ItemRemoved.Unsubscribe(onRemove);
        Assert.Equal(0, list.ItemAddedSubscriberCount);     // 三事件全退订 → 停止追踪

        a.Value = false;                                    // 已停追踪 + 退订成员谓词 → 不再合成
        Assert.Equal(1, added);
        Assert.Equal(1, removed);
    }

    // 并集 refcount：跨三个对外事件统计，任一有观察者即维持追踪。
    [Fact]
    public void Where_RefCounts_UnionAcrossThreeEvents()
    {
        var list = new FakeNotifiableList<NotifiableProperty<bool>>();
        list.Add(new NotifiableProperty<bool>(true));

        var filtered = list.Where(
            p => p.Value,
            p => ((IReadOnlyNotifiableProperty<bool>)p).Modified);

        Action<NotifiableProperty<bool>> h = _ => { };
        Action onMembership = () => { };

        filtered.ItemAdded.Subscribe(h);
        Assert.Equal(1, list.ItemAddedSubscriberCount);     // 0→1 激活
        filtered.MembershipModified.Subscribe(onMembership);
        Assert.Equal(1, list.ItemAddedSubscriberCount);     // 第二个事件也订上，并集不重复挂

        filtered.ItemAdded.Unsubscribe(h);
        Assert.Equal(1, list.ItemAddedSubscriberCount);     // 仍有 MembershipModified 观察者
        filtered.MembershipModified.Unsubscribe(onMembership);
        Assert.Equal(0, list.ItemAddedSubscriberCount);     // 并集归零 → 停追踪
    }

    class FakeNotifiableList<T> : IReadOnlyNotifiableList<T>
    {
        public IActionEvent<T> ItemAdded => mItemAdded;
        public IActionEvent<T> ItemRemoved => mItemRemoved;
        public IActionEvent MembershipModified => mMembershipModified;

        public int ItemAddedSubscriberCount => mItemAdded.SubscriberCount;
        public int ItemRemovedSubscriberCount => mItemRemoved.SubscriberCount;
        public IEnumerable<T> Items => this;

        public T this[int index] => mItems[index];
        public int Count => mItems.Count;
        public IEnumerator<T> GetEnumerator() => mItems.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Add(T item) { mItems.Add(item); mItemAdded.Invoke(item); mMembershipModified.Invoke(); }
        public void Remove(T item) { mItems.Remove(item); mItemRemoved.Invoke(item); mMembershipModified.Invoke(); }

        readonly CountingActionEvent<T> mItemAdded = new();
        readonly CountingActionEvent<T> mItemRemoved = new();
        readonly ActionEvent mMembershipModified = new();
        readonly List<T> mItems = [];
    }

    // 在原生事件外包一层订阅计数，让测试能断言组合子对 source 的接线/摘除（拆除契约不可见，故需观测）。
    class CountingActionEvent<T> : IActionEvent<T>
    {
        public int SubscriberCount { get; private set; }
        public void Subscribe(Action<T> action) { mInner.Subscribe(action); SubscriberCount++; }
        public void Unsubscribe(Action<T> action) { mInner.Unsubscribe(action); SubscriberCount--; }
        public void Invoke(T t) => mInner.Invoke(t);
        readonly ActionEvent<T> mInner = new();
    }
}
