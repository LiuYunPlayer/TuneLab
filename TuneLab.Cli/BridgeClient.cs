using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Bridge;

namespace TuneLab.Cli;

// 连上运行中的 TuneLab 并发一条命令。线上的形状（长度前缀 + JSON-RPC）由 BridgeProtocol 定义，
// 这里不另抄一份——CLI 与宿主共用同一个协议文件，故不可能各写各的。
internal sealed class BridgeClient(NamedPipeClientStream pipe) : IDisposable
{
    int mNextId;

    // 宿主在握手里回的【授权天花板】（用户在 TuneLab 里设的档位）。--yes 越不过它，故 CLI 据它说实话。
    // null = 宿主没回这个字段（旧版本）：那就什么都别说，凭猜测发警告比不发更误导。
    public string? Ceiling { get; private set; }

    // 连不上时【区分两种情形】：桥没开（去设置里开）与宿主已退出但凭据文件留着（去启动 TuneLab）。
    // 两者的下一步完全不同，混成一句"连不上"等于让用户自己去猜。
    public static async Task<(BridgeClient? Client, string? Error)> ConnectAsync(CancellationToken cancellationToken)
    {
        var credentials = BridgeCredentials.Read();
        if (credentials is not { } c)
            return (null, "TuneLab's command bridge is not on. Start TuneLab and turn on \"Command Bridge\" in Settings → General."
                + "\n(No credentials file at " + BridgeCredentials.FilePath + ".)");

        // CurrentUserOnly：连之前先认宿主是不是同一个用户的进程——同机多用户下，别人放一个同名管道
        // 在那里等着，我们就会把命令（以及那次授权确认）发给它。
        var pipe = new NamedPipeClientStream(".", c.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(3000, cancellationToken);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            pipe.Dispose();
            return (null, c.IsHostRunning()
                ? "TuneLab is running but its command bridge did not answer. Turn \"Command Bridge\" off and on again in Settings → General."
                : "TuneLab is not running (a stale credentials file was left behind). Start TuneLab with the command bridge on.");
        }

        var client = new BridgeClient(pipe);
        var hello = await client.RequestAsync(BridgeProtocol.MethodHello, new JsonObject { ["token"] = c.Token, ["client"] = "tunelab-cli" }, null, cancellationToken);
        if (hello.Error != null)
        {
            client.Dispose();
            return (null, hello.Error);
        }
        client.Ceiling = (hello.Result as JsonObject)?["authorization"]?.GetValue<string>();
        return (client, null);
    }

    // 发一条请求并等它的响应。等待期间可能先收到【宿主发来的请求】（授权确认）——那不是我们的响应，
    // 交给 onServerRequest 现场作答，然后继续等。
    public async Task<(JsonNode? Result, string? Error)> RequestAsync(
        string method, JsonObject parameters, Func<JsonObject, JsonObject>? onServerRequest, CancellationToken cancellationToken)
    {
        int id = ++mNextId;
        await BridgeProtocol.WriteAsync(pipe, BridgeProtocol.Request(id, method, parameters), cancellationToken);

        while (true)
        {
            var message = await BridgeProtocol.ReadAsync(pipe, cancellationToken);
            if (message == null)
                return (null, "TuneLab closed the connection before answering.");

            if (message["method"]?.GetValue<string>() is { } serverMethod)
            {
                var answer = onServerRequest?.Invoke(message)
                    // 没准备答（没给回调）就明说答不了，别让宿主一直等着。
                    ?? new JsonObject { ["decision"] = BridgeProtocol.DecisionReject };
                await BridgeProtocol.WriteAsync(pipe, BridgeProtocol.Result(message["id"], answer), cancellationToken);
                continue;
            }

            if (message["id"]?.GetValue<int>() != id)
                continue;   // 不是这次的响应（协议上不该发生，但也没必要为此断线）
            if (message["error"] is JsonObject error)
                return (null, error["message"]?.GetValue<string>() ?? "unknown error");
            return (message["result"], null);
        }
    }

    public void Dispose() => pipe.Dispose();
}
