using System;
using System.Text;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.Scripting;
using TuneLab.SDK;

namespace TuneLab.Commands;

// `script inputs`（读）与 run_saved_script（写）共用的小助手：稳定 id 解析 + 入参 schema 文本化。
// 两边必须给出同一份字段口径——读那边告诉调用方"有哪些参数、上次填了什么"，写那边照着收参数，
// 一处多认一个字段名就会让调用方按读到的形状去传、却被写那边忽略。
internal static class SavedScriptSupport
{
    // 「库里没这个名字」——读与写两边一字不差（调用方据它回去核名字），故收在一处。
    // 不带 "Error: " 前缀：前缀由入口加。
    public static string NotFound(string name) => "no script named \"" + name + "\". Call list_scripts to see available names.";

    // 一次 eval 取回脚本身份：稳定 id（= 入参记忆键，与快捷键锚点同一套；声明 id 合法用之否则文件名）+ 是否带入参
    // （定义了 getInputConfig）。非工具脚本（无 getScriptInfo）→ id=文件名、hasInputs=false（其入参运行期被忽略）。
    public static (string ScriptId, bool HasInputs) Inspect(string name, string code, IProject project, Func<IMidiPart?>? currentPart, Func<IQuantization?>? quantization, Func<string?>? language)
    {
        var (info, _) = ScriptTools.InspectSource(name, code, project, currentPart, quantization, language);
        return info != null ? (ScriptTools.StableId(info), info.HasInputs) : (name, false);
    }

    // 入参 schema + 上次值 → 可读文本。逐字段列：名(+标签)、类型/范围/选项、默认、上次用值。
    //
    // 【为什么留成一段文本而不拆成结构化字段】与 `source list` 的 schemaText 同一判据：产物的事实形态
    // 就是"一份参数说明"，而它的格式化早已收口在 ConfigText。拆成 JSON 不增加信息，却要在渲染处再复制
    // 一份格式化逻辑、迟早与 ConfigText 漂移。
    public static string DescribeSchema(string name, ObjectConfig schema, PropertyObject lastValues)
    {
        var sb = new StringBuilder();
        int count = schema.Properties.Count;
        sb.Append(string.Format("Inputs for script \"{0}\" ({1} field(s)). Pass any subset as `inputs` to run_saved_script; ", name, count));
        sb.Append("omitted fields fall back to the last value shown (else the default). Values you pass are not saved as the user's last values.");
        foreach (var kvp in schema.Properties)
        {
            var key = kvp.Key;
            sb.Append("\n- ").Append(key.Id);
            if (!string.IsNullOrEmpty(key.DisplayText) && key.DisplayText != key.Id)
                sb.Append(" (\"").Append(key.DisplayText).Append("\")");
            sb.Append(": ").Append(ConfigText.Describe(kvp.Value));

            if (kvp.Value is IValueConfig leaf)
                sb.Append(". default ").Append(ConfigText.FormatValue(leaf.DefaultValue));
            if (lastValues.Map.TryGetValue(key.Id, out var last) && !last.IsNull())
                sb.Append(". last used: ").Append(ConfigText.FormatValue(last));
        }
        return sb.ToString();
    }
}
