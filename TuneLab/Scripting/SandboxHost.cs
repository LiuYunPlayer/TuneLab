using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jint;
using Jint.Native;
using TuneLab.Data;
using TuneLab.Data.Synthesis;
using TuneLab.Extensions.Voices;
using TuneLab.SDK;

namespace TuneLab.Scripting;

// ── 探测沙箱（F 支柱验证桩）──
//
// 静态读 schema 有天花板：空 context 只能拿默认分支，条件化 / 数据驱动的 schema（如 phoneme slot 来自真实
// 音素）静态永远够不着。解法 = 给一段脚本一个【可丢弃的无头工程】，用同一 `tl` 动作面随便造场景，再真触发
// 合成、读回显（note.SynthesizedSyllable）拿到只有合成后才存在的真相。
//
// 本文件是"验证桩"：用最小机制打通最吓人的一环——无头造 1-note part + 挂真 voice 源 + 离线触发合成 +
// 泵驱动等待 + 读回真实音素，证明 SyncContext 泵 / 驱动循环 / 引擎 bootstrap 在无窗口下成立。
//
// 隔离与线程：整个生命周期跑在一条【专用后台线程】上，装一个可泵的 SynchronizationContext
// （PumpableSynchronizationContext）。合成插件的异步续体经它 marshal 回本线程，宿主手动泵。工程是全新
// 可丢弃的 ProjectDocument+Project，与用户工程完全无关——故写入【不过授权闸门】、可放开试。
internal static class SandboxHost
{
    const int MaxOutput = 16 * 1024;

    // 沙箱运行结果：Ok=脚本正常跑完；Error=脚本抛错的清晰说明；Output=print/log 捕获；ResultText=脚本最后表达式的值。
    public readonly record struct SandboxResult(bool Ok, string? Error, string Output, string? ResultText);

    public static Task<SandboxResult> RunAsync(string code, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<SandboxResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(RunOnThread(code, cancellationToken)); }
            catch (Exception ex) { tcs.SetResult(new SandboxResult(false, "sandbox host error: " + ex.Message, "", null)); }
        })
        {
            IsBackground = true,
            Name = "AgentSandbox",
        };
        thread.Start();
        return tcs.Task;
    }

    static SandboxResult RunOnThread(string code, CancellationToken cancellationToken)
    {
        // 沙箱工程的合成不设 Jint JS 超时（同步阻塞的 synthesize() 不是 JS 语句、Jint 超时也拦不住原生阻塞）：
        // 时限由 synthesize 自己的 budget + CancellationToken 兜。仍留语句数上限当失控循环保险丝。
        var limits = new ScriptLimits(null, 50_000_000);

        var prev = SynchronizationContext.Current;
        var pump = new PumpableSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(pump);

        ProjectDocument? document = null;
        var output = new StringBuilder();
        try
        {
            document = new ProjectDocument();
            document.SetProject(new Project());   // 挂文档根（否则 DataObject.Head NRE）；默认 120bpm/4-4 已由管理器兜底

            var context = new ScriptContext(document.Project!, null, null, null, null, null);
            var engine = ScriptRunner.CreateEngine(limits, cancellationToken);
            var sandbox = new SandboxApi(context, pump, cancellationToken);

            void Print(JsValue v)
            {
                if (output.Length < MaxOutput)
                    output.Append(ScriptRunner.Format(v)).Append('\n');
            }

            engine.SetValue("tl", new ScriptApp(context));
            engine.SetValue("sandbox", sandbox);
            engine.SetValue("print", Print);
            engine.SetValue("log", Print);
            engine.Execute("globalThis.console = { log: print, info: print, warn: print, error: print, debug: print };");

            var completion = engine.Evaluate(code);
            string? resultText = completion is not null && !completion.IsUndefined() && !completion.IsNull()
                ? ScriptRunner.Format(completion) : null;

            context.FlushForSynthesis();   // 关掉任何仍开着的 merge 括号（形态收尾；沙箱不 Commit）
            return new SandboxResult(true, null, output.ToString(), resultText);
        }
        catch (ScriptApiException ae)
        {
            return new SandboxResult(false, ae.Message, output.ToString(), null);
        }
        catch (Jint.Runtime.JavaScriptException jse)
        {
            return new SandboxResult(false, jse.Message, output.ToString(), null);
        }
        catch (Exception ex)
        {
            return new SandboxResult(false,
                cancellationToken.IsCancellationRequested ? "sandbox run was cancelled." : ex.Message,
                output.ToString(), null);
        }
        finally
        {
            // 拆场景：换一个空工程触发旧工程 Detach+Dispose（各 part Deactivate → 合成会话 Dispose）；
            // 会话若有在飞收尾会 Post 回本泵，故短暂泵一会儿让延迟销毁落地（尽力而为，沙箱可丢弃）。
            try
            {
                document?.SetProject(new Project());
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 200 && pump.DrainAll())
                    pump.WaitForWork(TimeSpan.FromMilliseconds(20));
            }
            catch { /* teardown best-effort */ }
            SynchronizationContext.SetSynchronizationContext(prev);
        }
    }
}

