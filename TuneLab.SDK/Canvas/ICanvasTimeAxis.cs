namespace TuneLab.SDK;

// 时间轴画布的时间换算（宿主实现）：秒 ↔ 画布内横坐标。全部只在主线程上用。
//
// 秒是全局秒（与声明视图 IVoiceSynthesisNoteView.StartTime 同一口径）；横坐标是画布内的逻辑像素，与绘制、鼠标事件同一个坐标系。
// 换算跟着钢琴窗的时间轴走（眼下是 tick 轴：速度变化处不均匀），不要假设它线性。缩放、速度表改动后换算随之变，宿主会重新调用 Render。
//
// 这块画布覆盖的时间段就是 XToTime(0) 到 XToTime(Width)，不另给起止：它未必等于对象自己的起止（note 级画布按 note 在参数面板里
// 的那一段，会被钳到下一个 note 的起点；音素的显示时间由宿主排版）。按秒画的内容拿自己数据里的绝对时间换算即可。
//
// 所见即所闻：按秒定义的曲线别只换算控制点再连直线（跨过速度变化处会与按秒插值的结果不一致），逐像素采样——每一列
// XToTime(x) 换回秒、再按秒求值——就与合成严格一致。
public interface ICanvasTimeAxis
{
    double TimeToX(double time);
    double XToTime(double x);
}
