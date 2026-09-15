using System;
using System.IO;
using System.Linq;
using System.Text;
using TuneLab.Extensions;
using TuneLab.Extensions.Scripted;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// 真 Lua 解释器 format E2E（与 Node/Python 同构）。CI 的 ubuntu/macos 装了 lua5.4 才真跑
// （windows 暂无打包 lua → vacuous）；TUNELAB_REQUIRE_LUA=1 强制断言。
[Trait("runtime", "needs-interpreter")]
[Collection("SidecarSpawns")]
public class ScriptedLuaE2ETests : IDisposable
{
    static readonly string sPackageDir = LocateUp("tests", "plugins", "LuaFormat");

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
        throw new DirectoryNotFoundException("LuaFormat package not found: " + Path.Combine(tail));
    }

    static RuntimeSpec Parse(string s)
    {
        Assert.True(RuntimeSpec.TryParse(s, out var spec, out _));
        return spec;
    }

    [Fact(Timeout = 120_000)]
    public void RealLuaFormatRoundTrip()
    {
        if (!RuntimeLocator.Default.TryResolve(Parse("lua>=5.1"), out var lua, out var trail))
        {
            if (Environment.GetEnvironmentVariable("TUNELAB_REQUIRE_LUA") == "1")
                Assert.Fail("lua not resolved: " + trail);
            return;   // 无解释器环境：vacuous pass
        }
        Environment.SetEnvironmentVariable("LUA_PATH", LocateUp("scripting", "lua") + Path.DirectorySeparatorChar + "?.lua;;");

        var entry = "plugin/main.lua";
        var host = SidecarRegistry.For("com.tunelab.test.luaformat", sPackageDir, lua, entry);
        host.ExpectFormat(entry, ["lpx"], ["lpx"]);
        var proxy = new RemoteFormat(host, entry, hasSettings: false);
        try
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes("60 0 480 la\n62 480 240 du\n# comment\n65 720 120 mi\n"));
            var project = ((IImportFormat)proxy).Deserialize(input);

            var part = Assert.IsType<MidiPartInfo>(Assert.Single(Assert.Single(project.Tracks).Parts));
            Assert.Equal(3, part.Notes.Count);
            Assert.Equal(60, part.Notes[0].Pitch);
            Assert.Equal("mi", part.Notes[2].Lyric);
            Assert.Equal("lpx", part.Notes[0].Properties.GetString("source"));   // tagged property 跨语言往返

            using var output = new MemoryStream();
            ((IExportFormat)proxy).Serialize(output, project);
            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.Contains("60 0 480 la", text);
            Assert.Contains("65 720 120 mi", text);
            Assert.DoesNotContain("# comment", text);
        }
        finally
        {
            host.Shutdown();
        }
    }

    public void Dispose() => SidecarRegistry.ShutdownAll();
}
