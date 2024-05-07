using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Extensions.Formats.DataInfo;

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

    void IDataObject<TempoInfo>.SetInfo(TempoInfo info)
    {
        IDataObject<TempoInfo>.SetInfo(Pos, info.Pos);
        IDataObject<TempoInfo>.SetInfo(Bpm, info.Bpm);
    }
}
