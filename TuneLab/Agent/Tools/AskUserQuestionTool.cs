using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.Agent;

// 向用户提一个问题，【在本轮之内】等到答案再继续——不必把任务切成两轮、也不丢失已有进展。
//
// 为什么是工具而不是 tl 脚本原语：要阻塞等用户点卡片就得 async，而脚本经 Jint 同步跑在 UI 线程，
// 中途阻塞等卡片会自死锁（同 export_project 那条理由）。工具的 ExecuteAsync 天然容得下 await。
// 它也不改工程状态、只为 agent 自身推理服务，故按分面原则归工具面。
//
// 不设超时：卡片一直挂着等用户点。用户点停则本轮取消，这次调用随之成为悬空调用、被如实记作"结果未知"
//（与其它工具一致，见 AgentRunner.CloseDanglingToolCalls）。
internal sealed class AskUserQuestionTool(Func<AgentUserQuestion, CancellationToken, Task<AgentUserAnswer>> ask) : IAgentTool
{
    // 名字对外公开：宿主重放历史时要按它认出"这次调用是问用户"，从而还原成只读问答块而非普通工具步骤。
    public const string ToolName = "ask_user_question";

    public string Name => ToolName;

    public string Description =>
        "Ask the user a question and WAIT for their answer, then keep working in the same turn. " +
        "Use it when you genuinely cannot proceed without a human decision and guessing wrong would be costly or hard to undo — " +
        "an ambiguous request with materially different readings, a destructive choice, or a preference only they can settle. " +
        "Do NOT use it for things you can find out yourself: read the project with run_script, look up settings with list_settings, " +
        "enumerate sound sources / effects / extensions with their list tools. Asking about those wastes the user's time. " +
        "Give concrete options whenever you can — picking one is far less work for the user than typing. " +
        "The user may pick from your options, add free-form text, or ignore your options entirely and just write something; " +
        "the result tells you exactly which of those happened. Ask ONE question per call, and keep working after you get the answer.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "question": { "type": "string", "description": "The question, in the user's language. Be specific and self-contained — include the context they need to decide (what you found, what differs between the options)." },
            "options": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Preset answers to choose from. Keep each one short (it becomes a clickable row); put any explanation in the question instead. Omit this for an open question — the user then only gets the text box."
            },
            "multiple": { "type": "boolean", "description": "true = the user may pick several options; false (default) = at most one." }
          },
          "required": ["question"],
          "additionalProperties": false
        }
        """;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        string? question;
        var options = new List<string>();
        bool multiple = false;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;
            question = root.GetString("question");
            if (root.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
                foreach (var o in opts.EnumerateArray())
                {
                    var label = ((o.ValueKind == JsonValueKind.String ? o.GetString() : o.ToString()) ?? string.Empty).Trim();
                    // 去空、去重：空行渲染成一个点不动的空按钮，重复项让用户无从分辨选了哪个。
                    if (label.Length > 0 && !options.Contains(label))
                        options.Add(label);
                }
            if (root.TryGetProperty("multiple", out var m) && (m.ValueKind == JsonValueKind.True || m.ValueKind == JsonValueKind.False))
                multiple = m.GetBoolean();
        }
        catch (Exception ex) { return "Error: invalid arguments — " + ex.Message; }

        question = (question ?? string.Empty).Trim();
        if (question.Length == 0)
            return "Error: \"question\" is required.";

        var answer = await ask(new AgentUserQuestion(question, options, multiple), cancellationToken);

        // 回报把"选了什么"与"另外写了什么"分开陈述——模型据此能区分"选了 A"、"写了字"、"两者都有"三种情形。
        //
        // 【选中项逐行列出，不用逗号拼接】选项文本自身可能含逗号（"轨道1, 副歌"），拼成一行后【模型和宿主一样】
        // 无从判断那是几项——而模型不会报错，只会静默误解，比解析失败更糟。一行一项则两边都无歧义。
        var sb = new StringBuilder();
        if (answer.SelectedOptions.Count > 0)
        {
            sb.Append("Selected:");
            foreach (var option in answer.SelectedOptions)
                sb.Append("\n- ").Append(option);
        }
        else if (options.Count == 0)
            sb.Append("No options were offered.");
        else if (multiple)
            // 多选的空集【是一个答案】（"这几条都要吗" → 一条都不要），措辞必须与"没回答"区分开，
            // 否则模型会以为用户跳过了问题、转而去猜。
            sb.Append("Selected: none — the user deliberately chose none of the options.");
        else
            sb.Append("No option was selected.");
        var text = (answer.Text ?? string.Empty).Trim();
        // 文本放在最后一段：它可能自带换行，故"该标记之后全是文本"这条规则才立得住（解析与阅读都不会截错）。
        if (text.Length > 0)
            sb.Append('\n').Append(answer.SelectedOptions.Count > 0 ? "Additional input: " : "Input: ").Append(text);
        return sb.ToString();
    }
}
