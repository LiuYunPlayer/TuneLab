using System.Text.Json.Nodes;
using System.Threading;
using TuneLab.Commands;
using TuneLab.Commands.Handlers;
using Xunit;

namespace TuneLab.Tests;

// `extension list`（搬家前的 list_extensions）的封条。渲染用合成 Data 测——一次覆盖全分支，
// 且不依赖这台机器上装了什么插件。
//
// 这份清单的价值几乎全在那些【否定性事实】上：装了但被关掉、加载了但被别的包顶替、有摘要但那是
// 转述不是作者原话。它们一旦被"顺手统一"掉，调用方就会理直气壮地向用户保证一个本次运行根本不存在
// 的能力，故逐条钉住。
public class ExtensionListCommandTests
{
    static readonly ExtensionListCommand Command = new();

    static string Render(JsonArray packages, int missingSummaries = 0, bool summarizerAvailable = true)
        => Command.Render(new JsonObject
        {
            ["packages"] = packages,
            ["missingSummaries"] = missingSummaries,
            ["summarizerAvailable"] = summarizerAvailable,
        }, CommandArgs.Empty);

    static JsonObject Package(string? id, string name, string status = "Loaded", JsonArray? entries = null) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["version"] = "1.2.3",
        ["generation"] = "V1",
        ["status"] = status,
        ["types"] = new JsonArray { "voice" },
        ["author"] = null,
        ["description"] = null,
        ["error"] = null,
        ["entries"] = entries ?? new JsonArray(),
        ["routing"] = new JsonArray(),
    };

    static JsonObject Entry(string kind, string identity, string displayName, string status = "Registered") => new()
    {
        ["kind"] = kind,
        ["identities"] = new JsonArray { identity },
        ["displayName"] = displayName,
        ["status"] = status,
        ["error"] = null,
        ["hasIntroduction"] = false,
        ["summary"] = null,
        ["routing"] = new JsonArray(),
    };

    static JsonObject Routing(string kind, string identity, bool active, string activePackage, params string[] others) => new()
    {
        ["kind"] = kind,
        ["identity"] = identity,
        ["active"] = active,
        ["activePackage"] = activePackage,
        ["otherPackages"] = new JsonArray(System.Array.ConvertAll(others, o => (JsonNode?)o)),
    };

    // ── 包这一层

    [Fact]
    public void NoExtensionsSaysOnlyBuiltInsAreThere()
    {
        Assert.Equal("No extensions are installed. TuneLab is running with only its built-in capabilities.",
            Render(new JsonArray()));
    }

    [Fact]
    public void PackageLineCarriesIdVersionGenerationStatusAndKinds()
    {
        var package = Package("pkg.a", "示例包");
        package["author"] = "某作者";
        package["description"] = "一套示例插件";

        var text = Render(new JsonArray { package });

        Assert.StartsWith("1 extension(s) installed:", text);
        Assert.Contains("\n- \"示例包\" [id=pkg.a, v1.2.3, V1, status=Loaded]  kinds: voice  by 某作者", text);
        Assert.Contains("\n    一套示例插件", text);
    }

    // Legacy 包没有 id，但仍要列出来——它照样会参与冲突、照样能被用户装着。
    [Fact]
    public void LegacyPackageWithoutIdStillGetsALine()
    {
        Assert.Contains("[id=(legacy, no id), v1.2.3", Render(new JsonArray { Package(null, "老包") }));
    }

    // 整包被关掉：装了 ≠ 能用。这一句缺了，模型会照着 kinds 那行向用户保证一个没注册的能力。
    [Fact]
    public void DisabledPackageSaysNoneOfItsCapabilitiesExist()
    {
        var text = Render(new JsonArray
        {
            Package("pkg.a", "示例包", "Disabled", new JsonArray { Entry("voice", "v1.voice", "示例引擎", "Disabled") }),
        });

        Assert.Contains("\n    DISABLED by the user — installed but switched off, so NONE of its capabilities exist in this session."
            + " Re-enable it in the Extensions sidebar or with set_extension_enabled (takes effect after a restart).", text);
        // 整包被禁时逐条目也标一遍——调用方常只读到自己关心的那一行。
        Assert.Contains("provides voice:v1.voice \"示例引擎\"  [NOT AVAILABLE: the whole package is disabled]", text);
    }

    [Fact]
    public void PackageErrorIsGivenAsANote()
    {
        var package = Package("pkg.a", "示例包", "PartiallyLoaded");
        package["error"] = "one entry failed";

        Assert.Contains("\n    note: one entry failed", Render(new JsonArray { package }));
    }

    // ── 能力位这一层

    // 单个能力位被关掉与整包被关掉是两回事：前者其余能力还在，措辞不能混。
    [Fact]
    public void OneCapabilityDisabledSaysTheRestOfThePackageStillWorks()
    {
        var text = Render(new JsonArray
        {
            Package("pkg.a", "示例包", "Loaded", new JsonArray { Entry("effect", "v1.effect", "示例效果器", "Disabled") }),
        });

        Assert.Contains("[DISABLED by the user: this capability alone is switched off (the rest of the package still works)."
            + " Re-enable with set_extension_enabled; needs a restart]", text);
    }

    [Fact]
    public void FailedAndSkippedCapabilitiesSayTheyDoNotExistThisSession()
    {
        var failed = Entry("voice", "v1.voice", "坏引擎", "Failed");
        failed["error"] = "boom";
        var skipped = Entry("effect", "v1.effect", "跳过的", "Skipped");
        skipped["error"] = "sdk-version too new";

        var text = Render(new JsonArray { Package("pkg.a", "示例包", "PartiallyLoaded", new JsonArray { failed, skipped }) });

        Assert.Contains("[FAILED to load: boom — this capability does NOT exist in this session]", text);
        Assert.Contains("[SKIPPED: sdk-version too new — this capability does NOT exist in this session]", text);
    }

    // 一个条目可占多个能力位（format 的后缀别名），要列全，别只报第一个。
    [Fact]
    public void MultiIdentityEntryListsEveryIdentity()
    {
        var entry = Entry("format", "mid", "MIDI");
        entry["identities"] = new JsonArray { "mid", "midi" };
        entry["hasIntroduction"] = true;

        var text = Render(new JsonArray { Package("pkg.a", "示例包", "Loaded", new JsonArray { entry }) });

        Assert.Contains("provides format:mid,midi \"MIDI\"", text);
        // 全文入口按第一个身份给（多身份共用同一份说明）。
        Assert.Contains("[full text: get_extension_introduction(\"format:mid\")]", text);
    }

    // ── 摘要的出处

    [Fact]
    public void VerbatimSummaryIsAttributedToTheAuthorAndKeepsItsLineStructure()
    {
        var entry = Entry("voice", "v1.voice", "示例引擎");
        entry["hasIntroduction"] = true;
        entry["summary"] = new JsonObject { ["text"] = "第一行\n第二行", ["verbatim"] = true };

        var text = Render(new JsonArray { Package("pkg.a", "示例包", "Loaded", new JsonArray { entry }) });

        Assert.Contains("\n        (author's own words)\n          第一行\n          第二行", text);
    }

    // 长文档的摘要是 TuneLab 的转述，不能被当成作者的官方说法转述给用户。
    [Fact]
    public void GeneratedSummaryIsLabelledAsACondensation()
    {
        var entry = Entry("voice", "v1.voice", "示例引擎");
        entry["hasIntroduction"] = true;
        entry["summary"] = new JsonObject { ["text"] = "一句话", ["verbatim"] = false };

        Assert.Contains("\n        (TuneLab's condensation of the author's introduction, not their wording)\n          一句话",
            Render(new JsonArray { Package("pkg.a", "示例包", "Loaded", new JsonArray { entry }) }));
    }

    // 有介绍但还没摘要 ≠ 这插件没东西可说。
    [Fact]
    public void MissingSummarySaysSoInPlace()
    {
        var entry = Entry("voice", "v1.voice", "示例引擎");
        entry["hasIntroduction"] = true;

        Assert.Contains("\n        (not summarized yet — see the note at the end)",
            Render(new JsonArray { Package("pkg.a", "示例包", "Loaded", new JsonArray { entry }) }, 1));
    }

    // 压根没写介绍的条目不该冒出"还没摘要"那句——那会让人以为等一等就有了。
    [Fact]
    public void EntryWithoutAnIntroductionSaysNothingAboutSummaries()
    {
        var text = Render(new JsonArray
        {
            Package("pkg.a", "示例包", "Loaded", new JsonArray { Entry("voice", "v1.voice", "示例引擎") }),
        });

        Assert.DoesNotContain("not summarized yet", text);
        Assert.DoesNotContain("full text:", text);
    }

    // ── §5.3 的降级：末尾那句话取决于这个入口有没有模型

    [Fact]
    public void WithAModelTheTailSaysToAskAgainInAMoment()
    {
        var text = Render(new JsonArray { Package("pkg.a", "示例包") }, 2, summarizerAvailable: true);

        Assert.Contains("\n\nNote: 2 capability(ies) could not be summarized this time (a summarization request failed, or the time budget ran out).", text);
        Assert.Contains("they can ask again in a moment to fill those in; the ones already done are cached and cost nothing.", text);
    }

    // 没有模型的入口（CLI / MCP / headless）：再问一次也不会有，必须给能真正解决问题的下一步。
    [Fact]
    public void WithoutAModelTheTailSaysThisEntryPointCannotFillThemIn()
    {
        var text = Render(new JsonArray { Package("pkg.a", "示例包") }, 2, summarizerAvailable: false);

        Assert.Contains("\n\nNote: 2 capability(ies) have no one-line summary yet, and this entry point has no model to write one", text);
        Assert.Contains("so asking again will not fill them in.", text);
        Assert.Contains("read the author's own text for those with get_extension_introduction.", text);
        Assert.DoesNotContain("ask again in a moment", text);
    }

    [Fact]
    public void NothingMissingMeansNoTailNoteAtAll()
    {
        Assert.DoesNotContain("Note:", Render(new JsonArray { Package("pkg.a", "示例包") }));
    }

    // ── 冲突（排障的关键一句：加载成功与"在用"是两回事）

    [Fact]
    public void ShadowedCapabilitySaysItIsLoadedButNeverUsed()
    {
        var entry = Entry("voice", "v1.voice", "示例引擎");
        entry["routing"] = new JsonArray { Routing("voice", "v1.voice", false, "包甲") };

        var text = Render(new JsonArray { Package("pkg.b", "包乙", "Loaded", new JsonArray { entry }) });

        Assert.Contains("\n        voice: SHADOWED: \"包甲\" provides it instead, so THIS package's implementation is loaded but never used."
            + " See list_extension_routing / set_extension_routing.", text);
    }

    [Fact]
    public void ActiveCapabilityNamesThePackagesItShadows()
    {
        var entry = Entry("voice", "v1.voice", "示例引擎");
        entry["routing"] = new JsonArray { Routing("voice", "v1.voice", true, "包甲", "包乙", "包丙") };

        Assert.Contains("\n        voice: ACTIVE (also provided by \"包乙\", \"包丙\", which is/are shadowed).",
            Render(new JsonArray { Package("pkg.a", "包甲", "Loaded", new JsonArray { entry }) }));
    }

    // Legacy 包没有 manifest 条目，冲突只能挂在包这一层——那一行的形状与条目级不同，别顺手合并。
    [Fact]
    public void LegacyPackageListsItsConflictsAtThePackageLevel()
    {
        var package = Package(null, "老包");
        package["id"] = "legacy.pkg";
        package["routing"] = new JsonArray { Routing("format-import", "mid", false, "包甲") };

        Assert.Contains("\n    provides format-import:mid — SHADOWED: \"包甲\" provides it instead,", Render(new JsonArray { package }));
    }

    // ── 执行路径：测试进程里没装扩展，且没有旁路模型（CLI/headless 的形状）
    [Fact]
    public void RunsWithoutASideModelAndReportsAnEmptyInstallation()
    {
        var result = Command.ExecuteAsync(CommandArgs.Empty, new CommandContext(), CancellationToken.None)
            .GetAwaiter().GetResult();

        Assert.False(result.IsError);
        Assert.False(result.Data!["summarizerAvailable"]!.GetValue<bool>());
        Assert.Empty(result.Data["packages"]!.AsArray());
        Assert.Equal("No extensions are installed. TuneLab is running with only its built-in capabilities.",
            Command.Render(result.Data, CommandArgs.Empty));
    }
}
