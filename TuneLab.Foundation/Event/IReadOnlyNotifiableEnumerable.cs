using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

// 可通知序列的最小只读形状（与 DataObject 无关）：当前成员（Items）+ 成员增删事件。
// 这是 WhenAny / WhenAnyItem / Where 的最小接受面——只需能枚举当前成员并感知增删，不要求 Count。
// 用 Items 属性而非 : IEnumerable<T>，以便 DataObjectMap 这类多视图容器把 value 集合显式投影出来，
// 不与自身的 IEnumerable<KeyValuePair> 冲突。
// 名副其实带 Count 的特化见 IReadOnlyNotifiableCollection。
public interface IReadOnlyNotifiableEnumerable<out T>
{
    IActionEvent<T> ItemAdded { get; }
    IActionEvent<T> ItemRemoved { get; }
    // 任一成员增删后的聚合信号，与逐项的 ItemAdded / ItemRemoved 互补（不带成员标识，只表"成员结构变过了"）。
    IActionEvent MembershipModified { get; }
    IEnumerable<T> Items { get; }
}

public static class IReadOnlyNotifiableEnumerableExtension
{
    // WhenAny 的「带成员标识」形态：任一成员的任一指定事件触发 → 通知时携带该成员（Action<T>）。
    // 成员增删自动接线/退订。与无参 WhenAny 互补：WhenAny 聚合成一个共享触发器、handler 不知道是谁；
    // WhenAnyItem 让 handler 拿到触发的成员，可做 per-member 精确处理（如增量标脏 markDirty(item)）。
    public static IActionEvent<T> WhenAnyItem<T>(this IReadOnlyNotifiableEnumerable<T> source,
        params Func<T, IEvent<Action>>[] eventSelectors)
    {
        return new AnyItemEvent<T>(source, eventSelectors);
    }

    // 成员事件组合子的共享骨架：引用计数地跟随 source 成员增删。
    // 仅当有下游订阅时才挂在 source.ItemAdded/ItemRemoved 上，下游清零即摘下——
    // 避免组合子被长寿 source 经事件委托 pin 住（无下游时 source→组合子 这条边不存在）。
    // 子类只需实现「如何把单个成员接到/从某个下游」。
    abstract class MemberEventBase<T, TDownstream>
    {
        protected MemberEventBase(IReadOnlyNotifiableEnumerable<T> source)
        {
            mSource = source;
        }

        public void Subscribe(TDownstream downstream)
        {
            if (mDownstreams.Count == 0)
            {
                mSource.ItemAdded.Subscribe(OnAdd);
                mSource.ItemRemoved.Subscribe(OnRemove);
            }
            mDownstreams.Add(downstream);
            foreach (var item in mSource.Items)
                WireItem(item, downstream);
        }

        public void Unsubscribe(TDownstream downstream)
        {
            if (!mDownstreams.Remove(downstream))
                return;
            foreach (var item in mSource.Items)
                UnwireItem(item, downstream);
            if (mDownstreams.Count == 0)
            {
                mSource.ItemAdded.Unsubscribe(OnAdd);
                mSource.ItemRemoved.Unsubscribe(OnRemove);
            }
        }

        void OnAdd(T item)
        {
            foreach (var downstream in mDownstreams)
                WireItem(item, downstream);
        }

        void OnRemove(T item)
        {
            foreach (var downstream in mDownstreams)
                UnwireItem(item, downstream);
        }

        // 把单个成员的相关事件接到 / 从指定下游。约定只做接线、不触发任何下游用户代码——
        // 故上面 OnAdd/OnRemove 的 foreach mDownstreams 不会被用户回调重入修改。
        protected abstract void WireItem(T item, TDownstream downstream);
        protected abstract void UnwireItem(T item, TDownstream downstream);

        protected readonly IReadOnlyNotifiableEnumerable<T> mSource;
        readonly List<TDownstream> mDownstreams = new();
    }

