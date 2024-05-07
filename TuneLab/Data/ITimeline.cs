using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Data;

internal interface ITimeline
{
    ITempoManager TempoManager { get; }
    ITimeSignatureManager TimeSignatureManager { get; }
}
