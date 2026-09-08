using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace TuneLab.Tests;

// 界面入口与动作面之间的防漂移封条。
//
// 动作注册表是「外部够得着」这件事的定义，于是反过来有了一条可机械检查的判据：**一个挂着内联处理器、
// 没有任何对应动作或命令的入口，按定义就是外部够不着的地方**。docs/action-coverage.md 把界面上的每个
// 入口逐条认领（指向一条动作 / 命令，或说清它为什么不该在那儿），这条测试钉住那份清单与代码一致：
//
//  ① 代码里多了一个入口、清单里没有 → 红。**这不是障碍，是提问**：这个新按钮，外部该不该够得着？
//     （新加界面时的正确做法：往那份清单里补一行，写下裁决与理由。）
//  ② 清单里有、代码里没有了 → 红：删了按钮要顺手删那一行，否则清单会慢慢变成考古层。
//  ③ 入口用 SetAction("<id>") 绑了动作，而清单写的是别的裁决 → 红：两边说的必须是同一件事。
//  ④ 清单里写 `action:<id>`，但整个源码里找不到那个 id 的字面量 → 红：动作被改名 / 删掉了，
//     清单跟着烂掉而没人发现。
//
// 【为什么扫源码文本而不是反射运行期注册表】动作由界面在建控件时注册，拿不到就得起一个真窗口；
// 而这条封条要查的恰恰是**没有**进注册表的那些入口——它们在运行期什么都不留下，只有源码里看得见。
public class ActionCoverageTests
{
    const string Doc = "docs/action-coverage.md";

    // 扫描规则与 docs/action-coverage.md §1 的那张表逐条对应，改一处要改两处。
    static readonly Regex NewMenuItem = new(@"new\s+(?:Avalonia\.Controls\.)?MenuItem", RegexOptions.Compiled);
    static readonly Regex Label = new(@"\.Set(?:Tr)?Name\(\s*(?:""([^""]*)""|([A-Za-z_][\w.]*))", RegexOptions.Compiled);
    static readonly Regex SetAction = new(@"\.SetAction\(", RegexOptions.Compiled);
    static readonly Regex SetActionId = new(@"\.SetAction\(\s*""([^""]*)""", RegexOptions.Compiled);
    static readonly Regex StandaloneSetAction = new(@"^\s*([A-Za-z_]\w*)\.SetAction\(", RegexOptions.Compiled);
    static readonly Regex Clicked = new(@"(?:([A-Za-z_]\w*)|\))\s*\.(?:Clicked|Pressed)\s*\+=", RegexOptions.Compiled);
    static readonly Regex Switched = new(@"([A-Za-z_]\w*)\.Switched\.Subscribe", RegexOptions.Compiled);
    static readonly Regex CommandAssign = new(@"([A-Za-z_]\w*)\.Command\s*=", RegexOptions.Compiled);
    static readonly Regex AddButton = new(@"AddButton\(", RegexOptions.Compiled);

    // 一个界面入口：文件 + 文件内稳定 key（同名的按出现次序加 #2）+ 种类 + 它声明触发的动作 id（若有）。
    readonly record struct Entry(string File, string Key, string Kind, string? ActionId);

    // 清单里的一行。
    readonly record struct Claim(string File, string Key, string Kind, string Verdict);

