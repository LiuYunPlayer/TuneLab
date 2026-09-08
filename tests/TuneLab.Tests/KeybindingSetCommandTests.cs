using System.Text.Json.Nodes;
using System.Threading;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using Xunit;

namespace TuneLab.Tests;

// `keybinding set`（搬家前的 set_keybinding）的封条。
//
// 措辞里的 "command" 已按术语表统一为 "action"（理由见 KeybindingListCommandTests 的说明）；其余逐字不动。落地那一步会真改用户的键位表，故除了两条不可能
// 落地的错误路径外，全部用合成 Data 测渲染。
//
// 这条命令的措辞里有三处是【安全性质的】，不能被"顺手统一"掉：
//  · 夺键会让另一条命令失去快捷键 —— 必须在回报里点名说出来（用户得知情）；
//  · 等用户确认期间键被别人占走 —— 什么都不做，并说清是"等待期间被占了"；
//  · 跨域同手势不是冲突 —— 要说"两个都还在、聚焦哪层哪层生效"，别报成冲突吓人。
public class KeybindingSetCommandTests
{
    static readonly KeybindingSetCommand Command = new();

    static JsonObject Gesture(string token, string display) => new() { ["token"] = token, ["display"] = display };

    static string Render(JsonObject data) => Command.Render(data, CommandArgs.Empty);

    // 「坐在编辑器里」的语境。id 写错之类的错误只有在有命令目录时才谈得上，故那些用例要显式给一个
    // 编辑器态——四个访问器与选区写口全给 null 就够（这条命令一个都不读，它只是在场与否的判据）。
    sealed class StubEditorState : IEditorStateAccess
    {
        public TuneLab.Data.IMidiPart? CurrentPart => null;
        public TuneLab.Data.IQuantization? Quantization => null;
        public TuneLab.Scripting.ScriptSelection? Selection => null;
        public TuneLab.Scripting.ScriptPianoSelection? PianoSelection => null;
        public TuneLab.Scripting.IScriptSelectionWriter? SelectionWriter => null;
    }

    static CommandContext WithEditor() => new() { EditorState = new StubEditorState() };

    // 没有编辑器的进程里按 id 找不到是【必然】的。照常报"没有这个 id"会让调用方以为自己名字写错了
    // 而反复试——那是把结构性缺席说成拼写错误。故先如实失败（测试进程本身就没有编辑器，天然是这个场景）。
    [Fact]
    public void FailsPlainlyWhenThereIsNoEditorInThisProcess()
    {
        var result = Command.ExecuteAsync(
            CommandArgs.Parse("""{"id": "edit.undo", "gesture": "ctrl+alt+u"}"""),
            new CommandContext(), CancellationToken.None).GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("no_editor", result.Error!.Value.Code);
        Assert.StartsWith("No editor is present in this process", result.Error!.Value.Message);
    }

    // ── 不用改的三种

    [Fact]
    public void AlreadyAtDefaultChangesNothing()
    {
        Assert.Equal("\"Undo\" is already at its default shortcut (ctrl+z (Ctrl+Z)). Nothing changed.", Render(new JsonObject
        {
            ["id"] = "edit.undo",
            ["label"] = "Undo",
            ["outcome"] = "unchanged",
            ["action"] = "reset",
            ["gesture"] = Gesture("ctrl+z", "Ctrl+Z"),
        }));
    }

    [Fact]
    public void AlreadyUnboundChangesNothing()
    {
        Assert.Equal("\"My Tool\" has no shortcut already. Nothing changed.", Render(new JsonObject
        {
            ["id"] = "script:my-tool",
            ["label"] = "My Tool",
            ["outcome"] = "unchanged",
            ["action"] = "unbind",
            ["gesture"] = null,
        }));
    }

    [Fact]
    public void AlreadyBoundToThatGestureChangesNothing()
    {
        Assert.Equal("\"Undo\" is already bound to ctrl+z (Ctrl+Z). Nothing changed.", Render(new JsonObject
        {
            ["id"] = "edit.undo",
            ["label"] = "Undo",
            ["outcome"] = "unchanged",
            ["action"] = "bind",
            ["gesture"] = Gesture("ctrl+z", "Ctrl+Z"),
        }));
    }

    // ── 授权

    [Fact]
    public void RefusedChangeGivesTheGateWordingBackVerbatim()
    {
        var text = Render(new JsonObject
        {
            ["id"] = "edit.undo",
            ["label"] = "Undo",
            ["outcome"] = "refused",
            ["action"] = "bind",
            ["note"] = "Authorization is READ-ONLY (advice mode): I did NOT set the shortcut for the command \"edit.undo\" to Ctrl+Q.",
        });

        Assert.Equal("Authorization is READ-ONLY (advice mode): I did NOT set the shortcut for the command \"edit.undo\" to Ctrl+Q.", text);
    }

    // ── 落地

    [Fact]
    public void BindingReportsTheGestureAndThatItIsSavedImmediately()
    {
        var text = Render(new JsonObject
        {
            ["id"] = "script:my-tool",
            ["label"] = "My Tool",
            ["outcome"] = "applied",
            ["action"] = "bind",
            ["gesture"] = Gesture("ctrl+shift+p", "Ctrl+Shift+P"),
            ["replaced"] = null,
            ["otherScopeUsers"] = new JsonArray(),
            ["conflicts"] = new JsonArray(),
            ["note"] = null,
        });

        Assert.Equal("Bound \"My Tool\" to ctrl+shift+p (Ctrl+Shift+P). Saved; it works right away (no restart).", text);
    }

