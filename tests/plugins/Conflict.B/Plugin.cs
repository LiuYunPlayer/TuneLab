using System.IO;
using TuneLab.SDK.Format;
using TuneLab.SDK.Format.DataInfo;

namespace TuneLab.TestPlugins.ConflictB;

// B 包应看到 ConflictHelper v2.0.0.0。扩展名 .tlconfb。
[ImportFormat("tlconfb")]
public sealed class ConflictBImport : IImportFormat
{
    public ProjectInfo Deserialize(Stream stream)
    {
        var version = ConflictHelper.Helper.Version;
        var project = new ProjectInfo();
        project.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });
        var track = new TrackInfo { Name = $"ConflictHelper v{version} (pkg B)" };
        var part = new MidiPartInfo { Name = "B", Pos = 0, Dur = 480 };
        part.Notes.Add(new NoteInfo { Pos = 0, Dur = 480, Pitch = 72, Lyric = version });
        track.Parts.Add(part);
        project.Tracks.Add(track);
        return project;
    }
}
