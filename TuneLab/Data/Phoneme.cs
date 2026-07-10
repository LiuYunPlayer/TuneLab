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

    IDataProperty<double> IPhoneme.Duration => Duration;
    IDataProperty<string> IPhoneme.Symbol => Symbol;
    IDataProperty<double> IPhoneme.StretchWeight => StretchWeight;

    // per-phoneme 引擎自定义属性：lazy 物化（未编辑零开销）。空容器 ≡ 无属性，故只读消费走 HasProperties 闸门。
    DataPropertyObject? mProperties;
    public bool HasProperties => mProperties != null && mProperties.Count > 0;
    public DataPropertyObject Properties => mProperties ??= new DataPropertyObject(this);

    public Phoneme()
    {
        Duration.Attach(this);
        Symbol.Attach(this);
        StretchWeight.Attach(this);
    }

    public PhonemeInfo GetInfo()
    {
        return new PhonemeInfo()
        {
            Duration = Duration,
            Symbol = Symbol,
            StretchWeight = StretchWeight,
            // 空属性省略（pay-as-you-go）：未编辑过的音素不写 Properties。
            Properties = HasProperties ? mProperties!.GetInfo() : null,
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
        // 仅在有属性时物化 + 灌入；无则保持 lazy 未物化（零开销）。
        if (info.Properties != null && info.Properties.Map.Count > 0)
            Properties.SetInfo(info.Properties);
    }
}
