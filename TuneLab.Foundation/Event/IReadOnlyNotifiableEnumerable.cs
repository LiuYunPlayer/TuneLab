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
    public static IActionEvent<T> WhenAnyItem<T>(this IReadOnlyNotifiableEnumerable<T> collection,
        params Func<T, IActionEvent>[] eventSelectors)
    {
        return new AnyItemEvent<T>(collection, eventSelectors);
    }

    class AnyItemEvent<T> : IActionEvent<T>
    {
        public AnyItemEvent(IReadOnlyNotifiableEnumerable<T> collection, Func<T, IActionEvent>[] selectors)
        {
            mCollection = collection;
            mSelectors = selectors;
            mCollection.ItemAdded.Subscribe(OnAdd);
            mCollection.ItemRemoved.Subscribe(OnRemove);
        }

        public void Subscribe(Action<T> handler)
        {
            mHandlers.Add(handler);
            foreach (var item in mCollection.Items)
                Wire(item, handler);
        }

        public void Unsubscribe(Action<T> handler)
        {
            mHandlers.Remove(handler);
            foreach (var item in mCollection.Items)
                Unwire(item, handler);
        }

        void OnAdd(T item)
        {
            foreach (var handler in mHandlers)
                Wire(item, handler);
        }

        void OnRemove(T item)
        {
            foreach (var handler in mHandlers)
                Unwire(item, handler);
        }

        // 为 (成员, 下游 handler) 对，把成员的每个选择事件接到一个捕获该成员的转发 lambda，
        // 存下转发实例以便精确退订（lambda 身份留存，避免退订泄漏）。
        void Wire(T item, Action<T> handler)
        {
            void Forward() => handler(item);
            var subscriptions = new List<(IActionEvent Event, Action Forward)>();
            foreach (var selector in mSelectors)
            {
                var actionEvent = selector(item);
                actionEvent.Subscribe(Forward);
                subscriptions.Add((actionEvent, Forward));
            }
            mWires[(item, handler)] = subscriptions;
        }

        void Unwire(T item, Action<T> handler)
        {
            if (mWires.Remove((item, handler), out var subscriptions))
            {
                foreach (var (actionEvent, forward) in subscriptions)
                    actionEvent.Unsubscribe(forward);
            }
        }

        readonly IReadOnlyNotifiableEnumerable<T> mCollection;
        readonly Func<T, IActionEvent>[] mSelectors;
        readonly List<Action<T>> mHandlers = new();
        readonly Dictionary<(T, Action<T>), List<(IActionEvent Event, Action Forward)>> mWires = new();
    }

    // 跟随集合任一成员的某个事件：成员增删时自动接上/退订，使用者只订一次。
    public static IEvent<TEvent> WhenAny<T, TEvent>(this IReadOnlyNotifiableEnumerable<T> collection, ISubscriber<T, TEvent> subscriber)
    {
        return new AnyEvent<T, TEvent>(collection, subscriber);
    }

    public static IEvent<TEvent> WhenAny<T, TEvent>(this IReadOnlyNotifiableEnumerable<T> collection, Func<T, IEvent<TEvent>> selector)
    {
        return collection.WhenAny(new SelectorSubscriber<T, TEvent>(selector));
    }

    public static IEvent<TEvent> WhenAny<T, TEvent>(this IReadOnlyNotifiableEnumerable<T> collection, Action<T, TEvent> subscribe, Action<T, TEvent> unsubscribe)
    {
        return collection.WhenAny(new ActionSubscriber<T, TEvent>(subscribe, unsubscribe));
    }

    // 维护“下游 handler 集 × 当前成员集”的活订阅矩阵：成员增删、下游订退，均保持叉乘订阅一致。
    class AnyEvent<T, TEvent> : IEvent<TEvent>
    {
        public AnyEvent(IReadOnlyNotifiableEnumerable<T> collection, ISubscriber<T, TEvent> subscriber)
        {
            mCollection = collection;
            mSubscriber = subscriber;

            mCollection.ItemAdded.Subscribe(OnAdd);
            mCollection.ItemRemoved.Subscribe(OnRemove);
        }

        public void Subscribe(TEvent invokable)
        {
            foreach (var item in mCollection.Items)
            {
                mSubscriber.Subscribe(item, invokable);
            }

            mEvents.Add(invokable);
        }

        public void Unsubscribe(TEvent invokable)
        {
            foreach (var item in mCollection.Items)
            {
                mSubscriber.Unsubscribe(item, invokable);
            }

            mEvents.Remove(invokable);
        }

        void OnAdd(T item)
        {
            foreach (var invokable in mEvents)
            {
                mSubscriber.Subscribe(item, invokable);
            }
        }

        void OnRemove(T item)
        {
            foreach (var invokable in mEvents)
            {
                mSubscriber.Unsubscribe(item, invokable);
            }
        }

        readonly IReadOnlyNotifiableEnumerable<T> mCollection;
        readonly ISubscriber<T, TEvent> mSubscriber;
        readonly List<TEvent> mEvents = new();
    }

    // 响应式过滤视图：成员的谓词翻转时合成 ItemAdded / ItemRemoved，过滤结果随谓词实时变化。
    // 与 WhenAny 同族、可串接（collection.Where(...).WhenAny(...)）。
    public static IReadOnlyNotifiableEnumerable<T> Where<T>(this IReadOnlyNotifiableEnumerable<T> collection, Func<T, bool> predicate, Func<T, IEvent<Action>> predicateChanged) where T : notnull
    {
        return new WhereCollection<T>(collection, predicate, predicateChanged);
    }

    public static IReadOnlyNotifiableEnumerable<T> Where<T>(this IReadOnlyNotifiableEnumerable<T> collection, Func<T, IReadOnlyNotifiableProperty<bool>> predicateProperty) where T : notnull
    {
        return new WhereCollection<T>(collection, item => predicateProperty(item).Value, item => predicateProperty(item).Modified);
    }

    class WhereCollection<T> : IReadOnlyNotifiableEnumerable<T> where T : notnull
    {
        public IActionEvent<T> ItemAdded => mItemAdded;
        public IActionEvent<T> ItemRemoved => mItemRemoved;
        public IActionEvent MembershipModified => mMembershipModified;
        public IEnumerable<T> Items => mCollection.Items.Where(mPredicate);

        public WhereCollection(IReadOnlyNotifiableEnumerable<T> collection, Func<T, bool> predicate, Func<T, IEvent<Action>> predicateChanged)
        {
            mCollection = collection;
            mPredicate = predicate;
            mPredicateChanged = predicateChanged;

            foreach (var item in mCollection.Items)
            {
                Track(item);
            }

            mCollection.ItemAdded.Subscribe(OnAdd);
            mCollection.ItemRemoved.Subscribe(OnRemove);
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

        // 订阅成员的谓词变化，留存 handler 以便成员移除时真正退订（避免订阅泄漏）。
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

        readonly ActionEvent<T> mItemAdded = new();
        readonly ActionEvent<T> mItemRemoved = new();
        readonly ActionEvent mMembershipModified = new();

        readonly IReadOnlyNotifiableEnumerable<T> mCollection;
        readonly Func<T, bool> mPredicate;
        readonly Func<T, IEvent<Action>> mPredicateChanged;
        readonly Dictionary<T, Action> mPredicateHandlers = new();
    }
}
