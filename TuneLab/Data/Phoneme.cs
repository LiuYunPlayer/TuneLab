using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Extensions.Formats.DataInfo;

namespace TuneLab.Data;

internal class Phoneme : DataObject, IPhoneme
{
    public DataStruct<double> StartTime { get; } = new();
    public DataStruct<double> EndTime { get; } = new();
    public DataString Symbol { get; } = new();

    IDataProperty<double> IPhoneme.StartTime => StartTime;
    IDataProperty<double> IPhoneme.EndTime => EndTime;
    IDataProperty<string> IPhoneme.Symbol => Symbol;

    public Phoneme()
    { 
        StartTime.Attach(this);
        EndTime.Attach(this);
        Symbol.Attach(this);
    }

    public PhonemeInfo GetInfo()
    {
        return new PhonemeInfo()
        {
            StartTime = StartTime,
            EndTime = EndTime,
            Symbol = Symbol
        };
    }

    public static Phoneme Create(PhonemeInfo info)
    {
        var phoneme = new Phoneme();
        IDataObject<PhonemeInfo>.SetInfo(phoneme, info);
        return phoneme;
    }

    void IDataObject<PhonemeInfo>.SetInfo(PhonemeInfo info)
    {
        IDataObject<PhonemeInfo>.SetInfo(StartTime, info.StartTime);
        IDataObject<PhonemeInfo>.SetInfo(EndTime, info.EndTime);
        IDataObject<PhonemeInfo>.SetInfo(Symbol, info.Symbol);
    }
}
