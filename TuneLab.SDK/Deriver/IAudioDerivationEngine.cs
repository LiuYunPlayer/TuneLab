using TuneLab.Foundation;

namespace TuneLab.SDK;

// 一次性、音频驱动的派生引擎：对一段固定音频跑一次离线模型，从它派生出新的工程材料
//（提取信息 = audio→MIDI 转写 / 音高 / 节拍速度；分离成分 = 声部 stem；生成新内容 = 变声）。
// 与合成三类（voice/instrument/effect）的反应式模型相反：用户显式触发一次、产物是用户拥有可自由编辑的
// 新工程数据、绝不监听输入变更自动重跑。派生而非取代：源音频保留，产物是并存的新材料。
//
// engine 即 deriver（形态近 effect：一个 engine = 一种派生算法），身份 id + 显示名经 manifest 声明。
// 无常驻 session：Derive 收一份不可变输入快照 → 返回一次产物。
//
// 单位纪律：派生产物一律说物理量（秒 / BPM），宿主是 tick 网格的唯一主人，所有秒↔tick 换算都在宿主侧做。
// 插件既不消费也不生产 tick 时间线（见 DerivedResult）。
//
// 「能产什么」不做静态声明：产物随参数而变、无法（也不该）提前预知，唯一真相是运行时结果（DerivedResult 里哪些槽非空）。
//
// 加性约定（插件实现面）：将来在本面新增成员一律用默认接口方法（DIM）给兜底体，使增补不破已装插件。
public interface IAudioDerivationEngine
{
    // 初始化引擎（懒加载模型等）。无参、失败抛异常：宿主在调用边界 catch。
    // 不传安装路径——插件 DLL 经 Assembly.Location 即可自定位包目录。
    void Init();

    // 释放引擎资源。
    void Destroy();

    // 参数面板配置：声明该派生器暴露给用户的可编辑参数（灵敏度 / 最短音符 / onset 阈值…），渲染为对话框里的属性面板。
    // config 是当前情境（context）的纯函数——宿主在用户改值时按当前值重算并 diff 到控件树（条件字段随值显隐）。
    // 须为纯函数（同输入同输出、无副作用、轻量）、不依赖 Init：对话框在 Init 前即会调用（呈现参数而不加载模型）。
    // 静态面板的引擎忽略 context 返回固定 ObjectConfig 即可。
    ObjectConfig GetPropertyConfig(IAudioDerivationContext context);

    // 跑一次派生：收一份不可变输入快照（冻结源音频 + 冻结参数）→ 返回一次产物（物理秒基）；null = 取消或什么都没产出。
    // 取消尽力而为（CancellationToken），取消是正常结局（返回 null、不抛 OperationCanceledException）；
    // 真正错误才抛异常，宿主在调用边界 catch。progress 报进度 + 阶段文案。宿主在后台 worker 线程调用。
    Task<DerivedResult?> Derive(IAudioDerivationInput input, IProgress<DerivationProgress> progress, CancellationToken cancellation = default);
}
