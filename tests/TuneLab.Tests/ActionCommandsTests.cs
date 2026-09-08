using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using TuneLab.Input;
using Xunit;

namespace TuneLab.Tests;

// 动作面（issue #150 一期）的封条。这里测的是【触发入口不许假装成功】那几条闸：
//  ① 没有编辑器 → 如实说这里永远做不到；
//  ② 不该从这里跑的动作（脚本工具）→ 指路，不开第二套写通道；
//  ③ 此刻跑不动 → 回那条判据的原因，并且真的没跑；
//  ④ 会弹要人应答的模态 → 默认拒绝；
//  ⑤ 后果分档决定过不过授权闸门（AppState 不过、ProjectEdit / Destructive 过）。
//
// ActionRegistry 是进程级静态表，测试进程里空着，故这里注册一批 "test." 前缀的假动作真跑一遍
// ——这条链路（判据 → 闸 → 执行 → 回报状态）值得按真路径验，而不是只验渲染。
[Collection("ActionRegistry")]   // ActionRegistry 是进程级静态表，与 KeymapDispatchTests 串行
public class ActionCommandsTests
{
    static readonly ActionRunCommand Run = new();
    static readonly ActionListCommand List = new();

    // 「坐在编辑器里」的语境：有没有编辑器的判据就是 EditorStatus 在不在（见 IEditorStatusAccess）。
    sealed class StubEditorStatus : IEditorStatusAccess
    {
        public bool IsPlaying { get; set; }
        public double PlayheadTime => 3.24;
        public double? PlayheadTick => 1536;
        public double EndTime => 131;
        public string CurrentToolActionId => "tool.pitch";
        public bool IsParameterPanelVisible => true;
        public bool IsWaveformVisible => false;
        public string? SidebarPanelActionId => "sidebar.showAgent";
        public string? FocusedSurface => "pianoRoll";
    }

    sealed class AutoPolicy : IAuthorizationPolicy
    {
        public AuthorizationMode Mode => AuthorizationMode.Auto;
        public bool CanAsk => false;
        public Task<AuthorizationDecision> AskAsync(AuthorizationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(AuthorizationDecision.ApplyOnce);
    }

    // 闸门问过什么，得能查——分档错了（把丢工作的动作说成"进撤销栈"）是安全性质的错。
    sealed class RecordingPolicy : IAuthorizationPolicy
    {
        public readonly List<AuthorizationRequest> Asked = [];
        public AuthorizationMode Mode => AuthorizationMode.Confirm;
        public bool CanAsk => true;
        public Task<AuthorizationDecision> AskAsync(AuthorizationRequest request, CancellationToken cancellationToken)
        {
            Asked.Add(request);
            return Task.FromResult(AuthorizationDecision.ApplyOnce);
        }
    }

    static CommandContext WithEditor(IAuthorizationPolicy? policy = null)
        => new() { EditorStatus = new StubEditorStatus(), Authorization = policy };

    static CommandResult Trigger(string argumentsJson, CommandContext ctx)
        => Run.ExecuteAsync(CommandArgs.Parse(argumentsJson), ctx, CancellationToken.None).GetAwaiter().GetResult();

    // 注册一条假动作并在用完后撤掉（静态表，不清会漏给别的用例）。ran 记录它到底跑没跑。
    sealed class Probe : System.IDisposable
    {
        public int Ran;
        readonly string mId;

        public Probe(string id, ActionKind kind, string? unavailable = null, bool prompts = false, string? runElsewhere = null)
        {
            mId = id;
            ActionRegistry.Register(new()
            {
                Id = id,
                DisplayName = () => "Probe " + id,
                Kind = kind,
                Unavailable = unavailable == null ? null : () => unavailable,
                Prompts = prompts ? () => true : null,
                RunElsewhere = runElsewhere,
                Execute = () => Ran++,
            });
        }

        public void Dispose() => ActionRegistry.Unregister(mId);
    }

