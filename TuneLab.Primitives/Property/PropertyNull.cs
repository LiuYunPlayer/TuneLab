namespace TuneLab.Primitives.Property;

// 空值哨兵：替代旧 PropertyValue.Invalid 的裸 object 哨兵，给 null 一个有名身份。
public sealed class PropertyNull
{
    public static readonly PropertyNull Shared = new();

    PropertyNull() { }

    public override string ToString() => "null";
}
