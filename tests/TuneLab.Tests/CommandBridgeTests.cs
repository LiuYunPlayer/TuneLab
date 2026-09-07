using System;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Bridge;
using TuneLab.Commands;
using Xunit;

namespace TuneLab.Tests;

// 命令桥的端到端封条：起一条真的命名管道，按线上协议跑一遍握手、自省、执行、授权确认。
// 不开界面——桥是命令面的入口，本来就不该依赖 UI；这条测试同时钉住了这一点。
//
// 三件必须守住的事：
//  · 没握手/token 不对，一条命令都跑不了（这是"谁能连"的全部门槛）；
//  · 命令树自省与注册表同源（CLI 的离线 --help、MCP 的 tools/list 都靠它）；
//  · Edit 命令的授权按【这次连接声明的档位】走，且宿主问、客户端答那一趟真的能跑通；
//  · 声明的档位再被【用户设定】压顶——外部进程不能比用户给自家侧栏 agent 的权限更大。
public class CommandBridgeTests
{
    const string Token = "test-token";

    // 一对连起来的命名管道（名字随机，避免并行测试互撞）。
    static async Task<(NamedPipeServerStream Server, NamedPipeClientStream Client)> PairAsync()
    {
        var name = "TuneLab.Test." + Guid.NewGuid().ToString("N");
        var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        var accept = server.WaitForConnectionAsync();
        await client.ConnectAsync(5000);
        await accept;
        return (server, client);
    }

    sealed class Harness : IAsyncDisposable
    {
        public NamedPipeServerStream Server = null!;
        public NamedPipeClientStream Client = null!;
        public CancellationTokenSource Cancellation = null!;
        public Task Session = null!;
        int mId;

        public static async Task<Harness> StartAsync()
        {
            var (server, client) = await PairAsync();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var harness = new Harness { Server = server, Client = client, Cancellation = cts };
            harness.Session = new BridgeSession(server, Token).RunAsync(cts.Token);
            return harness;
        }

        // 发一条请求、等它的响应；期间宿主发来的请求（授权确认）交给 answer 现场作答。
        public async Task<JsonObject> CallAsync(string method, JsonObject parameters, Func<JsonObject, JsonObject>? answer = null)
        {
            int id = ++mId;
            await BridgeProtocol.WriteAsync(Client, BridgeProtocol.Request(id, method, parameters), Cancellation.Token);
            while (true)
            {
                var message = await BridgeProtocol.ReadAsync(Client, Cancellation.Token) ?? throw new InvalidOperationException("the session closed the connection");
                if (message["method"] != null)
                {
                    var reply = answer?.Invoke(message) ?? new JsonObject { ["decision"] = BridgeProtocol.DecisionReject };
                    await BridgeProtocol.WriteAsync(Client, BridgeProtocol.Result(message["id"], reply), Cancellation.Token);
                    continue;
                }
                return message;
            }
        }

        public Task<JsonObject> HelloAsync(string token = Token)
            => CallAsync(BridgeProtocol.MethodHello, new JsonObject { ["token"] = token });

        public async ValueTask DisposeAsync()
        {
            Cancellation.Cancel();
            Client.Dispose();
            try { await Session; } catch (Exception) { /* 取消/断流即收工 */ }
            Server.Dispose();
            Cancellation.Dispose();
        }
    }

    static JsonObject Result(JsonObject message) => message["result"]!.AsObject();
    static JsonObject Error(JsonObject message) => message["error"]!.AsObject();

    // ── 门槛

    [Fact]
    public async Task NothingRunsBeforeAValidHello()
    {
        await using var h = await Harness.StartAsync();

        var early = await h.CallAsync(BridgeProtocol.MethodCommandsList, []);
        Assert.Equal(BridgeProtocol.ErrorUnauthorized, Error(early)["code"]!.GetValue<int>());

        var wrong = await h.HelloAsync("not-the-token");
        Assert.Equal(BridgeProtocol.ErrorUnauthorized, Error(wrong)["code"]!.GetValue<int>());
        // 说清是凭据问题、且该重读文件——宿主每次开桥都换 token，这是最常见的一种"连不上"。
        Assert.Contains("Re-read the credentials file", Error(wrong)["message"]!.GetValue<string>());

        Assert.NotNull(Result(await h.HelloAsync())["version"]);
    }

