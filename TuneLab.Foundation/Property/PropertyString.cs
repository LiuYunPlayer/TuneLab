namespace TuneLab.Foundation.Property;

public class PropertyString : IPropertyString
{
    public string Value => mValue ?? string.Empty;
    public PropertyType Type => PropertyType.String;

    public static implicit operator string(PropertyString property) => property.Value;

    public static implicit operator PropertyString(string value) => new(value);

    public PropertyString(string value) { mValue = value; }

    public override string ToString() => mValue ?? string.Empty;

    bool IEquatable<IPrimitiveValue>.Equals(IPrimitiveValue? other) => other != null && other.ToString(out var value) && value == mValue;

    readonly string? mValue;
}
