namespace TuneLab.SDK.Base;

public class PropertyBoolean_V1 : IPrimitiveValue_V1
{
    public static implicit operator bool(PropertyBoolean_V1 property) => property.mValue;

    public static implicit operator PropertyBoolean_V1(bool value) => new(value);

    public PropertyBoolean_V1(bool value) { mValue = value; }

    public override string ToString() => mValue.ToString();

    bool IEquatable<IPropertyValue_V1>.Equals(IPropertyValue_V1? other) => other is PropertyBoolean_V1 property && property.mValue == mValue;

    readonly bool mValue;
}