    // ── 自省

    [Fact]
    public async Task CommandsListMirrorsTheRegistry()
    {
        await using var h = await Harness.StartAsync();
        await h.HelloAsync();

        var commands = Result(await h.CallAsync(BridgeProtocol.MethodCommandsList, []))["commands"]!.AsArray();

        Assert.Equal(CommandRegistry.All.Count, commands.Count);
        Assert.Equal(CommandRegistry.All.Select(c => c.Path),
            commands.Select(c => c!["path"]!.GetValue<string>()));
        var first = commands[0]!.AsObject();
        Assert.False(string.IsNullOrEmpty(first["brief"]!.GetValue<string>()));
        Assert.False(string.IsNullOrEmpty(first["documentation"]!.GetValue<string>()));
        Assert.Equal("object", first["schema"]!["type"]!.GetValue<string>());
    }

    // ── 执行

    [Fact]
    public async Task ReadCommandComesBackAsBothTextAndData()
    {
        await using var h = await Harness.StartAsync();
        await h.HelloAsync();

        var result = Result(await h.CallAsync(BridgeProtocol.MethodCommandExecute, new JsonObject
        {
            ["path"] = "docs script-api",
            ["arguments"] = new JsonObject(),
        }));

        // 两者出自同一次执行：CLI 默认打 text，--json 打 data，不可能对不上。
        Assert.False(string.IsNullOrEmpty(result["text"]!.GetValue<string>()));
        Assert.NotNull(result["data"]);
    }

    [Fact]
    public async Task UnknownCommandPointsAtTheCommandTree()
    {
        await using var h = await Harness.StartAsync();
        await h.HelloAsync();

        var error = Error(await h.CallAsync(BridgeProtocol.MethodCommandExecute, new JsonObject
        {
            ["path"] = "no such",
            ["arguments"] = new JsonObject(),
        }));

        Assert.Equal(BridgeProtocol.ErrorInvalidRequest, error["code"]!.GetValue<int>());
        Assert.Contains("commands/list", error["message"]!.GetValue<string>());
    }

    // 命令自己报的错原样带出来：code 给机器分支，message 给人看。
    [Fact]
    public async Task CommandErrorKeepsItsCodeAndMessage()
    {
        await using var h = await Harness.StartAsync();
        await h.HelloAsync();

        var error = Error(await h.CallAsync(BridgeProtocol.MethodCommandExecute, new JsonObject
        {
            ["path"] = "setting set",
            ["arguments"] = new JsonObject { ["key"] = "NoSuchSetting", ["value"] = 1 },
        }));

        Assert.Equal(BridgeProtocol.ErrorCommandFailed, error["code"]!.GetValue<int>());
        Assert.Equal("unknown_key", error["data"]!["code"]!.GetValue<string>());
        Assert.Contains("no setting with key", error["message"]!.GetValue<string>());
    }

    // ── 授权（这次连接声明什么档位，就按什么档位来）

    [Fact]
    public async Task DryRunNeverWritesAndSaysHowToActuallyApplyIt()
    {
        await using var h = await Harness.StartAsync();
        await h.HelloAsync();

        var result = Result(await h.CallAsync(BridgeProtocol.MethodCommandExecute, new JsonObject
        {
            ["path"] = "setting set",
            ["arguments"] = new JsonObject { ["key"] = "AutoSaveInterval", ["value"] = OtherThanCurrent() },
            ["authorization"] = BridgeProtocol.AuthReadOnly,
        }));

        Assert.Equal("refused", result["data"]!["outcome"]!.GetValue<string>());
        Assert.Contains("READ-ONLY (advice mode)", result["text"]!.GetValue<string>());
    }

