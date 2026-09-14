using System;
using System.IO;
using System.Linq;
using System.Text;
using TuneLab.Extensions;
using TuneLab.Extensions.Formats;
using TuneLab.Extensions.Scripted;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// 端到端真解释器集成（设计 §12.0 M1 验收：HeadlessHost E2E 装→导入→导出）。
// 用 SidecarHost/RemoteFormat 而非完整 FormatsManager，是为了不碰 %APPDATA% 的已装扩展目录。
// python 不在场即 vacuously pass（CI 由 needs-interpreter 任务真跑）。trait 标记，
// 单独运行： dotnet test --filter "runtime=needs-interpreter"
[Trait("runtime", "needs-interpreter")]
public class ScriptedPythonE2ETests
{
    static readonly string sPackageDir = LocateUp("tests", "plugins", "PyFormat");

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
        throw new DirectoryNotFoundException("PyFormat package not found: " + Path.Combine(tail));
    }

    [Fact]
    public void RealPythonImportExportRoundTrip()
    {
        if (!RuntimeLocator.Default.TryResolve(ParseSpec("python>=3.10"), out var python, out var trail))
        {
            if (Environment.GetEnvironmentVariable("TUNELAB_REQUIRE_PYTHON") == "1")
                Assert.Fail("python not resolved: " + trail);
            return;   // 无解释器环境：vacuous pass；真 CI 装了 python 才验
        }

        var facadeDir = LocateUp("scripting", "python");
        Environment.SetEnvironmentVariable("PYTHONPATH", facadeDir);   // 子进程继承

        var entry = "plugin/main.py";
        var host = SidecarRegistry.For("com.tunelab.test.pyformat", sPackageDir, python, entry);
        host.ExpectFormat(entry, ["pyt"], ["pyt"]);
        var proxy = new RemoteFormat(host, entry);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // 导入：一行一音符的文本 → ProjectInfo
            using var input = new MemoryStream(Encoding.UTF8.GetBytes("60 0 480 la\n62 480 240 du\n"));
            var project = ((IImportFormat)proxy).Deserialize(input);
            Assert.Single(project.Tracks);
            var part = Assert.IsType<MidiPartInfo>(project.Tracks[0].Parts[0]);
            Assert.Equal(2, part.Notes.Count);
            Assert.Equal(60, part.Notes[0].Pitch);
            Assert.Equal("la", part.Notes[0].Lyric);
            Assert.Equal("pytxt", part.Notes[0].Properties.GetString("source"));   // 跨语言 tagged property 往返

            // 导出：ProjectInfo → 文本
            using var output = new MemoryStream();
            ((IExportFormat)proxy).Serialize(output, project);
            var text = Encoding.UTF8.GetString(output.ToArray());
            Assert.Contains("60 0 480 la", text);
            Assert.Contains("62 480 240 du", text);
        }
        finally
        {
            host.Shutdown();
        }
        // 真子进程往返（spawn + 两次 RPC）不可能亚百毫秒——这条把"vacuous 早退"钉死。
        Assert.True(sw.ElapsedMilliseconds > 50, $"round-trip suspiciously fast: {sw.ElapsedMilliseconds}ms");
    }

    static RuntimeSpec ParseSpec(string s)
    {
        Assert.True(RuntimeSpec.TryParse(s, out var spec, out _));
        return spec;
    }
}
