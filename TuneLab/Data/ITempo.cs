using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Extensions.Formats.DataInfo;

namespace TuneLab.Data;

internal interface ITempo : IDataObject<TempoInfo>
{
    double Pos { get; }
    double Bpm { get; }
}
