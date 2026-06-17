using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
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

    // 流式：stream:true + stream_options.include_usage（拿最后一帧 usage）。逐帧解析 delta，文本增量经 onContentDelta 回调，
    // 工具调用分片按 index 累积，结束后拼成完整一轮回复返回（语义与非流式一致，便于 Runner 复用循环）。
    public Task<AgentModelReply> SendAsync(AgentModelRequest request, IProgress<string>? onContentDelta, CancellationToken cancellationToken)
        => SendAsync(request, onContentDelta, null, cancellationToken);

    public async Task<AgentModelReply> SendAsync(AgentModelRequest request, IProgress<string>? onContentDelta, IProgress<string>? onReasoningDelta, CancellationToken cancellationToken)
    {
        var bodyObj = BuildRequestBody(request);
        bodyObj["stream"] = true;
        bodyObj["stream_options"] = new JsonObject { ["include_usage"] = true };
        var body = bodyObj.ToJsonString();

        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, mEndpoint) { Content = content };
        using var response = await mHttp.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new Exception(string.Format("Model request failed ({0}): {1}", (int)response.StatusCode, err));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        return await ParseStream(reader, onContentDelta, onReasoningDelta, cancellationToken).ConfigureAwait(false);
    }

    // SSE：每行 "data: {json}"，"data: [DONE]" 收尾。累积 content/reasoning_content/tool_calls/usage。
    static async Task<AgentModelReply> ParseStream(StreamReader reader, IProgress<string>? onContentDelta, IProgress<string>? onReasoningDelta, CancellationToken cancellationToken)
    {
        var contentSb = new StringBuilder();
        var reasoningSb = new StringBuilder(); // 推理「思考」全文（落盘/重现用；同时其长度判断"是否有产出"）
        var toolAcc = new SortedDictionary<int, ToolCallAcc>();
        AgentTokenUsage? usage = null;
        string? finishReason = null;

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0 || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            var data = line.AsSpan(5).Trim().ToString();
            if (data == "[DONE]")
                break;

            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            // 流中途的错误帧（限流/超长/服务端异常等）：不抛会被当成空内容静默吞掉，须显式暴露。
            if (root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.Object)
            {
                var msg = errEl.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : errEl.ToString();
                // 工具调用失败时部分 provider（如 Groq）把模型实际生成的（不合法）调用放在 failed_generation，
                // 是定位"调用为何被拒"的关键，一并暴露。
                if (errEl.TryGetProperty("failed_generation", out var fg) && fg.ValueKind == JsonValueKind.String)
                    msg += "\n[failed_generation] " + fg.GetString();
                throw new Exception("Model stream error: " + msg);
            }

            if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
                usage = ParseUsageObject(u);

            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                continue;
            if (choices[0].TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
                finishReason = fr.GetString();
            if (!choices[0].TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
                continue;

            if (delta.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
            {
                var piece = c.GetString();
                if (!string.IsNullOrEmpty(piece))
                {
                    contentSb.Append(piece);
                    onContentDelta?.Report(piece);
                }
            }

            // 推理模型的「思考」增量（reasoning_content）：转推理通道单独渲染，不并入正文。
            if (delta.TryGetProperty("reasoning_content", out var rc) && rc.ValueKind == JsonValueKind.String)
            {
                var rpiece = rc.GetString();
                if (!string.IsNullOrEmpty(rpiece))
                {
                    reasoningSb.Append(rpiece);
                    onReasoningDelta?.Report(rpiece);
                }
            }

            if (delta.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in tcs.EnumerateArray())
                {
                    int idx = tc.TryGetProperty("index", out var ie) && ie.ValueKind == JsonValueKind.Number ? ie.GetInt32() : 0;
                    if (!toolAcc.TryGetValue(idx, out var acc))
                        toolAcc[idx] = acc = new ToolCallAcc();
                    // id/name 只在非空时写入：标准 OpenAI 流仅首帧带 id/name、后续帧只追加 arguments，
                    // 但有的 provider（如 deepseek-v4-flash）会在后续帧重复发空串 id/name —— 若无条件覆盖会把
                    // 首帧的真实 name 清成空，导致整条调用在末尾因 Name 为空被丢弃（表现为"模型调了工具却无反应"）。
                    if (tc.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(id.GetString()))
                        acc.Id = id.GetString();
                    if (tc.TryGetProperty("function", out var fn) && fn.ValueKind == JsonValueKind.Object)
                    {
                        if (fn.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(n.GetString()))
                            acc.Name = n.GetString();
                        if (fn.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.String)
                            acc.Arguments.Append(a.GetString());
                    }
                }
            }
        }

        var toolCalls = new List<AgentToolCall>();
        foreach (var acc in toolAcc.Values)
        {
            if (string.IsNullOrEmpty(acc.Name))
                continue;
            toolCalls.Add(new AgentToolCall
            {
                Id = acc.Id ?? string.Empty,
                Name = acc.Name,
                ArgumentsJson = acc.Arguments.Length > 0 ? acc.Arguments.ToString() : "{}",
            });
        }

        // 空内容且无工具调用：把真正原因暴露出来，而不是让 UI 显示笼统的 "(no text reply)"。
        // finish_reason=length → 输出被 max_tokens 截断（常见于 Max Tokens 设太小）；content_filter → 被内容审查拦截。
        if (contentSb.Length == 0 && toolCalls.Count == 0)
        {
            // reasoning_content 非空（推理模型把输出放在思考里、正文为空）属正常——不误报"无内容"，思考块自有呈现。
            if (reasoningSb.Length == 0)
                Log.Warning(string.Format("Agent stream produced no content. finish_reason={0}, hasUsage={1}", finishReason ?? "(none)", usage != null));
            if (finishReason == "length")
                throw new Exception("Model returned no content (finish_reason: length). Output was cut by Max Tokens — raise the Max Tokens setting (0 = no limit).");
            if (finishReason == "content_filter")
                throw new Exception("Model returned no content (finish_reason: content_filter). The request was blocked by the provider's content filter.");
            if (finishReason == "tool_calls")
                throw new Exception("Model requested a tool call but none could be parsed from the stream (provider tool-call format mismatch).");
        }

        return new AgentModelReply
        {
            Content = contentSb.Length > 0 ? contentSb.ToString() : null,
            Reasoning = reasoningSb.Length > 0 ? reasoningSb.ToString() : null,
            ToolCalls = toolCalls,
            Usage = usage,
        };
    }

    // 流式工具调用分片累积器：分片携带 index + 渐进的 id/name/arguments 片段。
    sealed class ToolCallAcc
    {
        public string? Id;
        public string? Name;
        public readonly StringBuilder Arguments = new();
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

        string? reasoning = message.TryGetProperty("reasoning_content", out var reasoningElement) && reasoningElement.ValueKind == JsonValueKind.String
            ? reasoningElement.GetString()
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

        return new AgentModelReply { Content = content, Reasoning = reasoning, ToolCalls = toolCalls, Usage = ParseUsage(doc.RootElement) };
    }

    // OpenAI 协议 usage：{ prompt_tokens, completion_tokens, total_tokens }。缺失则返回 null（不是所有端点都返回）。
    static AgentTokenUsage? ParseUsage(JsonElement root)
        => root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object ? ParseUsageObject(usage) : null;

    static AgentTokenUsage ParseUsageObject(JsonElement usage)
        => new()
        {
            PromptTokens = GetInt(usage, "prompt_tokens"),
            CompletionTokens = GetInt(usage, "completion_tokens"),
            TotalTokens = GetInt(usage, "total_tokens"),
        };

    static int GetInt(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 0;

    public void Dispose() => mHttp.Dispose();

    readonly System.Net.Http.HttpClient mHttp;
    readonly string mEndpoint;
    readonly string mModel;
    readonly double mTemperature;
    readonly int mMaxTokens;
}
