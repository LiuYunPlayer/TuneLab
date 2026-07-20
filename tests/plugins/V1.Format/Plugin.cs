using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1Format;

// V1 format 测试插件：.tltest 自定义 JSON 格式，import/export 往返。
// 空/损坏输入 → 返回固定样例工程，保证手动测试一定能看到 note。

public sealed class TestImportFormat : IImportFormat
{
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

        dto ??= ProjectDto.Sample();

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

public sealed class TestExportFormat : IExportFormat
{
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

        var bytes = JsonSerializer.SerializeToUtf8Bytes(dto, new JsonSerializerOptions { WriteIndented = true });
        output.Write(bytes, 0, bytes.Length);
    }
}

// ── 简易序列化 DTO ──
internal sealed class ProjectDto
{
    public List<TempoDto> Tempos { get; set; } = new();
    public List<TrackDto> Tracks { get; set; } = new();

    public static ProjectDto Sample() => new()
    {
        Tempos = { new TempoDto { Pos = 0, Bpm = 120 } },
        Tracks =
        {
            new TrackDto
            {
                Name = "V1 Test Track",
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
