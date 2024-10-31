using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Base.DataStructures;

public readonly struct PropertyNumber_V1
{
    public static implicit operator sbyte(PropertyNumber_V1 number) => (sbyte)number.mValue;
    public static implicit operator byte(PropertyNumber_V1 number) => (byte)number.mValue;
    public static implicit operator short(PropertyNumber_V1 number) => (short)number.mValue;
    public static implicit operator ushort(PropertyNumber_V1 number) => (ushort)number.mValue;
    public static implicit operator int(PropertyNumber_V1 number) => (int)number.mValue;
    public static implicit operator uint(PropertyNumber_V1 number) => (uint)number.mValue;
    public static implicit operator long(PropertyNumber_V1 number) => (long)number.mValue;
    public static implicit operator ulong(PropertyNumber_V1 number) => (ulong)number.mValue;
    public static implicit operator nint(PropertyNumber_V1 number) => (nint)number.mValue;
    public static implicit operator nuint(PropertyNumber_V1 number) => (nuint)number.mValue;
    public static implicit operator float(PropertyNumber_V1 number) => (float)number.mValue;
    public static implicit operator double(PropertyNumber_V1 number) => (double)number.mValue;
    public static implicit operator decimal(PropertyNumber_V1 number) => number.mValue;

    public static implicit operator PropertyNumber_V1(sbyte number) => new PropertyNumber_V1(number);
    public static implicit operator PropertyNumber_V1(byte number) => new PropertyNumber_V1(number);
    public static implicit operator PropertyNumber_V1(short number) => new PropertyNumber_V1(number);
    public static implicit operator PropertyNumber_V1(ushort number) => new PropertyNumber_V1(number);
    public static implicit operator PropertyNumber_V1(int number) => new PropertyNumber_V1(number);
    public static implicit operator PropertyNumber_V1(uint number) => new PropertyNumber_V1(number);
    public static implicit operator PropertyNumber_V1(long number) => new PropertyNumber_V1(number);
    public static implicit operator PropertyNumber_V1(ulong number) => new PropertyNumber_V1(number);
    public static implicit operator PropertyNumber_V1(nint number) => new PropertyNumber_V1(number);
    public static implicit operator PropertyNumber_V1(nuint number) => new PropertyNumber_V1(number);
    public static implicit operator PropertyNumber_V1(float number) => new PropertyNumber_V1((decimal)number);
    public static implicit operator PropertyNumber_V1(double number) => new PropertyNumber_V1((decimal)number);
    public static implicit operator PropertyNumber_V1(decimal number) => new PropertyNumber_V1(number);

    public PropertyNumber_V1(decimal number) { mValue = number; }

    readonly decimal mValue = 0;
}