    // 夺键：被夺的那条命令要点名说出来（知情同意）。
    [Fact]
    public void TakingAGestureOverNamesTheCommandThatLostIt()
    {
        var text = Render(new JsonObject
        {
            ["id"] = "script:my-tool",
            ["label"] = "My Tool",
            ["outcome"] = "applied",
            ["action"] = "bind",
            ["gesture"] = Gesture("ctrl+p", "Ctrl+P"),
            ["replaced"] = new JsonObject { ["id"] = "edit.paste", ["label"] = "Paste" },
            ["otherScopeUsers"] = new JsonArray(),
            ["conflicts"] = new JsonArray(),
            ["note"] = null,
        });

        Assert.Contains(" \"Paste\" (id edit.paste) lost that shortcut and is now unbound — tell the user.", text);
    }

    // 跨域同手势不是冲突：两个都还在，聚焦哪层哪层生效。
    [Fact]
    public void SameGestureInAnotherAreaIsReportedAsCoexistingNotAsAConflict()
    {
        var text = Render(new JsonObject
        {
            ["id"] = "piano.delete",
            ["label"] = "Delete Notes",
            ["outcome"] = "applied",
            ["action"] = "bind",
            ["gesture"] = Gesture("delete", "Delete"),
            ["replaced"] = null,
            ["otherScopeUsers"] = new JsonArray { "Delete Parts" },
            ["conflicts"] = new JsonArray(),
            ["note"] = null,
        });

        Assert.Contains(" Note: \"Delete Parts\" also use(s) this gesture in another area — both stay active, the focused area wins.", text);
        Assert.DoesNotContain("CONFLICT", text);
    }

    // 同域冲突（改完仍在）：那一句与 `keybinding list` 共用一份格式化，措辞必须一致。
    [Fact]
    public void RemainingSameAreaConflictIsSpelledOutAfterTheChange()
    {
        var text = Render(new JsonObject
        {
            ["id"] = "script:my-tool",
            ["label"] = "My Tool",
            ["outcome"] = "applied",
            ["action"] = "bind",
            ["gesture"] = Gesture("ctrl+k", "Ctrl+K"),
            ["replaced"] = null,
            ["otherScopeUsers"] = new JsonArray(),
            ["conflicts"] = new JsonArray { new JsonObject { ["id"] = "script:other", ["label"] = "Other" } },
            ["note"] = null,
        });

        Assert.EndsWith(" CONFLICT: the same area also binds this gesture to \"Other\" (script:other)"
            + " — only one of them fires (the built-in / earliest registered wins). Rebind one of them to fix it.", text);
    }

    [Fact]
    public void ResetWithoutADefaultSaysThereIsNoShortcutNow()
    {
        var text = Render(new JsonObject
        {
            ["id"] = "script:my-tool",
            ["label"] = "My Tool",
            ["outcome"] = "applied",
            ["action"] = "reset",
            ["gesture"] = null,
            ["conflicts"] = new JsonArray(),
            ["note"] = null,
        });

        Assert.Equal("Reset \"My Tool\" to its default shortcut: no shortcut. Saved; it works right away (no restart).", text);
    }

    [Fact]
    public void UnbindingSaysSo()
    {
        var text = Render(new JsonObject
        {
            ["id"] = "edit.undo",
            ["label"] = "Undo",
            ["outcome"] = "applied",
            ["action"] = "unbind",
            ["conflicts"] = new JsonArray(),
            ["note"] = null,
        });

        Assert.Equal("Removed the shortcut for \"Undo\". Saved; it works right away (no restart).", text);
    }

    // 等用户确认期间键被别人占走：什么都不做，并说清是等待期间被占的。
    [Fact]
    public void GestureTakenWhileWaitingChangesNothingAndSaysWhy()
    {
        var text = Render(new JsonObject
        {
            ["id"] = "script:my-tool",
            ["label"] = "My Tool",
            ["outcome"] = "taken_meanwhile",
            ["action"] = "bind",
            ["gesture"] = Gesture("ctrl+p", "Ctrl+P"),
            ["conflict"] = new JsonObject { ["id"] = "edit.paste", ["label"] = "Paste" },
            ["note"] = null,
        });

        Assert.Equal("Nothing changed: ctrl+p (Ctrl+P) got taken by \"Paste\" (id edit.paste) while waiting for the user."
            + " Pick another gesture, or call again with replaceConflict = true.", text);
    }

    // ── 不可能落地的两条错误路径（Keymap 未初始化的进程里也稳定成立）

    [Fact]
    public void EmptyIdPointsAtTheList()
    {
        var result = Command.ExecuteAsync(CommandArgs.Parse("""{"id":"  "}"""), WithEditor(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("empty_id", result.Error!.Value.Code);
    }

    [Fact]
    public void UnknownCommandIdExplainsHowScriptCommandsAppear()
    {
        var result = Command.ExecuteAsync(CommandArgs.Parse("""{"id":"no.such.command","gesture":"ctrl+q"}"""), WithEditor(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("unknown_id", result.Error!.Value.Code);
        Assert.StartsWith("no bindable action with id \"no.such.command\".", result.Error!.Value.Message);
        Assert.Contains("becomes \"script:<its id>\"", result.Error!.Value.Message);
    }
}