    // WhenAnyItem 的组合子：把成员的每个选择事件接到一个捕获该成员的转发 lambda，触发时回调下游并带上该成员。
    // 转发 lambda 身份须留存（mWires）才能精确退订——这正是「带回成员」不可约的代价（区别于 AnyEvent/AnyActionEvent 的无账本直订）。
    class AnyItemEvent<T> : MemberEventBase<T, Action<T>>, IActionEvent<T>
    {
        public AnyItemEvent(IReadOnlyNotifiableEnumerable<T> source, Func<T, IEvent<Action>>[] selectors) : base(source)
        {
            mSelectors = selectors;
        }

        protected override void WireItem(T item, Action<T> handler)
        {
            void Forward() => handler(item);
            var subscriptions = new List<(IEvent<Action> Event, Action Forward)>();
            foreach (var selector in mSelectors)
            {
                var actionEvent = selector(item);
                actionEvent.Subscribe(Forward);
                subscriptions.Add((actionEvent, Forward));
            }
            // 同一 (item, handler) 可被 wire 多次——下游把同一 handler 重复订阅（mDownstreams 是 List、容忍重复，
            // 与原生事件"订两次触发两次"一致）。每次的转发器身份不同，故按次堆叠留存、退订时逐次弹出精确摘除；
            // 若覆盖式记账则后一次会抹掉前一次的转发器记录，致其永不退订（泄漏 + 全退订后仍回调）。
            if (!mWires.TryGetValue((item, handler), out var stack))
                mWires[(item, handler)] = stack = new();
            stack.Push(subscriptions);
        }

        protected override void UnwireItem(T item, Action<T> handler)
        {
            if (!mWires.TryGetValue((item, handler), out var stack) || stack.Count == 0)
                return;
            foreach (var (actionEvent, forward) in stack.Pop())
                actionEvent.Unsubscribe(forward);
            if (stack.Count == 0)
                mWires.Remove((item, handler));
        }

        readonly Func<T, IEvent<Action>>[] mSelectors;
        readonly Dictionary<(T, Action<T>), Stack<List<(IEvent<Action> Event, Action Forward)>>> mWires = new();
    }

    // 跟随集合任一成员的某个事件：成员增删时自动接上/退订，使用者只订一次。
    public static IEvent<TEvent> WhenAny<T, TEvent>(this IReadOnlyNotifiableEnumerable<T> source, ISubscriber<T, TEvent> subscriber)
    {
        return new AnyEvent<T, TEvent>(source, subscriber);
    }

    public static IEvent<TEvent> WhenAny<T, TEvent>(this IReadOnlyNotifiableEnumerable<T> source, Func<T, IEvent<TEvent>> selector)
    {
        return source.WhenAny(new SelectorSubscriber<T, TEvent>(selector));
    }

    public static IEvent<TEvent> WhenAny<T, TEvent>(this IReadOnlyNotifiableEnumerable<T> source, Action<T, TEvent> subscribe, Action<T, TEvent> unsubscribe)
    {
        return source.WhenAny(new ActionSubscriber<T, TEvent>(subscribe, unsubscribe));
    }

    // WhenAnyItem 的「匿名」对偶：任一成员的任一选择事件触发 → 触发，但不带成员标识。
    // 与 WhenAnyItem 同样接受多个 0 参选择器（params），区别仅在是否把成员递给下游。
    // 注意重载决议：单个选择器的调用会优先匹配上面保荷载的 WhenAny<T,TEvent>（params 仅在扩展形式下适用，
    // 普通形式优先）；要走本匿名聚合，传 2 个及以上选择器，或显式传 Func<T, IEvent<Action>>[] 数组。
    public static IActionEvent WhenAny<T>(this IReadOnlyNotifiableEnumerable<T> source,
        params Func<T, IEvent<Action>>[] eventSelectors)
    {
        return new AnyActionEvent<T>(source, eventSelectors);
    }

