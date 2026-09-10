using Avalonia.Input;
using TuneLab.Input;
using Xunit;
using KeyBinding = TuneLab.GUI.Input.KeyBinding;   // 消歧：Avalonia.Input 也有 KeyBinding

namespace TuneLab.Tests;

// ActionRegistry / Keymap 是进程级静态表。动它们的测试类必须串行，否则两个类并行 Register 会同时写
// 同一个 Dictionary（非线程安全）。
[CollectionDefinition("ActionRegistry")]
public class ActionRegistryCollection { }

// 键盘分发的封条（issue #150 把这条路径改成了「查表 → ActionRegistry.Execute」，判据从各 Execute
// 里上移到 EditorAction.Unavailable）。
//
// 【为什么值得自动化】这一层是这次重构里唯一「用户按键就会撞上」的地方，而键按下来的样子测不到
// （没有按键注入的基建），可 TryHandle 只读 e.Key 与 e.KeyModifiers——喂一个合成的 KeyEventArgs
// 就能把「哪个手势在哪个作用域触发了哪条动作」验完整。
[Collection("ActionRegistry")]
public class KeymapDispatchTests
{
    static KeyEventArgs Press(Key key, KeyModifiers modifiers = KeyModifiers.None)
        => new() { Key = key, KeyModifiers = modifiers };

    // 一条一次性的假动作：跑过几次、注册完自动撤掉（静态表，不清会漏给别的用例）。
    sealed class Probe : System.IDisposable
    {
        public int Ran;
        readonly string mId;

        public Probe(string id, KeyScope scope, KeyBinding gesture, string? unavailable = null)
        {
            mId = id;
            Keymap.Register(new()
            {
                Id = id,
                DisplayName = () => id,
                Kind = ActionKind.AppState,
                Unavailable = unavailable == null ? null : () => unavailable,
                Execute = () => Ran++,
            }, scope, gesture);
        }

        public void Dispose() => Keymap.Unregister(mId);
    }

    [Fact]
    public void AGestureInItsOwnScopeTriggersTheAction()
    {
        using var probe = new Probe("test.dispatch.hit", KeyScope.Editor, new(Key.F7));

        Assert.True(Keymap.TryHandle(KeyScope.Editor, Press(Key.F7)));
        Assert.Equal(1, probe.Ran);
    }

    // 未命中不能吞键：调用方据返回值置 e.Handled，吞了就等于把键从下游控件手里抢走。
    [Fact]
    public void AnUnboundGestureIsNotHandled()
    {
        using var probe = new Probe("test.dispatch.miss", KeyScope.Editor, new(Key.F7));

        Assert.False(Keymap.TryHandle(KeyScope.Editor, Press(Key.F8)));
        Assert.False(Keymap.TryHandle(KeyScope.Editor, Press(Key.F7, KeyModifiers.Control)));
        Assert.Equal(0, probe.Ran);
    }

    // 作用域是分发的一部分：同一手势在别的作用域下不该命中（内层优先由 Avalonia 的冒泡给出，
    // 每个能收键的控件只用自身 scope 问一次）。
    [Fact]
    public void TheSameGestureInAnotherScopeDoesNotFire()
    {
        using var piano = new Probe("test.dispatch.piano", KeyScope.PianoWindow, new(Key.F7));

        Assert.False(Keymap.TryHandle(KeyScope.Editor, Press(Key.F7)));
        Assert.False(Keymap.TryHandle(KeyScope.Global, Press(Key.F7)));
        Assert.True(Keymap.TryHandle(KeyScope.PianoWindow, Press(Key.F7)));
        Assert.Equal(1, piano.Ran);
    }

    // 修饰键要按位精确匹配，不是"包含即算"。
    [Fact]
    public void ModifiersMustMatchExactly()
    {
        using var probe = new Probe("test.dispatch.mods", KeyScope.Editor, new(Key.F7, KeyModifiers.Control | KeyModifiers.Shift));

        Assert.False(Keymap.TryHandle(KeyScope.Editor, Press(Key.F7, KeyModifiers.Control)));
        Assert.True(Keymap.TryHandle(KeyScope.Editor, Press(Key.F7, KeyModifiers.Control | KeyModifiers.Shift)));
        Assert.Equal(1, probe.Ran);
    }

    // 【行为等价性的封条】动作此刻不可用时，键仍然算处理过——与重构前"守卫在 Execute 内部直接
    // return、键照样被吞"逐字等价。判据搬了家（EditorAction.Unavailable），键盘的观感一点不能变：
    // 若这里改成不吞键，Delete 这种键就会漏给下游控件，变成"撤销栈空时按 Delete 删掉了别的东西"。
    [Fact]
    public void AnUnavailableActionStillSwallowsTheKeyButDoesNotRun()
    {
        using var probe = new Probe("test.dispatch.guarded", KeyScope.Editor, new(Key.F7), unavailable: "there is nothing to undo");

        Assert.True(Keymap.TryHandle(KeyScope.Editor, Press(Key.F7)));
        Assert.Equal(0, probe.Ran);
    }

