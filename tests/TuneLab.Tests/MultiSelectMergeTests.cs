using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using Xunit;

namespace TuneLab.Tests;

// 多选编辑下容器（数组 / 对象）的三态合并：config 层（MultiplePropertyMerge，决定显示哪些行/键）
// 与数据层（MultipleDataPropertyArray / MultipleDataPropertyObject.Array，live 绑定的三态读 + 扇出写）。
// 对齐规则：数组按 index（长度取最长，缺位算差异）、对象按 key 并集（缺键算差异）。只测本需求受影响范围。
public class MultiSelectMergeTests
{
    static PropertyObject Obj(params (string key, PropertyValue value)[] entries)
    {
        var map = new Map<string, PropertyValue>();
        foreach (var (key, value) in entries)
            map.Add(key, value);
        return new PropertyObject(map);
    }

    static PropertyArray Arr(params PropertyValue[] values) => new(values);

    static PropertyValue ValueAt(PropertyObject obj, string key)
        => obj.Map.TryGetValue(key, out var v) ? v : PropertyValue.Null;

    // ===== config 层：PropertyMerge.Merge =====

    [Fact]
    public void Merge_Scalar_SameValueOrMultiple()
    {
        var merged = PropertyMerge.Merge([
            Obj(("a", 1.0), ("b", 2.0)),
            Obj(("a", 1.0), ("b", 3.0)),
        ]);

        Assert.True(ValueAt(merged, "a").ToDouble(out var a) && a == 1.0);   // 同值
        Assert.True(ValueAt(merged, "b").IsMultiple());                      // 不等
    }

    [Fact]
    public void Merge_MissingKey_IsMultiple()
    {
        // 键并集：b 只在其一 → 视作差异。
        var merged = PropertyMerge.Merge([
            Obj(("a", 1.0)),
            Obj(("a", 1.0), ("b", 2.0)),
        ]);

        Assert.True(ValueAt(merged, "a").ToDouble(out var a) && a == 1.0);
        Assert.True(ValueAt(merged, "b").IsMultiple());
    }

    [Fact]
    public void Merge_Array_ByIndex_MaxLength()
    {
        // [1,2] vs [1,9,3] → 长度 3：index0 同值=1、index1 不等=Multiple、index2 缺位=Multiple。
        var merged = PropertyMerge.Merge([
            Obj(("arr", Arr(1.0, 2.0))),
            Obj(("arr", Arr(1.0, 9.0, 3.0))),
        ]);

        Assert.True(ValueAt(merged, "arr").ToArray(out var arr));
        Assert.Equal(3, arr.Count);
        Assert.True(arr[0].ToDouble(out var e0) && e0 == 1.0);
        Assert.True(arr[1].IsMultiple());
        Assert.True(arr[2].IsMultiple());
    }

    [Fact]
    public void Merge_Array_AllEqual_PreservesValues()
    {
        var merged = PropertyMerge.Merge([
            Obj(("arr", Arr(1.0, 2.0))),
            Obj(("arr", Arr(1.0, 2.0))),
        ]);

        Assert.Equal((PropertyValue)Arr(1.0, 2.0), ValueAt(merged, "arr"));
    }

    [Fact]
    public void Merge_Object_KeyUnion_Recurses()
    {
        // {x:1} vs {x:1,y:2} → {x:1, y:Multiple}（y 缺于其一）。
        var merged = PropertyMerge.Merge([
            Obj(("o", PropertyValue.Create(Obj(("x", 1.0))))),
            Obj(("o", PropertyValue.Create(Obj(("x", 1.0), ("y", 2.0))))),
        ]);

        Assert.True(ValueAt(merged, "o").ToObject(out var o));
        Assert.True(ValueAt(o, "x").ToDouble(out var x) && x == 1.0);
        Assert.True(ValueAt(o, "y").IsMultiple());
    }

