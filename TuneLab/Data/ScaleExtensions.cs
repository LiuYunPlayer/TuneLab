using TuneLab.SDK;

namespace TuneLab.Data;

internal static class ScaleExtensions
{
    // 把值投影到标度的可表示集（往返标度一次）——
    //   · 离散标度（如 NormalizedScale.Integer）⇒ 落到最近格点；
    //   · 连续标度 ⇒ 数学上恒等，仅有浮点往返的 ULP 级扰动（勿依赖"原样透传"做等值比较/去重）。
    // NaN（分段轨段间空）原样透传、不参与投影。约定标度单调递增（ToNormalized 为其底层连续逆）。
    // 这是宿主对"离散 scale ⇒ 信号处处落格"的强制点：求值/渲染把 Hermite 连续输出投影回标度，
    // 与操作层写入吸附互补——覆盖 load/preset/插件回喂/undo 等一切绕过操作层的路径。
    //
    // 只管格点、不管值域：投影不把值钳进 [ToValue(0), ToValue(1)]。因为"钳进量程"是一个宿主无法兑现的保证——
    // 标度单调只是文档约定（INormalizedScale 是公共接口、Custom 收任意两个 lambda），非单调时端点不是极值、
    // ToNormalized 的逆也不唯一，钳位既不保证落进插件理解的范围、又会破坏合法值。故值域校验归插件自己按需做，
    // 宿主不提供这层假保证（呈现侧的越界裁剪是画布几何，见 AutomationRenderer.NormalizedToY）。
    public static double Project(this INormalizedScale scale, double value)
        => double.IsNaN(value) ? value : scale.ToValue(scale.ToNormalized(value));
}