    // 同作用域撞键（手改 Keybindings.json、多脚本声明同一默认手势）时确定性取胜：注册序最小者。
    // 内建启动期先注册，故第三方脚本夺不走它的键。
    [Fact]
    public void TheEarliestRegisteredActionWinsAClash()
    {
        using var first = new Probe("test.dispatch.first", KeyScope.Editor, new(Key.F7));
        using var second = new Probe("test.dispatch.second", KeyScope.Editor, new(Key.F7));

        Assert.True(Keymap.TryHandle(KeyScope.Editor, Press(Key.F7)));
        Assert.Equal(1, first.Ran);
        Assert.Equal(0, second.Ran);

        // 冲突不隐藏：设置页据此持久警示，两边互相点名。
        Assert.Contains("test.dispatch.second", Keymap.SameScopeConflictPeers("test.dispatch.first"));
        Assert.Contains("test.dispatch.first", Keymap.SameScopeConflictPeers("test.dispatch.second"));
    }

    // 注销之后手势立刻失效（脚本消失即不再分发；用户 override 另存，脚本回归即复活）。
    [Fact]
    public void UnregisteringDropsTheGestureFromDispatch()
    {
        var probe = new Probe("test.dispatch.gone", KeyScope.Editor, new(Key.F7));
        Assert.True(Keymap.TryHandle(KeyScope.Editor, Press(Key.F7)));

        probe.Dispose();

        Assert.False(Keymap.TryHandle(KeyScope.Editor, Press(Key.F7)));
        Assert.Equal(1, probe.Ran);
    }

    // 菜单点击走的是同一个 ActionRegistry.Execute（ActionBindings.SetAction），故不可用时
    // 菜单点了也不该执行——与键盘同一份判据。
    [Fact]
    public void MenuPathSharesTheSameAvailabilityJudgement()
    {
        using var probe = new Probe("test.dispatch.menu", KeyScope.Editor, new(Key.F7), unavailable: "no part is open in the piano roll");

        Assert.Equal("no part is open in the piano roll", ActionRegistry.Execute("test.dispatch.menu"));
        Assert.Equal(0, probe.Ran);
    }

    // 【可选修饰符参数的动作可以绑手势】必填参数的不行（一条绑定只有手势、没有参数，按下去无从知道
    // 作用在哪个成员上），但可选的那种缺省路径就是"按用户设置来"，正是手势要的行为——四条移调动作
    // 即是（command-surface.md §5.7）。这里连注册带分发一起验：Register 的断言放宽了，按下去也必须
    // 落到缺省那条 Execute，而不是报"缺参数"。
    [Fact]
    public void AnActionWithAnOptionalParameterCanBeBoundAndTheGestureTakesTheDefaultPath()
    {
        int ranDefault = 0;
        int ranWithValue = 0;
        Keymap.Register(new()
        {
            Id = "test.dispatch.optional",
            DisplayName = () => "test.dispatch.optional",
            Kind = ActionKind.AppState,
            Execute = () => ranDefault++,
            Parameter = new()
            {
                Name = "parameters",
                Description = "Whether the curves move along.",
                Values = () => [new ActionArgument("sync", "Move them too"), new ActionArgument("keep", "Notes only")],
                Execute = _ => ranWithValue++,
                Optional = true,
                DefaultBehavior = "follows the user's setting",
            },
        }, KeyScope.Editor, new(Key.F8));

        try
        {
            Assert.True(Keymap.TryHandle(KeyScope.Editor, Press(Key.F8)));
            Assert.Equal(1, ranDefault);
            Assert.Equal(0, ranWithValue);
        }
        finally
        {
            Keymap.Unregister("test.dispatch.optional");
        }
    }

    // 【最外层兜底分发】焦点丢了（撤销把钢琴窗里的 part 删掉后，原先聚焦的控件随之从视觉树摘除）之后，
    // 按键只在窗口上触发、冒泡不到 Editor，工程级快捷键因此失灵过（Ctrl+Y 没反应，而菜单里的重做照常）。
    // 兜底把 Editor 域接住。
    [Fact]
    public void TheOutermostFallbackHandlesEditorScopeActions()
    {
        using var probe = new Probe("test.fallback.editor", KeyScope.Editor, new(Key.Y, KeyModifiers.Control));

        Assert.True(Keymap.TryHandleFallback(Press(Key.Y, KeyModifiers.Control)));
        Assert.Equal(1, probe.Ran);
    }

    [Fact]
    public void TheOutermostFallbackHandlesGlobalScopeActions()
    {
        using var probe = new Probe("test.fallback.global", KeyScope.Global, new(Key.F11));

        Assert.True(Keymap.TryHandleFallback(Press(Key.F11)));
        Assert.Equal(1, probe.Ran);
    }

    // 面内动作**不**兜：删音符/切工具那类事，在那个面没有焦点时执行才是错的。
    [Fact]
    public void TheOutermostFallbackLeavesSurfaceScopedActionsAlone()
    {
        using var piano = new Probe("test.fallback.piano", KeyScope.PianoWindow, new(Key.F9));
        using var track = new Probe("test.fallback.track", KeyScope.TrackWindow, new(Key.F10));

        Assert.False(Keymap.TryHandleFallback(Press(Key.F9)));
        Assert.False(Keymap.TryHandleFallback(Press(Key.F10)));
        Assert.Equal(0, piano.Ran);
        Assert.Equal(0, track.Ran);
    }
}