    // ── ① 没有编辑器

    [Fact]
    public void RunSaysThereIsNoEditorInsteadOfBlamingTheId()
    {
        var result = Trigger("""{"id": "transport.play"}""", new CommandContext());

        Assert.True(result.IsError);
        Assert.Equal("no_editor", result.Error!.Value.Code);
        Assert.Equal(EditorStatusText.NoEditor, result.Error!.Value.Message);
    }

    [Fact]
    public void ListSaysThereIsNoEditorWhenTheRegistryIsEmptyForThatReason()
    {
        var data = new JsonObject
        {
            ["total"] = 0,
            ["runnable"] = 0,
            ["hasEditor"] = false,
            ["query"] = null,
            ["actions"] = new JsonArray(),
        };
        Assert.Equal(EditorStatusText.NoEditor, List.Render(data, CommandArgs.Empty));
    }

    // ── ② 不从这里跑

    [Fact]
    public void RunRefusesAnActionThatBelongsToAnotherCommandAndSaysWhichOne()
    {
        using var probe = new Probe("test.elsewhere", ActionKind.ProjectEdit,
            runElsewhere: "run it with `script run-saved` instead");
        var result = Trigger("""{"id": "test.elsewhere"}""", WithEditor(new AutoPolicy()));

        Assert.True(result.IsError);
        Assert.Equal("run_elsewhere", result.Error!.Value.Code);
        Assert.Contains("script run-saved", result.Error!.Value.Message);
        Assert.Equal(0, probe.Ran);
    }

    // ── ③ 此刻跑不动：原因原样回，且真的没跑

    [Fact]
    public void RunReportsTheUnavailableReasonAndDoesNotRunIt()
    {
        using var probe = new Probe("test.unavailable", ActionKind.AppState, unavailable: "there is nothing to undo");
        var result = Trigger("""{"id": "test.unavailable"}""", WithEditor());

        Assert.True(result.IsError);
        Assert.Equal("unavailable", result.Error!.Value.Code);
        Assert.Contains("there is nothing to undo", result.Error!.Value.Message);
        Assert.Contains("Nothing happened", result.Error!.Value.Message);
        Assert.Equal(0, probe.Ran);
    }

    // 判据只有一份：键盘与菜单走的 ActionRegistry.Execute 同样先问它，故三条路径的答案一致。
    [Fact]
    public void RegistryExecuteRefusesAnUnavailableActionItself()
    {
        using var probe = new Probe("test.guarded", ActionKind.AppState, unavailable: "no part is open in the piano roll");

        Assert.Equal("no part is open in the piano roll", ActionRegistry.Execute("test.guarded"));
        Assert.Equal(0, probe.Ran);
    }

    // ── ④ 会弹模态：默认拒绝，显式 opt-in 才放行

    [Fact]
    public void RunRefusesAModalActionUnlessTheCallerSaysSomeoneIsThere()
    {
        using var probe = new Probe("test.prompts", ActionKind.AppState, prompts: true);
        var refused = Trigger("""{"id": "test.prompts"}""", WithEditor());

        Assert.True(refused.IsError);
        Assert.Equal("needs_user_present", refused.Error!.Value.Code);
        Assert.Contains("stops on a dialog", refused.Error!.Value.Message);
        Assert.Equal(0, probe.Ran);

        var allowed = Trigger("""{"id": "test.prompts", "allowPrompt": true}""", WithEditor());
        Assert.False(allowed.IsError, allowed.Error?.Message);
        Assert.Equal(1, probe.Ran);
        // 回报要说清"这件事停在一个等人回答的框上、还没做完"，否则调用方会以为它已经完成了。
        Assert.Contains("does not finish until someone answers it", Run.Render(allowed.Data, CommandArgs.Empty));
    }

    // ── ⑤ 授权分档

