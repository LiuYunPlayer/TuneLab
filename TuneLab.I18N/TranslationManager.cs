using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;

namespace TuneLab.I18N;

internal static class TranslationManager
{
    static readonly ITranslator DefaultTranslator = new DummyTranslator();
    public static IReadOnlyList<string> Languages => mLanguages;
    public static NotifiableProperty<string> CurrentLanguage { get; } = string.Empty;
    public static ITranslator CurrentTranslator { get; private set; } = DefaultTranslator;

    static TranslationManager()
    {
        CurrentLanguage.Modified.Subscribe(() => { CurrentTranslator = GetTranslator(CurrentLanguage); });
    }

    public static void Init(string path)
    {
        DirectoryInfo resDir = new DirectoryInfo(path);
        if (!resDir.Exists) return;
        // 仅登记可选语言与其文件路径，不在此解析 toml——只有真正被选中的语言才会在 GetTranslator 里懒加载。
        // 切换语言强制重启，运行期至多用到一两个 translator，急切解析全部（~16 份）是启动路径上的浪费。
        foreach (var file in resDir.GetFiles("*.toml"))
        {
            var i18nName = Path.GetFileNameWithoutExtension(file.FullName);
            if (mPaths.ContainsKey(i18nName))
                continue;

            mPaths.Add(i18nName, file.FullName);
            mLanguages.Add(i18nName);
        }
    }

    public static string GetCurrentOSLanguage()
    {
        CultureInfo currentCulture = CultureInfo.CurrentCulture;
        string currentLanguage = currentCulture.Name;
        return currentLanguage;
    }

    public static ITranslator GetTranslator(string language)
    {
        // 已加载过的直接复用（如启动时选中的语言）；否则按登记路径懒加载并缓存。
        if (mTranslators.TryGetValue(language, out var translator))
            return translator;

        if (mPaths.TryGetValue(language, out var path))
        {
            translator = new TomlTranslator(path);
            mTranslators.Add(language, translator);
            return translator;
        }

        return DefaultTranslator;
    }

    // 语言在其自身语言下的外显名（endonym / 自称），用于设置里语言下拉的显示文本。
    // 刻意写死在代码里、不放进各 toml：这样列出可选语言无需读盘解析任何翻译文件，懒加载才得以保留。
    // 未收录的语言回退到 CultureInfo.NativeName（含地区括号），再退到文化代码本身。
    public static string GetDisplayName(string language)
    {
        if (mDisplayNames.TryGetValue(language, out var name))
            return name;

        try
        {
            var native = new CultureInfo(language).NativeName;
            if (!string.IsNullOrEmpty(native))
                return native;
        }
        catch (CultureNotFoundException) { }

        return language;
    }

    static readonly Dictionary<string, string> mDisplayNames = new()
    {
        ["de-DE"] = "Deutsch",
        ["el-GR"] = "Ελληνικά",
        ["en-US"] = "English",
        ["es-US"] = "Español",
        ["fr-FR"] = "Français",
        ["it-IT"] = "Italiano",
        ["ja-JP"] = "日本語",
        ["ko-KR"] = "한국어",
        ["nl-NL"] = "Nederlands",
        ["pt-BR"] = "Português",
        ["ru-RU"] = "Русский",
        ["sv-SE"] = "Svenska",
        ["tr-TR"] = "Türkçe",
        ["uk-UA"] = "Українська",
        ["zh-CN"] = "简体中文",
        ["zh-TW"] = "繁體中文",
    };

    static Dictionary<string, string> mPaths = [];
    static Dictionary<string, ITranslator> mTranslators = [];
    static List<string> mLanguages = [];
}
