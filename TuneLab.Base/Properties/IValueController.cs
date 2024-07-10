using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Base.Event;
using TuneLab.Base.Utils;

namespace TuneLab.Base.Properties;

public interface IValueController<T>
{
    IActionEvent ValueWillChange => ActionEvent.Empty;
    IActionEvent ValueChanged => ActionEvent.Empty;
    IActionEvent ValueCommited { get; }
    T Value { get; }
    void Display(T value);
    void DisplayNull() { }
    void DisplayMultiple() { }
}

public static class IValueControllerExtension
{
    public static void Bind<T>(this IValueController<T> controller, IProvider<IDataProperty<T>> propertyProvider, DisposableManager? context = null) where T : notnull
    {
        var binding = new DataPropertyProviderBinding<T>(controller, propertyProvider);
        context?.Add(binding);
    }

    class DataPropertyProviderBinding<T> : IDisposable where T : notnull
    {
        public DataPropertyProviderBinding(IValueController<T> controller, IProvider<IDataProperty<T>> propertyProvider)
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

        readonly IValueController<T> mController;
        readonly IProvider<IDataProperty<T>> mPropertyProvider;
    }

    public static void Bind<T>(this IValueController<T> controller, INotifiableProperty<T> property, bool syncWhileModifying = false, DisposableManager? context = null)
    {
        var binding = new NotifiablePropertyBinding<T>(controller, property, syncWhileModifying);
        context?.Add(binding);
    }

    class NotifiablePropertyBinding<T> : IDisposable
    {
        public NotifiablePropertyBinding(IValueController<T> controller, INotifiableProperty<T> property, bool syncWhileModifying)
        {
            void SyncPropertyValue() => property.Value = controller.Value;

            if (syncWhileModifying)
                controller.ValueChanged.Subscribe(SyncPropertyValue);

            controller.ValueCommited.Subscribe(SyncPropertyValue);
            property.Modified.Subscribe(() => controller.Display(property.Value));
        }

        public void Dispose()
        {
            s.DisposeAll();
        }

        readonly DisposableManager s = new();
    }
}
