using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TuneLab.Audio;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Agent;

// agent 工具与工程数据之间的薄门面：把"模型可下达的查询/编辑意图"翻译成数据层操作，
// 并把对 TuneLab.Data 具体 API 的依赖收口在这一层——工具与 agent 循环只依赖本接口，
// 数据层重构时只需改实现。
//
// 三条铁律：
//  1) 寻址 1-based。所有 trackNumber/partNumber/noteNumber 都是面向模型的 1-based 序号
//     （"第 1 轨"即首轨，贴合用户认知）；本层在边界处一次性 −1 转 0-based 内部寻址，越界
//     报错也用 1-based 措辞。回灌给模型的文案同样按 1-based 标号，两端自洽。
//  2) 单位用 tick。位置/时长一律 tick（PPQ=480），note 位置相对所属 part；不引入秒。
//  3) 每次写 = 一个可撤销单位。每个写方法在完成后单次 Commit；批量 ApplyEdits 整批一个单位。
internal interface IAgentProjectEditor
{
    // ── 只读：把工程结构/明细格式化成喂给模型的文本 ──
    string GetProjectSummary();
    // 当前在钢琴窗打开编辑的 part（用户说"当前/这个 part"时用来解析其 1-based 轨/part 序号）；未打开则告知。
    string GetCurrentPart();
    // 播放线位置（秒 + tick + 小节:拍 + 是否播放中）；用户说"播放线/这里/当前位置"时用来取 tick。
    string GetPlayhead();
    // 把一个绝对 tick 吸附到当前量化网格（写旋律对齐用；播放线本身不吸附）。
    string SnapTick(double tick);
    string GetTrackDetail(int trackNumber);
    string GetPartNotes(int trackNumber, int partNumber, double? startTick, double? endTick);
    string GetPartParameters(int trackNumber, int partNumber);
    string GetParameterValues(int trackNumber, int partNumber, string parameterId, double startTick, double endTick, int samples);

    // ── 业务级写：各自一个可撤销单位 ──
    int ShiftTrackPitch(int trackNumber, int semitones);
    int TransposeNotes(int trackNumber, int partNumber, int semitones, double? startTick, double? endTick);
    string SetTrackProperties(int trackNumber, string? name, bool? mute, bool? solo, double? gainDb, double? pan);
    int AddTrack(string? name);                 // 返回新轨的 1-based 编号
    void RemoveTrack(int trackNumber);
    string SetTempo(double bpm, double? atTick);
    string SetTimeSignature(int numerator, int denominator, int? atBarNumber);

    // ── 批量 DSL：整批一个可撤销单位 ──
    string ApplyEdits(IReadOnlyList<AgentEditOp> ops);
}

