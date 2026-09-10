using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using TuneLab.Data;

namespace TuneLab.Commands.Handlers;

// 编辑器此刻的状态：在播吗、播到哪、拿着哪支笔、参数面板开着没、键盘焦点在哪个编辑面、钢琴窗里开着哪个 part。
//
// 【为什么必须有这一条】动作面里有一半是即发即忘的（`transport.play` 是切换而不是"开始播"，`tool.*` 是选择），
// 没有配套的状态读，调用方就只能靠猜——那个动作面是瞎的。
//
// 三条命令的分工，刻意不互相重叠：
//  · `app info`      —— 命令跑在**哪一份安装**里（版本、数据目录、日志、有没有编辑器）；
//  · `project status` —— 用户的**文档**（曲速、拍号、轨道、音符数）；
//  · `editor status`  —— 编辑器**此刻的界面状态**（本条）。不随工程保存、撤销栈里也没有它。
internal sealed class EditorStatusCommand : ICommand
{
    public string Path => "editor status";
    public CommandKind Kind => CommandKind.Read;
    public string AgentToolName => "get_editor_status";

    public string Brief => "What the editor is doing right now";

    public string Documentation =>
        "What TuneLab's editor is doing right now: whether it is playing and where the playhead is (seconds and ticks), which tool is selected, whether the parameter panel is open, which part is open in the piano roll and what the snap grid is, and which edit surface has keyboard focus. "
        + "\nCall it after run_action when you need to know the result of a toggle (play/pause), and before the clipboard verbs — those act on whatever is selected in the FOCUSED surface, so if nothing has focus they have nothing to act on. "
        + "\nNone of this is project data: it is not saved with the project and undo does not affect it. For the document itself use get_project_overview, and for the actions that change this state use list_actions. Read-only.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """;

    // 全部字段都是实时界面状态 → 在主线程取一致快照（半段在 UI 线程、半段不在会给出自相矛盾的一份状态）。
    public async Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
        => CommandResult.Ok(await ctx.OnMainThread(() => (JsonNode)EditorStatusText.Build(ctx)));

    // 取事实与渲染都在 EditorStatusText：`action run` 的回报用的是同一份，故两处说的话不可能分裂。
    public string Render(JsonNode? data, CommandArgs args)
        => data is JsonObject obj ? EditorStatusText.Render(obj) : string.Empty;
}

// 把用户的视野挪到某处去——「让他看到我说的是哪里」。
//
// 【翻案的边界】§11 否掉的是模拟滚轮缩放：以鼠标为轴心的连续手势，外部没有鼠标，且不改变任何结果。
// 它同时留下了一句话——真正有用的那件事是"让用户看到我改了哪里"，它要一个 tick 或一个对象作参数。
// 这条命令就是那件事。缩放在这里只作为**后果**出现：目标装不下才缩小，且永不替用户放大。
//
// 【为什么是命令不是带参动作】§5.6 自己写着"滚到某个 tick 要的是一个连续量、不是闭集里的一个成员"；
// 而带必填参数的动作不可绑手势，扩了 ActionParameter 也还是只能从命令面调——绕一圈回到原地。
//
// 【为什么不过闸门】它只改应用自身此刻显示什么，不碰工程数据、不动播放头、撤销栈里也没有它——
// 同 ActionKind.AppState 那一档的理由。唯一有后果的是 open（切钢琴窗的 part，改的是用户的编辑目标），
// 故它默认关着、要显式要。
internal sealed class EditorRevealCommand : ICommand
{
    public string Path => "editor reveal";
    public CommandKind Kind => CommandKind.Edit;
    public string AgentToolName => "reveal_in_editor";

    public string Brief => "Scroll the editor so the user can see a place you name";

