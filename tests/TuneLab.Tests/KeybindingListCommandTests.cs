using System.Text.Json.Nodes;
using System.Threading;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using Xunit;

namespace TuneLab.Tests;

// `keybinding list`（搬家前的 list_keybindings）的封条。
//
// Render 用合成 Data 测，一次覆盖真实 Keymap 里未必同时凑齐的组合：已绑定/未绑定、用户改过/用默认/
// 没有默认、有冲突/无冲突、有 query/无 query、注册表为空。
public class KeybindingListCommandTests
{
    static readonly string Preamble = string.Join("\n", [
        "The user changes these themselves in the Settings window's Keybindings page (it has a search box and a per-row reset).",
        KeybindingText.GestureSyntax,
        "Areas (scopes): Global (anywhere), Editor, TrackWindow (arrangement), PianoWindow (piano roll). "
            + "The SAME gesture in DIFFERENT areas is not a conflict — both stay bound and the focused area wins."
            + " Two commands in the SAME area is a conflict (only one fires).",
        "Format: <id> \"<label>\" [area]: <gesture token> (<as shown to the user>)",
    ]);

    [Fact]
    public void RenderKeepsTheWordingItHadBeforeTheMoveToTheCommandSurface()
    {
        var data = new JsonObject
        {
            ["total"] = 7,
            ["query"] = "note",
            ["commands"] = new JsonArray
            {
                // 用户改过绑定 + 有默认 + 同域冲突
                new JsonObject
                {
                    ["id"] = "edit.undo",
                    ["label"] = "撤销",
                    ["scope"] = "Global",
                    ["gesture"] = new JsonObject { ["token"] = "ctrl+z", ["display"] = "Ctrl+Z" },
                    ["hasOverride"] = true,
                    ["default"] = new JsonObject { ["token"] = "ctrl+shift+z", ["display"] = "Ctrl+Shift+Z" },
                    ["conflicts"] = new JsonArray
                    {
                        new JsonObject { ["id"] = "script:foo", ["label"] = "我的脚本" },
                    },
                },
                // 未绑定 + 没有默认 + 无冲突
                new JsonObject
                {
                    ["id"] = "note.split",
                    ["label"] = "拆分音符",
                    ["scope"] = "PianoWindow",
                    ["gesture"] = null,
                    ["hasOverride"] = false,
                    ["default"] = null,
                    ["conflicts"] = new JsonArray(),
                },
            },
        };

        Assert.Equal(string.Join("\n", [
            "7 bindable command(s), 2 matching \"note\". Change one with set_keybinding(id, gesture).",
            Preamble,
            "- edit.undo \"撤销\" [Global]: ctrl+z (Ctrl+Z), changed by the user (default ctrl+shift+z (Ctrl+Shift+Z))",
            "    CONFLICT: the same area also binds this gesture to \"我的脚本\" (script:foo)"
                + " — only one of them fires (the built-in / earliest registered wins). Rebind one of them to fix it.",
            "- note.split \"拆分音符\" [PianoWindow]: (unbound), no default",
        ]), new KeybindingListCommand().Render(data, CommandArgs.Empty));
    }

    // 用默认绑定（未被用户改过）只报 ", default"，不重复给出手势。
    [Fact]
    public void RenderMarksADefaultBindingWithoutRepeatingIt()
    {
        var data = new JsonObject
        {
            ["total"] = 1,
            ["query"] = null,
            ["commands"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "edit.copy",
                    ["label"] = "Copy",
                    ["scope"] = "Editor",
                    ["gesture"] = new JsonObject { ["token"] = "ctrl+c", ["display"] = "Ctrl+C" },
                    ["hasOverride"] = false,
                    ["default"] = new JsonObject { ["token"] = "ctrl+c", ["display"] = "Ctrl+C" },
                    ["conflicts"] = new JsonArray(),
                },
            },
        };

        var text = new KeybindingListCommand().Render(data, CommandArgs.Empty);
        Assert.StartsWith("1 bindable command(s). Change one with set_keybinding(id, gesture).", text);
        Assert.EndsWith("- edit.copy \"Copy\" [Editor]: ctrl+c (Ctrl+C), default", text);
    }

    [Fact]
    public void RenderSaysSoWhenNothingMatchesTheQuery()
    {
        var data = new JsonObject { ["total"] = 7, ["query"] = "zzz", ["commands"] = new JsonArray() };
        var text = new KeybindingListCommand().Render(data, CommandArgs.Empty);

        Assert.StartsWith("7 bindable command(s), 0 matching \"zzz\".", text);
        Assert.EndsWith("\n(no command matches — try a shorter query, or call without one)", text);
    }

    // 注册表为空时整条回报换成一句话（搬家前就是这个特例）。
    [Fact]
    public void RenderReportsAnEmptyRegistryInOneLine()
    {
        var data = new JsonObject { ["total"] = 0, ["query"] = null, ["commands"] = new JsonArray() };
        Assert.Equal("No bindable commands are registered yet.",
            new KeybindingListCommand().Render(data, CommandArgs.Empty));
    }

    // 空表的两种成因不能说成同一句：真的一条没注册（等等也许就有）vs 这个进程根本没有编辑器
    // （等多久都不会有）。后者是命令面多出 headless 入口之后才有的新事实，说错了会让调用方
    // 白等或反复重试。判据取现成的 EditorState 是否为 null。
    [Fact]
    public void RenderSaysWhyWhenThereIsNoEditorInThisProcess()
    {
        var data = new JsonObject { ["total"] = 0, ["hasEditor"] = false, ["query"] = null, ["commands"] = new JsonArray() };
        Assert.StartsWith("No editor is present in this process",
            new KeybindingListCommand().Render(data, CommandArgs.Empty));
    }

    // 未初始化 Keymap 的进程（CI / headless）里不该抛，而应如实回报空注册表。
    [Fact]
    public void DoesNotThrowWhenTheKeymapWasNeverInitialised()
    {
        var command = new KeybindingListCommand();
        var result = command.ExecuteAsync(CommandArgs.Empty, new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.False(result.IsError, result.Error?.Message);
        Assert.NotNull(result.Data!["commands"]);
        Assert.False(string.IsNullOrEmpty(command.Render(result.Data, CommandArgs.Empty)));
    }
}
