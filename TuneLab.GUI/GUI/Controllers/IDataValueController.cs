using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using static TuneLab.GUI.Controllers.IValueControllerExtension;

namespace TuneLab.GUI.Controllers;

public interface IDataValueController<T> : IValueController<T>
{
    IActionEvent ValueWillChange { get; }
}

public static class IDataValueControllerExtension
{
    public static IDataValueController<U> Select<T, U>(this IDataValueController<T> valueController, Func<T, U> to, Func<U, T> from)
    {
        return new SelectController<T, U>(valueController, to, from);
    }

    class SelectController<T, U>(IDataValueController<T> valueController, Func<T, U> to, Func<U, T> from) : IValueControllerExtension.SelectController<T, U>(valueController, to, from), IDataValueController<U>
    {
        public IActionEvent ValueWillChange => valueController.ValueWillChange;
    }

    public static IDataValueController<T> Select<T>(this IDataValueController<T> valueController, Func<T, T> to)
    {
        return new SelectController<T, T>(valueController, to, x => x);
    }

    public static void Bind<T>(this IDataValueController<T> controller, IHolder<IDataProperty<T>> propertyHolder, DisposableManager? context = null, string description = "Edit Properties", string? detail = null) where T : notnull
    {
        var binding = new DataPropertyHolderBinding<T>(controller, propertyHolder, description, detail);
        context?.Add(binding);
    }

    // 把一个固定的 IDataProperty<T> 直接绑定到控件（属性面板逐字段绑定用）。
    // 字段对象由面板在 SetConfig 时一次性创建，对象切换时整面板重建，故用常量 provider（事件永不触发）即可。
    public static void BindDataProperty<T>(this IDataValueController<T> controller, IDataProperty<T> property, DisposableManager? context = null, string description = "Edit Properties", string? detail = null) where T : notnull
    {
        controller.Bind(new ConstantHolder<IDataProperty<T>>(property), context, description, detail);
    }

    class ConstantHolder<T>(T value) : IHolder<T>
    {
        public IActionEvent WillModify => mNever;
        public IActionEvent Modified => mNever;
        public T? Value => value;
        readonly ActionEvent mNever = new();
    }

    class DataPropertyHolderBinding<T> : IDisposable where T : notnull
    {
        public DataPropertyHolderBinding(IDataValueController<T> controller, IHolder<IDataProperty<T>> propertyHolder, string description, string? detail)
        {
            mController = controller;
            mPropertyHolder = propertyHolder;
            mDescription = description;
            mDetail = detail;
            mController.ValueWillChange.Subscribe(() =>
            {
                var property = Property;
                if (property == null || mEditingProperty != null)
                    return;

                // 编辑全程套一层 merge：中间态（每帧/每键的 Set）被纳入同一作用域，只发 canIgnore 中间通知、不发结果态，
                // 直到 ValueCommitted 退出 merge 才发一次结果态。使"结果态 Modified = 用户提交"语义成立——面板重算
                //（订阅结果态）只在提交时触发、拖动/输入过程中不触发；并把整段编辑归为一个撤销单元。
                mEditingProperty = property;
                mMergeStartHead = property.Head;
                mCanDiscardEmptyMerge = property.Pushable();
                property.BeginMergeNotify();
                // mHead 必须捕获在 BeginMergeNotify 之后：ValueChanged 里 DiscardTo(mHead) 只回退本次编辑写入的值，
                // 绝不能把 BeginMergeNotify 命令一并回退——否则首个 ValueChanged 就把 merge 作用域撤销掉，flag 归 0，
                // 此后每次 Set 都落在 flag=0 发结果态，使拖动/输入全程持续触发面板重算（merge 形同虚设）。
                mHead = property.Head;
            }, s);

            mController.ValueChanged.Subscribe(() =>
            {
                var property = mEditingProperty;
                if (property == null)
                    return;

                var value = mController.Value;
                property.DiscardTo(mHead);
                property.Set(value);
            }, s);

            mController.ValueCommitted.Subscribe(() => FinishEdit(true), s);

            // 跟随属性内容变化刷新控件显示。订阅带 canIgnore 的形式，中间态(canIgnore==true)与结果态都刷新，
            // 使本控件能实时反映他处编辑（如钢琴窗拖动）对同一属性的中间过程，而不必等其提交。
            // 会发中间态的只有拖拽类控件，自编辑时回灌同值无害；文本框编辑全程不发中间态，故无光标干扰之虞。
            // （注：这只刷新本字段控件的显示，与"面板重算只在提交时触发"无关——后者是另一处订阅结果态的消费者。）
            mPropertyHolder.When(p => p.Modified.AsEverytime()).Subscribe(_ => Refresh(), s);

            mPropertyHolder.Modified.Subscribe(Refresh);

            Refresh();
        }

        public void Dispose()
        {
            // 兜底：编辑中途绑定被释放（如编辑时切换选中导致控件重绑）时补一次 EndMergeNotify，
            // 否则 BeginMergeNotify 无配对、数据对象 merge 计数泄漏，将永不再发结果态。
            FinishEdit(false);
            s.DisposeAll();
        }

        // 必须在 EndMergeNotify 前判断是否真的写入过数据：EndMergeNotify 自身也是命令，会推进 Head。
        // 无变化时关闭 merge 后撤掉空的 begin/end 命令对；若编辑开始时文档是干净的，Discard 还能补发
        // 最终 StatusChanged，使保存标记和 Undo/Redo 可用状态回到编辑前。
        void FinishEdit(bool commit)
        {
            var property = mEditingProperty;
            if (property == null)
                return;

            bool dataChanged = property.Head != mHead;
            EndMerge();

            if (!dataChanged)
            {
                if (mCanDiscardEmptyMerge)
                    property.Discard();
                else
                    property.DiscardTo(mMergeStartHead);
                return;
            }

            if (commit)
                property.Commit(mDescription, mDetail);
        }

        // 退出编辑 merge（幂等）：仅在已进入时 EndMergeNotify，避免无配对的多发。
        void EndMerge()
        {
            var property = mEditingProperty;
            if (property == null)
                return;

            mEditingProperty = null;
            property.EndMergeNotify();
        }

        // 按三态分派：原始值为 Multiple→DisplayMultiple、Invalid→DisplayNull，否则 coerce 成 T 走 Display。
        // 字段（PropertyField）经 IRawValueProperty 暴露未 coerce 的原始值；非该来源的退化为只看有无 Property。
        void Refresh()
        {
            var property = Property;
            if (property == null)
            {
                mController.DisplayNull();
                return;
            }

            if (property is IRawValueProperty raw)
            {
                var value = raw.RawValue;
                if (value.IsMultiple())
                {
                    mController.DisplayMultiple();
                    return;
                }
                if (value.IsNull())
                {
                    mController.DisplayNull();
                    return;
                }
            }

            mController.Display(property.Value);
        }

        IDataProperty<T>? Property => mPropertyHolder.Value;

        Head mHead;
        Head mMergeStartHead;
        bool mCanDiscardEmptyMerge;
        readonly DisposableManager s = new();

        readonly IDataValueController<T> mController;
        readonly IHolder<IDataProperty<T>> mPropertyHolder;
        readonly string mDescription;
        readonly string? mDetail;
        IDataProperty<T>? mEditingProperty;
    }
}
