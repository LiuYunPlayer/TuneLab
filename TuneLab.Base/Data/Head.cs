using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Base.Data;

public readonly struct Head(int head) : IEquatable<Head>
{
    public static bool operator ==(Head left, Head right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Head left, Head right)
    {
        return !left.Equals(right);
    }

    public bool Equals(Head other)
    {
        return mHead == other.mHead;
    }

    readonly int mHead = head;
}
