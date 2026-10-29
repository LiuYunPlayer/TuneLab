using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TuneLab.Foundation;
using TuneLab.I18N;

namespace TuneLab.Docs;

// 手册的一章。Body 是原文（含插图语法，供应用内手册窗渲染）；喂模型的纯文本走 ForModel。
internal sealed record ManualSection(string Id, string Title, string Body, IReadOnlyList<string> Subheadings);

// 随包用户手册的加载与切分。唯一真相源是仓库里的 docs/user-manual.{文化码}.md（构建时 Link 进
// Resources/Manual/{文化码}.md，见 TuneLab.csproj），故手册与软件同版本、不会各说一套。
//
// 应用内手册窗与 agent 的 get_manual 共用本类：两者对「哪些章、章叫什么、章从哪到哪」必须同一口径，
// 否则用户照手册窗提问、agent 却按另一套章节回答。
internal static class ManualLibrary
{
    // 章节锚点：手册里每个 "## " 标题前的 <!-- section: id --> 注释。id 是稳定引用（标题可改、编号可变）。
    static readonly Regex SectionMarker = new(@"^<!--\s*section:\s*([a-z0-9\-]+)\s*-->\s*$", RegexOptions.Compiled);
    static readonly Regex ImageLine = new(@"^\s*!\[[^\]]*\]\([^)]*\)\s*$", RegexOptions.Compiled);
    static readonly Regex InlineImage = new(@"!\[[^\]]*\]\([^)]*\)", RegexOptions.Compiled);
    static readonly Regex HtmlComment = new(@"<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);
    // 标题里的章号（"## 10. 参数面板" → "参数面板"）：编号会随插章变动，不该进 id 也不必进引用。
    static readonly Regex LeadingNumber = new(@"^\d+\.\s*", RegexOptions.Compiled);

    // 插图的相对路径以手册文件所在目录为基准（手册里写作 images/manual/*.png）。
    public static string BaseDir => PathManager.ManualFolder;

    public static bool IsAvailable => ResolvePath() != null;

    // 当前语言那份手册文件的绝对路径（应用内手册窗「用外部编辑器打开」用）；手册没随包时为 null。
    public static string? FilePath => ResolvePath();

    // 手册实际用的语言（可能因缺失而回退），如 "zh-CN"；没有手册时为 null。
    public static string? Language
    {
        get
        {
            var path = ResolvePath();
            return path == null ? null : Path.GetFileNameWithoutExtension(path);
        }
    }

    // 当前界面语言是否有对应的手册（否则调用方该提示「暂无本语言版本」）。
    public static bool IsCurrentLanguage => Language != null && Language == CurrentCulture;

    public static IReadOnlyList<ManualSection> Sections
    {
        get
        {
            var path = ResolvePath();
            if (path == null)
                return [];

            // 缓存到 (路径, 写入时间)：开发期改了手册无需重启即可看到新内容。
            var stamp = SafeWriteTime(path);
            if (mCachePath == path && mCacheStamp == stamp && mCache != null)
                return mCache;

            try
            {
                mCache = Parse(File.ReadAllText(path));
                mCachePath = path;
                mCacheStamp = stamp;
                return mCache;
            }
            catch (Exception ex)
            {
                Log.Error("Failed to read user manual: " + ex);
                return [];
            }
        }
    }

    public static ManualSection? Find(string idOrTitle)
    {
        if (string.IsNullOrWhiteSpace(idOrTitle))
            return null;

        var key = idOrTitle.Trim();
        var sections = Sections;
        return sections.FirstOrDefault(s => string.Equals(s.Id, key, StringComparison.OrdinalIgnoreCase))
            ?? sections.FirstOrDefault(s => s.Title.Contains(key, StringComparison.OrdinalIgnoreCase))
            ?? sections.FirstOrDefault(s => s.Subheadings.Any(h => h.Contains(key, StringComparison.OrdinalIgnoreCase)));
    }

    // 目录：章 id + 标题 + 子节标题。给模型的「先看目录再取节」那一步，也给手册窗建左侧列表。
    public static string BuildToc()
    {
        var sb = new StringBuilder();
        foreach (var section in Sections)
        {
            sb.Append("- ").Append(section.Id).Append(" — ").Append(section.Title);
            if (section.Subheadings.Count > 0)
                sb.Append(" (").Append(string.Join(" / ", section.Subheadings)).Append(')');
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // 关键词检索：逐行匹配，报出命中所在的章 id + 该行原文（截断）。给模型定位用，不做排序打分。
    public static IReadOnlyList<(string SectionId, string SectionTitle, string Line)> Search(string query, int maxHits = 30)
    {
        var hits = new List<(string, string, string)>();
        if (string.IsNullOrWhiteSpace(query))
            return hits;

        foreach (var section in Sections)
        {
            foreach (var raw in section.Body.Split('\n'))
            {
                if (hits.Count >= maxHits)
                    return hits;
                var line = raw.Trim();
                if (line.Length == 0 || !line.Contains(query, StringComparison.OrdinalIgnoreCase))
                    continue;
                hits.Add((section.Id, section.Title, line.Length > 200 ? line[..200] + "…" : line));
            }
        }
        return hits;
    }

    // 喂模型的纯文本：剥掉插图与 HTML 注释（对模型是噪声），压掉多余空行。
    public static string ForModel(ManualSection section)
    {
        var sb = new StringBuilder();
        sb.Append("## ").AppendLine(section.Title);
        bool lastBlank = false;
        foreach (var raw in section.Body.Split('\n'))
        {
            if (ImageLine.IsMatch(raw))
                continue;
            var line = HtmlComment.Replace(InlineImage.Replace(raw, string.Empty), string.Empty).TrimEnd();
            bool blank = line.Trim().Length == 0;
            if (blank && lastBlank)
                continue;
            lastBlank = blank;
            sb.AppendLine(line);
        }
        return sb.ToString().TrimEnd() + "\n";
    }

    static IReadOnlyList<ManualSection> Parse(string text)
    {
        var sections = new List<ManualSection>();
        string? pendingId = null;
        string? title = null;
        string? id = null;
        var body = new StringBuilder();
        var subheadings = new List<string>();

        void Flush()
        {
            if (title == null)
                return;
            sections.Add(new ManualSection(id ?? Slug(title), title, body.ToString().Trim(), subheadings.ToList()));
            body.Clear();
            subheadings.Clear();
        }

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var marker = SectionMarker.Match(raw);
            if (marker.Success)
            {
                pendingId = marker.Groups[1].Value;
                continue;
            }

            if (raw.StartsWith("## "))
            {
                Flush();
                title = LeadingNumber.Replace(raw[3..].Trim(), string.Empty);
                id = pendingId;
                pendingId = null;
                continue;
            }

            if (title == null)
                continue;   // 章前的引言（文档标题、目录）不属任何章

            if (raw.StartsWith("### "))
                subheadings.Add(raw[4..].Trim());
            body.AppendLine(raw);
        }
        Flush();

        // 「目录」章对读者是导航、对模型是重复信息，两处都用不上：本类自己就给目录。
        return sections.Where(s => s.Id != "toc" && s.Title != "目录" && s.Title != "Contents").ToList();
    }

    // 手册里的 #锚点 → 它所在章的序号（三级标题落到所在的章）；找不到返回 -1。锚点按 GitHub 给标题生成锚点的规则算
    //（AnchorOf）。章标题解析时去掉了章号（「15. 扩展管理」→「扩展管理」），锚点里却带着（#15-扩展管理）：比章标题时
    // 先去掉锚点开头的「数字-」。应用内手册窗口用它把正文里的跳转链接滚到对应章。
    public static int IndexOfAnchor(IReadOnlyList<ManualSection> sections, string anchor)
    {
        var withoutNumber = Regex.Replace(anchor, @"^\d+-", string.Empty);
        for (int i = 0; i < sections.Count; i++)
        {
            var title = AnchorOf(sections[i].Title);
            if (title == anchor || title == withoutNumber)
                return i;
        }
        for (int i = 0; i < sections.Count; i++)
        {
            foreach (var heading in sections[i].Subheadings)
            {
                if (AnchorOf(heading) == anchor)
                    return i;
            }
        }
        return -1;
    }

    // GitHub 的标题锚点：转小写，去掉字母、数字、空格、连字符与下划线以外的字符（中文算字母），空格换成连字符。
    public static string AnchorOf(string heading)
    {
        var sb = new StringBuilder();
        foreach (char c in heading.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                sb.Append(c);
            else if (c == ' ')
                sb.Append('-');
        }
        return sb.ToString();
    }

    static string Slug(string title)
        => new string(title.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');

    // 语言选择（同 Resources/ScriptDoc 范式）：当前界面语言 → en-US → 目录里任意一份。
    static string? ResolvePath()
    {
        try
        {
            if (!Directory.Exists(BaseDir))
                return null;

            foreach (var candidate in new[] { CurrentCulture, "en-US" })
            {
                if (string.IsNullOrEmpty(candidate))
                    continue;
                var path = Path.Combine(BaseDir, candidate + ".md");
                if (File.Exists(path))
                    return path;
            }
            return Directory.EnumerateFiles(BaseDir, "*.md").OrderBy(p => p).FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to locate user manual: " + ex);
            return null;
        }
    }

    static string CurrentCulture => TranslationManager.CurrentLanguage.Value is { Length: > 0 } lang
        ? lang : CultureInfo.CurrentUICulture.Name;

    static DateTime SafeWriteTime(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    static IReadOnlyList<ManualSection>? mCache;
    static string? mCachePath;
    static DateTime mCacheStamp;
}
