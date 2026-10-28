namespace TuneLab.SDK;

// 时间轴画布（TimelineCanvasConfig）的宿主：在 ICanvasHost 之上多一条时间轴。
// 一块时间轴画布永远只对应一个对象（一个 note 或一个音素），所以时间轴一定有。其余成员与侧栏画布同一套契约。
public interface ITimelineCanvasHost : ICanvasHost
{
    ICanvasTimeAxis TimeAxis { get; }
}