// 注入沙箱脚本的 `sandbox` 全局：环境自省 + 挂音源 + 触发合成 + 读回显。这些是沙箱【专属】原语（synthesize
// 依赖手动驱动循环、只在无头沙箱里有意义），故不进正常脚本面的 tl 句柄——保正常 API 面干净、语义不歧义。
// 场景搭建仍走 tl（tl.currentProject().addTrack()、track.addPart()、part.addNote() …），本对象只补"合成相关"的那半。
internal sealed class SandboxApi(ScriptContext context, PumpableSynchronizationContext pump, CancellationToken cancellationToken)
{
    // 列出本机的 voice 音源（会惰性 Init 各引擎、跑插件代码——这正是要验证的 bootstrap）。返回 [{type, id, name}]。
    // 如实镜像 VoicesManager 的枚举（含内建空引擎 type=""——无声源 part 的回退）：sandbox 是通用可丢弃工程、
    // 无特殊情况保持与普通 project 一致，不自造「只列可合成音源」的分歧契约，取哪个由脚本自行判断。
    public SandboxVoice[] Voices()
    {
        var list = new List<SandboxVoice>();
        foreach (var engine in VoicesManager.GetAllVoiceEngines())
        {
            var infos = VoicesManager.GetAllVoiceInfos(engine);
            if (infos == null)
                continue;
            foreach (var kv in infos)
                list.Add(new SandboxVoice(engine, kv.Key, kv.Value.Name));
        }
        return list.ToArray();
    }

    // 挂音源用正常 tl 写原语 `part.setSoundSource({kind,type,id})`（真实编辑器/沙箱通用，含存在校验），沙箱不再另开 setVoice。

    // 触发该 part 的离线合成并【同步等待】完成（在本沙箱线程手动泵驱动循环，仿 Editor.SynthesisNext）。
    // opts（可选）：{timeoutMs?(默认30000,1000~120000), maxDispatches?(默认64,1~512)}。返回 {done, dispatches, ms, timedOut}。
    // 完成后即可用 sandbox.syllable(note) 读回真实音素。
    public SandboxSynthResult Synthesize(ScriptPart part, JsValue? opts = null)
    {
        var midi = MidiOf(part, "synthesize");
        var pipeline = midi.SynthesisPipeline
            ?? throw new ScriptApiException("this part has no synthesis pipeline; attach a voice with part.setSoundSource({kind:'voice', type, id}) first.");

        int timeoutMs = 30000, maxDispatches = 64;
        if (opts is not null && !opts.IsUndefined() && !opts.IsNull())
        {
            var o = ScriptArgs.Obj(opts, "opts");
            if (ScriptArgs.OptInt(o, "timeoutMs") is { } t) timeoutMs = t;
            if (ScriptArgs.OptInt(o, "maxDispatches") is { } m) maxDispatches = m;
        }
        timeoutMs = Math.Clamp(timeoutMs, 1000, 120000);
        maxDispatches = Math.Clamp(maxDispatches, 1, 512);

        // 关括号，让管线看到刚加的音符并开始 prep（否则 IsSynthesisBatching 抑制、通知也未扇出）。
        context.FlushForSynthesis();

        var sw = Stopwatch.StartNew();
        int dispatches = 0;
        bool timedOut = false, done = false;
        while (true)
        {
            pump.DrainAll();   // 跑掉 marshal 回来的续体：prep 完成、Dispatch 的 await 续体（含置 IsBusy=false + 回填音素）
            if (cancellationToken.IsCancellationRequested)
                throw new ScriptApiException("sandbox synthesis was cancelled.");

            if (!pipeline.IsBusy)
            {
                // 全窗口 peek（±∞，不依赖 AudioEngine 播放线）：无待合成块即完成。
                if (pipeline.PeekNext(double.MinValue, double.MaxValue) is null)
                {
                    done = true;
                    break;
                }
                if (dispatches >= maxDispatches)
                    break;   // 预算用尽（未完成，如实报告）
                pipeline.Dispatch(double.MinValue, double.MaxValue);
                dispatches++;
            }

            if (sw.ElapsedMilliseconds > timeoutMs)
            {
                timedOut = true;
                break;
            }
            pump.WaitForWork(TimeSpan.FromMilliseconds(50));   // 小步长轮询：兼容续体未回本上下文的插件
        }

        pump.DrainAll();   // 收尾：排空可能仍在队列里的音素回填回调（保证 syllable() 读到最新）
        return new SandboxSynthResult(done, dispatches, sw.ElapsedMilliseconds, timedOut);
    }

