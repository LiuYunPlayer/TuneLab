using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

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
