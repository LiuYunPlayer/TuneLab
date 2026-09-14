using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.Extensions.Scripted;

// 一个包一个 sidecar 的宿主（设计文档 §3）。懒 spawn（首次 Call 前 EnsureRunning）；
// 死亡 = 读者线程 EOF / 进程退出；重启带退避与熔断；epoch 防尾包。
// M1 消费方 = RemoteFormat；M2/M3 接 effect/voice（引擎注册改常驻后仍复用本类）。
internal sealed class SidecarHost
{
    readonly string mPackageId;
    readonly string mPackageDir;
    readonly string mEntry;               // 包内相对路径
    readonly RuntimeInfo mRuntime;
    readonly int mBackoffSecondsMax = 30;
    readonly TimeSpan mCallTimeout;

    // 测试注入点：每次 spawn 叠加的环境变量（生产不触碰）。
    public Dictionary<string, string?>? ExtraEnvironment { get; set; }

    public SidecarHost(string packageId, string packageDir, RuntimeInfo runtime, string entry,
        TimeSpan? callTimeout = null, TimeSpan? heartbeat = null, TimeSpan? pingTimeout = null)
    {
        mPackageId = packageId; mPackageDir = packageDir; mRuntime = runtime; mEntry = entry;
        mCallTimeout = callTimeout ?? TimeSpan.FromMinutes(2);
        mHeartbeatInterval = heartbeat ?? TimeSpan.FromSeconds(15);
        mPingTimeout = pingTimeout ?? TimeSpan.FromSeconds(5);
    }

    readonly object mLock = new();
    readonly TimeSpan mHeartbeatInterval;
    readonly TimeSpan mPingTimeout;
    readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> mPending = new();
    int mEpoch;
    Process? mProcess;
    Stream? mStdin;
    TaskCompletionSource<bool>? mStartedTcs;   // 握手进行中（单飞）
    DateTime mFirstStartUtc = DateTime.MinValue;
    int mDeathsInWindow;
    bool mCircuitOpen;
    bool mDisposed;
    Timer? mHeartbeat;
    int mHeartbeatMisses;

    public int Epoch { get { lock (mLock) return mEpoch; } }
    public bool CircuitOpen { get { lock (mLock) return mCircuitOpen; } }
    public string StderrTail { get; private set; } = string.Empty;
    internal Exception? LastReaderFault { get; private set; }   // 诊断（测试与失败文案用）

    // 熔断复位（侧栏"重新加载"的语义；测试亦用）。不复活进行中的调用，只放行下一次 spawn。
    public void ResetCircuit()
    {
        lock (mLock)
        {
            mCircuitOpen = false;
            mDeathsInWindow = 0;
            mFirstStartUtc = DateTime.MinValue;
        }
    }

    // sidecar 侧被动事件（宿主拉模式，M1 只消费 log 与 diagnostics）。
    public event Action<string, string>? LogReceived;          // level, message
    public event Action? Died;                                  // 每次死亡（含即将重启）

    // 握手后必须兑现在这些期望（§2.3：manifest 承诺 ↔ sidecar 声明逐条核对，缺席 = 协议违例）。
    // 加载期注册（懒 spawn 之前），线程安全：只在注册后、spawn 前的窗口写。
    sealed class ExpectedFormat
    {
        public string Entry = "";
        public string[] Import = [];
        public string[] Export = [];
        public string? EngineType;                              // effect/voice 引擎期望（非 null 时 Entry 即 engine id）
        public JsonNode? Settings;                              // 持久设置值（spawn 前灌的在这里随握手补发）
    }
    readonly List<ExpectedFormat> mExpectedFormats = [];

    public void ExpectFormat(string entryId, string[] importSuffixes, string[] exportSuffixes)
    {
        lock (mExpectedFormats)
            mExpectedFormats.Add(new ExpectedFormat { Entry = entryId, Import = importSuffixes, Export = exportSuffixes });
    }

    // effect/voice 引擎期望（§2.3 同款核对：provides 必须含 {type, engine}）。
    public void ExpectEngine(string type, string engineId)
    {
        lock (mExpectedFormats)
            mExpectedFormats.Add(new ExpectedFormat { Entry = engineId, Import = [], Export = [], EngineType = type });
    }

