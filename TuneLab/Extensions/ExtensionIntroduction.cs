using System.IO;

namespace TuneLab.Extensions;

// 条目级 introduction 文件的落地：把 manifest 声明的包内相对路径解析成绝对路径。
// 与 icon 同一套路（见 ExtensionManager.ResolveIconPath）：未声明、或声明了但文件不存在 → null，
// 调用方按「无文档」处理。
//
// 宿主【不再】按 README.md 之类的约定名去发现文档：README 是作者可发可不发的自留文件（面向仓库读者的
// 完整自我介绍，含 build/license/贡献指南），不是宿主认可的元数据。要让宿主展示什么、由 manifest 显式声明。
//
// 语言变体在声明层就已选定（ExtensionInfo.LocalizedIntroduction 走 localizations 覆盖，各语言可指不同
// 文件名），故本类不做任何文件名猜测。
internal static class ExtensionIntroduction
{
    public static string? Resolve(string packageDir, ExtensionInfo ext, string language)
    {
        if (string.IsNullOrEmpty(packageDir))
            return null;

        var relative = ext.LocalizedIntroduction(language);
        if (string.IsNullOrWhiteSpace(relative))
            return null;

        var full = Path.Combine(packageDir, relative);
        return File.Exists(full) ? full : null;
    }
}