    // 只改应用自身状态的动作不过闸门：这个入口连授权策略都没配，它照样能跑（每次播放都弹卡片这个
    // 动作面就没法用了）。
    [Fact]
    public void AnAppStateActionRunsWithoutAnyAuthorizationPolicy()
    {
        using var probe = new Probe("test.appState", ActionKind.AppState);
        var result = Trigger("""{"id": "test.appState"}""", WithEditor());

        Assert.False(result.IsError, result.Error?.Message);
        Assert.Equal("ran", result.Data!["outcome"]!.GetValue<string>());
        Assert.Equal(1, probe.Ran);
    }

    // 改工程的动作没有授权就一条都不做，并且说清是"这个入口没配授权"而不是"用户拒绝了"。
    [Fact]
    public void AProjectEditActionIsRefusedWhenTheEntryPointHasNoAuthorization()
    {
        using var probe = new Probe("test.projectEdit", ActionKind.ProjectEdit);
        var result = Trigger("""{"id": "test.projectEdit"}""", WithEditor());

        Assert.False(result.IsError, result.Error?.Message);
        Assert.Equal("refused", result.Data!["outcome"]!.GetValue<string>());
        Assert.Contains("no authorization policy configured", Run.Render(result.Data, CommandArgs.Empty));
        Assert.Equal(0, probe.Ran);
    }

    // 两档的措辞必须不一样：撤销栈救得回的与救不回的，用户凭这句话做决定。
    [Fact]
    public void TheGateHearsWhichConsequenceItIsBeingAskedAbout()
    {
        var policy = new RecordingPolicy();
        using (var probe = new Probe("test.edit", ActionKind.ProjectEdit))
            Trigger("""{"id": "test.edit"}""", WithEditor(policy));
        using (var probe = new Probe("test.destructive", ActionKind.Destructive))
            Trigger("""{"id": "test.destructive"}""", WithEditor(policy));

        Assert.Equal(2, policy.Asked.Count);
        Assert.Equal(WriteKind.EditorAction, policy.Asked[0].Kind);
        Assert.Contains("goes into the undo history", policy.Asked[0].ActionPhrase());
        Assert.Equal(WriteKind.EditorActionDestructive, policy.Asked[1].Kind);
        Assert.Contains("discard unsaved work", policy.Asked[1].ActionPhrase());
    }

    // ── 身份与回报

    // id 大小写写错不该失败（同 `keybinding set` 的口径）。
    [Fact]
    public void RunAcceptsAnIdWithTheWrongCase()
    {
        using var probe = new Probe("test.appState", ActionKind.AppState);
        var result = Trigger("""{"id": "TEST.APPSTATE"}""", WithEditor());

        Assert.False(result.IsError, result.Error?.Message);
        Assert.Equal("test.appState", result.Data!["id"]!.GetValue<string>());
        Assert.Equal(1, probe.Ran);
    }

    [Fact]
    public void RunBlamesTheIdOnlyWhenThereIsAnEditorToHaveActions()
    {
        var result = Trigger("""{"id": "no.such.action"}""", WithEditor());

        Assert.True(result.IsError);
        Assert.Equal("unknown_id", result.Error!.Value.Code);
        Assert.Contains("list_actions", result.Error!.Value.Message);
    }

    // 即发即忘的动作靠回报里的状态才不瞎：跑完的那一段必须带上执行后的编辑器状态。
    [Fact]
    public void RunReportsTheEditorStateAfterwards()
    {
        using var probe = new Probe("test.appState", ActionKind.AppState);
        var result = Trigger("""{"id": "test.appState"}""", WithEditor());
        var text = Run.Render(result.Data, CommandArgs.Empty);

        Assert.Equal(true, result.Data!["status"]!["hasEditor"]!.GetValue<bool>());
        Assert.Contains("Ran \"Probe test.appState\" (test.appState).", text);
        Assert.Contains("playhead at 0:03.24 (tick 1536)", text);
        // 显示名回落成 id：tool.pitch 在测试进程里没被注册过（真实进程里有），而回落好过留空白。
        Assert.Contains("Tool: \"tool.pitch\" (tool.pitch)", text);
    }

