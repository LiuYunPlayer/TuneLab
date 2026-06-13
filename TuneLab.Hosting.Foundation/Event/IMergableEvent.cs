using System;

namespace TuneLab.Foundation;

// 自合并的独立事件：事件自身持有合并逻辑（BeginMerge/EndMerge 间的触发合并为一次）。
// 用于与数据对象无关、需要自己合并触发的事件（如选择变更 ISelectableCollection.SelectionChanged）。
// 数据对象的修改通知不走这里——那是数据对象生命周期的属性，见 IModifiedEvent / DataObject.MergeNotify。
public interface IMergableEvent : IActionEvent
{
    void BeginMerge();
    void EndMerge();
}
