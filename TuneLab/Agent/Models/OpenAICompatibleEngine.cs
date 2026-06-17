using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Agent.Models;

// 内置的参考模型适配器：对接任何 OpenAI 兼容的 /chat/completions 端点（OpenAI 官方、各云厂商、
// 本地 Ollama / LM Studio / vLLM 等）。本身不含模型——端点/密钥/模型名由用户在设置界面填入。
// 作为内置引擎随宿主开箱即用，同时是第三方写其它 agent-model 插件的参照实现。
internal sealed class OpenAICompatibleEngine : IAgentModelEngine
{
    public ObjectConfig GetPropertyConfig(IAgentModelPropertyContext context)
    {
        var properties = new OrderedMap<string, IControllerConfig>();
        properties.Add("base_url", new TextBoxConfig { DisplayText = "Base URL", DefaultValue = "https://api.openai.com/v1" });
        properties.Add("api_key", new TextBoxConfig { DisplayText = "API Key", DefaultValue = "", IsPassword = true });
        properties.Add("model", new TextBoxConfig { DisplayText = "Model", DefaultValue = "gpt-4o-mini" });
        properties.Add("temperature", new SliderConfig { DisplayText = "Temperature", MinValue = 0, MaxValue = 2, DefaultValue = 1 });
        // 0 = 不发送 max_tokens，由服务端用默认上限。
        properties.Add("max_tokens", new SliderConfig { DisplayText = "Max Tokens (0=auto)", MinValue = 0, MaxValue = 32768, DefaultValue = 0, IsInteger = true });
        return new ObjectConfig { Properties = properties };
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
