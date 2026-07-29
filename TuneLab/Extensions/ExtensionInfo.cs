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
//   format —— 1 条目 : 每个(方向, 后缀)一个能力位，routeKey 为 format-import:<后缀> / format-export:<后缀>。
//     每个后缀的每个方向各自可路由，所以用户能让 A 包管 .mid 的导入、B 包管 .midi 的导出——宿主开文件时
//     本就按后缀选实现，这个粒度不替作者收窄。但【声明】的单位是格式而非后缀、更不是方向：一个条目
//     一份 name/introduction/实现类/设置，它认的所有(方向, 后缀)共用它们。
//   资源类 —— 不占能力位，只登记目录。
// 于是 introduction 天然挂在【条目】上，一个格式只有一份说明、详情窗只有一个 tab——不必再靠
// 「多条目指同一份文档就合并」那种事后补救（那样一旦两条目 name 不同就无从取舍）。
//
// 【方向是声明，不是推断】早先方向由「实现类实现了哪个接口」反推（扫到 IImportFormat 就注册导入），
//   于是给类补个接口就会悄悄改变它占的能力位、连带换掉扩展设置的桶。现在方向由**作者填了哪几个后缀
//   字段**决定：写了 import-suffixes 就有导入方向，没写就没有。类没实现所声明的方向 = **加载期错误**，
//   不再静默按实际接口降级。桶键因此是 manifest 文本的纯函数。
//
//   【方向不进 type】type 恒为 "format"。方向已由后缀字段完整表达，再让 type 分出 format-import /
//   format-export 就是同一件事说两遍，作者还得判断"什么时候用哪个"。宿主内部仍按方向区分 kind
//   （format / format-import / format-export，见 FormatsManager.DeriveKind），但那是**推出来**的、
//   服务于路由键与设置桶键，作者从不书写——他只回答"这个格式能读哪些后缀、能写哪些后缀"。
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
//                宿主不认识的 type 若还声明了 assembly/class，判为「本宿主不支持的插件类型」跳过
//                （见 ExtensionManager 的资源类分支）——不静默当资源包吞掉。
//   engine     —— voice/instrument/effect 的引擎类型 id。
//   —— format 的后缀声明（三个字段，两种写法，**不混用**）——
//   suffixes   —— 两个方向都认这些后缀时的**简写**，等价于 import-suffixes 与 export-suffixes 同时取此值。
//   import-suffixes / export-suffixes
//              —— 分别声明各方向认的后缀。**没写的方向就是不存在**（显式授予：能力靠写出来获得，
//                不靠"忘了剥夺"）。不对称就这么写：`import-suffixes:["mid","midi"] + export-suffixes:["midi"]`
//                = 读两种、只写一种，仍是**一个**条目、一份实现、一个设置桶。
//                与 suffixes 互斥：同时写是加载错误——"两个方向都认这些"和"各方向分别认"是同一件事的
//                两种说法，混着写只会产生"谁覆盖谁"这种无谓的问题。
//                至少一个方向要有后缀，否则这个条目什么都不注册。
//   class      —— **入口类全名**（唯一一个）：本条目的那一份实现。宿主校验它实现了所声明的每个方向所需的
//                接口（导入→IImportFormat / 导出→IExportFormat；voice→IVoiceSynthesisEngine /
//                instrument→IInstrumentSynthesisEngine / effect→IEffectSynthesisEngine），不实现就是加载错误。
//                【一一对应】一个条目 = 一个实现类 = 一份 introduction = 一份扩展设置，三者是同一个东西。
//                 曾经是候选类【数组】、由宿主扫描认领，那是为了让一个 format 条目容纳导入类 + 导出类；
//                 但"宿主替作者猜哪个类"与"方向是声明不是推断"相悖，且两个类共用一个设置桶必然牺牲其中
//                 一份 schema。故收敛成：两份实现就写两个条目，各自一份设置。
//   assembly   —— 含该实现类的程序集（相对包文件夹的路径）；资源类省略。
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

    // 各方向认的后缀。**未声明 = 该方向不存在**（与 suffixes 互斥，见头注释）。
    [JsonPropertyName("import-suffixes")]
    public string[]? importSuffixes { get; set; }

    [JsonPropertyName("export-suffixes")]
    public string[]? exportSuffixes { get; set; }

    // 规整后的后缀清单：剔空、去重、保序、统一小写（宿主按后缀匹配文件时不区分大小写，注册键须唯一）。
    [JsonIgnore]
    public string[] EffectiveSuffixes => NormalizeSuffixes(suffixes) ?? [];

    // 各方向最终认的后缀（规整后）。**空数组 = 该方向不存在**。
    // suffixes 是"两个方向都认这些"的简写，故两侧同取它；否则各取各的声明（未声明即空 = 没有这个方向）。
    // 混写（suffixes 与任一方向字段同时出现）由 ValidateSuffixDeclaration 拦下，这里不必再判。
    [JsonIgnore]
    public string[] EffectiveImportSuffixes => HasSuffixesShorthand ? EffectiveSuffixes : NormalizeSuffixes(importSuffixes) ?? [];

    [JsonIgnore]
    public string[] EffectiveExportSuffixes => HasSuffixesShorthand ? EffectiveSuffixes : NormalizeSuffixes(exportSuffixes) ?? [];

    [JsonIgnore]
    bool HasSuffixesShorthand => suffixes != null;

    // 后缀声明的合法性（加载期，早于任何类型解析）。返回 false 时 error 可直接呈给作者。
    public bool ValidateSuffixDeclaration(out string? error)
    {
        if (suffixes != null && (importSuffixes != null || exportSuffixes != null))
        {
            error = "'suffixes' is shorthand for both directions; declare either 'suffixes' or 'import-suffixes'/'export-suffixes', not both";
            return false;
        }
        if (EffectiveImportSuffixes.Length == 0 && EffectiveExportSuffixes.Length == 0)
        {
            error = "no suffixes declared: give 'suffixes' (both directions) or at least one of 'import-suffixes' / 'export-suffixes'";
            return false;
        }
        error = null;
        return true;
    }

    // 条目的身份集（= 拼设置桶键与列身份用）：两个方向的并集，按导入声明序、再追加只在导出里出现的。
    // 用并集而非某一侧：条目身份就是"这份实现负责哪些格式"，与它对某个格式是读是写无关。
    [JsonIgnore]
    public string[] EffectiveIdentitySuffixes
    {
        get
        {
            var list = new List<string>(EffectiveImportSuffixes);
            foreach (var s in EffectiveExportSuffixes)
                if (!list.Contains(s))
                    list.Add(s);
            return list.ToArray();
        }
    }

    static string[]? NormalizeSuffixes(string[]? source)
    {
        if (source == null)
            return null;
        var list = new List<string>();
        foreach (var s in source)
        {
            if (string.IsNullOrWhiteSpace(s))
                continue;
            var normalized = s.Trim().TrimStart('.').ToLowerInvariant();
            if (normalized.Length > 0 && !list.Contains(normalized))
                list.Add(normalized);
        }
        return list.ToArray();
    }

    // 入口类全名（唯一）。C# 侧不能叫 class（关键字），JSON 里就是 "class"。
    [JsonPropertyName("class")]
    public string? entryClass { get; set; }

    public string? assembly { get; set; }

    [JsonIgnore]
    public string EffectiveClass => (entryClass ?? string.Empty).Trim();

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
