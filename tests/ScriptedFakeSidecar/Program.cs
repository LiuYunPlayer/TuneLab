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
        Console.Error.WriteLine($"fake-up mode={mode} pid={Environment.ProcessId}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Console.Error.WriteLine("fake-unhandled: " + e.ExceptionObject);
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();

        while (TryRead(stdin, out var json))
        {
            var req = JsonNode.Parse(json)!.AsObject();
            var method = req["method"]?.GetValue<string>();

            if (method == "shutdown")
                return 0;

            var id = req["id"]?.DeepClone();
            if (method == "format.import" && mode == "crash-on-import")
                Environment.Exit(3);   // 模拟插件 native 崩溃：不留尾包，直接暴毙

            if (method == "initialize")
            {
                var result = HandleInitialize(req["params"]?.AsObject() ?? new JsonObject(), mode);
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
                default:
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

    static JsonNode HandleInitialize(JsonObject req, string mode)
    {
        // req = initialize 的 params（宿主发 {protocol, package_id, entries[]}）
        var provides = new JsonArray();
        if (mode != "no-provides")
        {
            if (req["entries"] is JsonArray entries)
                foreach (var e in entries)
                    provides.Add((JsonNode)new JsonObject { ["type"] = (string?)e?["type"] ?? "format", ["entry"] = e?["entry"]?.DeepClone() });
        }
        return new JsonObject
        {
            ["provides"] = provides,
            ["capabilities"] = new JsonObject { ["audio_plane"] = "inline", ["curve_plane"] = "inline" },
            ["runtime"] = new JsonObject { ["exe"] = Environment.ProcessPath ?? "fake", ["version"] = "0.0", ["arch"] = "x64", ["flavor"] = "dotnet-fake" },
            ["facade_version"] = "test",
        };
    }

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

    static void Write(Stream output, JsonNode node)
    {
        var body = System.Text.Encoding.UTF8.GetBytes(node.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        var header = System.Text.Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        output.Write(header);
        output.Write(body);
        output.Flush();
    }
}
