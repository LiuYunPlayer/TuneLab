using System;
using System.Collections.Generic;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;
using TuneLab.Foundation.Utils;

namespace TuneLab.Extensions.ControllerConfigs;

public struct ComboBoxOption(PrimitiveValue value, string? displayText = null)
{
    public static implicit operator ComboBoxOption(PropertyBoolean value) => new() { Value = value };
    public static implicit operator ComboBoxOption(PropertyNumber value) => new() { Value = value };
    public static implicit operator ComboBoxOption(PropertyString value) => new() { Value = value };

    public static implicit operator ComboBoxOption(bool value) => new() { Value = value };

    public static implicit operator ComboBoxOption(sbyte value) => new() { Value = value };
    public static implicit operator ComboBoxOption(byte value) => new() { Value = value };
    public static implicit operator ComboBoxOption(short value) => new() { Value = value };
    public static implicit operator ComboBoxOption(ushort value) => new() { Value = value };
    public static implicit operator ComboBoxOption(int value) => new() { Value = value };
    public static implicit operator ComboBoxOption(uint value) => new() { Value = value };
    public static implicit operator ComboBoxOption(long value) => new() { Value = value };
    public static implicit operator ComboBoxOption(ulong value) => new() { Value = value };
    public static implicit operator ComboBoxOption(nint value) => new() { Value = value };
    public static implicit operator ComboBoxOption(nuint value) => new() { Value = value };
    public static implicit operator ComboBoxOption(float value) => new() { Value = value };
    public static implicit operator ComboBoxOption(double value) => new() { Value = value };
    public static implicit operator ComboBoxOption(decimal value) => new() { Value = value };

    public static implicit operator ComboBoxOption(string value) => new() { Value = value };

    public static implicit operator ComboBoxOption(PrimitiveValue value) => new() { Value = value };

    public PrimitiveValue Value { get; set; } = value;
    public string? DisplayText { get; set; } = displayText;
}

public class ComboBoxConfig(IReadOnlyList<ComboBoxOption> options, ComboBoxOption defaultValue) : IControllerConfig
{
    public ComboBoxOption DefaultOption { get; set; } = defaultValue;
    public IReadOnlyList<ComboBoxOption> Options { get; set; } = options;

    public ComboBoxConfig() : this(Array.Empty<ComboBoxOption>(), default) { }
    public ComboBoxConfig(IReadOnlyList<ComboBoxOption> options) : this(options, options.IsEmpty() ? default : options[0]) { }
    
    public ComboBoxConfig(IReadOnlyList<bool> options, ComboBoxOption defaultValue) : this(options.Convert(o => new ComboBoxOption(o)), defaultValue) { }
    public ComboBoxConfig(IReadOnlyList<bool> options) : this(options, options.IsEmpty() ? false : options[0]) { }

    public ComboBoxConfig(IReadOnlyList<sbyte> options, ComboBoxOption defaultValue) : this(options.Convert(o => new ComboBoxOption(o)), defaultValue) { }
    public ComboBoxConfig(IReadOnlyList<sbyte> options) : this(options, options.IsEmpty() ? default : options[0]) { }

    public ComboBoxConfig(IReadOnlyList<byte> options, ComboBoxOption defaultValue) : this(options.Convert(o => new ComboBoxOption(o)), defaultValue) { }
    public ComboBoxConfig(IReadOnlyList<byte> options) : this(options, options.IsEmpty() ? default : options[0]) { }

    public ComboBoxConfig(IReadOnlyList<short> options, ComboBoxOption defaultValue) : this(options.Convert(o => new ComboBoxOption(o)), defaultValue) { }
    public ComboBoxConfig(IReadOnlyList<short> options) : this(options, options.IsEmpty() ? default : options[0]) { }

    public ComboBoxConfig(IReadOnlyList<ushort> options, ComboBoxOption defaultValue) : this(options.Convert(o => new ComboBoxOption(o)), defaultValue) { }
    public ComboBoxConfig(IReadOnlyList<ushort> options) : this(options, options.IsEmpty() ? default : options[0]) { }

    public ComboBoxConfig(IReadOnlyList<int> options, ComboBoxOption defaultValue) : this(options.Convert(o => new ComboBoxOption(o)), defaultValue) { }
    public ComboBoxConfig(IReadOnlyList<int> options) : this(options, options.IsEmpty() ? default : options[0]) { }

    public ComboBoxConfig(IReadOnlyList<uint> options, ComboBoxOption defaultValue) : this(options.Convert(o => new ComboBoxOption(o)), defaultValue) { }
    public ComboBoxConfig(IReadOnlyList<uint> options) : this(options, options.IsEmpty() ? default : options[0]) { }

    public ComboBoxConfig(IReadOnlyList<long> options, ComboBoxOption defaultValue) : this(options.Convert(o => new ComboBoxOption(o)), defaultValue) { }
    public ComboBoxConfig(IReadOnlyList<long> options) : this(options, options.IsEmpty() ? default : options[0]) { }

    public ComboBoxConfig(IReadOnlyList<ulong> options, ComboBoxOption defaultValue) : this(options.Convert(o => new ComboBoxOption(o)), defaultValue) { }
    public ComboBoxConfig(IReadOnlyList<ulong> options) : this(options, options.IsEmpty() ? default : options[0]) { }

    public ComboBoxConfig(IReadOnlyList<nint> options, ComboBoxOption defaultValue) : this(options.Convert(o => new ComboBoxOption(o)), defaultValue) { }
    public ComboBoxConfig(IReadOnlyList<nint> options) : this(options, options.IsEmpty() ? default : options[0]) { }

    public ComboBoxConfig(IReadOnlyList<nuint> options, ComboBoxOption defaultValue) : this(options.Convert(o => new ComboBoxOption(o)), defaultValue) { }
    public ComboBoxConfig(IReadOnlyList<nuint> options) : this(options, options.IsEmpty() ? default : options[0]) { }

    public ComboBoxConfig(IReadOnlyList<float> options, ComboBoxOption defaultValue) : this(options.Convert(o => new ComboBoxOption(o)), defaultValue) { }
    public ComboBoxConfig(IReadOnlyList<float> options) : this(options, options.IsEmpty() ? default : options[0]) { }

    public ComboBoxConfig(IReadOnlyList<double> options, ComboBoxOption defaultValue) : this(options.Convert(o => new ComboBoxOption(o)), defaultValue) { }
    public ComboBoxConfig(IReadOnlyList<double> options) : this(options, options.IsEmpty() ? default : options[0]) { }

    public ComboBoxConfig(IReadOnlyList<decimal> options, ComboBoxOption defaultValue) : this(options.Convert(o => new ComboBoxOption(o)), defaultValue) { }
    public ComboBoxConfig(IReadOnlyList<decimal> options) : this(options, options.IsEmpty() ? default : options[0]) { }

    public ComboBoxConfig(IReadOnlyList<string> options, ComboBoxOption defaultValue) : this(options.Convert(o => new ComboBoxOption(o)), defaultValue) { }
    public ComboBoxConfig(IReadOnlyList<string> options) : this(options, options.IsEmpty() ? string.Empty : options[0]) { }
}
