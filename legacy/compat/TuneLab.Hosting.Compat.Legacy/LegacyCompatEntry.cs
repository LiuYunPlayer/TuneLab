using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using LFmt = TuneLab.Extensions.Formats;
using LVoice = TuneLab.Extensions.Voices;
using VFmt = TuneLab.SDK.Format;
using VVoice = TuneLab.SDK.Voice;
using TuneLab.Hosting.Compat.Legacy.Format;
using TuneLab.Hosting.Compat.Legacy.Voice;

namespace TuneLab.Hosting.Compat.Legacy;

// 反射加载入口（主程序对本程序集零编译依赖）：
//   宿主经 Assembly.LoadFrom 加载本 dll → 反射取 LegacyCompatEntry.TryLoad → 注入注册委托。
//   委托签名只用共享契约类型（SDK.Format / SDK.Voice），它们由 Default ALC 加载一份、跨边界同一 Type，
//   故反射 Invoke 的实参类型与宿主侧构造的委托精确匹配。adapter 全 internal，公开面仅本类 + V1 SDK 类型。
//
// 公开面刻意只此一个静态方法 + 全 BCL/SDK 类型参数（暴露的工厂接口）。
public static class LegacyCompatEntry
{
    // 加载一个 Legacy 包目录，把发现的老 format/voice 插件包成 V1 适配器、经委托注册进宿主。
    //   packagePath     —— 包文件夹（= ALC / 安装单位）。
    //   assemblies      —— description 显式声明的程序集（相对名）；空 → 扫目录全部 *.dll。
    //   addImporter/addExporter —— (扩展名, V1 IImportFormat/IExportFormat 适配器)。
    //   addVoiceEngine  —— (引擎 type, V1 IVoiceEngine 适配器, enginePath=包目录)。
    // 返回是否注册到任何插件；全程优雅降级，单个程序集/类型失败不影响其余、不抛给宿主。
    public static bool TryLoad(
        string packagePath,
        string[] assemblies,
        Action<string, VFmt.IImportFormat> addImporter,
        Action<string, VFmt.IExportFormat> addExporter,
        Action<string, VVoice.IVoiceEngine, string> addVoiceEngine,
        Action<string> log)
    {
        // 预热冻结契约程序集：主程序对它们无 ProjectReference（不在 Default ALC 的 TPA），
        // 故 Default 只能解析"已加载"的它们。若不预热，第一个被处理的插件在 Compat 尚未触碰
        // 这些类型前做 GetTypes 会解析失败（加载顺序竞态）。这里先把它们载入共享上下文。
        WarmUpContract();

        // per-plugin ALC：隔离野外 voice 引擎各自捆绑的冲突原生依赖（ONNX 等）。
        // 传首个声明程序集做 AssemblyDependencyResolver 锚点（有 deps.json 时辅助解析私有依赖）。
        var fileList = (assemblies != null && assemblies.Length > 0)
            ? assemblies.Select(a => Path.Combine(packagePath, a)).Where(File.Exists).ToList()
            : Directory.GetFiles(packagePath, "*.dll").ToList();

        var alc = new LegacyPluginLoadContext(packagePath, fileList.Count > 0 ? fileList[0] : null);

        bool any = false;
        var seenImport = new HashSet<string>();
        var seenExport = new HashSet<string>();
        var seenVoice = new HashSet<string>();

        log(string.Format("扫描 {0} 个程序集: {1}", fileList.Count, string.Join(", ", fileList.Select(Path.GetFileName))));

        foreach (var file in fileList)
        {
            Type[] types;
            try
            {
                var asm = alc.LoadFromAssemblyPath(Path.GetFullPath(file));
                types = SafeGetTypes(asm, log);
            }
            catch (Exception ex)
            {
                log(string.Format("加载程序集失败 {0}: {1}", Path.GetFileName(file), ex.Message));
                continue;
            }

            foreach (var type in types)
            {
                if (type is null || type.IsAbstract || type.IsInterface)
                    continue;

                try
                {
                    any |= TryFormat(type, addImporter, addExporter, seenImport, seenExport, log);
                    any |= TryVoice(type, packagePath, addVoiceEngine, seenVoice, log);
                }
                catch (Exception ex)
                {
                    log(string.Format("实例化/注册类型失败 {0}: {1}", type.FullName, ex.Message));
                }
            }
        }

        if (!any)
            log(string.Format("未发现可用的 Legacy format/voice 插件（包: {0}）", packagePath));
        return any;
    }

