using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK.Format.DataInfo;

namespace TuneLab.Data;

internal abstract class Tempo(DataObject parent) : DataObject(parent), IDataObject<TempoInfo>
{
    public abstract DataProperty<double> Pos { get; }
    public abstract DataProperty<double> Bpm { get; }

    public TempoInfo GetInfo() => new()
    {
        Pos = Pos,
        Bpm = Bpm
    };

    public void SetInfo(TempoInfo info)
    {
        using var _ = MergeNotify();
        Pos.SetInfo(info.Pos);
        Bpm.SetInfo(info.Bpm);
    }
}
