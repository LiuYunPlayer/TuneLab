using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Event;

namespace TuneLab.Foundation.Document;

// 数据对象集合的只读形状：当前成员（Items）+ 成员增删事件。
// WhenAny / Where 等组合子建在这三个成员之上，对 List / Map / LinkedList 统一适用。
public interface IReadOnlyDataCollection<out T>
{
    IActionEvent<T> ItemAdded { get; }
    IActionEvent<T> ItemRemoved { get; }
    IEnumerable<T> Items { get; }
}

public static class IReadOnlyDataObjectCollectionExtension
{
    // 跟随集合任一成员的某个事件：成员增删时自动接上/退订，使用者只订一次。
    public static IEvent<TEvent> WhenAny<T, TEvent>(this IReadOnlyDataCollection<T> collection, ISubscriber<T, TEvent> subscriber)
    {
        return new AnyEvent<T, TEvent>(collection, subscriber);
    }

    public static IEvent<TEvent> WhenAny<T, TEvent>(this IReadOnlyDataCollection<T> collection, Func<T, IEvent<TEvent>> selector)
    {
        return collection.WhenAny(new SelectorSubscriber<T, TEvent>(selector));
    }

    public static IEvent<TEvent> WhenAny<T, TEvent>(this IReadOnlyDataCollection<T> collection, Action<T, TEvent> subscribe, Action<T, TEvent> unsubscribe)
    {
        return collection.WhenAny(new ActionSubscriber<T, TEvent>(subscribe, unsubscribe));
    }

    // 维护“下游 handler 集 × 当前成员集”的活订阅矩阵：成员增删、下游订退，均保持叉乘订阅一致。
    class AnyEvent<T, TEvent> : IEvent<TEvent>
    {
        public AnyEvent(IReadOnlyDataCollection<T> collection, ISubscriber<T, TEvent> subscriber)
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

        readonly IReadOnlyDataCollection<T> mCollection;
        readonly ISubscriber<T, TEvent> mSubscriber;
        readonly List<TEvent> mEvents = new();
    }

    // 响应式过滤视图：成员的谓词翻转时合成 ItemAdded / ItemRemoved，过滤结果随谓词实时变化。
    // 与 WhenAny 同族、可串接（collection.Where(...).WhenAny(...)）。
    public static IReadOnlyDataCollection<T> Where<T>(this IReadOnlyDataCollection<T> collection, Func<T, bool> predicate, Func<T, IEvent<Action>> predicateChanged) where T : notnull
    {
        return new WhereCollection<T>(collection, predicate, predicateChanged);
    }

    public static IReadOnlyDataCollection<T> Where<T>(this IReadOnlyDataCollection<T> collection, Func<T, INotifiableProperty<bool>> predicateProperty) where T : notnull
    {
        return new WhereCollection<T>(collection, item => predicateProperty(item).Value, item => predicateProperty(item).Modified);
    }

    class WhereCollection<T> : IReadOnlyDataCollection<T> where T : notnull
    {
        public IActionEvent<T> ItemAdded => mItemAdded;
        public IActionEvent<T> ItemRemoved => mItemRemoved;
        public IEnumerable<T> Items => mCollection.Items.Where(mPredicate);

        public WhereCollection(IReadOnlyDataCollection<T> collection, Func<T, bool> predicate, Func<T, IEvent<Action>> predicateChanged)
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
                mItemAdded.Invoke(item);
        }

        void OnRemove(T item)
        {
            Untrack(item);

            if (mPredicate(item))
                mItemRemoved.Invoke(item);
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

        readonly IReadOnlyDataCollection<T> mCollection;
        readonly Func<T, bool> mPredicate;
        readonly Func<T, IEvent<Action>> mPredicateChanged;
        readonly Dictionary<T, Action> mPredicateHandlers = new();
    }
}
