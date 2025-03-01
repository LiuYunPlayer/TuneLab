namespace TuneLab.Base.Event;

internal class ActionSubscriber<T, TEvent>(Action<T, TEvent> subscribe, Action<T, TEvent> unsubscribe) : ISubscriber<T, TEvent>
{
    public void Subscribe(T observable, TEvent function)
    {
        subscribe(observable, function);
    }

    public void Unsubscribe(T observable, TEvent function)
    {
        unsubscribe(observable, function);
    }
}

internal class SelectorSubscriber<T, TEvent>(Func<T, IEvent<TEvent>> selector) : ISubscriber<T, TEvent>
{
    public void Subscribe(T observable, TEvent function)
    {
        selector(observable).Subscribe(function);
    }

    public void Unsubscribe(T observable, TEvent function)
    {
        selector(observable).Unsubscribe(function);
    }
}
