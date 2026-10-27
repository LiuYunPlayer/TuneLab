using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TuneLab.Agent;
using TuneLab.Commands;
using TuneLab.UI;
using Xunit;

namespace TuneLab.Tests;

// 给 agent 读的两段【自由文本】（内置 agent 的系统提示、外部 agent 的接入文案）与命令注册表之间的防漂移封条。
//
// 这两段文字讲的是别的模块能做什么，而加命令 / 改名的人并不知道这里还有一份副本。曾经它们都写着"不能导出音频"，
// `project export-audio` 加上后没人回来改，外部 agent 照着文案主动拒绝了替用户导出——过时的"不能"危害最大，
// 因为 agent 连试都不试。故钉两件事：
//  · 文中点名的工具与 `tunelab <组> <命令>` 必须真实存在（改名 / 删除后留下的死引用）；
//  · 不许出现否定能力的句式——能做什么交给注册表自省，文案只写行为约束（见 AGENTS.md）。
//    描述【运行时状态】的否定句（这份安装没有命令行、桥此刻没开）是事实陈述，逐句列进白名单。
public class AgentGuidanceTextTests
{
    const string FakeExecutable = @"C:\TuneLab\TuneLab.Cli.exe";

    // 接入文案按状态分支生成，每个分支都要过一遍。
    static IEnumerable<(string Name, string Text)> Texts()
    {
        yield return ("system prompt", AgentSideBarContentProvider.SystemPrompt);
        yield return ("onboarding (bridge on)", ExternalAgentOnboarding.Build(FakeExecutable, bridgeEnabled: true));
        yield return ("onboarding (bridge off)", ExternalAgentOnboarding.Build(FakeExecutable, bridgeEnabled: false));
        yield return ("onboarding (no command line)", ExternalAgentOnboarding.Build(null, bridgeEnabled: true));
    }

    static HashSet<string> ToolNames()
    {
        var names = CommandRegistry.All.Select(c => c.AgentToolName).ToHashSet();
        names.Add(AskUserQuestionTool.ToolName);
        return names;
    }

    // 形如工具名、但不是工具名的词（文中出现的其它 snake_case）。新加一条要说得出它为什么不是工具。
    static readonly HashSet<string> NotToolNames = [];

    [Fact]
    public void EveryNamedToolExists()
    {
        var tools = ToolNames();
        var named = Texts()
            .SelectMany(t => Regex.Matches(t.Text, @"(?<![\w.])[a-z]+(?:_[a-z]+)+(?!\w)").Select(m => (t.Name, Word: m.Value)))
            .ToList();
        Assert.NotEmpty(named);   // 一个都没认出来 = 正则失效，封条形同虚设
        var dangling = named
            .Where(x => !tools.Contains(x.Word) && !NotToolNames.Contains(x.Word))
            .Select(x => x.Name + ": " + x.Word)
            .Distinct()
            .ToList();
        Assert.True(dangling.Count == 0, "Agent guidance names tools that do not exist:\n" + string.Join("\n", dangling));
    }

    [Fact]
    public void EveryNamedCommandExists()
    {
        var named = Texts()
            .SelectMany(t => Regex.Matches(t.Text, @"\btunelab ([a-z][a-z-]*) ([a-z][a-z-]*)").Select(m => (t.Name, Path: m.Groups[1].Value + " " + m.Groups[2].Value)))
            .ToList();
        Assert.NotEmpty(named);
        var dangling = named
            .Where(x => !CommandRegistry.TryGet(x.Path, out _))
            .Select(x => x.Name + ": tunelab " + x.Path)
            .Distinct()
            .ToList();
        Assert.True(dangling.Count == 0, "Agent guidance names commands that do not exist:\n" + string.Join("\n", dangling));
    }

    // 运行时状态的否定句：按片段整句认领，片段必须真的还在文中（删了句子就该删掉这条）。
    static readonly string[] StateSentences =
    [
        "so you cannot drive it from a terminal",          // 这份安装没有命令行
        "so the command line cannot reach the",            // 命令桥此刻没开
    ];

    static readonly Regex NegatedCapability = new(
        @"\b(cannot|can't|can not|unable to|not able to|not possible to|there is no [\w-]+ (command|tool)|no (command|tool) (for|to))\b",
        RegexOptions.IgnoreCase);

    [Fact]
    public void NoNegatedCapabilityClaims()
    {
        var offending = new List<string>();
        foreach (var (name, text) in Texts())
        {
            var scrubbed = StateSentences.Aggregate(text, (s, sentence) => s.Replace(sentence, ""));
            foreach (Match m in NegatedCapability.Matches(scrubbed))
            {
                int start = System.Math.Max(0, m.Index - 60);
                offending.Add(name + ": …" + scrubbed.Substring(start, System.Math.Min(scrubbed.Length - start, 140)).ReplaceLineEndings(" ") + "…");
            }
        }
        Assert.True(offending.Count == 0,
            "Agent guidance states what the agent cannot do; describe the behaviour you want instead and let the tool list say what exists:\n"
            + string.Join("\n", offending));
    }

    [Fact]
    public void StateSentencesStillOccur()
    {
        var all = string.Join("\n", Texts().Select(t => t.Text));
        foreach (var sentence in StateSentences)
            Assert.Contains(sentence, all);
    }
}