    [Fact]
    public void EveryUiEntryPointIsClaimedByTheCoverageList()
    {
        var root = RepoRoot();
        var entries = Scan(root);

        // 正则写错时"一个都没漏"会变成一句空话，故先钉几个必然存在的入口。
        Assert.True(entries.Count > 100, "the scan found only " + entries.Count + " entry points, which means the rules stopped matching");
        Assert.Contains(new Entry("TuneLab/UI/MainWindow/Editor/Editor.cs", "New", "menu", "file.new"), entries);
        Assert.Contains(new Entry("TuneLab/UI/MainWindow/Editor/SideBar/SideTabBar.cs", "toggle", "toggle", null), entries);

        var claims = ParseDoc(root);
        var claimed = claims.ToDictionary(c => (c.File, c.Key));

        var missing = entries.Where(e => !claimed.ContainsKey((e.File, e.Key))).ToList();
        Assert.True(missing.Count == 0,
            "these UI entry points are not in " + Doc + " — decide for each one whether the outside should be able to reach it, "
            + "then add a row (see the verdict vocabulary in §2):\n"
            + string.Join("\n", missing.Select(e => "  " + e.File + " | " + e.Key + " | " + e.Kind)));

        var scanned = new HashSet<(string, string)>(entries.Select(e => (e.File, e.Key)));
        var stale = claims.Where(c => !scanned.Contains((c.File, c.Key))).ToList();
        Assert.True(stale.Count == 0,
            "these rows in " + Doc + " no longer match any UI entry point (the button was removed or renamed) — drop or update them:\n"
            + string.Join("\n", stale.Select(c => "  " + c.File + " | " + c.Key)));

        foreach (var entry in entries)
        {
            var claim = claimed[(entry.File, entry.Key)];
            Assert.True(entry.Kind == claim.Kind,
                Doc + " calls " + entry.File + " | " + entry.Key + " a \"" + claim.Kind + "\", but the code says \"" + entry.Kind + "\"");

            // 入口自己声明了动作 id 的，清单不许说成别的：那两句话说的是同一件事。
            if (entry.ActionId is { } id)
                Assert.True(claim.Verdict == "action:" + id,
                    entry.File + " | " + entry.Key + " triggers action \"" + id + "\" in code, but " + Doc
                    + " says \"" + claim.Verdict + "\"");
        }
    }

    [Fact]
    public void EveryActionIdInTheCoverageListStillExists()
    {
        var root = RepoRoot();
        var sources = new StringBuilder();
        foreach (var file in SourceFiles(root))
            sources.Append(File.ReadAllText(file)).Append('\n');
        var all = sources.ToString();

        var ids = ParseDoc(root)
            .Where(c => c.Verdict.StartsWith("action:", StringComparison.Ordinal))
            .Select(c => c.Verdict["action:".Length..])
            .Distinct()
            .ToList();

        Assert.NotEmpty(ids);
        var gone = ids.Where(id => !all.Contains('"' + id + '"', StringComparison.Ordinal)).ToList();
        Assert.True(gone.Count == 0,
            Doc + " points at action ids that no longer appear anywhere in the source — they were renamed or removed, "
            + "so those entry points are no longer reachable the way the list claims:\n  " + string.Join("\n  ", gone));
    }

    [Fact]
    public void TheCoverageListCountsItsOwnRows()
    {
        var root = RepoRoot();
        var claims = ParseDoc(root);
        var by = claims.GroupBy(c => c.Verdict.Split(':')[0]).ToDictionary(g => g.Key, g => g.Count());
        int Count(string kind) => by.TryGetValue(kind, out var n) ? n : 0;

        var expected = new[]
        {
            claims.Count, Count("action"), Count("command"), Count("script"), Count("pending"),
            Count("dialog"), Count("internal"), Count("by-design"), Count("todo"),
        };

        var line = File.ReadAllLines(Path.Combine(root, Doc)).FirstOrDefault(l => l.StartsWith("**", StringComparison.Ordinal) && l.Contains("`action`"));
        Assert.NotNull(line);
        var actual = Regex.Matches(line!, @"\d+").Select(m => int.Parse(m.Value)).ToArray();
        Assert.True(expected.SequenceEqual(actual),
            "the summary line in " + Doc + " does not match its own table. Numbers in order "
            + "(total / action / command / script / pending / dialog / internal / by-design / todo) should be: "
            + string.Join(" / ", expected) + "\n  the line says: " + line);
    }

    // ---- 扫描 ----

