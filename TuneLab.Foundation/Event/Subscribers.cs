using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation;

// ISubscriber 的实现细节（internal）：把一对 subscribe/unsubscribe 委托适配成 ISubscriber。
// 经 InternalsVisibleTo 供宿主 IHolder.When 复用，但不进 SDK public 契约（插件只用封装好的 WhenAny 系列）。
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

// ISubscriber 的实现细节（internal）：把"成员→其某事件"的选择器适配成 ISubscriber。
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