    [Fact]
    public void Merge_Array_KeyAbsentInSomeMembers_PreservesLength()
    {
        // 容器键仅部分成员有（另一从未设该键）：不塌成 Multiple，按结构合并——长度取最长、缺位逐项 Multiple。
        // （驱动 config 行数：避免「多选不等长/部分缺键 → 0 行」。）
        var merged = PropertyMerge.Merge([
            Obj(("phonemes", Arr(1.0, 2.0))),
            Obj(("other", 9.0)),   // 无 phonemes 键
        ]);

        Assert.True(ValueAt(merged, "phonemes").ToArray(out var arr));
        Assert.Equal(2, arr.Count);
        Assert.True(arr[0].IsMultiple());
        Assert.True(arr[1].IsMultiple());
    }

    [Fact]
    public void Merge_Object_KeyAbsentInSomeMembers_PreservesKeyUnion()
    {
        // 键控对象键仅部分成员有 → 键并集保留（不塌 Multiple）。
        var merged = PropertyMerge.Merge([
            Obj(("tags", PropertyValue.Create(Obj(("red", PropertyValue.Create(PropertyObject.Empty)))))),
            Obj(("other", 9.0)),
        ]);

        Assert.True(ValueAt(merged, "tags").ToObject(out var o));
        Assert.True(o.Map.ContainsKey("red"));
    }

    [Fact]
    public void Merge_SingleOrEmpty_PassesThrough()
    {
        var single = Obj(("a", 1.0));
        Assert.Equal(single, PropertyMerge.Merge([single]));
        Assert.Equal(PropertyObject.Empty, PropertyMerge.Merge([]));
    }

    // ===== 数据层：MultipleDataPropertyArray（经 MultipleDataPropertyObject.Array 取得） =====

    static DataPropertyObject NoteWith(string key, PropertyArray array)
    {
        var note = new DataPropertyObject();
        note.SetInfo(Obj((key, array)));
        return note;
    }

    static IDataPropertyArray MergedArray(string key, params DataPropertyObject[] notes)
        => new MultipleDataPropertyObject(notes).Array(key);

    [Fact]
    public void DataArray_Count_IsMaxMemberLength()
    {
        var merged = MergedArray("ph",
            NoteWith("ph", Arr(1.0, 2.0)),
            NoteWith("ph", Arr(1.0, 9.0, 3.0)));

        Assert.Equal(3, merged.Count);
        Assert.Equal(["0", "1", "2"], merged.Tokens);
    }

    [Fact]
    public void DataArray_GetValue_ThreeState()
    {
        var merged = MergedArray("ph",
            NoteWith("ph", Arr(1.0, 2.0)),
            NoteWith("ph", Arr(1.0, 9.0, 3.0)));

        Assert.True(merged.GetValue("0", -1.0).ToDouble(out var v0) && v0 == 1.0);  // 同值
        Assert.True(merged.GetValue("1", -1.0).IsMultiple());                       // 不等
        Assert.True(merged.GetValue("2", -1.0).IsMultiple());                       // 有成员缺该位
    }

    [Fact]
    public void DataArray_GetValue_MissingMember_UsesElementDefault()
    {
        // pair 场景：a 物化为 [0.5, 0.8]，b 从未设过该键（absent）；元素默认 [0.2, 0.8]。
        var a = NoteWith("pair", Arr(0.5, 0.8));
        var b = new DataPropertyObject();   // 无 pair 键
        var merged = (MultipleDataPropertyArray)MergedArray("pair", a, b);
        merged.SetElementDefaults([0.2, 0.8]);

        Assert.Equal(2, merged.Count);
        Assert.True(merged.GetValue("0", -1.0).IsMultiple());                       // 0.5 vs 默认 0.2 → 不等
        Assert.True(merged.GetValue("1", -1.0).ToDouble(out var v1) && v1 == 0.8);  // 0.8 vs 默认 0.8 → 相等（缺位取默认，不误报 Multiple）
    }

