using System.Collections;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Event;

namespace TuneLab.Foundation.Document;

public interface IReadOnlyDataCollection<out T>
{
    IActionEvent<T> ItemAdded { get; }
    IActionEvent<T> ItemRemoved { get; }
    IEnumerable<T> Items { get; }
}

public static class IReadOnlyDataObjectCollectionExtension
{
    public static IEvent<TEvent> Any<T, TEvent>(this IReadOnlyDataCollection<T> collection, ISubscriber<T, TEvent> subscriber)
    {
        return new AnyEvent<T, TEvent>(collection, subscriber);
    }

    public static IEvent<TEvent> Any<T, TEvent>(this IReadOnlyDataCollection<T> collection, Func<T, IEvent<TEvent>> selector)
    {
        return collection.Any(new SelectorSubscriber<T, TEvent>(selector));
    }

    public static IEvent<TEvent> Any<T, TEvent>(this IReadOnlyDataCollection<T> collection, Action<T, TEvent> subscribe, Action<T, TEvent> unsubscribe)
    {
        return collection.Any(new ActionSubscriber<T, TEvent>(subscribe, unsubscribe));
    }

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
            foreach (var dataObject in mCollection.Items)
            {
                mSubscriber.Subscribe(dataObject, invokable);
            }

            mEvents.Add(invokable);
        }

        public void Unsubscribe(TEvent invokable)
        {
            foreach (var dataObject in mCollection.Items)
            {
                mSubscriber.Unsubscribe(dataObject, invokable);
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
                mSubscriber.Subscribe(item, invokable);
            }
        }

        readonly IReadOnlyDataCollection<T> mCollection;
        readonly ISubscriber<T, TEvent> mSubscriber;
        readonly List<TEvent> mEvents = [];
    }

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

            mCollection.ItemAdded.Subscribe(OnAdd);
            mCollection.ItemRemoved.Subscribe(OnRemove);
        }

        void OnAdd(T item)
        {
            mPredicateChanged(item).Subscribe(() =>
            {
                if (mPredicate(item))
                    mItemAdded.Invoke(item);
                else
                    mItemRemoved.Invoke(item);
            });

            if (mPredicate(item))
                mItemAdded.Invoke(item);
        }

        void OnRemove(T item)
        {
            if (mPredicateChangedEvents.TryGetValue(item, out var disposable))
            {
                disposable.Dispose();
                mPredicateChangedEvents.Remove(item);
            }

            if (mPredicate(item))
                mItemRemoved.Invoke(item);
        }

        readonly ActionEvent<T> mItemAdded = new();
        readonly ActionEvent<T> mItemRemoved = new();

        readonly IReadOnlyDataCollection<T> mCollection;
        readonly Func<T, bool> mPredicate;
        readonly Func<T, IEvent<Action>> mPredicateChanged;
        readonly Dictionary<T, IDisposable> mPredicateChangedEvents = [];
    }
}