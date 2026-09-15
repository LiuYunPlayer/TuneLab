using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Data;
using TuneLab.Data.Synthesis;

namespace TuneLab.Commands;

// 「把合成驱动到落定，然后等」——`project synthesize` 与 `project export-audio` 共用的那一段。
//
// 两条命令对结果的处置完全不同（一条只回报事实，一条据此决定写不写文件），但【怎么等】必须是同一份：
// 派活节奏、超时口径、取消点各写一遍的话，两条命令迟早给出不一样的"还剩多少"。
internal static class SynthesisWaitSupport
{
    // 默认等 5 分钟。这不是"渲染要多久"的估计（那取决于引擎与曲子长度），而是"卡住了多久算卡住"。
    public const int DefaultTimeoutSeconds = 300;
    public const int MinTimeoutSeconds = 10;
    public const int MaxTimeoutSeconds = 3600;

    public static int ClampTimeout(int? given)
        => System.Math.Clamp(given ?? DefaultTimeoutSeconds, MinTimeoutSeconds, MaxTimeoutSeconds);

    // 每一跳都上数据线程走一拍，跳与跳之间让出去，好让引擎的续体（编辑器里是 UI 线程的 Dispatcher，
    // headless 里是驱动循环的泵）跑起来。返回最后一跳的 tick（Done 即落定，否则就是超时时的剩余量）。
    //
    // 取消原样抛出 OperationCanceledException：被取消之后该说什么，只有调用方知道（有没有写下东西、
    // 写了一半没有），在这里统一措辞就会替它说谎。
    public static async Task<(SynthesisTick Tick, double ElapsedSeconds)> DriveAsync(
        CommandContext ctx, IProject project, SynthesisScope scope, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var tick = await ctx.OnMainThread(() => SynthesisCompletion.DriveOnce(project, scope));
            if (tick.Done || clock.Elapsed.TotalSeconds >= timeoutSeconds)
                return (tick, clock.Elapsed.TotalSeconds);

            await Task.Delay(50, cancellationToken);
        }
    }

    // 「链尾不是应有结果」的那些范围，进结果 JSON 的形状。两条命令报的是同一件事，故同一份渲染。
    public static JsonArray Flaws(IReadOnlyList<SynthesisFlaw> flaws)
    {
        var array = new JsonArray();
        foreach (var flaw in flaws)
            array.Add(new JsonObject
            {
                ["where"] = flaw.Where,
                ["startSeconds"] = Math.Round(flaw.StartTime, 3),
                ["endSeconds"] = Math.Round(flaw.EndTime, 3),
                ["message"] = string.IsNullOrEmpty(flaw.Message) ? null : flaw.Message,
            });
        return array;
    }

    public static string DescribeFlaws(JsonNode? node)
    {
        if (node is not JsonArray array)
            return string.Empty;

        var text = new StringBuilder();
        foreach (var item in array)
        {
            if (item is not JsonObject flaw)
                continue;
            text.AppendFormat("\n  · {0}, {1:0.###}s–{2:0.###}s",
                flaw["where"]!.GetValue<string>(), flaw["startSeconds"]!.GetValue<double>(), flaw["endSeconds"]!.GetValue<double>());
            if (flaw["message"]?.GetValue<string>() is { Length: > 0 } why)
                text.Append(" — ").Append(why.Replace("\n", " "));
        }
        return text.ToString();
    }
}
