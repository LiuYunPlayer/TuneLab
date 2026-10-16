using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using TuneLab.Extensions.Scripted;
using Xunit;

namespace TuneLab.Tests;

// 三语言 façade 的真解释器握手冒烟：起进程 → initialize → 断言 provides 兑现与 capabilities。
// 比完整 E2E 便宜，专门钉"façade 能起来、说得同一种话"这一层（Node/Lua 的运行时差异大）。
// 缺解释器 vacuous；TUNELAB_REQUIRE_NODE / TUNELAB_REQUIRE_LUA 强制。
[Trait("runtime", "needs-interpreter")]
[Collection("SidecarSpawns")]
public class ScriptedFacadeHandshakeTests
{
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
        throw new DirectoryNotFoundException(string.Join("/", tail));
    }

    static void Smoke(string label, string exe, string reqEnv, string packageDir, string entry,
                      string expectEntry, Action preSpawn)
    {
        if (Environment.GetEnvironmentVariable(reqEnv) != "1")
        {
            if (!File.Exists(exe) && !ResolveOnPath(exe))
                return;   // vacuous
        }
        preSpawn();
        var resolved = exe.Contains('/') || exe.Contains('\\') ? exe : FindOnPath(exe);
        Assert.NotNull($"interpreter not found on PATH: {exe}");
        var psi = new ProcessStartInfo(resolved!)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = packageDir,
        };
        psi.ArgumentList.Add(entry);
        using var proc = Process.Start(psi)!;
        var req = """{"jsonrpc":"2.0","id":"h1","method":"initialize","params":{"protocol":1,"package_id":"com.test.smoke","entries":[{"type":"format","entry":"PLUGIN"}]}}"""
            .Replace("PLUGIN", expectEntry);
        var bytes = Encoding.UTF8.GetBytes(req);
        proc.StandardInput.BaseStream.Write(Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n"));
        proc.StandardInput.BaseStream.Write(bytes);
        proc.StandardInput.BaseStream.Flush();

        // 读一个应答帧：headers 直到空行，收 Content-Length，再读 body
        long len = -1;
        while (true)
        {
            var line = ReadLine(proc.StandardOutput.BaseStream);
            if (line == null) break;
            if (line.Length == 0) break;
            var m = System.Text.RegularExpressions.Regex.Match(line, @"Content-Length:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) len = long.Parse(m.Groups[1].Value);
        }
        Assert.True(len > 0, label + ": no Content-Length frame received");
        proc.StandardInput.Close();          // EOF 保护：若 façade 卡在等什么
        var body = new byte[(int)len];
        int got = proc.StandardOutput.BaseStream.Read(body, 0, (int)len);
        Assert.Equal(len, got);
        var json = Encoding.UTF8.GetString(body, 0, (int)len);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var result = doc.RootElement.GetProperty("result");
        Assert.Equal("inline", result.GetProperty("capabilities").GetProperty("audio_plane").GetString());
        var provides = result.GetProperty("provides");
        Assert.Equal(System.Text.Json.JsonValueKind.Array, provides.ValueKind);
        Assert.True(provides.GetArrayLength() >= 1, label + " façade provided no entries: " + json);
        try { proc.Kill(true); } catch { }
    }

    static string? ReadLine(Stream s)
    {
        var sb = new StringBuilder();
        while (true)
        {
            int c = s.ReadByte();
            if (c < 0) return sb.Length == 0 ? null : sb.ToString();
            if (c == '\n') return sb.ToString().TrimEnd('\r');
            sb.Append((char)c);
        }
    }

    static bool ResolveOnPath(string exe) => FindOnPath(exe) != null;
    static string FindOnPath(string exe)
        => (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator).Select(d => Path.Combine(d, OperatingSystem.IsWindows() ? exe + ".exe" : exe))
            .FirstOrDefault(File.Exists)!;

    [Fact(Timeout = 60_000)]
    public void NodeFacadeShakesHands()
    {
        var node = Environment.GetEnvironmentVariable("TUNELAB_NODE") ?? "node";
        if (node == "node" && FindOnPath("node") == null) { /* vacuous below */ }
        var facade = LocateUp("scripting", "node");
        Smoke("node", node, "TUNELAB_REQUIRE_NODE",
            LocateUp("tests", "plugins", "NodeFormat"), Path.Combine("plugin", "main.js"),
            "plugin/main.js",
            () => Environment.SetEnvironmentVariable("NODE_PATH", facade));
    }

    [Fact(Timeout = 60_000)]
    public void LuaFacadeShakesHands()
    {
        if (FindOnPath("lua") == null && FindOnPath("lua5.4") == null)
        {
            if (Environment.GetEnvironmentVariable("TUNELAB_REQUIRE_LUA") == "1")
                Assert.Fail("no lua on PATH");
            return;   // vacuous（windows CI/本地）
        }
        var exe = FindOnPath("lua") ?? FindOnPath("lua5.4")!;
        var facadeDir = LocateUp("scripting", "lua");
        Smoke("lua", exe, "TUNELAB_REQUIRE_LUA",
            LocateUp("tests", "plugins", "LuaFormat"), Path.Combine("plugin", "main.lua"),
            "plugin/main.lua",
            () => Environment.SetEnvironmentVariable("LUA_PATH", facadeDir + Path.DirectorySeparatorChar + "?.lua;;"));
    }
}
