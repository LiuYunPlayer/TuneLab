using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base;

public struct ComboBoxItemConfig_V1
{
    public static implicit operator ComboBoxItemConfig_V1(PropertyBoolean_V1 value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig_V1(PropertyNumber_V1 value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig_V1(PropertyString_V1 value) => new() { Value = value };

    public static implicit operator ComboBoxItemConfig_V1(bool value) => new() { Value = value };

    public static implicit operator ComboBoxItemConfig_V1(sbyte value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig_V1(byte value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig_V1(short value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig_V1(ushort value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig_V1(int value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig_V1(uint value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig_V1(long value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig_V1(ulong value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig_V1(nint value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig_V1(nuint value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig_V1(float value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig_V1(double value) => new() { Value = value };
    public static implicit operator ComboBoxItemConfig_V1(decimal value) => new() { Value = value };

    public static implicit operator ComboBoxItemConfig_V1(string value) => new() { Value = value };

    public required PrimitiveValue_V1 Value { get; set; }
    public string? Text { get; set; }
}

public class ComboBoxConfig_V1 : IControllerConfig_V1
{
    public PrimitiveValue_V1 DefaultValue { get; set; }
    public required IReadOnlyList<ComboBoxItemConfig_V1> Items { get; set; }

    PropertyValue_V1 IControllerConfig_V1.DefaultValue => DefaultValue;
}
