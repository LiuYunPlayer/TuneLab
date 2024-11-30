using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.Event;

public interface ISubscriber<in T, in TFunction>
{
    void Subscribe(T observable, TFunction action);
    void Unsubscribe(T observable, TFunction action);
}

public static class ISubscriberExtension
{
    public static void Subscribe<T, TFunction>(this ISubscriber<T, TFunction> subscriber, T observable, IInvokableEvent<TFunction> action)
    {
        subscriber.Subscribe(observable, action.InvokeEvent);
    }
}
