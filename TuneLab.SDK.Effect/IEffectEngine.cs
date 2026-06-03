using TuneLab.Primitives.DataStructures;
using TuneLab.SDK.Base;

namespace TuneLab.SDK.Effect;

// 效果器引擎：面向耗时较长的离线 effect 模型（如 SVC 换声），对一段已合成音频做整段变换。
// 一个引擎实例对应一种效果器类型；宿主为每个工程里的 effect 创建合成任务驱动它。
public interface IEffectEngine
{
    // 参数面板配置：声明该效果器暴露给用户的可编辑参数（渲染为属性面板）。
    ObjectConfig PropertyConfig { get; }

    // 自动化轨配置：声明该效果器支持的、可随时间变化的自动化参数。
    IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs { get; }

    // 初始化引擎（加载模型等）。enginePath 为插件包目录。失败返回 false 并给出 error。
    bool Init(string enginePath, out string? error);

    // 释放引擎资源。
    void Destroy();

    // 为一段输入音频创建合成任务，处理结果写入 output。
    IEffectSynthesisTask CreateSynthesisTask(IEffectSynthesisInput input, IEffectSynthesisOutput output);
}
