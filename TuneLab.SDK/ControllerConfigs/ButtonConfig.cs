using System;

namespace TuneLab.SDK;

// 按钮控件配置：在属性面板中渲染一个可点击的按钮。
// 与值控件不同，ButtonConfig 不绑定数据，而是通过 Action 回调触发行为。
// 由宿主侧 PropertyObjectController 的 ButtonCreator 渲染为 GUI Components.Button。
public sealed class ButtonConfig : IControllerConfig
{
    /// <summary>按钮显示文本（经 L.Tr 翻译）。</summary>
    public required string DisplayText { get; init; }

    /// <summary>点击时的回调（在 UI 线程触发）。</summary>
    public required Action? Action { get; init; }

    /// <summary>按钮激活状态（true=可点击；false=灰显禁用）。</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>按钮提示文字（鼠标悬停时显示）。</summary>
    public string? Tooltip { get; init; }
}
