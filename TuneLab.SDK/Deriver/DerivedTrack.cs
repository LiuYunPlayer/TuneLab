namespace TuneLab.SDK;

// 派生轨：一组同轨内的 part（midi / audio）。只承载派生得出的东西（轨名 + part 列表），
// 不带创作字段（gain/pan/mute/solo/color/export/…）——那些是用户意图，宿主并入工程时以默认填。
public sealed class DerivedTrack
{
    // 轨名，空 = 不产（宿主用默认命名）。
    public string Name { get; init; } = string.Empty;
    // 该轨的 part 列表（midi / audio），时间序、可多个不重叠 part。
    public IReadOnlyList<DerivedPart> Parts { get; init; } = [];
}
