using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TuneLab.Extensions.Scripted;
using Xunit;

namespace TuneLab.Tests;

// scripting/wire/v1.json 是宿主↔fake↔三语言 façade 的规范真源——它必须机器可读、且与
// 实现的方法面同步（新增方法不更 spec = 本类红；spec 坏了 = 三方各猜形状的开始）。
public class ScriptedWireSpecTests
{
    static readonly string[] RequiredMethods =
    {
        "initialize", "ping", "shutdown", "log.write",
        "format.import", "format.export",
        "settings.get_config", "settings.apply",
        "engine.init", "engine.destroy", "engine.create_session", "session.dispose",
        "effect.get_property_config", "effect.get_automation_configs", "effect.get_synthesized_parameter_configs",
        "effect.process", "effect.cancel", "effect.status", "effect.parameters",
        "voice.get_catalog", "voice.get_automation_configs", "voice.get_synthesized_parameter_configs",
        "voice.get_part_property_config", "voice.get_note_property_config", "voice.get_phoneme_property_configs",
        "voice.default_lyric", "voice.is_continuation", "voice.peek", "voice.synthesize", "voice.dirty",
        "voice.cancel",
        "voice.status", "voice.pitch", "voice.phonemes", "voice.parameters", "engine.catalog_changed",
        "host.input_read", "host.properties", "host.automation_tracks", "host.evaluate",
        "host.snapshot", "host.notes",
        "host.segment_create", "host.segment_write", "host.segment_commit", "host.segment_resize", "host.segment_dispose",
    };

    static string SpecPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "scripting", "wire", "v1.json");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("scripting/wire/v1.json not found from test output");
    }

    [Fact]
    public void SpecParsesAndMatchesHostProtocolVersion()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SpecPath()));
        var root = doc.RootElement;
        Assert.Equal(WireProtocol.Version, root.GetProperty("protocol-version").GetInt32());
    }

    [Fact]
    public void MethodTableCoversEveryWireMethod()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SpecPath()));
        var methods = new HashSet<string>(doc.RootElement.GetProperty("methods").EnumerateObject().Select(p => p.Name));
        Assert.Subset(RequiredMethods.ToHashSet(), methods);   // spec ⊇ 实现清单：加方法不写 spec 即红
    }

    [Fact]
    public void ValueEncodingSectionIsPresent()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SpecPath()));
        Assert.True(doc.RootElement.GetProperty("value-encoding").TryGetProperty("config", out _));
        Assert.True(doc.RootElement.GetProperty("value-encoding").TryGetProperty("partinfo-discriminator", out _));
    }
}
