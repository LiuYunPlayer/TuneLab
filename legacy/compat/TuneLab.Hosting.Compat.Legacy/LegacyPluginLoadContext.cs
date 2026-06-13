using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace TuneLab.Hosting.Compat.Legacy;

// per-plugin ALC：每个 Legacy 包一个隔离加载上下文，根除野外 voice 引擎各自捆绑的冲突原生依赖
//   （ONNX/原生运行时不同版本）——这是必要而非可选。
//
// 共享契约硬约束：Legacy 冻结三程序集（Base/Extensions.Formats/Extensions.Voices）+ V1 SDK 由 Default ALC
//   加载一份、所有插件 ALC 共享（Load 对契约返回 null 落 Default）——Compat 适配器已在 Default 加载这些，
//   故插件返回的 Legacy 类型与适配器看到的是同一 Type 标识，无需 marshaling。
//   插件私有依赖（ONNX 托管/原生库）才进各自 ALC（AssemblyDependencyResolver + 目录探测）。
//
// 非 collectible 起步：isCollectible 默认 false，吃下隔离全部好处而无泄漏/JIT 税；
//   切热卸载（免重启卸载 UX）时只需传 isCollectible:true，加载/契约结构零改动。
internal sealed class LegacyPluginLoadContext : AssemblyLoadContext
{
    public LegacyPluginLoadContext(string pluginDirectory, string? mainAssemblyPath = null, bool isCollectible = false)
        : base(name: "legacy:" + Path.GetFileName(pluginDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), isCollectible: isCollectible)
    {
        mPluginDirectory = pluginDirectory;
        // 老插件多为 xcopy 无 .deps.json：resolver 多半为 null，退回目录探测。
        if (mainAssemblyPath != null && File.Exists(mainAssemblyPath))
        {
            try { mResolver = new AssemblyDependencyResolver(mainAssemblyPath); }
            catch { mResolver = null; }
        }
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name;

        // 共享契约 → 返回 null 落 Default ALC，跨边界 Type 同一标识。
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
        if (path != null)
            return LoadUnmanagedDllFromPath(path);

        // 无 deps.json：在插件目录探测原生 dll（ONNX runtime 等）；探测失败落默认解析、最终优雅返回 Zero。
        foreach (var candidate in NativeCandidates(unmanagedDllName))
        {
            if (File.Exists(candidate))
                return LoadUnmanagedDllFromPath(candidate);
        }

        return IntPtr.Zero;
    }

    System.Collections.Generic.IEnumerable<string> NativeCandidates(string unmanagedDllName)
    {
        yield return Path.Combine(mPluginDirectory, unmanagedDllName);

        string prefix, ext;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) { prefix = string.Empty; ext = ".dll"; }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) { prefix = "lib"; ext = ".dylib"; }
        else { prefix = "lib"; ext = ".so"; }

        var bare = unmanagedDllName;
        if (!bare.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            yield return Path.Combine(mPluginDirectory, prefix + bare + ext);
    }

    // 契约：Legacy 冻结三件 + Foundation + TuneLab.SDK（前缀匹配兼容将来可能再拆的 SDK.* 同族）。其余走插件私有解析。
    static bool IsSharedContract(string name)
        => name == "TuneLab.Base"
        || name == "TuneLab.Extensions.Formats"
        || name == "TuneLab.Extensions.Voices"
        || name == "TuneLab.Foundation"
        || name == "TuneLab.SDK"
        || name.StartsWith("TuneLab.SDK.", StringComparison.Ordinal);

    readonly string mPluginDirectory;
    readonly AssemblyDependencyResolver? mResolver;
}
