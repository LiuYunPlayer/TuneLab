using System.Collections.Generic;
using System.Text.Json.Serialization;
using TuneLab.Foundation.Utils;

namespace TuneLab.Extensions;

// 包级（部署/安装/卸载/dedup 单位）元数据 = description.json 最外层。
//
// 代际判定：含 id ⇒ V1；无 id（老 schema：只有 name/version/assemblies/platforms，或根本无文件）⇒ Legacy。
//   id 是通用于一切 V1 包（代码 + 资源）、Legacy 从未有过的字段，故用它作判别符
//   （sdk-version 只有代码包才有，无法覆盖资源包）。
//
// 继承 ExtensionInfo：单插件可省略 extensions[]，直接把 type/assemblies/platforms 写在顶层，
// 由 EffectiveExtensions 归一化成"把自身当作那唯一的一个 extension"。复杂度收进解析这一个点，
// 管线后续只见到 IReadOnlyList<ExtensionInfo>，不知简写存在。
internal class ExtensionDescription : ExtensionInfo
{
    public required string name { get; set; }
    public string? id { get; set; }
    public string version { get; set; } = "1.0.0";
    public string author { get; set; } = string.Empty;
    public string description { get; set; } = string.Empty;

    // 包元数据本地化：manifest 是宿主直接读、早于任何插件代码，无法靠插件自译，故内置按语言的覆盖。
    // 形如 { "zh-CN": { "name": "...", "description": "..." } }；缺当前语言或缺字段则回退基础 name/description。
    public Dictionary<string, ExtensionLocalization>? localizations { get; set; }

    public string LocalizedName(string language)
        => localizations != null && localizations.TryGetValue(language, out var loc) && !string.IsNullOrEmpty(loc.name) ? loc.name! : name;

    public string LocalizedDescription(string language)
        => localizations != null && localizations.TryGetValue(language, out var loc) && !string.IsNullOrEmpty(loc.description) ? loc.description! : description;

    // 包内相对路径的图标（位图 .png/.jpg… 或矢量 .svg）。空 → sidebar 用名称首字母占位。
    public string? icon { get; set; }

    // 代码插件必填——插件编译时绑定的 SDK ABI 地板，host 据此做兼容门校验；资源包省略。
    [JsonPropertyName("sdk-version")]
    public string? sdkVersion { get; set; }

    public ExtensionInfo[] extensions { get; set; } = [];

    [JsonIgnore]
    public bool IsV1 => !string.IsNullOrEmpty(id);

    // 单条语言的本地化覆盖（name/description 各可省，省则回退基础值）。
    public class ExtensionLocalization
    {
        public string? name { get; set; }
        public string? description { get; set; }
    }

    // 归一化：有 extensions[] 以它为准（顶层 type/assemblies 忽略）；否则顶层字段定义那唯一的单插件。
    [JsonIgnore]
    public ExtensionInfo[] EffectiveExtensions => extensions.IsEmpty() ? [this] : extensions;
}
