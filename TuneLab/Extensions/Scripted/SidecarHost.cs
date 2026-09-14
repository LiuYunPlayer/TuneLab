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

    public SidecarHost(string packageId, string packageDir, RuntimeInfo runtime, string entry, TimeSpan? callTimeout = null)
    {
        mPackageId = packageId; mPackageDir = packageDir; mRuntime = runtime; mEntry = entry;
        mCallTimeout = callTimeout ?? TimeSpan.FromMinutes(2);
    }

    readonly object mLock = new();
    readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> mPending = new();
    int mEpoch;
    Process? mProcess;
    Stream? mStdin;
    TaskCompletionSource<bool>? mStartedTcs;   // 握手进行中（单飞）
    DateTime mFirstStartUtc = DateTime.MinValue;
    int mDeathsInWindow;
    bool mCircuitOpen;
    bool mDisposed;

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
    sealed class ExpectedFormat { public string Entry = ""; public string[] Import = []; public string[] Export = []; }
    readonly List<ExpectedFormat> mExpectedFormats = [];

    public void ExpectFormat(string entryId, string[] importSuffixes, string[] exportSuffixes)
    {
        lock (mExpectedFormats)
            mExpectedFormats.Add(new ExpectedFormat { Entry = entryId, Import = importSuffixes, Export = exportSuffixes });
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
        // params.entries 携带宿主期望（entry 键 = manifest 的相对路径）——sidecar 按同一键兑现 provides。
        var handshake = new JsonObject
        {
            ["protocol"] = WireProtocol.Version,
            ["package_id"] = mPackageId,
        };
        var entriesNode = new JsonArray();
        lock (mExpectedFormats)
            foreach (var ef in mExpectedFormats)
                entriesNode.Add((JsonNode)new JsonObject
                {
                    ["type"] = "format",
                    ["entry"] = ef.Entry,
                    ["import_suffixes"] = JsonSerializer.SerializeToNode(ef.Import),
                    ["export_suffixes"] = JsonSerializer.SerializeToNode(ef.Export),
                });
        handshake["entries"] = entriesNode;
        try
        {
            var reply = CallInternal("initialize", handshake, TimeSpan.FromSeconds(30), mEpoch);
            if (reply == null || reply["capabilities"] is not JsonObject)
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "sidecar for {0} completed handshake without capabilities", mPackageId));
            ValidateHandshake(reply);
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

    // 握手后核对 provides（§2.3）：manifest 承诺的每个脚本条目，sidecar 必须自报支持，否则握手期
    // 就判死（"作者写了、sidecar 不认"是最容易静默失联的一类不匹配，拦在最前）。
    void ValidateHandshake(JsonNode? reply)
    {
        var provided = new HashSet<string>(StringComparer.Ordinal);
        if (reply!["provides"] is JsonArray arr)
            foreach (var item in arr)
                if (item is JsonObject o && o["entry"]?.GetValue<string>() is { } e)
                    provided.Add(e);
        lock (mExpectedFormats)
        {
            foreach (var ef in mExpectedFormats)
                if (!provided.Contains(ef.Entry))
                    throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                        "sidecar for {0} handshake does not declare entry '{1}' that the manifest promised", mPackageId, ef.Entry));
        }
    }

    void AppendStderr(string line)
    {
        const int Cap = 64 * 1024;
        var tail = StderrTail + line + "\n";
        if (tail.Length > Cap)
            tail = tail.Substring(tail.Length - Cap);
        StderrTail = tail;
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
            // sidecar → 宿主：M1 只有 log.write；其余通知忽略（前向兼容）。
            if (m.GetValue<string>() == "log.write")
            {
                var p = node["params"]?.AsObject();
                LogReceived?.Invoke(p?["level"]?.GetValue<string>() ?? "info", p?["message"]?.GetValue<string>() ?? "");
            }
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

    public async Task<JsonNode?> CallAsync(string method, JsonNode? args)
    {
        await EnsureRunningAsync().ConfigureAwait(false);
        int epoch;
        lock (mLock) epoch = mEpoch;
        return await Task.Run(() => CallInternal(method, args, mCallTimeout, epoch)).ConfigureAwait(false);
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
