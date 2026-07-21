using System.Collections.Generic;
using TuneLab.SDK;

namespace TuneLab.Data;

// SynthesizedSyllable 的扁平投影（宿主侧）：SDK 类型只承载结构化真相（LeadingPhonemes/BodyPhonemes 双列表），
// 扁平合并序仅宿主的扁平索引寻址模型需要（音素面板行遍历 / 显示遍历），故不入 SDK 面、落在此。
//
// 值与 nullable 各一重载（扩展 receiver 不做 T→T? 隐式转换，故不能只留 nullable 版）：nullable 版吸掉
// null 调用点、值版顺带 null-safe 处理 default(struct) 的空双列表，皆返回空 / 0。
internal static class SynthesizedSyllableExtensions
{
    // 全序列音素数（引导 + 主体），零分配。"有没有 / 几个音素"用它，别 AllPhonemes().Count（那会物化整张 list）。
    public static int PhonemeCount(this SynthesizedSyllable syllable)
        => (syllable.LeadingPhonemes?.Count ?? 0) + (syllable.BodyPhonemes?.Count ?? 0);
    public static int PhonemeCount(this SynthesizedSyllable? syllable)
        => syllable is { } s ? s.PhonemeCount() : 0;

    // 全序列只读视图 = LeadingPhonemes ++ BodyPhonemes（时间序），每次物化。仅真需整张列表 / 按扁平位索引时用。
    public static IReadOnlyList<SynthesizedPhoneme> AllPhonemes(this SynthesizedSyllable syllable)
        => [.. syllable.LeadingPhonemes ?? [], .. syllable.BodyPhonemes ?? []];
    public static IReadOnlyList<SynthesizedPhoneme> AllPhonemes(this SynthesizedSyllable? syllable)
        => syllable is { } s ? s.AllPhonemes() : [];
}
