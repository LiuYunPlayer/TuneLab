using System.Threading;
using System.Threading.Tasks;
using TuneLab.Scripting;

namespace TuneLab.Agent;

// run_script 的「按需文档」工具（渐进式披露）：run_script 的描述只留一句引导，完整 API/规则/示例放这里，
// 模型决定要写脚本时调一次取回——避免把整份速查表常驻每次请求的 prompt。返回的就是脚本模块的权威参考常量。
internal sealed class GetScriptApiTool : IAgentTool
{
    public string Name => "get_script_api";

    public string Description =>
        "Return the full API reference for run_script (the `tl` action surface): every method with signatures, the handle/tick/commit rules, " +
        "handle fields, and worked examples. Call this once before writing your first run_script program in a conversation.";

    public string ParametersJsonSchema => """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)
        => Task.FromResult(ScriptApiReference.Text);
}
