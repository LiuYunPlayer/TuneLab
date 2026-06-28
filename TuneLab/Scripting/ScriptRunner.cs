using System;
using System.Globalization;
using System.Text;
using System.Threading;
using Jint;
using Jint.Native;
using Jint.Native.Function;
using Jint.Runtime.Interop;
using TuneLab.Data;
using TuneLab.Foundation;

namespace TuneLab.Scripting;

// 一次脚本运行的结果。Ok=false 时 Error 为给脚本作者（含 agent 模型）的清晰错误说明。
// Committed=是否有改动落地成一个可撤销单位（出错/取消/被拦则原子回退，恒为 false）；Output=脚本 print/log 的捕获文本。
// Changes=本次尝试的改动数（成功时即已提交数；出错时为回退掉的数，仅供信息）。
// Blocked=脚本试图在别处 UI 操作进行中写工程而被拦（只读脚本不会）；调用方可据此等 Pushable 恢复后整段重跑。
internal readonly record struct ScriptRunResult(bool Ok, string? Error, string Output, string? ResultText, bool Committed, int Changes, bool Blocked);

// 运行资源上限——按触发源分流：agent 写的代码当失控保险丝（紧）；用户显式运行放宽（无时限、靠 Cancel 中止）。
internal readonly record struct ScriptLimits(TimeSpan? Timeout, int MaxStatements)
{
    // agent：紧上限，防模型写出失控循环。
    public static readonly ScriptLimits Agent = new(TimeSpan.FromSeconds(5), 5_000_000);
    // 用户显式运行（侧栏 / 菜单工具）：时限大幅放宽以容纳大批量任务，语句上限放大；仅作失控保险丝。
    // 注：脚本当前在 UI 线程同步跑，真正"不冻 UI 的可取消长任务"需后台执行（后续 phase），故此处仍保留有限时限兜底。
    public static readonly ScriptLimits Interactive = new(TimeSpan.FromSeconds(60), 200_000_000);
}

// 脚本运行的【独立宿主】：不依赖 agent。负责
//  ① 构造沙箱化的 Jint 引擎（不暴露 CLR、限递归/语句数/超时/内存）；
//  ② 注入动作面 `tl`（ScriptApp）与 print/log/console.log 输出捕获；
//  ③ 双模式分发：脚本定义了 getScriptInfo（=工具）则动作在 main()，否则整段脚本体即动作；
//  ④ 在最外层把"整段脚本 = 一次 Commit / 出错原子回退"与 merge 括号收口包起来（脚本语言面看不到这些）。
internal static class ScriptRunner
{
    const int MaxOutput = 16 * 1024;          // 捕获输出上限（防淹没）

    // 沙箱化引擎工厂（运行与元数据枚举共用）：不开 CLR；限递归/语句数/内存/超时；camelCase→PascalCase。
    internal static Engine CreateEngine(ScriptLimits limits, CancellationToken cancellationToken)
    {
        return new Engine(options =>
        {
            options.LimitRecursion(64);
            options.MaxStatements(limits.MaxStatements);
            if (limits.Timeout is { } timeout)
                options.TimeoutInterval(timeout);
            options.LimitMemory(64L * 1024 * 1024);
            options.CancellationToken(cancellationToken);
            // 让 JS 的 camelCase 成员名匹配 C# 的 PascalCase（tl.addNote → AddNote、note.pos → Pos、tl.language → Language）。
            options.SetTypeResolver(new TypeResolver { MemberNameComparer = StringComparer.OrdinalIgnoreCase });
        });
    }

    public static ScriptRunResult Run(IProject project, Func<IMidiPart?>? currentPart, Func<IQuantization?>? quantization, Func<string?>? language, Func<ScriptSelection?>? selection, Func<ScriptPianoSelection?>? pianoSelection, ScriptLimits limits, string code, CancellationToken cancellationToken)
    {
        // 写守卫不在入口、而下沉到首次写入（ScriptContext.EnsureWritable）：只读脚本即便在用户操作中途也畅通，只拦写。
        var context = new ScriptContext(project, currentPart, quantization, language, selection, pianoSelection);
        var output = new StringBuilder();
        string? resultText = null;
        string? error = null;
        bool blocked = false;

        try
        {
            var engine = CreateEngine(limits, cancellationToken);

            void Print(JsValue v)
            {
                if (output.Length < MaxOutput)
                    output.Append(Format(v)).Append('\n');
            }

            engine.SetValue("tl", new ScriptApp(context));
            engine.SetValue("print", Print);
            engine.SetValue("log", Print);
            engine.Execute("globalThis.console = { log: print, info: print, warn: print, error: print, debug: print };");

            // 顶层：工具脚本=定义出 getScriptInfo/main（约定无副作用）；普通脚本=整段即动作。
            var completion = engine.Evaluate(code);
            if (engine.GetValue("getScriptInfo") is Function)
            {
                // 工具脚本：动作在 main()。其返回值（若有）作为结果文本。
                var mainResult = engine.Invoke("main");
                if (mainResult is not null && !mainResult.IsUndefined() && !mainResult.IsNull())
                    resultText = Format(mainResult);
            }
            else if (completion is not null && !completion.IsUndefined() && !completion.IsNull())
            {
                resultText = Format(completion);
            }
        }
        catch (Jint.Runtime.JavaScriptException jse)
        {
            error = jse.Message;   // JS 层抛错（含语法/类型错误），通常带行号
        }
        catch (ScriptApiException ae)
        {
            error = ae.Message;    // API 用法/参数错误
        }
        catch (ScriptBlockedException be)
        {
            error = be.Message;    // 别处 UI 操作进行中、写被拦——调用方可 wait-retry
            blocked = true;
        }
        catch (Exception ex)
        {
            error = cancellationToken.IsCancellationRequested
                ? "script was cancelled."
                : ex.Message;      // 超时 / 递归 / 内存 / 语句数上限等
        }

        // 收口：成功且有改动 → 提交成一个可撤销单位；出错/取消/无改动 → 原子回退到跑脚本前的干净状态。
        bool committed = context.Finish(rollback: error != null);
        return new ScriptRunResult(error == null, error, output.ToString(), resultText, committed, context.ChangeCount, blocked);
    }

    internal static string Format(JsValue v)
    {
        if (v is null || v.IsUndefined()) return "undefined";
        if (v.IsNull()) return "null";
        if (v.IsString()) return v.AsString();
        if (v.IsNumber())
        {
            double d = v.AsNumber();
            return d == Math.Floor(d) && !double.IsInfinity(d)
                ? ((long)d).ToString(CultureInfo.InvariantCulture)
                : d.ToString(CultureInfo.InvariantCulture);
        }
        return v.ToString();
    }
}
