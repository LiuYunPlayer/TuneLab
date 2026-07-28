using System.IO;
using System.Text;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1MultiSuffix;

// 同一格式的两个后缀别名（模拟 .mid/.midi）：manifest 里是【一个 format 条目 + suffixes: ["mtest","mtst"]】，
// 两个后缀共用这一个实现类与同一份 introduction。
//
// 两处要点由本插件验证：
//  1. 声明的单位是【格式】而非后缀：一份 name/summary/introduction/实现类覆盖全部别名，详情窗只有一个 tab，
//     不需要"多条目指同一份文档就合并"那种补救（那样两条目 name 不同时无从取舍）。
//  2. 注册与路由仍【逐后缀】：设置窗「Extension Routing」里 .mtest 与 .mtst 各有自己的 Import/Export 行，
//     用户可以让别的包接管其中一个——宿主开文件本就按后缀选实现，这个粒度没被收窄。
public sealed class MultiSuffixFormat : IImportFormat, IExportFormat
{
    // 导入：不解析内容，直接给一个固定的可见样例工程（手测时一定能看到 note）。
    public ProjectInfo Deserialize(Stream stream)
    {
        var project = new ProjectInfo();
        project.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });

        var track = new TrackInfo { Name = "Multi Suffix Track" };
        var part = new MidiPartInfo { Name = "Multi Suffix Part", Pos = 0, EndOffset = 960 };
        part.Notes.Add(new NoteInfo { Pos = 0, Dur = 480, Pitch = 60, Lyric = "do" });
        part.Notes.Add(new NoteInfo { Pos = 480, Dur = 480, Pitch = 67, Lyric = "so" });
        track.Parts.Add(part);
        project.Tracks.Add(track);
        return project;
    }

    // 导出：写一行纯文本摘要即可——本插件测的是元数据与详情窗，不是序列化保真。
    public void Serialize(Stream output, ProjectInfo info)
    {
        var text = new StringBuilder();
        text.Append("multi-suffix test export; tracks=").Append(info.Tracks.Count).Append('\n');
        var bytes = Encoding.UTF8.GetBytes(text.ToString());
        output.Write(bytes, 0, bytes.Length);
    }
}
