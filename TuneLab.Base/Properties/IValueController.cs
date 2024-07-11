using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.Base.Utils;

namespace TuneLab.Base.Properties;

public interface IValueController<T>
{
    IActionEvent ValueChanged { get; }
    IActionEvent ValueCommited { get; }
    T Value { get; }
    void Display(T value);
}

public static class IValueControllerExtension
{
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