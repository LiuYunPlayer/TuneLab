using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;

// 假 sidecar：与宿主 SidecarHost 说同一套 LSP 帧 + JSON-RPC（wire v1）。
// 供 M1 协议封条 / 生命周期测试用（CI 无需任何真解释器）。行为由 TUNELAB_FAKE_MODE 控制：
//   normal        正常应答 initialize / format.import / format.export / log.write / shutdown
//   no-capability initialize 应答缺 capabilities → 宿主应判握手失败
//   no-provides   initialize 应答不兑现在 entries 里承诺的条目 → 宿主 provides 核对应判死
//   hang          握手后收到调用即挂起不答 → 宿主 RPC 超时
//   echo-log      format.import 前发一条 log.write 通知
// 数据面用固定合成工程（不依赖真解析），把 stream_ref 原样回读以验证文件交接往返。
internal static class Program
{
    static int Main()
    {
        var mode = Environment.GetEnvironmentVariable("TUNELAB_FAKE_MODE") ?? "normal";
        var settingsLog = Environment.GetEnvironmentVariable("TUNELAB_FAKE_SETTINGS_LOG");
        Console.Error.WriteLine($"fake-up mode={mode} pid={Environment.ProcessId}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Console.Error.WriteLine("fake-unhandled: " + e.ExceptionObject);
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();

        void LogSettings(string origin, JsonNode? values)
        {
            if (settingsLog != null)
                File.AppendAllText(settingsLog, $"{origin}\t{values?.ToJsonString() ?? "<null>"}\n");
        }

        int reverseId = 0;
        // 反向请求（fake→宿主）：写请求帧后同步读应答帧——宿主读者线程把处理甩到池线程，不会锁环。
        JsonNode? Reverse(string method, JsonObject args)
        {
            var rid = "rev-" + ++reverseId;
            Write(stdout, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = rid, ["method"] = method, ["params"] = args });
            while (TryRead(stdin, out var respJson))
            {
                var resp = JsonNode.Parse(respJson)!.AsObject();
                if (resp["method"]?.GetValue<string>() == "ping")
                {
                    Write(stdout, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = resp["id"]?.DeepClone(), ["result"] = new JsonObject() });
                    continue;                                   // 反向途中回心跳
                }
                if (resp["method"] != null)
                    continue;                                   // 跳过其他通知（不应有）
                if (resp["id"]?.ToJsonString() == "\"" + rid + "\"")
                {
                    if (resp["error"] != null)
                        throw new InvalidOperationException("reverse " + method + " failed: " + resp["error"]!.ToJsonString());
                    return resp["result"];
                }
            }
            Environment.Exit(4);                                // 宿主管道关了：随它去
            return null;
        }

