using System;
using System.Collections.Generic;
using TuneLab.Extensions.Instruments;
using TuneLab.Extensions.Voices;
using TuneLab.Foundation;

namespace TuneLab.Extensions;

internal static class SoundSourceEngines
{
    // 急切 Init 全部音源引擎（已 Init 的为空操作），返回失败者的现成文案（成功则为空）。
    //
    // 【为什么收在一处】四个地方要做同一件事：启动、装完新扩展（界面与 `extension install` 各一条路）、
    // headless 起来。而「哪些引擎要急切 Init」是一条**判据**：voice 与 instrument 都是 MidiPart 的音源、
    // 都有音源目录要露出，故都要；effect 没有音源目录，按 part 用到再 Init 才对。这条判据散成几份，
    // 就一定会有一天只加了一边。
    //
    // 时机同样是判据的一部分：**必须在工程挂上之前**（工程一挂各 part 立即 Activate 并建合成管线，
    // 此刻引擎若未 Init 就会回落到空会话且无回建路径——起动即无声）。
    //
    // 失败只报不停：一个引擎坏掉不该让其余音源不可用。日志在这里统一记（含异常对象），
    // 呈现各归各——启动弹对话框、headless 打到 stderr、命令放进回报——故这里只把文案交出去。
    public static IReadOnlyList<string> InitAll()
    {
        var failures = new List<string>();

        foreach (var engine in VoicesManager.GetAllVoiceEngines())
            TryInit("Voice", engine, () => VoicesManager.InitEngine(engine), failures);
        foreach (var engine in InstrumentsManager.GetAllInstrumentEngines())
            TryInit("Instrument", engine, () => InstrumentsManager.InitEngine(engine), failures);

        return failures;
    }

    static void TryInit(string kind, string engine, Action init, List<string> failures)
    {
        try
        {
            init();
        }
        catch (Exception ex)
        {
            Log.ErrorAttributed(string.Format("{0} engine [{1}] failed to init", kind, engine), ex);
            failures.Add(string.Format("{0} engine [{1}] failed to init:\n{2}", kind, engine, ex.Message));
        }
    }
}
