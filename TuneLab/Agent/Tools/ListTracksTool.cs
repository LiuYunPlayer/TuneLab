using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.Agent;

// 只读总览工具：返回工程结构摘要（PPQ、tempo、拍号、各轨 1-based 编号/名/状态/part 数/音符数）。
internal sealed class ListTracksTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "get_project_overview";
    public string Description => "Get an overview of the current project: PPQ (ticks per quarter note), tempo, time signature, and every track with its 1-based number, name, mute/solo, gain/pan, part count and note count. Track/part/note numbers used everywhere are 1-based (track 1 is the first track).";

    // 无参数：空对象 schema。
    public string ParametersJsonSchema => """
        { "type": "object", "properties": {}, "additionalProperties": false }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        return Task.FromResult(editor.GetProjectSummary());
    }
}
