namespace TuneLab.Primitives.Property;

// 多值哨兵：多选编辑时各对象同一字段的值不完全相等的聚合态。
// 与 PropertyNull（无值 / 无选中）并列，二者同属"非具体值"的认知态：
// 瞬态、永不序列化；ToBool/ToDouble/ToString 等取值一律失败（安全降级），
// 关心的代码用 PropertyValue.IsMultiple() 判别。
public sealed class PropertyMultiple
{
    public static readonly PropertyMultiple Shared = new();

    PropertyMultiple() { }

    public override string ToString() => "multiple";
}
