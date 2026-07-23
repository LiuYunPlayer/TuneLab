using System;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

public interface IDataObject : IReadOnlyNotifiable
{
    // 富事件面（IActionEvent/IModifiedEvent，宿主内部用）与 SDK 最小事件面（event Action?）同名共存：
    // 直接成员访问解析到富属性，cast 到 IReadOnlyNotifiable 得最小事件——适配由下方 DIM 一次完成，
    // 一切数据对象因此天生可被 SDK 最小面订阅，实现类零样板。
    new IModifiedEvent Modified { get; }
    // 改前事件，merge 语义与 Modified 对偶：作用域内首次 canIgnore=false 必达、其余可忽略，收口重置。
    new IModifiedEvent WillModify { get; }
    Head Head { get; }

    // 最小面适配（DIM）：SDK 的 IActionEvent 面直接由富事件 IModifiedEvent 满足（IModifiedEvent : IActionEvent）。
    // 经 IActionEvent 的无参 Subscribe 只收结果态（canIgnore=false），merge 中间态不外漏。
    IActionEvent IReadOnlyNotifiable.WillModify => WillModify;
    IActionEvent IReadOnlyNotifiable.Modified => Modified;
    void Attach(IDataObject parent);
    void Detach();
    // 合并通知作用域：进=BeginMergeNotify、出（Dispose）=EndMergeNotify，异常也平衡。
    // 把一段多步改动的中间通知合并、结尾发一次结果态（见 ModifiedEvent / DataObject）。
    IDisposable MergeNotify();
    // 显式合并边界（跨方法 begin/end 用）；同一段内成对调用，常态优先用 MergeNotify() 作用域。
    void BeginMergeNotify();
    void EndMergeNotify();
    // 文档当前是否处于"无未提交命令"的可申请提交态：为真才可安全发起一段新的提交。
    // 委托到撤销根（DataDocument），用于在另一处 UI 操作中途时拒绝发起脚本/批量提交，避免吞掉它的未提交改动。
    bool Pushable();
    bool Commit();
    bool Commit(string description, string? detail = null);
    bool Discard();
    bool DiscardTo(Head head);
    bool Undo();
    bool Redo();

    // 借壳数据对象：自身不持状态，把整套文档身份（Head/Commit/Modified/merge/undo）转发给被包裹对象，
    // 子类只把类型化读写投影到别处（如属性面板字段适配器共享底层文档撤销根）。接口为纯契约后，仅转发公开成员。
    internal class Wrapper(IDataObject dataObject) : IDataObject
    {
        public IModifiedEvent Modified => dataObject.Modified;
        public IModifiedEvent WillModify => dataObject.WillModify;
        public Head Head => dataObject.Head;
        public void Attach(IDataObject parent) => dataObject.Attach(parent);
        public void Detach() => dataObject.Detach();
        public IDisposable MergeNotify() => dataObject.MergeNotify();
        public void BeginMergeNotify() => dataObject.BeginMergeNotify();
        public void EndMergeNotify() => dataObject.EndMergeNotify();
        public bool Pushable() => dataObject.Pushable();
        public bool Commit() => dataObject.Commit();
        public bool Commit(string description, string? detail = null) => dataObject.Commit(description, detail);
        public bool Discard() => dataObject.Discard();
        public bool DiscardTo(Head head) => dataObject.DiscardTo(head);
        public bool Undo() => dataObject.Undo();
        public bool Redo() => dataObject.Redo();
    }
}

public interface IReadOnlyDataObject<out T> : IDataObject
{
    T GetInfo();
}

// 契约设值：从完整 info 应用值，与 GetInfo() 对称。纯应用、无交互副作用——复合节点向子扇出 / 装载走它。
// 各实现一律 public 隐式实现（业务复合对象与基础件同一形态），concrete/接口类型上都可直接点调。
public interface IDataObject<T> : IReadOnlyDataObject<T>
{
    void SetInfo(T info);
}
