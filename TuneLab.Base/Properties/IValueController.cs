using TuneLab.Base.Event;
using TuneLab.Base.Utils;

namespace TuneLab.Base.Properties;

public interface IValueController<T>
{
    IActionEvent ValueChanged { get; }
    IActionEvent ValueCommited { get; }
    T Value { get; }
    void Display(T value);
    void DisplayNull() { }
    void DisplayMultiple() { }
}

public static class IValueControllerExtension
{
    public static IValueController<U> Select<T, U>(this IValueController<T> valueController, Func<T, U> to, Func<U, T> from)
    {
        return new SelectController<T, U>(valueController, to, from);
    }

    public class SelectController<T, U>(IValueController<T> valueController, Func<T, U> to, Func<U, T> from) : IValueController<U>
    {
        public IActionEvent ValueChanged => valueController.ValueChanged;

        public IActionEvent ValueCommited => valueController.ValueCommited;

        public U Value => to(valueController.Value);

        public void Display(U value)
        {
            valueController.Display(from(value));
        }

        public void DisplayNull()
        {
            valueController.DisplayNull();
        }

        public void DisplayMultiple()
        {
            valueController.DisplayMultiple();
        }
    }

    public static IValueController<T> Select<T>(this IValueController<T> valueController, Func<T, T> to)
    {
        return new SelectController<T, T>(valueController, to, x => x);
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
            controller.Display(property.Value);
        }

        public void Dispose()
        {
            s.DisposeAll();
        }

        readonly DisposableManager s = new();
    }
}