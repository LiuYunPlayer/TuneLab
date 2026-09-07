using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.Configs;

namespace TuneLab.Bridge;

// 命令桥：让【本机的外部进程】驱动这个运行中的 TuneLab——CLI、以及将来的 MCP server 都连它。
// 末端动作一份也不在这里，它只是命令面的又一个入口（见 docs/command-surface.md §7）。
//
// 默认关，由用户在设置里开（SettingsRegistry.CommandBridgeEnabled，且 agent 不可写——不能让 agent
// 自己打开自己的远程通道）。开即生成一次性凭据并监听，关即停止监听并删掉凭据文件。
internal static class CommandBridge
{
    static CancellationTokenSource? mRunning;
    static BridgeCredentials mCredentials;

    // 由 App 在主窗口建好之后调一次（那时 HostCommandContext 已由编辑器装上）。
    public static void Init()
    {
        Settings.CommandBridgeEnabled.Modified.Subscribe(Apply);
        Apply();
        // 进程正常退出时把凭据文件带走：留着一份连不上的文件，只会让下次用 CLI 的人以为桥开着。
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Stop();
    }

    static void Apply()
    {
        if (Settings.CommandBridgeEnabled.Value)
            Start();
        else
            Stop();
    }

    public static void Start()
    {
        if (mRunning != null)
            return;

        mCredentials = BridgeCredentials.Create();
        try { mCredentials.Write(); }
        catch (Exception ex)
        {
            // 写不出凭据就没人连得进来，那就别装作开着。
            Log.Error("Failed to write the command bridge credentials: " + ex.Message);
            return;
        }

        var cts = new CancellationTokenSource();
        mRunning = cts;
        Log.Info("Command bridge listening on " + mCredentials.PipeName);
        _ = Task.Run(() => AcceptLoopAsync(mCredentials, cts.Token));
    }

    public static void Stop()
    {
        var running = mRunning;
        if (running == null)
            return;
        mRunning = null;
        running.Cancel();
        // 取消只会叫醒【正在等连接】的那一次；已连上的会话在下一次读返回时结束。管道对象随之释放。
        BridgeCredentials.Delete();
        Log.Info("Command bridge stopped");
    }

    // 一次一个连接：外部 agent 是串行的，多连接的隔离留到真有需求再说。
    static async Task AcceptLoopAsync(BridgeCredentials credentials, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // CurrentUserOnly：管道的 ACL 只放本用户（Unix 上落到 0700 的 socket 文件）。
                // 纵深防御——门槛本来是凭据文件的文件权限，这一条让【连都连不上】比"连上了但 token 不对"更早发生。
                using var pipe = new NamedPipeServerStream(credentials.PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken);
                Log.Info("Command bridge client connected");
                await new BridgeSession(pipe, credentials.Token).RunAsync(cancellationToken);
                Log.Info("Command bridge client disconnected");
            }
            catch (OperationCanceledException) { return; }
            catch (IOException ex)
            {
                // 坏报文 / 对端硬断：这一条连接完了，桥本身照常等下一个客户端。
                Log.Info("Command bridge connection dropped: " + ex.Message);
            }
            catch (Exception ex)
            {
                Log.Error("Command bridge error: " + ex);
                // 未知错误不忙着重试：睡一下，免得管道创建持续失败时把日志刷爆。
                try { await Task.Delay(1000, cancellationToken); }
                catch (OperationCanceledException) { return; }
            }
        }
    }
}
