using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TuneLab.Extensions;
using Xunit;

namespace TuneLab.Tests;

// 脚本字段进完整加载管线（ExtensionManager.Load）后的结局（设计文档 §2.1 校验次序、§1.2 协议门）。
// 只用结构错误一支——它在 runtime 探测【之前】落判，故与机器上的解释器实况无关（确定性）。
// 与一切读 ExtensionManager.LoadResults（进程级静态表）的测试串行，且逐条自清理——
// ExtensionListCommandTests 的"空安装"断言依赖这张表，脏了就是跨类串扰（同 ActionRegistry 范式）。
[Collection("ExtensionLoadResults")]
public class ScriptedLoadPipelineTests : IDisposable
{
    // 进程级静态表 ExtensionManager.LoadResults 的串行域（定义在此，成员：本类 + ExtensionListCommandTests）。
    [CollectionDefinition("ExtensionLoadResults")]
    public sealed class ExtensionLoadResultsCollection { }

    readonly List<ExtensionLoadResult> mAdded = [];

    ExtensionLoadResult LoadManifest(string id, object manifest)
        => LoadRaw("pipe-" + id.Replace('.', '_'), JsonSerializer.Serialize(manifest));

    public void Dispose()
    {
        var list = (List<ExtensionLoadResult>)ExtensionManager.LoadResults;
        foreach (var r in mAdded)
            list.Remove(r);
    }

    [Fact]
    public void ScriptedEntryMixingAlcFieldsFailsThroughThePipeline()
    {
        var result = LoadManifest("com.test.scriptmix", new
        {
            id = "com.test.scriptmix", name = "mix", version = "1.0.0", type = "voice", engine = "MIX",
            @class = "X.Y", assembly = "missing.dll", runtime = "python>=3.10", entry = "main.py",
        });
        Assert.Equal(ExtensionLoadStatus.Failed, result.Status);
        Assert.Contains("'runtime'/'entry'", result.Error);
        Assert.Single(result.Entries);
        Assert.Equal(ExtensionEntryStatus.Failed, result.Entries[0].Status);
    }

    ExtensionLoadResult LoadRaw(string tag, string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), "tl-scripted-" + tag + "-" + Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), json);
        var before = ExtensionManager.LoadResults.Count;
        ExtensionManager.Load(dir);
        var result = ExtensionManager.LoadResults.Last(r => r.DirectoryPath == dir);
        for (int i = before; i < ExtensionManager.LoadResults.Count; i++)
            mAdded.Add(ExtensionManager.LoadResults[i]);
        return result;
    }

    [Fact]
    public void ProtocolVersionGateUsesExactJsonPropertyName()
    {
        // manifest 键是连字符的 "protocol-version"，匿名类型写不出来 → 手写 JSON。
        var result = LoadRaw("protocol", """
            {
              "id": "com.test.proto99", "name": "proto99", "version": "1.0.0",
              "protocol-version": 99,
              "type": "format", "suffixes": ["p99"], "class": "X.Y", "assembly": "missing.dll"
            }
            """);
        Assert.Equal(ExtensionLoadStatus.Skipped, result.Status);
        Assert.Contains("Requires extension protocol 99, host provides 1", result.Error);
    }

    [Fact]
    public void InvalidProtocolVersionFails()
    {
        var result = LoadRaw("protocolbad", """
            {
              "id": "com.test.protozero", "name": "protozero", "version": "1.0.0",
              "protocol-version": 0,
              "type": "format", "suffixes": ["pz0"], "class": "X.Y", "assembly": "missing.dll"
            }
            """);
        Assert.Equal(ExtensionLoadStatus.Failed, result.Status);
        Assert.Contains("Invalid protocol-version", result.Error);
    }

    [Fact]
    public void UnknownTypeDeclaringRuntimeIsNotSwallowedAsResource()
    {
        var result = LoadRaw("unknownscripted", """
            {
              "id": "com.test.weirdscripted", "name": "weird", "version": "1.0.0",
              "type": "sfx-scripted", "runtime": "node>=18", "entry": "main.js"
            }
            """);
        Assert.Equal(ExtensionLoadStatus.Skipped, result.Status);
        Assert.Contains("declares code (assembly/class or runtime/entry)", result.Error);
    }
}
