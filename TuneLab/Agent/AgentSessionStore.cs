using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TuneLab.Foundation;

namespace TuneLab.Agent;

// 会话本地持久化：每会话一个 JSON（AgentSessions/<id>.json），存有序的全量消息轨迹——用户输入、助手回复
// （含思考全文 + 它请求的工具调用 + 本次用量）、工具结果（含错误标记）。据此可让「重载 == 实时」：既能完整重建
// 分步视图（文本/思考/工具块按序交错），又能把含工具往返的上下文原样回灌续聊。陈旧性（历史工具结果是当时项目
// 状态的快照、可能过期）由系统提示兜底——涉及计数/索引的写操作前模型应重新调读工具核对，不靠丢历史解决。
//
// Schema 演进走 System.Text.Json 加法式、零迁移：旧文件无新字段即取默认值。SchemaVersion 区分加载路径——
// 缺失/0 = 旧版纯文本（只 user/assistant 文本，无工具/思考），降级为纯文本气泡；1 = 全量轨迹，重建分步视图。
internal sealed class ChatToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "{}";
}

internal sealed class ChatTurnMessage
{
    public string Role { get; set; } = "user";  // "user" | "assistant" | "tool"
    // 文本内容：user=输入，assistant=正文回复，tool=结果。沿用旧字段名 Text 保持向后兼容（旧文件可原样反序列化）。
    public string Text { get; set; } = string.Empty;
    public string? Reasoning { get; set; }              // 仅 assistant：思考通道全文（v1+）
    public List<ChatToolCall>? ToolCalls { get; set; }  // 仅 assistant：本次请求的工具调用（v1+）
    public string? ToolCallId { get; set; }             // 仅 tool：回指 ToolCalls[].Id（v1+）
    public bool IsError { get; set; }                   // 仅 tool：结果是否为错误（v1+）
    // 助手轮的 token 用量（端点返回才有；用户/工具轮恒空）。
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
}

internal sealed class ChatSession
{
    public int SchemaVersion { get; set; }   // 缺失/0=旧版纯文本；1=全量轨迹（含工具/思考）
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public long CreatedAtUnix { get; set; }
    public long UpdatedAtUnix { get; set; }
    public List<ChatTurnMessage> Messages { get; set; } = new();
}

internal static class AgentSessionStore
{
    // 省略 null 字段：每条消息上多数扩展字段（思考/工具调用/工具回指/用量）按角色只部分有值，
    // 不写 null 让文件干净许多；反序列化把缺失字段当默认值，向后兼容不受影响。
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    static string PathOf(string id) => System.IO.Path.Combine(PathManager.AgentSessionsFolder, id + ".json");

    // 列出全部会话，按最近更新倒序。损坏的单个文件跳过、不影响整体。
    public static List<ChatSession> List()
    {
        var result = new List<ChatSession>();
        try
        {
            if (!Directory.Exists(PathManager.AgentSessionsFolder))
                return result;
            foreach (var file in Directory.EnumerateFiles(PathManager.AgentSessionsFolder, "*.json"))
            {
                try
                {
                    var s = JsonSerializer.Deserialize<ChatSession>(File.ReadAllText(file));
                    if (s != null && !string.IsNullOrEmpty(s.Id))
                        result.Add(s);
                }
                catch (Exception ex)
                {
                    Log.Warning("Skip corrupt agent session file " + file + ": " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to list agent sessions: " + ex);
        }
        return result.OrderByDescending(s => s.UpdatedAtUnix).ToList();
    }

    public static void Save(ChatSession session)
    {
        try
        {
            PathManager.MakeSureExist(PathManager.AgentSessionsFolder);
            SaveFile.WriteAllText(PathOf(session.Id), JsonSerializer.Serialize(session, Options));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save agent session " + session.Id + ": " + ex);
        }
    }

    public static void Delete(string id)
    {
        try
        {
            var path = PathOf(id);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to delete agent session " + id + ": " + ex);
        }
    }
}
