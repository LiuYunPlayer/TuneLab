using TuneLab.SDK;

namespace TuneLab.UI;

// 「二值区间」呈现的宿主侧判定——纯派生谓词，刻意不进 SDK ABI 面（见 SDK 冻结审查）：
// 分段轨（presence=开、gap=关）+ 退化量程（min==max ⇒ 无值轴）⇒ 渲染为满高开关色带、而非折线曲线。
// 两个信号都语义诚实且正交：IsPiecewise 由 DefaultValue=NaN 表达、退化量程由 min==max 表达；
// legacy automation 恒为连续轨（compat 转发实数默认值），故永不满足 IsPiecewise、天然被排除在 band 之外。
// 作者侧无需新 SDK 成员：AutomationConfig.Create(v, v)（Create 默认 NaN ⇒ 分段）即产出 band-eligible 配置。
internal static class BandPresentation
{
    public static bool IsBand(this AutomationConfig config)
        => config.IsPiecewise && config.MinValue == config.MaxValue;
}
