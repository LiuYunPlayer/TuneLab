using TuneLab.SDK;

namespace TuneLab.Data;

// 宿主侧「键 + 自动化配置」对。当宿主把 voice/effect 的 PropertyKey-keyed 配置扁平成 AutomationKey-keyed 集合
//（如 readback 轨，AutomationKey 只带 EffectIndex + id、不带 PropertyKey 的 DisplayText）时，轨名/翻译会丢失——
// 本对把原 PropertyKey 一并带上，使下游渲染仍能取到轨名。Id/DisplayText 是便利访问（DisplayText 缺省回退 Id）。
public readonly struct AutomationConfigEntry(PropertyKey key, AutomationConfig config)
{
    public PropertyKey Key { get; } = key;
    public AutomationConfig Config { get; } = config;
    public string Id => Key.Id;
    public string DisplayText => Key.DisplayText ?? Key.Id;
}