        void Notify(string method, JsonObject args)
            => Write(stdout, new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = args });

        while (TryRead(stdin, out var json))
        {
            var req = JsonNode.Parse(json)!.AsObject();
            var method = req["method"]?.GetValue<string>();

            if (method == "shutdown")
                return 0;

            var id = req["id"]?.DeepClone();
            if (method == "format.import" && mode == "crash-on-import")
                Environment.Exit(3);   // 模拟插件 native 崩溃：不留尾包，直接暴毙
            if (method == "ping" && mode == "mute-ping")
                continue;              // 心跳装死：只吞 ping，其余照常（测宿主 4 连失联杀树）
            if (id == null && method == "settings.apply")
            {
                LogSettings("notify", req["params"]?["values"]);
                continue;
            }

            if (method == "initialize")
            {
                var result = HandleInitialize(req["params"]?.AsObject() ?? new JsonObject(), mode, LogSettings);
                if (mode == "no-capability")
                    result.AsObject().Remove("capabilities");
                Write(stdout, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
                continue;
            }

            if (mode == "hang")
                continue;   // 不回任何后续调用

            var p = req["params"]?.AsObject();
            switch (method)
            {
                case "log.write":
                    break;
                case "ping":
                    Write(stdout, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = new JsonObject() });
                    break;
                case "settings.get_config":
                    Write(stdout, new JsonObject
                    {
                        ["jsonrpc"] = "2.0", ["id"] = id,
                        ["result"] = new JsonObject { ["config"] = WidgetConfig() },
                    });
                    break;
                case "settings.apply":
                    LogSettings("request", p?["values"]);
                    Write(stdout, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = new JsonObject() });
                    break;
                case "format.import":
                {
                    if (mode == "echo-log")
                        Write(stdout, new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "log.write", ["params"] = new JsonObject { ["level"] = "info", ["message"] = "fake import" } });
                    Write(stdout, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = new JsonObject { ["project"] = SyntheticProject() } });
                    break;
                }
                case "format.export":
                {
                    var streamRef = p?["stream_ref"]?.GetValue<string>();
                    if (streamRef != null)
                        File.WriteAllText(streamRef, "FAKE-EXPORT");
                    Write(stdout, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = new JsonObject() });
                    break;
                }
                case "engine.destroy":
                case "session.dispose":
                    if (id != null)
                        Write(stdout, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = new JsonObject() });
                    break;
                case "engine.init":
                case "engine.create_session":
                    Write(stdout, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = new JsonObject() });
                    break;
                case "effect.get_property_config":
                    Write(stdout, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = new JsonObject { ["config"] = GainConfig() } });
                    break;
                case "effect.get_automation_configs":
                    Write(stdout, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id,
                        ["result"] = new JsonObject { ["tracks"] = new JsonArray
                        {
                            new JsonObject { ["id"] = "intensity", ["displayText"] = "Intensity", ["config"] = new JsonObject
                                { ["type"] = "automation", ["minValue"] = 0.0, ["maxValue"] = 1.0, ["defaultValue"] = 0.5 }, },
                        } } });
                    break;
                case "effect.get_synthesized_parameter_configs":
                    Write(stdout, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id,
                        ["result"] = new JsonObject { ["tracks"] = new JsonArray
                        {
                            new JsonObject { ["id"] = "loudness", ["config"] = new JsonObject
                                { ["type"] = "automation", ["minValue"] = 0.0, ["maxValue"] = 2.0, ["defaultValue"] = "NaN" }, },
                        } } });
                    break;
                case "effect.process":
                {
                    if (mode != "effect-gain")
                        break;
                    var engine = p!["engine"]!.GetValue<string>();
                    var session = p["session"]!.GetValue<string>();
                    var input = p["input"]!.AsObject();
                    long baseOffset = input["sample_offset"]!.GetValue<long>();
                    int count = input["sample_count"]!.GetValue<int>();
                    int rate = input["sample_rate"]!.GetValue<int>();
                    JsonObject Via(JsonObject extra)
                    {
                        extra["engine"] = engine;
                        extra["session"] = session;
                        return extra;
                    }

                    Notify("effect.status", Via(new JsonObject { ["segments"] = new JsonArray { new JsonObject
                        { ["start"] = (double)baseOffset / rate, ["end"] = (double)(baseOffset + count) / rate, ["status"] = "synthesizing", ["progress"] = 0.0 } } }));
                    var samples = new float[count];
                    for (int pos = 0; pos < count;)
                    {
                        int take = Math.Min(count - pos, 1 << 20);
                        var r = Reverse("host.input_read", Via(new JsonObject { ["offset"] = pos, ["count"] = take }));
                        var bytes = Convert.FromBase64String(r!["data"]!.GetValue<string>());
                        for (int i = 0; i < take; i++)
                            samples[pos + i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4));
                        pos += take;
                    }
                    var pr = Reverse("host.properties", Via(new JsonObject { ["keys"] = new JsonArray { "amount" } }));
                    double amount = pr!["values"]!.AsObject()["amount"] is { } av && av["n"] != null ? av["n"]!.GetValue<double>() : 1.0;
                    if (engine == "FakeEval")
                    {
                        // 反向曲线求值：两个时刻的 f64-LE positions → 断言形如 {"values": b64}
                        var pos = new byte[16];
                        BinaryPrimitives.WriteDoubleLittleEndian(pos.AsSpan(0, 8), (double)baseOffset / rate);
                        BinaryPrimitives.WriteDoubleLittleEndian(pos.AsSpan(8, 8), (double)(baseOffset + count) / rate);
                        var ev = Reverse("host.evaluate", Via(new JsonObject { ["track"] = "intensity", ["positions"] = Convert.ToBase64String(pos) }));
                        var first = BinaryPrimitives.ReadDoubleLittleEndian(Convert.FromBase64String(ev!["values"]!.GetValue<string>()).AsSpan(0, 8));
                        if (first != 0.25)
                            throw new InvalidOperationException("evaluate returned " + first);
                    }
                    var segId = Reverse("host.segment_create", Via(new JsonObject
                    {
                        ["sample_offset"] = baseOffset, ["sample_count"] = count, ["sample_rate"] = rate,
                    }))!["segment"]!.GetValue<string>();
                    for (int pos = 0; pos < count;)
                    {
                        int take = Math.Min(count - pos, 1 << 20);
                        var bytes = new byte[take * 4];
                        for (int i = 0; i < take; i++)
                            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), samples[pos + i] * (float)amount);
                        Reverse("host.segment_write", Via(new JsonObject { ["segment"] = segId, ["offset"] = pos, ["data"] = Convert.ToBase64String(bytes) }));
                        pos += take;
                    }
                    Reverse("host.segment_commit", Via(new JsonObject { ["segment"] = segId }));
                    Notify("effect.parameters", Via(new JsonObject { ["parameters"] = new JsonObject { ["loudness"] = new JsonObject
                    {
                        ["segments"] = new JsonArray { new JsonArray
                        {
                            new JsonArray { (double)baseOffset / rate, 0.9 },
                            new JsonArray { (double)(baseOffset + count) / rate, 0.9 },
                        } },
                    } } }));
                    Notify("effect.status", Via(new JsonObject { ["segments"] = new JsonArray { new JsonObject
                        { ["start"] = (double)baseOffset / rate, ["end"] = (double)(baseOffset + count) / rate, ["status"] = "synthesized", ["progress"] = 1.0 } } }));
                    Write(stdout, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = new JsonObject() });
                    break;
                }
                default:
                    if (id == null)
                        break;   // 未知通知（effect.cancel 等）：吞掉，不发 id:null 的非法应答
                    Write(stdout, new JsonObject
                    {
                        ["jsonrpc"] = "2.0", ["id"] = id,
                        ["error"] = new JsonObject { ["code"] = "bad_request", ["message"] = "unknown method " + method },
                    });
                    break;
            }
        }
        return 0;   // stdin EOF（宿主死）→ 自卫退出（防线 1 的另一半：真 façade 也照此）
    }

    static JsonNode HandleInitialize(JsonObject req, string mode, Action<string, JsonNode?> logSettings)
    {
        // req = initialize 的 params（宿主发 {protocol, package_id, entries[]}）
        var provides = new JsonArray();
        if (mode != "no-provides")
        {
            if (req["entries"] is JsonArray entries)
                foreach (var e in entries)
                {
                    var hasSettings = e?["settings"] != null;
                    if (hasSettings)
                        logSettings("handshake", e!["settings"]);
                    var type = (string?)e?["type"] ?? "format";
                    var prov = new JsonObject { ["type"] = type, ["settings"] = hasSettings };
                    if (type == "format")
                        prov["entry"] = e?["entry"]?.DeepClone();
                    else
                        prov["engine"] = e?["engine"]?.DeepClone();   // effect/voice 引擎期望按 engine id 兑现
                    provides.Add((JsonNode)prov);
                }
        }
        return new JsonObject
        {
            ["provides"] = provides,
            ["capabilities"] = new JsonObject { ["audio_plane"] = "inline", ["curve_plane"] = "inline" },
            ["runtime"] = new JsonObject { ["exe"] = Environment.ProcessPath ?? "fake", ["version"] = "0.0", ["arch"] = "x64", ["flavor"] = "dotnet-fake" },
            ["facade_version"] = "test",
        };
    }

    // effect 声明面 fixture：一个 amount 滑条（codec 与宿主 RemoteEffectEngine 双向钉）。
    static JsonNode GainConfig() => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonArray
        {
            new JsonObject { ["id"] = "amount", ["displayText"] = "Amount", ["config"] = new JsonObject
                { ["type"] = "slider", ["defaultValue"] = 1.0, ["scale"] = new JsonObject { ["kind"] = "linear", ["min"] = 0.0, ["max"] = 2.0 } } },
        },
    };

    // 一个尽量占满 config wire 各 type 的 ObjectConfig（codec 封条的 fixture 源）。
    static JsonNode WidgetConfig() => new JsonObject
    {
        ["type"] = "object",
        ["properties"] = new JsonArray
        {
            new JsonObject { ["id"] = "model_dir", ["displayText"] = "模型目录", ["config"] = new JsonObject
                { ["type"] = "path", ["defaultValue"] = "", ["target"] = "folder" } },
            new JsonObject { ["id"] = "gain", ["config"] = new JsonObject
                { ["type"] = "slider", ["defaultValue"] = 0.0, ["scale"] = new JsonObject { ["kind"] = "linear", ["min"] = -12.0, ["max"] = 12.0 },
                  ["format"] = new JsonObject { ["kind"] = "decimals", ["digits"] = 1 }, ["randomizable"] = true,
                  ["minLabel"] = "Soft", ["maxLabel"] = "Hard" } },
            new JsonObject { ["id"] = "seed_mode", ["config"] = new JsonObject
                { ["type"] = "combo", ["items"] = new JsonArray
                    { new JsonObject { ["value"] = new JsonObject { ["s"] = "fast" }, ["displayText"] = "Fast" },
                      new JsonObject { ["separator"] = true, ["displayText"] = "advanced" },
                      new JsonObject { ["value"] = new JsonObject { ["s"] = "full" } } },
                  ["default"] = new JsonObject { ["s"] = "fast" } } },
            new JsonObject { ["id"] = "denoise", ["config"] = new JsonObject { ["type"] = "checkbox", ["defaultValue"] = true } },
            new JsonObject { ["id"] = "api_key", ["config"] = new JsonObject { ["type"] = "textbox", ["defaultValue"] = "", ["password"] = true } },
            new JsonObject { ["id"] = "note", ["config"] = new JsonObject { ["type"] = "textbox", ["defaultValue"] = "", ["multiline"] = true, ["maxVisibleLines"] = 4 } },
            new JsonObject { ["id"] = "steps", ["config"] = new JsonObject
                { ["type"] = "number", ["defaultValue"] = 4.0, ["min"] = 1.0, ["max"] = 10.0, ["step"] = 1.0 } },
        },
    };

    // 与宿主 wire JSON 规范同形的合成工程：一个 track、一个 midi part、一个 note（带 properties，
    // 钉住 PropertyObject / Map / 具名浮点的往返）。数值用 1e18 之类以避开 camelCase 大小写敏感键。
    static JsonNode SyntheticProject() => new JsonObject
    {
        ["tempos"] = new JsonArray { new JsonObject { ["pos"] = 0, ["bpm"] = 120 } },
        ["timeSignatures"] = new JsonArray { new JsonObject { ["barIndex"] = 0, ["numerator"] = 4, ["denominator"] = 4 } },
        ["tracks"] = new JsonArray
        {
            new JsonObject
            {
                ["name"] = "Fake Track",
                ["gain"] = 0.0,
                ["pan"] = 0.0,
                ["mute"] = false,
                ["solo"] = false,
                ["asRefer"] = true,
                ["color"] = "#RRGGBB",
                ["parts"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["kind"] = "midi",
                        ["name"] = "Part",
                        ["pos"] = 960,
                        ["startOffset"] = 0,
                        ["endOffset"] = 0,
                        ["gain"] = 0.0,
                        ["soundSource"] = new JsonObject { ["kind"] = "voice", ["type"] = "fake", ["id"] = "bank" },
                        ["effects"] = new JsonArray(),
                        ["notes"] = new JsonArray
                        {
                            new JsonObject { ["pos"] = 960, ["dur"] = 480, ["pitch"] = 60, ["lyric"] = "la", ["pronunciation"] = "la",
                                ["properties"] = new JsonObject { ["tension"] = new JsonObject { ["n"] = 0.5 }, ["mode"] = new JsonObject { ["s"] = "normal" } },
                                ["leadingPhonemes"] = new JsonArray(), ["bodyPhonemes"] = new JsonArray(), ["bodyOffset"] = 0.0 },
                        },
                        ["automations"] = new JsonObject(),
                        ["piecewiseAutomations"] = new JsonObject(),
                        ["pitch"] = new JsonObject { ["segments"] = new JsonArray() },
                        ["vibratos"] = new JsonArray(),
                        ["properties"] = new JsonObject(),
                    },
                },
            },
        },
    };

    const long MaxFrame = 512L * 1024 * 1024;

    static bool TryRead(Stream input, out string json)
    {
        json = "";
        long length = -1;
        while (true)
        {
            var line = ReadLine(input);
            if (line == null) return false;
            if (line.Length == 0) break;
            var idx = line.IndexOf(':');
            if (idx > 0 && line[..idx].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(line[(idx + 1)..].Trim(), out var n) && n >= 0)
                length = n;
        }
        if (length < 0 || length > MaxFrame) return false;
        var body = new byte[length];
        int done = 0;
        while (done < length)
        {
            int r = input.Read(body, done, (int)length - done);
            if (r <= 0) return false;
            done += r;
        }
        json = System.Text.Encoding.UTF8.GetString(body);
        return true;
    }

    static string? ReadLine(Stream s)
    {
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            int c = s.ReadByte();
            if (c < 0) return sb.Length == 0 ? null : Trim(sb);
            if (c == '\n') return Trim(sb);
            sb.Append((char)c);
        }
        static string Trim(System.Text.StringBuilder b)
            => b.Length > 0 && b[^1] == '\r' ? b.ToString(0, b.Length - 1) : b.ToString();
    }

    static readonly JsonSerializerOptions WriteOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    static void Write(Stream output, JsonNode node)
    {
        var body = System.Text.Encoding.UTF8.GetBytes(node.ToJsonString(WriteOptions));
        var header = System.Text.Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        output.Write(header);
        output.Write(body);
        output.Flush();
    }
}
