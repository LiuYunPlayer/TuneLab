using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.SDK;
using TuneLab.Scripting;
using TuneLab.Utils;

namespace TuneLab.UI;

// 「Script」右侧栏：不依赖 agent 的运行脚本面板。用户输入一段 JavaScript，点 Run（或 Ctrl+Enter）对当前工程运行——
// 直接复用独立脚本模块 TuneLab.Scripting（与 agent 的 run_script 共享同一动作面 `tl`）。整段脚本运行 = 一个可撤销单位。
// 工程/当前 part/量化经访问器实时取（与 Agent 侧栏同构），故切工程/切 part 时本面板自动跟随。
internal sealed class ScriptSideBarContentProvider
{
    public IImage Icon => Assets.Script.GetImage(Style.LIGHT_WHITE);
    public string Name => "Script".Tr(this);
    public Control Root => mRoot;

    public void SetProject(IProject? project) => mProject = project;
    public void SetCurrentPartProvider(Func<IMidiPart?> provider) => mCurrentPart = provider;
    public void SetQuantizationProvider(Func<IQuantization?> provider) => mQuantization = provider;
    public void SetSelectionProvider(Func<ScriptSelection?> provider) => mSelection = provider;
    public void SetPianoSelectionProvider(Func<ScriptPianoSelection?> provider) => mPianoSelection = provider;

    readonly DockPanel mRoot = new() { LastChildFill = true, Background = Style.INTERFACE.ToBrush() };
    readonly TextEditor mCodeBox;
    readonly Control mDocView;        // 人类可读文档（Markdown 渲染、自适应宽度换行），与代码框同位互斥
    readonly TextInput mOutputBox;
    bool mShowingDoc;
    IProject? mProject;
    Func<IMidiPart?>? mCurrentPart;
    Func<IQuantization?>? mQuantization;
    Func<ScriptSelection?>? mSelection;
    Func<ScriptPianoSelection?>? mPianoSelection;

    // 脚本库管理：顶部一行 = 左侧脚本选择钮（点开下拉，列库内脚本、行内 ✕ 删除，仿 Agent 会话下拉）+ 右侧 ⋯ 菜单
    // （打开/导入/保存/另存/重命名，仿 Properties 的 preset ⋯ 收起范式）。脚本以 .js 文件存于 PathManager.ScriptsFolder，
    // 由 ScriptLibrary 维护。mCurrentScriptName = 当前编辑器内容所属的库内脚本名；null 表示"未命名/外部打开的工作副本"（不入库）。
    readonly Flyout mScriptFlyout;
    readonly TuneLab.GUI.Components.Button mScriptButton;   // 下拉触发钮，显示当前脚本名
    readonly ButtonContent mScriptLabel;
    readonly TuneLab.GUI.Components.Button mMoreButton;     // ⋯ 菜单钮
    bool mFlyoutJustClosed;
    string? mCurrentScriptName;
    bool mDirty;   // 编辑器内容自上次载入/新建/打开/保存以来是否被改过 → 标题前缀 *

    // 代码框初始示例：全部注释 + 短行（用户上来可直接在下方写，无需删除；行短不撑横向滚动条）。
    const string Sample =
        "// tl 是入口；点 Doc 看完整文档。\n" +
        "// 例：当前 part 所有音符升八度\n" +
        "// const part = tl.currentPart();\n" +
        "// for (const n of part.notes())\n" +
        "//   n.pitch += 12;\n" +
        "\n";

