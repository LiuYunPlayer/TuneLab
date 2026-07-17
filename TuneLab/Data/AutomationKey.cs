namespace TuneLab.Data;

// 参数面板轨道的来源类别：voice/part 级自动化、某个 effect 的自动化，或属性 lane（用户钉选的有界数值属性，
// 按 note / 音素分段呈现——数据存 note.Properties / phoneme.Properties，非时间曲线）。
internal enum AutomationSource
{
    Voice,
    Effect,
    NoteLane,
    PhonemeLane,
}

// 底部参数面板里"当前编辑/可见的轨"的标识。区分来源（voice/part 级、第几个 effect，或 note/phoneme 属性 lane）
// + 该来源内的 plain id。用类型而非伪造字符串前缀来路由：避免与真实自动化 id 撞名、可值相等比较。数据层
// （part.Automations / effect.Automations / note.Properties / phoneme.Properties）仍按 plain id 存储，无歧义；
// 本 key 只承担 UI→数据的路由，不持久化。同一 id 在不同来源下是不同 key（Source 参与相等比较），
// 路由层须各自分派、不得互相兜底。
internal readonly record struct AutomationKey(AutomationSource Source, int EffectIndex, string Id)
{
    // EffectIndex 仅对 Effect 来源有意义（Part.Effects 下标）；其余来源恒 -1。
    public bool IsEffect => Source == AutomationSource.Effect;
    public bool IsNoteLane => Source == AutomationSource.NoteLane;
    public bool IsPhonemeLane => Source == AutomationSource.PhonemeLane;
    // 属性 lane 统称（automation 路由的排除口径）。
    public bool IsLane => Source is AutomationSource.NoteLane or AutomationSource.PhonemeLane;

    public static AutomationKey Voice(string id) => new(AutomationSource.Voice, -1, id);
    public static AutomationKey Effect(int effectIndex, string id) => new(AutomationSource.Effect, effectIndex, id);
    public static AutomationKey NoteLane(string id) => new(AutomationSource.NoteLane, -1, id);
    public static AutomationKey PhonemeLane(string id) => new(AutomationSource.PhonemeLane, -1, id);
}

// effect 自动化轨在【数据层】的持久身份：effect 实例稳定 id（IEffect.Id）+ 该 effect 内的轨 id。
// 用于 Vibrato.AffectedEffectAutomations 的键（随工程持久化）。与 AutomationKey 的分工：AutomationKey 是
// UI→数据的路由键（带槽位下标、不持久化，交互时经 part 解析成 id）；本类型是落进数据的外键——
// 锚定实例身份而非链内位置，重排/替换无需任何重映射；删除留孤儿条目，undo 恢复同 id 即自然重连。
internal readonly record struct EffectAutomationRef(string EffectId, string Id);
