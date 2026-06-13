using System;

namespace TuneLab.Foundation;

// 复用 ActionEvent 作为 settled（无参）通道，只增量加 bool（全量）通道——不重复多播/退订逻辑、不额外分配对象。
public class ModifiedEvent : ActionEvent, IModifiedEvent
{
    public void Subscribe(Action<bool> action) => mAll += action;
    public void Unsubscribe(Action<bool> action) => mAll -= action;

    // canIgnore=true（调整中间态）只通知全量订阅者；canIgnore=false（结果态）两类订阅者都通知。
    public void Invoke(bool canIgnore)
    {
        mAll?.Invoke(canIgnore);
        if (!canIgnore)
            Invoke();
    }

    event Action<bool>? mAll;
}