    // ── 列表渲染

    [Fact]
    public void ListRendersEachActionWithWhatYouMustKnowBeforeRunningIt()
    {
        var data = new JsonObject
        {
            ["total"] = 4,
            ["runnable"] = 2,
            ["hasEditor"] = true,
            ["query"] = null,
            ["actions"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "transport.play",
                    ["label"] = "Play/Pause",
                    ["kind"] = "appState",
                    ["unavailable"] = null,
                    ["prompts"] = false,
                    ["runElsewhere"] = null,
                    ["bindable"] = true,
                    ["scope"] = "Editor",
                    ["shortcut"] = new JsonObject { ["token"] = "space", ["display"] = "Space" },
                },
                new JsonObject
                {
                    ["id"] = "edit.undo",
                    ["label"] = "Undo",
                    ["kind"] = "projectEdit",
                    ["unavailable"] = "there is nothing to undo",
                    ["prompts"] = false,
                    ["runElsewhere"] = null,
                    ["bindable"] = true,
                    ["scope"] = "Editor",
                    ["shortcut"] = new JsonObject { ["token"] = "ctrl+z", ["display"] = "Ctrl+Z" },
                },
                new JsonObject
                {
                    ["id"] = "file.open",
                    ["label"] = "Open",
                    ["kind"] = "destructive",
                    ["unavailable"] = null,
                    ["prompts"] = true,
                    ["runElsewhere"] = null,
                    ["bindable"] = true,
                    ["scope"] = "Editor",
                    ["shortcut"] = new JsonObject { ["token"] = "ctrl+o", ["display"] = "Ctrl+O" },
                },
                new JsonObject
                {
                    ["id"] = "script:my-tool",
                    ["label"] = "My tool",
                    ["kind"] = "projectEdit",
                    ["unavailable"] = null,
                    ["prompts"] = true,
                    ["runElsewhere"] = "run it with `script run-saved` (name \"my-tool\")",
                    ["bindable"] = true,
                    ["scope"] = "PianoWindow",
                    ["shortcut"] = null,
                },
            },
        };

        var text = List.Render(data, CommandArgs.Empty);

        Assert.StartsWith("4 action(s), 2 of them runnable right now. Trigger one with run_action(id).", text);
        Assert.Contains("\n- transport.play \"Play/Pause\" [appState] space (Space)", text);
        Assert.Contains("\n- edit.undo \"Undo\" [projectEdit] ctrl+z (Ctrl+Z) — CANNOT RUN NOW: there is nothing to undo", text);
        Assert.Contains("\n- file.open \"Open\" [destructive] ctrl+o (Ctrl+O) — stops on a dialog someone has to answer", text);
        Assert.Contains("\n- script:my-tool \"My tool\" [projectEdit] — NOT FROM HERE: run it with `script run-saved`", text);
    }

    // 一条动作既"不从这里跑"又"现在跑不动"时只说前者：后者是暂时的，前者是永远的，说错会让调用方等下去。
    [Fact]
    public void ListPrefersNotFromHereOverATemporaryReason()
    {
        var data = new JsonObject
        {
            ["total"] = 1,
            ["runnable"] = 0,
            ["hasEditor"] = true,
            ["query"] = null,
            ["actions"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "script:x",
                    ["label"] = "X",
                    ["kind"] = "projectEdit",
                    ["unavailable"] = "no project is open",
                    ["prompts"] = true,
                    ["runElsewhere"] = "use `script run-saved`",
                    ["bindable"] = false,
                    ["scope"] = null,
                    ["shortcut"] = null,
                },
            },
        };

