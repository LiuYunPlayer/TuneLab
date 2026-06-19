using System.Collections.Generic;
using System.Formats.Cbor;
using TuneLab.Extensions.Formats.TLP;
using TuneLab.Foundation;
using Xunit;

namespace TuneLab.Tests;

// PropertyArray（有序可重复列表）数据核心三平面，对应 effect-migration.md §三.29 的 ①②③。
// 只测受影响范围：① 地板值容器 + PropertyValue Array 臂；② live-doc DataPropertyArray 往返/规范化/undo 粒度；
// ③ TLP/CBOR 递归读写（空数组 present-[]、null 元素占位、嵌套）。④ config/控件未做，不在此测。
public class PropertyArrayTests
{
    static PropertyObject Obj(params (string key, PropertyValue value)[] entries)
    {
        var map = new Map<string, PropertyValue>();
        foreach (var (key, value) in entries)
            map.Add(key, value);
        return new PropertyObject(map);
    }

    // ===== ① PropertyArray 值语义 =====

    [Fact]
    public void Array_Construction_CopiesFirstLevel()
    {
        var source = new List<PropertyValue> { 1.0, 2.0 };
        var array = new PropertyArray(source);
        source.Add(3.0);   // 构造后改源序列不应影响已建数组

        Assert.Equal(2, array.Count);
        Assert.True(array[0].ToDouble(out var v0) && v0 == 1.0);
        Assert.True(array[1].ToDouble(out var v1) && v1 == 2.0);
    }

    [Fact]
    public void Array_Empty_IsZeroLength()
    {
        Assert.Empty(PropertyArray.Empty);
        Assert.Equal(PropertyArray.Empty, new PropertyArray(new List<PropertyValue>()));
    }

