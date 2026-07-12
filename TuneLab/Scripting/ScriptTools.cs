using System;
using System.Collections.Generic;
using Jint.Native;
using Jint.Native.Function;
using TuneLab.Data;
using TuneLab.Foundation;

namespace TuneLab.Scripting;

// 脚本工具挂载的上下文：决定注册到哪个菜单、对应不同的目标心智。每个编辑区的右键分支各自闭合：
//  global=顶部 Scripts 菜单；
//  钢琴窗三分支：note=命中音符 / partContent=命中空白（part 内容区）/ pianoSelection=命中范围选区（tick 带，tl.pianoSelection()）；
//  编排区三分支 + 轨道头：part=命中 part / trackContent=空白泳道 / trackSelection=命中范围选区（tick×轨道，tl.trackSelection()）/ track=轨道头。
internal enum ScriptToolContext { Global, Note, Part, PartContent, Track, TrackContent, PianoSelection, TrackSelection }

// 一个"工具脚本"（定义了 getScriptInfo 的库内脚本）的元数据。ScriptName=库内文件名（去扩展），用于加载/运行；
// 其余来自 getScriptInfo() 的返回。亮灭(enabled)不做——它只是 UI 糖、不防逻辑错误，真正校验在脚本 main() 里。
// DeclaredId/DefaultGesture 是快捷键系统用的原始声明串（未校验）：稳定绑定锚点与建议默认手势，
// 校验/解析归 UI 侧（ScriptToolMenu），本层只忠实透出作者声明。见 docs/keybinding-system.md §6。
internal sealed record ScriptToolInfo(
    string ScriptName,
    string DisplayName,
    string? Category,
    string? Author,
    string? Version,
    ScriptToolContext Context,
    string? DeclaredId = null,
    string? DefaultGesture = null);

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

        var (info, error) = InspectSource(name, code, project, currentPart, quantization, language);
        if (error != null)
            Log.Warning(string.Format("Script \"{0}\" getScriptInfo failed; skipped from tools: {1}", name, error));
        return info;
    }

    // 解析一段脚本【源码】的工具元数据（不保存、不跑 main）。供枚举与 agent 的 save_script 预校验共用。返回 (info, error)：
    //  · info!=null            → 合法工具脚本；
    //  · info==null,error==null → 非工具脚本（没有 getScriptInfo）；
    //  · error!=null            → 有 getScriptInfo 但 eval/解析失败（消息回报调用方）。
    // 顶层在沙箱里 eval（约定无副作用）、调一次 getScriptInfo 取字段；任何意外数据改动在 finally 原子回退。
    public static (ScriptToolInfo? Info, string? Error) InspectSource(string scriptName, string code, IProject project, Func<IMidiPart?>? currentPart, Func<IQuantization?>? quantization, Func<string?>? language)
    {
        // 快速排除：不含 "getScriptInfo" 字样必非工具，不 eval（避免跑普通脚本顶层=其动作）。
        if (string.IsNullOrEmpty(code) || !code.Contains("getScriptInfo")) return (null, null);

        var context = new ScriptContext(project, currentPart, quantization, language, null, null);   // 选区(编排区/钢琴窗)与元数据无关
        try
        {
            var engine = ScriptRunner.CreateEngine(ScriptLimits.Agent, default);
            engine.SetValue("tl", new ScriptApp(context));
            engine.SetValue("print", (Action<JsValue>)(_ => { }));
            engine.SetValue("log", (Action<JsValue>)(_ => { }));
            engine.Execute("globalThis.console = { log: print, info: print, warn: print, error: print, debug: print };");

            engine.Execute(code);   // 顶层：约定只定义函数、无副作用
            if (engine.GetValue("getScriptInfo") is not Function)
                return (null, null);   // 不是工具脚本

            var infoVal = engine.Invoke("getScriptInfo");
            var o = ScriptArgs.Obj(infoVal, "getScriptInfo() result");
            string display = ScriptArgs.OptStr(o, "name") is { Length: > 0 } n ? n : scriptName;
            return (new ScriptToolInfo(
                ScriptName: scriptName,
                DisplayName: display,
                Category: ScriptArgs.OptStr(o, "category"),
                Author: ScriptArgs.OptStr(o, "author"),
                Version: ScriptArgs.OptStr(o, "version"),
                Context: ParseContext(ScriptArgs.OptStr(o, "context")),
                DeclaredId: ScriptArgs.OptStr(o, "id"),
                DefaultGesture: ScriptArgs.OptStr(o, "defaultGesture")), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
        finally
        {
            context.Finish(rollback: true);
        }
    }

    static ScriptToolContext ParseContext(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "note" => ScriptToolContext.Note,
        "part" => ScriptToolContext.Part,
        "partcontent" => ScriptToolContext.PartContent,
        "track" => ScriptToolContext.Track,
        "trackcontent" => ScriptToolContext.TrackContent,
        "pianoselection" => ScriptToolContext.PianoSelection,
        "trackselection" => ScriptToolContext.TrackSelection,
        _ => ScriptToolContext.Global,
    };
}
