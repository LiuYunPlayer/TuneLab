using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using TuneLab.Foundation;
using TuneLab.Utils;

namespace TuneLab.Extensions;

// 插件级（注册单位）元数据：manifest.json 的 extensions[] 中每个元素，
// 或单插件简写时由顶层字段兜底（ExtensionManifest 继承本类）。
//
// 身份内联进 manifest（不再靠代码 attribute）：一个条目 = 一个具体可注册能力，自带身份 + 实现类全名，
// 宿主读完 manifest 即知插件提供什么、无需加载程序集反射。
//
// 身份 id 与显示名分离：
//   engine/extension —— **不可变身份**（注册键 + 工程序列化引用），改了会让旧工程失配，绝不本地化。
//   name/localizations —— **显示名**（仅 UI 展示），可按语言翻译；缺省回退到身份 id。
//
//   type       —— 必填，类别（决定派给哪个 manager）：format / voice / effect / agent-model / 资源类。
//   engine     —— voice/effect/agent-model 的引擎类型 id。
//   extension  —— format 的文件扩展名（不带点）。
//   classes    —— **入口候选类清单**（全名数组）：宿主把数组里的类都扫一遍，按本 type 所需接口逐个匹配、命中即注册
//                （voice→IVoiceSynthesisEngine / effect→IEffectSynthesisEngine / agent-model→IAgentModelEngine / format→IImportFormat+IExportFormat）。
//                 因 manifest 只是"方便宿主加载的描述"，无需精确指明哪个类干哪件事——把候选都列上、宿主按接口认领。
//                 一种类型可需多个入口类（如 format 的导入类 + 导出类），数组天然承载。
//   assembly   —— 含上述实现类的程序集（相对包文件夹的路径）；资源类省略。所有候选类同居此程序集。
//   assemblies —— 仅 Legacy 老 schema 顶层使用（盲扫候选 dll）；V1 条目改用单数 assembly。
//   platforms  —— 平台过滤（同一包内不同插件可各自声明）。
internal class ExtensionInfo
{
    public string type { get; set; } = string.Empty;

    // —— V1 身份内联字段（不可变 id）——
    public string? engine { get; set; }
    public string? extension { get; set; }

    // 入口候选类清单；宿主按 type 所需接口扫描认领。
    public string[]? classes { get; set; }

    public string? assembly { get; set; }

    // 入口候选类（去重、保序、剔空）。宿主对每个所需接口扫此清单取首个命中类；空清单 = 未声明任何入口类。
    [JsonIgnore]
    public string[] CandidateClasses
    {
        get
        {
            if (classes == null)
                return [];
            var list = new List<string>();
            foreach (var c in classes)
                if (!string.IsNullOrEmpty(c) && !list.Contains(c))
                    list.Add(c);
            return list.ToArray();
        }
    }

    // —— 显示名（可翻译，独立于身份 id）——
    public string? name { get; set; }

    // 按语言覆盖显示名（与包级同模式）：形如 { "zh-CN": { "name": "..." } }。缺当前语言或字段则回退基础 name。
    public Dictionary<string, ExtensionLocalization>? localizations { get; set; }

    // —— Legacy 老 schema 顶层兼容 ——
    public string[] assemblies { get; set; } = [];

    public string[] platforms { get; set; } = [];

    // 本地化显示名：当前语言覆盖 ?? 基础 name ?? 身份 id（engine/extension）?? ""。
    public string LocalizedName(string language)
    {
        if (localizations != null && localizations.TryGetValue(language, out var loc) && !string.IsNullOrEmpty(loc.name))
            return loc.name!;
        if (!string.IsNullOrEmpty(name))
            return name!;
        return engine ?? extension ?? string.Empty;
    }

    // 单条语言的本地化覆盖（name/description 各可省，省则回退基础值）。description 仅包级使用。
    public class ExtensionLocalization
    {
        public string? name { get; set; }
        public string? description { get; set; }
    }

    public bool IsPlatformAvailable()
    {
        if (platforms.IsEmpty())
            return true;

        return platforms.Contains(PlatformHelper.GetOS()) | platforms.Contains(PlatformHelper.GetPlatform());
    }
}
