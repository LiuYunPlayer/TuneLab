using System.IO;
using System.Text;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.RouteConflictA;

// 冲突消解夹具 A：扩展名 .tlroute（与包 B 同身份、不同包 id）。
// 导入产出固定样例工程，轨名标注「Package A」——活实现是哪个包，导入后轨名即见分晓。
public sealed class RouteImport : IImportFormat
{
    public ProjectInfo Deserialize(Stream stream)
    {
        var project = new ProjectInfo();
        project.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });
        var track = new TrackInfo { Name = "Route Conflict — Imported by Package A" };
        var part = new MidiPartInfo { Name = "A", Pos = 0, EndOffset = 480 };
        part.Notes.Add(new NoteInfo { Pos = 0, Dur = 480, Pitch = 60, Lyric = "A" });
        track.Parts.Add(part);
        project.Tracks.Add(track);
        return project;
    }
}

// 导出写一行标记 exportedBy=A——活导出实现是哪个包，导出文件内容即见分晓（验证 import/export 可各选不同包）。
public sealed class RouteExport : IExportFormat
{
    public void Serialize(Stream output, ProjectInfo info)
    {
        var bytes = Encoding.UTF8.GetBytes("exportedBy=A\n");
        output.Write(bytes, 0, bytes.Length);
    }
}