    // ── 反向调用（sidecar → 宿主请求）：方法 → 处理器表（§5.5，effect/voice 的地基）。
    // 处理器在 RPC 工作线程上跑，绝不占读者线程；回写经单写锁。
    readonly Dictionary<string, Func<JsonNode?, Task<JsonNode?>>> mLocalCalls = new();
    readonly Dictionary<string, Action<JsonNode?>> mLocalNotifies = new();

    public void RegisterLocalCall(string method, Func<JsonNode?, Task<JsonNode?>> handler)
    {
        lock (mLocalCalls) mLocalCalls[method] = handler;
    }

    public void RegisterLocalNotify(string method, Action<JsonNode?> handler)
    {
        lock (mLocalCalls) mLocalNotifies[method] = handler;
    }

    public bool Running { get { lock (mLock) return mProcess != null && !mDisposed; } }

    // 宿主已解好的设置值（ApplySettings 路径）：未运行则存着、下次握手随 entries[].settings 补发；
    // 运行中则即时通知。密码解密发生在宿主侧——sidecar 拿到的与 .NET 插件同权。
    public void SetSettings(string entryId, JsonNode? values)
    {
        bool notify = false;
        lock (mExpectedFormats)
            foreach (var ef in mExpectedFormats)
                if (ef.Entry == entryId) { ef.Settings = values; notify = Running; break; }
        if (notify)
        {
            try { Notify("settings.apply", new JsonObject { ["entry"] = entryId, ["values"] = values?.DeepClone() }); }
            catch { /* 通知竞态（刚死）：下一次握手补发兜底 */ }
        }
    }