    // 读一个音符的合成回显音素（引导 + 主体双列表 + BodyOffset）；未合成 / 无产物返回 null。
    public SandboxSyllable? Syllable(ScriptNote note)
    {
        if (note?.Note is not { } n)
            throw new ScriptApiException("syllable expects a live note handle (from part.notes()/part.addNote()).");
        var syllable = n.SynthesizedSyllable;
        return syllable == null ? null : new SandboxSyllable(syllable);
    }

    static MidiPart MidiOf(ScriptPart? part, string what)
    {
        if (part?.Part is MidiPart midi)
            return midi;
        throw new ScriptApiException(string.Format("{0} expects a midi part handle (from tl.currentProject().tracks()[i].parts()[j] or track.addPart()).", what));
    }
}

// —— 沙箱只读快照类型（给 Jint 读；camelCase 经解析器映射到这些 PascalCase 属性）——

internal sealed class SandboxVoice(string type, string id, string name)
{
    public string Type { get; } = type;
    public string Id { get; } = id;
    public string Name { get; } = name;
    public override string ToString() => string.Format(CultureInfo.InvariantCulture, "Voice(type={0}, id={1}, \"{2}\")", Type, Id, Name);
}

internal sealed class SandboxPhoneme(string symbol, double duration, double stretchWeight, string section)
{
    public string Symbol { get; } = symbol;
    public double Duration { get; } = duration;              // 标称时长（秒）
    public double StretchWeight { get; } = stretchWeight;    // 0=刚性辅音，>0=可伸核/元音
    public string Section { get; } = section;               // "leading" | "body"
    public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{{{0}: {1}s w={2} [{3}]}}", Symbol, Duration, StretchWeight, Section);
}

internal sealed class SandboxSyllable
{
    public SandboxPhoneme[] Leading { get; }
    public SandboxPhoneme[] Body { get; }
    public double BodyOffset { get; }        // 主体起点相对 note 头的有符号秒偏移
    public string[] Symbols { get; }         // 便利：引导 ++ 主体的扁平符号序

    public SandboxSyllable(SynthesizedSyllable syllable)
    {
        Leading = Convert(syllable.LeadingPhonemes, "leading");
        Body = Convert(syllable.BodyPhonemes, "body");
        BodyOffset = syllable.BodyOffset;
        var symbols = new List<string>(Leading.Length + Body.Length);
        foreach (var p in Leading) symbols.Add(p.Symbol);
        foreach (var p in Body) symbols.Add(p.Symbol);
        Symbols = symbols.ToArray();
    }

    static SandboxPhoneme[] Convert(IReadOnlyList<SynthesizedPhoneme> phonemes, string section)
    {
        var array = new SandboxPhoneme[phonemes.Count];
        for (int i = 0; i < phonemes.Count; i++)
            array[i] = new SandboxPhoneme(phonemes[i].Symbol, phonemes[i].Duration, phonemes[i].StretchWeight, section);
        return array;
    }

    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "Syllable(symbols=[{0}], bodyOffset={1}s)", string.Join(" ", Symbols), BodyOffset);
}

internal sealed class SandboxSynthResult(bool done, int dispatches, long ms, bool timedOut)
{
    public bool Done { get; } = done;             // 是否自然合成完毕（非超时/预算截断）
    public int Dispatches { get; } = dispatches;  // 派发了几段合成
    public double Ms { get; } = ms;               // 驱动循环耗时（毫秒）
    public bool TimedOut { get; } = timedOut;
    public override string ToString()
        => string.Format(CultureInfo.InvariantCulture, "SynthResult(done={0}, dispatches={1}, ms={2:0}{3})", Done, Dispatches, Ms, TimedOut ? ", TIMED OUT" : "");
}
