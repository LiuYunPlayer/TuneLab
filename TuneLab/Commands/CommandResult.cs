using System.Text.Json.Nodes;

namespace TuneLab.Commands;

// 一次命令失败：机器可读的 code + 给人/模型看的 message（+ 可选细节）。
//
// message 由 handler 直接组合，是本设计里【唯一】不走 Render 的文本：错误的上下文太杂，
// 模板化会丢信息；而且现有工具里那些引导语（"no setting with key X. Call list_settings to see
// the exact keys."）本就是给模型看的、实测调过的，原样保留。
internal readonly record struct CommandError(string Code, string Message, JsonNode? Details = null);

// 一次命令的结果：成功给 Data（结构化事实），失败给 Error。二者互斥。
//
// 各入口按自己的方式适配同一份结果：CLI 默认打 Render(Data)、--json 打 Data 原样；
// agent 入口打 Render(Data) 并按上限截断；MCP 把 Data 放进 structuredContent。
internal readonly record struct CommandResult
{
    public JsonNode? Data { get; private init; }
    public CommandError? Error { get; private init; }

    public bool IsError => Error != null;

    public static CommandResult Ok(JsonNode? data = null) => new() { Data = data };

    public static CommandResult Fail(string code, string message, JsonNode? details = null)
        => new() { Error = new CommandError(code, message, details) };
}
