using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;

namespace TuneLab.Data;

internal interface IDuration
{
    IActionEvent DurationChanged { get; }
    double Duration { get; }
}
