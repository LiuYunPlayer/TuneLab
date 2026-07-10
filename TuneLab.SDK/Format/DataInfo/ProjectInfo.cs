using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.SDK;

public class ProjectInfo
{
    public EditorInfo EditorInfo { get; set; } = new();
    public List<TempoInfo> Tempos { get; set; } = new();
    public List<TimeSignatureInfo> TimeSignatures { get; set; } = new();
    public List<TrackInfo> Tracks { get; set; } = new();
    public ExportConfigInfo ExportConfig { get; set; } = new();
}

public class ExportConfigInfo
{
    public string ExportPath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Format { get; set; } = "wav"; // wav | mp3 | flac | ogg
    public int SampleRate { get; set; } = 44100;
    public int BitDepth { get; set; } = 16;   // 无损格式(wav/flac)位深
    public int Bitrate { get; set; } = 320;   // 有损格式(mp3/ogg)目标码率 kbps
    public bool MasterExportEnabled { get; set; } = true;
    public int MasterExportChannels { get; set; } = 2;
}
