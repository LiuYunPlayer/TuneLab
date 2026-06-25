using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Data;

internal class Phoneme : DataObject, IPhoneme
{
    // 标称时长（辅音固定 / 元音为派生填充、布局忽略此值）。位置由布局派生不存（元音起点对齐 note 起点、前辅音往左累积）。
    public DataStruct<double> Duration { get; } = new();
    public DataString Symbol { get; } = new();
    // 弹性伸缩权重：0 = 刚性辅音 / >0 = 可伸元音（吸收伸缩压缩）。
    public DataStruct<double> StretchWeight { get; } = new();
    // 是否前置音素（音节核之前的引导辅音）。
    public DataStruct<bool> IsLead { get; } = new();

    IDataProperty<double> IPhoneme.Duration => Duration;
    IDataProperty<string> IPhoneme.Symbol => Symbol;
    IDataProperty<double> IPhoneme.StretchWeight => StretchWeight;
    IDataProperty<bool> IPhoneme.IsLead => IsLead;

    public Phoneme()
    {
        Duration.Attach(this);
        Symbol.Attach(this);
        StretchWeight.Attach(this);
        IsLead.Attach(this);
    }

    public PhonemeInfo GetInfo()
    {
        return new PhonemeInfo()
        {
            Duration = Duration,
            Symbol = Symbol,
            StretchWeight = StretchWeight,
            IsLead = IsLead
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
        Duration.SetInfo(info.Duration);
        Symbol.SetInfo(info.Symbol);
        StretchWeight.SetInfo(info.StretchWeight);
        IsLead.SetInfo(info.IsLead);
    }
}
