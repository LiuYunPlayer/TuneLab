using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Science;

namespace TuneLab.Data;

internal interface IMeter
{
    int Numerator { get; }
    int Denominator { get; }
}

internal static class IMeterExtension
{
    public static int TicksPerBeat(this IMeter meter)
    {
        return MusicTheory.RESOLUTION * 4 / meter.Denominator;
    }

    public static int TicksPerBar(this IMeter meter)
    {
        return meter.TicksPerBeat() * meter.Numerator;
    }
}