using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace TuneLab.Extensions;

// per-folder ALC：每个插件文件夹一个加载上下文 = 隔离 + 依赖共享边界。
//   一包多插件 → 多个入口程序集装进同一个 ALC，共享基建程序集天然只加载一份、类型标识一致。
//
// 共享契约硬约束：契约程序集（TuneLab.Foundation + TuneLab.SDK.* + BCL）由 Default ALC 加载一份、
//   所有插件 ALC 共享 —— Load 对契约程序集返回 null 落 Default，保证跨边界同名 Type 相等；
//   否则同名 Type 跨 ALC 不相等，连同版本插件都要 marshaling（footgun）。
//   插件私有依赖（如 ONNX 托管/原生库）才走 AssemblyDependencyResolver + 目录探测，进各自 ALC。
//
// 非 collectible 起步：isCollectible 默认 false，吃下隔离全部好处而无泄漏/JIT 税。
//   切热卸载（免重启卸载/更新 UX）时只需传 isCollectible:true，加载/契约结构零改动。
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    public PluginLoadContext(string pluginDirectory, string? mainAssemblyPath = null, bool isCollectible = false)
        : base(name: Path.GetFileName(pluginDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), isCollectible: isCollectible)
    {
        mPluginDirectory = pluginDirectory;
        // resolver 基于主程序集旁的 .deps.json 解析私有依赖；无主程序集 / 无 deps.json 时为 null，退回目录探测。
        if (mainAssemblyPath != null && File.Exists(mainAssemblyPath))
            mResolver = new AssemblyDependencyResolver(mainAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;

        // 共享契约：返回 null 落 Default ALC（host 已加载一份），跨边界 Type 同一标识。
        if (name != null && IsSharedContract(name))
            return null;

        // 插件私有托管依赖：deps.json → 目录探测。
        var resolved = mResolver?.ResolveAssemblyToPath(assemblyName);
        if (resolved != null)
            return LoadFromAssemblyPath(resolved);

        if (name != null)
        {
            var candidate = Path.Combine(mPluginDirectory, name + ".dll");
            if (File.Exists(candidate))
                return LoadFromAssemblyPath(candidate);
        }

        return null; // 其余（BCL 等）落 Default 共享
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = mResolver?.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }

    // 契约程序集：Foundation + TuneLab.SDK（及 TuneLab.SDK.* 同族，如 SDK.Format）。其余走插件私有解析。
    static bool IsSharedContract(string assemblyName)
    {
        return assemblyName == "TuneLab.Foundation"
            || assemblyName == "TuneLab.SDK"
            || assemblyName.StartsWith("TuneLab.SDK.", StringComparison.Ordinal);
    }

    readonly string mPluginDirectory;
    readonly AssemblyDependencyResolver? mResolver;
}
