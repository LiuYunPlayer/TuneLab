using System.Collections.Generic;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Utils;

namespace TuneLab.Data;

internal class ParameterClipboard
{
    public bool IsEmpty => Pitch.IsEmpty() && Automations.Count == 0;
    public required List<List<Point>> Pitch { get; set; }
    public required Map<string, List<Point>> Automations { get; set; }
}
