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
// 条目与「能力位」（可被冲突消解的单位，见 ExtensionRouting）的对应关系：
//   engine 类（voice/instrument/effect）—— 1 条目 : 1 能力位，身份即 engine id。
//   format —— 1 条目 : **2×N** 能力位（N = suffixes 个数）：每个后缀的导入与导出各自可路由
//     （routeKey 为 format-import:<后缀> / format-export:<后缀>），所以用户既能让 A 包管导入 B 包管导出，
//     也能让 A 包管 .mid、B 包管 .midi——宿主开文件时本就按后缀选实现，这个粒度不替作者收窄。
//     但【声明】的单位是格式而非后缀：一个条目一份 name/introduction/实现类，多个后缀共用它们。
//   资源类 —— 不占能力位，只登记目录。
// 于是 introduction 天然挂在【条目】上，一个格式只有一份说明、详情窗只有一个 tab——不必再靠
// 「多条目指同一份文档就合并」那种事后补救（那样一旦两条目 name 不同就无从取舍）。
//
// introduction —— **条目级的面向用户说明**（一份 markdown 介绍），详情窗渲染、agent 按需拉取。
//   它是作者唯一需要写的说明：agent 要的"一句话摘要"由它自己从这份全文提炼，不再要求作者另写一行
//   （作者不清楚模型要什么，写出来多半是产品文案；模型自己提炼自己用最准）。
//   条目没写 introduction 时【不拿包级 description 冒充】：那句讲的是整个包（可含多个能力位），
//   顶替会让 agent 把包里别的能力当成这个能力自带的、进而向用户传播错误事实。agent 侧只做
//   **标注式降级**——如实说明"该能力没有自己的介绍，以下是其所属包的自述，仅供参考"，让模型知道
//   这是二手信息（见 ExtensionInfoTools）。
//   单插件简写（省略 extensions[]）下 introduction 写在顶层即该唯一条目的（与 name 同理）；多插件包
//   在顶层写它无效——包没有 introduction 概念，宿主只从 extensions[] 各条目取。
//   type       —— 必填，类别（决定派给哪个 manager）：format / voice / instrument / effect / 资源类。
//                （agent-model 不开放外部扩展：模型适配器是宿主内部模块，新适配走 PR，见 ExtensionManager 注释。）
//   engine     —— voice/instrument/effect 的引擎类型 id。
//   suffixes   —— format 认的文件后缀清单（不带点）。一个条目 = 一个格式，可有多个后缀别名
//                （如 ["mid","midi"]），它们共用这一条目的实现类与全部说明。
//   classes    —— **入口候选类清单**（全名数组）：宿主把数组里的类都扫一遍，按本 type 所需接口逐个匹配、命中即注册
//                （voice→IVoiceSynthesisEngine / effect→IEffectSynthesisEngine / format→IImportFormat+IExportFormat）。
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

    // format 认的文件后缀清单（不带点）。同一格式的多个别名（如 ["mid","midi"]）写在一个条目里：
    // 它们共用该条目的实现类与说明，但注册与路由仍逐后缀独立（见头注释）。
    public string[]? suffixes { get; set; }

    // 规整后的后缀清单：剔空、去重、保序、统一小写（宿主按后缀匹配文件时不区分大小写，注册键须唯一）。
    [JsonIgnore]
    public string[] EffectiveSuffixes
    {
        get
        {
            if (suffixes == null)
                return [];
            var list = new List<string>();
            foreach (var s in suffixes)
            {
                if (string.IsNullOrWhiteSpace(s))
                    continue;
                var normalized = s.Trim().TrimStart('.').ToLowerInvariant();
                if (normalized.Length > 0 && !list.Contains(normalized))
                    list.Add(normalized);
            }
            return list.ToArray();
        }
    }

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

    // —— 面向用户的说明（条目级，不回退包级；见头注释）——

    // 包内相对路径，指向一份 markdown 介绍（详情窗渲染、agent 按需拉取）。
    // 语言变体走 localizations 覆盖（各语言可指不同文件名），不搞 <base>.<lang>.md 的隐式后缀约定。
    public string? introduction { get; set; }

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
        // 缺显示名则回退身份：engine 类用 engine id；format 用首个后缀（多后缀条目也只取一个作代称，
        // 全清单在详情窗与 agent 回报里另行列出）。
        if (!string.IsNullOrEmpty(engine))
            return engine!;
        var suffixList = EffectiveSuffixes;
        return suffixList.Length > 0 ? suffixList[0] : string.Empty;
    }

    // 本地化 introduction 的【包内相对路径】：当前语言覆盖 ?? 基础 introduction。未声明则 null。
    // 只解析声明值，不判断文件是否存在（由 ExtensionIntroduction 落地成绝对路径时校验）。
    public string? LocalizedIntroduction(string language)
    {
        if (localizations != null && localizations.TryGetValue(language, out var loc) && !string.IsNullOrEmpty(loc.introduction))
            return loc.introduction;
        return string.IsNullOrEmpty(introduction) ? null : introduction;
    }

    // 单条语言的本地化覆盖（各字段可省，省则回退基础值）。
    // 适用层级随宿主取值处而定：name 包级与条目级共用；description 仅包级；introduction 仅条目级。
    public class ExtensionLocalization
    {
        public string? name { get; set; }
        public string? description { get; set; }
        public string? introduction { get; set; }
    }

    public bool IsPlatformAvailable()
    {
        if (platforms.IsEmpty())
            return true;

        return platforms.Contains(PlatformHelper.GetOS()) | platforms.Contains(PlatformHelper.GetPlatform());
    }
}
