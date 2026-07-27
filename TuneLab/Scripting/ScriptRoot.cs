using System;
using System.Globalization;
using System.Linq;
using Jint;
using Jint.Native;
using TuneLab.Audio;
using TuneLab.Data;
using TuneLab.Extensions.Formats;
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

    // 编排区当前的范围选区（DAW 式 tick×轨道矩形，编辑器态、不入工程）；无选区返回 null。轨道号 1-based、连续区间。
    // 与 selectedParts()/selectedNotes()（选中的对象）正交：它只圈出"这片地方"、与里面有没有 part 无关，脚本据其横纵跨度批量处理落在区域里的东西。
    // 命名与 pianoSelection() 对偶：trackSelection=编排区(tick×轨道)、pianoSelection=钢琴窗(tick)，各负责一块。
    public ScriptSelection? TrackSelection() => mContext.TrackSelection;

    // 钢琴窗当前的范围选区（DAW 式 tick 带，限当前 part、贯穿全音高，编辑器态、不入工程）；无选区返回 null。
    // 与 selection()（编排区 tick×轨道）正交且独立并存：本接口只有时间维，脚本据其圈出当前 part 里"这段时间"批量处理。
    public ScriptPianoSelection? PianoSelection() => mContext.PianoSelection;

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
// 增删一律挂父（project.addTrack/removeTrack、track.addPart/removePart、part.addNote/removeNote）——没有 x.remove()。
internal sealed class ScriptProject(ScriptContext ctx)
{
    public ScriptTrack[] Tracks() => ctx.Project.Tracks.Select(ctx.WrapTrack).ToArray();

    // ── 导出设置（工程级；对齐 IProject 的 8 个属性） ──
    // 「跑一段脚本把导出各项设成我的预设」是用户会要的可复用命令（还会想绑快捷键），故按归属判据落在脚本面
    // （见 docs/agent-tools.md）。**这些是设置项、不入撤销栈**：与在导出侧栏里改它们一致，改完按 Ctrl+Z 不会
    // 退回。但脚本仍保证「出错 / preview 不落地」——由 ScriptContext 做写前快照 + 回退恢复。
    public string ExportPath { get => ctx.Project.ExportPath; set => SetExport(p => p.ExportPath = value ?? string.Empty); }
    public string ExportFileName { get => ctx.Project.ExportFileName; set => SetExport(p => p.ExportFileName = value ?? string.Empty); }

    // 音频格式 id："wav" | "mp3" | "flac" | "ogg"。未知值报错（而非静默回退 wav）——按名字指定一个东西时要显式。
    public string ExportFormat
    {
        get => ctx.Project.ExportFormat;
        set
        {
            if (!AudioExportFormatExtensions.TryParseId(value, out _))
                throw new ScriptApiException(string.Format("unknown export format \"{0}\"; use one of {1}.",
                    value, string.Join(", ", AudioExportFormatExtensions.AllIds)));
            SetExport(p => p.ExportFormat = value);
        }
    }

    public int ExportSampleRate
    {
        get => ctx.Project.ExportSampleRate;
        set { RequirePositive(value, "exportSampleRate"); SetExport(p => p.ExportSampleRate = value); }
    }

    // 无损格式（wav/flac）的位深；有损格式忽略它。
    public int ExportBitDepth
    {
        get => ctx.Project.ExportBitDepth;
        set { RequirePositive(value, "exportBitDepth"); SetExport(p => p.ExportBitDepth = value); }
    }

    // 有损格式（mp3/ogg）的目标码率 kbps；无损格式忽略它。
    public int ExportBitrate
    {
        get => ctx.Project.ExportBitrate;
        set { RequirePositive(value, "exportBitrate"); SetExport(p => p.ExportBitrate = value); }
    }

    // 是否导出总输出（母线）；各轨单独的开关在 track.exportEnabled 上。
    public bool MasterExportEnabled { get => ctx.Project.MasterExportEnabled; set => SetExport(p => p.MasterExportEnabled = value); }
    public int MasterExportChannels { get => ctx.Project.MasterExportChannels; set => SetExport(p => p.MasterExportChannels = ScriptTrack.RequireChannels(value, "masterExportChannels")); }