    // 自定义 JS 语法高亮：暗底护眼配色（关键字用柔紫、避免刺眼亮蓝），且不依赖 AvaloniaEdit 内置定义是否存在。
    const string XshdJs = """
        <?xml version="1.0"?>
        <SyntaxDefinition name="JavaScript" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Comment" foreground="#6A9955" />
          <Color name="String" foreground="#CE9178" />
          <Color name="Number" foreground="#B5CEA8" />
          <Color name="Keyword" foreground="#C586C0" />
          <Color name="Literal" foreground="#4EC9B0" />
          <RuleSet ignoreCase="false">
            <Span color="Comment" begin="//" />
            <Span color="Comment" multiline="true" begin="/\*" end="\*/" />
            <Span color="String" begin="&quot;" end="&quot;"><RuleSet><Span begin="\\" end="." /></RuleSet></Span>
            <Span color="String" begin="'" end="'"><RuleSet><Span begin="\\" end="." /></RuleSet></Span>
            <Span color="String" multiline="true" begin="`" end="`" />
            <Keywords color="Keyword">
              <Word>const</Word><Word>let</Word><Word>var</Word><Word>function</Word>
              <Word>return</Word><Word>if</Word><Word>else</Word><Word>for</Word>
              <Word>of</Word><Word>in</Word><Word>while</Word><Word>do</Word>
              <Word>break</Word><Word>continue</Word><Word>switch</Word><Word>case</Word>
              <Word>default</Word><Word>new</Word><Word>typeof</Word><Word>instanceof</Word>
              <Word>throw</Word><Word>try</Word><Word>catch</Word><Word>finally</Word>
            </Keywords>
            <Keywords color="Literal">
              <Word>true</Word><Word>false</Word><Word>null</Word><Word>undefined</Word><Word>this</Word><Word>NaN</Word>
            </Keywords>
            <Rule color="Number">\b0[xX][0-9a-fA-F]+|\b\d+(\.[0-9]+)?([eE][+-]?[0-9]+)?</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    static readonly IHighlightingDefinition JsHighlighting = LoadJsHighlighting();

    static IHighlightingDefinition LoadJsHighlighting()
    {
        using var reader = System.Xml.XmlReader.Create(new System.IO.StringReader(XshdJs));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    public ScriptSideBarContentProvider()
    {
        var mono = new FontFamily("Consolas, Menlo, Courier New, monospace");

        // 可编辑代码框：AvaloniaEdit 代码编辑器（JS 语法高亮 + 行号）；底色用最深的 DARK（聚焦编辑区）。
        mCodeBox = new TextEditor
        {
            FontFamily = mono,
            FontSize = 13,
            Background = Style.DARK.ToBrush(),
            Foreground = Style.TEXT_NORMAL.ToBrush(),
            ShowLineNumbers = true,
            LineNumbersForeground = Style.LIGHT_WHITE.Opacity(0.35).ToBrush(),
            WordWrap = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new(8, 6),
            Margin = new(8, 8, 8, 0),
            SyntaxHighlighting = JsHighlighting,
        };
        mCodeBox.Options.IndentationSize = 2;
        mCodeBox.Options.ConvertTabsToSpaces = true;
        mCodeBox.Text = Sample;
        mCodeBox.TextChanged += (_, _) => MarkDirty();   // 用户编辑即标脏（程序性置文后由 SetCurrentScript 复位）

        // 人类文档页：与代码框【同位互斥】（点 Doc/Code 在原地翻面）。Markdown 渲染、自适应宽度换行（禁横向滚动）。
        // 这是【给人读】的版本，与喂 LLM 的速查表（ScriptApiReference / get_script_api）分开。
        var docContent = ChatMarkdownRenderer.Render(LoadDoc());
        docContent.Margin = new(12, 4, 24, 8);   // 右内边距更大：给竖向滚动条让位，正文在其左侧换行，左右视觉对称
        mDocView = new ScrollViewer
        {
            Content = docContent,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new(8, 8, 8, 0),
            IsVisible = false,
        };

        // 只读输出（console）：比可编辑框更平淡又有层级——BACK 底、暗前景、无插入符、不可编辑。
        mOutputBox = new TextInput
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = mono,
            FontSize = 12,
            Background = Style.BACK.ToBrush(),
            Foreground = Style.LIGHT_WHITE.Opacity(0.7).ToBrush(),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top,
            CaretBrush = Brushes.Transparent,
            Padding = new(8, 6),
            Margin = new(8, 0, 8, 8),
            MinHeight = 120,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(mOutputBox, ScrollBarVisibility.Auto);

        // 中央区：代码框 / 文档页叠放，仅其一可见，Doc/Code 按钮原地翻面。
        var center = new Panel();
        center.Children.Add(mCodeBox);
        center.Children.Add(mDocView);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new(8),
            Spacing = 8,
        };
        // Doc/Code 切换（普通按钮）：动态改字靠重设 ButtonContent.Item（触发重绘）。
        var viewToggle = MakeButton("Doc", 60, Style.BUTTON_NORMAL, Style.BUTTON_NORMAL_HOVER, Style.LIGHT_WHITE, out var docLabel);
        viewToggle.Clicked += () =>
        {
            mShowingDoc = !mShowingDoc;
            mCodeBox.IsVisible = !mShowingDoc;
            mDocView.IsVisible = mShowingDoc;
            docLabel.Item = new TextItem { Text = mShowingDoc ? "Code" : "Doc" };
        };
        // Run（主按钮）。
        var runButton = MakeButton("Run", 80, Style.BUTTON_PRIMARY, Style.BUTTON_PRIMARY_HOVER, Colors.White, out _);
        runButton.Clicked += Run;
        buttons.Children.Add(viewToggle);
        buttons.Children.Add(runButton);

        // ── 顶部脚本库管理栏 ───────────────────────────────────────────────────────
        // ⋯ 菜单钮（仿 preset 的收起钮：圆角底 + ⋯）；脚本选择钮填充其左侧剩余宽。
        mMoreButton = new TuneLab.GUI.Components.Button { Width = 28, Height = 28 }
            .AddContent(new() { Item = new BorderItem { CornerRadius = 4 }, ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER, PressedColor = Style.INTERFACE } })
            .AddContent(new() { Item = new TextItem { Text = "⋯", FontSize = 16 }, ColorSet = new() { Color = Colors.White } });
        mMoreButton.Clicked += OnMoreButtonClicked;

        mScriptButton = new TuneLab.GUI.Components.Button { Height = 28, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch }
            .AddContent(new() { Item = new BorderItem { CornerRadius = 4 }, ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER } });
        mScriptLabel = new ButtonContent { Item = new TextItem { Text = "", FontSize = 12 }, ColorSet = new() { Color = Style.LIGHT_WHITE } };
        mScriptButton.AddContent(mScriptLabel);
        mScriptButton.Clicked += OnScriptButtonClicked;

        // 下拉用自定义 Flyout（与 Agent 会话下拉同构）：StackPanel 装行，行右侧贴 ✕ 可删，整行 hover 高亮、点击加载。
        mScriptFlyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        mScriptFlyout.FlyoutPresenterClasses.Add("agent-menu");
        mScriptFlyout.Closed += (_, _) =>
        {
            mFlyoutJustClosed = true;     // light-dismiss 会在再次点钮时先关闭，置标志让随后的 Click 不重开 → toggle
            Dispatcher.UIThread.Post(() => mFlyoutJustClosed = false, DispatcherPriority.Input);
        };

        var header = new DockPanel { Height = 44, LastChildFill = true, Background = Style.INTERFACE.ToBrush() };
        var headerInner = new DockPanel { LastChildFill = true, Margin = new(8, 8, 8, 0) };
        DockPanel.SetDock(mMoreButton, Dock.Right);
        mScriptButton.Margin = new(0, 0, 8, 0);
        headerInner.Children.Add(mMoreButton);
        headerInner.Children.Add(mScriptButton);
        header.Children.Add(headerInner);

        SetCurrentScript(null);   // 初始：未命名工作副本

        // 布局：顶部管理栏固定；底部输出区 + 其上按钮行固定；中央区填充剩余。
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(mOutputBox, Dock.Bottom);
        DockPanel.SetDock(buttons, Dock.Bottom);
        mRoot.Children.Add(header);
        mRoot.Children.Add(mOutputBox);
        mRoot.Children.Add(buttons);
        mRoot.Children.Add(center);

        mRoot.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                Run();
                e.Handled = true;
            }
        };
    }

    // 人类可读文档（Markdown）。与喂 LLM 的速查表（ScriptApiReference / get_script_api）分开维护。
    // 多语言：按当前语言设置加载 Resources/ScriptDoc/{文化码}.md，缺失则回退 en-US.md，再缺失用下面的内嵌英文兜底。
    // 文档文件随程序打包（csproj 把 Resources/** 拷到输出）；翻译者只需在该目录加一份 {语言}.md，无需改代码。
    static string LoadDoc()
    {
        static string? TryRead(string lang)
        {
            try
            {
                var path = System.IO.Path.Combine(PathManager.ScriptDocFolder, lang + ".md");
                return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
            }
            catch { return null; }
        }
        return TryRead(TranslationManager.CurrentLanguage.Value ?? "") ?? TryRead("en-US") ?? FallbackDoc;
    }

    // 内嵌英文兜底（仅当 Resources/ScriptDoc 缺失时用）。
    const string FallbackDoc =
        "# Script\n\n" +
        "Run JavaScript to edit the project; the whole run is one undoable change. " +
        "Object-style: global `tl` is the editor, project data is `tl.currentProject()`; tracks/parts/notes/vibratos are handles with read/write fields (`n.pitch += 12`, `track.isMute = true`) and methods. " +
        "Reads (`tl.currentProject().tracks()`, `track.parts()`, `part.notes()`, `tl.currentPart()`, `part.selectedNotes()`) return arrays of handles (not a linked list). " +
        "Create and delete both hang off the parent (`track.addPart({...})`/`track.removePart(p)`, `part.addNote({...})`/`part.removeNote(n)`). " +
        "Positions are absolute ticks (`tl.ppq`), pitch is MIDI. `print(x)` logs to the output below.";

    // 用 TuneLab 内置 Button 组件造一个「圆角底色 + 居中文字」按钮；返回文字 ButtonContent 供动态改字。
    static TuneLab.GUI.Components.Button MakeButton(string text, double width, Color bg, Color bgHover, Color fg, out ButtonContent label)
    {
        var b = new TuneLab.GUI.Components.Button { Width = width, Height = 30 };
        b.AddContent(new ButtonContent { Item = new BorderItem { CornerRadius = 6 }, ColorSet = new ColorSet { Color = bg, HoveredColor = bgHover } });
        label = new ButtonContent { Item = new TextItem { Text = text, FontSize = 12 }, ColorSet = new ColorSet { Color = fg } };
        b.AddContent(label);
        return b;
    }

    void Run()
    {
        var project = mProject;
        if (project == null) { mOutputBox.Text = "No project is open."; return; }
        string code = mCodeBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(code)) { mOutputBox.Text = "Script is empty."; return; }

        ScriptRunResult result;
        try { result = ScriptRunner.Run(project, mCurrentPart, mQuantization, () => TranslationManager.CurrentLanguage.Value, mSelection, mPianoSelection, ScriptLimits.Interactive, code, CancellationToken.None); }
        catch (Exception ex) { mOutputBox.Text = "Host error: " + ex.Message; return; }

        var sb = new System.Text.StringBuilder();
        if (result.Ok)
            sb.Append(result.Committed
                ? string.Format("✓ OK — applied {0} edit(s) as one undoable change (Ctrl+Z to undo).", result.Changes)
                : "✓ OK — no changes were made.");
        else
        {
            sb.Append("✗ Script error: ").Append(result.Error);
            sb.Append("\n(All changes were rolled back — the project is unchanged.)");
        }
        if (!string.IsNullOrEmpty(result.Output))
            sb.Append("\n\n--- output ---\n").Append(result.Output.TrimEnd('\n'));
        if (result.Ok && !string.IsNullOrEmpty(result.ResultText))
            sb.Append("\n\n--- result ---\n").Append(result.ResultText);
        mOutputBox.Text = sb.ToString();
    }

    // ───────────────── 脚本库管理 ─────────────────

    // 设当前脚本名并清脏标（载入/新建/打开/另存后，编辑器内容与库文件一致）。
    void SetCurrentScript(string? name)
    {
        mDirty = false;
        SetScriptName(name);
    }

    // 只改名 + 刷标题（重命名时用：不动脏标——重命名只改库里的文件名，编辑器缓冲若已脏，相对新名仍是脏的）。
    void SetScriptName(string? name)
    {
        mCurrentScriptName = string.IsNullOrWhiteSpace(name) ? null : name;
        RefreshTitle();
    }

    // 下拉钮文字：脏则前缀 *；当前脚本名（null = 未命名工作副本，显示「Untitled」）；末尾缀 ▾ 提示可下拉。
    void RefreshTitle()
    {
        var shown = mCurrentScriptName ?? "Untitled".Tr(this);
        if (mDirty) shown = "*" + shown;
        mScriptLabel.Item = new TextItem { Text = shown + "  ▾", FontSize = 12 };
    }

    void MarkDirty()
    {
        if (mDirty) return;
        mDirty = true;
        RefreshTitle();
    }

    // 切到代码视图（管理动作改的是代码，若正看文档则翻回去）。
    void EnsureCodeView()
    {
        if (!mShowingDoc) return;
        mShowingDoc = false;
        mCodeBox.IsVisible = true;
        mDocView.IsVisible = false;
    }

    void OnScriptButtonClicked()
    {
        if (mFlyoutJustClosed) return;   // 再次点击恰逢 light-dismiss 刚关 → 不重开（toggle）
        PopulateScriptMenu();
        mScriptFlyout.ShowAt(mScriptButton);
    }

    // 每次打开重建：顶部「New」（清空成未命名工作副本）+ 分隔线 + 库内脚本（点击加载、右侧 ✕ 删除）。
    void PopulateScriptMenu()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical, MinWidth = 220 };
        stack.Children.Add(FlyoutMenuRow.Build("New".Tr(this), null, NewScript, null, mScriptFlyout));

        var names = ScriptLibrary.List();
        if (names.Count > 0)
            stack.Children.Add(new Border { Height = 1, Margin = new(8, 4), Background = Style.LIGHT_WHITE.Opacity(0.15).ToBrush() });
        foreach (var name in names)
        {
            var captured = name;
            stack.Children.Add(FlyoutMenuRow.Build(captured, captured,
                () => LoadScript(captured),
                () => { mScriptFlyout.Hide(); _ = DeleteScript(captured); }, mScriptFlyout));
        }

        mScriptFlyout.Content = stack;
    }

    // New：清空成未命名工作副本（保留起手注释示例，便于直接写）。
    void NewScript()
    {
        mCodeBox.Text = Sample;
        SetCurrentScript(null);
        EnsureCodeView();
    }

    // 从库加载：读文件入编辑器、记其为当前脚本。
    void LoadScript(string name)
    {
        try
        {
            mCodeBox.Text = ScriptLibrary.Read(name);
            SetCurrentScript(name);
            EnsureCodeView();
        }
        catch (Exception ex)
        {
            _ = mRoot.ShowMessage("Error".Tr(this), string.Format("Failed to load script \"{0}\": \n{1}", name, ex.Message));
        }
    }

    // 删除库内脚本（先二次确认）；若删的是当前脚本，编辑器内容留下但降为未命名工作副本。
    async Task DeleteScript(string name)
    {
        if (!await ConfirmAsync(string.Format("Delete script \"{0}\"?".Tr(this), name)))
            return;
        try
        {
            ScriptLibrary.Delete(name);
            if (string.Equals(name, mCurrentScriptName, StringComparison.OrdinalIgnoreCase))
                SetCurrentScript(null);
        }
        catch (Exception ex)
        {
            await mRoot.ShowMessage("Error".Tr(this), string.Format("Failed to delete script \"{0}\": \n{1}", name, ex.Message));
        }
    }

    // ⋯ 菜单：打开（任意位置载入工作副本，不入库）/ 导入（复制进库并加载）/ 保存（覆盖当前脚本）/ 另存 / 重命名。
    void OnMoreButtonClicked()
    {
        var menu = new ContextMenu();
        var hasCurrent = mCurrentScriptName != null;

        menu.Items.Add(new MenuItem().SetName("Open".Tr(this)).SetAction(async () => await OpenFromDisk()));
        menu.Items.Add(new MenuItem().SetName("Import".Tr(this)).SetAction(async () => await ImportFromDisk()));
        menu.Items.Add(new MenuItem().SetName("Save".Tr(this)).SetAction(async () => await SaveScript()));
        menu.Items.Add(new MenuItem().SetName("Save As".Tr(this)).SetAction(async () => await SaveAsScript()));
        {
            var rename = new MenuItem().SetName("Rename".Tr(this)).SetAction(async () => await RenameScript());
            rename.IsEnabled = hasCurrent;   // 只有库内脚本可重命名
            menu.Items.Add(rename);
        }

        mMoreButton.OpenContextMenu(menu);
    }

    // 打开：从任意位置选 .js → 载入编辑器作工作副本（不入库，当前脚本置空）。
    async Task OpenFromDisk()
    {
        var path = await PickJsFileAsync("Open Script".Tr(this));
        if (path == null) return;
        try
        {
            mCodeBox.Text = System.IO.File.ReadAllText(path);
            SetCurrentScript(null);
            EnsureCodeView();
        }
        catch (Exception ex)
        {
            await mRoot.ShowMessage("Error".Tr(this), "Failed to open file: \n" + ex.Message);
        }
    }

    // 导入：从任意位置选若干 .js → 各复制进库。只入库，不打开/不改变当前编辑内容。
    // 同名冲突按 Windows 复制冲突风格逐项询问（覆盖/跳过，无副本选项），并可"对剩余所有项执行此操作"。
    async Task ImportFromDisk()
    {
        var paths = await PickJsFilesAsync("Import Script".Tr(this), allowMultiple: true);
        if (paths.Count == 0) return;

        bool? overwriteAll = null;   // null = 逐项询问；true = 全覆盖；false = 全跳过
        var failed = new List<string>();
        for (int i = 0; i < paths.Count; i++)
        {
            var path = paths[i];
            if (ScriptLibrary.Exists(ScriptLibrary.NameForImport(path)))
            {
                if (overwriteAll == null)
                {
                    var (overwrite, applyToAll) = await AskImportConflict(ScriptLibrary.NameForImport(path), showApplyToAll: paths.Count - i > 1);
                    if (applyToAll) overwriteAll = overwrite;
                    if (!overwrite) continue;   // 跳过本项
                }
                else if (!overwriteAll.Value) continue;   // 全跳过
                // 全覆盖 / 本项选覆盖 → 落到下面 Import
            }
            try { ScriptLibrary.Import(path, overwrite: true); }
            catch (Exception ex) { failed.Add(System.IO.Path.GetFileName(path) + ": " + ex.Message); }
        }
        if (failed.Count > 0)
            await mRoot.ShowMessage("Error".Tr(this), "Failed to import:\n" + string.Join("\n", failed));
    }

    // 同名冲突弹窗：覆盖(主) / 跳过；showApplyToAll 时附"对剩余所有项执行此操作"复选框。返回(是否覆盖, 是否应用到剩余全部)。
    async Task<(bool overwrite, bool applyToAll)> AskImportConflict(string name, bool showApplyToAll)
    {
        var dialog = new TuneLab.GUI.Dialog();
        dialog.SetTitle("Import".Tr(this));
        dialog.SetMessage(string.Format("Script \"{0}\" already exists.".Tr(this), name));

        var applyToAllBox = new TuneLab.GUI.Components.CheckBox();
        applyToAllBox.Display(false);
        if (showApplyToAll && dialog.FindControl<StackPanel>("MessageStackPanel") is { } messageStack)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new(0, 12, 0, 0) };
            row.Children.Add(applyToAllBox);
            row.Children.Add(new TextBlock { Text = "Do this for all remaining items".Tr(this), Foreground = Colors.White.ToBrush(), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            messageStack.Children.Add(row);
        }

        bool overwrite = false;
        var overwriteButton = dialog.AddButton("Overwrite".Tr(this), TuneLab.GUI.Dialog.ButtonType.Primary);
        overwriteButton.Pressed += () => overwrite = true;
        dialog.AddButton("Skip".Tr(this), TuneLab.GUI.Dialog.ButtonType.Normal);   // 默认 overwrite=false 即跳过
        dialog.Topmost = true;
        await dialog.ShowDialog(mRoot.Window());
        return (overwrite, showApplyToAll && applyToAllBox.Value);
    }

    // 保存：覆盖当前脚本；无当前脚本（未命名工作副本）则转为另存。
    async Task SaveScript()
    {
        if (mCurrentScriptName == null)
        {
            await SaveAsScript();
            return;
        }
        try
        {
            ScriptLibrary.Save(mCurrentScriptName, mCodeBox.Text ?? "");
            mDirty = false;
            RefreshTitle();
        }
        catch (Exception ex) { await mRoot.ShowMessage("Error".Tr(this), "Failed to save script: \n" + ex.Message); }
    }

    // 另存：取新名 → 写入库（重名先确认覆盖）→ 选中为当前脚本。
    async Task SaveAsScript()
    {
        var name = await RequestNameAsync(mCurrentScriptName ?? "");
        if (name == null) return;
        if (ScriptLibrary.Exists(name) && !await ConfirmAsync(string.Format("Overwrite script \"{0}\"?".Tr(this), name)))
            return;
        try
        {
            ScriptLibrary.Save(name, mCodeBox.Text ?? "");
            SetCurrentScript(name);
        }
        catch (Exception ex) { await mRoot.ShowMessage("Error".Tr(this), "Failed to save script: \n" + ex.Message); }
    }

    // 重命名当前脚本（仅库内脚本可用）。新名重名先确认覆盖。
    async Task RenameScript()
    {
        var old = mCurrentScriptName;
        if (old == null) return;
        var name = await RequestNameAsync(old);
        if (name == null || name.Equals(old, StringComparison.OrdinalIgnoreCase)) return;
        if (ScriptLibrary.Exists(name) && !await ConfirmAsync(string.Format("Overwrite script \"{0}\"?".Tr(this), name)))
            return;
        try
        {
            ScriptLibrary.Rename(old, name);
            SetScriptName(name);   // 不清脏标：重命名只改库文件名，未保存的缓冲改动仍未落盘
        }
        catch (Exception ex) { await mRoot.ShowMessage("Error".Tr(this), "Failed to rename script: \n" + ex.Message); }
    }

    // 取名弹窗：返回 sanitize 后的非空名，取消/空则 null。
    async Task<string?> RequestNameAsync(string initialName)
    {
        var dialog = new NameInputDialog("Script Name".Tr(this), initialName);
        var name = await dialog.ShowDialog<string?>(mRoot.Window());
        name = ScriptLibrary.SanitizeName(name?.Trim() ?? "");
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    async Task<bool> ConfirmAsync(string message)
    {
        var dialog = new TuneLab.GUI.Dialog();
        dialog.SetTitle("Tips".Tr(this));
        dialog.SetMessage(message);
        bool confirmed = false;
        dialog.AddButton("Cancel".Tr(this), TuneLab.GUI.Dialog.ButtonType.Normal);
        var ok = dialog.AddButton("OK".Tr(this), TuneLab.GUI.Dialog.ButtonType.Primary);
        ok.Pressed += () => confirmed = true;
        dialog.Topmost = true;
        await dialog.ShowDialog(mRoot.Window());
        return confirmed;
    }

    // 系统文件选择器：取本地路径。取消/无窗 → 空。单选用 allowMultiple=false（取首个）。
    async Task<string?> PickJsFileAsync(string title)
        => (await PickJsFilesAsync(title, allowMultiple: false)).FirstOrDefault();

    async Task<IReadOnlyList<string>> PickJsFilesAsync(string title, bool allowMultiple)
    {
        var top = TopLevel.GetTopLevel(mRoot);
        if (top == null) return [];
        IReadOnlyList<IStorageFile> files;
        try
        {
            files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = allowMultiple,
                FileTypeFilter = new[] { new FilePickerFileType("JavaScript") { Patterns = new[] { "*.js" } } },
            });
        }
        catch (Exception ex)
        {
            Log.Warning("Script file picker failed: " + ex.Message);
            return [];
        }
        return files.Select(f => f.TryGetLocalPath()).Where(p => p != null).Select(p => p!).ToList();
    }
}
