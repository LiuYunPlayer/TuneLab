using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Extensions.Formats.DataInfo;

public class ProjectInfo
{
    public List<TempoInfo> Tempos { get; set; } = new();
    public List<TimeSignatureInfo> TimeSignatures { get; set; } = new();
    public List<TrackInfo> Tracks { get; set; } = new();
}
