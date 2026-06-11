using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Document;
using TuneLab.SDK.Format.DataInfo;

namespace TuneLab.Data;

internal class Phoneme : DataObject, IPhoneme
{
    public DataStruct<double> StartTime { get; } = new();
    public DataStruct<double> EndTime { get; } = new();
    public DataString Symbol { get; } = new();
    // 伸缩权重：锁定时随时长一并从合成产物固定（用户意图的一部分）；
    // 宿主拖伸 note 分配长度变化用。旧工程缺省 0 → 公式 Σw≤0 退化均匀。
    public DataStruct<double> Weight { get; } = new();

    IDataProperty<double> IPhoneme.StartTime => StartTime;
    IDataProperty<double> IPhoneme.EndTime => EndTime;
    IDataProperty<string> IPhoneme.Symbol => Symbol;
    IDataProperty<double> IPhoneme.Weight => Weight;

    public Phoneme()
    {
        StartTime.Attach(this);
        EndTime.Attach(this);
        Symbol.Attach(this);
        Weight.Attach(this);
    }

    public PhonemeInfo GetInfo()
    {
        return new PhonemeInfo()
        {
            StartTime = StartTime,
            EndTime = EndTime,
            Symbol = Symbol,
            Weight = Weight
        };
    }

    public static Phoneme Create(PhonemeInfo info)
    {
        var phoneme = new Phoneme();
        phoneme.SetInfo(info);
        return phoneme;
    }

    public void SetInfo(PhonemeInfo info)
    {
        using var _ = MergeNotify();
        StartTime.SetInfo(info.StartTime);
        EndTime.SetInfo(info.EndTime);
        Symbol.SetInfo(info.Symbol);
        Weight.SetInfo(info.Weight);
    }
}
