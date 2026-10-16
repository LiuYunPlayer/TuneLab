using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TuneLab.Extensions;
using TuneLab.Extensions.Scripted;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// 真 Node 解释器 format E2E（与 ScriptedPythonE2ETests 同构）。node 经 TUNELAB_NODE 或 PATH 探测；
// 不在场默认 vacuous pass，TUNELAB_REQUIRE_NODE=1 变红（CI 矩阵装了就必须真跑）。
[Trait("runtime", "needs-interpreter")]
[Collection("SidecarSpawns")]
public class ScriptedNodeE2ETests : IDisposable
{
    static readonly string sPackageDir = LocateUp("tests", "plugins", "NodeFormat");

    static string LocateUp(params string[] tail)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(tail).ToArray());
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("NodeFormat package not found: " + Path.Combine(tail));
    }

    static RuntimeSpec Parse(string s)
    {
        Assert.True(RuntimeSpec.TryParse(s, out var spec, out _));
        return spec;
    }

    [Fact(Timeout = 120_000)]
    public void RealNodeFormatRoundTrip()
    {
        if (!RuntimeLocator.Default.TryResolve(Parse("node>=18"), out var node, out var trail))
        {
            if (Environment.GetEnvironmentVariable("TUNELAB_REQUIRE_NODE") == "1")
                Assert.Fail("node not resolved: " + trail);
            return;   // 无解释器环境：vacuous pass
        }
        Environment.SetEnvironmentVariable("NODE_PATH", LocateUp("scripting", "node"));

        var entry = "plugin/main.js";
        var host = SidecarRegistry.For("com.tunelab.test.nodeformat", sPackageDir, node, entry);
        host.ExpectFormat(entry, ["ntxt"], ["ntxt"]);
        var proxy = new RemoteFormat(host, entry, hasSettings: false);
        try
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes("60 0 480 la\n62 480 240 du\n# comment\n65 720 120 do re\n"));
            var project = ((IImportFormat)proxy).Deserialize(input);

            var part = Assert.IsType<MidiPartInfo>(Assert.Single(Assert.Single(project.Tracks).Parts));
            Assert.Equal(3, part.Notes.Count);
            Assert.Equal(60, part.Notes[0].Pitch);
            Assert.Equal("do re", part.Notes[2].Lyric);          // 空格歌词整段取回
            Assert.Equal("ntxt", part.Notes[0].Properties.GetString("source"));   // tagged property 跨语言往返

            using var output = new MemoryStream();
            ((IExportFormat)proxy).Serialize(output, project);
            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.Contains("60 0 480 la", text);
            Assert.Contains("65 720 120 do re", text);
            Assert.DoesNotContain("# comment", text);
        }
        finally
        {
            host.Shutdown();
        }
    }

    public void Dispose() => SidecarRegistry.ShutdownAll();
}
