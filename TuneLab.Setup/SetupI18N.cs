using System;
using System.IO;
using Avalonia.Platform;
using TuneLab.I18N;

namespace TuneLab.Setup;

/// <summary>
/// 安装器自带的轻量 i18n 引导：源码字符串即英文（默认 DummyTranslator 原样返回），
/// 仅额外携带 zh-CN.toml。按系统区域自动切中文；其它语言回退英文。
/// 复用 TuneLab.I18N 的 TranslationManager/.Tr 机制，翻译数据由安装器自带一份小的。
/// </summary>
internal static class SetupI18N
{
    // 安装器内嵌的翻译（英文为源、无需 toml）。新增语言时在此登记并放置对应 Assets/Translations/*.toml。
    static readonly string[] BundledLanguages = { "zh-CN" };

    // 翻译上下文分类（对应 toml 的 [Setup] 段）。
    public const string Ctx = "Setup";

    public static void Init()
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "TuneLab.Setup.i18n");
            Directory.CreateDirectory(dir);

            foreach (var lang in BundledLanguages)
                ExtractToml(lang, dir);

            TranslationManager.Init(dir);

            // 系统区域 → 选语言：zh-* 一律用 zh-CN（目前只带简体），否则留默认英文。
            var os = TranslationManager.GetCurrentOSLanguage();
            string lang2 = os.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh-CN" : os;
            foreach (var available in TranslationManager.Languages)
            {
                if (string.Equals(available, lang2, StringComparison.OrdinalIgnoreCase))
                {
                    TranslationManager.CurrentLanguage.Value = available;
                    break;
                }
            }
        }
        catch
        {
            // i18n 属锦上添花，失败则整体回退英文，不影响安装。
        }
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
