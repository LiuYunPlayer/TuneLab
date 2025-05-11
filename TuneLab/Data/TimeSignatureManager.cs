using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TuneLab.Core.DataInfo;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Utils;

namespace TuneLab.Data;

internal class TimeSignatureManager : DataObject, ITimeSignatureManager
{
    public IProject Project => mProject;
    public IReadOnlyList<ITimeSignature> TimeSignatures => mTimeSignatures;

    public const int DefaultNumerator = 4;
    public const int DefaultDenominator = 4;
    public TimeSignatureManager(IProject project) : this(project, DefaultNumerator, DefaultDenominator) { }
    public TimeSignatureManager(IProject project, int numerator, int denominator) : this(project, [new TimeSignatureInfo { BarIndex = 0, Numerator = numerator, Denominator = denominator }]) { }

    public TimeSignatureManager(IProject project, List<TimeSignatureInfo> timeSignatures) : base(project)
    {
        mTimeSignatures = new(this);
        mProject = project;
        IDataObject<List<TimeSignatureInfo>>.SetInfo(this, timeSignatures);
    }

    public int AddTimeSignature(int barIndex, int numerator, int denominator)
    {
        barIndex = Math.Max(barIndex, TimeSignatures[0].BarIndex);

        BeginMergeNotify();
        int i = mTimeSignatures.Count - 1;
        for (; i >= 0; --i)
        {
            if (mTimeSignatures[i].BarIndex <= barIndex)
                break;
        }

        int result;
        if (mTimeSignatures[i].BarIndex == barIndex)
        {
            var timeSignature = mTimeSignatures[i];
            timeSignature.Numerator.Set(numerator);
            timeSignature.Denominator.Set(denominator);
            result = i;
        }
        else
        {
            mTimeSignatures.Insert(i + 1, new TimeSignature(new() { BarIndex = barIndex, Numerator = numerator, Denominator = denominator }));
            result = i + 1;
        }

        CorrectStatusFrom(i + 1);
        EndMergeNotify();
        return result;
    }

    public void RemoveTimeSignatureAt(int index)
    {
        if (index < 0 || index >= mTimeSignatures.Count)
            return;

        BeginMergeNotify();
        mTimeSignatures.RemoveAt(index);
        CorrectStatusFrom(index);
        EndMergeNotify();
    }

    public void SetMeter(int index, int numerator, int denominator)
    {
        if (index < 0 || index >= TimeSignatures.Count)
            return;

        BeginMergeNotify();
        mTimeSignatures[index].Numerator.Set(numerator);
        mTimeSignatures[index].Denominator.Set(denominator);
        CorrectStatusFrom(index + 1);
        EndMergeNotify();
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

    public List<TimeSignatureInfo> GetInfo()
    {
        return mTimeSignatures.GetInfo().ToInfo();
    }

    void IDataObject<List<TimeSignatureInfo>>.SetInfo(List<TimeSignatureInfo> info)
    {
        if (info.Count == 0)
            info = [new() { BarIndex = 0, Numerator = DefaultNumerator, Denominator = DefaultDenominator }];

        IDataObject<List<TimeSignatureInfo>>.SetInfo(mTimeSignatures, info.Convert(t => new TimeSignature(t)).ToArray());
        CorrectStatusFrom(0);
    }

    readonly DataObjectList<TimeSignature> mTimeSignatures;
    readonly IProject mProject;

    class TimeSignature : DataObject, ITimeSignature
    {
        public DataStruct<int> BarIndex { get; }
        public DataStruct<int> Numerator { get; }
        public DataStruct<int> Denominator { get; }
        public int GlobalBeatIndex { get => mBeatIndex; set => mBeatIndex = value; }
        public double Pos { get => mPos; set => mPos = value; }

        int ITimeSignature.BarIndex => BarIndex;
        int IMeter.Numerator => Numerator;
        int IMeter.Denominator => Denominator;

        public TimeSignature(TimeSignatureInfo info)
        {
            BarIndex = new(this);
            Numerator = new(this);
            Denominator = new(this);
            IDataObject<TimeSignatureInfo>.SetInfo(this, info);
        }

        public TimeSignatureInfo GetInfo() => new() { BarIndex = BarIndex.Value, Numerator = Numerator.Value, Denominator = Denominator.Value };

        void IDataObject<TimeSignatureInfo>.SetInfo(TimeSignatureInfo info)
        {
            IDataObject<TimeSignatureInfo>.SetInfo(BarIndex, info.BarIndex);
            IDataObject<TimeSignatureInfo>.SetInfo(Numerator, info.Numerator);
            IDataObject<TimeSignatureInfo>.SetInfo(Denominator, info.Denominator);
        }

        int mBeatIndex;
        double mPos;
    }
}
