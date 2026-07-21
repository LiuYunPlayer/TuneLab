using System.Collections.Generic;

namespace TuneLab.SDK;

// 音素的「核相对 slot」坐标口径（声明面共享纯函数，PhonemeLayout 同款先例：宿主对齐 / 面板合并 / 引擎声明
// 共用这一份，永不漂移）。slot = 音素下标 − LeadingPhonemes.Count：0 = 核（主体首音素）、<0 = 引导辅音
//（离核越近越接近 −1）、>0 = 核后。它是 GetPhonemePropertyConfigs 返回 map 的键——schema 按角色（slot）授予，
// 多选 note 时宿主把各 note 同 slot 的音素并到一行、值三态合并；引擎按 slot 授 schema 即天然与之对齐。
//
// 选区 slot 全集连续性：每个非空 note 的 slot 域是连续区间 [−leadCount, count−1−leadCount] 且必含 −1 或 0
//（贴核），故并集恒为连续区间 [min, max]——UnionSlots 按此枚举，与宿主音素面板的对齐循环同构。
public static class PhonemeSlots
{
    // 该 note 在 slot 处的音素；该位无音素（slot 越界）→ null。多选合并的成员选取即此。
    // 全序列 = 引导 ++ 主体：核（slot 0）= 主体首音素，全序列下标 = LeadingPhonemes.Count。就地跨两列表索引，不物化合并串。
    public static IVoiceSynthesisPhonemeView? PhonemeAt(this IVoiceSynthesisNoteView note, int slot)
    {
        var leading = note.LeadingPhonemes;
        int index = slot + leading.Count;
        if (index < 0)
            return null;
        if (index < leading.Count)
            return leading[index];
        index -= leading.Count;
        var body = note.BodyPhonemes;
        return index < body.Count ? body[index] : null;
    }

    // 选区 slot 全集（升序连续区间；全部 note 无音素 → 空）。引擎逐 slot 授 schema 的标准遍历。
    public static IEnumerable<int> UnionSlots(this IReadOnlyList<IVoiceSynthesisNoteView> notes)
    {
        int min = int.MaxValue, max = int.MinValue;
        foreach (var note in notes)
        {
            int lead = note.LeadingPhonemes.Count;
            int count = lead + note.BodyPhonemes.Count;
            if (count == 0)
                continue;
            if (-lead < min) min = -lead;
            if (count - 1 - lead > max) max = count - 1 - lead;
        }
        for (int slot = min; slot <= max; slot++)
            yield return slot;
    }
}
