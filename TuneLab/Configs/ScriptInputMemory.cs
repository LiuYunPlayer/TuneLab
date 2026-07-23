using System;
using System.IO;
using Newtonsoft.Json.Linq;
using TuneLab.Foundation;
using TuneLab.Utils;

namespace TuneLab.Configs;

// 脚本入参窗「上次输入值」的持久化（按脚本稳定 id 一份）。不是用户可调项，故不放进 Settings/EditorState，
// 自带独立 JSON（照 RecentSoundSourceManager 范式）。文件是 { scriptId: { 值对象 } } 的映射，值经 PropertyJsonUtils
// 与工程/扩展设置同一套转换存取。schema 变动导致的陈旧键由 PropertyObjectController 的按字段默认兜底容错
// （缺字段回默认、多余字段 main 忽略）。懒加载，首次访问即读盘。
internal static class ScriptInputMemory
{
    static JObject mRoot = new();
    static bool mLoaded;

    static void EnsureLoaded()
    {
        if (mLoaded)
            return;
        mLoaded = true;
        var path = PathManager.ScriptInputsFilePath;
        if (!File.Exists(path))
            return;
        try { mRoot = JObject.Parse(File.ReadAllText(path)); }
        catch (Exception ex) { Log.Error("Failed to load script inputs: " + ex); mRoot = new JObject(); }
    }

    // 取某脚本上次输入值；无记录返回空对象（各字段由控件按 config 默认兜底）。
    public static PropertyObject Load(string scriptId)
    {
        EnsureLoaded();
        return mRoot[scriptId] is JObject o ? PropertyJsonUtils.ToPropertyObject(o) : PropertyObject.Empty;
    }

    // 记住某脚本本次输入值，即时存盘。
    public static void Save(string scriptId, PropertyObject values)
    {
        EnsureLoaded();
        mRoot[scriptId] = PropertyJsonUtils.ToJson(values);
        var path = PathManager.ScriptInputsFilePath;
        try
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            File.WriteAllText(path, mRoot.ToString());
        }
        catch (Exception ex) { Log.Error("Failed to save script inputs: " + ex); }
    }
}
