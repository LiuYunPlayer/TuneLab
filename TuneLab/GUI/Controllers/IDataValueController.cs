using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.Property;
using TuneLab.Foundation.Utils;
using TuneLab.Primitives.Property;
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

    public static void Bind<T>(this IDataValueController<T> controller, IHolder<IDataProperty<T>> propertyHolder, DisposableManager? context = null) where T : notnull
    {
        var binding = new DataPropertyHolderBinding<T>(controller, propertyHolder);
        context?.Add(binding);
    }

    // 把一个固定的 IDataProperty<T> 直接绑定到控件（属性面板逐字段绑定用）。
    // 字段对象由面板在 SetConfig 时一次性创建，对象切换时整面板重建，故用常量 provider（事件永不触发）即可。
    public static void BindDataProperty<T>(this IDataValueController<T> controller, IDataProperty<T> property, DisposableManager? context = null) where T : notnull
    {
        controller.Bind(new ConstantHolder<IDataProperty<T>>(property), context);
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
        public DataPropertyHolderBinding(IDataValueController<T> controller, IHolder<IDataProperty<T>> propertyHolder)
        {
            mController = controller;
            mPropertyHolder = propertyHolder;
            mController.ValueWillChange.Subscribe(() =>
            {
                if (Property == null)
                    return;

                mHead = Property.Head;
            }, s);

            mController.ValueChanged.Subscribe(() =>
            {
                if (Property == null)
                    return;

               var value = mController.Value;
                Property.DiscardTo(mHead);
                Property.Set(value);
            }, s);

            mController.ValueCommitted.Subscribe(() =>
            {
                if (Property == null)
                    return;

                var head = Property.Head;
                if (mHead == head)
                    return;

                Property.Commit();
            }, s);

            mPropertyHolder.When(p => p.Modified).Subscribe(Refresh, s);

            mPropertyHolder.Modified.Subscribe(Refresh);

            Refresh();
        }

        public void Dispose()
        {
            s.DisposeAll();
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
                if (value.IsInvalid())
                {
                    mController.DisplayNull();
                    return;
                }
            }

            mController.Display(property.Value);
        }

        IDataProperty<T>? Property => mPropertyHolder.Value;

        Head mHead;
        readonly DisposableManager s = new();

        readonly IDataValueController<T> mController;
        readonly IHolder<IDataProperty<T>> mPropertyHolder;
    }
}
