using System.Collections.Generic;
using TuneLab.Data;
using TuneLab.Extensions.Formats.TLP;

namespace TuneLab.UI;

// 参数面板里"哪一条参数"在运行时与工程文件之间的换算（纯函数，不碰控件）。
// 运行时的路由键 AutomationKey 带的是 effect 的链内下标——交互用、不能直接存：布局是整个工程共用的、跨 part 的。
// 故存的时候给 effect 参数补上实例 id 与类型，打开时按下面的顺序认回：
//   1. 当前 part 的链里有这个实例 id → 跟着它（链重排过也准）；
//   2. 否则（换了 part，各 part 的实例 id 不同）→ 该位置恰是同类型的 effect，就用它的同名参数；
//   3. 都对不上 → 认不回（激活参数随之回落 Volume，可见集里就少这一条）。
internal static class ParameterPanelLayoutStore
{
    // 一个 effect 槽位的身份：实例 id 与类型。按链序排列，下标即 AutomationKey.EffectIndex。
    public readonly record struct EffectSlot(string Id, string Type);

    public static IReadOnlyList<EffectSlot> EffectSlotsOf(IMidiPart? part)
    {
        var slots = new List<EffectSlot>();
        if (part == null)
            return slots;

        foreach (var effect in part.Effects)
            slots.Add(new(effect.Id, effect.Type));
        return slots;
    }

    // effect 下标越界（没有 part、链已变短）的认不出身份，存不下 → null。
    public static ParameterRefInfo? ToRef(AutomationKey key, IReadOnlyList<EffectSlot> effects)
    {
        switch (key.Source)
        {
            case AutomationSource.Voice:
                return new() { Source = VoiceSource, Id = key.Id };
            case AutomationSource.NoteLane:
                return new() { Source = NoteLaneSource, Id = key.Id };
            case AutomationSource.PhonemeLane:
                return new() { Source = PhonemeLaneSource, Id = key.Id };
            case AutomationSource.Effect:
                if (key.EffectIndex < 0 || key.EffectIndex >= effects.Count)
                    return null;
                var slot = effects[key.EffectIndex];
                return new() { Source = EffectSource, Id = key.Id, EffectId = slot.Id, EffectIndex = key.EffectIndex, EffectType = slot.Type };
            default:
                return null;
        }
    }

    public static AutomationKey? Resolve(ParameterRefInfo info, IReadOnlyList<EffectSlot> effects)
    {
        if (string.IsNullOrEmpty(info.Id))
            return null;

        switch (info.Source)
        {
            case VoiceSource:
                return AutomationKey.Voice(info.Id);
            case NoteLaneSource:
                return AutomationKey.NoteLane(info.Id);
            case PhonemeLaneSource:
                return AutomationKey.PhonemeLane(info.Id);
            case EffectSource:
                if (!string.IsNullOrEmpty(info.EffectId))
                {
                    for (int i = 0; i < effects.Count; i++)
                    {
                        if (effects[i].Id == info.EffectId)
                            return AutomationKey.Effect(i, info.Id);
                    }
                }
                if (info.EffectIndex >= 0 && info.EffectIndex < effects.Count && effects[info.EffectIndex].Type == info.EffectType)
                    return AutomationKey.Effect(info.EffectIndex, info.Id);
                return null;
            default:
                // 不认识的来源（更新版本写的）：跳过这一条，不让它挡住整份布局。
                return null;
        }
    }

    const string VoiceSource = "voice";
    const string EffectSource = "effect";
    const string NoteLaneSource = "noteLane";
    const string PhonemeLaneSource = "phonemeLane";
}
