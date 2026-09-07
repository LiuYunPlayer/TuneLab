using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using TuneLab.Commands;

namespace TuneLab.Bridge;

// 命令桥的线上协议：**4 字节小端长度前缀 + 一条 UTF-8 的 JSON-RPC 2.0 报文**。
//
// 为什么要长度前缀而不是逐行 JSON：命令的结果里天然有换行（渲染出来的文本、脚本输出、手册章节），
// 按行切分等于把"内容里能不能出现分隔符"变成一个永远要小心的坑。长度前缀没有这个问题。
//
// 为什么是命名管道不是端口：管道天然限本机、天然带用户 ACL，不必选端口、不必处理端口占用，也不会被
// 同机其它进程扫到。跨平台上 .NET 的命名管道落到 Unix domain socket，语义一致。
// 【另开一个管道，不复用 "TuneLab"】——那个管道的语义是"把命令行参数转发给运行中实例"（单实例逻辑），
// 载荷是逐行文本；混进 JSON-RPC 会让两个协议共用一个名字。
internal static class BridgeProtocol
{
    public const int Version = 1;

    // 单条报文上限。给得宽（手册全文、长脚本输出都要过这条线），但必须有：没有上限的长度前缀等于让
    // 任何一个坏客户端申请任意大的内存。
    public const int MaxMessageBytes = 64 * 1024 * 1024;

    // 方法名。客户端 → 宿主：
    public const string MethodHello = "hello";                 // 必须是第一条：带凭据文件里的 token
    public const string MethodCommandsList = "commands/list";  // 命令树自省（CLI 的在线 --help / MCP 的 tools/list）
    public const string MethodCommandExecute = "command/execute";
    // 宿主 → 客户端（只在执行 Edit 命令、且这次连接声明了 confirm 档时发）：
    public const string MethodAuthorizationConfirm = "authorization/confirm";

    // 这次连接的授权档位（客户端在每次 execute 里声明；缺省按最保守的 confirm 处理）。
    // 与档位并列的还有 execute 参数 "canAsk"（布尔，缺省 true）：声明了 confirm 就说明"要问"，
    // canAsk 才说明"问得着"——非交互的 shell 里两者不同。缺了它，命令会把"根本没法问"说成
    // "用户拒绝了"，而当时并没有任何用户被问过。
    public const string AuthAuto = "auto";
    public const string AuthConfirm = "confirm";
    public const string AuthReadOnly = "readonly";

    // 档位 → 线上字样。宿主用它在 hello 里回自己当前的【天花板】（用户设定），客户端据此把话说准。
    public static string Wire(AuthorizationMode mode) => mode switch
    {
        AuthorizationMode.Auto => AuthAuto,
        AuthorizationMode.ReadOnlyAdvice => AuthReadOnly,
        _ => AuthConfirm,
    };

    // 授权裁决（客户端答 authorization/confirm 用）。
    public const string DecisionOnce = "once";
    public const string DecisionAlways = "always";
    public const string DecisionReject = "reject";

    // JSON-RPC 的标准错误码（我们只用得上这几个）+ 本协议自己的两个。
    public const int ErrorParse = -32700;
    public const int ErrorInvalidRequest = -32600;
    public const int ErrorMethodNotFound = -32601;
    public const int ErrorInternal = -32603;
    public const int ErrorUnauthorized = -32000;   // token 不对 / 没先 hello
    public const int ErrorCommandFailed = -32001;  // 命令本身报错（CommandError 原样带出 code/message）

    public static async Task WriteAsync(Stream stream, JsonObject message, CancellationToken cancellationToken)
    {
        var payload = Encoding.UTF8.GetBytes(message.ToJsonString());
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    // 读一条报文。对端正常收工（读到 0 字节）返回 null；坏报文抛 IOException——那不是"没有消息"，
    // 而是这条连接已经不可信，调用方该断开而不是接着读。
    public static async Task<JsonObject?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        if (!await FillAsync(stream, header, cancellationToken))
            return null;

        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > MaxMessageBytes)
            throw new IOException("bridge message length out of range: " + length);

        var payload = new byte[length];
        if (!await FillAsync(stream, payload, cancellationToken))
            throw new IOException("bridge message truncated");

        try
        {
            return JsonNode.Parse(Encoding.UTF8.GetString(payload)) as JsonObject
                ?? throw new IOException("bridge message is not a JSON object");
        }
        catch (JsonException ex) { throw new IOException("bridge message is not valid JSON: " + ex.Message, ex); }
    }

    static async Task<bool> FillAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (n == 0)
                return false;
            read += n;
        }
        return true;
    }

    public static JsonObject Request(int id, string method, JsonObject? parameters = null)
    {
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (parameters != null)
            message["params"] = parameters;
        return message;
    }

    public static JsonObject Result(JsonNode? id, JsonNode? result)
        => new() { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result };

    public static JsonObject Error(JsonNode? id, int code, string message, JsonNode? data = null)
    {
        var error = new JsonObject { ["code"] = code, ["message"] = message };
        if (data != null)
            error["data"] = data;
        return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = error };
    }
}
