using System;
using System.IO;

namespace TuneLab.Extensions.Scripted;

internal enum ScriptedGateStatus { Ready, Skipped, Failed }

// 脚本条目加载门：结构校验 + deps 模式 + runtime 四级探测。Ready 之后由各 type 的注册支处理
// （M1: format 注册 RemoteFormat；voice/effect 的注册支随 M2/M3 落地，此前如实 Skipped）。
// 纯函数式判给（不 mutate 结果对象），ExtensionManager 据 Status 记账（skipped/failed/registered）
// 并回填 entry.Error。错误文案 = 作者看得懂的人话（英文，日志/异常约定）。
internal static class ScriptedEntryGate
{
    public readonly record struct Result(ScriptedGateStatus Status, string Message, RuntimeInfo Runtime);

    // locator 参数为测试注入点（假 prober/假扩展根，绝不在单测里跑真解释器）；生产走 Default。
    public static Result Evaluate(ExtensionInfo ext, string packageDir, RuntimeLocator? locator = null)
    {
        locator ??= RuntimeLocator.Default;

        // —— 结构：runtime 与 entry 成对；与 class/assembly 互斥（"声明即事实"，不静默择一）——
        var entry = (ext.entry ?? string.Empty).Trim();
        if (entry.Length == 0)
            return new(ScriptedGateStatus.Failed, "'runtime' declares a scripted entry but 'entry' (the in-package script path) is missing", default);
        if (!string.IsNullOrEmpty(ext.assembly) || ext.EffectiveClass.Length > 0)
            return new(ScriptedGateStatus.Failed, "an entry declares both 'runtime'/'entry' (scripted, out-of-process) and 'class'/'assembly' (in-process ALC); pick one", default);

        // entry 必须是包内相对路径（防 .. 逃逸）且文件存在——缺了就是包打错了，如实报。
        var fullPath = Path.GetFullPath(Path.Combine(packageDir, entry.Replace('/', Path.DirectorySeparatorChar)));
        var root = Path.GetFullPath(packageDir) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.Ordinal))
            return new(ScriptedGateStatus.Failed, string.Format("'entry' must be a path inside the package folder, got '{0}'", entry), default);
        if (!File.Exists(fullPath))
            return new(ScriptedGateStatus.Failed, string.Format("'entry' script not found in the package: '{0}'", entry), default);

        // —— deps 模式（§12.2）：vendored 隐式；pip 已声明未实现（Skipped，非错误）——
        var deps = (ext.deps ?? string.Empty).Trim().ToLowerInvariant();
        if (deps.Length > 0 && deps != "vendored")
        {
            if (deps == "pip")
                return new(ScriptedGateStatus.Skipped, "'deps': \"pip\" mode is declared but not implemented by this host build (author-vendored dependencies only for now)", default);
            return new(ScriptedGateStatus.Failed, string.Format("'deps': unknown mode '{0}' (supported: vendored)", deps), default);
        }

        // —— runtime 声明与四级探测（§8.1/§8.2）——
        if (!RuntimeSpec.TryParse(ext.runtime, out var spec, out var specError))
            return new(ScriptedGateStatus.Failed, specError!, default);
        if (!locator.TryResolve(spec, out var info, out var trail))
            return new(ScriptedGateStatus.Skipped, string.Format(
                "no matching runtime: {0}. To provide one, install a 'runtime' package (type \"runtime\"), set '{1}' in settings, or put a matching interpreter on PATH.",
                trail, RuntimeLocator.SettingsKeyFor(spec.Flavor)), default);

        // runtime 就位：交给调用方按 kind 注册（format=M1；voice/effect 随 M2/M3 的注册支，此前如实 Skipped）。
        return new(ScriptedGateStatus.Ready, string.Format("runtime resolved: {0} {1} at '{2}'", info.Flavor, info.Version, info.Exe), info);
    }
}
