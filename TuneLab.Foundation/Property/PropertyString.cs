namespace TuneLab.Foundation;

public class PropertyString : IPrimitiveValue
{
    public static implicit operator string(PropertyString property) => property.mValue ?? string.Empty;

    public static implicit operator PropertyString(string value) => new(value);

    public PropertyString(string value) { mValue = value; }

    public override string ToString() => mValue ?? string.Empty;

    bool IEquatable<IPropertyValue>.Equals(IPropertyValue? other) => other is PropertyString property && property.mValue == mValue;

    readonly string? mValue;
}
