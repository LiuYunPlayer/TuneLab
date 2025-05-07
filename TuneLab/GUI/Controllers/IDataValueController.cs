using ExCSS;
using System;
using System.Threading;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.Utils;

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

    public static IDisposable BindDataProperty<T>(this IDataValueController<T> controller, IDataProperty<T> property) where T : notnull
    {
        return new DataPropertyBinding<T>(controller, property);
    }

    class DataPropertyBinding<T> : IDisposable where T : notnull
    {
        public DataPropertyBinding(IDataValueController<T> controller, IDataProperty<T> property)
        {
            mController = controller;
            mProperty = property;
            mController.ValueWillChange.Subscribe(() =>
            {
                mHead = mProperty.Head;
            }, s);

            mController.ValueChanged.Subscribe(() =>
            {
                var value = mController.Value;
                mProperty.DiscardTo(mHead);
                mProperty.Set(value);
            }, s);

            mController.ValueCommited.Subscribe(() =>
            {
                var head = mProperty.Head;
                if (mHead == head)
                    return;

                mProperty.Commit();
            }, s);

            mController.Display(mProperty.Value);
        }

        public void Dispose()
        {
            s.DisposeAll();
        }

        Head mHead;
        readonly DisposableManager s = new();

        readonly IDataValueController<T> mController;
        readonly IDataProperty<T> mProperty;
    }

    public static IDisposable BindDataProperty<T>(this IDataValueController<T> controller, IProvider<IDataProperty<T>> propertyProvider) where T : notnull
    {
        return new DataPropertyProviderBinding<T>(controller, propertyProvider);
    }

    public static void BindDataProperty<T>(this IDataValueController<T> controller, IProvider<IDataProperty<T>> propertyProvider, DisposableManager disposableManager) where T : notnull
    {
        disposableManager.Add(new DataPropertyProviderBinding<T>(controller, propertyProvider));
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

            if (Property != null)
                mController.Display(Property.Value);
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
