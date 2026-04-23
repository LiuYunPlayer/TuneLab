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
    public ExportConfigInfo ExportConfig { get; set; } = new();
}

public class ExportConfigInfo
{
    public string ExportPath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int SampleRate { get; set; } = 44100;
    public int BitDepth { get; set; } = 16;
    public bool MasterExportEnabled { get; set; } = true;
    public int MasterExportChannels { get; set; } = 2;
}
