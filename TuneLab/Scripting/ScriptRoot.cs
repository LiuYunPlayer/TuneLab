using System;
using System.Linq;
using Jint.Native;
using TuneLab.Audio;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Scripting;

// 脚本的【根对象】（注入脚本的全局 `tl`）= 编辑器。它承载"编辑器态"的入口——系统常量、当前工程、当前编辑的
// part、播放线、网格吸附——而工程数据本身在 `tl.currentProject()` 返回的 project 对象上。
// 这样 `tl.currentProject()` / `tl.currentPart()` 对称（都是"编辑器的当前 X"），且多开工程时 `currentProject()`
// 语义天然稳定、老脚本不失效。`tl` 不注入任何收口逻辑——危险包裹全在 ScriptContext（脚本不可见）。
internal sealed class ScriptApp
{
    readonly ScriptContext mContext;
    readonly ScriptProject mProject;

    public ScriptApp(ScriptContext context)
    {
        mContext = context;
        mProject = new ScriptProject(context);
    }

    // 每四分音符的 tick 数（系统常量；C# 中在 MusicTheory，不在 IProject，故归编辑器/系统级）。
    public double Ppq => MusicTheory.RESOLUTION;

    // 当前界面语言文化码（如 "zh-CN"/"en-US"）。用于在 getScriptInfo 里产出本地化显示名、或动作里本地化对话框文案。
    // 与工程无关，无工程打开时亦可读。
    public string Language => mContext.Language;

    // 当前工程对象（脚本的工程数据入口）。
    public ScriptProject CurrentProject() => mProject;

    // 钢琴窗当前打开编辑的 midi part；未打开返回 null。
    public ScriptPart? CurrentPart()
    {
        var part = mContext.CurrentMidiPart;
        return part == null ? null : mContext.WrapPart(part);
    }

    // 编排区当前选中的 parts（跨全部轨道，支持多选）；无选中返回空数组。右键某 part 时它必被选中，故这是"右键目标"的入口。
    public ScriptPart[] SelectedParts()
        => mContext.Project.Tracks.SelectMany(t => t.Parts).Where(p => p.IsSelected).Select(mContext.WrapPart).ToArray();

    // 当前选中的轨道（支持多选）；无选中返回空数组。右键轨道头/空白泳道时该轨必被选中，故这是 track/trackContent 工具的目标入口。
    public ScriptTrack[] SelectedTracks()
        => mContext.Project.Tracks.Where(t => t.IsSelected).Select(mContext.WrapTrack).ToArray();

    public ScriptPlayhead Playhead()
    {
        double sec = AudioEngine.CurrentTime;
        double tick = mContext.Project.TempoManager.GetTick(sec);
        var (bar, beat) = mContext.Project.TimeSignatureManager.GetBarAndBeatIndexForTick(tick);
        return new ScriptPlayhead(tick, sec, bar + 1, beat + 1, AudioEngine.IsPlaying);
    }

    // 把绝对 tick 吸附到当前量化网格；无网格时原样返回。
    public double Snap(double tick)
    {
        var q = mContext.Quantization;
        int cell = q?.TicksPerCell() ?? 0;
        return cell <= 0 ? tick : Math.Round(tick / cell) * cell;
    }
}

// `tl.currentProject()` 返回的工程对象：承载工程级数据——轨、速度、拍号。对称 C# 的 IProject。
// 创建挂父（project.addTrack / track.addPart / part.addNote），删除一律 x.remove()。
internal sealed class ScriptProject(ScriptContext ctx)
{
    public ScriptTrack[] Tracks() => ctx.Project.Tracks.Select(ctx.WrapTrack).ToArray();

    public ScriptTrack AddTrack(JsValue name)
    {
        ctx.EnsureWritable();
        ctx.Project.AddTrack(new TrackInfo { Name = ScriptArgs.AsStrOrNull(name) ?? "Track" });
        ctx.Bump();
        return ctx.WrapTrack(ctx.Project.Tracks[ctx.Project.Tracks.Count - 1]);
    }

    public void RemoveTrack(ScriptTrack track)
    {
        if (track == null || track.Removed) throw new ScriptApiException("expected a live track handle (from project.tracks()/project.addTrack()).");
        ctx.EnsureWritable();
        ctx.Project.RemoveTrack(track.Track);
        track.Removed = true;
        ctx.Bump();
    }

    public ScriptTempo[] Tempos()
        => ctx.Project.TempoManager.Tempos.Select(t => new ScriptTempo(t.Bpm, t.Pos)).ToArray();

    public ScriptTimeSignature[] TimeSignatures()
        => ctx.Project.TimeSignatureManager.TimeSignatures.Select(s => new ScriptTimeSignature(s.Numerator, s.Denominator, s.BarIndex + 1)).ToArray();

    public void SetTempo(double bpm, JsValue atTick)
    {
        if (bpm <= 0) throw new ScriptApiException("bpm must be positive.");
        ctx.EnsureWritable();
        double tick = ScriptArgs.AsNumOrNull(atTick) ?? 0;
        var manager = ctx.Project.TempoManager;
        int existing = -1;
        for (int i = 0; i < manager.Tempos.Count; i++)
            if (Math.Abs(manager.Tempos[i].Pos - tick) < 0.5) { existing = i; break; }
        if (existing >= 0) manager.SetBpm(existing, bpm);
        else manager.AddTempo(tick, bpm);
        ctx.Bump();
    }

    public void SetTimeSignature(int numerator, int denominator, JsValue atBar)
    {
        if (numerator < 1 || denominator < 1) throw new ScriptApiException("numerator/denominator must be >= 1.");
        ctx.EnsureWritable();
        int barIndex = (ScriptArgs.AsIntOrNull(atBar) ?? 1) - 1;   // 1-based 小节号 → 0-based index
        if (barIndex < 0) throw new ScriptApiException("atBar must be >= 1.");
        var manager = ctx.Project.TimeSignatureManager;
        int existing = -1;
        for (int i = 0; i < manager.TimeSignatures.Count; i++)
            if (manager.TimeSignatures[i].BarIndex == barIndex) { existing = i; break; }
        if (existing >= 0) manager.SetMeter(existing, numerator, denominator);
        else manager.AddTimeSignature(barIndex, numerator, denominator);
        ctx.Bump();
    }
}
