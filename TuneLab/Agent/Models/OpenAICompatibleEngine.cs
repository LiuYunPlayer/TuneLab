using TuneLab.Foundation;
using TuneLab.I18N;
using TuneLab.SDK;

namespace TuneLab.Agent.Models;

// 内置的参考模型适配器：对接任何 OpenAI 兼容的 /chat/completions 端点（OpenAI 官方、各云厂商、
// 本地 Ollama / LM Studio / vLLM 等）。本身不含模型——端点/密钥/模型名由用户在设置界面填入。
// 作为内置引擎随宿主开箱即用；新的模型适配器走 PR 加进宿主（agent-model 不开放外部扩展，见 IAgentModelEngine 头注释）。
internal sealed class OpenAICompatibleEngine : IAgentModelEngine
{
    public ObjectConfig GetPropertyConfig(IAgentModelPropertyContext context)
    {
        var properties = new OrderedMap<PropertyKey, IControllerConfig>();
        properties.Add(("base_url", "Base URL".Tr(this)), TextBoxConfig.Create("https://api.openai.com/v1"));
        properties.Add(("api_key", "API Key".Tr(this)), TextBoxConfig.Create().WithPassword());
        properties.Add(("model", "Model".Tr(this)), TextBoxConfig.Create("gpt-4o-mini"));
        properties.Add(("temperature", "Temperature".Tr(this)), SliderConfig.Linear(1, 0, 2));
        // 0 = 不发送 max_tokens，由服务端用默认上限。
        properties.Add(("max_tokens", "Max Tokens (0=auto)".Tr(this)), SliderConfig.Integer(0, 0, 32768));
        return ObjectConfig.Create(properties);
    }

    public void Init() { }

    public void Destroy() { }

    public IAgentModelSession CreateSession(PropertyObject properties)
    {
        var baseUrl = properties.GetString("base_url", "https://api.openai.com/v1");
        var apiKey = properties.GetString("api_key", "");
        var model = properties.GetString("model", "gpt-4o-mini");
        var temperature = properties.GetDouble("temperature", 1);
        var maxTokens = (int)properties.GetDouble("max_tokens", 0);
        return new OpenAICompatibleSession(baseUrl, apiKey, model, temperature, maxTokens);
    }
}
