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
                return new TomlTranslator(languageFile,new Dictionary<string, string>() {
                    /*这里加Dictionary来Load插件语言包
                     * 参数1：string，为插件名，也是Context的名字
                     * 参数2：string，为插件语言包toml地址，可以为空。此部分需要和插件管理器联动，再看。
                    */
                    /*
                     * 测试样例：
                     * new TomlTranslator("主语言包.toml",new Dictionary<string,string>(){ {"VOCALOID5","插件语言包.toml"} });
                     * 
                     * 插件语言包内容：
                     * NoteLanguage = "音符语言"
                     * [G2PA]
                     * JPN="日语"
                     * CHS="中文"
                     * 
                     * 主要语言包内容：
                     * [Dialog]
                     * File="文件"
                     * [VOCALOID5]
                     * G2PA.CHS="汉语"
                     * 
                     * 加载完成后，实际TOML模型内容是这样的：
                     * [Dialog]
                     * File="文件"
                     * [VOCALOID5]
                     * NoteLanugae = "音符语言"
                     * [VOCALOID5.G2PA]
                     * JPN="日语"
                     * CHS="汉语"
                     */
                });
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
