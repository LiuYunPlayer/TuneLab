using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Data;

internal readonly struct MeterStatus
{
    public ITimeSignature TimeSignature => mTimeSignatureManager.TimeSignatures[mIndex];
    public int TimeSignatureIndex => mIndex;
    public double BarIndex => TimeSignature.BarIndex + (mPos - TimeSignature.Pos) / TimeSignature.TicksPerBar();
    public double GlobalBeatIndex => TimeSignature.GlobalBeatIndex + (mPos - TimeSignature.Pos) / TimeSignature.TicksPerBeat();
    public double BeatIndex => (mPos - TimeSignature.Pos) / TimeSignature.TicksPerBeat() / TimeSignature.Numerator;
    public MeterStatus(ITimeSignatureManager manager, int index, double pos)
    {
        mTimeSignatureManager = manager;
        mIndex = index;
        mPos = pos;
    }

    readonly ITimeSignatureManager mTimeSignatureManager;
    readonly int mIndex;
    readonly double mPos;
}
