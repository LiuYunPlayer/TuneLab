using System.IO;
using TuneLab.SDK.Format;
using TuneLab.SDK.Format.DataInfo;

namespace TuneLab.TestPlugins.ConflictA;

// 导入即产出一条以所见 ConflictHelper 版本命名的轨/note。A 包应看到 v1.0.0.0。扩展名 .tlconfa。
[ImportFormat("tlconfa")]
public sealed class ConflictAImport : IImportFormat
{
    public ProjectInfo Deserialize(Stream stream)
    {
        var version = ConflictHelper.Helper.Version;
        var project = new ProjectInfo();
        project.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });
        var track = new TrackInfo { Name = $"ConflictHelper v{version} (pkg A)" };
        var part = new MidiPartInfo { Name = "A", Pos = 0, Dur = 480 };
        part.Notes.Add(new NoteInfo { Pos = 0, Dur = 480, Pitch = 60, Lyric = version });
        track.Parts.Add(part);
        project.Tracks.Add(track);
        return project;
    }
}
