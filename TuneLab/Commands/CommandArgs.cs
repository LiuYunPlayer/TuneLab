using System;
using System.Text.Json;

namespace TuneLab.Commands;

// 一次命令调用的参数：入口把 JSON 原样交进来（agent 给模型产的 arguments、CLI 把 flag 拼成对象、
// MCP 给 arguments 字段），handler 从这里取字段。
//
// 取字段的容错（数字/字符串互转、整数四舍五入、null 当缺省）目前住在 TuneLab/Agent/Tools/ToolJson.cs，
// 那些容错是为「弱模型也能稳定调用」加的、对 CLI/MCP 同样有用；搬第一条带参数的命令时把它移到本命名空间
// （方向才对：命令面不该依赖 agent 层）。`project status` 无参数，故此步不动它。
internal readonly record struct CommandArgs(JsonElement Json)
{
    static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    public static CommandArgs Empty => new(EmptyObject);

    // 解析入口给的 JSON 文本。缺省（null/空白/JSON null）当空参数对象——无参命令的调用方常给 "" 或 "null"。
    // 但【畸形或不是对象】的 JSON 抛异常：那是调用方的错误，静默当空参数会让命令带着默认值跑掉，
    // 把"参数写错了"变成"结果不对"。异常由入口转成 CommandError 回报（模型据此自行纠正）。
    public static CommandArgs Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Empty;

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(json).RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("arguments must be a JSON object: " + ex.Message, nameof(json), ex);
        }

        return root.ValueKind switch
        {
            JsonValueKind.Object => new CommandArgs(root),
            JsonValueKind.Null => Empty,
            _ => throw new ArgumentException("arguments must be a JSON object, got " + root.ValueKind + ".", nameof(json)),
        };
    }
}
