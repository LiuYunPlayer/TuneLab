using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Utils;
using TuneLab.Extensions.Formats.DataInfo;

namespace TuneLab.Data;

internal class TimeSignatureManager : ITimeSignatureManager
{
    public IReadOnlyList<ITimeSignature> TimeSignatures => mTimeSignatures;

    public const int DefaultNumerator = 4;
    public const int DefaultDenominator = 4;
    public TimeSignatureManager() : this(DefaultNumerator, DefaultDenominator) { }
    public TimeSignatureManager(int numerator, int denominator) : this(new TimeSignatureInfo[] { new TimeSignatureInfo { BarIndex = 0, Numerator = numerator, Denominator = denominator } }) { }

    public TimeSignatureManager(IReadOnlyList<TimeSignatureInfo> timeSignatures)
    {
        if (timeSignatures == null || timeSignatures.Count == 0)
            timeSignatures = new TimeSignatureInfo[] { new TimeSignatureInfo { BarIndex = 0, Numerator = DefaultNumerator, Denominator = DefaultDenominator } };

        foreach (var timeSignature in timeSignatures)
        {
            mTimeSignatures.Add(new TimeSignature(timeSignature.BarIndex, timeSignature.Numerator, timeSignature.Denominator));
        }
        CorrectStatusFrom(0);
    }

    public void AddTimeSignature(int barIndex, int numerator, int denominator)
    {
        int i = mTimeSignatures.Count - 1;
        for (; i >= 0; --i)
        {
            if (mTimeSignatures[i].BarIndex <= barIndex)
                break;
        }

        if (mTimeSignatures[i].BarIndex == barIndex)
            mTimeSignatures[i] = new TimeSignature(barIndex, numerator, denominator) { GlobalBeatIndex = mTimeSignatures[i].GlobalBeatIndex, Pos = mTimeSignatures[i].Pos };
        else
            mTimeSignatures.Insert(i + 1, new TimeSignature(barIndex, numerator, denominator));

        CorrectStatusFrom(i + 1);
    }

    public void RemoveTimeSignatureAt(int index)
    {
        if (index < 0 || index >= mTimeSignatures.Count)
            return;

        mTimeSignatures.RemoveAt(index);
        CorrectStatusFrom(index);
    }

    public void SetNumeratorAndDenominator(int index, int numerator, int denominator)
    {
        if (index < 0 || index >= TimeSignatures.Count)
            return;

        mTimeSignatures[index].Numerator = numerator;
        mTimeSignatures[index].Denominator = denominator;
        CorrectStatusFrom(index + 1);
    }

    void CorrectStatusFrom(int index)
    {
        if (index <= 0)
        {
            mTimeSignatures[0].GlobalBeatIndex = 0;
            mTimeSignatures[0].Pos = 0;
            CorrectStatusFrom(1);
            return;
        }

        int n = mTimeSignatures.Count;
        for (int i = index; i < n; i++)
        {
            var last = mTimeSignatures[i - 1];
            var next = mTimeSignatures[i];

            int beatCount = (next.BarIndex - last.BarIndex) * last.Numerator;
            mTimeSignatures[i].GlobalBeatIndex = last.GlobalBeatIndex + beatCount;
            mTimeSignatures[i].Pos = last.Pos + beatCount * last.TicksPerBeat();
        }
    }

    readonly List<TimeSignature> mTimeSignatures = new();
    class TimeSignature : ITimeSignature
    {
        public int BarIndex { get => mBarIndex; set => mBarIndex = value; }
        public int Numerator { get => mNumerator; set => mNumerator = value; }
        public int Denominator { get => mDenominator; set => mDenominator = value; }
        public int GlobalBeatIndex { get => mBeatIndex; set => mBeatIndex = value; }
        public double Pos { get => mPos; set => mPos = value; }

        public TimeSignature(int barIndex, int numerator, int denominator)
        {
            mBarIndex = barIndex;
            mNumerator = numerator;
            mDenominator = denominator;
        }

        int mBarIndex;
        int mNumerator;
        int mDenominator;
        int mBeatIndex;
        double mPos;
    }
}
