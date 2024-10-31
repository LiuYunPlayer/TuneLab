using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK.Format.DataInfo;

public class ProjectInfo_V1
{
    public List<TempoInfo_V1> Tempos { get; set; } = [];
    public List<TimeSignatureInfo_V1> TimeSignatures { get; set; } = [];
    public List<TrackInfo_V1> Tracks { get; set; } = [];
}