    static bool TryFormat(
        Type type,
        Action<string, VFmt.IImportFormat> addImporter,
        Action<string, VFmt.IExportFormat> addExporter,
        HashSet<string> seenImport,
        HashSet<string> seenExport,
        Action<string> log)
    {
        bool any = false;

        var import = type.GetCustomAttribute<LFmt.ImportFormatAttribute>();
        if (import != null)
        {
            if (!typeof(LFmt.IImportFormat).IsAssignableFrom(type))
                log(string.Format("{0} 带 [ImportFormat] 但未实现 IImportFormat（类型身份不匹配？）", type.FullName));
            else if (type.GetConstructor(Type.EmptyTypes) is not { } ctor)
                log(string.Format("import format {0} 缺少 public 无参构造函数", type.FullName));
            else if (seenImport.Add(import.FileExtension))
            {
                addImporter(import.FileExtension, new ImportFormatAdapter((LFmt.IImportFormat)ctor.Invoke(null)));
                log(string.Format("已注册 Legacy import format: .{0} ({1})", import.FileExtension, type.FullName));
                any = true;
            }
        }

        var export = type.GetCustomAttribute<LFmt.ExportFormatAttribute>();
        if (export != null)
        {
            if (!typeof(LFmt.IExportFormat).IsAssignableFrom(type))
                log(string.Format("{0} 带 [ExportFormat] 但未实现 IExportFormat（类型身份不匹配？）", type.FullName));
            else if (type.GetConstructor(Type.EmptyTypes) is not { } ctor)
                log(string.Format("export format {0} 缺少 public 无参构造函数", type.FullName));
            else if (seenExport.Add(export.FileExtension))
            {
                addExporter(export.FileExtension, new ExportFormatAdapter((LFmt.IExportFormat)ctor.Invoke(null)));
                log(string.Format("已注册 Legacy export format: .{0} ({1})", export.FileExtension, type.FullName));
                any = true;
            }
        }

        return any;
    }

    static bool TryVoice(
        Type type,
        string packagePath,
        Action<string, VVoice.IVoiceEngine, string> addVoiceEngine,
        HashSet<string> seenVoice,
        Action<string> log)
    {
        var attribute = type.GetCustomAttribute<LVoice.VoiceEngineAttribute>();
        if (attribute == null)
            return false;

        if (!typeof(LVoice.IVoiceEngine).IsAssignableFrom(type))
        {
            log(string.Format("{0} 带 [VoiceEngine] 但未实现 IVoiceEngine（类型身份不匹配？）", type.FullName));
            return false;
        }
        if (type.GetConstructor(Type.EmptyTypes) is not { } ctor)
        {
            log(string.Format("voice 引擎 {0} 缺少 public 无参构造函数", type.FullName));
            return false;
        }
        if (!seenVoice.Add(attribute.Type))
            return false;

        addVoiceEngine(attribute.Type, new VoiceEngineAdapter((LVoice.IVoiceEngine)ctor.Invoke(null), packagePath), packagePath);
        log(string.Format("已注册 Legacy voice 引擎: {0} ({1})", attribute.Type, type.FullName));
        return true;
    }

    static bool sWarmedUp;
    static void WarmUpContract()
    {
        if (sWarmedUp)
            return;
        sWarmedUp = true;
        // 触碰各冻结契约程序集的一个类型，确保它们先于任何插件 GetTypes 载入共享上下文。
        _ = typeof(LFmt.ImportFormatAttribute).Assembly;     // TuneLab.Extensions.Formats
        _ = typeof(LVoice.VoiceEngineAttribute).Assembly;    // TuneLab.Extensions.Voices
        _ = typeof(TuneLab.Base.Structures.Point).Assembly;  // TuneLab.Base
    }

    static Type[] SafeGetTypes(Assembly assembly, Action<string> log)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            foreach (var le in ex.LoaderExceptions.Where(e => e != null).Select(e => e!.Message).Distinct())
                log(string.Format("类型加载异常 in {0}: {1}", assembly.GetName().Name, le));
            return ex.Types.Where(t => t != null).ToArray()!;
        }
    }
}
