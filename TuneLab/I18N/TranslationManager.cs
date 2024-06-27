using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.I18N;

internal class TranslationManager
{
    public static string GetCurrentOSLanguage()
    {
        CultureInfo currentCulture = CultureInfo.CurrentCulture;
        string currentLanguage = currentCulture.Name;
        return currentLanguage;
    }
    public static string[] GetAvaliableLanguages()
    {
        List<string> languages = new List<string>() { "en-US" };
        DirectoryInfo i18nfolder = new DirectoryInfo(PathManager.TranslationsFolder);
        foreach (FileInfo i18nfile in i18nfolder.GetFiles("*.toml"))
        {
            var i18nName = Path.GetFileNameWithoutExtension(i18nfile.FullName);
            if (!languages.Contains(i18nName)) languages.Add(i18nName);
        }
        return languages.ToArray();
    }
    public static string CurrentLanguage { get; set; } = GetCurrentOSLanguage();
    public static ITranslator CurrentTranslator { get => GetLanguage(CurrentLanguage); }
    public static ITranslator GetLanguage(string languageID)
    {
        ITranslator ret;
        if (!languageMap.TryGetValue(languageID, out ret))
        {
            string languageFile = Path.Combine(PathManager.TranslationsFolder, string.Format("{0}.toml", languageID));
            if (Path.Exists(languageFile))
            {
                return new TomlTranslator(languageFile);
            }
            else
            {
                return new DummyTranslator();
            }
        }
        return ret;
    }

    private static Dictionary<string, ITranslator> languageMap = new();
}