// 绑定到当前工程的 Facade 实现。构造时注入 IProject、"当前编辑 part"访问器与"当前量化"访问器
//（后两者实时读钢琴窗，用户切 part / 改量化即变）。
internal sealed class ProjectAgentEditor(
    IProject project,
    Func<IMidiPart?>? currentPart = null,
    Func<IQuantization?>? quantization = null) : IAgentProjectEditor
{
    const int PPQ = MusicTheory.RESOLUTION;

    // ===================== 只读 =====================

    public string GetCurrentPart()
    {
        var part = currentPart?.Invoke();
        if (part == null)
            return "No part is currently open in the piano editor. Ask the user which track/part to work on, or use get_project_overview.";
        if (FindPartNumbers(part) is not (int t, int p))
            return "A part is open but could not be located in the project.";
        return "Current part open in the piano editor:\n" + GetPartHeader(t, p, part);
    }

    public string SnapTick(double tick)
    {
        var q = quantization?.Invoke();
        if (q == null)
            return "Snapping is unavailable (no piano editor grid). Compute grid positions from the PPQ instead.";
        int cell = q.TicksPerCell();
        if (cell <= 0)
            return "Snapping is unavailable for the current quantization.";
        double snapped = Math.Round(tick / cell) * cell;
        return string.Format(CultureInfo.InvariantCulture,
            "Snapped tick {0:0.##} -> {1:0} (grid cell = {2} ticks; quantization base={3}, division={4}).",
            tick, snapped, cell, (int)q.Base, (int)q.Division);
    }

    public string GetPlayhead()
    {
        double sec = AudioEngine.CurrentTime;
        double tick = project.TempoManager.GetTick(sec);
        var (bar, beat) = project.TimeSignatureManager.GetBarAndBeatIndexForTick(tick);
        return string.Format(CultureInfo.InvariantCulture,
            "Playhead: tick={0:0}, time={1:0.###}s, bar {2}:{3:0.##} (1-based bar), playing={4}.",
            tick, sec, bar + 1, beat + 1, AudioEngine.IsPlaying);
    }

    public string GetProjectSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "Project: PPQ={0} (ticks per quarter note). Positions/durations are in ticks.", PPQ));

        var tempos = project.TempoManager.Tempos;
        if (tempos.Count > 0)
        {
            sb.Append("Tempo: ");
            sb.Append(string.Join(", ", tempos.Select(t => string.Format(CultureInfo.InvariantCulture,
                "{0:0.##}bpm@tick{1:0}", t.Bpm, t.Pos))));
            sb.AppendLine();
        }
        var timeSigs = project.TimeSignatureManager.TimeSignatures;
        if (timeSigs.Count > 0)
        {
            sb.Append("Time signature: ");
            sb.Append(string.Join(", ", timeSigs.Select(s => string.Format(CultureInfo.InvariantCulture,
                "{0}/{1}@bar{2}", s.Numerator, s.Denominator, s.BarIndex + 1))));
            sb.AppendLine();
        }

        var tracks = project.Tracks;
        sb.AppendLine(string.Format("Tracks ({0}):", tracks.Count));
        for (int i = 0; i < tracks.Count; i++)
        {
            var track = tracks[i];
            var parts = track.Parts.ToList();
            int noteCount = parts.OfType<IMidiPart>().Sum(p => p.Notes.Count());
            var flags = new List<string>();
            if (track.IsMute.Value) flags.Add("mute");
            if (track.IsSolo.Value) flags.Add("solo");
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  Track {0}: \"{1}\"{2}, gain={3:0.#}dB, pan={4:0.##}, parts={5}, notes={6}",
                i + 1, track.Name.Value, flags.Count > 0 ? " [" + string.Join(",", flags) + "]" : "",
                track.Gain.Value, track.Pan.Value, parts.Count, noteCount));
        }
        return sb.ToString();
    }

    public string GetTrackDetail(int trackNumber)
    {
        var track = ResolveTrack(trackNumber);
        var sb = new StringBuilder();
        var flags = new List<string>();
        if (track.IsMute.Value) flags.Add("mute");
        if (track.IsSolo.Value) flags.Add("solo");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "Track {0}: \"{1}\"{2}, gain={3:0.#}dB, pan={4:0.##}",
            trackNumber, track.Name.Value, flags.Count > 0 ? " [" + string.Join(",", flags) + "]" : "",
            track.Gain.Value, track.Pan.Value));

        var parts = track.Parts.ToList();
        sb.AppendLine(string.Format("Parts ({0}):", parts.Count));
        for (int i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            double start = part.Pos.Value;
            double end = start + part.Dur.Value;
            if (part is IMidiPart midi)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  Part {0}: midi \"{1}\", ticks [{2:0}..{3:0}], voice=\"{4}\", notes={5}",
                    i + 1, part.Name.Value, start, end, midi.Voice.Name, midi.Notes.Count()));
            }
            else
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "  Part {0}: audio \"{1}\", ticks [{2:0}..{3:0}]",
                    i + 1, part.Name.Value, start, end));
            }
        }
        return sb.ToString();
    }

    // 单次回灌的音符上限，超出截断并提示，避免淹没上下文。
    const int MaxNotesListed = 500;

    public string GetPartNotes(int trackNumber, int partNumber, double? startTick, double? endTick)
    {
        var part = ResolveMidiPart(trackNumber, partNumber);
        double partPos = part.Pos.Value;
        double lo = startTick ?? double.NegativeInfinity;
        double hi = endTick ?? double.PositiveInfinity;

        var sb = new StringBuilder();
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "Track {0} Part {1} notes (pos is an absolute tick; part spans [{2:0}..{3:0}]; NoteNumber is 1-based):",
            trackNumber, partNumber, partPos, partPos + part.Dur.Value));

        int number = 0;     // 1-based 编号按 part 内完整音符序，过滤不改变编号
        int listed = 0;
        bool truncated = false;
        foreach (var note in part.Notes)
        {
            number++;
            double pos = note.Pos.Value + partPos;   // 绝对 tick
            if (pos < lo || pos >= hi)
                continue;
            if (listed >= MaxNotesListed)
            {
                truncated = true;
                break;
            }
            listed++;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  Note {0}: pos={1:0}, dur={2:0}, pitch={3}({4}), lyric=\"{5}\"",
                number, pos, note.Dur.Value, note.Pitch.Value, PitchName(note.Pitch.Value), note.Lyric.Value));
        }
        if (listed == 0)
            sb.AppendLine("  (no notes in range)");
        if (truncated)
            sb.AppendLine(string.Format("  ... (truncated at {0} notes; narrow the tick range to see more)", MaxNotesListed));
        return sb.ToString();
    }

    public string GetPartParameters(int trackNumber, int partNumber)
    {
        var part = ResolveMidiPart(trackNumber, partNumber);
        var sb = new StringBuilder();
        sb.AppendLine(string.Format(
            "Track {0} Part {1} editable parameters (use these ids with get_parameter and apply_edits set_automation_line / clear_automation):",
            trackNumber, partNumber));
        sb.AppendLine("  pitch — final pitch curve in MIDI scale; edit via apply_edits set_pitch_line / clear_pitch.");
        // voice 级（已含宿主自带 Volume / VibratoEnvelope + 引擎声明的条件轨）。
        foreach (var kvp in part.Voice.AutomationConfigs)
        {
            var c = kvp.Value;
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  {0} — \"{1}\", range [{2:0.###}..{3:0.###}], default {4:0.###}{5}",
                kvp.Key, c.DisplayText ?? kvp.Key, c.MinValue, c.MaxValue, c.DefaultValue,
                part.Automations.ContainsKey(kvp.Key) ? ", has curve" : ""));
        }
        // effect 级参数轨当前不可经 agent 工具编辑（已知限制），仅列出告知存在。
        for (int i = 0; i < part.Effects.Count; i++)
        {
            var eff = part.Effects[i];
            if (eff.AutomationConfigs.Count == 0)
                continue;
            sb.AppendLine(string.Format("  (effect {0} \"{1}\" parameters — not editable by agent tools yet):", i + 1, eff.Type));
            foreach (var kvp in eff.AutomationConfigs)
                sb.AppendLine(string.Format("    {0} — \"{1}\"", kvp.Key, kvp.Value.DisplayText ?? kvp.Key));
        }
        return sb.ToString();
    }

    public string GetParameterValues(int trackNumber, int partNumber, string parameterId, double startTick, double endTick, int samples)
    {
        var part = ResolveMidiPart(trackNumber, partNumber);
        double partPos = part.Pos.Value;
        if (samples < 2) samples = 2;
        if (samples > 200) samples = 200;
        if (endTick <= startTick)
            throw new ArgumentException("endTick must be greater than startTick.");

        var ticks = new double[samples];            // 绝对 tick（用于回显）
        var sampleTicks = new double[samples];      // part 相对 tick（曲线采样用）
        double step = (endTick - startTick) / (samples - 1);
        for (int i = 0; i < samples; i++)
        {
            ticks[i] = startTick + step * i;
            sampleTicks[i] = ticks[i] - partPos;
        }

        double[] values;
        bool isPitch = string.Equals(parameterId, "pitch", StringComparison.OrdinalIgnoreCase);
        if (isPitch)
            values = part.GetFinalPitch(sampleTicks);
        else if (part.IsEffectiveAutomation(parameterId))
            values = part.GetAutomationValues(sampleTicks, parameterId);
        else
            throw new ArgumentException(string.Format(
                "Unknown parameter \"{0}\" for this part. Use \"pitch\", or call get_part_parameters to list available automation ids.", parameterId));

        var sb = new StringBuilder();
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "Parameter \"{0}\" sampled over ticks [{1:0}..{2:0}] ({3} samples). NaN = no curve (falls back to note pitch).",
            parameterId, startTick, endTick, samples));
        for (int i = 0; i < samples; i++)
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "  tick {0:0}: {1:0.###}", ticks[i], values[i]));
        return sb.ToString();
    }

    // ===================== 业务级写 =====================

    public int ShiftTrackPitch(int trackNumber, int semitones)
    {
        var track = ResolveTrack(trackNumber);
        int changed = 0;
        foreach (var part in track.Parts.OfType<IMidiPart>())
        {
            part.BeginMergeDirty();
            part.Notes.BeginMergeNotify();
            foreach (var note in part.Notes)
            {
                int target = Math.Clamp(note.Pitch.Value + semitones, MusicTheory.MIN_PITCH, MusicTheory.MAX_PITCH);
                if (target != note.Pitch.Value)
                {
                    note.Pitch.Set(target);
                    changed++;
                }
            }
            part.Notes.EndMergeNotify();
            part.EndMergeDirty();
        }
        if (changed > 0)
            project.Commit();
        return changed;
    }

    public int TransposeNotes(int trackNumber, int partNumber, int semitones, double? startTick, double? endTick)
    {
        var part = ResolveMidiPart(trackNumber, partNumber);
        double partPos = part.Pos.Value;
        double lo = startTick ?? double.NegativeInfinity;   // 绝对 tick
        double hi = endTick ?? double.PositiveInfinity;
        int changed = 0;
        part.BeginMergeDirty();
        part.Notes.BeginMergeNotify();
        foreach (var note in part.Notes)
        {
            double abs = note.Pos.Value + partPos;
            if (abs < lo || abs >= hi)
                continue;
            int target = Math.Clamp(note.Pitch.Value + semitones, MusicTheory.MIN_PITCH, MusicTheory.MAX_PITCH);
            if (target != note.Pitch.Value)
            {
                note.Pitch.Set(target);   // 改音高不影响排序，无需摘除-重插
                changed++;
            }
        }
        part.Notes.EndMergeNotify();
        part.EndMergeDirty();
        if (changed > 0)
            project.Commit();
        return changed;
    }

    public string SetTrackProperties(int trackNumber, string? name, bool? mute, bool? solo, double? gainDb, double? pan)
    {
        var track = ResolveTrack(trackNumber);
        var applied = new List<string>();
        if (name != null && track.Name.Value != name) { track.Name.Set(name); applied.Add("name=\"" + name + "\""); }
        if (mute is { } m && track.IsMute.Value != m) { track.IsMute.Set(m); applied.Add("mute=" + m); }
        if (solo is { } s && track.IsSolo.Value != s) { track.IsSolo.Set(s); applied.Add("solo=" + s); }
        if (gainDb is { } g && track.Gain.Value != g) { track.Gain.Set(g); applied.Add(string.Format(CultureInfo.InvariantCulture, "gain={0:0.#}dB", g)); }
        if (pan is { } p && track.Pan.Value != Math.Clamp(p, -1, 1)) { track.Pan.Set(Math.Clamp(p, -1, 1)); applied.Add(string.Format(CultureInfo.InvariantCulture, "pan={0:0.##}", Math.Clamp(p, -1, 1))); }

        if (applied.Count == 0)
            return string.Format("Track {0}: nothing changed.", trackNumber);
        project.Commit();
        return string.Format("Track {0}: set {1}.", trackNumber, string.Join(", ", applied));
    }

    public int AddTrack(string? name)
    {
        project.AddTrack(new TrackInfo { Name = name ?? "Track" });
        project.Commit();
        return project.Tracks.Count;   // 1-based 编号 = 新数量
    }

    public void RemoveTrack(int trackNumber)
    {
        ResolveTrack(trackNumber);   // 越界校验（1-based 报错）
        project.RemoveTrackAt(trackNumber - 1);
        project.Commit();
    }

    public string SetTempo(double bpm, double? atTick)
    {
        if (bpm <= 0) throw new ArgumentException("bpm must be positive.");
        var manager = project.TempoManager;
        double tick = atTick ?? 0;
        // 该 tick 已有 tempo 标记则改，否则新增。
        int existing = -1;
        for (int i = 0; i < manager.Tempos.Count; i++)
            if (Math.Abs(manager.Tempos[i].Pos - tick) < 0.5) { existing = i; break; }

        string verb;
        if (existing >= 0) { manager.SetBpm(existing, bpm); verb = "set"; }
        else { manager.AddTempo(tick, bpm); verb = "added"; }
        project.Commit();
        return string.Format(CultureInfo.InvariantCulture, "Tempo {0}: {1:0.##}bpm at tick {2:0}.", verb, bpm, tick);
    }

    public string SetTimeSignature(int numerator, int denominator, int? atBarNumber)
    {
        if (numerator < 1 || denominator < 1) throw new ArgumentException("numerator/denominator must be >= 1.");
        int barIndex = (atBarNumber ?? 1) - 1;   // 1-based bar → 0-based index
        if (barIndex < 0) throw new ArgumentException("atBarNumber must be >= 1.");
        var manager = project.TimeSignatureManager;
        int existing = -1;
        for (int i = 0; i < manager.TimeSignatures.Count; i++)
            if (manager.TimeSignatures[i].BarIndex == barIndex) { existing = i; break; }

        string verb;
        if (existing >= 0) { manager.SetMeter(existing, numerator, denominator); verb = "set"; }
        else { manager.AddTimeSignature(barIndex, numerator, denominator); verb = "added"; }
        project.Commit();
        return string.Format("Time signature {0}: {1}/{2} at bar {3}.", verb, numerator, denominator, barIndex + 1);
    }

    // ===================== 批量 DSL =====================

    public string ApplyEdits(IReadOnlyList<AgentEditOp> ops)
    {
        if (ops.Count == 0)
            return "No edits supplied.";

        // 1) 先把每个 op 解析到目标 part（失败的记错、跳过）。
        var resolved = new List<(AgentEditOp op, IMidiPart? part, string? error)>(ops.Count);
        var affected = new List<IMidiPart>();        // 去重的受影响 part（保持插入序）
        var affectedSet = new HashSet<IMidiPart>();
        foreach (var op in ops)
        {
            try
            {
                var part = ResolveMidiPart(op.TrackNumber, op.PartNumber);
                resolved.Add((op, part, null));
                if (affectedSet.Add(part))
                    affected.Add(part);
            }
            catch (Exception ex)
            {
                resolved.Add((op, null, ex.Message));
            }
        }

        // 2) 对受影响 part 做"批开始时"的音符顺序快照（按对象引用解析 NoteNumber，免受批内增删扰动）。
        var snapshots = affected.ToDictionary(p => p, p => p.Notes.ToList());

        // 3) 开括号 → 顺序施加 → 收括号 → 单次 Commit。
        foreach (var part in affected) { part.BeginMergeDirty(); part.Notes.BeginMergeNotify(); }

        int ok = 0;
        var lines = new List<string>(resolved.Count);
        bool anyChange = false;
        for (int i = 0; i < resolved.Count; i++)
        {
            var (op, part, error) = resolved[i];
            if (part == null)
            {
                lines.Add(string.Format("  [{0}] {1} — ERROR: {2}", i + 1, op.GetType().Name, error));
                continue;
            }
            try
            {
                string detail = ApplyOne(op, part, snapshots[part]);
                anyChange = true;
                ok++;
                lines.Add(string.Format("  [{0}] OK: {1}", i + 1, detail));
            }
            catch (Exception ex)
            {
                lines.Add(string.Format("  [{0}] {1} — ERROR: {2}", i + 1, op.GetType().Name, ex.Message));
            }
        }

        foreach (var part in affected) { part.Notes.EndMergeNotify(); part.EndMergeDirty(); }
        if (anyChange)
            project.Commit();

        var sb = new StringBuilder();
        sb.AppendLine(string.Format("Applied {0}/{1} edit(s) as one undoable change:", ok, resolved.Count));
        foreach (var line in lines)
            sb.AppendLine(line);
        return sb.ToString();
    }

    // 单个 op 的施加；返回供回灌的简短描述。snapshot 为该 part 批开始时的音符序。
    // 模型侧所有 tick 都是绝对（全局）tick；这里减去 part 起点转成数据层用的 part 相对坐标。
    string ApplyOne(AgentEditOp op, IMidiPart part, List<INote> snapshot)
    {
        double partPos = part.Pos.Value;
        double Rel(double absTick) => absTick - partPos;   // 绝对 → part 相对

        switch (op)
        {
            case AddNoteOp a:
            {
                int pitch = Math.Clamp(a.Pitch, MusicTheory.MIN_PITCH, MusicTheory.MAX_PITCH);
                var note = part.CreateNote(new NoteInfo { Pos = Rel(a.Pos), Dur = a.Dur, Pitch = pitch, Lyric = a.Lyric ?? string.Empty });
                part.InsertNote(note);
                return string.Format(CultureInfo.InvariantCulture,
                    "add note at tick={0:0} dur={1:0} pitch={2}({3}) lyric=\"{4}\"", a.Pos, a.Dur, pitch, PitchName(pitch), a.Lyric);
            }
            case SetNoteOp s:
            {
                var note = ResolveNote(snapshot, s.NoteNumber);
                double? newRelPos = s.Pos is { } np ? Rel(np) : null;
                bool reorder = (newRelPos is { } rp && rp != note.Pos.Value) || (s.Dur is { } nd && nd != note.Dur.Value);
                if (reorder) part.RemoveNote(note);
                if (newRelPos is { } pos) note.Pos.Set(pos);
                if (s.Dur is { } dur) note.Dur.Set(Math.Max(0, dur));
                if (s.Pitch is { } pit) note.Pitch.Set(Math.Clamp(pit, MusicTheory.MIN_PITCH, MusicTheory.MAX_PITCH));
                if (s.Lyric != null) note.Lyric.Set(s.Lyric);
                if (reorder) part.InsertNote(note);
                return string.Format("set note {0}", s.NoteNumber);
            }
            case DeleteNoteOp d:
            {
                var note = ResolveNote(snapshot, d.NoteNumber);
                part.RemoveNote(note);
                return string.Format("delete note {0}", d.NoteNumber);
            }
            case DeleteNotesInRangeOp dr:
            {
                int n = 0;
                foreach (var note in part.AllNotesInSelection(Rel(dr.Start), Rel(dr.End)))
                {
                    part.RemoveNote(note);
                    n++;
                }
                return string.Format(CultureInfo.InvariantCulture, "delete {0} note(s) in ticks [{1:0}..{2:0})", n, dr.Start, dr.End);
            }
            case SetPitchLineOp sp:
            {
                part.Pitch.Clear(Rel(sp.Start), Rel(sp.End));
                if (sp.Points.Count > 0)
                    part.Pitch.AddLine(sp.Points.OrderBy(p => p.X).Select(p => new AnchorPoint(Rel(p.X), p.Y)).ToList(), 0);
                return string.Format(CultureInfo.InvariantCulture, "set pitch line over ticks [{0:0}..{1:0}], {2} point(s)", sp.Start, sp.End, sp.Points.Count);
            }
            case ClearPitchOp cp:
            {
                part.Pitch.Clear(Rel(cp.Start), Rel(cp.End));
                return string.Format(CultureInfo.InvariantCulture, "clear pitch over ticks [{0:0}..{1:0})", cp.Start, cp.End);
            }
            case SetAutomationLineOp sa:
            {
                var automation = GetOrAddAutomation(part, sa.AutomationId);
                if (sa.DefaultValue is { } dv) automation.DefaultValue.Set(dv);
                automation.Clear(Rel(sa.Start), Rel(sa.End), 0);
                if (sa.Points.Count > 0)
                    automation.AddLine(sa.Points.OrderBy(p => p.X).Select(p => new AnchorPoint(Rel(p.X), p.Y)).ToList(), 0);
                return string.Format(CultureInfo.InvariantCulture, "set automation \"{0}\" over ticks [{1:0}..{2:0}], {3} point(s)", sa.AutomationId, sa.Start, sa.End, sa.Points.Count);
            }
            case ClearAutomationOp ca:
            {
                if (part.Automations.TryGetValue(ca.AutomationId, out var automation))
                    automation.Clear(Rel(ca.Start), Rel(ca.End), 0);
                return string.Format(CultureInfo.InvariantCulture, "clear automation \"{0}\" over ticks [{1:0}..{2:0})", ca.AutomationId, ca.Start, ca.End);
            }
            default:
                throw new ArgumentException("Unsupported edit op: " + op.GetType().Name);
        }
    }

    // ===================== 解析助手（1-based 边界） =====================

    ITrack ResolveTrack(int trackNumber)
    {
        var tracks = project.Tracks;
        if (trackNumber < 1 || trackNumber > tracks.Count)
            throw new ArgumentOutOfRangeException(nameof(trackNumber),
                string.Format("track number {0} out of range [1,{1}].", trackNumber, tracks.Count));
        return tracks[trackNumber - 1];
    }

    IMidiPart ResolveMidiPart(int trackNumber, int partNumber)
    {
        var track = ResolveTrack(trackNumber);
        var parts = track.Parts.ToList();
        if (partNumber < 1 || partNumber > parts.Count)
            throw new ArgumentOutOfRangeException(nameof(partNumber),
                string.Format("part number {0} out of range [1,{1}] on track {2}.", partNumber, parts.Count, trackNumber));
        if (parts[partNumber - 1] is not IMidiPart midi)
            throw new ArgumentException(string.Format("part {0} on track {1} is not a midi part.", partNumber, trackNumber));
        return midi;
    }

    static INote ResolveNote(List<INote> snapshot, int noteNumber)
    {
        if (noteNumber < 1 || noteNumber > snapshot.Count)
            throw new ArgumentOutOfRangeException(nameof(noteNumber),
                string.Format("note number {0} out of range [1,{1}].", noteNumber, snapshot.Count));
        return snapshot[noteNumber - 1];
    }

    IAutomation GetOrAddAutomation(IMidiPart part, string automationId)
    {
        if (part.Automations.TryGetValue(automationId, out var existing))
            return existing;
        var created = part.AddAutomation(automationId);
        if (created == null)
            throw new ArgumentException(string.Format(
                "automation \"{0}\" is not available on this part (not declared by its voice/effect).", automationId));
        return created;
    }

    // 在工程内按对象引用定位某 part 的 1-based (轨号, part号)；找不到返回 null。
    (int track, int part)? FindPartNumbers(IMidiPart target)
    {
        var tracks = project.Tracks;
        for (int ti = 0; ti < tracks.Count; ti++)
        {
            var parts = tracks[ti].Parts.ToList();
            for (int pi = 0; pi < parts.Count; pi++)
                if (ReferenceEquals(parts[pi], target))
                    return (ti + 1, pi + 1);
        }
        return null;
    }

    static string GetPartHeader(int trackNumber, int partNumber, IMidiPart part)
    {
        return string.Format(CultureInfo.InvariantCulture,
            "  Track {0} Part {1}: \"{2}\", ticks [{3:0}..{4:0}], voice=\"{5}\", notes={6}",
            trackNumber, partNumber, part.Name.Value, part.Pos.Value, part.Pos.Value + part.Dur.Value,
            part.Voice.Name, part.Notes.Count());
    }

    static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    static string PitchName(int midi)
    {
        int rel = midi - MusicTheory.C0_PITCH;   // C0 = MIDI 12
        int octave = (int)Math.Floor(rel / 12.0);
        int idx = ((rel % 12) + 12) % 12;
        return NoteNames[idx] + octave.ToString(CultureInfo.InvariantCulture);
    }
}
