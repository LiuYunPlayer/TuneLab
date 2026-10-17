using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TuneLab.Foundation;
using TuneLab.GUI.Controllers;

namespace TuneLab.Configs;

// 属性面板里「用户收起了哪些字段」的记录。
// 【为何单独一个文件】这是用户习惯，不是可调项，不进 Settings；也不是窗口布局，不进 EditorState——与参数钉选
//（ParameterPins.json）同一类东西、同一种存法：独立 JSON，改了即时存盘。
// 【键】作用域 + 字段路径，如 "part:Voice:SomeEngine/advanced/tension"（作用域由各面板按「面板 + 插件身份」给，
//   见 PropertyObjectController.CollapseScope）。只记收起的——默认全部展开，插件新增的字段自然展开。
internal sealed class FieldFolds : IFieldFoldStore
{
    public static void Init(string path)
    {
        HashSet<string>? collapsed = null;
        if (File.Exists(path))
        {
            try
            {
                collapsed = JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Log.Error("Failed to deserialize collapsed fields: " + ex);
            }
        }
        FieldFoldStore.Current = new FieldFolds(path, collapsed ?? []);
    }

    FieldFolds(string path, HashSet<string> collapsed)
    {
        mPath = path;
        mCollapsed = collapsed;
    }

    public bool IsCollapsed(string key) => mCollapsed.Contains(key);

    public void SetCollapsed(string key, bool collapsed)
    {
        if (!(collapsed ? mCollapsed.Add(key) : mCollapsed.Remove(key)))
            return;
        try
        {
            var folder = Path.GetDirectoryName(mPath);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            File.WriteAllText(mPath, JsonSerializer.Serialize(mCollapsed.Order(StringComparer.Ordinal), JsonSerializerOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save collapsed fields: " + ex);
        }
    }

    readonly string mPath;
    readonly HashSet<string> mCollapsed;
    static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };
}
