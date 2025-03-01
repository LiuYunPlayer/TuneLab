using TuneLab.Base.Event;

namespace TuneLab.Base.Data;

public interface IReadOnlyDataObjectCollection<out T>
{
    IEvent<TEvent> Any<TEvent>(ISubscriber<T, TEvent> subscriber);
    IEvent<TEvent> Any<TEvent>(Func<T, IEvent<TEvent>> selector) => Any(new SelectorSubscriber<T, TEvent>(selector));
    IEvent<TEvent> Any<TEvent>(Action<T, TEvent> subscribe, Action<T, TEvent> unsubscribe) => Any(new ActionSubscriber<T, TEvent>(subscribe, unsubscribe));
}

public static class IReadOnlyDataObjectCollectionExtension
{
    public static IEvent<TEvent> Any<T, TEvent>(this IReadOnlyDataObjectCollection<T> collection, Func<T, IEvent<TEvent>> selector) where T : IDataObject
    {
        return collection.Any(new SelectorSubscriber<T, TEvent>(selector));
    }

    public static IEvent<TEvent> Any<T, TEvent>(this IReadOnlyDataObjectCollection<T> collection, Action<T, TEvent> subscribe, Action<T, TEvent> unsubscribe) where T : IDataObject
    {
        return collection.Any(new ActionSubscriber<T, TEvent>(subscribe, unsubscribe));
    }
}