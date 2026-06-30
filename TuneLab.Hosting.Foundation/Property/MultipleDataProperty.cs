using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;

namespace TuneLab.Foundation;

// 多选编辑的复合叶子属性：把多个同类 IDataProperty<T> 合成一个 IDataProperty<T> 外观（如多 part 的 Gain）。
// 与 MultipleDataPropertyObject 对偶，区别是这里合并的是单个 typed 叶子值（part.Gain 这类常驻独立字段）而非属性包。
//
// 读三态（经 IRawValueProperty.RawValue，绑定层 IDataValueController 据此分派 DisplayMultiple/DisplayNull）：
//   0 对象（无选中）→ Null；各对象值不全等 → Multiple；全等 → 该值。
// 写：扇出到所有对象（三段 merge：全 begin → 全 set → 全 end），消除"部分写完→瞬时 Multiple"的中间态闪烁
//   （语义同 MultipleDataPropertyObject.SetValue）。
// 撤销根（IDataObject）：Head/Commit/DiscardTo 委托首对象——同一文档共享一个撤销栈，DiscardTo(首对象 Head)
//   会回滚该 head 之后文档内的全部命令（含其余对象的扇出写），故拖动编辑能整体回退、一次提交归一个撤销单元。
//   Modified/WillModify 合并所有对象，使扇出过程"最后一次刷新"看到全部已写完的最终值。
// 允许 0 对象（无选中）：撤销机制成 no-op，仅供面板把控件绑出 Invalid 态。
public class MultipleDataProperty<T> : IDataProperty<T>, IRawValueProperty where T : notnull
{
    public MultipleDataProperty(IReadOnlyCollection<IDataProperty<T>> properties, T defaultValue, Func<T, PropertyValue> toRawValue)
    {
        mProperties = properties as IReadOnlyList<IDataProperty<T>> ?? new List<IDataProperty<T>>(properties);
        mDefaultValue = defaultValue;
        mToRawValue = toRawValue;
        mModified = mProperties.Select(p => p.Modified).MergeModified();
        mWillModify = mProperties.Select(p => p.WillModify).MergeModified();
        mRoot = mProperties.Count > 0 ? mProperties[0] : null;
    }

    public T Value => GetInfo();
    public T GetInfo() => mRoot != null ? mRoot.GetInfo() : mDefaultValue;

    // 三态原始值：空→Null、不全等→Multiple、全等→该值。
    public PropertyValue RawValue
    {
        get
        {
            if (mProperties.Count == 0)
                return PropertyValue.Null;
            var first = mProperties[0].GetInfo();
            for (int i = 1; i < mProperties.Count; i++)
                if (!EqualityComparer<T>.Default.Equals(mProperties[i].GetInfo(), first))
                    return PropertyValue.Multiple;
            return mToRawValue(first);
        }
    }

    public void Set(T value) => FanOut(p => p.Set(value));
    public void SetInfo(T value) => FanOut(p => p.SetInfo(value));

    void FanOut(Action<IDataProperty<T>> action)
    {
        foreach (var p in mProperties) p.BeginMergeNotify();
        foreach (var p in mProperties) action(p);
        foreach (var p in mProperties) p.EndMergeNotify();
    }

    public IModifiedEvent Modified => mModified;
    public IModifiedEvent WillModify => mWillModify;
    public Head Head => mRoot?.Head ?? default;
    public void Attach(IDataObject parent) { }
    public void Detach() { }
    public IDisposable MergeNotify()
    {
        if (mProperties.Count == 0)
            return EmptyDisposable.Shared;
        foreach (var p in mProperties) p.BeginMergeNotify();
        return new MergeScope(mProperties);
    }
    public void BeginMergeNotify() { foreach (var p in mProperties) p.BeginMergeNotify(); }
    public void EndMergeNotify() { foreach (var p in mProperties) p.EndMergeNotify(); }
    public bool Pushable() => mRoot?.Pushable() ?? true;
    public bool Commit() => mRoot?.Commit() ?? false;
    public bool Discard() => mRoot?.Discard() ?? false;
    public bool DiscardTo(Head head) => mRoot?.DiscardTo(head) ?? false;
    public bool Undo() => mRoot?.Undo() ?? false;
    public bool Redo() => mRoot?.Redo() ?? false;

    sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Shared = new();
        public void Dispose() { }
    }

    sealed class MergeScope(IReadOnlyList<IDataProperty<T>> properties) : IDisposable
    {
        public void Dispose() { foreach (var p in properties) p.EndMergeNotify(); }
    }

    readonly IReadOnlyList<IDataProperty<T>> mProperties;
    readonly T mDefaultValue;
    readonly Func<T, PropertyValue> mToRawValue;
    readonly IModifiedEvent mModified;
    readonly IModifiedEvent mWillModify;
    readonly IDataProperty<T>? mRoot;
}
