namespace TuneLab.SDK.Voice;

// 半透明调度 token：GetNextSegment 在数据线程廉价 peek 返回——只记范围/引用、不深拷
// （peek 常被多会话 speculative 地问、多数不中选，深拷会白做）。
// 宿主调度只看秒边界排优先级；物化快照时读捕获声明（Notes/tick 区间）；
// 插件其余私有载荷（管线级缓存键等）放进自己的实现类、commit 时 downcast 取出
// （宿主只经 SDK 接口持有，插件在自己 ALC 内 downcast，跨 ALC 安全）。
//
// token 短命：仅当前调度 tick 内有效、不跨 tick 缓存。peek→commit 在同一调度 tick 内
// 同步执行、期间无编辑可插入，故 segment 持 live note 引用安全。
public interface ISynthesisSegment
{
    // 调度边界（秒，与音频产物同一时间系）。
    double StartTime { get; }
    double EndTime { get; }

    // —— 捕获声明（宿主按此物化 ISynthesisSnapshot）——
    // 这段合成需要的 note：段内 + 协同发音邻居边距，插件自由圈定；
    // 物化出的 snapshot.Notes 与本列表索引对齐（插件据此把快照产物归属回 live note）。
    IReadOnlyList<ISynthesisNote> Notes { get; }
    // automation/pitch 开窗区间（曲线数据在 tick 轴）。
    double StartTick { get; }
    double EndTick { get; }
}
