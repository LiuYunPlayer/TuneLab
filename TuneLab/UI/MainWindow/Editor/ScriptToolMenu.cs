using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.I18N;
using TuneLab.Scripting;
using TuneLab.Utils;

namespace TuneLab.UI;

// 用户脚本工具 → 菜单的桥（UI 侧）。脚本库里定义了 getScriptInfo 的脚本即"工具"，按其 context 注册：
//   global → 顶部 Scripts 菜单（按 category 分组）；note/part/track → 对应右键菜单。
// 由 Editor 用一次性 Init 注入"当前工程 / 当前 part / 量化"访问器（项目随新建/打开切换，故用访问器而非快照），
// 各右键菜单站点只需一行 AppendContextTools 即可。运行复用独立脚本模块 ScriptRunner（整段 = 一个可撤销单位、出错原子回退）。
internal static class ScriptToolMenu
{
    static Func<IProject?>? sProject;
    static Func<IMidiPart?>? sCurrentPart;
    static Func<IQuantization?>? sQuantization;
    static Func<ScriptSelection?>? sSelection;

    public static void Init(Func<IProject?> project, Func<IMidiPart?> currentPart, Func<IQuantization?> quantization, Func<ScriptSelection?> selection)
    {
        sProject = project;
        sCurrentPart = currentPart;
        sQuantization = quantization;
        sSelection = selection;
    }

    static List<ScriptToolInfo> Discover()
    {
        var project = sProject?.Invoke();
        if (project == null) return new List<ScriptToolInfo>();
        return ScriptTools.Discover(project, sCurrentPart, sQuantization, () => TranslationManager.CurrentLanguage.Value);
    }

    // 顶部 Scripts 菜单的菜单项（global 工具，按 category 分组：有 category 的进同名子菜单，无的直接列）。
    // 空时给一个禁用占位项，便于发现"脚本工具放这里"。
    public static IReadOnlyList<Control> BuildGlobalMenuItems(Control anchor)
    {
        var tools = Discover().Where(t => t.Context == ScriptToolContext.Global)
                              .OrderBy(t => t.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (tools.Count == 0)
            return new Control[] { new MenuItem { Header = "No script tools".Tr(TC.Menu), IsEnabled = false } };

        var items = new List<Control>();
        var categorized = new Dictionary<string, MenuItem>();
        foreach (var tool in tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Category))
            {
                items.Add(MakeItem(tool, anchor));
            }
            else
            {
                if (!categorized.TryGetValue(tool.Category, out var sub))
                {
                    sub = new MenuItem { Header = tool.Category };
                    categorized[tool.Category] = sub;
                    items.Add(sub);
                }
                sub.Items.Add(MakeItem(tool, anchor));
            }
        }
        return items;
    }

    // 注入项的标记，便于"只建一次"的菜单（TrackHead）每次打开时清掉上轮注入再重建。
    const string ToolTag = "script-tool";

    static List<ScriptToolInfo> ToolsFor(ScriptToolContext context)
        => Discover().Where(t => t.Context == context)
                     .OrderBy(t => t.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();

    // 在某右键菜单末尾追加该 context 下的工具项（前置一条分隔线）。无工具则什么都不加。
    // 用于每次右键都重新构建的菜单（钢琴卷帘音符/空白、编排区命中 part/空白）。
    public static void AppendContextTools(ItemCollection items, ScriptToolContext context, Control anchor)
    {
        var tools = ToolsFor(context);
        if (tools.Count == 0) return;

        items.Add(new Separator { Tag = ToolTag });
        foreach (var tool in tools)
        {
            var item = MakeItem(tool, anchor);
            item.Tag = ToolTag;
            items.Add(item);
        }
    }

    // 用于"只建一次"的菜单（TrackHead）：每次打开先移除上轮注入的工具项，再按当前脚本库重建。挂 menu.Opening。
    public static void RefreshContextTools(ContextMenu menu, ScriptToolContext context, Control anchor)
    {
        for (int i = menu.Items.Count - 1; i >= 0; i--)
            if (menu.Items[i] is Control c && Equals(c.Tag, ToolTag))
                menu.Items.RemoveAt(i);
        AppendContextTools(menu.Items, context, anchor);
    }

    static MenuItem MakeItem(ScriptToolInfo tool, Control anchor)
    {
        var item = new MenuItem { Header = tool.DisplayName };
        item.Command = ReactiveUI.ReactiveCommand.Create(() => Run(tool, anchor));
        return item;
    }

    static void Run(ScriptToolInfo tool, Control anchor)
    {
        var project = sProject?.Invoke();
        if (project == null) return;

        string code;
        try { code = ScriptLibrary.Read(tool.ScriptName); }
        catch (Exception ex)
        {
            _ = anchor.ShowMessage("Script".Tr(TC.Menu), "Failed to load script:".Tr(TC.Dialog) + " " + tool.ScriptName + "\n" + ex.Message);
            return;
        }

        ScriptRunResult result;
        try { result = ScriptRunner.Run(project, sCurrentPart, sQuantization, () => TranslationManager.CurrentLanguage.Value, sSelection, ScriptLimits.Interactive, code, CancellationToken.None); }
        catch (Exception ex)
        {
            _ = anchor.ShowMessage("Script".Tr(TC.Menu), "Host error:".Tr(TC.Dialog) + " " + ex.Message);
            return;
        }

        // 出错弹窗告知（菜单工具无可见 console）；成功静默（改动已落地，Ctrl+Z 可撤销）。
        // result.Error 来自脚本引擎（技术性英文，不翻译）；包裹说明走翻译。
        if (!result.Ok)
            _ = anchor.ShowMessage("Script".Tr(TC.Menu), result.Error + "\n" + "All changes were rolled back; the project is unchanged.".Tr(TC.Dialog));
    }
}
