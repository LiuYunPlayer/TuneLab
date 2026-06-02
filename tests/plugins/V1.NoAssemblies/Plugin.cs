using System.IO;
using TuneLab.SDK.Format;
using TuneLab.SDK.Format.DataInfo;

namespace TuneLab.TestPlugins.NoAssemblies;

// description 无 assemblies → 被扫描发现。扩展名 .tlnoasm。
[ImportFormat("tlnoasm")]
public sealed class NoAssembliesImport : IImportFormat
{
    public ProjectInfo Deserialize(Stream stream)
    {
        var project = new ProjectInfo();
        project.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });
        var track = new TrackInfo { Name = "scanned (no assemblies declared)" };
        var part = new MidiPartInfo { Name = "scan", Pos = 0, Dur = 480 };
        part.Notes.Add(new NoteInfo { Pos = 0, Dur = 480, Pitch = 62, Lyric = "scan" });
        track.Parts.Add(part);
        project.Tracks.Add(track);
        return project;
    }
}
