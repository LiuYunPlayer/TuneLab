namespace TuneLab.Data;

// 底部参数面板里"当前编辑/可见的自动化轨"的标识。区分来源（voice/part 级，或第几个 effect）+ 该来源内的 plain id。
// 用类型而非伪造字符串前缀来路由：避免与真实自动化 id 撞名、可值相等比较。数据层（part.Automations /
// effect.Automations）仍按 plain id 存储，无歧义；本 key 只承担 UI→数据的路由，不持久化。
internal readonly record struct AutomationKey(int EffectIndex, string Id)
{
    // EffectIndex < 0 表示 voice/part 级自动化；>= 0 表示 Part.Effects[EffectIndex] 的自动化。
    public bool IsEffect => EffectIndex >= 0;

    public static AutomationKey Voice(string id) => new(-1, id);
    public static AutomationKey Effect(int effectIndex, string id) => new(effectIndex, id);
}
