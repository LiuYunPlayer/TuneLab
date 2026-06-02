using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TuneLab.Extensions.Formats;
using TuneLab.Extensions.Formats.DataInfo;

namespace TuneLab.TestPlugins.LegacyFormat;

// Legacy format 测试插件：用【老】接口 IImportFormat/IExportFormat + 老 DataInfo（TuneLab.Base.* 身份）。
// 经 Compat.Legacy 适配：老 ProjectInfo 深拷成 V1。验证老接口加载 + 边界往返。扩展名 .tloldfmt。

[ImportFormat("tloldfmt")]
public sealed class LegacyTestImportFormat : IImportFormat
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
        catch { }

        dto ??= ProjectDto.Sample();

        var project = new ProjectInfo();
        foreach (var t in dto.Tempos)
            project.Tempos.Add(new TempoInfo { Pos = t.Pos, Bpm = t.Bpm });
        if (project.Tempos.Count == 0)
            project.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });

        foreach (var tr in dto.Tracks)
        {
            var track = new TrackInfo { Name = tr.Name };
            var part = new MidiPartInfo { Name = tr.Name, Pos = 0, Dur = tr.Dur };
            foreach (var n in tr.Notes)
                part.Notes.Add(new NoteInfo { Pos = n.Pos, Dur = n.Dur, Pitch = n.Pitch, Lyric = n.Lyric });
            track.Parts.Add(part);
            project.Tracks.Add(track);
        }
        return project;
    }
}

[ExportFormat("tloldfmt")]
public sealed class LegacyTestExportFormat : IExportFormat
{
    public Stream Serialize(ProjectInfo info)
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
                    trackDto.Dur = midi.Dur;
                    foreach (var n in midi.Notes)
                        trackDto.Notes.Add(new NoteDto { Pos = n.Pos, Dur = n.Dur, Pitch = n.Pitch, Lyric = n.Lyric });
                }
            }
            dto.Tracks.Add(trackDto);
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(dto, new JsonSerializerOptions { WriteIndented = true });
        return new MemoryStream(bytes);
    }
}

internal sealed class ProjectDto
{
    public List<TempoDto> Tempos { get; set; } = new();
    public List<TrackDto> Tracks { get; set; } = new();

    public static ProjectDto Sample() => new()
    {
        Tempos = { new TempoDto { Pos = 0, Bpm = 100 } },
        Tracks =
        {
            new TrackDto
            {
                Name = "Legacy Test Track",
                Dur = 1440,
                Notes =
                {
                    new NoteDto { Pos = 0,    Dur = 480, Pitch = 67, Lyric = "sol" },
                    new NoteDto { Pos = 480,  Dur = 480, Pitch = 69, Lyric = "la" },
                    new NoteDto { Pos = 960,  Dur = 480, Pitch = 71, Lyric = "ti" },
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
