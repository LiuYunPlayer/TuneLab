using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Extensions.Formats.DataInfo;

namespace TuneLab.Data;

internal interface IPhoneme : IDataObject<PhonemeInfo>
{
    IDataProperty<double> StartTime { get; }
    IDataProperty<double> EndTime { get; }
    IDataProperty<string> Symbol { get; }
}
