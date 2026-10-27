using TuneLab.Foundation;

namespace TuneLab.SDK;

// 画刷：区域怎么填（填充形状、文字的颜色）。不可变、只走静态工厂；眼下只有纯色，以后加渐变等是加一个工厂，
// 不动任何绘制方法的签名。可以建好存进静态字段反复用。
public sealed class CanvasBrush
{
    private CanvasBrush() { }

    public static CanvasBrush Solid(Color color) => new() { SolidColor = color };

    // 宿主侧：画刷的内容（插件只造不读，经 InternalsVisibleTo 给宿主），与转成的原生画刷缓存（只在主线程上读写）。
    internal Color SolidColor { get; private init; }
    internal object? Native { get; set; }
}
