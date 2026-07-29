using System.IO;
using System.Text;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1MultiSuffix;

// 同一格式的两个后缀别名（模拟 .mid/.midi）：manifest 里是【一个 format 条目 + suffixes: ["mtest","mtst"]】，
// 两个后缀共用这一个实现类与同一份 introduction。
//
// 三处要点由本插件验证：
//  1. 声明的单位是【格式】而非后缀：一份 name/summary/introduction/实现类覆盖全部别名，详情窗只有一个 tab，
//     不需要"多条目指同一份文档就合并"那种补救（那样两条目 name 不同时无从取舍）。
//  2. 注册与路由仍【逐后缀】：设置窗「Extension Routing」里 .mtest 与 .mtst 各有自己的 Import/Export 行，
//     用户可以让别的包接管其中一个——宿主开文件本就按后缀选实现，这个粒度没被收窄。
//  3. 扩展设置（IExtensionSettings）也以【格式】为单位：两个后缀共**一个**设置桶（键 "format:mtest+mtst"），
//     设置窗只出现一行、值只存一份。format 与三种引擎的结构差异在于它注册的是工厂、每次导入导出现 new，
//     故设置是在实例化处回喂的——下面 ApplySettings 的日志每导入/导出一次就打一条，正是这一点的可观测点。
public sealed class MultiSuffixFormat : IImportFormat, IExportFormat, IExtensionSettings
{
    // 设置 schema：两个明文字段（都影响导入产物，肉眼可验）+ 一个密钥字段（验证密钥桶的 account 用的是条目键）。
    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
    {
        var props = new OrderedMap<PropertyKey, IControllerConfig>();
        props.Add(("track_name", "Track Name"), TextBoxConfig.Create(DefaultTrackName));
        props.Add(("note_count", "Note Count"), SliderConfig.Integer(DefaultNoteCount, 1, 8));
        props.Add(("licence", "Licence Key"), TextBoxConfig.Create(string.Empty).WithPassword());
        return ObjectConfig.Create(props);
    }

    // 回喂：宿主在【每次现 new 之后】立刻灌一次（format 没有长驻实例）。自存下来供随后的导入/导出使用。
    // 密钥只记录有没有、绝不打印明文。
    public void ApplySettings(PropertyObject settings)
    {
        mTrackName = settings.GetString("track_name", DefaultTrackName);
        mNoteCount = (int)settings.GetDouble("note_count", DefaultNoteCount);
        mHasLicence = !string.IsNullOrEmpty(settings.GetString("licence", string.Empty));
        TuneLabContext.Global.GetLogger().Info(string.Format(
            "[V1.MultiSuffix] ApplySettings: track_name='{0}', note_count={1}, licence={2}",
            mTrackName, mNoteCount, mHasLicence ? "<set>" : "<empty>"));
    }

    // 导入：不解析内容，直接给一个固定的可见样例工程（手测时一定能看到 note）。
    // 轨名与 note 个数取自扩展设置——用户在设置窗改完再导入一次，就能直接看出设置到没到这个现 new 的实例。
    public ProjectInfo Deserialize(Stream stream)
    {
        var project = new ProjectInfo();
        project.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });

        var track = new TrackInfo { Name = mTrackName };
        var part = new MidiPartInfo { Name = "Multi Suffix Part", Pos = 0, EndOffset = 480 * mNoteCount };
        for (int i = 0; i < mNoteCount; i++)
            part.Notes.Add(new NoteInfo { Pos = 480 * i, Dur = 480, Pitch = 60 + i * 2, Lyric = "la" });
        track.Parts.Add(part);
        project.Tracks.Add(track);
        return project;
    }

    // 导出：写一行纯文本摘要即可——本插件测的是元数据、详情窗与设置，不是序列化保真。
    // 摘要里带上收到的设置，供核对"导出方向的实例同样拿到了设置"；密钥只写有无。
    public void Serialize(Stream output, ProjectInfo info)
    {
        var text = new StringBuilder();
        text.Append("multi-suffix test export; tracks=").Append(info.Tracks.Count)
            .Append("; track_name=").Append(mTrackName)
            .Append("; note_count=").Append(mNoteCount)
            .Append("; licence=").Append(mHasLicence ? "<set>" : "<empty>")
            .Append('\n');
        var bytes = Encoding.UTF8.GetBytes(text.ToString());
        output.Write(bytes, 0, bytes.Length);
    }

    const string DefaultTrackName = "Multi Suffix Track";
    const int DefaultNoteCount = 2;

    // 未回喂时的兜底（宿主只在实例化后回喂一次，读不到的字段按自身默认——与 SDK 契约一致）。
    string mTrackName = DefaultTrackName;
    int mNoteCount = DefaultNoteCount;
    bool mHasLicence;
}