    void SetExport(Action<IProject> mutate)
    {
        ctx.EnsureWritable();
        ctx.CaptureExportConfig();   // 首次写入时留底，供出错 / preview 还原
        mutate(ctx.Project);
        ctx.Bump();
    }

    static void RequirePositive(int value, string what)
    {
        if (value <= 0) throw new ScriptApiException(string.Format("{0} must be positive.", what));
    }

    // 按完整 track info 新建一条轨并插到 index 位（省略 index = 追加到末尾；越界钳到合法范围）。
    // info: {name?, gain?, pan?, mute?, solo?, asRefer?, color?, parts?}——省略即用各字段的存储默认值
    // （如 name 为空串），宿主不替调用方假想。导出开关刻意不在 info 里（设置项、非"轨的内容"），见 track.exportEnabled。
    // parts 里可嵌完整的 part 树，故 project.addTrack(other.getInfo()) 就是整轨复制。
    public ScriptTrack AddTrack(JsValue? info = null, JsValue? index = null)
    {
        var trackInfo = info is null || info.IsUndefined() || info.IsNull()
            ? new TrackInfo()
            : ScriptInfo.ReadTrackInfo(info);
        ctx.EnsureWritable();
        var track = ctx.Project.CreateTrack(trackInfo);
        ctx.Project.InsertTrack(ClampTrackIndex(index), track);
        ctx.Bump();
        return ctx.WrapTrack(track);
    }

    // 把一条【游离】轨插回 index 位（保持对象身份，故其全部 part 与 undo 记录都还连着）。
    public void InsertTrack(ScriptTrack track, JsValue? index = null)
    {
        if (track == null) throw new ScriptApiException("expected a track handle.");
        if (!track.Detached) throw new ScriptApiException("this track is already in the project; remove it first (project.removeTrack(track)).");
        ctx.EnsureWritable();
        ctx.Project.InsertTrack(ClampTrackIndex(index), track.Track);
        track.Detached = false;
        ctx.Bump();
    }

    // 把轨从工程摘出，返回它的（现在游离的）句柄：不插回 = 删除，插回别的位置 = 调序。
    public ScriptTrack RemoveTrack(ScriptTrack track)
    {
        if (track == null || track.Detached) throw new ScriptApiException("expected a live track handle (from project.tracks()/project.addTrack()).");
        ctx.EnsureWritable();
        ctx.Project.RemoveTrack(track.Track);
        track.Detached = true;
        ctx.Bump();
        return track;
    }

    // 省略 index = 追加到末尾；给了就钳到 [0, count]（count = 追加）。
    int ClampTrackIndex(JsValue? index)
    {
        int count = ctx.Project.Tracks.Count;
        return ScriptArgs.AsIntOrNull(index) is { } i ? Math.Clamp(i, 0, count) : count;
    }

    // 从文件导入其【全部轨】、加法式并进当前工程，返回新加入的轨句柄（可据此增删/改）。
    // path = 本地文件绝对路径；格式由扩展名决定（内建 tlp/tlpx/mid/midi + 已装的格式插件）。导入含各轨的 part/音符/
    // 音源/effect/自动化（音源未装则优雅降级为空源，同 UI 导入）。
    // 时基：**保留当前工程的速度/拍号**，各轨按其【原始 tick】落位（=按小节对齐，不做时基重映射）——这是最可预期的加法式
    // 默认；「按当前速度时间对齐」「导入文件速度」等模式暂不做（未来可加 options 参数）。
    // 只读入文件、加法式写工程（整段脚本一个可撤销单位）；文件不存在/格式不支持/解析失败【报错】、整脚本原子回退。
    public ScriptTrack[] ImportTracks(string path)
    {
        if (string.IsNullOrEmpty(path)) throw new ScriptApiException("import path is required.");
        ctx.EnsureWritable();
        if (!FormatsManager.Deserialize(path, out var info, out var error))
            throw new ScriptApiException(string.Format("cannot import \"{0}\": {1}", path, error));
        int preCount = ctx.Project.Tracks.Count;
        foreach (var trackInfo in info.Tracks)
        {
            ctx.Project.AddTrack(trackInfo);
            ctx.Bump();
        }
        var added = new ScriptTrack[ctx.Project.Tracks.Count - preCount];
        for (int i = 0; i < added.Length; i++)
            added[i] = ctx.WrapTrack(ctx.Project.Tracks[preCount + i]);
        return added;
    }

