using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Svg.Skia;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using TuneLab.GUI;
using TuneLab.I18N;
using TuneLab.Utils;

namespace TuneLab.UI;

// 轻量 Markdown 渲染：Markdig 负责解析（健壮、零 UI 依赖），渲染由本类用 Avalonia 控件实现。
// 选用自渲染的原因：Markdown.Avalonia 在 Avalonia 11.3 下 StaticBinding 崩、Avalonia.Controls.Markdown 为付费包、
// LiveMarkdown.Avalonia 与本仓库的 Avalonia.Svg.Skia 存在 Svg.Skia 类型冲突——均不可用。自渲染零依赖冲突、文本可选中、主题可控。
// 覆盖：标题 / 段落 / 有序·无序列表(含嵌套) / 任务列表 / 表格 / 代码块 / 行内代码 / 粗体·斜体·删除线 / 链接(样式化) / 引用 / 分隔线 / 表情。
// 暂不做：代码块语法高亮（按需后续接 TextMateSharp）。
// 跨块文字选择：刻意不做——块级控件各自渲染（保代码框/列表缩进/表格视觉），跨块选区由「整条 Copy + 代码块各自悬浮 Copy」覆盖。
internal static class ChatMarkdownRenderer
{
    // 等宽字体回退链：用裸家名（缺失会优雅回退默认，不像 avares 资源缺失那样在 TextLayout 里硬抛）。
    // 内置 Assets.NotoMono 可用（真身 Assets/Font#Noto Mono），但仅 Regular 字形；此处 Markdown 内联会继承
    // Bold/Italic，为避免字形缺失仍走系统链。
    static readonly FontFamily Mono = new("Consolas, Menlo, Courier New");

    // 正文字体：系统默认为主，追加各平台彩色 emoji 字体作回退（兜底单码位 emoji）。
    static readonly FontFamily TextFont =
        new($"{FontManager.Current.DefaultFontFamily.Name}, Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji");
    // 纯 emoji 字体：emoji 字位簇强制整段用它，否则像键帽 1️⃣（数字+U+FE0F+U+20E3 组合序列）会被默认字体吃掉数字、
    // 组合符单独回退而拆散成豆腐块。整段用 emoji 字体后整形器把序列整形为单个彩色字形。系统字体裸名，缺失优雅回退。
    static readonly FontFamily EmojiFont = new("Segoe UI Emoji, Apple Color Emoji, Noto Color Emoji");
    static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseEmphasisExtras()  // ~~删除线~~ 等
        .UseAutoLinks()
        .UsePipeTables()      // | a | b | 表格
        .UseTaskLists()       // - [x] 任务列表
        .UseEmojiAndSmiley()  // :smile: → 😄（无对应字形则回退，不会崩）
        .Build();

    public static Control Render(string markdown) => Render(markdown, null);

