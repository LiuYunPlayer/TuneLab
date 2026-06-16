using System.Collections.Generic;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using TuneLab.GUI;
using TuneLab.Utils;

namespace TuneLab.UI;

// 轻量 Markdown 渲染：Markdig 负责解析（健壮、零 UI 依赖），渲染由本类用 Avalonia 控件实现。
// 选用自渲染的原因：Markdown.Avalonia 在 Avalonia 11.3 下 StaticBinding 崩、Avalonia.Controls.Markdown 为付费包、
// LiveMarkdown.Avalonia 与本仓库的 Avalonia.Svg.Skia 存在 Svg.Skia 类型冲突——均不可用。自渲染零依赖冲突、文本可选中、主题可控。
// 覆盖：标题 / 段落 / 有序·无序列表(含嵌套) / 代码块 / 行内代码 / 粗体·斜体·删除线 / 链接(样式化) / 引用 / 分隔线。
// 暂不做：代码块语法高亮、表格富渲染（按需后续接 TextMateSharp / 渲染 TableBlock）。
internal static class ChatMarkdownRenderer
{
    // 等宽字体回退链：用裸家名（缺失会优雅回退默认，不像 avares 资源缺失那样在 TextLayout 里硬抛）。
    // 注意 Assets/Fonts 目录为空、avares://.../#NotoMono 取不到字形，绝不能用于 SelectableTextBlock.FontFamily。
    static readonly FontFamily Mono = new("Consolas, Menlo, Courier New");
    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()  // ~~删除线~~ 等
        .UseAutoLinks()
        .Build();

    public static Control Render(string markdown)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
        var doc = Markdig.Markdown.Parse(markdown ?? string.Empty, Pipeline);
        foreach (var block in doc)
        {
            var c = RenderBlock(block);
            if (c != null)
                panel.Children.Add(c);
        }
        return panel;
    }

    static Control? RenderBlock(Block block)
    {
        switch (block)
        {
            case HeadingBlock h:
            {
                var tb = NewText();
                tb.FontSize = h.Level switch { 1 => 18, 2 => 16, 3 => 14, _ => 13 };
                tb.FontWeight = FontWeight.Bold;
                if (h.Inline != null)
                    RenderInlines(h.Inline, tb.Inlines!);
                return tb;
            }
            case ParagraphBlock p:
            {
                var tb = NewText();
                if (p.Inline != null)
                    RenderInlines(p.Inline, tb.Inlines!);
                return tb;
            }
            case ListBlock list:
                return RenderList(list);
            case QuoteBlock quote:
                return RenderQuote(quote);
            case ThematicBreakBlock:
                return new Border { Height = 1, Margin = new(0, 4), Background = Style.LIGHT_WHITE.Opacity(0.2).ToBrush() };
            case CodeBlock code: // FencedCodeBlock 也属 CodeBlock
                return RenderCode(GetCodeText(code));
            default:
                return null;
        }
    }

    static SelectableTextBlock NewText() => new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
        Foreground = Colors.White.ToBrush(),
    };

    static Control RenderList(ListBlock list)
    {
        var sp = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, Margin = new(6, 2, 0, 2) };
        int index = 1;
        foreach (var item in list)
        {
            if (item is not ListItemBlock li)
                continue;

            var bullet = new SelectableTextBlock
            {
                Text = list.IsOrdered ? $"{index}." : "•",
                FontSize = 12,
                Foreground = Colors.White.ToBrush(),
                Margin = new(0, 0, 6, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            };
            var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
            foreach (var sub in li)
            {
                var c = RenderBlock(sub);
                if (c != null)
                    content.Children.Add(c);
            }
            var row = new DockPanel();
            DockPanel.SetDock(bullet, Dock.Left);
            row.Children.Add(bullet);
            row.Children.Add(content); // 填充剩余宽
            sp.Children.Add(row);
            index++;
        }
        return sp;
    }

    static Control RenderQuote(QuoteBlock quote)
    {
        var inner = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        foreach (var sub in quote)
        {
            var c = RenderBlock(sub);
            if (c != null)
                inner.Children.Add(c);
        }
        var bar = new Border { Width = 3, Margin = new(0, 0, 8, 0), Background = Style.LIGHT_WHITE.Opacity(0.3).ToBrush() };
        var row = new DockPanel { Margin = new(0, 2) };
        DockPanel.SetDock(bar, Dock.Left);
        row.Children.Add(bar);
        row.Children.Add(inner);
        return row;
    }

    static Control RenderCode(string code)
    {
        var tb = new SelectableTextBlock
        {
            Text = code,
            FontFamily = Mono,
            FontSize = 11.5,
            Foreground = Style.LIGHT_WHITE.ToBrush(),
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.Normal, // NotoMono 仅 Regular，避免继承 Bold/Italic 取不到字形而崩
            FontStyle = FontStyle.Normal,
        };
        return new Border
        {
            Background = Style.BACK.Opacity(0.5).ToBrush(),
            CornerRadius = new(4),
            Padding = new(8, 6),
            Margin = new(0, 2),
            Child = tb,
        };
    }

    static string GetCodeText(CodeBlock code)
    {
        var sb = new StringBuilder();
        var lines = code.Lines.Lines;
        for (int i = 0; i < code.Lines.Count; i++)
        {
            if (i > 0)
                sb.Append('\n');
            sb.Append(lines[i].Slice.ToString());
        }
        return sb.ToString();
    }

    static void RenderInlines(ContainerInline container, InlineCollection target)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    target.Add(new Run(lit.Content.ToString()));
                    break;
                case EmphasisInline em:
                {
                    // '~' → 删除线；DelimiterCount>=2 → 粗体；否则斜体。
                    Span span = em.DelimiterChar == '~' ? new Span { TextDecorations = TextDecorations.Strikethrough }
                              : em.DelimiterCount >= 2 ? new Bold()
                              : new Italic();
                    RenderInlines(em, span.Inlines);
                    target.Add(span);
                    break;
                }
                case CodeInline codeInline:
                    // 锁 Normal 字重/字形：NotoMono 只有 Regular，继承到 Bold/Italic 会取不到字形而在布局期崩。
                    target.Add(new Run(codeInline.Content) { FontFamily = Mono, Foreground = Style.HIGH_LIGHT.ToBrush(), FontWeight = FontWeight.Normal, FontStyle = FontStyle.Normal });
                    break;
                case LinkInline link:
                {
                    if (link.IsImage)
                        break; // 图片暂不渲染
                    var span = new Span { Foreground = Style.HIGH_LIGHT.ToBrush(), TextDecorations = TextDecorations.Underline };
                    RenderInlines(link, span.Inlines);
                    if (span.Inlines.Count == 0 && !string.IsNullOrEmpty(link.Url))
                        span.Inlines.Add(new Run(link.Url));
                    target.Add(span);
                    break;
                }
                case AutolinkInline auto:
                    target.Add(new Run(auto.Url) { Foreground = Style.HIGH_LIGHT.ToBrush(), TextDecorations = TextDecorations.Underline });
                    break;
                case LineBreakInline:
                    target.Add(new LineBreak());
                    break;
                case ContainerInline c:
                    RenderInlines(c, target); // 其他容器型内联：递归子节点
                    break;
                default:
                {
                    var s = inline.ToString();
                    if (!string.IsNullOrEmpty(s))
                        target.Add(new Run(s));
                    break;
                }
            }
        }
    }
}
