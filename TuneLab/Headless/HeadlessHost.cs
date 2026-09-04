using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Audio.NAudio;
using TuneLab.Commands;
using TuneLab.Data;
using TuneLab.Extensions;
using TuneLab.Extensions.Formats;
using TuneLab.Extensions.Instruments;
using TuneLab.Extensions.Voices;
using TuneLab.Foundation;
using TuneLab.I18N;
using TuneLab.Scripting;
using TuneLab.Utils;

namespace TuneLab.Headless;

// ── 无头宿主 ──
//
// headless = 不开窗口、不建音频设备，但插件加载了、工程在内存、命令能跑、合成能跑
// （docs/command-surface.md §8）。命令面的第四个入口从这里起：CLI 的 --headless、以及将来
// 无人值守跑批的进程。跑的是与侧栏 agent、命令桥同一份命令——这里只负责【把环境装起来】。
//
// 【为什么不建 Avalonia 平台】只为一个我们并不需要的窗口系统拉起 Skia/字体/平台一整套并不划算，
// 而宿主里命令要用到的那些本来就不碰 UI：数据层、扩展管理器、音源管理器零 Avalonia 依赖；
// AudioEngine 里数据层真正用到的（SampleRate、AudioGraph.AddTrack）都是静态量，不 Init 也成立。
// 故这里只装真正需要的那几样。若哪天某个第三方插件在 Init 里碰 Avalonia，升级成 headless 平台
// 只动本文件。
//
// 【线程与合成】整个生命周期跑在一条专用线程上，装一个可泵的 SynchronizationContext：合成插件的
// 异步续体经它 marshal 回这条「数据线程」，由下面的驱动循环手动泵。编辑器里那条数据线程 =
// Avalonia UI 线程（其 Dispatcher 自动泵），探测沙箱已用同一套机制跑通真引擎合成。
//
// 【数据目录】读写的是 PathManager.TuneLabFolder（设置、扩展、脚本库全在那儿）。CI 里应当用
// TUNELAB_DATA_DIR 指向一个临时沙盒；不隔离就是在动这台机器上真实的用户数据，且若同时开着
// TuneLab，两个进程会各写各的同一份配置。
internal static class HeadlessHost
{
    // 拆场景的时限。超了就撇下那条（后台）线程收工——见下面 Run 里的理由。
    const int TeardownGraceMs = 5000;

    // 起一个无头宿主，把 body 跑完再拆掉。body 拿到的 CommandContext 只在那条专用线程上有效，
    // 故 body 自己也跑在那条线程上（它的 await 续体由驱动循环泵回来）。
    public static T Run<T>(HeadlessOptions options, Func<CommandContext, Task<T>> body)
    {
        var outcome = new Outcome<T>();
        var thread = new Thread(() =>
        {
            try { RunOnThread(options, body, outcome); }
            catch (Exception ex) { outcome.Error = ex; outcome.Ready.Set(); }
        })
        {
            IsBackground = true,
            Name = "TuneLabHeadless",
        };
        thread.Start();

        // 【结果一出来就算跑完，拆场景只等一小会儿】结果早已产出，而拆场景要跑第三方插件的 Destroy——
        // 那是别人的代码，可以永远不返回：实测某 legacy voice 引擎的 Init 起了一个对本机后端的请求，
        // 后端没起时它永不完成，而它的 Destroy() 又阻塞等 Init 完成，一个引擎就把整个进程扣住了。
        // 无人值守的跑批挂死是最坏的结果（CI 里表现为一个永远不结束的 job），故给它一个上限，
        // 超时就记一笔、撇下那条后台线程收工（进程退出时它随之消失）。
        outcome.Ready.Wait();
        if (!thread.Join(TeardownGraceMs))
        {
            Log.Warning("Headless teardown did not finish in time; leaving it behind. Some extension's Destroy() is stuck.");
            options.Report?.Invoke("an extension did not shut down in time; exiting anyway.");
            Log.Shutdown();   // 幂等：正常收尾时 Teardown 已经调过
        }

        // 原样抛回调用方（保留类型与消息）：装载工程失败之类的事该由入口决定怎么报，不在这里翻译。
        if (outcome.Error != null)
            throw outcome.Error;
        return outcome.Result;
    }

    static void RunOnThread<T>(HeadlessOptions options, Func<CommandContext, Task<T>> body, Outcome<T> outcome)
    {
        var pump = new PumpableSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(pump);

        // 不回声到控制台：这个进程的 stdout 属于命令结果（--json 时要能直接喂给 jq）。日志在文件里。
        Log.SetupLogger(new FileLogger(PathManager.LogFilePath, echoToConsole: false));
        Log.Info("Headless host starting. Data dir: " + PathManager.TuneLabFolder);

        // 与真实启动同一份初始化（配置/翻译/路由/启停/键位/插件上下文），故 headless 下的行为与
        // 用户看到的一致。
        Program.InitCoreServices();
        // 解码器【不是】播放设备：音频 part 要靠它读 wav/mp3。真正开设备的 AudioEngine.Init() 刻意不调。
        AudioUtils.Init(new NAudioCodec());
        LegacyCompatLoader.Wire();
        ExtensionManager.LoadExtensions();
        InitSoundSourceEngines(options.Report);

        ProjectDocument? document = null;
        try
        {
            document = new ProjectDocument();
            var (project, path) = LoadProject(options.ProjectPath);
            document.SetProject(project, path);

            var ctx = new CommandContext
            {
                Project = document.Project,
                Language = () => TranslationManager.CurrentLanguage.Value,
                // 没给就是没给：一条 Edit 都不做，且命令会说清是「这个入口没配授权」而不是「用户拒绝了」。
                Authorization = options.Authorization,
                // 没有编辑器态：依赖它的命令按「用户什么也没选」处理或要求显式传参，不猜（§5.2）。
                EditorState = null,
                // 没有模型：用到它的命令按 §5.3 降级（缓存能用就用、用不上如实标注），不是消失。
                SideModel = null,
                MainThread = new PumpDispatcher(pump),
            };
            // 这个进程里的宿主共同环境也就位了——将来 MCP server 跑在 headless 里时，它与 body
            // 看到的是同一个工程。
            HostCommandContext.Provider = () => ctx;

            // 驱动循环：body 在本线程上起跑，遇 await 让出后由这里把续体泵回来。
            var task = body(ctx);
            while (!task.IsCompleted)
            {
                pump.DrainAll();
                pump.WaitForWork(TimeSpan.FromMilliseconds(20));
            }
            pump.DrainAll();
            outcome.Result = task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // 【必须在 finally 置位 Ready 之前记下】否则调用方可能拿着一个空结果先走，而异常还没写进信箱。
            outcome.Error = ex;
        }
        finally
        {
            HostCommandContext.Provider = null;
            // 结果（或异常）已经定了，先放调用方走，再拆场景——拆不动时它不必陪着挂着。
            outcome.Ready.Set();
            Teardown(document, pump);
        }
    }

