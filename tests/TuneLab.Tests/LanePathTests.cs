using System.Collections.Generic;
using System.Linq;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;
using Part = TuneLab.Data.LanePath.LabelPart;

namespace TuneLab.Tests;

// 参数面板 lane 的身份 = 字段在声明树里的路径：编码、遍历、解析、沿路径读写（缺的数组元素按默认值补齐）、最短不重名的标签。
public class LanePathTests
{
    sealed class NullControl : ICanvasControl { }

    static TimelineCanvasConfig Canvas => TimelineCanvasConfig.Create(PropertyObject.Empty, _ => new NullControl());

    // 音素混合式的形状：mixes = [{ curve: 时间轴画布, gain: 滑条 }, …]（定长 2）+ 顶层一个滑条。
    static ObjectConfig Declaration => ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>
    {
        { ("level", "Level"), SliderConfig.Linear(0, 0, 1) },
        { ("mixes", "Mix"), ArrayConfig.Create(
            [
                ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig> { { ("curve", "Curve"), Canvas }, { ("gain", "Gain"), SliderConfig.Linear(0.5, 0, 1) } }),
                ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig> { { ("curve", "Curve"), Canvas }, { ("gain", "Gain"), SliderConfig.Linear(0.5, 0, 1) } }),
            ]) },
    });

    [Fact]
    public void EncodeEscapesSlashAndTilde_TopLevelIsTheIdItself()
    {
        Assert.Equal("level", LanePath.Encode(["level"]));
        var path = LanePath.Encode(["a/b", "~x", "0"]);
        Assert.Equal("a~1b/~0x/0", path);
        Assert.Equal(new[] { "a/b", "~x", "0" }, LanePath.Decode(path));
    }

    [Fact]
    public void LeavesWalkIntoObjectsAndArrays()
    {
        var leaves = LanePath.Leaves(Declaration).ToList();
        Assert.Equal(["level", "mixes/0/curve", "mixes/0/gain", "mixes/1/curve", "mixes/1/gain"], leaves.Select(l => l.Path));
        Assert.IsType<TimelineCanvasConfig>(leaves[3].Config);
        Assert.Equal("Mix 2 · Curve", LanePath.FullLabel(leaves[3].Label));   // 序号跟在它所属的那一段后面
    }

    [Fact]
    public void ResolveFindsTheLeafOrNothing()
    {
        Assert.IsType<TimelineCanvasConfig>(LanePath.Resolve(Declaration, "mixes/1/curve")!.Leaf);
        Assert.Null(LanePath.Resolve(Declaration, "mixes/2/curve"));   // 越界
        Assert.Null(LanePath.Resolve(Declaration, "mixes/1"));         // 不是叶子
        Assert.Null(LanePath.Resolve(Declaration, "nope"));
    }

    // 写第 2 个元素：数组先按元素默认值补齐到 2 个（首次写入整段落地），再写到那个叶子上；读回来就是它，第 1 个是默认值。
    [Fact]
    public void WriteSeedsMissingElementsAndReadFollowsThePath()
    {
        var root = new DataPropertyObject();
        var gain = LanePath.Resolve(Declaration, "mixes/1/gain")!;
        Assert.Equal(0.5, LanePath.Read(root, gain.Steps, PropertyValue.Create(0.5)).ToDouble(out var before) ? before : -1);

        LanePath.Write(root, gain.Steps, PropertyValue.Create(0.9));
        Assert.Equal(2, root.Array("mixes").Count);
        LanePath.Read(root, gain.Steps, PropertyValue.Create(0.5)).ToDouble(out var after);
        Assert.Equal(0.9, after);
        var first = LanePath.Resolve(Declaration, "mixes/0/gain")!;
        LanePath.Read(root, first.Steps, PropertyValue.Create(0.5)).ToDouble(out var seeded);
        Assert.Equal(0.5, seeded);

        LanePath.Remove(root, gain.Steps);
        LanePath.Read(root, gain.Steps, PropertyValue.Create(0.5)).ToDouble(out var cleared);
        Assert.Equal(0.5, cleared);
    }

    // 默认只显示末段 + 全部序号；重名才往前补祖先名，补出来的段带回自己的序号；顶层就是字段名。
    [Fact]
    public void LabelsAreTheShortestThatStayDistinct()
    {
        IReadOnlyList<Part> Label(params (string Name, int? Number)[] parts) => parts.Select(p => new Part(p.Name, p.Number)).ToList();
        var labels = LanePath.ShortestLabels(
        [
            Label(("Level", null)),
            Label(("Mix", null), ("", 2)),                                   // 元素本身是画布
            Label(("Mix", null), ("", 2), ("Curve", null)),                  // 元素对象里的成员
            Label(("Face", null), ("", 2), ("Curve", null)),                 // 另一个数组里同名的成员
            Label(("Layers", null), ("", 2), ("", 3), ("Shape", null)),      // 两层数组
        ]);
        Assert.Equal(["Level", "Mix 2", "Mix 2 · Curve", "Face 2 · Curve", "Shape 2-3"], labels);
    }

    // 只补出一部分祖先时，没补出来的那几段的序号并到第一段显示出来的段上。
    [Fact]
    public void NumbersOfHiddenAncestorsGoOnTheFirstShownSegment()
    {
        IReadOnlyList<Part> Label(params (string Name, int? Number)[] parts) => parts.Select(p => new Part(p.Name, p.Number)).ToList();
        var labels = LanePath.ShortestLabels(
        [
            Label(("A", null), ("", 1), ("B", null), ("", 2), ("C", null)),
            Label(("X", null), ("", 1), ("B", null), ("", 2), ("C", null)),
            Label(("Y", null), ("", 1), ("D", null), ("", 2), ("C", null)),
        ]);
        Assert.Equal(["A 1 · B 2 · C", "X 1 · B 2 · C", "D 1-2 · C"], labels);
    }
}