    // 通知（不等应答）。未运行即抛（调用方按需吞——SetSettings 已包）。
    public void Notify(string method, JsonNode? args)
    {
        Stream stdin;
        lock (mLock)
        {
            if (mProcess == null || mStdin == null || mDisposed)
                throw new SidecarRpcException("runtime_error", "sidecar not running");
            stdin = mStdin;
        }
        lock (mLock)
        {
            if (mStdin != stdin) throw new SidecarRpcException("runtime_error", "sidecar restarted");
            WireFraming.Write(stdin, new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = args }.ToJsonString());
        }
    }

    void SendRaw(JsonObject message)
    {
        try
        {
            lock (mLock)
            {
                if (mProcess == null || mStdin == null || mDisposed) return;   // 半死途中丢应答：sidecar 端由超时/EOF 收敛
                WireFraming.Write(mStdin, message.ToJsonString());
            }
        }
        catch { }
    }

    void SendResult(JsonNode? id, JsonNode? result)
        => SendRaw(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result });

    void SendError(JsonNode? id, string code, string message)
        => SendRaw(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } });

    // 有界同步调用（声明面 §3.5 的宿主侧原语）：进程没起就不等——后台拉起、当前问立即落空
    // （调用方拿缓存/空值）；进程活着则最多等 timeout（数据线程上的阻塞有上界）。
    public bool TryCallRunning(string method, JsonNode? args, TimeSpan timeout, out JsonNode? result, out string? error)
    {
        result = null;
        error = null;
        int epoch;
        lock (mLock)
        {
            if (mDisposed || mProcess == null)
            {
                var _ = EnsureRunningAsync();   // fire-and-forget：下一次问就有真值
                error = "not running";
                return false;
            }
            epoch = mEpoch;
        }
        try
        {
            result = CallInternal(method, args, timeout, epoch);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public async Task EnsureRunningAsync()
    {
        TaskCompletionSource<bool> start;
        lock (mLock)
        {
            if (mDisposed)
                throw new ObjectDisposedException(nameof(SidecarHost));
            if (mCircuitOpen)
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "sidecar for {0} is stopped after repeated crashes", mPackageId));
            if (mProcess != null)
                return;
            if (mStartedTcs == null)
            {
                mStartedTcs = start = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                ThreadPool.QueueUserWorkItem(_ => StartOnce(start));
            }
            else
                start = mStartedTcs;
        }
        await start.Task.ConfigureAwait(false);
    }

    void StartOnce(TaskCompletionSource<bool> tcs)
    {
        try
        {
            lock (mLock)
            {
                if (mProcess != null) { tcs.TrySetResult(true); return; }   // 单飞竞态兜底
            }
            StartAndHandshake();
            lock (mLock)
            {
                if (mFirstStartUtc == DateTime.MinValue) mFirstStartUtc = DateTime.UtcNow;
                mStartedTcs = null;
            }
            tcs.TrySetResult(true);
        }
        catch (Exception ex)
        {
            lock (mLock) mStartedTcs = null;
            tcs.TrySetException(ex);
        }
    }

    void StartAndHandshake()
    {
        var entryPath = Path.GetFullPath(Path.Combine(mPackageDir, mEntry.Replace('/', Path.DirectorySeparatorChar)));
        var psi = new ProcessStartInfo(mRuntime.Exe)
        {
            WorkingDirectory = mPackageDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in FlavorArgs(mRuntime.Flavor))
            psi.ArgumentList.Add(a);
        psi.ArgumentList.Add(entryPath);
        psi.Environment["TUNELAB_PROTOCOL"] = WireProtocol.Version.ToString(CultureInfo.InvariantCulture);
        psi.Environment["TUNELAB_EPOCH"] = (mEpoch + 1).ToString(CultureInfo.InvariantCulture);
        psi.Environment["TUNELAB_PACKAGE_ID"] = mPackageId;
        if (ExtraEnvironment != null)
            foreach (var (k, v) in ExtraEnvironment)
            {
                if (v == null) psi.Environment.Remove(k);
                else psi.Environment[k] = v;
            }

        var proc = Process.Start(psi) ?? throw new InvalidOperationException("failed to start sidecar process");
        int epoch;
        lock (mLock)
        {
            mEpoch++; epoch = mEpoch;
            mProcess = proc;
            mStdin = proc.StandardInput.BaseStream;
        }
        ScriptedRuntimeLedger.Record(mPackageId, proc.Id, mRuntime.Exe);
        if (OperatingSystem.IsWindows())
            JobObjects.AssignToKillOnHostClose(proc);

        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) AppendStderr(e.Data); };
        try { proc.BeginErrorReadLine(); } catch { }

        var reader = new Thread(() => ReadLoop(proc, epoch)) { IsBackground = true, Name = "sidecar-reader-" + mPackageId };
        reader.Start();

        // 握手（同步在本线程：EnsureRunning 已在池线程上）。失败即杀半死进程并计一次死亡。
        // params.entries 携带宿主期望（format 键 = manifest 的相对路径；engine 键 = engine id）——
        // sidecar 按同一键兑现 provides。
        var handshake = new JsonObject
        {
            ["protocol"] = WireProtocol.Version,
            ["package_id"] = mPackageId,
        };
        var entriesNode = new JsonArray();
        lock (mExpectedFormats)
            foreach (var ef in mExpectedFormats)
            {
                var e = new JsonObject { ["type"] = ef.EngineType ?? "format" };
                if (ef.EngineType == null)
                {
                    e["entry"] = ef.Entry;
                    e["import_suffixes"] = JsonSerializer.SerializeToNode(ef.Import);
                    e["export_suffixes"] = JsonSerializer.SerializeToNode(ef.Export);
                }
                else
                    e["engine"] = ef.Entry;
                if (ef.Settings != null)
                    e["settings"] = ef.Settings.DeepClone();    // 握手补发（spawn 前灌过的持久值）
                entriesNode.Add((JsonNode)e);
            }
        handshake["entries"] = entriesNode;
        try
        {
            var reply = CallInternal("initialize", handshake, TimeSpan.FromSeconds(30), mEpoch);
            if (reply == null || reply["capabilities"] is not JsonObject)
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "sidecar for {0} completed handshake without capabilities", mPackageId));
            ValidateHandshake(reply);
            StartHeartbeat(mEpoch);
        }
        catch (Exception ex)
        {
            // 真因先落 started tcs（Kill 会触发读者线程 OnDeath 的泛化"exited"消息——first-set-wins，
            // 不抢先就是把 "does not declare entry" 说成 "exited during startup"）。
            TaskCompletionSource<bool>? st;
            lock (mLock)
            {
                st = mStartedTcs;
                if (mProcess == proc) { mProcess = null; mStdin = null; }
            }
            st?.TrySetException(ex);
            try { proc.Kill(true); } catch { }
            throw;
        }
    }

    static IEnumerable<string> FlavorArgs(string flavor) => flavor switch
    {
        "python" => ["-u"],
        "node" => ["--disable-warning=ExperimentalWarning"],
        "dotnet-fake" => ["exec"],      // 协议封条测试执行体：dotnet exec <dll>（设计 §12.1，CI 免装解释器）
        _ => [],
    };

    // 心跳（防线 3）：ping 一次带 5s 超时；连续 4 次失联（≈ interval×4+超时 已够判"半死"）杀树。
    // 心跳只用于识别挂死的 sidecar——宿主自己挂起不影响这里（Timer 停了也只是少 ping）。
    void StartHeartbeat(int epoch)
    {
        StopHeartbeat();
        mHeartbeatMisses = 0;
        var timer = new Timer(_ =>
        {
            lock (mLock)
            {
                if (mDisposed || epoch != mEpoch || mProcess == null) return;
            }
            _ = Task.Run(async () =>
            {
                try { await CallInternalAsync("ping", new JsonObject { ["epoch"] = epoch }, mPingTimeout, epoch).ConfigureAwait(false); Interlocked.Exchange(ref mHeartbeatMisses, 0); }
                catch (SidecarRpcException ex) when (ex.Code == "runtime_error") { /* 进程已死：OnDeath 接管 */ }
                catch
                {
                    if (Interlocked.Increment(ref mHeartbeatMisses) >= 4)
                    {
                        LastReaderFault ??= new TimeoutException("heartbeat: 4 consecutive ping failures (sidecar wedged?)");
                        int pid;
                        lock (mLock) { pid = epoch == mEpoch ? mProcess?.Id ?? -1 : -1; }
                        if (pid > 0) KillTree(pid);
                    }
                }
            });
        }, null, mHeartbeatInterval, mHeartbeatInterval);
        mHeartbeat = timer;
    }

    void StopHeartbeat()
    {
        mHeartbeat?.Dispose();
        mHeartbeat = null;
    }

    Task<JsonNode?> CallInternalAsync(string method, JsonNode args, TimeSpan timeout, int epoch)
        => Task.Run(() => CallInternal(method, args, timeout, epoch));

    // 握手后核对 provides（§2.3）：manifest 承诺的每个脚本条目，sidecar 必须自报支持，否则握手期
    // 就判死（"作者写了、sidecar 不认"是最容易静默失联的一类不匹配，拦在最前）。
    void ValidateHandshake(JsonNode? reply)
    {
        var provided = new HashSet<string>(StringComparer.Ordinal);
        if (reply!["provides"] is JsonArray arr)
            foreach (var item in arr)
                if (item is JsonObject o && o["entry"]?.GetValue<string>() is { } e)
                    provided.Add(e);
                else if (item is JsonObject eo && eo["engine"]?.GetValue<string>() is { } eng)
                    provided.Add("engine:" + eng);
        lock (mExpectedFormats)
        {
            foreach (var ef in mExpectedFormats)
            {
                var key = ef.EngineType != null ? "engine:" + ef.Entry : ef.Entry;
                if (!provided.Contains(key))
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                        "sidecar for {0} handshake does not declare {1} '{2}' that the manifest promised", mPackageId, ef.EngineType ?? "entry", ef.Entry));
            }
        }
    }

    void AppendStderr(string line)
    {
        const int Cap = 64 * 1024;
        var tail = StderrTail + line + "\n";
        if (tail.Length > Cap)
            tail = tail.Substring(tail.Length - Cap);
        StderrTail = tail;
        // 滚动落盘（§3.4）：包目录 logs/stderr-<boot>.txt，尽力而为（只读目录等失败不致命）。
        try
        {
            var dir = Path.Combine(mPackageDir, "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "stderr-" + SidecarIo.Nonce + ".txt"), line + "\n");
        }
        catch { }
    }

    void ReadLoop(Process proc, int epoch)
    {
        string? json = null;
        try
        {
            while (WireFraming.TryRead(proc.StandardOutput.BaseStream, out json))
            {
                try { Dispatch(json, epoch); }
                catch (Exception ex) { LastReaderFault = ex; break; }
            }
        }
        catch (Exception ex)
        {
            LastReaderFault = new InvalidOperationException("frame read failed near: " + (json == null ? "<none>" : json[..Math.Min(160, json.Length)]), ex);
        }
        try { proc.WaitForExit(2000); } catch { }
        int exitCode = -1;
        try { if (proc.HasExited) exitCode = proc.ExitCode; } catch { }
        OnDeath(epoch, exitCode);
        try { proc.Dispose(); } catch { }
    }

    void Dispatch(string json, int epoch)
    {
        var node = JsonNode.Parse(json)?.AsObject();
        if (node == null) return;
        if (node.TryGetPropertyValue("method", out var m) && m != null)
        {
            var method = m.GetValue<string>();
            if (method == "log.write")
            {
                var p = node["params"]?.AsObject();
                LogReceived?.Invoke(p?["level"]?.GetValue<string>() ?? "info", p?["message"]?.GetValue<string>() ?? "");
                return;
            }
            var inId = node["id"];
            var args = node["params"];
            if (inId != null)
            {
                // 反向请求（sidecar→宿主）：必须在池线程完成——读者线程一被处理器卡住，
                // sidecar 等反向应答 ↔ 宿主等 sidecar 的在途请求应答，锁环即成。
                Func<JsonNode?, Task<JsonNode?>>? handler;
                lock (mLocalCalls) mLocalCalls.TryGetValue(method, out handler);
                if (handler == null)
                {
                    SendError(inId, "bad_request", "host has no handler for '" + method + "'");
                    return;
                }
                var id = inId.DeepClone();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var result = await handler(args).ConfigureAwait(false);
                        if (epoch == mEpoch)
                            SendResult(id, result);
                    }
                    catch (SidecarRpcException ex)
                    {
                        if (epoch == mEpoch) SendError(id, ex.Code, ex.Message);
                    }
                    catch (Exception ex)
                    {
                        if (epoch == mEpoch) SendError(id, "runtime_error", ex.Message);
                    }
                });
                return;
            }
            // 无 id 通知：本地表优先，未注册者忽略（前向兼容）。
            Action<JsonNode?>? notifyHandler;
            lock (mLocalCalls) mLocalNotifies.TryGetValue(method, out notifyHandler);
            notifyHandler?.Invoke(args);
            return;
        }
        if (node.TryGetPropertyValue("id", out var idNode) && idNode != null)
        {
            // 键与发出时一致：字符串 id 取裸值（ToJsonString 会带引号 → 永不命中 pending）。
            var key = idNode.GetValueKind() == System.Text.Json.JsonValueKind.String ? idNode.GetValue<string>() : idNode.ToJsonString();
            if (mPending.TryRemove(key, out var tcs))
            {
                if (node["error"] is JsonObject err)
                    tcs.TrySetException(new SidecarRpcException(err["code"]?.ToString() ?? "runtime_error", err["message"]?.ToString() ?? ""));
                else
                    tcs.TrySetResult(node["result"]);
            }
        }
    }

    void OnDeath(int epoch, int exitCode)
    {
        TaskCompletionSource<bool>? started;
        bool fatal;
        int backoffSeconds;
        lock (mLock)
        {
            if (epoch != mEpoch || mDisposed)   // 旧世代的读者线程：尾包/尾死信号，忽略
                return;
            StopHeartbeat();
            foreach (var kv in mPending)
                if (mPending.TryRemove(kv.Key, out var tcs))
                    tcs.TrySetException(new SidecarRpcException("runtime_error", string.Format(CultureInfo.InvariantCulture, "sidecar exited (code {0})", exitCode)));
            mProcess = null; mStdin = null;
            started = mStartedTcs;

            var now = DateTime.UtcNow;
            if ((now - mFirstStartUtc) > TimeSpan.FromMinutes(10))
            { mFirstStartUtc = now; mDeathsInWindow = 0; }
            mDeathsInWindow++;
            fatal = mDeathsInWindow >= 5;
            if (fatal) mCircuitOpen = true;
            backoffSeconds = fatal ? 0 : Math.Min(mBackoffSecondsMax, 1 << Math.Min(5, mDeathsInWindow));   // 2/4/8/16/30s
        }
        started?.TrySetException(new SidecarRpcException("runtime_error", string.Format(CultureInfo.InvariantCulture, "sidecar exited during startup (code {0})", exitCode)));
        Died?.Invoke();
        if (!fatal)
        {
            // 不自动复活进行中的调用（M1 语义）：失败如实抛给调用方；下一次调用触发 EnsureRunning 重开。
            // 熔断窗口计数只随时间复位——下次调用若在窗口内仍死，很快 Open。
        }
    }

    public Task<JsonNode?> CallAsync(string method, JsonNode? args) => CallAsync(method, args, mCallTimeout);

    // timeout = Timeout.InfiniteTimeSpan：等到完成或死亡（engine.init 类长任务；死亡通道兜底）。
    public async Task<JsonNode?> CallAsync(string method, JsonNode? args, TimeSpan timeout)
    {
        await EnsureRunningAsync().ConfigureAwait(false);
        int epoch;
        lock (mLock) epoch = mEpoch;
        return await Task.Run(() => CallInternal(method, args, timeout, epoch)).ConfigureAwait(false);
    }

    JsonNode? CallInternal(string method, JsonNode? args, TimeSpan timeout, int epoch)
    {
        Stream stdin;
        lock (mLock)
        {
            if (mProcess == null || mStdin == null || epoch != mEpoch)
                throw new SidecarRpcException("runtime_error", "sidecar not running");
            stdin = mStdin;
        }
        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        mPending[id] = tcs;
        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse("\"" + id + "\""),
            ["method"] = method,
            ["params"] = args?.DeepClone(),
        };
        try
        {
            lock (mLock)
            {
                if (mStdin != stdin) throw new SidecarRpcException("runtime_error", "sidecar restarted mid-call");
                WireFraming.Write(stdin, request.ToJsonString());
            }
            if (!tcs.Task.Wait(timeout))
            {
                mPending.TryRemove(id, out _);
                throw new SidecarRpcException("timeout", string.Format(CultureInfo.InvariantCulture, "'{0}' timed out after {1}s", method, timeout.TotalSeconds));
            }
            return tcs.Task.Result;
        }
        catch
        {
            mPending.TryRemove(id, out _);
            throw;
        }
    }

    public void Shutdown()
    {
        Process? proc;
        Stream? stdin;
        lock (mLock)
        {
            mDisposed = true;
            proc = mProcess; stdin = mStdin;
        }
        if (stdin != null)
        {
            try
            {
                WireFraming.Write(stdin, new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "shutdown", ["params"] = new JsonObject { ["reason"] = "host-stop" } }.ToJsonString());
            }
            catch { }
        }
        int pid = -1;
        if (proc != null)
        {
            try { pid = proc.Id; } catch { }
            try { if (!proc.WaitForExit(3000) && pid > 0) KillTree(pid); } catch { }
        }
        if (pid > 0)
            ScriptedRuntimeLedger.Complete(mPackageId, pid);
    }

    static void KillTree(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(true);
        }
        catch { }
    }
}

