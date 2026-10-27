namespace TuneLab.Foundation;

// 矩形（坐标与 Point 同一套）。宽或高为负 = 空矩形。只读值：要改就 rect with { X = … } 得到新值。
// 只给眼下用得上的几个方法；求交 / 合并等等真有需求再加（加的时候先定"不相交时返回什么"）。
public readonly record struct Rect(double X, double Y, double Width, double Height)
{
    // 按四条边构造（名字即参数顺序：左、上、右、下）。
    public static Rect FromLTRB(double left, double top, double right, double bottom) => new(left, top, right - left, bottom - top);

    // 四条边。X / Y 是矩形的位置，Left / Top 是边——两个概念：TuneLab 的界面坐标原点在左上、y 朝下，位置才恰好落在左上角，
    // 于是 X 与 Left、Y 与 Top 取值相等（换成左下原点的坐标系，Y 对应的就是 Bottom）。
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public Point Center => new(X + Width / 2, Y + Height / 2);

    // 点在矩形内（含四条边上）。空矩形不含任何点。
    public bool Contains(Point point) => point.X >= Left && point.X <= Right && point.Y >= Top && point.Y <= Bottom;

    // 四周各向外扩 dx / dy（负数即向内缩，如留内边距）；缩过头宽高变负 = 空矩形。
    public Rect Inflate(double dx, double dy) => new(X - dx, Y - dy, Width + 2 * dx, Height + 2 * dy);
}
