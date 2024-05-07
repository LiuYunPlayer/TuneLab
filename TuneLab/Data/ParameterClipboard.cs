using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Structures;
using TuneLab.Base.Utils;

namespace TuneLab.Data;

internal class ParameterClipboard
{
    public bool IsEmpty => Pitch.IsEmpty() && Automations.Count == 0;
    public required List<List<Point>> Pitch { get; set; }
    public required Map<string, List<Point>> Automations { get; set; }
}
