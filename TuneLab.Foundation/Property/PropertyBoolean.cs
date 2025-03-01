namespace TuneLab.Foundation;

public class PropertyBoolean : IPrimitiveValue
{
    public static implicit operator bool(PropertyBoolean property) => property.mValue;

    public static implicit operator PropertyBoolean(bool value) => new(value);

    public PropertyBoolean(bool value) { mValue = value; }

    public override string ToString() => mValue.ToString();

    bool IEquatable<IPropertyValue>.Equals(IPropertyValue? other) => other is PropertyBoolean property && property.mValue == mValue;

    readonly bool mValue;
}
