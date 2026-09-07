using System;
using System.Text.Json.Nodes;
using System.Threading;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using TuneLab.Scripting;
using Xunit;

namespace TuneLab.Tests;

// `docs script-api` / `docs manual`（搬家前的 get_script_api / get_manual）的措辞封条。
//
// Render 是纯函数（只从 Data 拼文本），故这里【合成 Data】来测——四个分支（目录 / 章节命中 /
// 章节未命中 / 检索）一次覆盖全，且不依赖手册是否随这次构建发布。
// Execute 那一半只做冒烟（手册可用时才有内容可言）。
public class DocsCommandsTests
{
    static string Run(ICommand command, CommandArgs args)
    {
        var result = command.ExecuteAsync(args, new CommandContext(), CancellationToken.None).GetAwaiter().GetResult();
        Assert.False(result.IsError, result.Error?.Message);
        return command.Render(result.Data, args);
    }

    // 这两条【就地可答】：CLI 因此既不连桥也不起无头宿主（外部 agent 第一次接触时，读参考与查手册
    // 零启动、零前提）。下面所有用例都是在空 CommandContext 上跑的，正是这条声明的凭据。
    [Fact]
    public void BothDocsCommandsAnswerWithoutAHost()
    {
        Assert.False(new DocsScriptApiCommand().NeedsHost);
        Assert.False(new DocsManualCommand().NeedsHost);
    }

    [Fact]
    public void ScriptApiReturnsTheReferenceTextVerbatim()
    {
        Assert.Equal(ScriptApiReference.Text, Run(new DocsScriptApiCommand(), CommandArgs.Empty));
    }

    [Fact]
    public void ManualTocKeepsItsWording()
    {
        var text = new DocsManualCommand().Render(new JsonObject
        {
            ["available"] = true,
            ["language"] = "zh-CN",
            ["isCurrentLanguage"] = true,
            ["mode"] = "toc",
            ["text"] = "  ui — 界面",
        }, CommandArgs.Empty);

        // 前三段各自 AppendLine，正文（BuildToc 的产物）原样追加——渲染不代它加换行。
        Assert.Equal(string.Join(Environment.NewLine, [
            "TuneLab user manual (language: zh-CN).",
            "Chapters (id — title (subsections)). Read one with section=<id>, or search with query=<keyword>:",
            "",
            "",
        ]) + "  ui — 界面", text);
    }

    // 语言不匹配时追加的那句 fallback 说明挂在同一行的括号后面（不是单独一行）。
    [Fact]
    public void ManualHeaderExplainsAFallbackEdition()
    {
        var text = new DocsManualCommand().Render(new JsonObject
        {
            ["available"] = true,
            ["language"] = "en-US",
            ["isCurrentLanguage"] = false,
            ["mode"] = "toc",
            ["text"] = "",
        }, CommandArgs.Empty);

        Assert.StartsWith(
            "TuneLab user manual (language: en-US) - no edition in the current UI language, so this is a fallback edition; "
            + "answer the user in their own language regardless of the manual's language.",
            text);
    }

    // 命中的章节：Header + 空行 + 章节正文。
    [Fact]
    public void ManualSectionHitPutsTheHeaderBeforeTheChapter()
    {
        var text = new DocsManualCommand().Render(new JsonObject
        {
            ["available"] = true,
            ["language"] = "zh-CN",
            ["isCurrentLanguage"] = true,
            ["mode"] = "section",
            ["section"] = "ui",
            ["found"] = true,
            ["id"] = "ui",
            ["title"] = "界面",
            ["text"] = "chapter body",
        }, CommandArgs.Empty);

        Assert.Equal("TuneLab user manual (language: zh-CN)." + Environment.NewLine + "\nchapter body", text);
    }

    // 未命中的章节【刻意不带 Header】——搬家前的实现就是这样，别"顺手统一"。
    [Fact]
    public void ManualSectionMissKeepsHavingNoHeader()
    {
        var text = new DocsManualCommand().Render(new JsonObject
        {
            ["available"] = true,
            ["language"] = "zh-CN",
            ["isCurrentLanguage"] = true,
            ["mode"] = "section",
            ["section"] = "nope",
            ["found"] = false,
            ["text"] = "  ui — 界面\n",
        }, CommandArgs.Empty);

        Assert.DoesNotContain("TuneLab user manual", text);
        Assert.StartsWith("No chapter matches \"nope\". Available chapters:", text);
    }

    [Fact]
    public void ManualSearchGroupsHitsByChapter()
    {
        var text = new DocsManualCommand().Render(new JsonObject
        {
            ["available"] = true,
            ["language"] = "zh-CN",
            ["isCurrentLanguage"] = true,
            ["mode"] = "search",
            ["query"] = "颤音",
            ["found"] = true,
            ["hits"] = new JsonArray
            {
                new JsonObject { ["section"] = "params", ["title"] = "参数", ["line"] = "颤音深度" },
                new JsonObject { ["section"] = "params", ["title"] = "参数", ["line"] = "颤音频率" },
                new JsonObject { ["section"] = "ui", ["title"] = "界面", ["line"] = "颤音笔刷" },
            },
        }, CommandArgs.Empty);

        Assert.Equal(string.Join(Environment.NewLine, [
            "TuneLab user manual (language: zh-CN).",
            "Lines matching \"颤音\" (read the whole chapter with section=<id> for context):",
            "",
            "[params] 参数",
            "  颤音深度",
            "  颤音频率",
            "[ui] 界面",
            "  颤音笔刷",
            "",
        ]), text);
    }

    // 手册没随包不是错误：如实说明并把调用方引向自省类命令（不套 "Error: " 前缀，同搬家前）。
    [Fact]
    public void ManualUnavailableIsReportedAsAPlainAnswer()
    {
        var command = new DocsManualCommand();
        var result = command.ExecuteAsync(CommandArgs.Empty, new CommandContext(), CancellationToken.None).GetAwaiter().GetResult();
        Assert.False(result.IsError);

        var text = command.Render(new JsonObject { ["available"] = false }, CommandArgs.Empty);
        Assert.Equal(
            "The user manual is not bundled with this build (Resources/Manual is missing). "
            + "Answer from the introspection tools instead, and say that the manual is unavailable.",
            text);
    }
}
