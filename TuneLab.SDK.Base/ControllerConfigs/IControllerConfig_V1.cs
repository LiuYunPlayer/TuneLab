namespace TuneLab.SDK.Base;

public interface IControllerConfig_V1
{
    PropertyValue_V1 DefaultValue { get; }
}

public static class IControllerConfig_V1Extensions
{
    public static IExpression_V1<bool> When(this IControllerConfig_V1 controllerConfig, Func<PropertyValue_V1, bool> condition)
    {
        return new ControllerConfigExpression(controllerConfig).Excute(condition);
    }

    public static IExpression_V1<T?> Excute<T>(this IControllerConfig_V1 controllerConfig, Func<PropertyValue_V1, T?> function)
    {
        return new ControllerConfigExpression(controllerConfig).Excute(function);
    }

    class ControllerConfigExpression : IExpression_V1<PropertyValue_V1>
    {
        public PropertyValue_V1 Result { get; private set; }

        public event Action? ResultChanged;

        public ControllerConfigExpression(IControllerConfig_V1 controllerConfig)
        {
            Result = controllerConfig.DefaultValue;

            if (mValueChangedActions.TryGetValue(controllerConfig, out var action))
                action += OnValueChanged;
            else
                mValueChangedActions.Add(controllerConfig, OnValueChanged);
        }

        void OnValueChanged(PropertyValue_V1 value)
        {
            if (value == Result)
                return;

            Result = value;
            ResultChanged?.Invoke();
        }
    }

    public static void InvokeValueChanged(this IControllerConfig_V1 controllerConfig, PropertyValue_V1 value)
    {
        mValueChangedActions.GetValueOrDefault(controllerConfig)?.Invoke(value);
    }

    static readonly Dictionary<IControllerConfig_V1, Action<PropertyValue_V1>> mValueChangedActions = [];
}