internal sealed class SidecarRpcException(string code, string message) : Exception(code + ": " + message)
{
    public string Code { get; } = code;
}

// Windows 防遗留第 2 防线（设计文档 §3.3）：所有 sidecar 挂进同一个 kill-on-close 的 Job Object，
// 宿主进程无论怎么死（含 Kill），OS 关作业句柄 → 作业内全部进程连坐。子进程继承作业 ⇒ 整棵树被罩住。
internal static class JobObjects
{
    static readonly Lazy<IntPtr> sJob = new(() =>
    {
        var h = CreateJobObjectW(IntPtr.Zero, null);
        if (h == IntPtr.Zero) return IntPtr.Zero;
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION { BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION { LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE } };
        int size = System.Runtime.InteropServices.Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        if (!SetInformationJobObject(h, JobObjectExtendedLimitInformation, ref info, (uint)size))
        { }   // 设置失败仍保留作业：至少 Assign 还能生效或静默失败，都不致命
        return h;
    });

    public static void AssignToKillOnHostClose(Process process)
    {
        var h = sJob.Value;
        if (h == IntPtr.Zero) return;
        _ = AssignProcessToJobObject(h, process.Handle);
    }

    const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    const int JobObjectExtendedLimitInformation = 9;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public long Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct IO_COUNTERS
    {
        public ulong ReadOperationCount; public ulong WriteOperationCount; public ulong OtherOperationCount;
        public ulong ReadTransferCount; public ulong WriteTransferCount; public ulong OtherTransferCount;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateJobObjectW(IntPtr jobAttributes, string? name);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(IntPtr job, int infoType, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, uint infoSize);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
}
