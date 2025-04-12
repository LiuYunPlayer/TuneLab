using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Extensions.Formats.DataInfo;

namespace TuneLab.Data;

internal interface ITimeSignature : IDataObject<TimeSignatureInfo>, IMeter
{
    int BarIndex { get; }
    int GlobalBeatIndex { get; }
    double Pos { get; }
}

internal static class ITimeSignatureExtension
{
    public static double GetTickByBarIndex(this ITimeSignature timeSignature, int barIndex)
    {
        return timeSignature.Pos + (barIndex - timeSignature.BarIndex) * timeSignature.TicksPerBar();
    }

    public static double GetTickByBarAndBeat(this ITimeSignature timeSignature, int barIndex, int beatIndex)
    {
        return timeSignature.GetTickByBarIndex(barIndex) + beatIndex * timeSignature.TicksPerBeat();
    }
}
