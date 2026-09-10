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
        // 挡着界面的东西：这套用例里默认没有，个别用例自己塞。
        public IReadOnlyList<string> BlockingDialogs { get; set; } = [];
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

    // 带【选择器参数】的假动作。Last 记的是**执行真正拿到的那个值**：带参路径最容易错的地方就是
    // 把外部原样给的串直接喂进去（调用方可以给 label），那样动作作用在一个它没校验过的东西上。
    sealed class ParameterProbe : System.IDisposable
    {
        public int Ran;
        public string Last = string.Empty;
        readonly string mId;

        public static readonly ActionArgument[] Tracks =
        [
            new("source:vol", "Volume (sound source)", "hidden"),
            new("effect0:tension", "Tension (Reverb)", "shown"),
        ];

        public ParameterProbe(string id, ActionKind kind = ActionKind.AppState, ActionArgument[]? values = null, string? unavailable = null)
        {
            mId = id;
            var list = values ?? Tracks;
            ActionRegistry.Register(new()
            {
                Id = id,
                DisplayName = () => "Probe " + id,
                Kind = kind,
                Unavailable = unavailable == null ? null : () => unavailable,
                Parameter = new()
                {
                    Name = "track",
                    Description = "Which synthesized parameter track.",
                    Values = () => list,
                    Execute = value => { Ran++; Last = value; },
                },
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

    // ── ⑥ 带选择器参数的动作（动态成员集：值域即判据）

    // 少了参数不去空跑一趟，并且把**此刻的合法值**说出来——那是调用方唯一能自救的信息
    //（成员随工程变，它不可能预先知道）。
    [Fact]
    public void RunAsksForTheArgumentAndSaysWhatIsValidRightNow()
    {
        using var probe = new ParameterProbe("test.selector");
        var result = Trigger("""{"id": "test.selector"}""", WithEditor());

        Assert.True(result.IsError);
        Assert.Equal("needs_argument", result.Error!.Value.Code);
        Assert.Contains("needs a value for \"track\"", result.Error!.Value.Message);
        Assert.Contains("source:vol \"Volume (sound source)\" (hidden)", result.Error!.Value.Message);
        Assert.Contains("Nothing happened", result.Error!.Value.Message);
        Assert.Equal(0, probe.Ran);
    }

    [Fact]
    public void RunRefusesAValueOutsideTheSetAndListsTheOnesInIt()
    {
        using var probe = new ParameterProbe("test.selector");
        var result = Trigger("""{"id": "test.selector", "argument": "source:nope"}""", WithEditor());

        Assert.True(result.IsError);
        Assert.Equal("invalid_argument", result.Error!.Value.Code);
        Assert.Contains("\"source:nope\" is not one of the values", result.Error!.Value.Message);
        Assert.Contains("effect0:tension", result.Error!.Value.Message);
        Assert.Equal(0, probe.Ran);
    }

    // 回报要说清【作用在哪个成员上】：一句"跑了隐藏合成参数轨"看不出隐藏的是哪一条。
    [Fact]
    public void RunActsOnTheValueAndSaysWhichOne()
    {
        using var probe = new ParameterProbe("test.selector");
        var result = Trigger("""{"id": "test.selector", "argument": "effect0:tension"}""", WithEditor());

        Assert.False(result.IsError, result.Error?.Message);
        Assert.Equal("ran", result.Data!["outcome"]!.GetValue<string>());
        Assert.Equal("effect0:tension", result.Data!["argument"]!.GetValue<string>());
        Assert.Equal(1, probe.Ran);
        Assert.Equal("effect0:tension", probe.Last);
        Assert.Contains("Ran \"Probe test.selector\" on \"Tension (Reverb)\" (test.selector effect0:tension).",
            Run.Render(result.Data, CommandArgs.Empty));
    }

    // 认 label 也认 token（一处认一种写法、另一处不认，调用方就会来回试错），但交给动作的**恒是 token**。
    [Fact]
    public void RunAcceptsTheLabelButHandsTheActionTheValue()
    {
        using var probe = new ParameterProbe("test.selector");
        var result = Trigger("""{"id": "test.selector", "argument": "Volume (sound source)"}""", WithEditor());

        Assert.False(result.IsError, result.Error?.Message);
        Assert.Equal("source:vol", probe.Last);
    }

    // 反过来：给一条无参动作塞参数是调用方搞错了动作，照跑会让它以为参数生效了。
    [Fact]
    public void RunRefusesAnArgumentForAnActionThatTakesNone()
    {
        using var probe = new Probe("test.appState", ActionKind.AppState);
        var result = Trigger("""{"id": "test.appState", "argument": "source:vol"}""", WithEditor());

        Assert.True(result.IsError);
        Assert.Equal("unexpected_argument", result.Error!.Value.Code);
        Assert.Equal(0, probe.Ran);
    }

    // "整条动作跑不动"排在参数之前：没开 part 的时候值域必然是空的，报"没有合法值"远不如报那个原因。
    [Fact]
    public void RunPrefersTheUnavailableReasonOverAskingForAnArgument()
    {
        using var probe = new ParameterProbe("test.selector", unavailable: "no part is open in the piano roll");
        var result = Trigger("""{"id": "test.selector"}""", WithEditor());

        Assert.Equal("unavailable", result.Error!.Value.Code);
        Assert.Equal(0, probe.Ran);
    }

    // 键盘/菜单走的 ActionRegistry.Execute 是同一份判据：带参动作没给值同样不跑，且回那句原因。
    [Fact]
    public void RegistryExecuteRefusesAParameterActionWithoutAValue()
    {
        using var probe = new ParameterProbe("test.selector");

        var reason = ActionRegistry.Execute("test.selector");
        Assert.NotNull(reason);
        Assert.Contains("source:vol", reason);
        Assert.Equal(0, probe.Ran);

        Assert.Null(ActionRegistry.Execute("test.selector", "source:vol"));
        Assert.Equal(1, probe.Ran);
    }

    // 授权卡片也要点名成员（同 ExtensionActivationChange 要点名包）：用户在卡片上批的是"对哪一个"。
    [Fact]
    public void TheGateHearsWhichMemberTheActionWouldActOn()
    {
        var policy = new RecordingPolicy();
        using (var probe = new ParameterProbe("test.selectorEdit", ActionKind.ProjectEdit))
            Trigger("""{"id": "test.selectorEdit", "argument": "source:vol"}""", WithEditor(policy));

        Assert.Single(policy.Asked);
        Assert.Equal("Volume (sound source)", policy.Asked[0].NewValue);
        Assert.Contains("on \"Volume (sound source)\"", policy.Asked[0].ActionPhrase());
    }

    // 值域随注册表一起报出去：调用方不必先跑一次失败的 run 才知道能填什么。
    [Fact]
    public void ListReportsTheParameterAndTheValuesValidRightNow()
    {
        using var probe = new ParameterProbe("test.selector");
        var result = List.ExecuteAsync(CommandArgs.Parse("""{"query": "test.selector"}"""), WithEditor(), CancellationToken.None)
            .GetAwaiter().GetResult();

        var action = result.Data!["actions"]!.AsArray()[0]!.AsObject();
        var parameter = action["parameter"]!.AsObject();
        Assert.Equal("track", parameter["name"]!.GetValue<string>());
        Assert.Equal(2, parameter["values"]!.AsArray().Count);
        Assert.Equal("hidden", parameter["values"]![0]!["state"]!.GetValue<string>());
        // 可绑是另一层筛选，而带参动作【不可绑】：一条绑定只有手势、没有参数（v1 的裁决）。
        Assert.False(action["bindable"]!.GetValue<bool>());

        var text = List.Render(result.Data, CommandArgs.Empty);
        Assert.Contains("takes <track>: Which synthesized parameter track.", text);
        Assert.Contains("valid now: source:vol \"Volume (sound source)\" (hidden), effect0:tension \"Tension (Reverb)\" (shown)", text);
    }

    // 值域为空时如实说，不装作"随便填一个也行"。
    [Fact]
    public void ListSaysSoWhenThereIsNothingToPick()
    {
        using var probe = new ParameterProbe("test.selector", values: []);
        var result = List.ExecuteAsync(CommandArgs.Parse("""{"query": "test.selector"}"""), WithEditor(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.Contains("(no valid values right now)", List.Render(result.Data, CommandArgs.Empty));
    }

    // 选项过多时截断并如实说是前几个（同 setting 的选项截断口径）——淹没上下文比少列几个更糟。
    [Fact]
    public void TheValueSummaryIsTruncatedHonestly()
    {
        var values = new ActionArgument[20];
        for (int i = 0; i < values.Length; i++)
            values[i] = new ActionArgument("source:p" + i, "P" + i);

        var text = ActionParameter.Describe(values);
        Assert.Contains("source:p11", text);
        Assert.DoesNotContain("source:p12", text);
        Assert.Contains("first 12 of 20", text);
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

    // 挡着界面的东西要**排在最前面**说，而且要说清后果。
    //
    // 【为何值得一条】模态框期间 Avalonia 跑嵌套消息循环、Dispatcher 照常泵，故命令桥照常应答：
    // 外部可以在用户屏幕被一个框锁着的时候跑一串命令、条条回报成功，而用户一个字都看不见。
    // 那是最难被发现的一种假装成功（每一条单独看都没错），故这一句既要在、又要在最上面——
    // 埋在工具与面板那几行后面就等于藏起来了。
    [Fact]
    public void EditorStatusLeadsWithWhateverIsBlockingTheScreen()
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
            ["quantization"] = null,
            ["blockingDialogs"] = new JsonArray("保存文件"),
        });

        Assert.StartsWith("BLOCKED:", text);
        Assert.Contains("\"保存文件\"", text);           // 用户屏幕上看到的就是这几个字，原样报
        Assert.Contains("still report success", text);   // 说清后果：命令照跑照成功
        Assert.Contains("Only a person can dismiss it", text);
    }

    // 没东西挡着时一个字都不提：一句恒在的"没有弹窗"会把它变成噪音，读的人很快就不看了。
    [Fact]
    public void NothingBlockingMeansNothingIsSaidAboutIt()
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
            ["quantization"] = null,
            ["blockingDialogs"] = new JsonArray(),
        });

        Assert.DoesNotContain("BLOCKED", text);
        Assert.StartsWith("Stopped,", text);
    }

    // 结构化那一半：空 = 空数组而不是缺字段。"问过了，没有"与"这一版不报这一条"对调用方是两件事。
    [Fact]
    public void TheBlockingFieldIsAlwaysThereEvenWhenEmpty()
    {
        var data = EditorStatusText.Build(new CommandContext { EditorStatus = new StubEditorStatus() });
        Assert.NotNull(data["blockingDialogs"]);
        Assert.Empty(data["blockingDialogs"]!.AsArray());

        var blocked = EditorStatusText.Build(new CommandContext
        {
            EditorStatus = new StubEditorStatus { BlockingDialogs = ["Unsaved changes"] },
        });
        Assert.Equal("Unsaved changes", blocked["blockingDialogs"]!.AsArray()[0]!.GetValue<string>());
    }

    // `action run` 的回报与 `editor status` 共用同一份状态文本，故这一句在触发动作之后也必须出现——
    // 那正是它最有用的时刻：动作按下去了，而用户屏幕上还杵着一个框。
    [Fact]
    public void RunningAnActionAlsoSaysTheScreenIsBlocked()
    {
        var ctx = new CommandContext
        {
            EditorStatus = new StubEditorStatus { BlockingDialogs = ["保存文件"] },
            Authorization = new AutoPolicy(),
        };
        var sb = new System.Text.StringBuilder();
        EditorStatusText.Append(sb, EditorStatusText.Build(ctx));

        Assert.StartsWith("BLOCKED:", sb.ToString());
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

    // 带【可选修饰符参数】的假动作：给值走 Parameter.Execute，不给值走 Execute 那条缺省路径
    //（手势与界面菜单走的就是后者）。两条各记一份，才验得出"到底走了哪条"。
    sealed class OptionalParameterProbe : System.IDisposable
    {
        public int RanDefault;
        public int RanWithValue;
        public string Last = string.Empty;
        readonly string mId;

        public OptionalParameterProbe(string id, ActionKind kind = ActionKind.ProjectEdit)
        {
            mId = id;
            ActionRegistry.Register(new()
            {
                Id = id,
                DisplayName = () => "Probe " + id,
                Kind = kind,
                Execute = () => RanDefault++,
                Parameter = new()
                {
                    Name = "parameters",
                    Description = "Whether the curves are transposed along with the notes.",
                    Values = () =>
                    [
                        new ActionArgument("sync", "Transpose the parameters too", "current default"),
                        new ActionArgument("keep", "Move the notes only, leave the curves"),
                    ],
                    Execute = value => { RanWithValue++; Last = value; },
                    Optional = true,
                    DefaultBehavior = "follows the user's \"Parameter Sync Mode\" setting",
                },
            });
        }

        public void Dispose() => ActionRegistry.Unregister(mId);
    }

    // ── 可选修饰符参数（command-surface.md §5.7）

    // 不给值不是错：动作照样跑，走缺省路径。手势按下去就是这一条——键盘无从携带参数。
    [Fact]
    public void RunWithoutAValueTakesTheDefaultPath()
    {
        using var probe = new OptionalParameterProbe("test.optional");
        var result = Trigger("""{"id": "test.optional"}""", WithEditor(new AutoPolicy()));

        Assert.False(result.IsError);
        Assert.Equal(1, probe.RanDefault);
        Assert.Equal(0, probe.RanWithValue);
    }

    // 给了值就与用户的设置无关：走带参路径，且拿到的是**校验过**的 value。
    [Fact]
    public void RunWithAValueTakesTheParameterPath()
    {
        using var probe = new OptionalParameterProbe("test.optional");
        var result = Trigger("""{"id": "test.optional", "argument": "Move the notes only, leave the curves"}""",
            WithEditor(new AutoPolicy()));

        Assert.False(result.IsError);
        Assert.Equal("keep", probe.Last);   // 给的是 label，落到 value 上
        Assert.Equal(1, probe.RanWithValue);
        Assert.Equal(0, probe.RanDefault);
    }

    // 值域外的值仍然拒绝——可选不等于"随便填"，否则拼错一个字就会静默走成另一种行为。
    [Fact]
    public void RunRejectsAValueOutsideTheSetEvenWhenTheParameterIsOptional()
    {
        using var probe = new OptionalParameterProbe("test.optional");
        var result = Trigger("""{"id": "test.optional", "argument": "yes"}""", WithEditor(new AutoPolicy()));

        Assert.True(result.IsError);
        Assert.Equal("invalid_argument", result.Error!.Value.Code);
        Assert.Contains("sync", result.Error!.Value.Message);
        Assert.Equal(0, probe.RanDefault);
        Assert.Equal(0, probe.RanWithValue);
    }

    // 键盘与菜单走的是同一个 ActionRegistry.Execute：不带参数调它就该落到缺省路径，而不是报"缺参数"。
    [Fact]
    public void RegistryExecuteWithoutAValueTakesTheDefaultPath()
    {
        using var probe = new OptionalParameterProbe("test.optional");

        Assert.Null(ActionRegistry.Execute("test.optional"));
        Assert.Equal(1, probe.RanDefault);
        Assert.Equal(0, probe.RanWithValue);
    }

    // 自省：可选性、不给值时的行为、以及此刻哪个值是缺省，都得报出去——否则调用方无从判断
    // "要不要显式给值"。可绑也仍然是 true（与必填参数那批相反）。
    [Fact]
    public void ListReportsOptionalityAndWhatHappensWithoutAValue()
    {
        using var probe = new OptionalParameterProbe("test.optional");
        var result = List.ExecuteAsync(CommandArgs.Parse("""{"query": "test.optional"}"""), WithEditor(), CancellationToken.None)
            .GetAwaiter().GetResult();

        var parameter = result.Data!["actions"]!.AsArray()[0]!.AsObject()["parameter"]!.AsObject();
        Assert.True(parameter["optional"]!.GetValue<bool>());
        Assert.Contains("Parameter Sync Mode", parameter["default"]!.GetValue<string>());

        var text = List.Render(result.Data, CommandArgs.Empty);
        Assert.Contains("optionally takes <parameters>:", text);
        Assert.Contains("without a value: follows the user's \"Parameter Sync Mode\" setting", text);
        Assert.Contains("sync \"Transpose the parameters too\" (current default)", text);
    }

    // 必填参数那一侧一个字都没变：不给值仍然是错，并且回列此刻的合法值。
    [Fact]
    public void RunStillNeedsAValueWhenTheParameterIsRequired()
    {
        using var probe = new ParameterProbe("test.selector");
        var result = Trigger("""{"id": "test.selector"}""", WithEditor());

        Assert.True(result.IsError);
        Assert.Equal("needs_argument", result.Error!.Value.Code);
        Assert.Equal(0, probe.Ran);
    }
}
