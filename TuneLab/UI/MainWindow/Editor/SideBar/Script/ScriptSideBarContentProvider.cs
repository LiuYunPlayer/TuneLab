using System;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using TuneLab.Data;
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

    readonly DockPanel mRoot = new() { LastChildFill = true, Background = Style.INTERFACE.ToBrush() };
    readonly TextEditor mCodeBox;
    readonly Control mDocView;        // 人类可读文档（Markdown 渲染、自适应宽度换行），与代码框同位互斥
    readonly TextInput mOutputBox;
    bool mShowingDoc;
    IProject? mProject;
    Func<IMidiPart?>? mCurrentPart;
    Func<IQuantization?>? mQuantization;

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

        // 布局：底部输出区 + 其上按钮行固定，中央区填充剩余。
        DockPanel.SetDock(mOutputBox, Dock.Bottom);
        DockPanel.SetDock(buttons, Dock.Bottom);
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
        try { result = ScriptRunner.Run(project, mCurrentPart, mQuantization, code, CancellationToken.None); }
        catch (Exception ex) { mOutputBox.Text = "Host error: " + ex.Message; return; }

        var sb = new System.Text.StringBuilder();
        if (result.Ok)
            sb.Append(result.Committed
                ? string.Format("✓ OK — applied {0} edit(s) as one undoable change (Ctrl+Z to undo).", result.Changes)
                : "✓ OK — no changes were made.");
        else
        {
            sb.Append("✗ Script error: ").Append(result.Error);
            if (result.Committed)
                sb.Append(string.Format("\n(Edits made before the error — {0} — were still applied as one undoable change.)", result.Changes));
        }
        if (!string.IsNullOrEmpty(result.Output))
            sb.Append("\n\n--- output ---\n").Append(result.Output.TrimEnd('\n'));
        if (result.Ok && !string.IsNullOrEmpty(result.ResultText))
            sb.Append("\n\n--- result ---\n").Append(result.ResultText);
        mOutputBox.Text = sb.ToString();
    }
}
