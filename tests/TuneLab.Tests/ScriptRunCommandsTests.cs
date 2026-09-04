using System.Text.Json.Nodes;
using System.Threading;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using Xunit;

namespace TuneLab.Tests;

// `script run` / `script run-saved`（搬家前的 run_script / run_saved_script）的封条。两条共用同一条
// 写路径（ScriptWriteExecutor），故回报的渲染也只有一份——这里测的就是那一份。
//
// 这些措辞是 agent 判断"我的编辑到底落地没有"的唯一依据，四种"没落地"必须彼此可分：
// 用户正在操作（稍后重试）、只读档（去提权）、没法问（换档位）、用户拒绝（别再问）。
// 混成一句"没有应用"，模型要么重复尝试、要么向用户谎报已完成。
public class ScriptRunCommandsTests
{
    static string Render(JsonObject data) => ScriptWriteExecutor.Render(data);

    [Fact]
    public void AppliedRunReportsTheEditCountAsOneUndoableChange()
    {
        Assert.Equal("Script ran OK. Applied 12 edit(s) as one undoable change.", Render(new JsonObject
        {
            ["outcome"] = "applied",
            ["previewed"] = false,
            ["changes"] = 12,
            ["error"] = null,
            ["output"] = null,
            ["resultText"] = null,
            ["note"] = null,
        }));
    }

    [Fact]
    public void OutputAndResultAreAppendedUnderTheirOwnHeadings()
    {
        var text = Render(new JsonObject
        {
            ["outcome"] = "applied",
            ["previewed"] = false,
            ["changes"] = 1,
            ["output"] = "hello",
            ["resultText"] = "42",
            ["note"] = null,
        });

        Assert.Equal("Script ran OK. Applied 1 edit(s) as one undoable change.\n--- output ---\nhello\n--- result ---\n42", text);
    }

    // 确认过这件事本身要留在回报里，否则 Confirm 档与 Auto 档的回报一字不差。
    [Fact]
    public void ConfirmationNoteStaysInFrontOfTheResult()
    {
        var text = Render(new JsonObject
        {
            ["outcome"] = "applied",
            ["previewed"] = false,
            ["changes"] = 3,
            ["output"] = null,
            ["resultText"] = null,
            ["note"] = "(The user was asked to confirm and approved these edits; authorization stays at Confirm, so your next edit will ask again.)\n",
        });

        Assert.StartsWith("(The user was asked to confirm and approved these edits;", text);
        Assert.EndsWith("Script ran OK. Applied 3 edit(s) as one undoable change.", text);
    }

    // 脚本出错：明说已全部回退，且别在当前状态上打补丁——那是最容易被模型做错的一步。
    [Fact]
    public void ScriptErrorSaysEverythingWasRolledBack()
    {
        var text = Render(new JsonObject
        {
            ["outcome"] = "error",
            ["previewed"] = false,
            ["changes"] = 0,
            ["error"] = "TypeError: x is not a function",
            ["output"] = "before the crash",
            ["resultText"] = null,
            ["note"] = null,
        });

        Assert.Equal("Script error: TypeError: x is not a function"
            + "\n(All changes were rolled back; the project is unchanged. Fix the script and re-run — do not patch from current state.)"
            + "\n--- output ---\nbefore the crash", text);
    }

    // ── 四种"没落地"，各说各的下一步

    [Fact]
    public void BlockedByTheUserEditingSaysToRetryLater()
    {
        Assert.Equal("The user is editing the project right now, so the script did not run and nothing was changed."
            + " Wait a moment and try again, or ask the user to finish their current edit.",
            Render(new JsonObject { ["outcome"] = "blocked" }));
    }

    [Fact]
    public void ReadOnlyAdviceSaysWhatItWouldHaveDone()
    {
        var text = Render(new JsonObject { ["outcome"] = "advice", ["changes"] = 7, ["output"] = null });

        Assert.Equal("Authorization is READ-ONLY (advice mode): the script ran and WOULD apply 7 edit(s), but NOTHING was changed."
            + " Explain the plan to the user; to actually apply it, ask them to set agent authorization to Confirm or Auto, or run the script manually.", text);
    }

    [Fact]
    public void ConfirmModeWithNoWayToAskSaysToSwitchAuthorization()
    {
        Assert.Equal("Confirmation is required (Confirm mode) but no UI is available to ask, so the 4 edit(s) were NOT applied."
            + " Ask the user to apply manually or switch authorization to Auto.",
            Render(new JsonObject { ["outcome"] = "cannot_ask", ["changes"] = 4 }));
    }

    [Fact]
    public void RejectedRunSaysTheUserReviewedAndDeclined()
    {
        Assert.Equal("The user reviewed the 5 proposed edit(s) and chose NOT to apply them. Nothing was changed.",
            Render(new JsonObject { ["outcome"] = "rejected", ["changes"] = 5 }));
    }

    // 入口没配授权策略：连预览都不跑（预览虽会回退，跑的仍是会改工程的用户代码）。
    [Fact]
    public void WithoutAPolicyTheScriptIsNotRunAtAll()
    {
        var text = Render(new JsonObject { ["outcome"] = "no_policy" });

        Assert.Contains("no authorization policy configured", text);
        Assert.Contains("the script was NOT run", text);
    }

    // 预览与真跑的"没有改动"是两句话：一句说"不会产生"，一句说"没有做"。
    [Fact]
    public void NoChangesReadsDifferentlyForAPreviewAndForARealRun()
    {
        Assert.Equal("Script ran OK. No changes were produced.",
            Render(new JsonObject { ["outcome"] = "no_changes", ["previewed"] = true, ["changes"] = 0, ["output"] = null }));
        Assert.Equal("Script ran OK. No changes were made.",
            Render(new JsonObject { ["outcome"] = "no_changes", ["previewed"] = false, ["changes"] = 0, ["output"] = null, ["resultText"] = null, ["note"] = null }));
    }

    // ── 参数准入（不跑脚本也成立的两条）

    [Fact]
    public void EmptyCodeIsRejected()
    {
        var result = new ScriptRunCommand()
            .ExecuteAsync(CommandArgs.Parse("""{"code":"   "}"""), new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("empty_code", result.Error!.Value.Code);
    }

    [Fact]
    public void RunningAnUnknownSavedScriptPointsAtTheList()
    {
        var result = new ScriptRunSavedCommand()
            .ExecuteAsync(CommandArgs.Parse("""{"name":"no-such-script-xyz"}"""), new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.True(result.IsError);
        Assert.Equal("not_found", result.Error!.Value.Code);
    }
}
