using System;
using System.Linq;
using System.Text.Json;
using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// scripting/wire/v1.json 的宿主侧封条：值编码规范（tagged property、具名浮点、Map 保序、
// PartInfo 判别器、枚举 camelCase）逐条钉住。假 sidecar / 三语言 façade 都按这份规范实现，
// 宿主偏离规范 = 本类红。
public class ScriptedWireCodecTests
{
    static ProjectInfo SampleProject()
    {
        var props = new Map<string, PropertyValue>();
        props["s"] = PropertyValue.Create(0.5);
        props["b"] = PropertyValue.Create(true);
        props["t"] = PropertyValue.Create("str");
        props["arr"] = PropertyValue.Create(new PropertyArray(new PropertyValue[] { PropertyValue.Create(1.0), PropertyValue.Create("x") }));
        var inner = new Map<string, PropertyValue>();
        inner["inner"] = PropertyValue.Create(-2.25);
        props["obj"] = PropertyValue.Create(new PropertyObject(inner));
        var midi = new MidiPartInfo
        {
            Name = "P",
            Pos = 960,
            SoundSource = new SoundSourceInfo { Kind = SourceKind.Voice, Type = "Eng", Id = "bank" },
            Notes =
            {
                new NoteInfo
                {
                    Pos = 960, Dur = 480, Pitch = 60, Lyric = "la", Pronunciation = "la",
                    Properties = new PropertyObject(props),
                },
            },
        };
        midi.Automations["Growl"] = new AutomationInfo { DefaultValue = 0.3, Points = { new Point(960, 0.5), new Point(1440, 0.7) } };
        midi.Vibratos.Add(new VibratoInfo { Pos = 100, Dur = 200, Frequency = 5.5, AffectedAutomations = { ["Volume"] = 1.0 } });
        return new ProjectInfo
        {
            Tempos = { new TempoInfo { Pos = 0, Bpm = 120 } },
            TimeSignatures = { new TimeSignatureInfo { BarIndex = 0, Numerator = 4, Denominator = 4 } },
            Tracks = { new TrackInfo { Name = "T", Parts = { midi } } },
        };
    }

    [Fact]
    public void RoundTripsEveryShapeTheWireCarries()
    {
        var json = Extensions.Scripted.ScriptedWire.Serialize(SampleProject());
        var back = Extensions.Scripted.ScriptedWire.Deserialize<ProjectInfo>(json)!;

        var note = ((MidiPartInfo)back.Tracks[0].Parts[0]).Notes[0];
        Assert.Equal("la", note.Lyric);
        Assert.Equal(60, note.Pitch);                                   // required int 成员
        Assert.Equal(0.5, note.Properties.GetDouble("s"));
        Assert.True(note.Properties.GetBoolean("b"));
        Assert.Equal("str", note.Properties.GetString("t"));
        var arr = note.Properties.Map["arr"].ToArray(out var a) ? a : null;
        Assert.NotNull(arr);
        Assert.Equal(2, arr!.Count);
        Assert.Equal("x", arr[1].ToString(out var s) ? s : null);
        var obj = note.Properties.Map["obj"].ToObject(out var o) ? o : null;
        Assert.NotNull(obj);
        Assert.Equal(-2.25, obj!.GetDouble("inner"));

        var automations = ((MidiPartInfo)back.Tracks[0].Parts[0]).Automations;
        Assert.Equal(0.3, automations["Growl"].DefaultValue);
        Assert.Equal(new Point(1440, 0.7), automations["Growl"].Points[1]);   // 公有字段 X/Y
        Assert.Equal(SourceKind.Voice, ((MidiPartInfo)back.Tracks[0].Parts[0]).SoundSource.Kind);
        Assert.Equal(1.0, ((MidiPartInfo)back.Tracks[0].Parts[0]).Vibratos[0].AffectedAutomations["Volume"]);
    }

    [Fact]
    public void WireFormMatchesSpec()
    {
        var json = Extensions.Scripted.ScriptedWire.Serialize(SampleProject());
        using var doc = JsonDocument.Parse(json);
        var track = doc.RootElement.GetProperty("tracks")[0];
        var part = track.GetProperty("parts")[0];
        Assert.Equal("midi", part.GetProperty("kind").GetString());            // PartInfo 判别器
        var props = part.GetProperty("notes")[0].GetProperty("properties");
        Assert.Equal(JsonValueKind.True, props.GetProperty("b").GetProperty("b").ValueKind);  // tagged {"b":true}
        Assert.Equal(0.5, props.GetProperty("s").GetProperty("n").GetDouble());
        Assert.Equal("voice", part.GetProperty("soundSource").GetProperty("kind").GetString()); // 枚举 camelCase 名
        Assert.False(part.GetProperty("notes")[0].TryGetProperty("Properties", out _));       // 一律 camelCase
    }

    [Fact]
    public void MapsPreserveInsertionOrderOnTheWire()
    {
        var map = new Map<string, double>();
        map["zulu"] = 1; map["alpha"] = 2; map["mike"] = 3;
        var json = JsonSerializer.Serialize(map, Extensions.Scripted.ScriptedWire.Options);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(new[] { "zulu", "alpha", "mike" }, doc.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public void MultipleNeverCrossesTheWire()
    {
        var props = new Map<string, PropertyValue>();
        props["x"] = PropertyValue.Multiple;
        var part = new MidiPartInfo { Properties = new PropertyObject(props) };
        var project = new ProjectInfo();
        project.Tracks.Add(new TrackInfo { Parts = { part } });
        Assert.Throws<NotSupportedException>(() => Extensions.Scripted.ScriptedWire.Serialize(project));
    }

    [Fact]
    public void NaNAndInfinityRideStringLiterals()
    {
        // 写侧形态 = 字符串字面量 {"n":"NaN"}（合法 JSON，STJ AllowNamedFloatingPointLiterals 的写出形）；
        // 读侧两种都收（字符串字面量 / 裸具名 token，后者 python json allow_nan 产）。
        var props = new Map<string, PropertyValue>();
        props["nan"] = PropertyValue.Create(double.NaN);
        props["inf"] = PropertyValue.Create(double.PositiveInfinity);
        var json = Extensions.Scripted.ScriptedWire.Serialize(new PropertyObject(props));
        Assert.Contains("\"n\":\"NaN\"", json);
        Assert.Contains("\"n\":\"Infinity\"", json);
        var back = Extensions.Scripted.ScriptedWire.Deserialize<PropertyObject>(json)!;
        Assert.True(double.IsNaN(back.Map["nan"].ToDouble(out var n) ? n : 0));
        Assert.True(double.IsPositiveInfinity(back.Map["inf"].ToDouble(out var i) ? i : 0));
    }

    [Fact]
    public void TolerantReadAcceptsPlainNativePropertyValues()
    {
        // 宽容读侧：第三方手写的裸值（未 tagged）按类型推断收——宿主写侧永远带 tag。
        var json = """{"tension":0.5,"on":true,"word":"x","nest":{"deep":1.0},"nothing":null}""";
        var obj = Extensions.Scripted.ScriptedWire.Deserialize<PropertyObject>(json)!;
        Assert.Equal(0.5, obj.GetDouble("tension"));
        Assert.True(obj.GetBoolean("on"));
        Assert.Equal("x", obj.GetString("word"));
        Assert.True(obj.Map["nest"].ToObject(out var nest));
        Assert.Equal(1.0, nest!.GetDouble("deep"));
        Assert.True(obj.Map["nothing"].IsNull());
    }
}
