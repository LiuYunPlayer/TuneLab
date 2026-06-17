using System;

namespace TuneLab.SDK;

// 标注一个 agent 模型引擎实现类，type 是它登记的模型适配器类型标识。
// 宿主按此 attribute 发现并实例化引擎（与 [EffectEngine]、[VoiceEngine] 同范式）。
// 模型适配器只负责"把对话+工具发给某个 LLM、拿回回复"，不内置任何模型——具体接哪个模型
// （OpenAI 兼容端点 / 本地 Ollama / …）由用户在设置界面填入引擎声明的 properties 决定。
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public class AgentModelEngineAttribute(string type) : Attribute
{
    public string Type { get; } = type;
}
