using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Event;
using TuneLab.Primitives.Property;

namespace TuneLab.Foundation.Property;

// 多选编辑的复合数据源：把多个属性对象合成一个 IDataPropertyObject 外观。
// 读：三态——0 对象（无选中）返 Invalid；各对象同 key 不完全相等返 Multiple；全等返该值。
// 写：扇出到所有对象。导航 Object(key)：复合各对象的 Object(key) 而成，自然递归——
//     缺该嵌套的成员经其懒视图读出 default，仍正确参与三态比较（与有该嵌套的成员不等即 Multiple）。
// 撤销根（IDataObject）：Head/Commit/DiscardTo 委托首对象——同一文档共享一个 Head，一次提交即把所有对象的改动
//     归为一个撤销单元；但 Modified 合并所有对象的修改事件，使扇出/撤销过程中"最后一次刷新"看到的是全部已写完的
//     最终值（否则只听首对象会在它先被写、其余未写时算出 Multiple 并卡住）。各成员的撤销根都根锚在最外层对象，
//     其 Modified 冒泡覆盖全部嵌套写。允许 0 对象（无选中）：撤销机制成 no-op，仅供面板在遮罩下把控件绑出 Invalid 态。
// 本对象只是面板侧瞬态外观、从不挂进文档树，故文档机制内部成员（Attach/Detach 等）不会被实际用到。
public class MultipleDataPropertyObject : IDataPropertyObject
{
    public MultipleDataPropertyObject(IReadOnlyCollection<IDataPropertyObject> dataObjects)
    {
        mDataObjects = dataObjects as IReadOnlyList<IDataPropertyObject> ?? dataObjects.ToList();
        mModified = new MergedModifiedEvent(mDataObjects);
        mWillModified = new MergedWillModifiedEvent(mDataObjects);
        mRoot = mDataObjects.Count > 0 ? mDataObjects[0] : null;
    }

    public IDataPropertyObject Object(string key)
    {
        var members = new List<IDataPropertyObject>(mDataObjects.Count);
        foreach (var dataObject in mDataObjects)
            members.Add(dataObject.Object(key));
        return new MultipleDataPropertyObject(members);
    }

    public PropertyValue GetValue(string key, PropertyValue defaultValue)
    {
        if (mDataObjects.Count == 0)
            return PropertyValue.Null;

        var first = mDataObjects[0].GetValue(key, defaultValue);
        for (int i = 1; i < mDataObjects.Count; i++)
        {
            if (!mDataObjects[i].GetValue(key, defaultValue).Equals(first))
                return PropertyValue.Multiple;
        }
        return first;
    }

    // 扇出期间合并通知：先让所有对象进 merge，再统一写值，最后统一退 merge。
    // 这样写到一半时各对象只发 canIgnore 中间通知（结果态订阅者不触发），全部写完退 merge 才发结果态，
    // 此刻每次刷新看到的都是"所有对象已写完"的最终值——消除"部分写完→瞬时 Multiple"的中间态闪烁
    // （否则会反复清空正在编辑的文本框/令 checkbox 闪回 dash）。三段分开循环是关键：必须所有 set 完成后才退首个 merge。
    public void SetValue(string key, PropertyValue value)
    {
        foreach (var dataObject in mDataObjects)
            dataObject.BeginMergeNotify();
        foreach (var dataObject in mDataObjects)
            dataObject.SetValue(key, value);
        foreach (var dataObject in mDataObjects)
            dataObject.EndMergeNotify();
    }

    // ---- IDataObject：撤销机制委托首对象（文档级共享），Modified/WillModified 取合并事件，无选中时全 no-op ----
    public IModifiedEvent Modified => mModified;
    public IActionEvent WillModified => mWillModified;
    public Head Head => mRoot?.Head ?? default;
    public void Attach(IDataObject parent) { }
    public void Detach() { }
    // merge 通知作用于**所有**成员（不只首成员）：binding 在编辑全程套一层 merge 时，需把所有被扇出写入的成员
    // 都纳入同一 merge 作用域，中间态才不会逐成员发结果态——否则非首成员每次写仍发结果态、触发面板重算。
    public IDisposable MergeNotify()
    {
        if (mDataObjects.Count == 0)
            return EmptyDisposable.Shared;
        foreach (var dataObject in mDataObjects)
            dataObject.BeginMergeNotify();
        return new MultiMergeScope(mDataObjects);
    }
    public void BeginMergeNotify() { foreach (var dataObject in mDataObjects) dataObject.BeginMergeNotify(); }
    public void EndMergeNotify() { foreach (var dataObject in mDataObjects) dataObject.EndMergeNotify(); }
    public bool Commit() => mRoot?.Commit() ?? false;
    public bool Discard() => mRoot?.Discard() ?? false;
    public bool DiscardTo(Head head) => mRoot?.DiscardTo(head) ?? false;
    public bool Undo() => mRoot?.Undo() ?? false;
    public bool Redo() => mRoot?.Redo() ?? false;

    // 任一对象的 Modified 都转发给同一订阅者：扇出/撤销逐对象触发刷新，最后一次刷新时全部已写完 → 显示最终值。
    // 两种订阅形状（无参=结果态、带 bool=全量）都转发到各对象的 Modified。
    class MergedModifiedEvent(IReadOnlyList<IDataPropertyObject> dataObjects) : IModifiedEvent
    {
        public void Subscribe(Action invokable)
        {
            foreach (var dataObject in dataObjects)
                dataObject.Modified.Subscribe(invokable);
        }

        public void Unsubscribe(Action invokable)
        {
            foreach (var dataObject in dataObjects)
                dataObject.Modified.Unsubscribe(invokable);
        }

        public void Subscribe(Action<bool> invokable)
        {
            foreach (var dataObject in dataObjects)
                dataObject.Modified.Subscribe(invokable);
        }

        public void Unsubscribe(Action<bool> invokable)
        {
            foreach (var dataObject in dataObjects)
                dataObject.Modified.Unsubscribe(invokable);
        }
    }

    sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Shared = new();
        public void Dispose() { }
    }

    // 退出 merge 作用域时对所有成员 EndMergeNotify（与 MergeNotify 进入时的全成员 BeginMergeNotify 对称）。
    sealed class MultiMergeScope(IReadOnlyList<IDataPropertyObject> dataObjects) : IDisposable
    {
        public void Dispose()
        {
            foreach (var dataObject in dataObjects)
                dataObject.EndMergeNotify();
        }
    }

    // 任一对象的 WillModified 都转发给同一订阅者（与 MergedModifiedEvent 同理，改前事件无 bool 形状）。
    class MergedWillModifiedEvent(IReadOnlyList<IDataPropertyObject> dataObjects) : IActionEvent
    {
        public void Subscribe(Action invokable)
        {
            foreach (var dataObject in dataObjects)
                dataObject.WillModified.Subscribe(invokable);
        }

        public void Unsubscribe(Action invokable)
        {
            foreach (var dataObject in dataObjects)
                dataObject.WillModified.Unsubscribe(invokable);
        }
    }

    readonly IReadOnlyList<IDataPropertyObject> mDataObjects;
    readonly MergedModifiedEvent mModified;
    readonly MergedWillModifiedEvent mWillModified;
    readonly IDataPropertyObject? mRoot;
}
