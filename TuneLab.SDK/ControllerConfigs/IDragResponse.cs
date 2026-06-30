using System;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// 拖拽响应：把一次拖拽的像素位移映射成数值变化。是 DraggableNumberBox 的核心抽象——与 slider 的 INormalizedScale
// 对位（slider 抽"位置↔值"，本接口抽"位移→值"）。固定灵敏度只是其线性特例，故不焊进 config。
//
// 纯函数，入参 = 拖拽起点的值 + 自起点累计的像素位移（非逐帧增量累加）：非线性映射下逐帧增量累加会路径依赖、
// 回拖不可逆；以"起点值 + 累计位移"求当前值，保证拖到同一像素位置值确定、回拖精确还原。
//
// delta 坐标按用户直觉：X 右为正、Y 上为正、正即变大。框架坐标系 Y 向下为正，由控件入口翻转后再喂入，
// 故接口实现一律按直觉写，无需任何符号修正。
public interface IDragResponse
{
    double Apply(double startValue, Point delta);
}

// 二维位移投影到一维驱动量的轴选择。Linear/Exponential 内部据此取 delta 的某一分量；两轴正向均已是"变大"。
public enum DragAxis
{
    Horizontal,
    Vertical,
}

// IDragResponse 的语法糖工厂。Custom 是逃生口（拿到完整二维 delta，可做"水平粗调 + 垂直细调"、按比例增长等
// 两轴各有语义 / 非线性的手感）；Linear 仅为便利，覆盖单轴线性常用场景。
// 指数 / 加速等内置非线性曲线暂不开放——需要时经 Custom 表达，待有真实用例再加内置工厂（零破坏增量）。
public static class DragResponse
{
    // 线性：值增量 = 投影位移 × sensitivity。最常用。
    public static IDragResponse Linear(double sensitivity, DragAxis axis = DragAxis.Horizontal)
        => new LinearResponse(sensitivity, axis);

    public static IDragResponse Custom(Func<double, Point, double> apply)
        => new CustomResponse(apply);

    static double Project(Point delta, DragAxis axis)
        => axis == DragAxis.Horizontal ? delta.X : delta.Y;

    sealed class LinearResponse(double sensitivity, DragAxis axis) : IDragResponse
    {
        public double Apply(double startValue, Point delta)
            => startValue + Project(delta, axis) * sensitivity;
    }

    sealed class CustomResponse(Func<double, Point, double> apply) : IDragResponse
    {
        public double Apply(double startValue, Point delta) => apply(startValue, delta);
    }
}
