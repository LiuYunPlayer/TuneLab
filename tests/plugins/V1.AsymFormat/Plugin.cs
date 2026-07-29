using System.IO;
using System.Text;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1AsymFormat;

// 方向不对称的【紧凑形态】：一个类同时实现两个接口，manifest 里是**一个** `type: "format"` 条目，
// 但用 `export-suffixes` 把写出侧收窄到 `.asymx`。
//
// 这是「读得宽、写得规范」这一格式常态的形状（.mid/.midi 都能读、只写一种）。它**不该**被写成
// format-import + format-export 两个条目——那会把同一份实现劈成两份说明、两份设置；后缀集不对称
// 不等于实现不是同一个。
//
// 预期：导入菜单里 .asym 与 .asymx 都在，导出菜单里只有 .asymx。
public sealed class AsymFormat : IImportFormat, IExportFormat
{
    // 导入：不解析内容，给一个固定的可见样例工程（手测时一定能看到 note）。
    public ProjectInfo Deserialize(Stream stream)
    {
        var project = new ProjectInfo();
        project.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });

        var track = new TrackInfo { Name = "Asym Track" };
        var part = new MidiPartInfo { Name = "Asym Part", Pos = 0, EndOffset = 960 };
        part.Notes.Add(new NoteInfo { Pos = 0, Dur = 480, Pitch = 62, Lyric = "re" });
        part.Notes.Add(new NoteInfo { Pos = 480, Dur = 480, Pitch = 69, Lyric = "la" });
        track.Parts.Add(part);
        project.Tracks.Add(track);
        return project;
    }

    // 导出：一行纯文本摘要即可——本插件测的是声明形态，不是序列化保真。
    public void Serialize(Stream output, ProjectInfo info)
    {
        var text = new StringBuilder();
        text.Append("asym test export; tracks=").Append(info.Tracks.Count).Append('\n');
        var bytes = Encoding.UTF8.GetBytes(text.ToString());
        output.Write(bytes, 0, bytes.Length);
    }
}
