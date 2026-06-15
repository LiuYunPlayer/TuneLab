using System;
using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.SDK;

// 一条「effect 实例 × 音频段」的持久处理器：跨重复调用保留内部中间结果，按 change 描述的变化事实
// 精确决定内部哪些级需重算（引擎私有失效图——automation A 标脏的内部内容可与 automation B 不同，
// 宿主无从复制）。无内部增量可做的引擎每次整段重处理即可（退化为整段进整段出）。
//
// 线程纪律：input / change 仅可在 Process 的同步前缀（数据线程）读取；offload 到 worker 后
// worker 只读同步前缀物化的不可变值（合成永不回碰宿主活数据）。
public interface IEffectProcessor : IDisposable
{
    // (重)处理该段。input = 本次整段输入音频 + 当前参数快照 + 自动化求值入口；change = 自上次 Process
    // 以来的变化事实（首次调用 IsInitial=true）；处理结果整段写入 output（可比输入长，含尾巴）。
    // 返回纯 Task（无 outcome）：取消是正常调度结局——经 cancellation 请求、不抛 OperationCanceledException、
    // 正常返回（不可中止的引擎把这段跑完才返回）；错误抛异常，宿主在调用边界 catch → 该级 passthrough 降级。
    // progress 用 IProgress（Progress<T> 自带 SynchronizationContext marshal）。
    Task Process(IEffectInput input, IEffectOutput output, IEffectChange change,
                 IProgress<double>? progress = null, CancellationToken cancellation = default);
}
