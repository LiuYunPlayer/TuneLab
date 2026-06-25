using TuneLab.Foundation;

namespace TuneLab.SDK;

// 一次 part 合成的有状态会话：声明 + 调度 + 产物 + 状态全在此——instrument 专属面。
// 厚插件原则：分片、调度状态、音频缓冲、合成进度、失效（dirty）判定全由插件托管；宿主只推变更流
//（经 IInstrumentContext）、驱动调度、读产物展示。
//
// 生命周期：绑定一个 part，活到 part 被删除（Dispose）；换音源时宿主丢弃旧会话、重建新会话。
//
// 与 voice 的 ISynthesisSession 差异：【无 DefaultLyric、无 SynthesizedPhonemes、无 SynthesizedPitch】
// 及其事件——instrument 仅产音频 + 可选参数回显。调度、音频交付（经 context.CreateAudioSegment）、
// 状态时间线与 voice 同构。
//
// 加性约定：将来若需在本（插件实现）面新增输出产物成员，一律用默认接口方法给 Empty 兜底，使增补
// 不破已装插件（见 instrument-sdk-design.md §4）。
public interface IInstrumentSession : IDisposable
{
    // —— 调度（宿主驱动逐步合成；与 voice 逐字同构）——
    // peek：窗内"下一块待合成"的纯值边界，无副作用；只在会话空闲时被问。null = 窗内无待合成。
    SynthesisRange? GetNextSegment(double startTime, double endTime);

    // commit：合成宿主选中的这一块。入参是与选中它的那次 peek【完全相同】的窗口（秒）——插件按同一窗口
    // 确定性重导出 notelist。插件在同步前缀经 IInstrumentContext.GetSnapshot 拉取快照，之后才 offload。
    // await 返回 = 槽位释放。返回纯 Task、无 outcome（取消不抛、错误经 GetStatus 看、是否还有待合成经
    // GetNextSegment 看）。
    Task SynthesizeNext(double startTime, double endTime, CancellationToken cancellation = default);

    // —— 音频产物 ——
    // 经 IAudioSegment 握柄交付（插件向 context.CreateAudioSegment 申请段、写入、Commit）；
    // 时间对齐：全局 0 时刻 = 采样点 0；覆盖区域权威信息是各音频段（未交付区域即静音）。

    // —— 参数回显（按轨 id 键，与音频同一秒时间系）：key 与 GetSynthesizedParameterConfigs 对齐，
    //    仅承载曲线数据本身（轨形态 / 色由 config 给）。引擎不声明回显轨即恒空。 ——
    IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters { get; }

    // —— 状态 / 按段报错（统一时间线）——
    IReadOnlyList<SynthesisStatusSegment> GetStatus();

    // —— 更新信号（按产物分离；只在对应产物真正变化时 fire）——
    // 音频产物不在此列——经 IAudioSegment.Commit 自有通道驱动。出方向事件允许任意线程触发，宿主负责 marshal。
    event Action? SynthesizedParametersChanged;
    // 状态 / 进度（GetStatus）变化——通常最高频（进度条逐 tick），不触及产物。
    event Action? StatusChanged;
}
