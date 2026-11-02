namespace TuneLab.SDK;

// 画布 lane（CanvasLaneConfig）的宿主：在 ICanvasHost 之上多一条时间轴。
// 画布 lane 上的一块永远只对应一个对象（一个 note 或一个音素），所以时间轴一定有。其余成员与侧栏画布同一套契约。
public interface ITimelineCanvasHost : ICanvasHost
{
    ICanvasTimeAxis TimeAxis { get; }
}
