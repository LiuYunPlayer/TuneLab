using System;

namespace TuneLab.Foundation;

// 复用 ActionEvent 作为 settled（无参/默认脸）通道，只增量加 bool（全量脸 AsEverytime）通道——不重复多播/退订逻辑。
public class ModifiedEvent : ActionEvent, IModifiedEvent
{
    // 全量脸：返回稳定后端（同一对象、零分配），可直接进泛型组合子。
    public IActionEvent<bool> AsEverytime() => mAll;

    // canIgnore=true（调整中间态）只通知全量脸订阅者；canIgnore=false（结果态）两脸都通知。
    public void Invoke(bool canIgnore)
    {
        mAll.Invoke(canIgnore);
        if (!canIgnore)
            Invoke();
    }

    readonly ActionEvent<bool> mAll = new();
}