    [Fact]
    public void DataArray_SetValue_PadsShorterMembersOnEdit()
    {
        var a = NoteWith("ph", Arr(1.0, 2.0));
        var b = NoteWith("ph", Arr(1.0, 9.0, 3.0));
        var merged = (MultipleDataPropertyArray)MergedArray("ph", a, b);
        merged.SetElementDefaults([0.0, 0.0, 0.0]);

        merged.SetValue("1", 7.0);   // 两成员都有 index 1 → 都改、不补
        Assert.Equal((PropertyValue)Arr(1.0, 7.0), a.GetInfo().Map["ph"]);
        Assert.Equal((PropertyValue)Arr(1.0, 7.0, 3.0), b.GetInfo().Map["ph"]);

        merged.SetValue("2", 5.0);   // a 缺 index 2 → 编辑即补齐：a 补到长度 3 再写
        Assert.Equal((PropertyValue)Arr(1.0, 7.0, 5.0), a.GetInfo().Map["ph"]);
        Assert.Equal((PropertyValue)Arr(1.0, 7.0, 5.0), b.GetInfo().Map["ph"]);
    }

    [Fact]
    public void DataArray_Add_AppendsToAllMembers()
    {
        var a = NoteWith("ph", Arr(1.0));
        var b = NoteWith("ph", Arr(1.0, 2.0));
        var merged = MergedArray("ph", a, b);

        merged.Add(8.0);
        Assert.Equal((PropertyValue)Arr(1.0, 8.0), a.GetInfo().Map["ph"]);
        Assert.Equal((PropertyValue)Arr(1.0, 2.0, 8.0), b.GetInfo().Map["ph"]);
    }

    [Fact]
    public void DataArray_RemoveAt_RemovesFromMembersHavingIndex()
    {
        var a = NoteWith("ph", Arr(1.0, 2.0));
        var b = NoteWith("ph", Arr(1.0, 9.0, 3.0));
        var merged = MergedArray("ph", a, b);

        merged.RemoveAt(2);   // 仅 b 有 index 2
        Assert.Equal((PropertyValue)Arr(1.0, 2.0), a.GetInfo().Map["ph"]);
        Assert.Equal((PropertyValue)Arr(1.0, 9.0), b.GetInfo().Map["ph"]);
    }

    [Fact]
    public void DataArray_SingleMember_PassesThroughRealArray()
    {
        var a = NoteWith("ph", Arr(1.0, 2.0));
        var merged = MergedArray("ph", a);

        // 单成员直通：真实稳定 token（非 "0"/"1" 合成 index token）。
        Assert.IsNotType<MultipleDataPropertyArray>(merged);
        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void DataArray_NoMembers_IsEmptyAndNoOp()
    {
        var merged = new MultipleDataPropertyObject([]).Array("ph");
        Assert.Equal(0, merged.Count);
        Assert.Empty(merged.Tokens);
        merged.Add(1.0);                 // no-op、不抛
        merged.SetValue("0", 1.0);
        Assert.Equal(0, merged.Count);
    }

    [Fact]
    public void DataArray_ObjectElement_NavigatesAndMergesSubField()
    {
        var a = NoteWith("ph", Arr(Obj(("sym", "i"), ("dur", 100.0))));
        var b = NoteWith("ph", Arr(Obj(("sym", "i"), ("dur", 200.0))));
        var merged = MergedArray("ph", a, b);

        var element = merged.Object("0");
        Assert.True(element.GetValue("sym", "").ToString(out var sym) && sym == "i");  // 同值
        Assert.True(element.GetValue("dur", -1.0).IsMultiple());                       // 不等

        element.SetValue("sym", "a");   // 扇出写子字段
        Assert.True(a.GetInfo().Map["ph"].ToArray(out var aArr) && aArr[0].ToObject(out var aObj)
            && aObj.GetString("sym") == "a");
        Assert.True(b.GetInfo().Map["ph"].ToArray(out var bArr) && bArr[0].ToObject(out var bObj)
            && bObj.GetString("sym") == "a");
    }
}