    public ScriptTempo[] Tempos()
        => ctx.Project.TempoManager.Tempos.Select(t => new ScriptTempo(t.Bpm, t.Pos)).ToArray();

    public ScriptTimeSignature[] TimeSignatures()
        => ctx.Project.TimeSignatureManager.TimeSignatures.Select(s => new ScriptTimeSignature(s.Numerator, s.Denominator, s.BarIndex + 1)).ToArray();

    public void SetTempo(double bpm, JsValue? atTick = null)
    {
        if (bpm <= 0) throw new ScriptApiException("bpm must be positive.");
        ctx.EnsureWritable();
        double tick = ScriptArgs.AsNumOrNull(atTick) ?? 0;
        var manager = ctx.Project.TempoManager;
        int existing = FindTempo(tick);
        if (existing >= 0) manager.SetBpm(existing, bpm);
        else manager.AddTempo(tick, bpm);
        ctx.Bump();
    }

    public void SetTimeSignature(int numerator, int denominator, JsValue? atBar = null)
    {
        if (numerator < 1 || denominator < 1) throw new ScriptApiException("numerator/denominator must be >= 1.");
        ctx.EnsureWritable();
        int barIndex = (ScriptArgs.AsIntOrNull(atBar) ?? 1) - 1;   // 1-based 小节号 → 0-based index
        if (barIndex < 0) throw new ScriptApiException("atBar must be >= 1.");
        var manager = ctx.Project.TimeSignatureManager;
        int existing = FindTimeSignature(barIndex);
        if (existing >= 0) manager.SetMeter(existing, numerator, denominator);
        else manager.AddTimeSignature(barIndex, numerator, denominator);
        ctx.Bump();
    }

    // 删掉 atTick 处的速度标记（setTempo 的对偶）。该处没有标记就报错而不是静默 no-op（假成功会让脚本
    // 以为删掉了）。工程起点那一个是基准速度、【不可删】——与时间轴右键菜单同一条规矩。
    public void RemoveTempo(double atTick)
    {
        ctx.EnsureWritable();
        int index = FindTempo(atTick);
        if (index < 0)
            throw new ScriptApiException(string.Format(CultureInfo.InvariantCulture, "there is no tempo marker at tick {0:0}; see project.tempos().", atTick));
        if (index == 0)
            throw new ScriptApiException("the first tempo marker is the project's base tempo and can't be removed; change it with setTempo instead.");
        ctx.Project.TempoManager.RemoveTempoAt(index);
        ctx.Bump();
    }

    // 删掉第 atBar 小节（1-based）处的拍号标记（setTimeSignature 的对偶）。规矩同 removeTempo。
    public void RemoveTimeSignature(int atBar)
    {
        ctx.EnsureWritable();
        if (atBar < 1) throw new ScriptApiException("atBar must be >= 1.");
        int index = FindTimeSignature(atBar - 1);
        if (index < 0)
            throw new ScriptApiException(string.Format(CultureInfo.InvariantCulture, "there is no time signature marker at bar {0}; see project.timeSignatures().", atBar));
        if (index == 0)
            throw new ScriptApiException("the first time signature is the project's base meter and can't be removed; change it with setTimeSignature instead.");
        ctx.Project.TimeSignatureManager.RemoveTimeSignatureAt(index);
        ctx.Bump();
    }

    // 标记的定址：速度按 tick 就近（半 tick 内即同一处，与 setTempo 的"该处已有则改"同判据），拍号按小节号精确。
    int FindTempo(double tick)
    {
        var tempos = ctx.Project.TempoManager.Tempos;
        for (int i = 0; i < tempos.Count; i++)
            if (Math.Abs(tempos[i].Pos - tick) < 0.5) return i;
        return -1;
    }

    int FindTimeSignature(int barIndex)
    {
        var signatures = ctx.Project.TimeSignatureManager.TimeSignatures;
        for (int i = 0; i < signatures.Count; i++)
            if (signatures[i].BarIndex == barIndex) return i;
        return -1;
    }
}
