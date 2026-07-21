using System;

namespace TuneLab.SDK;

// ObjectConfig 字段的键：Id 是数据寻址用的稳定标识（= 落进 PropertyObject 的 key 字符串），
// DisplayText 是它的翻译（缺省回退到 Id）。显示标签是「插槽」属性、不属 config 本身——
// 同一 config 放不同槽含义不同（对象字段=key 翻译、数组元素=行/类型名），故标签随键走。
// 相等性/哈希只认 Id：DisplayText 是注解、不入键身份——这让 keyed-diff 在「仅 DisplayText 变（如语言切换）」
// 时判同键、只重贴标签不重建控件。隐式转换让无标签字段写裸 string、带标签写 (id, 译文) 元组。
public readonly struct PropertyKey(string id, string? displayText = null) : IEquatable<PropertyKey>
{
    public string Id { get; } = id;
    public string? DisplayText { get; } = displayText;

    public bool Equals(PropertyKey other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is PropertyKey other && Equals(other);
    // default(PropertyKey) 的 Id 为 null：哈希取 0、ToString 退空串——作字典键 / 打印时稳健，不 NRE、不返回 null。
    // Equals 已 null-safe（string == 处理 null），default 相等自反、哈希一致。
    public override int GetHashCode() => Id?.GetHashCode() ?? 0;
    public override string ToString() => DisplayText ?? Id ?? string.Empty;

    public static implicit operator PropertyKey(string id) => new(id);
    public static implicit operator PropertyKey((string id, string? displayText) entry) => new(entry.id, entry.displayText);
}
