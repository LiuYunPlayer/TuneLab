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
        Action<string, VVoice.IVoiceEngine, string> addVoiceEngine)
    {
        // per-plugin ALC：隔离野外 voice 引擎各自捆绑的冲突原生依赖（ONNX 等）。
        var alc = new LegacyPluginLoadContext(packagePath);

        IEnumerable<string> files = (assemblies != null && assemblies.Length > 0)
            ? assemblies.Select(a => Path.Combine(packagePath, a)).Where(File.Exists)
            : Directory.GetFiles(packagePath, "*.dll");

        bool any = false;
        var seenImport = new HashSet<string>();
        var seenExport = new HashSet<string>();
        var seenVoice = new HashSet<string>();

        foreach (var file in files)
        {
            Type[] types;
            try
            {
                var asm = alc.LoadFromAssemblyPath(Path.GetFullPath(file));
                types = SafeGetTypes(asm);
            }
            catch
            {
                continue; // 非托管/损坏 dll 等：跳过，优雅降级。
            }

            foreach (var type in types)
            {
                if (type is null || type.IsAbstract || type.IsInterface)
                    continue;

                try
                {
                    any |= TryFormat(type, addImporter, addExporter, seenImport, seenExport);
                    any |= TryVoice(type, packagePath, addVoiceEngine, seenVoice);
                }
                catch
                {
                    // 单类型实例化/注册失败不影响其余。
                }
            }
        }

        return any;
    }

    static bool TryFormat(
        Type type,
        Action<string, VFmt.IImportFormat> addImporter,
        Action<string, VFmt.IExportFormat> addExporter,
        HashSet<string> seenImport,
        HashSet<string> seenExport)
    {
        bool any = false;

        var import = type.GetCustomAttribute<LFmt.ImportFormatAttribute>();
        if (import != null && typeof(LFmt.IImportFormat).IsAssignableFrom(type))
        {
            var ctor = type.GetConstructor(Type.EmptyTypes);
            if (ctor != null && seenImport.Add(import.FileExtension))
            {
                var legacy = (LFmt.IImportFormat)ctor.Invoke(null);
                addImporter(import.FileExtension, new ImportFormatAdapter(legacy));
                any = true;
            }
        }

        var export = type.GetCustomAttribute<LFmt.ExportFormatAttribute>();
        if (export != null && typeof(LFmt.IExportFormat).IsAssignableFrom(type))
        {
            var ctor = type.GetConstructor(Type.EmptyTypes);
            if (ctor != null && seenExport.Add(export.FileExtension))
            {
                var legacy = (LFmt.IExportFormat)ctor.Invoke(null);
                addExporter(export.FileExtension, new ExportFormatAdapter(legacy));
                any = true;
            }
        }

        return any;
    }

    static bool TryVoice(
        Type type,
        string packagePath,
        Action<string, VVoice.IVoiceEngine, string> addVoiceEngine,
        HashSet<string> seenVoice)
    {
        var attribute = type.GetCustomAttribute<LVoice.VoiceEngineAttribute>();
        if (attribute == null || !typeof(LVoice.IVoiceEngine).IsAssignableFrom(type))
            return false;

        var ctor = type.GetConstructor(Type.EmptyTypes);
        if (ctor == null || !seenVoice.Add(attribute.Type))
            return false;

        var legacy = (LVoice.IVoiceEngine)ctor.Invoke(null);
        addVoiceEngine(attribute.Type, new VoiceEngineAdapter(legacy), packagePath);
        return true;
    }

    static Type[] SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null).ToArray()!;
        }
    }
}
