using System.Collections.Generic;
using TuneLab.Foundation.Property;

namespace TuneLab.Extensions.ControllerConfigs;

public struct ComboBoxItemConfig
{
    public static implicit operator ComboBoxItemConfig(PropertyBoolean value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig(PropertyNumber value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig(PropertyString value) => new() { Value = value };

    public static implicit operator ComboBoxItemConfig(bool value) => new() { Value = value };

    public static implicit operator ComboBoxItemConfig(sbyte value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig(byte value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig(short value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig(ushort value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig(int value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig(uint value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig(long value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig(ulong value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig(nint value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig(nuint value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig(float value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig(double value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig(decimal value) => new() { Value = value };

    public static implicit operator ComboBoxItemConfig(string value) => new() { Value = value };

    public required ReadOnlyPrimitiveValue Value { get; set; }
    public string? Text { get; set; }
}

public class ComboBoxConfig : IControllerConfig
{
    public ReadOnlyPrimitiveValue DefaultValue { get; set; }
    public required IReadOnlyList<ComboBoxItemConfig> Items { get; set; }
}
