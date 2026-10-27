namespace TuneLab.SDK;

// 九宫格对齐（CanvasTextStyle 的对齐：DrawText 与 PopupTextBox 共用）。值是冻结契约（编译进插件），故显式写出：
// 低 2 位 = 水平（0 左 / 1 中 / 2 右），第 2–3 位 = 垂直（0 上 / 4 中 / 8 下）。插件只按名字使用、别对它做位运算
// （不是 Flags）。加成员是加性：宿主对未知值按 Center 处理。
public enum CanvasAlignment
{
    LeftTop = 0,
    CenterTop = 1,
    RightTop = 2,
    LeftCenter = 4,
    Center = 5,
    RightCenter = 6,
    LeftBottom = 8,
    CenterBottom = 9,
    RightBottom = 10,
}
