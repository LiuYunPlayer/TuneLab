using System;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Bridge;
using TuneLab.Commands;

namespace TuneLab.Cli;

// 一条命令跑完的产物：结构化事实 + 渲染文本，或者一句失败原因。三者的判据全在命令面，这里只是搬运。
internal readonly record struct CommandOutcome(JsonNode? Data, string Text, string? Failure);

// 命令送到哪里去跑。两条路：
//  · attach   —— 连上运行中的 TuneLab，跑在用户此刻开着的那个工程里；
//  · headless —— 就地起一个无头宿主（不开窗口、不建音频设备），工程在本进程内存里。
// 两条路跑的是【同一份命令】，故这个接口只管"送出去、把结果拿回来"。
internal interface ICommandRunner
{
    Task<CommandOutcome> RunAsync(ICommand command, JsonObject arguments, string authorization, bool canAsk, CancellationToken cancellationToken);
}

// 这次调用（单条）或这一趟（批量）的授权状态。
//
// 存在的理由只有一个：确认卡片上的 [a] always 说的是"此后不再问"，那就必须真的不再问——
// 否则批量跑的第二条 edit 又弹一次，而命令面早已如实告诉调用方"以后不会再问了"。
// 档位切换本就规定由策略实现方自己完成（见 IAuthorizationPolicy），CLI 这一份放在这里，
// attach 与 headless 共用。
internal sealed class AuthorizationState
{
    public bool Elevated { get; set; }

    // 这条命令实际声明的档位：一旦被抬到 auto 就一直是 auto。
    public string Declared(string requested) => Elevated ? BridgeProtocol.AuthAuto : requested;
}

// attach：把命令发给运行中的 TuneLab。
internal sealed class BridgeRunner(BridgeClient client, AuthorizationState authorization) : ICommandRunner
{
    public async Task<CommandOutcome> RunAsync(ICommand command, JsonObject arguments, string declared, bool canAsk, CancellationToken cancellationToken)
    {
        var (result, failure) = await client.RequestAsync(BridgeProtocol.MethodCommandExecute, new JsonObject
        {
            ["path"] = command.Path,
            ["arguments"] = arguments,
            ["authorization"] = authorization.Declared(declared),
            // 【能不能问】与【要不要问】是两回事：非交互的 shell 里没人能答那个确认，宿主必须知道，
            // 否则命令会把"根本没法问"说成"用户拒绝了"——而当时并没有任何用户被问过。
            ["canAsk"] = canAsk,
        }, request => StdinConfirm.OnBridgeRequest(request, authorization), cancellationToken);

        if (failure != null)
            return new CommandOutcome(null, string.Empty, failure);

        var payload = result as JsonObject;
        return new CommandOutcome(payload?["data"], payload?["text"]?.GetValue<string>() ?? string.Empty, null);
    }
}

// headless：就地执行。宿主环境（工程、语言、主线程）由 HeadlessHost 装好后经 baseContext 传进来，
// 每条命令只在它之上派生自己那一档授权。
internal sealed class HeadlessRunner(CommandContext baseContext, AuthorizationState authorization) : ICommandRunner
{
    public async Task<CommandOutcome> RunAsync(ICommand command, JsonObject arguments, string declared, bool canAsk, CancellationToken cancellationToken)
    {
        var args = CommandArgs.Parse(arguments.ToJsonString());
        var ctx = baseContext with { Authorization = new LocalAuthorizationPolicy(authorization, declared, canAsk) };

        var result = await command.ExecuteAsync(args, ctx, cancellationToken);
        if (result.IsError)
            return new CommandOutcome(null, string.Empty, result.Error!.Value.Message);

        return new CommandOutcome(result.Data, command.Render(result.Data, args), null);
    }
}

// headless 下的授权闸门：与 attach 那条路【同一套语义】（--yes 全放开 / --dry-run 只读建议 /
// 默认交互确认），只是问用户这件事就地在 stdin 上问，不经过桥。
// 流程与措辞仍在命令面（IAuthorizationPolicy 的默认实现 + ScriptWriteExecutor），这里只答三个原语。
internal sealed class LocalAuthorizationPolicy(AuthorizationState state, string declared, bool canAsk) : IAuthorizationPolicy
{
    public AuthorizationMode Mode => state.Declared(declared) switch
    {
        BridgeProtocol.AuthAuto => AuthorizationMode.Auto,
        BridgeProtocol.AuthReadOnly => AuthorizationMode.ReadOnlyAdvice,
        _ => AuthorizationMode.Confirm,
    };

    // 非交互的 shell 里没人能答，故如实说"问不了"——命令据此说"需要确认但这里没法问"，
    // 而不是"用户拒绝了"（后者是句假话：当时并没有任何用户被问过）。
    public bool CanAsk => canAsk && Mode == AuthorizationMode.Confirm;

    public Task<AuthorizationDecision> AskAsync(AuthorizationRequest request, CancellationToken cancellationToken)
        => Task.FromResult(StdinConfirm.Ask(request.ActionPhrase(), state));
}

// 问用户"这次写做不做"。打到 stderr、读 stdin：stdout 留给结果，方便管道。
// attach 与 headless 共用这一份措辞——同一个 CLI 不该因为连的是谁而说两套话。
internal static class StdinConfirm
{
    public static AuthorizationDecision Ask(string actionPhrase, AuthorizationState state)
    {
        // 兜底：正常路径上 canAsk 已经把这种情形拦在前面（宿主根本不会问），但策略实现不能假设
        // 调用方一定守规矩——真被问到又没人可问时，停在这里等一个永远不来的回车是最坏的结果。
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("tunelab: asked to confirm \"" + actionPhrase + "\", but stdin is not interactive. Nothing was changed.");
            return AuthorizationDecision.Reject;
        }

        Console.Error.WriteLine("TuneLab asks to confirm: " + actionPhrase);
        Console.Error.Write("[y] once  [a] always (stops asking for the rest of this run)  [N] reject: ");
        var answer = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
        switch (answer)
        {
            case "y":
            case "yes":
                return AuthorizationDecision.ApplyOnce;
            case "a":
            case "always":
                state.Elevated = true;
                return AuthorizationDecision.ApplyAlways;
            default:
                return AuthorizationDecision.Reject;
        }
    }

    // 桥那条路上的回调形：宿主经同一条连接反过来问，答什么都不算数据，只认三种裁决。
    public static JsonObject OnBridgeRequest(JsonObject request, AuthorizationState state)
    {
        var phrase = (request["params"] as JsonObject)?["actionPhrase"]?.GetValue<string>() ?? "make this change";
        return new JsonObject
        {
            ["decision"] = Ask(phrase, state) switch
            {
                AuthorizationDecision.ApplyOnce => BridgeProtocol.DecisionOnce,
                AuthorizationDecision.ApplyAlways => BridgeProtocol.DecisionAlways,
                _ => BridgeProtocol.DecisionReject,
            },
        };
    }
}
