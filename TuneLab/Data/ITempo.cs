using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK.Format.DataInfo;

namespace TuneLab.Data;

internal interface ITempo : IDataObject<TempoInfo>
{
    double Pos { get; }
    double Bpm { get; }
}
