namespace TuneLab.Foundation.Property;

public class PropertyBoolean : IPrimitiveValue
{
    public PropertyType Type => PropertyType.Boolean;

    public static implicit operator bool(PropertyBoolean property) => property.mValue;

    public static implicit operator PropertyBoolean(bool value) => new(value);

    public PropertyBoolean(bool value) { mValue = value; }

    public override string ToString() => mValue.ToString();

    //bool IEquatable<IPropertyValue>.Equals(IPropertyValue? other) => other is PropertyBoolean property && property.mValue == mValue;

    readonly bool mValue;
}
