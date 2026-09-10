using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Platform;
using TuneLab.I18N;

namespace TuneLab.Setup;

/// <summary>
/// 安装器自带的轻量 i18n 引导：源码字符串即英文（默认 DummyTranslator 原样返回），
/// 额外携带与主程序对齐的各语言 toml。初始语言优先取主程序用户设置里已保存的语言，没有则回退英文。
/// 复用 TuneLab.I18N 的 TranslationManager/.Tr 机制，翻译数据由安装器自带一份小的。
/// </summary>
internal static class SetupI18N
{
    // 安装器内嵌的翻译（英文为源，en-US.toml 为空占位使英文可选）。新增语言时在此登记并放置对应 Assets/Translations/*.toml。
    // 与主程序 TuneLab/Resources/Translations 的语言集对齐。
    static readonly string[] BundledLanguages =
    {
        "en-US", "zh-CN", "zh-TW", "ja-JP", "ko-KR", "es-US", "pt-BR", "fr-FR",
        "nl-NL", "it-IT", "el-GR", "ru-RU", "uk-UA", "de-DE", "sv-SE", "tr-TR",
    };

    // 翻译上下文分类（对应 toml 的 [Setup] 段）。
    public const string Ctx = "Setup";

    /// <summary>随安装器分发的语言集。命令行的 -language 认这些码，用法里也照着列。</summary>
    public static IReadOnlyList<string> SupportedLanguages => BundledLanguages;

    public static void Init()
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "TuneLab.Setup.i18n");
            Directory.CreateDirectory(dir);

            foreach (var lang in BundledLanguages)
                ExtractToml(lang, dir);

            TranslationManager.Init(dir);

            // 初始语言：优先用主程序用户设置里已保存的语言（与主程序共用 Settings.json）；
            // 没有（或匹配不到）则留默认英文。用户可在窗口下拉里改，改后写回设置文件。
            var saved = Core.UserSettings.ReadLanguage();
            var pick = saved != null ? Match(saved) : null;
            if (pick != null)
                TranslationManager.CurrentLanguage.Value = pick;
        }
        catch
        {
            // i18n 属锦上添花，失败则整体回退英文，不影响安装。
        }
    }

    // 在【随包分发的语言集】里按码不分大小写找一个匹配项，无则 null。
    // 【查 BundledLanguages 而不是 TranslationManager.Languages】后者要等 Init() 把 toml 解出来才有内容，
    // 而静默安装（-silent）根本不起 Avalonia、也就不会调 Init——那条路上问"这个语言码认不认"时，
    // 运行期列表还是空的，于是每个语言码都会被判成不认识。语言集本就是编译期常量，直接问它。
    public static string? Match(string language)
    {
        foreach (var available in BundledLanguages)
            if (string.Equals(available, language, StringComparison.OrdinalIgnoreCase))
                return available;
        return null;
    }

    static void ExtractToml(string lang, string destDir)
    {
        var uri = new Uri($"avares://TuneLab.Setup/Assets/Translations/{lang}.toml");
        if (!AssetLoader.Exists(uri))
            return;
        using var s = AssetLoader.Open(uri);
        using var fs = File.Create(Path.Combine(destDir, lang + ".toml"));
        s.CopyTo(fs);
    }
}
