using System.Text;
using System.Text.Json.Nodes;
using TuneLab.Commands;
using Xunit;

namespace TuneLab.Tests;

// 引擎清单的渲染封条。这段输出被 `source list` 与 `effect list` 共用，其中最要紧的是**路由冲突那一句**：
// 多个包提供同一身份时必须点明谁在生效、谁被顶替——否则调用方只看到"装上了"，会给用户"应该能用"的
// 误导结论，而真相是那个包被顶替了。
//
// 只测渲染（从合成的结构化清单拼文本）：Collect 那一半要真实的扩展注册表与路由表，属集成范围。
public class EngineCatalogRenderTests
{
    static JsonObject Engine(string type, string displayName, string activePackage, string[]? shadowed = null, string? packageDescription = null)
    {
        var shadowedArray = new JsonArray();
        foreach (var name in shadowed ?? [])
            shadowedArray.Add(name);
        return new JsonObject
        {
            ["type"] = type,
            ["displayName"] = displayName,
            ["activePackage"] = activePackage,
            ["activePackageId"] = activePackage,
            ["shadowed"] = shadowedArray,
            ["packageDescription"] = packageDescription,
        };
    }

    static string Render(string kindLabel, params JsonObject[] engines)
    {
        var array = new JsonArray();
        foreach (var e in engines)
            array.Add(e);
        var sb = new StringBuilder();
        EngineCatalog.AppendEngineList(sb, kindLabel, array);
        return sb.ToString();
    }

    [Fact]
    public void ListsEachEngineWithItsTypeAndPackage()
    {
        Assert.Equal(string.Join("\n", [
            "Voice engines (1):",
            "- \"示例引擎\" [type=v1.voice, package=示例包]",
        ]), Render("Voice", Engine("v1.voice", "示例引擎", "示例包")));
    }

    // 路由冲突：生效的那个标 ACTIVE，被顶替的点名，并把排查引向路由清单。
    [Fact]
    public void NamesTheActivePackageAndTheShadowedOnesOnAConflict()
    {
        var text = Render("Voice", Engine("v1.voice", "示例引擎", "包甲", ["包乙", "包丙"]));

        Assert.Equal(string.Join("\n", [
            "Voice engines (1):",
            "- \"示例引擎\" [type=v1.voice, package=包甲 (ACTIVE) — shadowed: 包乙, 包丙;"
                + " routing conflict, see list_extension_routing]",
        ]), text);
    }

    // 引擎自己没有摘要时退一步给所属包的自述，并【明确标注这是降级参考】——那句话讲的是整个包，
    // 不等于这个引擎的描述，不该被当成它的能力转述给用户。
    [Fact]
    public void MarksThePackageBlurbAsASecondHandFallback()
    {
        var text = Render("Effect", Engine("v1.effect", "示例效果器", "示例包", packageDescription: "一套示例插件"));

        Assert.Contains("(no summary of its own; its package describes itself as \"一套示例插件\""
            + " — package-level, may cover other capabilities too."
            + " For this engine specifically, call get_extension_introduction.)", text);
    }

    [Fact]
    public void SaysNoneWhenNothingIsInstalled()
    {
        Assert.Equal("Instrument engines (0):\n  (none)", Render("Instrument"));
    }

    // 两组连排（voice 后接 instrument）时，第二组前补一个换行——不能贴在上一组最后一行后面。
    [Fact]
    public void SeparatesConsecutiveGroupsWithABlankBoundary()
    {
        var sb = new StringBuilder();
        var voice = new JsonArray { Engine("v1.voice", "声库引擎", "包甲") };
        var instrument = new JsonArray { Engine("v1.instrument", "乐器引擎", "包乙") };
        EngineCatalog.AppendEngineList(sb, "Voice", voice);
        EngineCatalog.AppendEngineList(sb, "Instrument", instrument);

        Assert.Equal(string.Join("\n", [
            "Voice engines (1):",
            "- \"声库引擎\" [type=v1.voice, package=包甲]",
            "Instrument engines (1):",
            "- \"乐器引擎\" [type=v1.instrument, package=包乙]",
        ]), sb.ToString());
    }
}
