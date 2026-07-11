using System.IO;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.NoAssemblies;

// 负向用例：manifest 故意漏 assembly，加载器应优雅 Failed（本类不会被注册）。扩展名 .tlnoasm。
public sealed class NoAssembliesImport : IImportFormat
{
    public ProjectInfo Deserialize(Stream stream)
    {
        var project = new ProjectInfo();
        project.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });
        var track = new TrackInfo { Name = "scanned (no assemblies declared)" };
        var part = new MidiPartInfo { Name = "scan", Pos = 0, EndOffset = 480 };
        part.Notes.Add(new NoteInfo { Pos = 0, Dur = 480, Pitch = 62, Lyric = "scan" });
        track.Parts.Add(part);
        project.Tracks.Add(track);
        return project;
    }
}
