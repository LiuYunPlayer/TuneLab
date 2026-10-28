using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab;

internal static class PathManager
{
    public static string AppDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
    // 用户数据根目录（配置/扩展/脚本/日志/自动保存）。默认 %APPDATA%\TuneLab；
    // TUNELAB_DATA_DIR 置位时改指它——文档截图工具（tools/ScreenshotBot）用它把整套数据隔离到
    // 一个干净沙盒里，既不读到真实环境（截图会带上个人设置与已装的第三方插件），也不写坏它。
    public static string TuneLabFolder => Environment.GetEnvironmentVariable("TUNELAB_DATA_DIR") is { Length: > 0 } dir
        ? dir : Path.Combine(AppDataFolder, "TuneLab");
    // History 子目录由 AutoSaveStore 自己拥有（它按对复制与轮换），不在此另立一个会与之重复的定义。
    public static string AutoSaveFolder => Path.Combine(TuneLabFolder, "AutoSave");
    public static string LogsFolder => Path.Combine(TuneLabFolder, "Logs");
    public static string ConfigsFolder => Path.Combine(TuneLabFolder, "Configs");
    public static string SettingsFilePath => Path.Combine(ConfigsFolder, "Settings.json");
    public static string EditorStateFilePath => Path.Combine(ConfigsFolder, "EditorState.json");
    public static string RecentSoundSourcesFilePath => Path.Combine(ConfigsFolder, "RecentSoundSources.json");
    public static string ParameterPinsFilePath => Path.Combine(ConfigsFolder, "ParameterPins.json");
    public static string ParameterHiddenFilePath => Path.Combine(ConfigsFolder, "ParameterHidden.json");
    public static string CollapsedFieldsFilePath => Path.Combine(ConfigsFolder, "CollapsedFields.json");
    // 与用户环境绑定的扩展数据各存各的（同 ExtensionSettings.json，都不进 Settings.json）：
    public static string ExtensionRoutingFilePath => Path.Combine(ConfigsFolder, "ExtensionRouting.json");
    public static string ExtensionActivationFilePath => Path.Combine(ConfigsFolder, "ExtensionActivation.json");
    // 能力位摘要缓存（宿主从作者的 introduction 备好、供 agent 读；派生数据非用户可调项，键是内容哈希）。
    public static string ExtensionSummariesFilePath => Path.Combine(ConfigsFolder, "ExtensionSummaries.json");
    public static string KeybindingsFilePath => Path.Combine(ConfigsFolder, "Keybindings.json");
    public static string ScriptInputsFilePath => Path.Combine(ConfigsFolder, "ScriptInputs.json");
    public static string ExtensionsFolder => Path.Combine(TuneLabFolder, "Extensions");
    public static string AgentSessionsFolder => Path.Combine(TuneLabFolder, "AgentSessions");
    public static string ScriptsFolder => Path.Combine(TuneLabFolder, "Scripts");
    public static string LockFilePath => Path.Combine(TuneLabFolder, "TuneLab.lock");
    public static string LogFilePath { get { mLogFilePath ??= Path.Combine(LogsFolder, "TuneLab_" + DateTime.Now.ToString("yyyy-MM-dd_hh-mm-ss") + ".log"); return mLogFilePath; } }
    public static string ExcutableFolder => AppDomain.CurrentDomain.BaseDirectory;
    public static string ResourcesFolder => Path.Combine(ExcutableFolder, "Resources");
    public static string TranslationsFolder => Path.Combine(ResourcesFolder, "Translations");
    public static string ScriptDocFolder => Path.Combine(ResourcesFolder, "ScriptDoc");
    // 用户手册（随包发布的 docs/user-manual.*.md 与其插图），按文化码取文件，见 ManualLibrary。
    public static string ManualFolder => Path.Combine(ResourcesFolder, "Manual");

    public static void MakeSureExist(string folder)
    {
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);
    }

    static string? mLogFilePath = null;
}
