namespace TuneLab.SDK;

// 画布坐标里的矩形（逻辑像素，左上角为原点）。宽或高为负 = 空矩形：画它什么都不画，拿它裁剪则什么都不留。
// 带 Canvas 前缀而非放进 Foundation：插件为取 Color 会 using System.Drawing，那里已有 Size / Rectangle，同名会撞。
public readonly record struct CanvasRect(double X, double Y, double Width, double Height)
{
    // 四条边。X / Y 是矩形的位置，Left / Top 是边——两个概念：画布坐标原点在左上、y 朝下，位置才恰好落在左上角，
    // 于是 X 与 Left、Y 与 Top 取值相等（换成左下原点的坐标系，Y 对应的就是 Bottom）。
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
}
