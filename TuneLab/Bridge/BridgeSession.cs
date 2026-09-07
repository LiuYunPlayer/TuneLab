using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.Agent;
using TuneLab.Commands;

namespace TuneLab.Bridge;

// 一条命令桥连接的整个生命：握手 → 收请求 → 跑命令 → 回结果。一次只服务一个连接（外部 agent 是串行的；
// 并发连接的隔离留到真有需求再说）。
//
// 这是命令面的【第三个入口】（前两个是侧栏 agent 与将来的 headless），故它只做入口该做的三件事：
// 认证、把线上的参数交给命令、把 CommandResult 翻回线上。判据、措辞、闸门流程一概不在这里。
internal sealed class BridgeSession(Stream stream, string token)
{
    bool mAuthorized;
    int mNextRequestId;
    // 宿主 → 客户端的在途请求（目前只有授权确认）：按 id 等对方的响应。读循环与命令那一侧是两条
    // 执行流，故上锁。
    readonly Dictionary<int, TaskCompletionSource<JsonNode?>> mPending = [];
    readonly object mPendingLock = new();
    // 一条连接上同时只能有一个人在写：命令的回复与"问用户"的请求可能同时想发。
    readonly SemaphoreSlim mWriteLock = new(1, 1);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await BridgeProtocol.ReadAsync(stream, cancellationToken);
            if (message == null)
                return;   // 对端收工

            // 响应（对我们发出去的请求的回答）与请求走两条路。
            if (message["method"] == null)
            {
                CompletePending(message);
                continue;
            }

