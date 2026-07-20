using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using Xunit;

namespace TuneLab.Tests;

// 键值集合的 C# 集合表达式 [...] 支持（[CollectionBuilder]）。
// 覆盖：空字面量 []（含只读接口目标，new() 无法表达的场景）、非空字面量、OrderedMap 保序、
// 只读接口空字面量复用 Empty 单例（零分配）、重复 key 语义（Map 抛错 / OrderedMap 覆盖，与各自 .Add 一致）。
// 元素类型为接口 IReadOnlyKeyValuePair，故字面量元素须显式写 new ReadOnlyKeyValuePair<,>(...)：
// new(...) 不能 target 接口，BCL KeyValuePair 也不会隐式汇入接口（CS0029），所以非空 KV 字面量收益有限，主要价值在空 []。
public class MapCollectionExpressionTests
{
    [Fact]
    public void EmptyLiteral_ConcreteTypes()
    {
        Map<string, int> map = [];
        OrderedMap<string, int> orderedMap = [];

        Assert.Empty(map);
        Assert.Empty(orderedMap);
    }

    [Fact]
    public void EmptyLiteral_InterfaceTargets()
    {
        // new() 无法构造接口；集合表达式可以——这是本特性相对 new(){…} 的真实增量。
        IMap<string, int> map = [];
        IReadOnlyMap<string, int> readOnlyMap = [];
        IOrderedMap<string, int> orderedMap = [];
        IReadOnlyOrderedMap<string, int> readOnlyOrderedMap = [];

        Assert.Empty(map);
        Assert.Empty(readOnlyMap);
        Assert.Empty(orderedMap);
        Assert.Empty(readOnlyOrderedMap);
    }

    [Fact]
    public void NonEmptyLiteral_Map()
    {
        Map<string, int> map = [new ReadOnlyKeyValuePair<string, int>("a", 1), new ReadOnlyKeyValuePair<string, int>("b", 2)];

        Assert.Equal(2, map.Count);
        Assert.Equal(1, map["a"]);
        Assert.Equal(2, map["b"]);
    }

    [Fact]
    public void NonEmptyLiteral_OrderedMap_PreservesOrder()
    {
        OrderedMap<string, int> map = [new ReadOnlyKeyValuePair<string, int>("z", 26), new ReadOnlyKeyValuePair<string, int>("a", 1), new ReadOnlyKeyValuePair<string, int>("m", 13)];

        Assert.Equal(3, map.Count);
        Assert.Equal(new[] { "z", "a", "m" }, map.Keys.ToArray());
        Assert.Equal(26, map.ValueAt(0));
        Assert.Equal(1, map.ValueAt(1));
        Assert.Equal(13, map.ValueAt(2));
    }

    [Fact]
    public void EmptyReadOnlyLiteral_ReusesSingleton()
    {
        IReadOnlyMap<string, int> a = [];
        IReadOnlyMap<string, int> b = [];
        IReadOnlyOrderedMap<string, int> c = [];
        IReadOnlyOrderedMap<string, int> d = [];

        // 空只读字面量复用缓存单例，不重复分配。
        Assert.Same(a, b);
        Assert.Same(c, d);
    }

    [Fact]
    public void DuplicateKey_Map_Throws()
    {
        // Map 的 builder 走 .Add（= Dictionary.Add），重复 key 抛错。
        Assert.Throws<ArgumentException>(() =>
        {
            Map<string, int> _ = [new ReadOnlyKeyValuePair<string, int>("a", 1), new ReadOnlyKeyValuePair<string, int>("a", 2)];
        });
    }

    [Fact]
    public void DuplicateKey_OrderedMap_Throws()
    {
        // OrderedMap.Add 与 Map.Add 对齐（Dictionary 语义）：重复 key 抛错，不再静默覆盖 / 挪位。
        Assert.Throws<ArgumentException>(() =>
        {
            OrderedMap<string, int> _ = [new ReadOnlyKeyValuePair<string, int>("a", 1), new ReadOnlyKeyValuePair<string, int>("b", 2), new ReadOnlyKeyValuePair<string, int>("a", 3)];
        });
    }

    [Fact]
    public void OrderedMap_Indexer_ReplacesInPlace()
    {
        // 替换已有键的唯一入口 = 索引器：原位替换值、次序位置不变（区别于 Add 追加末尾）。
        OrderedMap<string, int> map = [new ReadOnlyKeyValuePair<string, int>("a", 1), new ReadOnlyKeyValuePair<string, int>("b", 2)];
        map["a"] = 3;

        Assert.Equal(2, map.Count);
        Assert.Equal(3, map["a"]);
        Assert.Equal(new[] { "a", "b" }, map.Keys.ToArray());
    }
}