    public string Documentation =>
        "Bring a place in the project into view, so the user can SEE what you are talking about — you found the passage they asked for, or you are pointing at something that looks wrong. "
        + "Both the arrangement and the piano roll scroll to it; if the range is wider than the view, they zoom OUT far enough to fit it, and they never zoom in (yanking someone's zoom onto one note leaves them lost). "
        + "\nSay where with any of: track (1-based, same numbering as get_project_overview), part (1-based within that track), startTick/endTick. Giving a part with no ticks reveals that whole part; giving ticks narrows it to that range. Positions are ticks — get PPQ from get_project_overview. "
        + "\nThis changes only what is on screen: no project data, no playhead, nothing in the undo history. The one exception is opening a part: if the part you name is not the one in the piano roll, this scrolls the arrangement to it and TELLS you the piano roll is showing something else — pass open: true to switch the piano roll to it, which changes what the user is editing. "
        + "\nThe reply says what is actually visible afterwards, so you can tell whether the whole range fit.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {
            "track": { "type": "integer", "description": "1-based track number, as reported by get_project_overview." },
            "part": { "type": "integer", "description": "1-based part number within that track. Requires \"track\"." },
            "startTick": { "type": "number", "description": "Start of the range to reveal, in ticks. Without \"endTick\" this is a single point." },
            "endTick": { "type": "number", "description": "End of the range to reveal, in ticks." },
            "open": { "type": "boolean", "description": "Switch the piano roll to the named part if it is not already open there. Default false, because that changes what the user is editing." }
          },
          "additionalProperties": false
        }
        """;

    public Task<CommandResult> ExecuteAsync(CommandArgs args, CommandContext ctx, CancellationToken cancellationToken)
    {
        if (ctx.EditorView is not { } view)
            return Task.FromResult(CommandResult.Fail("no_editor",
                "There is no editor in this process, so there is no view to move. " + EditorStatusText.NoEditor));
        if (ctx.Project is not { } project)
            return Task.FromResult(CommandResult.Fail("no_project", "no project is open, so there is nothing to reveal."));

        int? trackNumber = args.Json.GetIntOrNull("track");
        int? partNumber = args.Json.GetIntOrNull("part");
        double? startTick = args.Json.GetDoubleOrNull("startTick");
        double? endTick = args.Json.GetDoubleOrNull("endTick");
        bool open = args.Json.GetBoolOrNull("open") ?? false;

        if (partNumber != null && trackNumber == null)
            return Task.FromResult(CommandResult.Fail("part_needs_track",
                "\"part\" is numbered within a track, so give \"track\" too (both 1-based, as get_project_overview reports them)."));
        if (trackNumber == null && startTick == null && endTick == null)
            return Task.FromResult(CommandResult.Fail("nothing_named",
                "say where to look: a \"track\" (optionally with a \"part\"), a tick range (\"startTick\"/\"endTick\"), or both."));

        return ctx.OnMainThread(() => Reveal(project, view, trackNumber, partNumber, startTick, endTick, open));
    }

    static CommandResult Reveal(IProject project, IEditorViewAccess view,
        int? trackNumber, int? partNumber, double? startTick, double? endTick, bool open)
    {
        ITrack? track = null;
        if (trackNumber is { } number)
        {
            if (number < 1 || number > project.Tracks.Count)
                return CommandResult.Fail("no_such_track", string.Format(
                    "there is no track {0} — this project has {1}. Tracks are numbered from 1.", number, project.Tracks.Count));
            track = project.Tracks[number - 1];
        }

        IPart? part = null;
        if (partNumber is { } index)
        {
            var parts = track!.Parts.ToList();
            if (index < 1 || index > parts.Count)
                return CommandResult.Fail("no_such_part", string.Format(
                    "track {0} has no part {1} — it has {2}. Parts are numbered from 1, in the order get_project_overview lists them.",
                    trackNumber, index, parts.Count));
            part = parts[index - 1];
        }

        // 范围：显式的 tick 优先（它更具体），否则用 part 自己的两端。只给 startTick 就是一个点。
        double? rangeStart = startTick ?? endTick;
        double? rangeEnd = endTick ?? startTick;
        if (rangeStart == null && part != null)
        {
            rangeStart = part.StartPos();
            rangeEnd = part.EndPos();
        }

        // open 要先做：切过去之后音高轴才是这个 part 的，否则会把上一个 part 的音域挪进视野。
        bool opened = false;
        if (open && part != null && !ReferenceEquals(view.EditingPart, part))
        {
            view.OpenPart(part);
            opened = true;
        }

        (double Start, double End)? visible = null;
        if (rangeStart is { } start && rangeEnd is { } end)
            visible = view.RevealTicks(start, end);
        if (trackNumber is { } row)
            view.RevealTrack(row);

        // 音高只在钢琴窗真开着目标 part 时才动——挪一个看不见的轴既没用，又会在用户切回来时发现视野被人动过。
        (int Low, int High)? pitches = null;
        if (part is IMidiPart midiPart && ReferenceEquals(view.EditingPart, part))
        {
            pitches = PitchSpan(midiPart, rangeStart, rangeEnd);
            if (pitches is { } span)
                view.RevealPitches(span.Low, span.High);
        }

        var editing = view.EditingPart;
        return CommandResult.Ok(new JsonObject
        {
            ["track"] = trackNumber,
            ["trackName"] = track?.Name.Value,
            ["part"] = partNumber,
            ["partName"] = part?.Name.Value,
            ["startTick"] = rangeStart,
            ["endTick"] = rangeEnd,
            // 没给 tick 范围时（只挪了轨道）就没有"时间轴挪到哪"这回事，故为 null 而不是编一个数。
            ["visibleStartTick"] = visible is { } v1 ? Math.Round(v1.Start, 1) : null,
            ["visibleEndTick"] = visible is { } v2 ? Math.Round(v2.End, 1) : null,
            ["openedPart"] = opened,
            // part 指名了、但钢琴窗开着的不是它：调用方据此知道"音符那一半没给用户看到"，且下一步怎么办。
            ["pianoShowsAnotherPart"] = part != null && !ReferenceEquals(editing, part),
            ["editingPartName"] = editing?.Name.Value,
            ["lowPitch"] = pitches?.Low,
            ["highPitch"] = pitches?.High,
        });
    }

    // 该范围内音符的音域；范围内没有音符就退回整个 part 的音域（用户要看的是"这一带"，
    // 而不是"这一带恰好有没有音符"）；一个音符都没有则不动音高轴。
    static (int Low, int High)? PitchSpan(IMidiPart part, double? rangeStart, double? rangeEnd)
    {
        var span = Span(part, rangeStart, rangeEnd);
        return span ?? Span(part, null, null);

        static (int Low, int High)? Span(IMidiPart part, double? start, double? end)
        {
            int low = int.MaxValue, high = int.MinValue;
            foreach (var note in part.Notes)
            {
                if (start != null && note.GlobalEndPos() < start)
                    continue;
                if (end != null && note.GlobalStartPos() > end)
                    continue;
                low = Math.Min(low, note.Pitch.Value);
                high = Math.Max(high, note.Pitch.Value);
            }
            return low > high ? null : (low, high);
        }
    }

    public string Render(JsonNode? data, CommandArgs args)
    {
        if (data is not JsonObject obj)
            return string.Empty;

        var text = new StringBuilder("Moved the view to ");
        if (obj["part"] is { } partNumber)
            text.AppendFormat("track {0} part {1}{2}", obj["track"]!.GetValue<int>(), partNumber.GetValue<int>(), Named(obj["partName"]));
        else if (obj["track"] is { } trackNumber)
            text.AppendFormat("track {0}{1}", trackNumber.GetValue<int>(), Named(obj["trackName"]));
        else
            text.Append("that range");

        if (obj["startTick"] is { } start && obj["endTick"] is { } end)
        {
            double from = start.GetValue<double>(), to = end.GetValue<double>();
            text.Append(from == to ? string.Format(", at tick {0:0.#}", from) : string.Format(", ticks {0:0.#}–{1:0.#}", from, to));
        }
        text.Append('.');

        if (obj["visibleStartTick"] is { } visibleStart)
            text.AppendFormat(" The arrangement now shows ticks {0:0.#}–{1:0.#}",
                visibleStart.GetValue<double>(), obj["visibleEndTick"]!.GetValue<double>());
        else
            text.Append(" The arrangement scrolled to that track");
        if (obj["lowPitch"] is { } low)
            text.AppendFormat(", and the piano roll is on pitches {0}–{1}", low.GetValue<int>(), obj["highPitch"]!.GetValue<int>());
        text.Append('.');

        if (obj["openedPart"]!.GetValue<bool>())
            text.Append(" The piano roll was switched to that part, so that is what the user is editing now.");
        else if (obj["pianoShowsAnotherPart"]!.GetValue<bool>())
            text.AppendFormat(
                " The piano roll is still showing {0}, so the notes in the part you named are not on screen — call this again with open: true to switch it there (that changes what the user is editing).",
                obj["editingPartName"] is { } name && name.GetValue<string>().Length > 0
                    ? "\"" + name.GetValue<string>() + "\"" : "another part (or nothing)");

        return text.ToString();
    }

    static string Named(JsonNode? name)
        => name is null || name.GetValue<string>().Length == 0 ? string.Empty : string.Format(" (\"{0}\")", name.GetValue<string>());
}
