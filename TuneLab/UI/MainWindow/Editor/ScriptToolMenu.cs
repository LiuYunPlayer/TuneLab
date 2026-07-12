using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.GUI.Input;
using TuneLab.I18N;
using TuneLab.Input;
using TuneLab.Scripting;
using TuneLab.Utils;
using KeyBinding = TuneLab.GUI.Input.KeyBinding;

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
    static Func<ScriptPianoSelection?>? sPianoSelection;

    public static void Init(Func<IProject?> project, Func<IMidiPart?> currentPart, Func<IQuantization?> quantization, Func<ScriptSelection?> selection, Func<ScriptPianoSelection?> pianoSelection)
    {
        sProject = project;
        sCurrentPart = currentPart;
        sQuantization = quantization;
        sSelection = selection;
        sPianoSelection = pianoSelection;
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

    // 已注册的脚本命令 id 集（用于增删同步）。
    static readonly HashSet<string> sRegisteredCommandIds = new();

    // 把脚本库里全部工具脚本同步为可绑定命令。随 Scripts 菜单重建调用。id/默认手势/作用域的解析规则见
    // docs/keybinding-system.md §6：
    //  · id = script:<稳定 id>（getScriptInfo.id 合法时）否则 script:<文件名>；同一 id 被多脚本声明则各自忠实
    //    降级回文件名（文件名由文件系统保证唯一）。稳定 id 让重命名/重装不丢用户绑定。
    //  · 作用域按 context 收窄到"该脚本存活的焦点子树"（见 ScopeFor）：piano 侧 → PianoWindow、编排侧 → TrackWindow、
    //    global → Editor（编排脚本不越界钢琴窗、反之亦然）。
    //  · 声明的默认手势原样采用（不再"空槽才落"）；若撞了同作用域的内建/别的脚本，不静默丢弃也不夺键——分发按
    //    注册序确定生效者（内建恒胜），冲突由设置页持久警示（Keymap.SameScopeConflictPeers）交用户消解。
    //  · 消失脚本的用户 override 由 Keymap 静默保留（缺 id 即不进分发索引），脚本回归即复活。
    public static void SyncKeyCommands(Control anchor)
    {
        // 干净重来：先注销上轮全部脚本命令，令本轮"空槽"判定只对内建 + 本轮已处理脚本可见（先到先得可复现）。
        // 用户 override 存在 Keymap 内、与注册独立，注销不丢。
        foreach (var id in sRegisteredCommandIds)
            Keymap.Unregister(id);
        sRegisteredCommandIds.Clear();

        // List() 为确定序（OrdinalIgnoreCase），保证默认手势"先到先得"消解可复现。
        var tools = Discover();

        // 候选 id 令牌：声明 id 合法用之，否则文件名。同候选被多脚本占用则全部回落文件名。
        string Candidate(ScriptToolInfo t) => IsValidDeclaredId(t.DeclaredId) ? t.DeclaredId! : t.ScriptName;
        var candidateCount = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in tools)
            candidateCount[Candidate(t)] = candidateCount.GetValueOrDefault(Candidate(t)) + 1;

        var finalIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            if (tool.DeclaredId != null && !IsValidDeclaredId(tool.DeclaredId))
                Log.Warning(string.Format("Script \"{0}\": invalid declared id \"{1}\" (allowed chars: A-Z a-z 0-9 . _ -); using filename as keybinding id.", tool.ScriptName, tool.DeclaredId));

            var candidate = Candidate(tool);
            string token = candidate;
            if (candidateCount[candidate] > 1)
            {
                token = tool.ScriptName;   // 撞车：回落各自唯一的文件名
                Log.Warning(string.Format("Keybinding id \"{0}\" is declared by more than one script; \"{1}\" falls back to its filename.", candidate, tool.ScriptName));
            }

            var id = "script:" + token;
            if (!finalIds.Add(id))
            {
                // 极端残留撞车（如声明 id 恰等于另一脚本的文件名）：后者不注册命令、仅留在菜单，避免夺走前者的 Execute。
                Log.Warning(string.Format("Script \"{0}\": keybinding id \"{1}\" collides with an earlier script; not keybindable this session.", tool.ScriptName, id));
                continue;
            }

            Keymap.Register(new()
            {
                Id = id,
                DisplayName = () => tool.DisplayName,
                Scope = ScopeFor(tool.Context),
                DefaultGesture = ResolveDefaultGesture(tool),
                Execute = () => Run(tool, anchor),
            });
            sRegisteredCommandIds.Add(id);
        }
    }

    // context → 快捷键作用域 = 该脚本"存活的焦点子树"（焦点模型，§6）：
    //   piano 侧(note/partContent/pianoSelection) → PianoWindow；编排侧(part/track/trackContent/trackSelection) → TrackWindow；
    //   global → Editor。内层子树焦点时先命中并截停，外层被遮蔽；故编排脚本不会在钢琴窗焦点时越界触发（反之亦然）。
    static KeyScope ScopeFor(ScriptToolContext context) => context switch
    {
        ScriptToolContext.Note or ScriptToolContext.PartContent or ScriptToolContext.PianoSelection => KeyScope.PianoWindow,
        ScriptToolContext.Part or ScriptToolContext.Track or ScriptToolContext.TrackContent or ScriptToolContext.TrackSelection => KeyScope.TrackWindow,
        _ => KeyScope.Editor,   // Global：编辑器内哪都可达
    };

    // 稳定 id 字符集：ASCII 字母数字 + . _ -（禁 : / + / 空白——它们是前缀/修饰/序列化分隔符）。空或含非法字符即无效。
    static bool IsValidDeclaredId(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return false;
        foreach (var c in id)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
                return false;
        return true;
    }

    // 解析脚本声明的默认手势：令牌串（允许 mod+/primary+ → 本平台主命令键）。原样采用——撞键不在此丢弃，
    // 交给分发的注册序取胜（内建恒胜、不被夺）+ 设置页持久冲突警示（§6、§9）。仅解析失败时忽略并告知。
    static KeyBinding? ResolveDefaultGesture(ScriptToolInfo tool)
    {
        if (tool.DefaultGesture == null)
            return null;
        if (!KeyCodec.TryParseDeclaration(tool.DefaultGesture, out var gesture))
        {
            Log.Warning(string.Format("Script \"{0}\": unparseable defaultGesture \"{1}\"; ignored.", tool.ScriptName, tool.DefaultGesture));
            return null;
        }
        return gesture;
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

        // 仅当菜单已有项时才前置分隔线（选区菜单在无 copy/paste 时可能为空，避免顶部裸分隔线）。
        if (items.Count > 0)
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
        try { result = ScriptRunner.Run(project, sCurrentPart, sQuantization, () => TranslationManager.CurrentLanguage.Value, sSelection, sPianoSelection, ScriptLimits.Interactive, code, CancellationToken.None); }
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
