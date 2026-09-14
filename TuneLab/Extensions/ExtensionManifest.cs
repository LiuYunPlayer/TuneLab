using System.Text.Json.Serialization;
using TuneLab.Foundation;

namespace TuneLab.Extensions;

// 包级（部署/安装/卸载/dedup 单位）元数据 = manifest.json 最外层。
//
// 代际判定：含 id ⇒ V1；无 id（老 schema：只有 name/version/assemblies/platforms，或根本无文件）⇒ Legacy。
//   id 是通用于一切 V1 包（代码 + 资源）、Legacy 从未有过的字段，故用它作判别符
//   （sdk-version 只有代码包才有，无法覆盖资源包）。
//
// 继承 ExtensionInfo：单插件可省略 extensions[]，直接把 type/engine/class/assembly 等写在顶层，
// 由 EffectiveExtensions 归一化成"把自身当作那唯一的一个 extension"。复杂度收进解析这一个点，
// 管线后续只见到 IReadOnlyList<ExtensionInfo>，不知简写存在。
//
// name/localizations/LocalizedName 继承自基类（包级 name 与单插件简写时的引擎显示名共用同一字段）。
// 包元数据本地化：manifest 是宿主直接读、早于任何插件代码，无法靠插件自译，故内置按语言的覆盖。
internal class ExtensionManifest : ExtensionInfo
{
    public string? id { get; set; }
    public string version { get; set; } = "1.0.0";
    public string author { get; set; } = string.Empty;
    public string description { get; set; } = string.Empty;

    // 包内相对路径的图标（位图 .png/.jpg… 或矢量 .svg）。空 → sidebar 用名称首字母占位。
    public string? icon { get; set; }

    // 代码插件必填——插件编译时绑定的 SDK ABI 地板，host 据此做兼容门校验；资源包省略。
    [JsonPropertyName("sdk-version")]
    public string? sdkVersion { get; set; }

    // —— 脚本条目（sidecar；docs/scripted-plugin-sidecar-design.md）——
    // wire 协议版本（独立于 sdk-version 的第三轴，§1.2）。高于宿主 WireProtocol.Version ⇒ 握手前
    // Skipped（与 sdk-version 门同款语义）。缺省 = 本宿主最新轴，向后兼容（既有包不可能声明不存在的轴）。
    [JsonPropertyName("protocol-version")]
    public int? protocolVersion { get; set; }

    // —— type:"runtime" 资源包（§8.4）：解释器分发单元 ——
    // 零代码（无 assembly/class/runtime/entry），宿主只登记；RuntimeLocator 扫描消费。
    // 老宿主对未知 type 一律当资源包，天然向后兼容。
    public string? flavor { get; set; }

    // runtime 包的精确解释器版本（如 "3.12.7"）。与基类 version（包自身发布版本）是两根轴，勿混。
    [JsonPropertyName("runtime-version")]
    public string? runtimeVersion { get; set; }

    public ExtensionInfo[] extensions { get; set; } = [];

    // 缺当前语言或缺字段则回退基础 description。
    public string LocalizedDescription(string language)
        => localizations != null && localizations.TryGetValue(language, out var loc) && !string.IsNullOrEmpty(loc.description) ? loc.description! : description;

    [JsonIgnore]
    public bool IsV1 => !string.IsNullOrEmpty(id);

    // 归一化：有 extensions[] 以它为准（顶层 type/engine/class 忽略）；否则顶层字段定义那唯一的单插件。
    [JsonIgnore]
    public ExtensionInfo[] EffectiveExtensions => extensions.IsEmpty() ? [this] : extensions;
}
