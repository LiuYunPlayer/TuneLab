using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TuneLab.Foundation.Event;

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
        foreach (var file in resDir.GetFiles("*.toml"))
        {
            var i18nName = Path.GetFileNameWithoutExtension(file.FullName);
            if (mTranslators.ContainsKey(i18nName))
                continue;

            var translator = new TomlTranslator(file.FullName);
            mTranslators.Add(i18nName, translator);
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
        if (mTranslators.TryGetValue(language, out var translator))
            return translator; 

        return DefaultTranslator;
    }

    static Dictionary<string, ITranslator> mTranslators = [];
    static List<string> mLanguages = [];
}