        var text = List.Render(data, CommandArgs.Empty);
        Assert.Contains("NOT FROM HERE", text);
        Assert.DoesNotContain("CANNOT RUN NOW", text);
    }

    // ── editor status

    [Fact]
    public void EditorStatusRendersTheStateAndWhatItMeansForTheClipboardVerbs()
    {
        var status = new EditorStatusCommand();
        var result = status.ExecuteAsync(CommandArgs.Empty, WithEditor(), CancellationToken.None).GetAwaiter().GetResult();
        var text = status.Render(result.Data, CommandArgs.Empty);

        Assert.Contains("Stopped, playhead at 0:03.24 (tick 1536) of 2:11.00.", text);
        Assert.Contains("Parameter panel: open, waveform lane: hidden.", text);
        // 侧栏报的是【动作 id】（同当前工具）；本进程没注册动作，故显示名回落成 id 本身。
        Assert.Contains("Side panel: \"sidebar.showAgent\" (sidebar.showAgent).", text);
        Assert.Contains("Keyboard focus: the piano roll", text);
        Assert.Contains("No part is open in the piano roll.", text);
    }

    // 没有编辑器时不编造字段（在播吗、什么工具都是编辑器才有的事实），只回那一句。
    [Fact]
    public void EditorStatusSaysThereIsNoEditorInsteadOfInventingFields()
    {
        var status = new EditorStatusCommand();
        var result = status.ExecuteAsync(CommandArgs.Empty, new CommandContext(), CancellationToken.None).GetAwaiter().GetResult();

        Assert.False(result.Data!["hasEditor"]!.GetValue<bool>());
        Assert.Null(result.Data!["playing"]);
        Assert.Equal(EditorStatusText.NoEditor, status.Render(result.Data, CommandArgs.Empty));
    }

    // 焦点不在任何编辑面时要说出后果：那批动词此刻没有作用对象（外部驱动时这是最常见的一种）。
    [Fact]
    public void EditorStatusExplainsWhatNoFocusMeans()
    {
        var text = EditorStatusText.Render(new JsonObject
        {
            ["hasEditor"] = true,
            ["playing"] = true,
            ["playheadTime"] = 0,
            ["playheadTick"] = null,
            ["endTime"] = 0,
            ["tool"] = new JsonObject { ["id"] = "tool.note", ["label"] = "Note Tool" },
            ["parameterPanelVisible"] = false,
            ["focusedSurface"] = null,
            ["currentPart"] = null,
            ["quantization"] = null,
        });

        Assert.StartsWith("Playing, playhead at 0:00.00 of 0:00.00.", text);
        Assert.Contains("neither edit surface", text);
        Assert.Contains("until the user clicks into the arrangement or the piano roll", text);
    }

    // 量化报的分母要与工具栏下拉、与 `quantization.*` 那 18 档对得上。
    // 【为何值得一条】三连档是基数 3 × 细分 4 = 1/12；照细分报成 "1/4 triplets" 时，界面上写的是
    // 1/12、动作 id 叫 quantization.1_12，而状态说 1/4——外部无法把三者对上号（已修）。
    [Fact]
    public void EditorStatusReportsQuantizationTheWayTheToolbarAndTheActionIdsSpellIt()
    {
        var text = EditorStatusText.Render(new JsonObject
        {
            ["hasEditor"] = true,
            ["playing"] = false,
            ["playheadTime"] = 0,
            ["playheadTick"] = 0,
            ["endTime"] = 0,
            ["tool"] = new JsonObject { ["id"] = "tool.note", ["label"] = "Note Tool" },
            ["parameterPanelVisible"] = true,
            ["focusedSurface"] = "pianoRoll",
            ["currentPart"] = null,
            ["quantization"] = new JsonObject
            {
                ["division"] = 4,
                ["base"] = 3,
                ["label"] = "1/12",
                ["actionId"] = "quantization.1_12",
            },
        });

        Assert.Contains("Quantization (the snap grid): 1/12 (triplets), i.e. quantization.1_12.", text);
    }
}
