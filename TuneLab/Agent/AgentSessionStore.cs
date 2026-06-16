using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TuneLab.Foundation;

namespace TuneLab.Agent;

// 会话本地持久化：每会话一个 JSON（AgentSessions/<id>.json）。只存对话文本（用户/助手）+ 每轮 token 用量——
// 不存工具调用轨迹（那是当时项目状态的快照、易过期；续聊时模型应重新调工具读当前状态，比存快照更准）。
internal sealed class ChatTurnMessage
{
    public string Role { get; set; } = "user";  // "user" | "assistant"
    public string Text { get; set; } = string.Empty;
    // 助手轮的 token 用量（端点返回才有；用户轮恒空）。
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
}

internal sealed class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public long CreatedAtUnix { get; set; }
    public long UpdatedAtUnix { get; set; }
    public List<ChatTurnMessage> Messages { get; set; } = new();
}

internal static class AgentSessionStore
{
    static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

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
            File.WriteAllText(PathOf(session.Id), JsonSerializer.Serialize(session, Options));
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
