namespace TuneLab.Foundation.Property;

public interface IPrimitiveValue : IPropertyValue, IEquatable<IPrimitiveValue>
{

}

public interface IPrimitiveValue<T> : IPrimitiveValue
{
    T Value { get; }
}

public interface IPropertyBoolean : IPrimitiveValue<bool>
{
    PropertyType IReadOnlyPropertyValue.Type => PropertyType.Boolean;
}

public interface IPropertyNumber : IPrimitiveValue<double>
{
    PropertyType IReadOnlyPropertyValue.Type => PropertyType.Number;
}

public interface IPropertyString : IPrimitiveValue<string>
{
    PropertyType IReadOnlyPropertyValue.Type => PropertyType.String;
}
