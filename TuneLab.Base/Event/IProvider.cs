namespace TuneLab.Base.Event;

public interface IProvider<out T>
{
    IActionEvent ObjectWillChange { get; }
    IActionEvent ObjectChanged { get; }
    public T? Object { get; }
}

public static class IProviderExtension
{
    public static IEvent<TEvent> When<T, TEvent>(this IProvider<T> provider, ISubscriber<T, TEvent> subscriber)
    {
        return new WhenEvent<T, TEvent>(provider, subscriber);
    }

    class WhenEvent<T, TEvent> : IEvent<TEvent>
    {
        public WhenEvent(IProvider<T> provider, ISubscriber<T, TEvent> subscriber)
        {
            mProvider = provider;
            mSubscriber = subscriber;

            mProvider.ObjectWillChange.Subscribe(OnObjectWillChange);
            mProvider.ObjectChanged.Subscribe(OnObjectChanged);
        }

        public void Subscribe(TEvent invokable)
        {
            if (mProvider.Object != null)
                mSubscriber.Subscribe(mProvider.Object, invokable);

            mEvents.Add(invokable);
        }

        public void Unsubscribe(TEvent invokable)
        {
            if (mProvider.Object != null)
                mSubscriber.Unsubscribe(mProvider.Object, invokable);

            mEvents.Remove(invokable);
        }

        void OnObjectWillChange()
        {
            if (mProvider.Object == null)
                return;

            foreach (var invokable in mEvents)
            {
                mSubscriber.Unsubscribe(mProvider.Object, invokable);
            }
        }

        void OnObjectChanged()
        {
            if (mProvider.Object == null)
                return;

            foreach (var invokable in mEvents)
            {
                mSubscriber.Subscribe(mProvider.Object, invokable);
            }
        }

        readonly IProvider<T> mProvider;
        readonly ISubscriber<T, TEvent> mSubscriber;
        readonly List<TEvent> mEvents = new();
    }

    public static IEvent<TEvent> When<T, TEvent>(this IProvider<T> provider, Func<T, IEvent<TEvent>> selector)
    {
        return provider.When(new SelectorSubscriber<T, TEvent>(selector));
    }

    public static IEvent<TEvent> When<T, TEvent>(this IProvider<T> provider, Action<T, TEvent> subscribe, Action<T, TEvent> unsubscribe)
    {
        return provider.When(new ActionSubscriber<T, TEvent>(subscribe, unsubscribe));
    }

    public static IProvider<U> Select<T, U>(this IProvider<T> provider, Func<T, U> selector) where U : class
    {
        return new SelectProvider<T, U>(provider, selector);
    }

    class SelectProvider<T, U>(IProvider<T> provider, Func<T, U> selector) : IProvider<U> where U : class
    {
        public IActionEvent ObjectWillChange => provider.ObjectWillChange;
        public IActionEvent ObjectChanged => provider.ObjectChanged;
        public U? Object => provider.Object == null ? null : selector(provider.Object);
    }
}
