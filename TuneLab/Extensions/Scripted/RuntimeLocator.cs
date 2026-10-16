using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TuneLab.Configs;

namespace TuneLab.Extensions.Scripted;

// 一次真跑核验的结果（成功时带 RuntimeInfo；失败时 reason 是人话，进探测轨迹）。
internal readonly record struct ProbeOutcome(RuntimeInfo? Info, string? Reason);

internal interface IRuntimeProber
{
    // exe + 额外参数（py launcher 用 ["-3"]，普通候选为 null）。超时/坏输出 = 核验不过（防商店跳转桩与 shim）。
    ProbeOutcome Probe(string exe, string[]? extraArgs, string flavor);
}

// 解释器定位（设计文档 §8.1 四级探测 + §8.2 真跑核验）。加载期纯发现，不 spawn sidecar。
// 测试注入假 prober；生产用 ProcessProber（真跑、2s 超时、结果按 (exe,mtime) 缓存）。
internal sealed class RuntimeLocator
{
    public static readonly RuntimeLocator Default = new(new ProcessProber());

    readonly IRuntimeProber mProber;
    readonly Func<string> mSettingsPython;
    readonly Func<string> mSettingsNode;
    readonly Func<string> mSettingsLua;
    readonly Func<string, string?> mEnv;
    readonly Func<string> mExtensionsRoot;
    readonly Dictionary<string, ProbeOutcome> mCache = new();
    readonly object mLock = new();

    public RuntimeLocator(IRuntimeProber prober,
        Func<string>? settingsPython = null, Func<string>? settingsNode = null, Func<string>? settingsLua = null,
        Func<string, string?>? env = null, Func<string>? extensionsRoot = null)
    {
        mProber = prober;
        mSettingsPython = settingsPython ?? (() => SettingsRegistry.ScriptedRuntimePython.Property.Value);
        mSettingsNode = settingsNode ?? (() => SettingsRegistry.ScriptedRuntimeNode.Property.Value);
        mSettingsLua = settingsLua ?? (() => SettingsRegistry.ScriptedRuntimeLua.Property.Value);
        mEnv = env ?? Environment.GetEnvironmentVariable;
        mExtensionsRoot = extensionsRoot ?? (() => PathManager.ExtensionsFolder);
    }

    public const string EnvPython = "TUNELAB_PYTHON";
    public const string EnvNode = "TUNELAB_NODE";
    public const string EnvLua = "TUNELAB_LUA";
    public const string EnvLuaJit = "TUNELAB_LUAJIT";

    // 探测 env/settings 串的候选分隔符（多候选按序尝试）。
    static readonly char[] sSemicolon = [';'];

    // 返回 true 时 info 有效；false 时 trail = 多行轨迹（每级一行，含"为什么不算"），直接进 Skipped 文案。
    public bool TryResolve(RuntimeSpec spec, out RuntimeInfo info, out string trail)
    {
        var sb = new StringBuilder();
        info = default;
        trail = string.Empty;

        // ① 设置显式路径
        var settingsValue = spec.Flavor switch
        {
            "python" => mSettingsPython(),
            "node" => mSettingsNode(),
            _ => mSettingsLua(),
        };
        var candidates = Split(settingsValue);
        if (candidates.Count == 0)
            sb.Append(string.Format(CultureInfo.InvariantCulture, "settings ({0}: empty); ", SettingsKeyFor(spec.Flavor)));
        else if (TryEach(candidates, null, spec, out info, sb, "settings"))
            return Accept(sb, out trail);

        // ② 环境变量（多候选）
        var envName = spec.Flavor switch { "python" => EnvPython, "node" => EnvNode, "luajit" => EnvLuaJit, _ => EnvLua };
        candidates = Split(mEnv(envName));
        if (candidates.Count == 0)
            sb.Append(string.Format(CultureInfo.InvariantCulture, "env {0} (unset); ", envName));
        else if (TryEach(candidates, null, spec, out info, sb, "env"))
            return Accept(sb, out trail);

        // ③ runtime 资源包（type:"runtime"，平台过滤 + 区间匹配；§8.4）
        var packageCandidates = EnumerateRuntimePackageExes(spec.Flavor, mExtensionsRoot());
        if (packageCandidates.Count == 0)
            sb.Append("installed runtime packages (none matching); ");
        else if (TryEach(packageCandidates, null, spec, out info, sb, "runtime package"))
            return Accept(sb, out trail);

        // ④ 系统 PATH 候选名（Windows python 先试 py launcher）。net8 无 Environment.SearchPathForExecutable，
        // 自备 ResolveFromPath（PATH 目录 × 名字，Windows 再走 PATHEXT）。
        if (spec.Flavor == "python" && OperatingSystem.IsWindows())
        {
            if (TryProbeResolved("py", ["-3"], spec, out info, sb, "PATH 'py -3'"))
                return Accept(sb, out trail);
        }
        if (TryEach(PathCandidates(spec.Flavor), null, spec, out info, sb, "PATH"))
            return Accept(sb, out trail);

        trail = sb.ToString().TrimEnd(' ', ';');
        return false;
    }