    static List<Entry> Scan(string root)
    {
        var entries = new List<Entry>();
        foreach (var file in SourceFiles(root))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (rel.StartsWith("TuneLab/Input/", StringComparison.Ordinal))
                continue;   // 基础设施自身（ActionBindings 里的 SetAction 定义）

            var lines = File.ReadAllLines(file);
            var seen = new Dictionary<string, int>();
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                string kind, key;
                string? actionId = null;

                if (NewMenuItem.IsMatch(line))
                {
                    var statement = Statement(lines, i);
                    if (!SetAction.IsMatch(statement))
                        continue;   // 容器项：点了只展开子菜单
                    var label = Label.Match(statement);
                    kind = "menu";
                    key = !label.Success ? "?" : label.Groups[1].Success ? label.Groups[1].Value : label.Groups[2].Value;
                    actionId = IdOf(statement);
                }
                else if (StandaloneSetAction.Match(line) is { Success: true } standalone)
                {
                    key = standalone.Groups[1].Value;
                    kind = DeclaredKind(lines, key);
                    actionId = IdOf(Statement(lines, i));
                }
                else if (Clicked.Match(line) is { Success: true } clicked)
                {
                    kind = AddButton.IsMatch(line) ? "dialog" : "button";
                    key = clicked.Groups[1].Success ? clicked.Groups[1].Value : "(chain)";
                }
                else if (Switched.Match(line) is { Success: true } switched)
                {
                    kind = "toggle";
                    key = switched.Groups[1].Value;
                }
                else if (CommandAssign.Match(line) is { Success: true } command)
                {
                    kind = "menu";
                    key = command.Groups[1].Value;
                }
                else
                {
                    continue;
                }

                seen[key] = seen.TryGetValue(key, out var n) ? n + 1 : 1;
                if (seen[key] > 1)
                    key += "#" + seen[key];
                entries.Add(new Entry(rel, key, kind, actionId));
            }
        }
        return entries;
    }

    static string? IdOf(string statement) => SetActionId.Match(statement) is { Success: true } m ? m.Groups[1].Value : null;

    // 一条语句：从该行起拼到括号平衡且见到分号（最多 16 行，够覆盖链式构造）。
    static string Statement(string[] lines, int start)
    {
        int depth = 0;
        var sb = new StringBuilder();
        for (int i = start; i < Math.Min(start + 16, lines.Length); i++)
        {
            sb.Append(lines[i]).Append('\n');
            foreach (var c in lines[i])
            {
                if (c is '(' or '[' or '{')
                    depth++;
                else if (c is ')' or ']' or '}')
                    depth--;
            }
            if (depth <= 0 && lines[i].Contains(';'))
                break;
        }
        return sb.ToString();
    }

    // 单独一行的 X.SetAction(...)：回头找 X 的 new，据此区分菜单项与按钮。
    static string DeclaredKind(string[] lines, string ident)
    {
        var declaration = new Regex(@"\b" + Regex.Escape(ident) + @"\s*=\s*new\s+([\w.]+)");
        foreach (var line in lines)
            if (declaration.Match(line) is { Success: true } m)
                return m.Groups[1].Value.Contains("MenuItem", StringComparison.Ordinal) ? "menu" : "button";
        return "button";
    }

    static IEnumerable<string> SourceFiles(string root)
        => Directory.EnumerateFiles(Path.Combine(root, "TuneLab"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal);

    // ---- 清单 ----

    static List<Claim> ParseDoc(string root)
    {
        var path = Path.Combine(root, Doc);
        Assert.True(File.Exists(path), Doc + " is missing");

        var claims = new List<Claim>();
        string? file = null;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith("#### ", StringComparison.Ordinal))
            {
                file = line[5..].Trim();
                continue;
            }
            if (file == null || !line.StartsWith("| ", StringComparison.Ordinal))
                continue;

            var cells = line.Split('|').Select(c => c.Trim()).ToList();
            if (cells.Count < 5 || cells[1] is "入口" || cells[1].StartsWith("---", StringComparison.Ordinal))
                continue;

            var verdict = cells[3].Trim('`');
            claims.Add(new Claim(file, cells[1].Replace("\\|", "|"), cells[2], verdict));
        }
        Assert.NotEmpty(claims);
        return claims;
    }

    // 仓库根：从测试程序集所在目录往上找 TuneLab.sln。这条封条查的是**源码**，故必须在仓库里跑。
    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TuneLab.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
