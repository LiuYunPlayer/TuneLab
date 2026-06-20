using System;
using System.Collections.Generic;

namespace TuneLab.SDK;

/// <summary>一个音素条目（含语言前缀与符号）。</summary>
public sealed class PhonemeEntry
{
    /// <summary>完整符号，如 "ja/b" 或 "zh/a"。</summary>
    public string Symbol { get; set; } = string.Empty;
    /// <summary>是否是元音</summary>
    public bool IsVowel { get; set; }
    /// <summary>是否是滑音</summary>
    public bool IsGlide { get; set; }
}

/// <summary>
/// 音素编辑器控件配置：在属性面板中渲染一个可编辑的音素列表（辅音/元音分组）。
/// 数据通过 dataKey 绑定到 PropertyObject 的字符串字段（JSON 格式）。
/// </summary>
public sealed class PhonemeEditorConfig : IControllerConfig
{
    public required string DisplayText { get; init; }

    /// <summary>在 PropertyObject 中存储音素 JSON 的 key。</summary>
    public string DataKey { get; init; } = "_phonemes";

    /// <summary>当前音素列表（只读快照，用于 UI 渲染）。</summary>
    public IReadOnlyList<PhonemeEntry> Phonemes { get; init; } = Array.Empty<PhonemeEntry>();

    /// <summary>可用语言的列表（前缀选项）。</summary>
    public IReadOnlyList<string> AvailableLanguages { get; init; } = Array.Empty<string>();

    /// <summary>语言属性在 PropertyObject 中的 key。</summary>
    public string LanguageDataKey { get; init; } = "language";

    /// <summary>添加辅音时的回调（参数：更新后的 JSON）。</summary>
    public Action<string>? OnChanged { get; init; }

    /// <summary>辅音是否能删除（至少保留一个）。</summary>
    public bool CanDeleteConsonant { get; init; } = true;

    /// <summary>元音是否能删除（至少保留一个）。</summary>
    public bool CanDeleteVowel { get; init; } = true;
}
