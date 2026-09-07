using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TuneLab.Scripting;
using Xunit;

namespace TuneLab.Tests;

// 脚本 API 的两份说明书与【代码】之间的防漂移封条。
//
// 两份说明书刻意并存，读者不同：
//  · `ScriptApiReference.Text`（编译进来的常量）—— 模型看的精简版，`docs script-api` 的正文，为省 token 而短；
//  · `Resources/ScriptDoc/{文化码}.md` —— 人看的完整版，Script 侧栏 Doc 面渲染，且有译文。
// 谁也不是谁的真源：**真源是运行期的那些对象本身**（`ScriptHandles` / `ScriptRoot` 的句柄、`ScriptConfigs`
// 注册的全局门面；Jint 按名字大小写不敏感地映射过去）。两份文档各自照着代码写，故两个方向都要钉：
//  · 句柄暴露了、说明书没写 → 那个能力对读说明书的人（尤其模型）等于不存在；
//  · 说明书写了、代码没有 → 更糟：模型会自信地去调一个不存在的东西，且错误信息里看不出是文档骗了它。
//
// 这正是本仓库真出过的事：完整版讲了颤音与音高线的关系、精简版没讲；反过来精简版有一族文件对话框的
// 构造方法、完整版没有。两边各改一次就分叉了，而没有任何东西会发现。
public class ScriptApiDocCoverageTests
{
    // 脚本能拿到的句柄类型（`tl` 自己 + 各句柄）。新加一个句柄类型要在这里补一行——那是一次
    // 对脚本可见的表面变更，该由人认领而不是自动跟上。
    static readonly Type[] HandleTypes =
    [
        typeof(ScriptApp), typeof(ScriptProject), typeof(ScriptTrack), typeof(ScriptPart),
        typeof(ScriptNote), typeof(ScriptPhoneme), typeof(ScriptVibrato), typeof(ScriptEffect),
        typeof(ScriptSoundSource), typeof(ScriptTempo), typeof(ScriptTimeSignature),
        typeof(ScriptPlayhead), typeof(ScriptSelection), typeof(ScriptPianoSelection),
    ];

    // 入参 schema 的构造门面【是全局对象而不是句柄】（`SliderConfig.linear(...)`），但同样是脚本 API，
    // 故一并要求两份说明书写全。类型从 ScriptConfigs 的嵌套类型反射得来（名字以 Facade 结尾的是全局，
    // 以 Script 开头的是工厂方法返回的那个链式句柄），故新加一族配置自动纳入这条封条。
    static IEnumerable<Type> ConfigTypes() => typeof(ScriptConfigs)
        .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
        .Where(t => !t.IsInterface
            && (t.Name.EndsWith("Facade", StringComparison.Ordinal) || t.Name.StartsWith("Script", StringComparison.Ordinal)));

    // 这两个是 public 只因为同一外层类里的别处要调它们（宿主侧取真实 config / 解包），不是给脚本用的，
    // 故不要求写进说明书。清单刻意只有两项：它一长，这条封条就开始替漂移打掩护了。
    static readonly HashSet<string> NotForScripts = new(StringComparer.Ordinal) { "build", "inner" };

    // 反射真的看见了 API 才算这条测试成立（万一 BindingFlags 写错，"一个都没漏"会变成一句空话）。
    static readonly string[] Canaries = ["pos", "pitch", "notes", "addPart", "currentProject"];

    static string Terse => ScriptApiReference.Text;

    static IEnumerable<(string Source, string Text)> References()
    {
        yield return ("ScriptApiReference", Terse);

        var folder = TuneLab.PathManager.ScriptDocFolder;
        // 随包资源缺失时不静默通过：这条测试的价值全在"两份都检查"。
        Assert.True(Directory.Exists(folder), "Resources/ScriptDoc is missing from the test output: " + folder);
        var files = Directory.GetFiles(folder, "*.md").OrderBy(p => p).ToArray();
        Assert.NotEmpty(files);
        foreach (var file in files)
            yield return ("Resources/ScriptDoc/" + Path.GetFileName(file), File.ReadAllText(file));
    }