    [Fact]
    public void Array_DeepEquals_OrderSensitive()
    {
        var a = new PropertyArray(new PropertyValue[] { 1.0, 2.0 });
        var b = new PropertyArray(new PropertyValue[] { 1.0, 2.0 });
        var reversed = new PropertyArray(new PropertyValue[] { 2.0, 1.0 });
        var shorter = new PropertyArray(new PropertyValue[] { 1.0 });

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, reversed);   // 顺序敏感（与 PropertyObject 键集无序不同）
        Assert.NotEqual(a, shorter);
    }

    [Fact]
    public void Array_Nesting_DeepEquals()
    {
        var a = new PropertyArray(new PropertyValue[]
        {
            Obj(("name", "a"), ("ratio", 0.5)),
            new PropertyArray(new PropertyValue[] { true, "x" }),
        });
        var b = new PropertyArray(new PropertyValue[]
        {
            Obj(("name", "a"), ("ratio", 0.5)),
            new PropertyArray(new PropertyValue[] { true, "x" }),
        });

        Assert.Equal(a, b);
    }

    // ===== PropertyValue 的 Array 臂 =====

    [Fact]
    public void PropertyValue_ArrayArm_Roundtrips()
    {
        var array = new PropertyArray(new PropertyValue[] { 1.0, "x" });
        PropertyValue value = array;   // 隐式转换

        Assert.Equal(PropertyType.Array, value.Type);
        Assert.True(value.IsArray());
        Assert.True(value.ToArray(out var got));
        Assert.Equal(array, got);
        Assert.True(value.To<PropertyArray>(out var generic) && generic.Equals(array));
        Assert.True(value.TypeIs<PropertyArray>());
    }

    [Fact]
    public void PropertyValue_ArrayArm_DistinctFromOtherTypes()
    {
        PropertyValue array = new PropertyArray(new PropertyValue[] { 1.0 });
        PropertyValue scalar = 1.0;

        Assert.False(scalar.IsArray());
        Assert.False(scalar.ToArray(out _));
        Assert.NotEqual(array, scalar);
        Assert.NotEqual(array, PropertyValue.Create(Obj(("a", 1.0))));   // array != object
    }

    // ===== ② live-doc DataPropertyArray =====

    [Fact]
    public void DataArray_SetInfoGetInfo_Roundtrips()
    {
        var info = new PropertyArray(new PropertyValue[]
        {
            1.0,
            Obj(("symbol", "ph"), ("dur", 120.0)),
            new PropertyArray(new PropertyValue[] { true }),
            PropertyArray.Empty,   // 空数组元素
        });

        var live = new DataPropertyArray();
        live.SetInfo(info);

        Assert.Equal(4, live.Count);
        Assert.Equal(info, live.GetInfo());   // 含嵌套对象/数组/空数组递归触底全等
    }

    [Fact]
    public void DataArray_InsertRemoveSet_ReflectInGetInfo()
    {
        var live = new DataPropertyArray();
        live.Add(1.0);
        live.Add(2.0);
        live.Insert(1, 9.0);          // [1, 9, 2]
        live.SetValue(0, 5.0);        // [5, 9, 2]
        live.RemoveAt(2);             // [5, 9]

        Assert.Equal(new PropertyArray(new PropertyValue[] { 5.0, 9.0 }), live.GetInfo());
    }

    [Fact]
    public void DataArray_Insert_UndoRedo_IsElementGranular()
    {
        var doc = new DataDocument();
        var live = new DataPropertyArray(doc);
        live.SetInfo(new PropertyArray(new PropertyValue[] { 1.0, 2.0 }));
        doc.Commit();

        live.Insert(1, 9.0);          // [1, 9, 2]
        doc.Commit();
        Assert.Equal(new PropertyArray(new PropertyValue[] { 1.0, 9.0, 2.0 }), live.GetInfo());

        doc.Undo();                   // 撤销只回退这一次中插
        Assert.Equal(new PropertyArray(new PropertyValue[] { 1.0, 2.0 }), live.GetInfo());

        doc.Redo();
        Assert.Equal(new PropertyArray(new PropertyValue[] { 1.0, 9.0, 2.0 }), live.GetInfo());
    }

    // ===== ③ TLP/CBOR 递归读写 =====

    static PropertyObject CborRoundTrip(PropertyObject info)
    {
        var writer = new CborWriter();
        TuneLabProjectCbor.WritePropertyObject(writer, info);
        var reader = new CborReader(writer.Encode());
        return TuneLabProjectCbor.ReadPropertyObject(reader);
    }

    [Fact]
    public void Cbor_RoundTrips_ArraysAndNesting()
    {
        var info = Obj(
            ("scalar", 3.0),
            ("nestedObject", PropertyValue.Create(Obj(("flag", true)))),
            ("scalars", new PropertyArray(new PropertyValue[] { 1.0, "x", false })),
            ("objects", new PropertyArray(new PropertyValue[] { Obj(("k", 1.0)), Obj(("k", 2.0)) })),
            ("arrays", new PropertyArray(new PropertyValue[] { new PropertyArray(new PropertyValue[] { 1.0 }) })));

        Assert.Equal(info, CborRoundTrip(info));
    }

    [Fact]
    public void Cbor_PreservesEmptyArray_AsPresentValue()
    {
        // present-[] 是真实值（用户显式清空），不可因空被跳过——往返后仍是长度 0 的数组、非缺席。
        var info = Obj(("list", PropertyArray.Empty));
        var result = CborRoundTrip(info);

        Assert.True(result.Map.ContainsKey("list"));
        Assert.True(result.Map.TryGetValue("list", out var value));
        Assert.True(value.ToArray(out var array));
        Assert.Empty(array);
    }

    [Fact]
    public void Cbor_PreservesNullElement_PositionInArray()
    {
        // 数组元素须按位写齐：null 元素写成 CBOR null 占位、读回为 PropertyValue.Null，不塌缩位置。
        var info = Obj(("list", new PropertyArray(new PropertyValue[] { 1.0, PropertyValue.Null, 3.0 })));
        var result = CborRoundTrip(info);

        Assert.True(result.Map.TryGetValue("list", out var value));
        Assert.True(value.ToArray(out var array));
        Assert.Equal(3, array.Count);
        Assert.True(array[1].IsNull());
        Assert.True(array[2].ToDouble(out var third) && third == 3.0);
    }
}