    // WhenAny（保荷载/单选择器）的组合子：把下游 invokable 直接订到每个成员的选择事件上——
    // 无转发器，退订按 invokable 身份即可，故无需 per-(成员,下游) 账本。
    class AnyEvent<T, TEvent> : MemberEventBase<T, TEvent>, IEvent<TEvent>
    {
        public AnyEvent(IReadOnlyNotifiableEnumerable<T> source, ISubscriber<T, TEvent> subscriber) : base(source)
        {
            mSubscriber = subscriber;
        }

        protected override void WireItem(T item, TEvent invokable) => mSubscriber.Subscribe(item, invokable);
        protected override void UnwireItem(T item, TEvent invokable) => mSubscriber.Unsubscribe(item, invokable);

        readonly ISubscriber<T, TEvent> mSubscriber;
    }

    // WhenAny（匿名/多选择器）的组合子：0 参下游 Action 直接订到每个成员的每个选择事件，同样无转发器、无账本。
    class AnyActionEvent<T> : MemberEventBase<T, Action>, IActionEvent
    {
        public AnyActionEvent(IReadOnlyNotifiableEnumerable<T> source, Func<T, IEvent<Action>>[] selectors) : base(source)
        {
            mSelectors = selectors;
        }

        protected override void WireItem(T item, Action handler)
        {
            foreach (var selector in mSelectors)
                selector(item).Subscribe(handler);
        }

        protected override void UnwireItem(T item, Action handler)
        {
            foreach (var selector in mSelectors)
                selector(item).Unsubscribe(handler);
        }

        readonly Func<T, IEvent<Action>>[] mSelectors;
    }

    // 响应式过滤视图：成员的谓词翻转时合成 ItemAdded / ItemRemoved，过滤结果随谓词实时变化。
    // 与 WhenAny 同族、可串接（source.Where(...).WhenAny(...)）。
    public static IReadOnlyNotifiableEnumerable<T> Where<T>(this IReadOnlyNotifiableEnumerable<T> source, Func<T, bool> predicate, Func<T, IEvent<Action>> predicateChanged) where T : notnull
    {
        return new WhereCollection<T>(source, predicate, predicateChanged);
    }

    public static IReadOnlyNotifiableEnumerable<T> Where<T>(this IReadOnlyNotifiableEnumerable<T> source, Func<T, IReadOnlyNotifiableProperty<bool>> predicateProperty) where T : notnull
    {
        return new WhereCollection<T>(source, item => predicateProperty(item).Value, item => predicateProperty(item).Modified);
    }

    class WhereCollection<T> : IReadOnlyNotifiableEnumerable<T> where T : notnull
    {
        public IActionEvent<T> ItemAdded => mItemAdded;
        public IActionEvent<T> ItemRemoved => mItemRemoved;
        public IActionEvent MembershipModified => mMembershipModified;
        public IEnumerable<T> Items => mSource.Items.Where(mPredicate);

        public WhereCollection(IReadOnlyNotifiableEnumerable<T> source, Func<T, bool> predicate, Func<T, IEvent<Action>> predicateChanged)
        {
            mSource = source;
            mPredicate = predicate;
            mPredicateChanged = predicateChanged;

            mItemAdded = new(OnObserverArrived, OnObserverLeft);
            mItemRemoved = new(OnObserverArrived, OnObserverLeft);
            mMembershipModified = new(OnObserverArrived, OnObserverLeft);
        }

        // 引用计数（跨三个对外事件的并集）：任一事件首次被订阅 → 开始追踪 source 与各成员谓词；三事件全退订 → 停止追踪。
        // 于是本视图被 source/成员经事件委托 pin 住的边只在"有人在看"期间存在，过滤链可随下游退订自动整体卸载。
        // Items 是惰性枚举、不依赖追踪，无人订阅时照常可枚举。
        void OnObserverArrived()
        {
            if (mActiveEvents++ == 0)
                StartTracking();
        }

        void OnObserverLeft()
        {
            if (--mActiveEvents == 0)
                StopTracking();
        }

        void StartTracking()
        {
            foreach (var item in mSource.Items)
                Track(item);

            mSource.ItemAdded.Subscribe(OnAdd);
            mSource.ItemRemoved.Subscribe(OnRemove);
        }

