using System.Collections.Generic;
using System.IO;
using TuneLab.Data;
using TuneLab.Extensions.Derivers;
using TuneLab.Extensions.Formats.TLP;
using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// 派生记录账本经两种原生格式（tlp=JSON、tlpx=CBOR）Serialize→Deserialize 后全等。
// 记录是宿主内部（NativeAudioPartInfo 子类夹带、DerivationRecordInfo 宿主内部），只 native 格式持久化；
// 通用格式插件看不见（读侧总造 NativeAudioPartInfo，缺键 = 空账本）。
public class DerivationRecordSerializationTests
{
    static ProjectInfo SampleProject()
    {
        var audio = new NativeAudioPartInfo { Name = "clip", Pos = 0, StartOffset = 0, EndOffset = 1920, Path = "vox.wav" };
        audio.DerivationRecords.Add("key-abc", new DerivationRecordInfo
        {
            EngineId = "test.transcribe",
            EngineDisplayName = "Transcribe to MIDI",
            Parameters = new PropertyObject(new Map<string, PropertyValue> { { "sensitivity", 0.7 }, { "mode", "melody" } }),
            StartTimestamp = 1_700_000_000.5,
            Label = "run 1",
        });
        audio.DerivationRecords.Add("key-def", new DerivationRecordInfo
        {
            EngineId = "test.transcribe",
            EngineDisplayName = "Transcribe to MIDI",
            StartTimestamp = 1_700_000_050.0,
            Label = "run 2",
        });

        var track = new TrackInfo { Name = "t" };
        track.Parts.Add(audio);

        var project = new ProjectInfo();
        project.Tempos.Add(new TempoInfo { Pos = 0, Bpm = 120 });
        project.TimeSignatures.Add(new TimeSignatureInfo { BarIndex = 0, Numerator = 4, Denominator = 4 });
        project.Tracks.Add(track);
        return project;
    }

    static void AssertRoundTrip(ProjectInfo rt)
    {
        var part = Assert.IsType<NativeAudioPartInfo>(Assert.Single(Assert.Single(rt.Tracks).Parts));
        Assert.Equal("vox.wav", part.Path);
        Assert.Equal(2, part.DerivationRecords.Count);

        var a = part.DerivationRecords["key-abc"];
        Assert.Equal("test.transcribe", a.EngineId);
        Assert.Equal("Transcribe to MIDI", a.EngineDisplayName);
        Assert.Equal(1_700_000_000.5, a.StartTimestamp);
        Assert.Equal("run 1", a.Label);
        Assert.Equal(0.7, a.Parameters.GetValue("sensitivity", 0.0));
        Assert.Equal("melody", a.Parameters.GetValue("mode", ""));

        var b = part.DerivationRecords["key-def"];
        Assert.Equal("run 2", b.Label);
        Assert.Equal(1_700_000_050.0, b.StartTimestamp);
    }

    [Fact]
    public void Json_RoundTrip_PreservesRecords()
    {
        var format = new TuneLabProject();
        using var stream = new MemoryStream();
        format.Serialize(stream, SampleProject());
        stream.Position = 0;
        AssertRoundTrip(format.Deserialize(stream));
    }

    [Fact]
    public void Cbor_RoundTrip_PreservesRecords()
    {
        var format = new TuneLabProjectCbor();
        using var stream = new MemoryStream();
        format.Serialize(stream, SampleProject());
        stream.Position = 0;
        AssertRoundTrip(format.Deserialize(stream));
    }

    // 无记录：不落键、读回空账本（文件不因新字段膨胀）。
    [Fact]
    public void NoRecords_StaysEmpty_BothFormats()
    {
        var project = SampleProject();
        ((NativeAudioPartInfo)project.Tracks[0].Parts[0]).DerivationRecords.Clear();

        foreach (var format in new object[] { new TuneLabProject(), (object)new TuneLabProjectCbor() })
        {
            using var stream = new MemoryStream();
            ((IExportFormat)format).Serialize(stream, project);
            stream.Position = 0;
            var rt = ((IImportFormat)format).Deserialize(stream);
            var part = Assert.IsType<NativeAudioPartInfo>(rt.Tracks[0].Parts[0]);
            Assert.Empty(part.DerivationRecords);
        }
    }
}
