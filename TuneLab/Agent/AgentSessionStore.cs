using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TuneLab.Foundation;

namespace TuneLab.Agent;

// 会话本地持久化：每会话一个文件夹 AgentSessions/<id>/ —— session.json（清单：有序全量消息轨迹 + 附件引用）+
// blobs/<sha256>.<ext>（附件二进制，内容寻址、写一次不可变、跨消息/会话天然去重）。据此可让「重载 == 实时」：
// 既能完整重建分步视图（文本/思考/工具块按序交错 + 图片），又能把含工具往返的上下文原样回灌续聊。
// 附件单独落 blob、清单只存引用 —— 避免图片 base64 内联进 JSON 后每轮全量重写爆炸。
// 陈旧性（历史工具结果是当时项目状态的快照、可能过期）由系统提示兜底，不靠丢历史解决。
//
// 向后兼容：旧版「每会话一个扁平 AgentSessions/<id>.json」仍能列出/加载；新存盘一律走文件夹形式，
// 首次保存旧会话时把扁平文件迁成文件夹（删旧扁平文件）。Schema 演进走 System.Text.Json 加法式、零迁移：
// 旧文件无新字段即取默认值；SchemaVersion 缺失/0=旧版纯文本（降级为纯文本气泡），1=全量轨迹（重建分步视图）。
internal sealed class ChatToolCall
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "{}";
}

// 一个附件的清单引用：Hash=blob 内容寻址名（sha256 hex），MediaType=MIME。
// Data 仅在「待落盘」时携带原始字节（写 blob 用），不序列化进 session.json；读盘后恒为 null，渲染时由 ReadBlob 按 Hash 取。
internal sealed class ChatAttachment
{
    public string Hash { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    [JsonIgnore] public byte[]? Data { get; set; }
}

internal sealed class ChatTurnMessage
{
    public string Role { get; set; } = "user";  // "user" | "assistant" | "tool"
    // 文本内容：user=输入，assistant=正文回复，tool=结果。沿用旧字段名 Text 保持向后兼容（旧文件可原样反序列化）。
    public string Text { get; set; } = string.Empty;
    public List<ChatAttachment>? Attachments { get; set; }  // 仅 user：图片等多模态附件（引用 blob）
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
    public int SchemaVersion { get; set; }   // 缺失/0=旧版纯文本；1=全量轨迹（含工具/思考/附件）
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public long CreatedAtUnix { get; set; }
    public long UpdatedAtUnix { get; set; }
    public List<ChatTurnMessage> Messages { get; set; } = new();
}

internal static class AgentSessionStore
{
    // 省略 null 字段：每条消息上多数扩展字段（附件/思考/工具调用/工具回指/用量）按角色只部分有值，
    // 不写 null 让文件干净许多；反序列化把缺失字段当默认值，向后兼容不受影响。
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    static string Root => PathManager.AgentSessionsFolder;
    static string FolderOf(string id) => Path.Combine(Root, id);
    static string SessionJsonPath(string id) => Path.Combine(FolderOf(id), "session.json");
    static string BlobsDir(string id) => Path.Combine(FolderOf(id), "blobs");
    static string LegacyPath(string id) => Path.Combine(Root, id + ".json");

    // 列出全部会话，按最近更新倒序。新版=各 <id>/session.json，旧版=扁平 <id>.json（文件夹形式优先、不重复）。损坏项跳过。
    public static List<ChatSession> List()
    {
        var byId = new Dictionary<string, ChatSession>();
        try
        {
            if (!Directory.Exists(Root))
                return new();

            // 新版文件夹形式
            foreach (var dir in Directory.EnumerateDirectories(Root))
            {
                var json = Path.Combine(dir, "session.json");
                if (!File.Exists(json))
                    continue;
                var s = TryLoad(json);
                if (s != null && !string.IsNullOrEmpty(s.Id))
                    byId[s.Id] = s;
            }

            // 旧版扁平文件（仅当该 id 尚无文件夹形式时采用）
            foreach (var file in Directory.EnumerateFiles(Root, "*.json"))
            {
                var s = TryLoad(file);
                if (s != null && !string.IsNullOrEmpty(s.Id) && !byId.ContainsKey(s.Id))
                    byId[s.Id] = s;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to list agent sessions: " + ex);
        }
        return byId.Values.OrderByDescending(s => s.UpdatedAtUnix).ToList();
    }

    static ChatSession? TryLoad(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<ChatSession>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Log.Warning("Skip corrupt agent session file " + path + ": " + ex.Message);
            return null;
        }
    }

    public static void Save(ChatSession session)
    {
        try
        {
            PathManager.MakeSureExist(FolderOf(session.Id));
            // 先把带原始字节的附件落 blob（内容寻址、已存则跳过），再写清单——清单只引用 blob、不含字节。
            foreach (var m in session.Messages)
            {
                if (m.Attachments == null)
                    continue;
                foreach (var a in m.Attachments)
                    WriteBlobIfMissing(session.Id, a);
            }
            SaveFile.WriteAllText(SessionJsonPath(session.Id), JsonSerializer.Serialize(session, Options));
            // 迁移：旧扁平文件存在则删掉，避免与文件夹形式重复列出。
            var legacy = LegacyPath(session.Id);
            if (File.Exists(legacy))
                File.Delete(legacy);
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
            var folder = FolderOf(id);
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            var legacy = LegacyPath(id);
            if (File.Exists(legacy))
                File.Delete(legacy);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to delete agent session " + id + ": " + ex);
        }
    }

    // 读取某附件的原始字节（重载渲染图片用）。按 Hash + MediaType 推 ext 定位 blob；推不准则在 blobs 内按 Hash.* 兜底。
    public static byte[]? ReadBlob(string sessionId, string hash, string mediaType)
    {
        try
        {
            var exact = Path.Combine(BlobsDir(sessionId), hash + "." + ExtForMime(mediaType));
            if (File.Exists(exact))
                return File.ReadAllBytes(exact);
            var dir = BlobsDir(sessionId);
            if (Directory.Exists(dir))
            {
                var match = Directory.EnumerateFiles(dir, hash + ".*").FirstOrDefault();
                if (match != null)
                    return File.ReadAllBytes(match);
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to read agent blob " + hash + " in " + sessionId + ": " + ex.Message);
        }
        return null;
    }

    // 计算字节的 sha256（hex 小写）——内容寻址 blob 名，跨消息/会话相同图片天然去重。
    public static string ComputeHash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    static void WriteBlobIfMissing(string sessionId, ChatAttachment a)
    {
        if (a.Data is not { Length: > 0 } || string.IsNullOrEmpty(a.Hash))
            return;
        PathManager.MakeSureExist(BlobsDir(sessionId));
        var path = Path.Combine(BlobsDir(sessionId), a.Hash + "." + ExtForMime(a.MediaType));
        if (!File.Exists(path)) // 内容寻址、写一次不可变：已存即同内容，跳过
            SaveFile.WriteAllBytes(path, a.Data);
    }

    static string ExtForMime(string mime) => mime switch
    {
        "image/png" => "png",
        "image/jpeg" => "jpg",
        "image/jpg" => "jpg",
        "image/webp" => "webp",
        "image/gif" => "gif",
        "image/bmp" => "bmp",
        _ => "bin",
    };
}
