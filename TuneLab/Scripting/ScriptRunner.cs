using System;
using System.Globalization;
using System.Text;
using System.Threading;
using Jint;
using Jint.Native;
using Jint.Runtime.Interop;
using TuneLab.Data;
using TuneLab.Foundation;

namespace TuneLab.Scripting;

// 一次脚本运行的结果。Ok=false 时 Error 为给脚本作者（含 agent 模型）的清晰错误说明。
// Committed=出错前/正常结束时是否有改动落地成一个可撤销单位；Output=脚本 print/log 的捕获文本。
internal readonly record struct ScriptRunResult(bool Ok, string? Error, string Output, string? ResultText, bool Committed, int Changes);

// 脚本运行的【独立宿主】：不依赖 agent。负责
//  ① 构造沙箱化的 Jint 引擎（不暴露 CLR、限递归/语句数/超时/内存）；
//  ② 注入动作面 `tl`（ScriptProjectApi）与 print/log/console.log 输出捕获；
//  ③ 在最外层把"整段脚本 = 一次 Commit"与 merge 括号收口包起来（脚本语言面看不到这些危险操作）。
internal static class ScriptRunner
{
    const int MaxOutput = 16 * 1024;          // 捕获输出上限（防淹没）
    const int MaxStatements = 5_000_000;      // 语句数上限（截断死循环）
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static ScriptRunResult Run(IProject project, Func<IMidiPart?>? currentPart, Func<IQuantization?>? quantization, string code, CancellationToken cancellationToken)
    {
        var api = new ScriptProjectApi(project, currentPart, quantization);
        var output = new StringBuilder();
        string? resultText = null;
        string? error = null;

        try
        {
            var engine = new Engine(options =>
            {
                options.LimitRecursion(64);
                options.MaxStatements(MaxStatements);
                options.TimeoutInterval(Timeout);
                options.LimitMemory(64L * 1024 * 1024);
                options.CancellationToken(cancellationToken);
                // 让 JS 的 camelCase 成员名匹配 C# 的 PascalCase（tl.addNote → AddNote、note.pos → Pos）。
                options.SetTypeResolver(new TypeResolver { MemberNameComparer = StringComparer.OrdinalIgnoreCase });
            });

            void Print(JsValue v)
            {
                if (output.Length < MaxOutput)
                    output.Append(Format(v)).Append('\n');
            }

            engine.SetValue("tl", api);
            engine.SetValue("print", Print);
            engine.SetValue("log", Print);
            engine.Execute("globalThis.console = { log: print, info: print, warn: print, error: print, debug: print };");

            var completion = engine.Evaluate(code);
            if (completion is not null && !completion.IsUndefined() && !completion.IsNull())
                resultText = Format(completion);
        }
        catch (Jint.Runtime.JavaScriptException jse)
        {
            error = jse.Message;   // JS 层抛错（含语法/类型错误），通常带行号
        }
        catch (ScriptApiException ae)
        {
            error = ae.Message;    // API 用法/参数错误
        }
        catch (Exception ex)
        {
            error = cancellationToken.IsCancellationRequested
                ? "script was cancelled."
                : ex.Message;      // 超时 / 递归 / 内存 / 语句数上限等
        }

        // 无论成功/失败/取消：统一关 merge 括号；有改动则提交成一个可撤销单位（与 apply_edits 的"部分成功也落地"一致）。
        bool committed = api.Finish();
        return new ScriptRunResult(error == null, error, output.ToString(), resultText, committed, api.ChangeCount);
    }

    static string Format(JsValue v)
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
