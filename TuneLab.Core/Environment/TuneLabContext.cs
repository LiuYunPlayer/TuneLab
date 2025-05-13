using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Core.Environment;

public static class TuneLabContext
{
#nullable disable
    public static ITuneLabContext Global { get; internal set; }
#nullable enable
}