        void StopTracking()
        {
            mSource.ItemAdded.Unsubscribe(OnAdd);
            mSource.ItemRemoved.Unsubscribe(OnRemove);

            foreach (var (item, handler) in mPredicateHandlers)
                mPredicateChanged(item).Unsubscribe(handler);
            mPredicateHandlers.Clear();
        }

        void OnAdd(T item)
        {
            Track(item);

            if (mPredicate(item))
            {
                mItemAdded.Invoke(item);
                mMembershipModified.Invoke();
            }
        }

        void OnRemove(T item)
        {
            Untrack(item);

            if (mPredicate(item))
            {
                mItemRemoved.Invoke(item);
                mMembershipModified.Invoke();
            }
        }

        // 订阅成员的谓词变化，留存 handler 以便成员移除 / 停止追踪时真正退订（避免订阅泄漏）。
        void Track(T item)
        {
            void OnPredicateChanged()
            {
                if (mPredicate(item))
                    mItemAdded.Invoke(item);
                else
                    mItemRemoved.Invoke(item);
                mMembershipModified.Invoke();
            }

            mPredicateChanged(item).Subscribe(OnPredicateChanged);
            mPredicateHandlers[item] = OnPredicateChanged;
        }

        void Untrack(T item)
        {
            if (mPredicateHandlers.TryGetValue(item, out var handler))
            {
                mPredicateChanged(item).Unsubscribe(handler);
                mPredicateHandlers.Remove(item);
            }
        }

        readonly ObservableActionEvent<T> mItemAdded;
        readonly ObservableActionEvent<T> mItemRemoved;
        readonly ObservableActionEvent mMembershipModified;

        readonly IReadOnlyNotifiableEnumerable<T> mSource;
        readonly Func<T, bool> mPredicate;
        readonly Func<T, IEvent<Action>> mPredicateChanged;
        readonly Dictionary<T, Action> mPredicateHandlers = new();
        int mActiveEvents;
    }

    // 可观测事件：自管订阅表（与原生 += 同义——允许重复、未配对退订即 no-op），并在自身订阅数 0↔1 时回调 owner。
    // 供 WhereCollection 这类「自身即 source」的视图按"是否有人在看"驱动接线生命周期。Invoke 走快照以容忍触发中订/退。
    sealed class ObservableActionEvent<TArg> : IActionEvent<TArg>
    {
        public ObservableActionEvent(Action onActivated, Action onDeactivated)
        {
            mOnActivated = onActivated;
            mOnDeactivated = onDeactivated;
        }

        public void Subscribe(Action<TArg> action)
        {
            mHandlers.Add(action);
            if (mHandlers.Count == 1)
                mOnActivated();
        }

        public void Unsubscribe(Action<TArg> action)
        {
            if (!mHandlers.Remove(action))
                return;
            if (mHandlers.Count == 0)
                mOnDeactivated();
        }

        public void Invoke(TArg arg)
        {
            foreach (var handler in mHandlers.ToArray())
                handler(arg);
        }

        readonly List<Action<TArg>> mHandlers = new();
        readonly Action mOnActivated;
        readonly Action mOnDeactivated;
    }

    sealed class ObservableActionEvent : IActionEvent
    {
        public ObservableActionEvent(Action onActivated, Action onDeactivated)
        {
            mOnActivated = onActivated;
            mOnDeactivated = onDeactivated;
        }

        public void Subscribe(Action action)
        {
            mHandlers.Add(action);
            if (mHandlers.Count == 1)
                mOnActivated();
        }

        public void Unsubscribe(Action action)
        {
            if (!mHandlers.Remove(action))
                return;
            if (mHandlers.Count == 0)
                mOnDeactivated();
        }

        public void Invoke()
        {
            foreach (var handler in mHandlers.ToArray())
                handler();
        }

        readonly List<Action> mHandlers = new();
        readonly Action mOnActivated;
        readonly Action mOnDeactivated;
    }
}
