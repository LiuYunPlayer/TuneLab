namespace TuneLab.SDK;

// 画布 lane（CanvasLaneConfig）的宿主：在 ICanvasHost 之上多一条时间轴（横）与一条音高轴（纵）。
// 画布 lane 上的一块永远只对应一个对象（一个 note 或一个音素），所以两条轴一定有；宿主一律给，用不到的不调即可。
// 参数面板当作一扇看向下方音符区的窗口：纵轴就是它背后那块音符区的音高。其余成员与侧栏画布同一套契约。
// 一种画布一个宿主接口（将来的时间轴画布另有自己的）；想让同一段绘制代码给几种画布共用，就只依赖两条轴，不依赖宿主接口。
public interface ICanvasLaneHost : ICanvasHost
{
    ICanvasTimeAxis TimeAxis { get; }
    ICanvasPitchAxis PitchAxis { get; }
}
