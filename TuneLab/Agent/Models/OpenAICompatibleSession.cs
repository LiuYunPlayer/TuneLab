using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.SDK;

namespace TuneLab.Agent.Models;

// 一次到某个 OpenAI 兼容端点的会话。把宿主中立的 AgentModelRequest/Reply 翻译成 /chat/completions 协议。
internal sealed class OpenAICompatibleSession : IAgentModelSession
{
    public OpenAICompatibleSession(string baseUrl, string apiKey, string model, double temperature, int maxTokens)
    {
        mEndpoint = baseUrl.TrimEnd('/') + "/chat/completions";
        mModel = model;
        mTemperature = temperature;
        mMaxTokens = maxTokens;
        mHttp = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        if (!string.IsNullOrEmpty(apiKey))
            mHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<AgentModelReply> SendAsync(AgentModelRequest request, CancellationToken cancellationToken)
    {
        var body = BuildRequestBody(request).ToJsonString();
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await mHttp.PostAsync(mEndpoint, content, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new Exception(string.Format("Model request failed ({0}): {1}", (int)response.StatusCode, text));
        return ParseReply(text);
    }

    JsonObject BuildRequestBody(AgentModelRequest request)
    {
        var messages = new JsonArray();
        foreach (var message in request.Messages)
            messages.Add(BuildMessage(message));

        var body = new JsonObject
        {
            ["model"] = mModel,
            ["temperature"] = mTemperature,
            ["messages"] = messages,
        };

        if (mMaxTokens > 0)
            body["max_tokens"] = mMaxTokens;

        if (request.Tools.Count > 0)
        {
            var tools = new JsonArray();
            foreach (var tool in request.Tools)
            {
                tools.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = JsonNode.Parse(tool.ParametersJsonSchema),
                    },
                });
            }
            body["tools"] = tools;
        }

        return body;
    }

    static JsonObject BuildMessage(AgentMessage message)
    {
        var role = message.Role switch
        {
            AgentRole.System => "system",
            AgentRole.User => "user",
            AgentRole.Assistant => "assistant",
            AgentRole.Tool => "tool",
            _ => "user",
        };

        var obj = new JsonObject
        {
            ["role"] = role,
            // content 可为 null（assistant 仅有工具调用时）；OpenAI 协议接受 content:null。
            ["content"] = message.Content,
        };

        if (message.Role == AgentRole.Tool && message.ToolCallId != null)
            obj["tool_call_id"] = message.ToolCallId;

        if (message.Role == AgentRole.Assistant && message.ToolCalls is { Count: > 0 })
        {
            var calls = new JsonArray();
            foreach (var call in message.ToolCalls)
            {
                calls.Add(new JsonObject
                {
                    ["id"] = call.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = call.Name,
                        // OpenAI 约定 arguments 是 JSON 字符串而非对象。
                        ["arguments"] = call.ArgumentsJson,
                    },
                });
            }
            obj["tool_calls"] = calls;
        }

        return obj;
    }

    static AgentModelReply ParseReply(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");

        string? content = message.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String
            ? contentElement.GetString()
            : null;

        var toolCalls = new List<AgentToolCall>();
        if (message.TryGetProperty("tool_calls", out var toolCallsElement) && toolCallsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in toolCallsElement.EnumerateArray())
            {
                if (!call.TryGetProperty("function", out var function))
                    continue;
                toolCalls.Add(new AgentToolCall
                {
                    Id = call.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
                    Name = function.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                    ArgumentsJson = function.TryGetProperty("arguments", out var args) ? args.GetString() ?? "{}" : "{}",
                });
            }
        }

        return new AgentModelReply { Content = content, ToolCalls = toolCalls, Usage = ParseUsage(doc.RootElement) };
    }

    // OpenAI 协议 usage：{ prompt_tokens, completion_tokens, total_tokens }。缺失则返回 null（不是所有端点都返回）。
    static AgentTokenUsage? ParseUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return null;
        return new AgentTokenUsage
        {
            PromptTokens = GetInt(usage, "prompt_tokens"),
            CompletionTokens = GetInt(usage, "completion_tokens"),
            TotalTokens = GetInt(usage, "total_tokens"),
        };

        static int GetInt(JsonElement obj, string name)
            => obj.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 0;
    }

    public void Dispose() => mHttp.Dispose();

    readonly System.Net.Http.HttpClient mHttp;
    readonly string mEndpoint;
    readonly string mModel;
    readonly double mTemperature;
    readonly int mMaxTokens;
}
