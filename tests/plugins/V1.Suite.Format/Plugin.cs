using System.IO;
using System.Text;
using TuneLab.SDK.Format;
using TuneLab.SDK.Format.DataInfo;
using TuneLab.TestPlugins.Suite.Common;

namespace TuneLab.TestPlugins.Suite.Format;

// 一包多插件之 format：import 产出固定样例（track 名取自共享 Common，证明基建被引用）。扩展名 .tlsuite。
[ImportFormat("tlsuite")]
public sealed class SuiteImportFormat : IImportFormat
{
    public ProjectInfo Deserialize(Stream stream)
    {
        var project = new ProjectInfo();
        project.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });
        var track = new TrackInfo { Name = SuiteCommon.Label("Format") };
        var part = new MidiPartInfo { Name = "suite", Pos = 0, Dur = 960 };
        part.Notes.Add(new NoteInfo { Pos = 0, Dur = 480, Pitch = 60, Lyric = "su" });
        part.Notes.Add(new NoteInfo { Pos = 480, Dur = 480, Pitch = 64, Lyric = "ite" });
        track.Parts.Add(part);
        project.Tracks.Add(track);
        return project;
    }
}

[ExportFormat("tlsuite")]
public sealed class SuiteExportFormat : IExportFormat
{
    public Stream Serialize(ProjectInfo info)
    {
        var sb = new StringBuilder();
        sb.AppendLine(SuiteCommon.Label("export"));
        foreach (var track in info.Tracks)
            sb.AppendLine($"track: {track.Name}");
        return new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString()));
    }
}
