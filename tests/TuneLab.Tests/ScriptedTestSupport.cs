using System;
using System.Collections.Generic;
using System.IO;
using TuneLab.Extensions.Scripted;

namespace TuneLab.Tests;

// 假 prober：按 exe 完整路径查表出"版本/flavor/arch"，表外一律核验失败（模拟商店桩/shim 出局）。
// 单测绝不 spawn 真解释器（设计文档 §12.0 M0 验收：编译面不碰解释器真链）。
internal sealed class FakeProber : IRuntimeProber
{
    public Dictionary<string, RuntimeInfo> Table { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int ProbeCount { get; private set; }

    public void Add(string exe, string flavor, string version)
        => Table[exe] = new RuntimeInfo(exe, flavor, version, "x64");

    public ProbeOutcome Probe(string exe, string[]? extraArgs, string flavor)
    {
        ProbeCount++;
        return Table.TryGetValue(exe, out var info)
            ? new ProbeOutcome(info, null)
            : new ProbeOutcome(null, "probe exited with code 9009 (stub or shim)");
    }

    public static RuntimeInfo FakeExe(string dir, string name, FakeProber prober, string flavor, string version)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "fake-interpreter");
        prober.Add(path, flavor, version);
        return new RuntimeInfo(path, flavor, version, "x64");
    }
}
