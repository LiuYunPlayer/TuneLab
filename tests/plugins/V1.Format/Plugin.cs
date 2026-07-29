using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1Format;

// V1 format 测试插件：.tltest 自定义 JSON 格式，import/export 往返。
// 空/损坏输入 → 返回固定样例工程，保证手动测试一定能看到 note。
//
// 【拆写形态夹具】导入与导出由**两个类**实现，manifest 里也就是**两个条目**
// （type = format-import / format-export），于是它们各有自己的 name、introduction 与**独立的设置桶**：
//   format-import:tltest   →  fallback_track_name
//   format-export:tltest   →  indent
// 两份 schema 字段互不相同，正好用来验证两个桶没有串味：在一边改值不会影响另一边。
// 反例（合写形态）见 V1.MultiSuffix：一个类实现两个接口、type=format、只有一个桶。

public sealed class TestImportFormat : IImportFormat, IExtensionSettings
{
    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
    {
        var props = new OrderedMap<PropertyKey, IControllerConfig>();
        props.Add(("fallback_track_name", "Fallback Track Name"), TextBoxConfig.Create(DefaultFallbackTrackName));
        return ObjectConfig.Create(props);
    }

    public void ApplySettings(PropertyObject settings)
    {
        mFallbackTrackName = settings.GetString("fallback_track_name", DefaultFallbackTrackName);
        TuneLabContext.Global.GetLogger().Info(string.Format(
            "[V1.Format/import] ApplySettings: fallback_track_name='{0}'", mFallbackTrackName));
    }

    const string DefaultFallbackTrackName = "V1 Test Track";
    string mFallbackTrackName = DefaultFallbackTrackName;

    public ProjectInfo Deserialize(Stream stream)
    {
        ProjectDto? dto = null;
        try
        {
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(text))
                dto = JsonSerializer.Deserialize<ProjectDto>(text);
        }
        catch { /* 损坏输入 → 落样例 */ }

        // 样例轨名取自本条目的扩展设置——用户改完再导入一个空/坏文件，就能看出设置到没到这个现 new 的实例。
        dto ??= ProjectDto.Sample(mFallbackTrackName);

        var project = new ProjectInfo();
        foreach (var t in dto.Tempos)
            project.Tempos.Add(new TempoInfo { Pos = t.Pos, Bpm = t.Bpm });
        if (project.Tempos.Count == 0)
            project.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });

        foreach (var tr in dto.Tracks)
        {
            var track = new TrackInfo { Name = tr.Name };
            var part = new MidiPartInfo { Name = tr.Name, Pos = 0, EndOffset = tr.Dur };
            foreach (var n in tr.Notes)
                part.Notes.Add(new NoteInfo { Pos = n.Pos, Dur = n.Dur, Pitch = n.Pitch, Lyric = n.Lyric });
            track.Parts.Add(part);
            project.Tracks.Add(track);
        }
        return project;
    }
}

public sealed class TestExportFormat : IExportFormat, IExtensionSettings
{
    // 与导入侧【字段完全不同】：两个条目各有自己的桶，串味的话一眼可见。
    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
    {
        var props = new OrderedMap<PropertyKey, IControllerConfig>();
        props.Add(("indent", "Indent Output"), CheckBoxConfig.Create(true));
        return ObjectConfig.Create(props);
    }

    public void ApplySettings(PropertyObject settings)
    {
        mIndent = settings.GetBoolean("indent", true);
        TuneLabContext.Global.GetLogger().Info(string.Format("[V1.Format/export] ApplySettings: indent={0}", mIndent));
    }

    bool mIndent = true;

    public void Serialize(Stream output, ProjectInfo info)
    {
        var dto = new ProjectDto();
        foreach (var t in info.Tempos)
            dto.Tempos.Add(new TempoDto { Pos = t.Pos, Bpm = t.Bpm });

        foreach (var track in info.Tracks)
        {
            var trackDto = new TrackDto { Name = track.Name };
            foreach (var part in track.Parts)
            {
                if (part is MidiPartInfo midi)
                {
                    trackDto.Dur = midi.EndOffset - midi.StartOffset;
                    foreach (var n in midi.Notes)
                        trackDto.Notes.Add(new NoteDto { Pos = n.Pos, Dur = n.Dur, Pitch = n.Pitch, Lyric = n.Lyric });
                }
            }
            dto.Tracks.Add(trackDto);
        }

        // 缩进与否取自本条目自己的设置（与导入侧那份互不相干）——导出文件是单行还是多行，肉眼即可判定。
        var bytes = JsonSerializer.SerializeToUtf8Bytes(dto, new JsonSerializerOptions { WriteIndented = mIndent });
        output.Write(bytes, 0, bytes.Length);
    }
}

// ── 简易序列化 DTO ──
internal sealed class ProjectDto
{
    public List<TempoDto> Tempos { get; set; } = new();
    public List<TrackDto> Tracks { get; set; } = new();

    public static ProjectDto Sample(string trackName) => new()
    {
        Tempos = { new TempoDto { Pos = 0, Bpm = 120 } },
        Tracks =
        {
            new TrackDto
            {
                Name = trackName,
                Dur = 1920,
                Notes =
                {
                    new NoteDto { Pos = 0,    Dur = 480, Pitch = 60, Lyric = "do" },
                    new NoteDto { Pos = 480,  Dur = 480, Pitch = 62, Lyric = "re" },
                    new NoteDto { Pos = 960,  Dur = 480, Pitch = 64, Lyric = "mi" },
                    new NoteDto { Pos = 1440, Dur = 480, Pitch = 65, Lyric = "fa" },
                },
            },
        },
    };
}

internal sealed class TempoDto { public double Pos { get; set; } public double Bpm { get; set; } }

internal sealed class TrackDto
{
    public string Name { get; set; } = string.Empty;
    public double Dur { get; set; }
    public List<NoteDto> Notes { get; set; } = new();
}

internal sealed class NoteDto
{
    public double Pos { get; set; }
    public double Dur { get; set; }
    public int Pitch { get; set; }
    public string Lyric { get; set; } = string.Empty;
}
