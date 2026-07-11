using System.IO;
using System.Linq;

namespace TuneLab.UI;

// 包级 README 详情正文的发现：只在包根目录按约定文件名找，内部内容全由作者定义、宿主不解释。
// 语言回退：当前语言精确匹配 README.<lang>.md → 基准 README.md（与 manifest 的 localizations 同回退语义）。
// 文件名大小写不敏感（跨平台：Windows FS 本就不敏感，Linux 下也按不敏感匹配，容忍作者写成 readme.md）。
internal static class ExtensionReadme
{
    // 返回解析到的 README 绝对路径；无则 null。dir 为包目录（= ExtensionLoadResult.DirectoryPath）。
    public static string? Resolve(string dir, string language)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return null;

        var files = Directory.EnumerateFiles(dir).ToArray();

        if (!string.IsNullOrEmpty(language))
        {
            var localized = Match(files, $"README.{language}.md");
            if (localized != null)
                return localized;
        }

        return Match(files, "README.md");
    }

    static string? Match(string[] files, string name)
        => files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, System.StringComparison.OrdinalIgnoreCase));
}
