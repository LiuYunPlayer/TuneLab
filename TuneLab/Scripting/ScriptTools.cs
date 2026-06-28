using System;
using System.Collections.Generic;
using Jint.Native.Function;
using TuneLab.Data;
using TuneLab.Foundation;

namespace TuneLab.Scripting;

// 脚本工具挂载的上下文：决定注册到哪个菜单。
//  global=顶部 Scripts 菜单；note=钢琴卷帘命中音符；part=编排区命中 part（整个对象）；partContent=钢琴卷帘空白（part 内容区）。
//  （track / trackContent 待轨道头交互升级——加 track 选中模型——后再做。）
internal enum ScriptToolContext { Global, Note, Part, PartContent }

// 一个"工具脚本"（定义了 getScriptInfo 的库内脚本）的元数据。ScriptName=库内文件名（去扩展），用于加载/运行；
// 其余来自 getScriptInfo() 的返回。亮灭(enabled)不做——它只是 UI 糖、不防逻辑错误，真正校验在脚本 main() 里。
internal sealed record ScriptToolInfo(
    string ScriptName,
    string DisplayName,
    string? Category,
    string? Author,
    string? Version,
    ScriptToolContext Context);

// 脚本工具枚举器：扫描脚本库，逐个在沙箱里 eval 顶层 + 调 getScriptInfo 收元数据。只有定义了 getScriptInfo 的脚本
// 才算"工具"、参与菜单注册；普通脚本（没有 getScriptInfo）不在此列。按 (文件 mtime, 语言) 缓存，避免每次重复 eval
// （语言进 key——显示名随语言变；mtime 变即重读）。元数据枚举恒原子回退，getScriptInfo 不应改工程、误改也丢弃。
internal static class ScriptTools
{
    // name -> (mtime, lang, info)；info=null 表示该脚本不是工具（缓存以免重复 eval 普通脚本）。
    static readonly Dictionary<string, (DateTime Mtime, string Lang, ScriptToolInfo? Info)> mCache = new();

    // 枚举库内全部工具脚本。project/providers/language 为枚举时注入脚本的上下文（getScriptInfo 通常只读 tl.language）。
    public static List<ScriptToolInfo> Discover(IProject project, Func<IMidiPart?>? currentPart, Func<IQuantization?>? quantization, Func<string?>? language)
    {
        string lang = language?.Invoke() ?? "";
        var result = new List<ScriptToolInfo>();
        var seen = new HashSet<string>();

        foreach (var name in ScriptLibrary.List())
        {
            seen.Add(name);
            var mtime = ScriptLibrary.GetLastWriteTimeUtc(name);
            if (mCache.TryGetValue(name, out var cached) && cached.Mtime == mtime && cached.Lang == lang)
            {
                if (cached.Info != null) result.Add(cached.Info);
                continue;
            }

            var info = TryReadInfo(name, project, currentPart, quantization, language);
            mCache[name] = (mtime, lang, info);
            if (info != null) result.Add(info);
        }

        // 清理已删除脚本的缓存项。
        if (mCache.Count > seen.Count)
            foreach (var stale in new List<string>(mCache.Keys))
                if (!seen.Contains(stale)) mCache.Remove(stale);

        return result;
    }

    // 让外部（如 Scripts 目录变化）强制下次重新枚举。
    public static void InvalidateCache() => mCache.Clear();

    static ScriptToolInfo? TryReadInfo(string name, IProject project, Func<IMidiPart?>? currentPart, Func<IQuantization?>? quantization, Func<string?>? language)
    {
        string code;
        try { code = ScriptLibrary.Read(name); }
        catch (Exception ex) { Log.Warning(string.Format("Failed to read script \"{0}\" for tool discovery: {1}", name, ex.Message)); return null; }

        // 快速排除：不含 "getScriptInfo" 字样的脚本必非工具，直接跳过——避免为枚举而 eval 普通脚本顶层（=其动作）。
        // 误报（注释/字符串里恰含此词）至多多 eval 一次、且改动会被下方 finally 原子回退，不影响正确性。
        if (!code.Contains("getScriptInfo")) return null;

        // 元数据枚举用紧上限当保险丝；不取消。
        var context = new ScriptContext(project, currentPart, quantization, language);
        try
        {
            var engine = ScriptRunner.CreateEngine(ScriptLimits.Agent, default);
            engine.SetValue("tl", new ScriptApp(context));
            engine.SetValue("print", (Action<Jint.Native.JsValue>)(_ => { }));
            engine.SetValue("log", (Action<Jint.Native.JsValue>)(_ => { }));
            engine.Execute("globalThis.console = { log: print, info: print, warn: print, error: print, debug: print };");

            engine.Execute(code);   // 顶层：约定只定义函数、无副作用
            if (engine.GetValue("getScriptInfo") is not Function)
                return null;        // 不是工具脚本

            var infoVal = engine.Invoke("getScriptInfo");
            var o = ScriptArgs.Obj(infoVal, "getScriptInfo() result");
            string display = ScriptArgs.OptStr(o, "name") is { Length: > 0 } n ? n : name;
            return new ScriptToolInfo(
                ScriptName: name,
                DisplayName: display,
                Category: ScriptArgs.OptStr(o, "category"),
                Author: ScriptArgs.OptStr(o, "author"),
                Version: ScriptArgs.OptStr(o, "version"),
                Context: ParseContext(ScriptArgs.OptStr(o, "context")));
        }
        catch (Exception ex)
        {
            Log.Warning(string.Format("Script \"{0}\" getScriptInfo failed; skipped from tools: {1}", name, ex.Message));
            return null;
        }
        finally
        {
            // 丢弃 getScriptInfo 期间的任何意外改动（只回退本次枚举新增的命令，不动别处未提交改动）。
            context.Finish(rollback: true);
        }
    }

    static ScriptToolContext ParseContext(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "note" => ScriptToolContext.Note,
        "part" => ScriptToolContext.Part,
        "partcontent" => ScriptToolContext.PartContent,
        _ => ScriptToolContext.Global,
    };
}
