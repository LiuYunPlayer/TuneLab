using System;

namespace TuneLab.Foundation.Event;

// 数据层修改事件：单一事件源、两种订阅形状。
// 无参 Subscribe(Action)      —— 只在结果态触发（canIgnore==false），与旧 Modified.Subscribe(()=>…) 语义一致。
// 带参 Subscribe(Action<bool>) —— 每次都触发，参数即 canIgnore（true=可忽略的调整中间态，false=结果态），订一次拿全信息。
// 两种订阅退订均走原生委托身份，无过滤包装。
// 仅继承 IActionEvent（单一 IEvent<Action>），bool 形状以直接方法声明而非继承 IActionEvent<bool>——
// 否则实现两个 IEvent<> 会令 When/Any 的 IEvent<TEvent> 类型推断产生歧义。
public interface IModifiedEvent : IActionEvent
{
    void Subscribe(Action<bool> action);
    void Unsubscribe(Action<bool> action);
}
