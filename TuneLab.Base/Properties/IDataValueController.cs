using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Base.Event;
using TuneLab.Base.Utils;
using static TuneLab.Base.Properties.IValueControllerExtension;

namespace TuneLab.Base.Properties;

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

    public static void Bind<T>(this IDataValueController<T> controller, IProvider<IDataProperty<T>> propertyProvider, DisposableManager? context = null) where T : notnull
    {
        var binding = new DataPropertyProviderBinding<T>(controller, propertyProvider);
        context?.Add(binding);
    }

    class DataPropertyProviderBinding<T> : IDisposable where T : notnull
    {
        public DataPropertyProviderBinding(IDataValueController<T> controller, IProvider<IDataProperty<T>> propertyProvider)
        {
            mController = controller;
            mPropertyProvider = propertyProvider;
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

            mController.ValueCommited.Subscribe(() =>
            {
                if (Property == null)
                    return;

                var head = Property.Head;
                if (mHead == head)
                    return;

                Property.Commit();
            }, s);

            mPropertyProvider.When(p => p.Modified).Subscribe(() => 
            {
                if (Property == null)
                    return; 
                
                mController.Display(Property.Value); 
            }, s);

            mPropertyProvider.ObjectChanged.Subscribe(() =>
            {
                if (Property == null)
                {
                    mController.DisplayNull();
                }
                else
                {
                    mController.Display(Property.Value);
                }
            });
        }

        public void Dispose()
        {
            s.DisposeAll();
        }

        IDataProperty<T>? Property => mPropertyProvider.Object;

        Head mHead;
        readonly DisposableManager s = new();

        readonly IDataValueController<T> mController;
        readonly IProvider<IDataProperty<T>> mPropertyProvider;
    }
}
