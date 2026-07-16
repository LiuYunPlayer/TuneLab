using TuneLab.Foundation;

namespace TuneLab.SDK;

// agent 模型引擎：把宿主的对话/工具调用翻译成对某个外部 LLM 的请求。一个引擎实例对应一种适配器类型
// （如"OpenAI 兼容端点"）；用户在设置界面填好该引擎声明的 properties（端点、密钥、模型名等）后，
// 宿主用这些值创建一个 IAgentModelSession 开始工作。与 IEffectSynthesisEngine 同范式（声明参数 + 懒加载 + 创建会话）。
public interface IAgentModelEngine
{
    // 参数面板配置：声明该适配器暴露给用户的可编辑配置（端点 URL、API Key、模型名、温度等），
    // 由宿主渲染为属性面板。密钥类字段用 TextBoxConfig { IsPassword = true } 掩码显示。
    // 与 effect 同约定：须为纯函数（同输入同输出、无副作用、轻量），可随已填值条件显隐字段。
    ObjectConfig GetPropertyConfig(IAgentModelPropertyContext context);

    // 初始化引擎（如加载所需 SDK/校验环境）。无参、失败抛异常：宿主在调用边界 catch。
    void Init();

    // 释放引擎资源。
    void Destroy();

    // 用"用户在设置界面确定后收集的配置值"创建一次会话。配置语义由引擎自己解释（与其 GetPropertyConfig 对应）。
    IAgentModelSession CreateSession(PropertyObject properties);
}
