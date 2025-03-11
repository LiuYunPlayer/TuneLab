namespace TuneLab.Foundation.Property;

public class PropertyString : IPrimitiveValue
{
    public PropertyType Type => PropertyType.String;

    public static implicit operator string(PropertyString property) => property.mValue ?? string.Empty;

    public static implicit operator PropertyString(string value) => new(value);

    public PropertyString(string value) { mValue = value; }

    public override string ToString() => mValue ?? string.Empty;

    //bool IEquatable<IPropertyValue>.Equals(IPropertyValue? other) => other is PropertyString property && property.mValue == mValue;

    readonly string? mValue;
}
