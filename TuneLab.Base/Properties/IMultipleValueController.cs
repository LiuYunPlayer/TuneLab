using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Base.Event;
using TuneLab.Base.Utils;

namespace TuneLab.Base.Properties;

public interface IMultipleValueController<T> : IValueController<T>
{
    IActionEvent ValueWillChange { get; }
    void DisplayNull() { }
    void DisplayMultiple() { }
}

public static class IMultipleValueControllerExtension
{
    public static void Bind<T>(this IMultipleValueController<T> controller, IProvider<IDataProperty<T>> propertyProvider, DisposableManager? context = null) where T : notnull
    {
        var binding = new DataPropertyProviderBinding<T>(controller, propertyProvider);
        context?.Add(binding);
    }

    class DataPropertyProviderBinding<T> : IDisposable where T : notnull
    {
        public DataPropertyProviderBinding(IMultipleValueController<T> controller, IProvider<IDataProperty<T>> propertyProvider)
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

        readonly IMultipleValueController<T> mController;
        readonly IProvider<IDataProperty<T>> mPropertyProvider;
    }
}
