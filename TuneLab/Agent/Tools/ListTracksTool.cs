using System.Threading;
using System.Threading.Tasks;

namespace TuneLab.Agent;

// 代表性只读工具：返回工程结构摘要。用来验证"模型主动调用工具读取上下文"这条链路。
internal sealed class ListTracksTool(IAgentProjectEditor editor) : IAgentTool
{
    public string Name => "list_tracks";
    public string Description => "List all tracks in the current project with their name, part count and note count.";

    // 无参数：空对象 schema。
    public string ParametersJsonSchema => """
        { "type": "object", "properties": {}, "additionalProperties": false }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
    {
        return Task.FromResult(editor.GetProjectSummary());
    }
}
