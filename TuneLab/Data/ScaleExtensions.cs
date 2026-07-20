using System;
using TuneLab.SDK;

namespace TuneLab.Data;

internal static class ScaleExtensions
{
    // 把值投影到标度的可表示集：先把归一化位置钳到 [0,1] 再取值——
    //   · 线性标度 ⇒ 仅钳到 [min,max]（连续，无格点）；
    //   · 离散标度（如 NormalizedScale.Integer）⇒ 钳位 + 落到最近格点。
    // NaN（分段轨段间空）原样透传、不参与投影。约定标度单调递增（ToNormalized 为其底层连续逆）。
    // 这是宿主对"离散 scale ⇒ 信号处处落格"的强制点：求值/渲染把 Hermite 连续输出投影回标度，
    // 与操作层写入吸附互补——覆盖 load/preset/插件回喂/undo 等一切绕过操作层的路径。
    public static double Project(this INormalizedScale scale, double value)
        => double.IsNaN(value) ? value : scale.ToValue(Math.Clamp(scale.ToNormalized(value), 0, 1));
}
