using TuneLab.Foundation;
using TuneLab.GUI.Controllers;
using Xunit;

namespace TuneLab.Tests;

// 定长数组 / 列表里没存过值的复合元素（对象、数组）：面板渲染虚拟行，往下导航与读都不物化（浏览不弄脏文档），
// 首次写入才物化整段 seed、再落到真实元素的同一路径。属性是稀疏存储，没编辑过的数组根本不在数据里，这是常态。
public class SeedCompositeViewTests
{
    static PropertyObject Obj(string key, PropertyValue value)
    {
        var map = new Map<string, PropertyValue>();
        map.Add(key, value);
        return new PropertyObject(map);
    }

    static (DataDocument Doc, DataPropertyArray Array) Empty()
    {
        var doc = new DataDocument();
        var array = new DataPropertyArray(doc);
        doc.Commit();
        return (doc, array);
    }

    [Fact]
    public void NavigatingAndReadingAnUnmaterializedObjectElementWritesNothing()
    {
        var (_, array) = Empty();
        var seed = new SeedPositionView(array, 1, [Obj("mix", PropertyValue.Create(1.0)), Obj("mix", PropertyValue.Create(2.0))]);

        var element = seed.Object("@seed:1");
        Assert.Equal(PropertyValue.Create(9.0), element.GetValue("mix", PropertyValue.Create(9.0)));   // 没存值：读方回落默认
        Assert.True(element.GetRawValue("mix").IsNull());
        Assert.True(element.Object("inner").GetRawValue("x").IsNull());
        Assert.Equal(0, array.Count);
    }

    [Fact]
    public void FirstWriteMaterializesTheWholeSeedAndLandsOnTheRightElement()
    {
        var (_, array) = Empty();
        var seed = new SeedPositionView(array, 1, [Obj("mix", PropertyValue.Create(1.0)), Obj("mix", PropertyValue.Create(2.0))]);

        seed.Object("@seed:1").SetValue("mix", PropertyValue.Create(5.0));

        Assert.Equal(2, array.Count);
        var info = array.GetInfo();
        Assert.Equal(Obj("mix", PropertyValue.Create(1.0)), info[0]);   // 其余位取各自的默认值
        Assert.Equal(Obj("mix", PropertyValue.Create(5.0)), info[1]);
    }

    [Fact]
    public void NestedArrayElementIsEmptyUntilWrittenThenMaterializes()
    {
        var (_, array) = Empty();
        var seed = new SeedPositionView(array, 0, [new PropertyArray([])]);

        var nested = seed.Array("@seed:0");
        Assert.Equal(0, nested.Count);
        Assert.Equal(0, array.Count);

        nested.Add(PropertyValue.Create("a"));
        Assert.Equal(1, array.Count);
        Assert.True(array.GetInfo()[0].ToArray(out var inner) && inner.Count == 1);
    }
}
