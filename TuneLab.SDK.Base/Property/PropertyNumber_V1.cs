using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base;

public class PropertyNumber_V1 : IPropertyValue_V1
{
    public static implicit operator sbyte(PropertyNumber_V1 property) => (sbyte)property.mValue;
    public static implicit operator byte(PropertyNumber_V1 property) => (byte)property.mValue;
    public static implicit operator short(PropertyNumber_V1 property) => (short)property.mValue;
    public static implicit operator ushort(PropertyNumber_V1 property) => (ushort)property.mValue;
    public static implicit operator int(PropertyNumber_V1 property) => (int)property.mValue;
    public static implicit operator uint(PropertyNumber_V1 property) => (uint)property.mValue;
    public static implicit operator long(PropertyNumber_V1 property) => (long)property.mValue;
    public static implicit operator ulong(PropertyNumber_V1 property) => (ulong)property.mValue;
    public static implicit operator nint(PropertyNumber_V1 property) => (nint)property.mValue;
    public static implicit operator nuint(PropertyNumber_V1 property) => (nuint)property.mValue;
    public static implicit operator float(PropertyNumber_V1 property) => (float)property.mValue;
    public static implicit operator double(PropertyNumber_V1 property) => (double)property.mValue;
    public static implicit operator decimal(PropertyNumber_V1 property) => property.mValue;

    public static implicit operator PropertyNumber_V1(sbyte value) => new PropertyNumber_V1(value);
    public static implicit operator PropertyNumber_V1(byte value) => new PropertyNumber_V1(value);
    public static implicit operator PropertyNumber_V1(short value) => new PropertyNumber_V1(value);
    public static implicit operator PropertyNumber_V1(ushort value) => new PropertyNumber_V1(value);
    public static implicit operator PropertyNumber_V1(int value) => new PropertyNumber_V1(value);
    public static implicit operator PropertyNumber_V1(uint value) => new PropertyNumber_V1(value);
    public static implicit operator PropertyNumber_V1(long value) => new PropertyNumber_V1(value);
    public static implicit operator PropertyNumber_V1(ulong value) => new PropertyNumber_V1(value);
    public static implicit operator PropertyNumber_V1(nint value) => new PropertyNumber_V1(value);
    public static implicit operator PropertyNumber_V1(nuint value) => new PropertyNumber_V1(value);
    public static implicit operator PropertyNumber_V1(float value) => new PropertyNumber_V1((decimal)value);
    public static implicit operator PropertyNumber_V1(double value) => new PropertyNumber_V1((decimal)value);
    public static implicit operator PropertyNumber_V1(decimal value) => new PropertyNumber_V1(value);

    public PropertyNumber_V1(decimal number) { mValue = number; }

    readonly decimal mValue;
}