    // baseDir 非空时：Markdown 里的相对图片按它（= 内容所在目录，如插件包目录）解析并就地渲染本地图片；
    // baseDir 为空则沿用「不渲染图片」的旧行为。远程 http(s) 图片一律不抓取（避免意外网络请求）。
    public static Control Render(string markdown, string? baseDir)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
        var doc = Markdig.Markdown.Parse(markdown ?? string.Empty, Pipeline);
        foreach (var block in doc)
        {
            var c = RenderBlock(block, baseDir);
            if (c != null)
                panel.Children.Add(c);
        }
        return panel;
    }

    static Control? RenderBlock(Block block, string? baseDir)
    {
        switch (block)
        {
            case HeadingBlock h:
            {
                var tb = NewText();
                tb.FontSize = h.Level switch { 1 => 18, 2 => 16, 3 => 14, _ => 13 };
                tb.FontWeight = FontWeight.Bold;
                if (h.Inline != null)
                    RenderInlines(h.Inline, tb.Inlines!, baseDir);
                return tb;
            }
            case ParagraphBlock p:
            {
                // 仅含图片的段落：直接以块级图片渲染（不塞进文本行，避免 InlineUIContainer 撑坏行盒）。
                if (baseDir != null && TryRenderImageParagraph(p, baseDir, out var img))
                    return img;
                var tb = NewText();
                if (p.Inline != null)
                    RenderInlines(p.Inline, tb.Inlines!, baseDir);
                return tb;
            }
            case Table table:
                return RenderTable(table, baseDir);
            case ListBlock list:
                return RenderList(list, baseDir);
            case QuoteBlock quote:
                return RenderQuote(quote, baseDir);
            case ThematicBreakBlock:
                return new Border { Height = 1, Margin = new(0, 4), Background = Style.LIGHT_WHITE.Opacity(0.2).ToBrush() };
            case CodeBlock code: // FencedCodeBlock 也属 CodeBlock
                return RenderCode(GetCodeText(code));
            default:
                return null;
        }
    }

    // 段落是否「只有一张图片」（可含环绕空白），是则输出块级图片控件。用于把 README 里独占一行的插图放大展示。
    static bool TryRenderImageParagraph(ParagraphBlock p, string baseDir, out Control? image)
    {
        image = null;
        if (p.Inline == null)
            return false;
        LinkInline? only = null;
        foreach (var inline in p.Inline)
        {
            switch (inline)
            {
                case LinkInline { IsImage: true } li when only == null:
                    only = li;
                    break;
                case LiteralInline lit when string.IsNullOrWhiteSpace(lit.Content.ToString()):
                    break; // 忽略图片前后的纯空白
                default:
                    return false; // 混入其它内容 → 走普通段落
            }
        }
        if (only == null)
            return false;
        image = TryCreateImage(baseDir, only.Url, block: true);
        return image != null;
    }

    static SelectableTextBlock NewText() => new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
        FontFamily = TextFont, // emoji 回退链
        Foreground = Colors.White.ToBrush(),
    };

    static Control RenderList(ListBlock list, string? baseDir)
    {
        var sp = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2, Margin = new(6, 2, 0, 2) };
        int index = 1;
        foreach (var item in list)
        {
            if (item is not ListItemBlock li)
                continue;

            // 任务列表项（- [x]）由行内 ☑/☐ 充当标记，不再加项目符号，避免「• ☑」重复。
            var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
            foreach (var sub in li)
            {
                var c = RenderBlock(sub, baseDir);
                if (c != null)
                    content.Children.Add(c);
            }
            if (IsTaskItem(li))
            {
                sp.Children.Add(content);
                index++;
                continue;
            }

            var bullet = new SelectableTextBlock
            {
                Text = list.IsOrdered ? $"{index}." : "•",
                FontSize = 12,
                Foreground = Colors.White.ToBrush(),
                Margin = new(0, 0, 6, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            };
            var row = new DockPanel();
            DockPanel.SetDock(bullet, Dock.Left);
            row.Children.Add(bullet);
            row.Children.Add(content); // 填充剩余宽
            sp.Children.Add(row);
            index++;
        }
        return sp;
    }

    // 任务列表项判定：首个段落的首个内联是 TaskList（- [x]/[ ]）。
    static bool IsTaskItem(ListItemBlock li)
        => li.Count > 0 && li[0] is ParagraphBlock p && p.Inline?.FirstChild is TaskList;

    static Control RenderQuote(QuoteBlock quote, string? baseDir)
    {
        var inner = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        foreach (var sub in quote)
        {
            var c = RenderBlock(sub, baseDir);
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

        // 悬浮复制按钮（方案1）：跨块选区不做，代码块靠它独立整段复制。默认隐藏，悬浮代码框才淡入。
        var copyText = new TextBlock { Text = "Copy".Tr(TC.Menu), FontSize = 10, Foreground = Style.LIGHT_WHITE.Opacity(0.7).ToBrush() };
        var copy = new Border
        {
            Background = Style.BACK.Opacity(0.7).ToBrush(),
            CornerRadius = new(3),
            Padding = new(5, 1),
            Margin = new(0, 0, 2, 0),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            Cursor = new Cursor(StandardCursorType.Hand),
            Opacity = 0, // 悬浮代码框才显示
            Child = copyText,
        };
        copy.PointerEntered += (_, _) => copyText.Foreground = Colors.White.ToBrush();
        copy.PointerExited += (_, _) => copyText.Foreground = Style.LIGHT_WHITE.Opacity(0.7).ToBrush();
        copy.PointerPressed += (_, e) =>
        {
            e.Handled = true; // 不抢代码文本的选区交互
            _ = TopLevel.GetTopLevel(copy)?.Clipboard?.SetTextAsync(code);
        };

        var stack = new Panel();
        stack.Children.Add(tb);
        stack.Children.Add(copy);
        var border = new Border
        {
            Background = Style.BACK.Opacity(0.5).ToBrush(),
            CornerRadius = new(4),
            Padding = new(8, 6),
            Margin = new(0, 2),
            Child = stack,
        };
        border.PointerEntered += (_, _) => copy.Opacity = 1;
        border.PointerExited += (_, _) => copy.Opacity = 0;
        return border;
    }

    // 表格 → Avalonia Grid：列宽按内容自适应——短列用 Auto（贴合内容、不浪费空间、不提前换行），含长文本的列用 Star
    // （吸收剩余宽度、需要时才换行）。修掉「等宽星列把短列留白、却逼长列过早换行」。表头加粗 + 浅底；细网格线。
    static Control RenderTable(Table table, string? baseDir)
    {
        int cols = table.ColumnDefinitions.Count;
        foreach (var rowObj in table)
            if (rowObj is TableRow r)
                cols = System.Math.Max(cols, r.Count);
        if (cols == 0)
            return new SelectableTextBlock { Text = string.Empty };

        // 逐列算内容最大「显示长度」（CJK/全角计 2）：超过阈值的列含长文本 → Star 吸收剩余宽并按需换行；其余 → Auto 贴合内容。
        var colMaxLen = new int[cols];
        foreach (var rowObj in table)
        {
            if (rowObj is not TableRow row)
                continue;
            for (int c = 0; c < row.Count && c < cols; c++)
                if (row[c] is TableCell cell)
                    colMaxLen[c] = System.Math.Max(colMaxLen[c], DisplayLength(CellText(cell)));
        }
        const int NarrowThreshold = 16;
        bool anyFlex = false;
        for (int c = 0; c < cols; c++)
            anyFlex |= colMaxLen[c] > NarrowThreshold;

        var line = Style.LIGHT_WHITE.Opacity(0.2).ToBrush();
        // 全是窄列时左对齐（小表格按内容宽、不强行拉满侧栏）；有长列时默认 Stretch，Star 列把表格撑满可用宽。
        var grid = new Grid { Margin = new(0, 2) };
        if (!anyFlex)
            grid.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        for (int c = 0; c < cols; c++)
        {
            bool flex = anyFlex && colMaxLen[c] > NarrowThreshold;
            grid.ColumnDefinitions.Add(new ColumnDefinition(flex ? GridLength.Star : GridLength.Auto));
        }

        int rowIndex = 0;
        foreach (var rowObj in table)
        {
            if (rowObj is not TableRow row)
                continue;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int c = 0; c < row.Count; c++)
            {
                if (row[c] is not TableCell cell)
                    continue;
                var align = c < table.ColumnDefinitions.Count ? table.ColumnDefinitions[c].Alignment : null;
                var cellBorder = new Border
                {
                    BorderBrush = line,
                    BorderThickness = new(1),
                    Background = row.IsHeader ? Style.LIGHT_WHITE.Opacity(0.06).ToBrush() : null,
                    Padding = new(6, 3),
                    Child = RenderCell(cell, row.IsHeader, align, baseDir),
                };
                Grid.SetRow(cellBorder, rowIndex);
                Grid.SetColumn(cellBorder, c);
                int span = cell.ColumnSpan > 1 ? cell.ColumnSpan : 1;
                if (span > 1)
                    Grid.SetColumnSpan(cellBorder, System.Math.Min(span, cols - c));
                grid.Children.Add(cellBorder);
            }
            rowIndex++;
        }
        return grid;
    }

    static Control RenderCell(TableCell cell, bool header, TableColumnAlign? align, string? baseDir)
    {
        var sp = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        foreach (var sub in cell)
        {
            var c = RenderBlock(sub, baseDir);
            if (c is SelectableTextBlock stb)
            {
                if (header)
                    stb.FontWeight = FontWeight.Bold;
                stb.TextAlignment = align switch
                {
                    TableColumnAlign.Center => TextAlignment.Center,
                    TableColumnAlign.Right => TextAlignment.Right,
                    _ => TextAlignment.Left,
                };
            }
            if (c != null)
                sp.Children.Add(c);
        }
        return sp;
    }

    // 单元格纯文本（拼接其内段落的内联文本），用于估列宽。
    static string CellText(TableCell cell)
    {
        var sb = new StringBuilder();
        foreach (var sub in cell)
            if (sub is LeafBlock lb && lb.Inline != null)
                sb.Append(GetInlineText(lb.Inline));
        return sb.ToString();
    }

    // 文本「显示长度」：CJK/全角字符计 2、其余计 1——比字符数更接近实际占宽，使中英文混排的列宽估计更准。
    static int DisplayLength(string s)
    {
        int n = 0;
        foreach (var rune in s.EnumerateRunes())
            n += IsWideChar(rune.Value) ? 2 : 1;
        return n;
    }

    static bool IsWideChar(int v)
        => (v >= 0x1100 && v <= 0x115F)    // Hangul Jamo
        || (v >= 0x2E80 && v <= 0xA4CF)    // CJK 部首…注音…康熙…汉字…彝文
        || (v >= 0xAC00 && v <= 0xD7A3)    // Hangul 音节
        || (v >= 0xF900 && v <= 0xFAFF)    // CJK 兼容汉字
        || (v >= 0xFF00 && v <= 0xFF60)    // 全角 ASCII
        || (v >= 0xFFE0 && v <= 0xFFE6)    // 全角符号
        || (v >= 0x20000 && v <= 0x3FFFD); // CJK 扩展 B+

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

    // 可点击链接：Avalonia 的 Run/Span 不接指针事件，故用 InlineUIContainer 内嵌一个可点 TextBlock，点击走系统打开 URL。
    static Avalonia.Controls.Documents.Inline LinkInlineUI(string text, string? url)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontFamily = TextFont,
            Foreground = Style.HIGH_LIGHT.ToBrush(),
            TextDecorations = TextDecorations.Underline,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        if (!string.IsNullOrEmpty(url))
        {
            ToolTip.SetTip(tb, url);
            tb.PointerEntered += (_, _) => tb.Foreground = Colors.White.ToBrush();
            tb.PointerExited += (_, _) => tb.Foreground = Style.HIGH_LIGHT.ToBrush();
            tb.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                ProcessHelper.OpenUrl(url);
            };
        }
        return new InlineUIContainer(tb) { BaselineAlignment = BaselineAlignment.TextBottom };
    }

    // Markdown 图片 → Avalonia Image：仅解析到本地文件才渲染（相对路径按 baseDir 拼、绝对本地路径直用）。
    // 远程 http(s)/data URI 一律不加载（避免网络请求 / 内存放大），返回 null 由调用方退回替代文字或整段忽略。
    // .svg 走矢量、其余按位图解码；解码失败也返回 null。block=块级独占图（更大上限、左对齐）；否则行内小图。
    static Control? TryCreateImage(string baseDir, string? url, bool block)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            // 去掉可能的 #fragment / ?query，再按内容目录解析相对路径。
            var clean = url.Split('?', '#')[0];
            var path = Path.IsPathRooted(clean) ? clean : Path.Combine(baseDir, clean);
            if (!File.Exists(path))
                return null;

            IImage source;
            if (Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase))
                source = new SvgImage { Source = SvgSource.LoadFromSvg(File.ReadAllText(path)) };
            else
            {
                using var stream = File.OpenRead(path);
                source = new Bitmap(stream);
            }

            return new Image
            {
                Source = source,
                Stretch = Stretch.Uniform,
                MaxWidth = block ? 640 : 480,
                MaxHeight = block ? 640 : 320,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Margin = block ? new(0, 4) : new(0),
            };
        }
        catch
        {
            return null;
        }
    }

    // 收集内联的纯文本（用于链接显示文本）。
    static string GetInlineText(ContainerInline container)
    {
        var sb = new StringBuilder();
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline lit: sb.Append(lit.Content.ToString()); break;
                case CodeInline ci: sb.Append(ci.Content); break;
                case LineBreakInline: sb.Append(' '); break;
                case ContainerInline c: sb.Append(GetInlineText(c)); break;
            }
        }
        return sb.ToString();
    }

    // 按字位簇（grapheme）切分文本：连续 emoji 簇聚成一段用 emoji 字体（保组合序列整形为彩色字形），
    // 其余文本段用继承的默认字体。避免键帽/ZWJ 等组合 emoji 被默认字体拆散成豆腐块。
    static void AppendText(InlineCollection target, string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        var buf = new StringBuilder();
        bool bufEmoji = false;
        void Flush()
        {
            if (buf.Length == 0)
                return;
            var run = new Run(buf.ToString());
            if (bufEmoji)
                run.FontFamily = EmojiFont;
            target.Add(run);
            buf.Clear();
        }
        var en = System.Globalization.StringInfo.GetTextElementEnumerator(text);
        while (en.MoveNext())
        {
            var g = (string)en.Current;
            bool isEmoji = IsEmojiGrapheme(g);
            if (buf.Length > 0 && isEmoji != bufEmoji)
                Flush();
            bufEmoji = isEmoji;
            buf.Append(g);
        }
        Flush();
    }

    // emoji 字位簇判定：簇内任一码位落在 emoji 区段，或含变体选择符 U+FE0F / 围框键帽 U+20E3。
    static bool IsEmojiGrapheme(string g)
    {
        foreach (var rune in g.EnumerateRunes())
        {
            int v = rune.Value;
            if (v == 0xFE0F || v == 0x20E3) return true;          // 变体选择符 / 围框键帽组合符
            if (v >= 0x1F000 && v <= 0x1FAFF) return true;        // 主 emoji 区
            if (v >= 0x1F1E6 && v <= 0x1F1FF) return true;        // 区域指示符（国旗）
            if (v >= 0x2600 && v <= 0x27BF) return true;          // 杂项符号 + 装饰符
            if (v >= 0x2300 && v <= 0x23FF) return true;          // 技术符号（⌚⏰ 等）
            if (v >= 0x2B00 && v <= 0x2BFF) return true;          // 杂项符号箭头（⭐ 等）
        }
        return false;
    }

    static void RenderInlines(ContainerInline container, InlineCollection target, string? baseDir)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    AppendText(target, lit.Content.ToString());
                    break;
                case EmphasisInline em:
                {
                    // '~' → 删除线；DelimiterCount>=2 → 粗体；否则斜体。
                    Span span = em.DelimiterChar == '~' ? new Span { TextDecorations = TextDecorations.Strikethrough }
                              : em.DelimiterCount >= 2 ? new Bold()
                              : new Italic();
                    RenderInlines(em, span.Inlines, baseDir);
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
                    {
                        // 行内图片：能解析到本地文件才内嵌；否则退回显示替代文字（alt），避免整块丢失。
                        var inlineImg = baseDir != null ? TryCreateImage(baseDir, link.Url, block: false) : null;
                        if (inlineImg != null)
                            target.Add(new InlineUIContainer(inlineImg) { BaselineAlignment = BaselineAlignment.TextBottom });
                        else
                            AppendText(target, GetInlineText(link));
                        break;
                    }
                    var text = GetInlineText(link);
                    if (string.IsNullOrEmpty(text))
                        text = link.Url ?? string.Empty;
                    target.Add(LinkInlineUI(text, link.Url));
                    break;
                }
                case AutolinkInline auto:
                    target.Add(LinkInlineUI(auto.Url, auto.Url));
                    break;
                case TaskList task:
                    // - [x]/[ ] → 勾选框字形（用基础 Unicode，避免依赖 emoji 字体）。
                    target.Add(new Run(task.Checked ? "☑ " : "☐ ") { Foreground = Style.HIGH_LIGHT.ToBrush() });
                    break;
                case LineBreakInline:
                    target.Add(new LineBreak());
                    break;
                case ContainerInline c:
                    RenderInlines(c, target, baseDir); // 其他容器型内联：递归子节点
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