    // 成功出口也回填 trail（含"被拒候选 + 最终接受者"，供日志/侧栏 tooltip 完整还原因果）。
    static bool Accept(StringBuilder sb, out string trail)
    {
        trail = sb.ToString().TrimEnd(' ', ';');
        return true;
    }

    bool TryEach(List<string> candidates, string[]? extraArgs, RuntimeSpec spec, out RuntimeInfo info, StringBuilder sb, string source)
    {
        foreach (var c in candidates)
        {
            if (TryProbeResolved(c, extraArgs, spec, out info, sb, source))
                return true;
        }
        info = default;
        return false;
    }

    bool TryProbeResolved(string exe, string[]? extraArgs, RuntimeSpec spec, out RuntimeInfo info, StringBuilder sb, string source)
    {
        var resolved = Path.IsPathRooted(exe) ? exe : ResolveFromPath(exe);
        if (resolved == null || !File.Exists(resolved))
        {
            info = default;
            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0} '{1}' rejected: not found; ", source, exe));
            return false;
        }
        if (!ProbeCached(resolved, extraArgs, spec.Flavor, out var outcome))
        {
            info = default;
            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0} '{1}' rejected: {2}; ", source, Path.GetFileName(resolved), outcome.Reason));
            return false;
        }
        info = outcome.Info!.Value;
        if (!spec.SatisfiedBy(info.Version))
        {
            sb.Append(string.Format(CultureInfo.InvariantCulture, "{0} '{1}' rejected: version {2} does not satisfy {3}; ", source, Path.GetFileName(resolved), info.Version, spec.Describe()));
            return false;
        }
        sb.Append(string.Format(CultureInfo.InvariantCulture, "{0} '{1}' accepted ({2}).", source, Path.GetFileName(resolved), info.Version));
        return true;
    }

    // 核验缓存：键 = exe 完整路径 + extraArgs + 文件 mtime（换版即失效重跑）。
    bool ProbeCached(string exe, string[]? extraArgs, string flavor, out ProbeOutcome outcome)
    {
        long mtime;
        try { mtime = File.GetLastWriteTimeUtc(exe).Ticks; }
        catch { mtime = 0; }
        var key = string.Concat(exe, "|", flavor, "|", string.Join(",", extraArgs ?? []), "|", mtime.ToString(CultureInfo.InvariantCulture));
        lock (mLock)
        {
            if (mCache.TryGetValue(key, out outcome))
                return outcome.Info != null;
        }
        outcome = mProber.Probe(exe, extraArgs, flavor);
        lock (mLock)
            mCache[key] = outcome;
        return outcome.Info != null;
    }

    static List<string> Split(string? value)
        => string.IsNullOrEmpty(value) ? [] : value.Split(sSemicolon, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    internal static string SettingsKeyFor(string flavor)
        => flavor switch { "python" => nameof(SettingsRegistry.ScriptedRuntimePython), "node" => nameof(SettingsRegistry.ScriptedRuntimeNode), _ => nameof(SettingsRegistry.ScriptedRuntimeLua) };

    static string? ResolveFromPath(string name)
    {
        if (Path.IsPathRooted(name))
            return File.Exists(name) ? name : null;
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var exts = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : new[] { "" };
        foreach (var dirRaw in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string dir;
            try { dir = Path.GetFullPath(dirRaw.Trim()); } catch { continue; }
            if (!Directory.Exists(dir))
                continue;
            foreach (var ext in exts)
            {
                var candidate = Path.Combine(dir, name + ext);
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    static List<string> PathCandidates(string flavor) => flavor switch
    {
        "python" => ["python", "python3"],
        "node" => ["node"],
        "lua" => OperatingSystem.IsWindows() ? ["lua", "lua54"] : ["lua5.4", "lua"],
        "luajit" => ["luajit", "luajit-5.1"],
        _ => [],
    };

    // ③ 级：扫已装 runtime 资源包（type:"runtime" + flavor 对 + 平台含当前 RID + 区间满足），高版本优先。
    // 宽容解析：坏 manifest / 缺字段的包跳过并记 warning（不影响其他包）。public 供 M1 下载按钮查重。
    public static List<string> EnumerateRuntimePackageExes(string flavor, string extensionsRoot)
    {
        var list = new List<(string Exe, Version V)>();
        foreach (var pkg in EnumerateRuntimePackages(extensionsRoot))
        {
            if (!string.Equals(pkg.Flavor, flavor, StringComparison.OrdinalIgnoreCase))
                continue;
            var exe = ResolvePackageExe(pkg.Directory, flavor);
            if (exe == null)
                continue;
            list.Add((exe, pkg.Version));
        }
        return list.OrderByDescending(x => x.V).Select(x => x.Exe).ToList();
    }

    internal readonly record struct InstalledRuntimePackage(string Directory, string Id, string Flavor, Version Version);

    public static List<InstalledRuntimePackage> EnumerateRuntimePackages(string extensionsRoot)
    {
        var result = new List<InstalledRuntimePackage>();
        if (!Directory.Exists(extensionsRoot))
            return result;
        foreach (var dir in Directory.GetDirectories(extensionsRoot))
        {
            var manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath))
                continue;
            try
            {
                using var stream = File.OpenRead(manifestPath);
                var m = JsonSerializer.Deserialize<ExtensionManifest>(stream);
                if (m == null || !string.Equals(m.type, "runtime", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!m.IsPlatformAvailable())
                    continue;
                var flavor = (m.flavor ?? string.Empty).Trim().ToLowerInvariant();
                if (flavor is not ("python" or "node" or "lua" or "luajit"))
                    continue;
                // runtime-version 缺失/畸形 = 不是可用的 runtime 包（勿把空串喂成 "0.0" 版本参与竞争）。
                var versionText = (m.runtimeVersion ?? string.Empty).Trim();
                if (versionText.Length == 0 || !Version.TryParse(NormalizeVersion(versionText), out var v))
                    continue;
                result.Add(new InstalledRuntimePackage(dir, m.id ?? Path.GetFileName(dir), flavor, v));
            }
            catch
            {
                // 坏包：本视图宽容跳过（LoadV1 主循环会如实报它的加载错误，两处判据不冲突）。
            }
        }
        return result;
    }

    // runtime 包 exe 布局（pack-runtime 工具产出，见 tools/build-runtime-package/README.md）：
    //   win：runtime\<exe>；unix：runtime\bin\<exe>。
    static string? ResolvePackageExe(string packageDir, string flavor)
    {
        string[] names = flavor switch
        {
            "python" => ["python.exe", "python3", "python"],
            "node" => ["node.exe", "node"],
            "lua" => ["lua.exe", "lua54", "lua", "lua5.4"],
            "luajit" => ["luajit.exe", "luajit"],
            _ => [],
        };
        foreach (var name in names)
        {
            var root = Path.Combine(packageDir, "runtime", name);
            if (File.Exists(root))
                return root;
            var bin = Path.Combine(packageDir, "runtime", "bin", name);
            if (File.Exists(bin))
                return bin;
        }
        return null;
    }

    internal static string NormalizeVersion(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return "0.0";
        var cut = s.IndexOfAny(['-', '+', ' ']);
        return cut < 0 ? s : s[..cut];
    }
}

// 生产 prober：真跑核验（§8.2：固定参数、2s 超时、stdout 两要素）。跑不过即"不算"——
// 商店跳转桩（exit 9009 无输出）、nvm/fnm shim（坏输出）在这层自然出局。
internal sealed class ProcessProber : IRuntimeProber
{
    const int TimeoutMs = 2000;

    public ProbeOutcome Probe(string exe, string[]? extraArgs, string flavor)
    {
        (string[]? Args, Func<string, (string, string)>? Parser) probe = flavor switch
        {
            "python" => (new[] { "-c", "import sys,platform;sys.stdout.write('.'.join(map(str,sys.version_info[:3]))+chr(10)+platform.machine())" }, (Func<string, (string, string)>)ParsePython),
            "node" => (new[] { "-p", "process.version.slice(1)+'\\n'+process.arch" }, (Func<string, (string, string)>)ParseNode),
            "lua" or "luajit" => (new[] { "-e", "local v=_VERSION:gsub('^Lua%s+','') local j=jit and jit.version and jit.version:gsub('^[^%d]-','') or '' if j~='' then io.write(j..'|luajit') else io.write(v..'|lua') end" }, (Func<string, (string, string)>)ParseLua),
            _ => (null, null),
        };
        var args = probe.Args;
        var parser = probe.Parser;
        if (args == null || parser == null)
            return new(null, "unknown flavor");
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            if (extraArgs != null)
                foreach (var a in extraArgs)
                    psi.ArgumentList.Add(a);
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            if (proc == null)
                return new(null, "failed to start");
            var stdout = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(TimeoutMs))
            {
                try { proc.Kill(true); } catch { }
                return new(null, "probe timed out (2s)");
            }
            if (proc.ExitCode != 0)
                return new(null, string.Format(CultureInfo.InvariantCulture, "probe exited with code {0}", proc.ExitCode));
            var (version, detectedFlavor) = parser(stdout.Trim());
            if (version.Length == 0)
                return new(null, "unrecognized probe output");
            // luajit 冒充 lua（_VERSION=5.1）时按 flavor 区间自然裁决；flavor=="luajit" 必须是 luajit。
            if (flavor == "luajit" && detectedFlavor != "luajit")
                return new(null, string.Format(CultureInfo.InvariantCulture, "'{0}' is {1}, not luajit", Path.GetFileName(exe), detectedFlavor));
            var arch = flavor is "python" or "node" ? ArchOf(stdout) : "";
            return new(new RuntimeInfo(exe, detectedFlavor.Length > 0 ? detectedFlavor : flavor, version, arch), null);
        }
        catch (Exception ex)
        {
            return new(null, ex.Message);
        }
    }

    static (string, string) ParsePython(string output)
    {
        var lines = output.Split('\n');
        return lines.Length >= 1 ? (RuntimeLocator.NormalizeVersion(lines[0].Trim()), "python") : ("", "");
    }

    static (string, string) ParseNode(string output)
    {
        var lines = output.Split('\n');
        return lines.Length >= 1 ? (RuntimeLocator.NormalizeVersion(lines[0].Trim()), "node") : ("", "");
    }

    static (string, string) ParseLua(string output)
    {
        var parts = output.Split('|');
        return parts.Length == 2 ? (RuntimeLocator.NormalizeVersion(parts[0].Trim()), parts[1].Trim()) : ("", "");
    }

    static string ArchOf(string output)
    {
        var lines = output.Split('\n');
        return lines.Length >= 2 ? lines[1].Trim() : "";
    }
}
