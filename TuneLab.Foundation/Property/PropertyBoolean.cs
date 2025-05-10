namespace TuneLab.Foundation.Property;

public class PropertyBoolean : IPropertyBoolean
{
    public bool Value => mValue;

    public static implicit operator bool(PropertyBoolean property) => property.mValue;

    public static implicit operator PropertyBoolean(bool value) => new(value);

    public PropertyBoolean(bool value) { mValue = value; }

    public override string ToString() => mValue.ToString();

    bool IEquatable<IPrimitiveValue>.Equals(IPrimitiveValue? other) => other != null && other.ToBoolean(out var value) && value == mValue;

    readonly bool mValue;
}