    // 宿主问、客户端答的那一趟：这里答"拒绝"，故什么都不该改。
    [Fact]
    public async Task ConfirmRoundTripReachesTheClientAndRejectionIsHonoured()
    {
        await using var h = await Harness.StartAsync();
        await h.HelloAsync();

        string? asked = null;
        var result = Result(await h.CallAsync(BridgeProtocol.MethodCommandExecute, new JsonObject
        {
            ["path"] = "setting set",
            ["arguments"] = new JsonObject { ["key"] = "AutoSaveInterval", ["value"] = OtherThanCurrent() },
            ["authorization"] = BridgeProtocol.AuthConfirm,
        }, request =>
        {
            asked = (request["params"] as JsonObject)?["actionPhrase"]?.GetValue<string>();
            return new JsonObject { ["decision"] = BridgeProtocol.DecisionReject };
        }));

        // 问的那句话来自命令面（与侧栏卡片同一份措辞），故 CLI 打给用户的与界面上写的是同一件事。
        Assert.NotNull(asked);
        Assert.Contains("change the setting \"AutoSaveInterval\"", asked);
        Assert.Equal("refused", result["data"]!["outcome"]!.GetValue<string>());
        Assert.Contains("chose NOT to allow it", result["text"]!.GetValue<string>());
    }

    // ── 天花板（用户设定压顶客户端声明）

    // 声明 auto 也越不过用户设的 Confirm：此处 canAsk=false（非交互的调用方），故只能如实落空。
    // 少了这一压，本机任何进程只要声明 auto 就比侧栏 agent 权限更大。
    [Fact]
    public async Task DeclaredAutoIsCappedByTheUsersSetting()
    {
        using var ceiling = Ceiling("Confirm");
        await using var h = await Harness.StartAsync();
        await h.HelloAsync();

        var result = Result(await h.CallAsync(BridgeProtocol.MethodCommandExecute, new JsonObject
        {
            ["path"] = "setting set",
            ["arguments"] = new JsonObject { ["key"] = "AutoSaveInterval", ["value"] = OtherThanCurrent() },
            ["authorization"] = BridgeProtocol.AuthAuto,
            ["canAsk"] = false,
        }));

        Assert.Equal("refused", result["data"]!["outcome"]!.GetValue<string>());
        Assert.Contains("no UI is available to ask", result["text"]!.GetValue<string>());
    }

    // 用户设成只读建议时，声明 auto 的外部调用一个字都改不了——与侧栏 agent 同一档待遇。
    [Fact]
    public async Task ReadOnlyCeilingRefusesEvenDeclaredAuto()
    {
        using var ceiling = Ceiling("ReadOnlyAdvice");
        await using var h = await Harness.StartAsync();
        await h.HelloAsync();

        var result = Result(await h.CallAsync(BridgeProtocol.MethodCommandExecute, new JsonObject
        {
            ["path"] = "setting set",
            ["arguments"] = new JsonObject { ["key"] = "AutoSaveInterval", ["value"] = OtherThanCurrent() },
            ["authorization"] = BridgeProtocol.AuthAuto,
        }));

        Assert.Equal("refused", result["data"]!["outcome"]!.GetValue<string>());
        Assert.Contains("READ-ONLY (advice mode)", result["text"]!.GetValue<string>());
    }

    // 握手里如实回天花板：CLI 据它说"--yes 也只能做到这一档"，不必自己去猜宿主的设置。
    [Fact]
    public async Task HelloReportsTheAuthorizationCeiling()
    {
        using var ceiling = Ceiling("ReadOnlyAdvice");
        await using var h = await Harness.StartAsync();

        Assert.Equal(BridgeProtocol.AuthReadOnly, Result(await h.HelloAsync())["authorization"]!.GetValue<string>());
    }

    // 天花板读的是进程级的那一份设置，故用完必须还原：同一个进程里跑着别的测试。
    static IDisposable Ceiling(string value)
    {
        var previous = TuneLab.Configs.Settings.AgentAuthorization.Value;
        TuneLab.Configs.Settings.AgentAuthorization.Value = value;
        return new Restore(() => TuneLab.Configs.Settings.AgentAuthorization.Value = previous);
    }

    sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    static int OtherThanCurrent()
    {
        TuneLab.Configs.SettingsRegistry.AutoSaveInterval.GetValue().ToDouble(out var current);
        return (int)current == 10 ? 11 : 10;   // 合法区间 [10, 60]，取一个与当前不同的值
    }
}