            // 【不 await】处理一条命令期间读循环必须继续转：Edit 命令会反过来问客户端"这次做不做"，
            // 而那个回答正是从这条连接上读回来的——在这里等着处理完，就是等一条自己不去读的消息。
            _ = HandleAndReplyAsync(message, cancellationToken);
        }
    }

    async Task HandleAndReplyAsync(JsonObject request, CancellationToken cancellationToken)
    {
        JsonObject? reply;
        try { reply = await HandleAsync(request, cancellationToken); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { reply = BridgeProtocol.Error(request["id"], BridgeProtocol.ErrorInternal, ex.Message); }

        try { if (reply != null) await SendAsync(reply, cancellationToken); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Info("Command bridge failed to reply: " + ex.Message); }
    }

    async Task SendAsync(JsonObject message, CancellationToken cancellationToken)
    {
        await mWriteLock.WaitAsync(cancellationToken);
        try { await BridgeProtocol.WriteAsync(stream, message, cancellationToken); }
        finally { mWriteLock.Release(); }
    }

    async Task<JsonObject?> HandleAsync(JsonObject request, CancellationToken cancellationToken)
    {
        var id = request["id"];
        var method = request["method"]?.GetValue<string>() ?? "";
        var parameters = request["params"] as JsonObject ?? [];

        // 第一条必须是 hello 且 token 对得上。不对就明说是凭据问题——客户端据此提示用户
        // "宿主重开过桥、请重读凭据文件"，而不是去猜命令写错了。
        if (!mAuthorized && method != BridgeProtocol.MethodHello)
            return BridgeProtocol.Error(id, BridgeProtocol.ErrorUnauthorized, "the first message must be \"hello\" with the token from the credentials file.");

        try
        {
            switch (method)
            {
                case BridgeProtocol.MethodHello:
                {
                    var given = parameters["token"]?.GetValue<string>() ?? "";
                    if (!string.Equals(given, token, StringComparison.Ordinal))
                        return BridgeProtocol.Error(id, BridgeProtocol.ErrorUnauthorized, "invalid token. Re-read the credentials file — the host regenerates it every time the bridge starts.");
                    mAuthorized = true;
                    return BridgeProtocol.Result(id, new JsonObject
                    {
                        ["version"] = BridgeProtocol.Version,
                        ["pid"] = Environment.ProcessId,
                        // 用户此刻设定的授权天花板：客户端据它把自己的话说准（"--yes 也只能做到这一档"）。
                        // 只是握手那一刻的快照——真正的判定每条命令现读一次设置，故这里不是判据、只是告知。
                        ["authorization"] = BridgeProtocol.Wire(AgentAuthorizationExtensions.UserMode),
                    });
                }

                case BridgeProtocol.MethodCommandsList:
                    return BridgeProtocol.Result(id, new JsonObject { ["commands"] = DescribeCommands() });

                case BridgeProtocol.MethodCommandExecute:
                    return await ExecuteAsync(id, parameters, cancellationToken);

                default:
                    return BridgeProtocol.Error(id, BridgeProtocol.ErrorMethodNotFound, "unknown method \"" + method + "\".");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // 命令自己抛出来的（参数畸形等）也在此：一条命令炸掉不该带走整条连接。
            Log.Info("Command bridge request failed: " + ex);
            return BridgeProtocol.Error(id, BridgeProtocol.ErrorInternal, ex.Message);
        }
    }

    // 命令树自省。注册表是纯声明，故这份表与 agent 的工具声明、CLI 的命令树同源，结构上不可能漂移。
    static JsonArray DescribeCommands()
    {
        var commands = new JsonArray();
        foreach (var command in CommandRegistry.All)
            commands.Add(new JsonObject
            {
                ["path"] = command.Path,
                ["kind"] = command.Kind.ToString(),
                ["brief"] = command.Brief,
                ["documentation"] = command.Documentation,
                ["schema"] = JsonNode.Parse(command.ParametersJsonSchema),
            });
        return commands;
    }

    async Task<JsonObject> ExecuteAsync(JsonNode? id, JsonObject parameters, CancellationToken cancellationToken)
    {
        var path = parameters["path"]?.GetValue<string>() ?? "";
        if (!CommandRegistry.TryGet(path, out var command))
            return BridgeProtocol.Error(id, BridgeProtocol.ErrorInvalidRequest,
                "unknown command \"" + path + "\". Call commands/list to see the command tree.");

        var args = parameters["arguments"] is JsonObject given
            ? CommandArgs.Parse(given.ToJsonString())
            : CommandArgs.Empty;

        // 宿主那份共同的环境（同一个工程、同一个当前 part）+ 这个入口自己的两样：
        //  · 授权按这次调用声明的档位（--yes / --dry-run / 交互确认），再被用户设定压顶（见 BridgeAuthorizationPolicy）；
        //  · 没有旁路模型——模型在用户的 agent 会话里，替外部调用方花它的额度是不该做的事（§5.3 的降级由命令自己处理）。
        var ctx = HostCommandContext.Current with
        {
            Authorization = new BridgeAuthorizationPolicy(
                parameters["authorization"]?.GetValue<string>(), parameters["canAsk"]?.GetValue<bool>() ?? true, this),
            SideModel = null,
        };

        var result = await command.ExecuteAsync(args, ctx, cancellationToken);
        if (result.IsError)
        {
            var error = result.Error!.Value;
            return BridgeProtocol.Error(id, BridgeProtocol.ErrorCommandFailed, error.Message, new JsonObject
            {
                ["command"] = path,
                ["code"] = error.Code,
                ["details"] = error.Details?.DeepClone(),
            });
        }

        // 成功一律同时给【结构化事实】与【渲染好的文本】：CLI 默认打文本、--json 打 data，
        // 两者出自同一次执行，不可能对不上。
        return BridgeProtocol.Result(id, new JsonObject
        {
            ["data"] = result.Data?.DeepClone(),
            ["text"] = command.Render(result.Data, args),
        });
    }

    // 宿主 → 客户端问一次（目前只有授权确认）。对方答什么都不算数据，只认三种裁决。
    public async Task<AuthorizationDecision> AskAsync(AuthorizationRequest request, CancellationToken cancellationToken)
    {
        int requestId = Interlocked.Decrement(ref mNextRequestId);   // 用负数发：与客户端自己的请求 id 空间不可能撞上
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (mPendingLock)
            mPending[requestId] = tcs;

        await SendAsync(BridgeProtocol.Request(requestId, BridgeProtocol.MethodAuthorizationConfirm, new JsonObject
        {
            // 动作短语是命令面给的那一句（"change the setting X to 3"），客户端原样打给用户就行——
            // 措辞在命令面只有一份，故 CLI 问的与侧栏卡片写的是同一件事。
            ["actionPhrase"] = request.ActionPhrase(),
            ["kind"] = request.Kind.ToString(),
            ["count"] = request.Count,
            ["target"] = request.Target,
            ["newValue"] = request.NewValue,
            ["secondaryTarget"] = request.SecondaryTarget,
        }), cancellationToken);

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        var answer = await tcs.Task;
        var decision = (answer as JsonObject)?["decision"]?.GetValue<string>();
        return decision switch
        {
            BridgeProtocol.DecisionOnce => AuthorizationDecision.ApplyOnce,
            BridgeProtocol.DecisionAlways => AuthorizationDecision.ApplyAlways,
            // 答得含糊（缺字段/写错）一律当拒绝：授权这件事上，看不懂的回答不能算同意。
            _ => AuthorizationDecision.Reject,
        };
    }

    void CompletePending(JsonObject message)
    {
        if (message["id"]?.GetValue<int>() is not { } id)
            return;
        TaskCompletionSource<JsonNode?>? tcs;
        lock (mPendingLock)
            if (!mPending.Remove(id, out tcs))
                return;
        if (message["error"] is JsonObject error)
            tcs.TrySetResult(null);   // 客户端说它答不了 → 当作没同意（AskAsync 那边落到 Reject）
        else
            tcs.TrySetResult(message["result"]);
    }
}

// 这次调用的授权档位由客户端声明：`--yes` 全放开 / 默认交互确认 / `--dry-run` 等价只读建议
// （docs/command-surface.md §5.1），**再被用户设定压顶**。流程与措辞不在这里——它们在命令面，
// 故 CLI 与侧栏说的是同一套话。
internal sealed class BridgeAuthorizationPolicy(string? declared, bool canAsk, BridgeSession session) : IAuthorizationPolicy
{
    // 【天花板】实际档位 = 客户端声明与用户设定取更严的一档。
    //
    // 少了这一压，本机任何进程只要声明 auto 就比侧栏 agent 权限更大——用户把面板设成只读建议时
    // 自家 agent 一个字都改不了，外部却能随手改用户的工程，这是说不通的。压顶之后"桥开着"最坏的
    // 后果止于用户设的那一档：默认 Confirm 下，一个无声连上来的进程只会拿到"需要确认、而这里没法问"，
    // 写全部落空。
    //
    // 现读设置（不缓存）：用户在面板上调档位是即时生效的事，缓存等于让一条长连接一直用着旧档位。
    public AuthorizationMode Mode => AuthorizationModes.Stricter(Declared, AgentAuthorizationExtensions.UserMode);

    // 客户端这次声明的档位。缺省按最保守的一档：没说清就当"要问"。客户端若没打算答，那次 Edit 会
    // 如实回报"没法问"而不是悄悄做掉。
    //
    // 【档位切换不在这里】ApplyAlways 意味着"此后不再问"，而这个策略只活一次调用——记住这件事的是
    // 客户端：它每次 execute 都重新声明档位，被抬到 auto 之后就一直声明 auto（但仍逐次过天花板）。
    AuthorizationMode Declared => declared switch
    {
        BridgeProtocol.AuthAuto => AuthorizationMode.Auto,
        BridgeProtocol.AuthReadOnly => AuthorizationMode.ReadOnlyAdvice,
        _ => AuthorizationMode.Confirm,
    };

    // 声明了 confirm 就意味着客户端【想】问；canAsk 说明它此刻【问得着】（非交互的 shell 里问不着）。
    // 被天花板压到 Confirm 的也照样要问——声明 auto 的客户端仍会收到那次确认请求，答不了就如实落空。
    public bool CanAsk => canAsk && Mode == AuthorizationMode.Confirm;

    public Task<AuthorizationDecision> AskAsync(AuthorizationRequest request, CancellationToken cancellationToken)
        => session.AskAsync(request, cancellationToken);
}
