using System;
using TuneLab.Foundation;
using Xunit;

namespace TuneLab.Tests;

// 整块槽（画布的值）：复合值原样存成一块不可变的值，不拆节点树——提交存引用、撤销换引用、读直接给出；工程文件写出的内容与节点树相同。
public class WholeValueSlotTests
{
    static PropertyValue Curve(params double[] ys)
    {
        var points = new PropertyValue[ys.Length];
        for (int i = 0; i < ys.Length; i++)
            points[i] = new PropertyArray([PropertyValue.Create((double)i), PropertyValue.Create(ys[i])]);
        return new PropertyObject(new Map<string, PropertyValue> { { "points", new PropertyArray(points) } });
    }

    [Fact]
    public void ReadsBackTheSameValue_AndSerializesLikeANodeTree()
    {
        var whole = new DataPropertyObject();
        var tree = new DataPropertyObject();
        whole.SetWholeValue("canvas", Curve(0.1, 0.5));
        tree.SetValue("canvas", Curve(0.1, 0.5));

        Assert.Equal(Curve(0.1, 0.5), whole.GetRawValue("canvas"));
        Assert.Equal(tree.GetInfo(), whole.GetInfo());   // 写进工程的内容不变
    }

    [Fact]
    public void UndoAndRedoSwapTheWholeValue()
    {
        var doc = new DataDocument();
        var properties = new DataPropertyObject(doc);
        properties.SetWholeValue("canvas", Curve(0.1));
        doc.Commit();
        properties.SetWholeValue("canvas", Curve(0.1, 0.9));
        doc.Commit();

        doc.Undo();
        Assert.Equal(Curve(0.1), properties.GetRawValue("canvas"));
        doc.Redo();
        Assert.Equal(Curve(0.1, 0.9), properties.GetRawValue("canvas"));
    }

    // 老工程读进来是节点树，第一次整块写后换成整块；整块写到数组元素上同样成立。
    [Fact]
    public void ReplacesANodeTree_AndWorksOnArrayElements()
    {
        var properties = new DataPropertyObject();
        properties.SetValue("canvas", Curve(0.2));
        properties.SetWholeValue("canvas", Curve(0.3));
        Assert.Equal(Curve(0.3), properties.GetRawValue("canvas"));

        var takes = new DataPropertyArray();
        takes.Add(PropertyValue.Null);
        takes.SetWholeValue(takes.Tokens[0], Curve(0.4));
        Assert.Equal(Curve(0.4), takes.GetRawValue(takes.Tokens[0]));
    }

    [Fact]
    public void RejectsMultipleAnywhereInside()
    {
        var properties = new DataPropertyObject();
        var bad = new PropertyObject(new Map<string, PropertyValue> { { "points", PropertyValue.Multiple } });
        Assert.Throws<ArgumentException>(() => properties.SetWholeValue("canvas", bad));
        Assert.True(properties.GetRawValue("canvas").IsNull());   // 被拒的写入不留空键
    }
}
