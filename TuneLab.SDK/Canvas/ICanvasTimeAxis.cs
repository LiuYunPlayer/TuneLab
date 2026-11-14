namespace TuneLab.SDK;

// 沿时间轴摆放的画布（眼下即时间轴画布）的时间换算（宿主实现）：秒 / tick ↔ 画布内横坐标。全部只在主线程上用。
//
// 两种时间：
// · 秒是全局秒（与声明视图 IVoiceSynthesisNoteView.StartTime 同一口径）：SecondToX(0) = 工程开头。
// · tick 是**局部** tick，相对这块数据的原点（时间轴画布 = 所属音符的起点，音素级同样是所属音符的起点）：TickToX(0) = 原点。
//   原点由宿主给、插件不必知道它在工程里的哪里；音符挪了、速度改了，换算随之变，数据不用动。局部 tick 可以为负。
// 横坐标是画布内的逻辑像素，与绘制、鼠标事件同一个坐标系。按自己数据的单位选一组换算，不经另一种单位中转；
// 不经 x 的 tick ↔ 秒换算用 TimeConverter。命名：换算按单位（Second / Tick）。
//
// **两组都不要假设线性**：眼下钢琴窗的横轴是 tick 轴（TickToX 恰好线性、SecondToX 在速度变化处不均匀），以后钢琴窗若能切成均匀
// 时间轴就反过来。缩放、滚动、速度表改动后换算随之变，宿主会重新调用 Render。
//
// 这块画布覆盖的时间段就是 XToSecond(0) 到 XToSecond(Width)，不另给起止：它未必等于对象自己的起止（note 级画布按 note 在参数面板里
// 的那一段，会被钳到下一个 note 的起点；音素的显示时间由宿主排版）。
//
// 所见即所闻：不在横轴线性的那个域里定义的曲线（眼下即按秒定义的），别只换算控制点再连直线（跨过速度变化处会与按秒插值的结果
// 不一致），逐像素采样——每一列 XToSecond(x) 换回秒、再按秒求值——就与合成严格一致。画与合成必须在同一个域里插值。
public interface ICanvasTimeAxis
{
    double SecondToX(double second);
    double XToSecond(double x);
    double TickToX(double tick);
    double XToTick(double x);
    // 当前的时间换算器（局部 tick ↔ 全局秒，原点同上）。速度表一改就是另一份：别跨 Render 缓存，用时再取。
    ITimeConverter TimeConverter { get; }
}