    // 一个类型暴露给脚本的名字：公开实例成员，camelCase（Jint 的成员名比较大小写不敏感，文档一律写 camelCase）。
    static IEnumerable<string> NamesOf(Type type)
    {
        foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (member is not (PropertyInfo or MethodInfo))
                continue;
            if (member is MethodInfo method && method.IsSpecialName)
                continue;   // 属性的 get_/set_
            if (member.Name is "ToString" or "Equals" or "GetHashCode" or "GetType")
                continue;
            yield return char.ToLowerInvariant(member.Name[0]) + member.Name[1..];
        }
    }

    // 说明书该写全的名字 = 句柄的 + 配置门面的，去掉那两个非脚本面的。
    static IEnumerable<string> DocumentedSurface() => HandleTypes.Concat(ConfigTypes())
        .SelectMany(NamesOf).Where(n => !NotForScripts.Contains(n)).Distinct();

    // 全局门面名（脚本里的那个全局对象叫什么）= 类名去掉 Facade，与 ScriptConfigs.Register 注入的一致。
    static IReadOnlyDictionary<string, Type> Facades() => ConfigTypes()
        .Where(t => t.Name.EndsWith("Facade", StringComparison.Ordinal))
        .ToDictionary(t => t.Name[..^"Facade".Length], t => t);

    // 代码有、说明书没写 —— 那个能力对读说明书的人等于不存在。
    [Fact]
    public void EveryNameTheScriptApiExposesIsInEveryReference()
    {
        var names = DocumentedSurface().OrderBy(n => n, StringComparer.Ordinal).ToArray();
        foreach (var canary in Canaries)
            Assert.Contains(canary, names);

        var missing = new List<string>();
        foreach (var (source, text) in References())
        {
            foreach (var name in names)
            {
                if (!text.Contains(name, StringComparison.Ordinal))
                    missing.Add(name + " — missing from " + source);
            }
        }

        Assert.True(missing.Count == 0,
            "the script API and its references have drifted apart (add the missing members to that reference, "
            + "or remove them from the handles):\n  " + string.Join("\n  ", missing));
    }

    // 说明书写了、代码没有。**按接收者精确认定**而不是扫全文里所有的 ".名字"：文档里到处是 JS 自己的
    // 方法（Math.floor / arr.map）和示例中用户自己的对象字段（cfg.targetPitch），把它们一并当成 API
    // 只能靠一张越来越长的例外清单养着，而例外清单会把真正的漂移一起放过去。
    //
    // `tl.` 与那几个全局门面名是【不含歧义】的两个接收者，也正是文档骗人时最致命的两处：
    // 模型照着 `tl.foo()` 调一个不存在的东西，错误信息里看不出是文档的错。
    [Fact]
    public void NeitherReferenceMentionsSomethingTheCodeDoesNotHave()
    {
        var app = NamesOf(typeof(ScriptApp)).ToHashSet(StringComparer.Ordinal);
        var facades = Facades();
        Assert.NotEmpty(facades);

        var receivers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal) { ["tl"] = app };
        foreach (var (name, type) in facades)
            receivers[name] = NamesOf(type).ToHashSet(StringComparer.Ordinal);

        var pattern = new Regex(@"\b(" + string.Join("|", receivers.Keys.Select(Regex.Escape)) + @")\.([A-Za-z][A-Za-z0-9]*)");
        var stray = new List<string>();
        foreach (var (source, text) in References())
        {
            foreach (var match in pattern.Matches(text).Cast<Match>())
            {
                var receiver = match.Groups[1].Value;
                var member = match.Groups[2].Value;
                if (!receivers[receiver].Contains(member))
                    stray.Add(receiver + "." + member + " — in " + source + ", but no such member exists");
            }
        }

        Assert.True(stray.Count == 0,
            "the references promise things the code does not have:\n  " + string.Join("\n  ", stray.Distinct()));
    }
}
