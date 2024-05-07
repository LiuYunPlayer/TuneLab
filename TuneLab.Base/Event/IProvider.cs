using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Base.Event;

public interface IProvider<out T>
{
    IActionEvent ObjectWillChange { get; }
    IActionEvent ObjectChanged { get; }
    public T? Object { get; }
    IEvent<TEvent> When<TEvent>(ISubscriber<T, TEvent> subscriber);
}

public static class IProviderExtension
{
    public static IEvent<TEvent> When<T, TEvent>(this IProvider<T> provider, Func<T, IEvent<TEvent>> selector)
    {
        return provider.When(new SelectorSubscriber<T, TEvent>(selector));
    }

    public static IEvent<TEvent> When<T, TEvent>(this IProvider<T> provider, Action<T, TEvent> subscribe, Action<T, TEvent> unsubscribe)
    {
        return provider.When(new ActionSubscriber<T, TEvent>(subscribe, unsubscribe));
    }
}