    // 装载工程：给了路径就照「打开」那条路（native 元数据一并吃进来），没给就新建空工程。
    // 失败一律抛——调用方点名要的工程没在手里，后面每条命令都会答非所问，不如就此打住。
    //
    // 刻意【不补轨道颜色】（编辑器打开时会给没颜色的轨补一个呈现层默认色）：headless 不呈现，
    // 补了反而会把呈现层的默认写进随后导出的文件里。
    static (Project Project, string Path) LoadProject(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return (new Project(), string.Empty);

        var fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(string.Format("no file at \"{0}\".", fullPath), fullPath);
        if (!FormatsManager.DeserializeNative(fullPath, out var file, out var error))
            throw new InvalidDataException(string.Format("cannot open \"{0}\" — {1}", fullPath, error));

        var project = new Project(file.Project);
        project.SetExportConfig(file.Export);
        return (project, fullPath);
    }

    // 音源引擎（voice 与 instrument）须在装载工程【之前】Init：工程一挂上，各 part 立即 Activate 并
    // 构建合成管线，此刻引擎若未 Init 就会回落到空会话且无回建路径（起动即无声）。与 App 里那段同理同序。
    // 失败只报不停：一个引擎坏掉不该让整趟无人值守的跑批停摆，其余音源照常可用。
    static void InitSoundSourceEngines(Action<string>? report)
    {
        foreach (var engine in VoicesManager.GetAllVoiceEngines())
        {
            try { VoicesManager.InitEngine(engine); }
            catch (Exception ex)
            {
                Log.ErrorAttributed(string.Format("Voice engine [{0}] failed to init", engine), ex);
                report?.Invoke(string.Format("voice engine [{0}] failed to init: {1}", engine, ex.Message));
            }
        }
        foreach (var engine in InstrumentsManager.GetAllInstrumentEngines())
        {
            try { InstrumentsManager.InitEngine(engine); }
            catch (Exception ex)
            {
                Log.ErrorAttributed(string.Format("Instrument engine [{0}] failed to init", engine), ex);
                report?.Invoke(string.Format("instrument engine [{0}] failed to init: {1}", engine, ex.Message));
            }
        }
    }

    // 拆场景：换一个空工程触发旧工程 Detach+Dispose（各 part Deactivate → 合成会话 Dispose）。
    // 会话若有在飞收尾会 Post 回本泵，故短暂泵一会儿让延迟销毁落地（尽力而为）。
    //
    // 刻意【不跑 ExtensionManager.LaunchPendingUninstalls()】：那是给用户装卸扩展收尾的，
    // 无人值守的进程不该替他执行——下次他自己开 TuneLab 时照样会跑。
    static void Teardown(ProjectDocument? document, PumpableSynchronizationContext pump)
    {
        try
        {
            document?.SetProject(new Project());
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 500 && pump.DrainAll())
                pump.WaitForWork(TimeSpan.FromMilliseconds(20));
        }
        catch (Exception ex) { Log.Info("Headless teardown (project): " + ex.Message); }

        try { ExtensionManager.Destroy(); }
        catch (Exception ex) { Log.Info("Headless teardown (extensions): " + ex.Message); }

        Log.Shutdown();
    }
}

// 一趟 headless 的结果信箱。结果与"拆完了没有"是两件事，故分开：Ready 一置位调用方就能收工。
internal sealed class Outcome<T>
{
    public T Result = default!;
    public Exception? Error;
    public readonly ManualResetEventSlim Ready = new(false);
}

// 起一个无头宿主要交代的三件事。
internal sealed record HeadlessOptions
{
    // 要装载的工程文件；null = 新建一个空工程。
    public string? ProjectPath { get; init; }

    // 授权策略。null = 一条 Edit 都不做（命令会如实说是「这个入口没配授权」）。
    public IAuthorizationPolicy? Authorization { get; init; }

    // 启动期的告警去哪（某个音源引擎 Init 失败等）。null = 只进日志。
    public Action<string>? Report { get; init; }
}

// headless 下的主线程调度器：把活儿送回那条装了泵的数据线程。
//
// 命令本来就跑在那条线程上（驱动循环在那儿），故同线程直接就地执行——入队再等自己泵是自死锁；
// 这一判断在 PumpableSynchronizationContext.Send 里，连同跨线程时的等待与异常透传。
internal sealed class PumpDispatcher(PumpableSynchronizationContext pump) : IMainThreadDispatcher
{
    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        T result = default!;
        pump.Send(_ => result = func(), null);
        return Task.FromResult(result);
    }
}
