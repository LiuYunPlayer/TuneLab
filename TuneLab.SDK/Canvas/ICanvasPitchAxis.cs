namespace TuneLab.SDK;

// 沿时间轴摆放的画布（眼下即时间轴画布）的音高换算（宿主实现）：音高 ↔ 画布内纵坐标。全部只在主线程上用。
//
// 音高以半音计，同音符 Pitch 与合成音高（60 = 中央 C）。**n 号键占 [n, n + 1)**：PitchToY(n) 是 n 号键的下沿、PitchToY(n + 1) 是上沿，
// 画一格琴键 / 音符块就是这两条线之间，指针下的键 = Math.Floor(YToPitch(y))。
// 音高**值**落在键的中线上（唱准的 60 画在 60 号键正中，与宿主画音高曲线同一口径）：画音高值用 PitchToY(值 + 0.5)；
// 由指针读出音高值用 YToPitch(y) - 0.5——直接拿 YToPitch 当音高存会偏高 50 音分。
// 纵坐标是画布内的逻辑像素（向下为正：音高越高 y 越小）。画布在钢琴窗里就对齐琴键；在参数面板里则是面板背后那块音符区的音高。
// 纵向滚动、缩放后换算随之变，宿主会重新调用 Render。
public interface ICanvasPitchAxis
{
    double PitchToY(double pitch);
    double YToPitch(double y);
}
