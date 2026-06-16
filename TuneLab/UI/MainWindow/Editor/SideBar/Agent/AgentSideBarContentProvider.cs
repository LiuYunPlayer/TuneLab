using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Agent;
using TuneLab.Data;
using TuneLab.Extensions.Agent;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Controllers;
using TuneLab.I18N;
using TuneLab.SDK;
using TuneLab.Utils;
using ScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility;
using PlacementMode = Avalonia.Controls.PlacementMode;

namespace TuneLab.UI;

// Agent 侧边栏（全高自管布局）：SideBar 大标题下方是固定小标题栏（☰ 会话 / 标题 / ⚙ 设置），中间是丝滑滚动的
// 气泡对话区（用户靠右、agent 靠左），底部是圆角输入框 + 图标发送键。设置为同区切换的子页。
// 对话区用 TuneLab.GUI.Components.ListView（带 AnimationScalableScrollAxis 动画滚动）；气泡靠 MaxWidth 在无限宽测量下仍换行。
internal sealed class AgentSideBarContentProvider
{
    public IImage Icon => Assets.Agent.GetImage(Style.LIGHT_WHITE);
    public string Name => "Agent".Tr(this);
    public Control Root => mRoot;

    public AgentSideBarContentProvider()
    {
        // 设置数据须挂在文档根上（属性面板字段绑定会读 DataObject.Head），用独立 DataDocument，与工程 undo 隔离。
        mSettings = new DataPropertyObject(mSettingsDocument);

        BuildChatView();
        BuildSettingsView();
        ShowChat();

        // 载入上次保存的设置（含解密后的密钥），并选中上次的引擎。
        var (savedEngine, savedValues) = AgentSettingsStore.Load();
        foreach (var kv in savedValues)
            mSettings.SetValue(kv.Key, kv.Value);
        mSettings.Commit();

        var engines = AgentModelManager.GetAllAgentModelEngines().ToList();
        mEngineComboBox.SetConfig(new ComboBoxConfig { Options = engines.Select(e => (ComboBoxOption)e).ToList() });
        string? initial = savedEngine != null && engines.Contains(savedEngine) ? savedEngine : (engines.Count > 0 ? engines[0] : null);
        if (initial != null)
        {
            mEngineComboBox.Display(PropertyValue.Create(initial));
            RefreshEnginePropertyPanel(initial);
        }
        mEngineComboBox.ValueCommitted.Subscribe(OnEngineSelectionChanged);

        // 有持久化设置时（说明之前 Submit 过）打开即静默自动接入，直接可聊天；失败则首次发送再引导去设置。
        if (savedEngine != null && engines.Contains(savedEngine))
            TryConnect(savedEngine, out _);
    }

    // 工程切换时由 Editor 调用：重建 Facade 与工具（runner 下次发送时按新工具重建，历史重置）。
    public void SetProject(IProject? project)
    {
        mProjectEditor = project != null ? new ProjectAgentEditor(project) : null;
        mTools = mProjectEditor != null
            ? new List<IAgentTool> { new ListTracksTool(mProjectEditor), new ShiftPitchTool(mProjectEditor) }
            : [];
        mRunner = null;
    }

    // ───────────────── 聊天视图 ─────────────────

    void BuildChatView()
    {
        // 固定小标题栏：☰（会话）/ 标题（省略号+tooltip）/ ⚙（设置）。按钮无底色、仅 icon hover 变色。
        var header = new DockPanel() { Height = 32, LastChildFill = true, Background = Style.INTERFACE.ToBrush() };

        // ☰ 用 Toggle 做图标变色（收起=灰、展开=亮白），永不显底色。AllowSwitch=false 关掉 Toggle 自身的点击翻转，
        // 让颜色完全由 flyout 开合事件经 Display() 驱动 → 连 light-dismiss 关闭也正确变灰，不与点击逻辑失步。
        var menuToggle = new Toggle() { Width = 32, Height = 32 }
            .AddContent(new()
            {
                Item = new IconItem() { Icon = Assets.Menu },
                // 无底色，反馈全落图标：展开恒亮白；收起恒灰（hover/press 不变色，回退到 Color）。
                CheckedColorSet = new() { Color = Colors.White },
                UncheckedColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5) },
            });
        menuToggle.AllowSwitch += () => false;
        menuToggle.Clicked += OnMenuButtonClicked;
        mMenuButton = menuToggle;
        DockPanel.SetDock(menuToggle, Dock.Left);
        header.Children.Add(menuToggle);

        // 会话菜单：锚定按钮正下方、左对齐；再次点击关闭（toggle）。会话列表尚未实装，先给「新对话」+ 占位项。
        mMenuFlyout = new MenuFlyout() { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var newChatItem = new MenuItem() { Header = "New Chat".Tr(this) };
        newChatItem.Click += (_, _) => NewChat();
        var soonItem = new MenuItem() { Header = "Saved sessions (coming soon)".Tr(this), IsEnabled = false };
        mMenuFlyout.Items.Add(newChatItem);
        mMenuFlyout.Items.Add(soonItem);
        // 开合状态 → ☰ 图标颜色：展开变亮、收起变灰。
        mMenuFlyout.Opened += (_, _) => menuToggle.Display(true);
        // light-dismiss 会在再次按按钮时先关闭，置标志让随后的 Click 不重开，从而实现 toggle。
        mMenuFlyout.Closed += (_, _) =>
        {
            menuToggle.Display(false);
            mMenuJustClosed = true;
            Dispatcher.UIThread.Post(() => mMenuJustClosed = false, DispatcherPriority.Input);
        };

        var settingsButton = IconButton(Assets.Settings, Style.LIGHT_WHITE.Opacity(0.6), Colors.White);
        settingsButton.Clicked += ShowSettings;
        DockPanel.SetDock(settingsButton, Dock.Right);
        header.Children.Add(settingsButton);

        mTitleLabel.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        mTitleLabel.Margin = new(8, 0);
        mTitleLabel.FontSize = 12;
        mTitleLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        mTitleLabel.Foreground = Style.LIGHT_WHITE.ToBrush();
        header.Children.Add(mTitleLabel);

        // 底部圆角输入区（描边 + 背景，和上方滚动区分隔）。
        var inputRow = new DockPanel() { LastChildFill = true };
        mSendButton = IconButton(Assets.Send, Style.LIGHT_WHITE.Opacity(0.85), Colors.White);
        mSendButton.Clicked += () => _ = OnSend();
        DockPanel.SetDock(mSendButton, Dock.Right);
        inputRow.Children.Add(mSendButton);
        // 多行自增长：随内容长高、自动换行，到上限内部滚动；Enter 发送，Shift+Enter 换行。
        mInput.AcceptsReturn = true;
        mInput.TextWrapping = TextWrapping.Wrap;
        mInput.MinHeight = 28;
        mInput.MaxHeight = 120;
        // 竖直内边距：TextInput 构造默认 Padding=(8,0)（竖距为 0），补一点上下内边距给文字留白。
        mInput.Padding = new(8, 6);
        // 竖直居中：发送键(32px)会把输入行撑到 32 高，Top 对齐会让单行文字偏上、与发送图标不齐；Center 让单行竖直居中、
        // 与发送图标对齐；多行时框随内容长高、内容填满，Center 与 Top 视觉一致。
        mInput.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
        // 多行换行的关键：TextInput 默认 HorizontalContentAlignment=Left（为单行属性编辑而设），会让模板内承载文字的
        // presenter 按内容宽度摆放而非填满框宽，使 Wrap 形成自反馈、末字恒折到第二行。多行场景必须改回 Stretch。
        mInput.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        // 临时隐藏原生滚动条：Fluent 原生竖条 hover 会膨胀成带箭头的粗条遮挡文字，很丑；内容仍可滚轮/光标跟随滚动。
        // 最终态拟用自带 scrollaxis 封装专门的 overlay 细滚动条控件再替换。隐藏也彻底排除滚动条对布局的影响。
        ScrollViewer.SetVerticalScrollBarVisibility(mInput, ScrollBarVisibility.Hidden);
        // 关闭布局取整：分数缩放下避免可用宽度被像素 floor 掉一截而在边界处误折一字（轻微保险）。
        // 注：对“从换行行首选择导致上一行末字跳行”无效——那是 Avalonia 在 Wrap 下选择重排的框架层行为，原生 TextBox 难根治，留待将来自定义文本/滚动控件处理。
        mInput.UseLayoutRounding = false;
        mInput.Background = Brushes.Transparent;
        mInput.Watermark = "Type a message...".Tr(this);
        // Enter 发送 / Shift+Enter 换行。用 handledEventsToo：AcceptsReturn 下 TextBox 类处理器会先处理 Enter（插入换行并标
        // Handled），普通 += 处理器随之被跳过；handledEventsToo 让发送处理器仍被调用（若先于类处理器则 Handled 拦掉换行，
        // 否则换行已插入但 OnSend 读取时 Trim 掉、并随即清空）。
        mInput.AddHandler(InputElement.KeyDownEvent, (EventHandler<KeyEventArgs>)((_, e) =>
        {
            if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Shift) == 0)
            {
                e.Handled = true;
                _ = OnSend();
            }
        }), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        inputRow.Children.Add(mInput);

        var inputBorder = new Border()
        {
            CornerRadius = new(8),
            BorderThickness = new(1),
            BorderBrush = Style.LIGHT_WHITE.Opacity(0.2).ToBrush(),
            Background = Style.BACK.ToBrush(),
            Margin = new(8),
            Padding = new(6, 2),
            Child = inputRow,
        };

        // 中间丝滑滚动对话区（自带动画轴）。透明背景让整块区域（含消息下方空白）都可命中滚轮。
        mMessagesList.Orientation = Orientation.Vertical;
        mMessagesList.Background = Brushes.Transparent;
        // 气泡 MaxWidth 随对话区宽度自适应：留出对侧 ~40px 空白（避免占满整宽损可读性）；侧栏拖宽即时更新所有现有气泡。
        mMessagesList.PropertyChanged += (_, e) =>
        {
            if (e.Property != Avalonia.Visual.BoundsProperty)
                return;
            mBubbleMaxWidth = Math.Max(140, mMessagesList.Bounds.Width - 40);
            foreach (var c in mMessagesList.Content.Children)
                c.MaxWidth = mBubbleMaxWidth;
        };

        DockPanel.SetDock(header, Dock.Top);
        mChatView.Children.Add(header);
        var sep = new Border() { Height = 1, Background = Style.BACK.ToBrush() };
        DockPanel.SetDock(sep, Dock.Top);
        mChatView.Children.Add(sep);
        DockPanel.SetDock(inputBorder, Dock.Bottom);
        mChatView.Children.Add(inputBorder);
        mChatView.Children.Add(mMessagesList); // 最后一个 → 填充中间
    }

    void OnMenuButtonClicked()
    {
        if (mMenuJustClosed)
            return; // 再次点击：刚被 light-dismiss 关闭，不重开 → toggle 关闭
        if (mMenuButton != null)
            mMenuFlyout.ShowAt(mMenuButton);
    }

    void NewChat()
    {
        mMessagesList.Content.Children.Clear();
        mRunner = null; // 重置对话历史，保留已连接的 session
        SetTitle("New Chat".Tr(this));
    }

    public void SetTitle(string title)
    {
        mTitleLabel.Text = title;
        ToolTip.SetTip(mTitleLabel, title);
    }

    async Task OnSend()
    {
        if (mBusy)
            return;

        var text = mInput.Text.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        if (mSession == null)
        {
            AppendMessage("system", "Not connected. Open settings (gear) to choose a model and submit.".Tr(this));
            ShowSettings();
            return;
        }
        if (mProjectEditor == null)
        {
            AppendMessage("system", "No project is open.".Tr(this));
            return;
        }

        mInput.Text = string.Empty;
        AppendMessage("you", text);
        var bubble = AddAssistantBubble(); // 响应期占位气泡（「…」）
        SetBusy(true);

        try
        {
            mRunner ??= new AgentRunner(mSession, mTools, SystemPrompt);
            var reply = await mRunner.SendAsync(text, CancellationToken.None);
            bubble.Child = string.IsNullOrEmpty(reply.Text)
                ? BubbleText("(no text reply)", Colors.White.ToBrush())
                : AssistantContent(reply.Text, reply.Usage);
        }
        catch (Exception ex)
        {
            bubble.Child = BubbleText("Error: " + ex.Message, Colors.IndianRed.ToBrush());
        }
        finally
        {
            SetBusy(false);
            ScrollToEnd();
        }
    }

    void AppendMessage(string role, string text)
    {
        Control item = role == "you"
            ? Bubble(BubbleText(text, Colors.White.ToBrush()), mine: true)
            : new SelectableTextBlock()
            {
                Text = text,
                MaxWidth = mBubbleMaxWidth,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (role == "error" ? Colors.IndianRed : Style.LIGHT_WHITE.Opacity(0.6)).ToBrush(),
                FontSize = 11,
                Margin = new(12, 4),
                TextAlignment = TextAlignment.Center,
            };
        mMessagesList.Content.Children.Add(item);
        ScrollToEnd();
    }

    // agent 侧占位气泡，返回 Border 以便回复回来后替换其内容（占位「…」→ Markdown 渲染 / 错误文本）。
    Border AddAssistantBubble()
    {
        var bubble = Bubble(BubbleText("…", Colors.White.ToBrush()), mine: false);
        mMessagesList.Content.Children.Add(bubble);
        ScrollToEnd();
        return bubble;
    }

    // 气泡容器：用户靠右、agent 靠左；内容可为可选中文本或 Markdown 控件。MaxWidth 随对话区宽度自适应。
    Border Bubble(Control content, bool mine) => new()
    {
        MaxWidth = mBubbleMaxWidth,
        CornerRadius = new(8),
        Padding = new(10, 6),
        Margin = new(8, 4),
        Background = (mine ? Style.BUTTON_PRIMARY : Style.INTERFACE).ToBrush(),
        HorizontalAlignment = mine ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left,
        Child = content,
    };

    // 纯文本气泡内容（用户消息、占位「…」、错误文本用它）。
    static SelectableTextBlock BubbleText(string text, IBrush foreground)
        => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = foreground, FontSize = 12 };

    // 助手消息：Markdig 解析 + 自渲染（ChatMarkdownRenderer，零依赖、文本可选中）+ 脚注（复制原文 + 本轮 token 用量）。
    Control AssistantContent(string markdown, AgentTokenUsage? usage)
    {
        var md = ChatMarkdownRenderer.Render(markdown);
        var copy = new TextBlock
        {
            Text = "Copy".Tr(this),
            FontSize = 11,
            Foreground = Style.LIGHT_WHITE.Opacity(0.45).ToBrush(),
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        copy.PointerEntered += (_, _) => copy.Foreground = Colors.White.ToBrush();
        copy.PointerExited += (_, _) => copy.Foreground = Style.LIGHT_WHITE.Opacity(0.45).ToBrush();
        copy.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            _ = TopLevel.GetTopLevel(copy)?.Clipboard?.SetTextAsync(markdown);
        };

        // 脚注一行：token 总量靠左（带单位，hover 看输入/输出明细）、Copy 靠右（端点未返回 usage 则只有 Copy）。
        var footer = new DockPanel { LastChildFill = false, Margin = new(0, 4, 0, 0) };
        DockPanel.SetDock(copy, Dock.Right);
        footer.Children.Add(copy);
        if (usage != null)
        {
            var tokens = new TextBlock
            {
                Text = string.Format("{0:N0} tokens", usage.TotalTokens),
                FontSize = 11,
                Foreground = Style.LIGHT_WHITE.Opacity(0.4).ToBrush(),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            ToolTip.SetTip(tokens, string.Format("Input {0:N0} · Output {1:N0}".Tr(this), usage.PromptTokens, usage.CompletionTokens));
            DockPanel.SetDock(tokens, Dock.Left);
            footer.Children.Add(tokens);
        }
        return new StackPanel { Orientation = Orientation.Vertical, Children = { md, footer } };
    }

    // 自动滚到底：大值经轴内 clamp 到底部（动画轴；轮滚自带顺滑动画）。
    void ScrollToEnd() => Dispatcher.UIThread.Post(() => mMessagesList.VerticalAxis.ViewOffset = 1e9, DispatcherPriority.Background);

    // 响应期间：输入框保持可用，仅禁用发送按钮（并由 mBusy 拦回车重复发送）。
    void SetBusy(bool busy)
    {
        mBusy = busy;
        mSendButton.IsEnabled = !busy;
    }

    // ───────────────── 设置视图 ─────────────────

    void BuildSettingsView()
    {
        var header = new DockPanel() { Height = 32, LastChildFill = true, Background = Style.INTERFACE.ToBrush() };
        // 返回键放右上角，与对话页 ⚙ 同位置；无底色、icon hover 变色。
        var back = IconButton(Assets.WindowClose, Style.LIGHT_WHITE.Opacity(0.6), Colors.White);
        back.Clicked += ShowChat;
        DockPanel.SetDock(back, Dock.Right);
        header.Children.Add(back);
        header.Children.Add(new Label() { Content = "Model Settings".Tr(this), FontSize = 12, Margin = new(8, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush() });

        var content = new StackPanel() { Orientation = Orientation.Vertical };
        content.Children.Add(new Label() { Content = "Model Plugin".Tr(this), FontSize = 12, Margin = new(24, 12, 24, 0), Foreground = Style.LIGHT_WHITE.ToBrush() });
        mEngineComboBox.Margin = new(24, 4, 24, 0);
        mEngineComboBox.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        content.Children.Add(mEngineComboBox);
        content.Children.Add(mPropertiesController);
        mSubmitButton = SmallTextButton("Submit".Tr(this), 0, 32, Style.BUTTON_PRIMARY, Style.BUTTON_PRIMARY_HOVER);
        mSubmitButton.Margin = new(24, 16, 24, 8);
        mSubmitButton.Clicked += OnSubmit;
        content.Children.Add(mSubmitButton);
        content.Children.Add(mStatusLabel);

        // 设置区用 Avalonia ScrollViewer（横向禁滚 → 约束宽度，长 API Key 在框内滚动，不撑侧栏）。
        var scroll = new ScrollViewer()
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = content,
        };

        DockPanel.SetDock(header, Dock.Top);
        mSettingsView.Children.Add(header);
        var sep = new Border() { Height = 1, Background = Style.BACK.ToBrush() };
        DockPanel.SetDock(sep, Dock.Top);
        mSettingsView.Children.Add(sep);
        mSettingsView.Children.Add(scroll);
    }

    void OnEngineSelectionChanged()
    {
        var type = mEngineComboBox.Value.ToString();
        if (!string.IsNullOrEmpty(type))
            RefreshEnginePropertyPanel(type);
    }

    void RefreshEnginePropertyPanel(string type)
    {
        var engine = AgentModelManager.GetInitedEngine(type);
        if (engine == null)
        {
            mPropertiesController.ResetConfig();
            mStatusLabel.Content = string.Format("Engine '{0}' is unavailable.".Tr(this), type);
            return;
        }
        mPropertiesController.SetConfig(engine.GetPropertyConfig(new PropertyContext(mSettings.GetInfo())), mSettings);
        mStatusLabel.Content = string.Empty;
    }

    void OnSubmit()
    {
        var type = mEngineComboBox.Value.ToString();
        if (string.IsNullOrEmpty(type))
            return;

        if (TryConnect(type, out var error))
        {
            SaveSettings(type);
            ShowChat();
            AppendMessage("system", string.Format("Connected via '{0}'.".Tr(this), type));
        }
        else
        {
            mStatusLabel.Content = error;
        }
    }

    // 用当前设置建立会话（不做界面跳转/提示）。供 Submit 与启动自动接入复用。
    bool TryConnect(string type, out string error)
    {
        error = string.Empty;
        var engine = AgentModelManager.GetInitedEngine(type);
        if (engine == null)
        {
            error = string.Format("Engine '{0}' is unavailable.".Tr(this), type);
            return false;
        }

        try
        {
            mSession?.Dispose();
            mSession = engine.CreateSession(mSettings.GetInfo());
            mRunner = null;
            return true;
        }
        catch (Exception ex)
        {
            error = "Submit failed: " + ex.Message;
            return false;
        }
    }

    // 持久化当前引擎与其填好的值；按 IsPassword 标出敏感字段交由存储层加密。
    void SaveSettings(string type)
    {
        var engine = AgentModelManager.GetInitedEngine(type);
        if (engine == null)
            return;

        var config = engine.GetPropertyConfig(new PropertyContext(mSettings.GetInfo()));
        var secrets = new HashSet<string>();
        foreach (var kv in config.Properties)
            if (kv.Value is TextBoxConfig tb && tb.IsPassword)
                secrets.Add(kv.Key);

        AgentSettingsStore.Save(type, mSettings.GetInfo(), secrets);
    }

    sealed class PropertyContext(PropertyObject properties) : IAgentModelPropertyContext
    {
        public PropertyObject Properties => properties;
    }

    void ShowChat()
    {
        mRoot.Children.Clear();
        mRoot.Children.Add(mChatView);
    }

    void ShowSettings()
    {
        mRoot.Children.Clear();
        mRoot.Children.Add(mSettingsView);
    }

    // ── 按钮工厂 ──

    // 无底色按钮：仅 icon/字形，hover 变色。
    TuneLab.GUI.Components.Button IconButton(SvgIcon icon, Color color, Color hover)
        => new TuneLab.GUI.Components.Button() { Width = 32, Height = 32 }
            .AddContent(new() { Item = new IconItem() { Icon = icon }, ColorSet = new() { Color = color, HoveredColor = hover } });

    TuneLab.GUI.Components.Button GlyphButton(string glyph, Color color, Color hover)
        => new TuneLab.GUI.Components.Button() { Width = 32, Height = 32 }
            .AddContent(new() { Item = new TextItem() { Text = glyph, FontSize = 14 }, ColorSet = new() { Color = color, HoveredColor = hover } });

    // 有底色的主操作按钮（Submit）。width<=0 时不固定宽（随容器拉伸）。
    TuneLab.GUI.Components.Button SmallTextButton(string text, double width, double height, Color color, Color hover)
    {
        var button = new TuneLab.GUI.Components.Button() { Height = height }
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { Color = color, HoveredColor = hover, PressedColor = Style.INTERFACE } })
            .AddContent(new() { Item = new TextItem() { Text = text, FontSize = 13 }, ColorSet = new() { Color = Colors.White } });
        if (width > 0)
            button.Width = width;
        return button;
    }

    const string SystemPrompt =
        "You are an assistant embedded in TuneLab, a singing voice synthesis editor. " +
        "Help the user inspect and edit the current project by calling the provided tools. " +
        "Prefer calling a tool to answer questions about the project rather than guessing. " +
        "Track indices are zero-based.";

    readonly Panel mRoot = new();
    readonly DockPanel mChatView = new() { LastChildFill = true };
    readonly DockPanel mSettingsView = new() { LastChildFill = true };
    readonly ListView mMessagesList = new();
    readonly TextInput mInput = new();
    readonly TextBlock mTitleLabel = new() { Text = "New Chat" };
    readonly ComboBoxController mEngineComboBox = new();
    readonly PropertyObjectController mPropertiesController = new();
    readonly Label mStatusLabel = new() { FontSize = 11, Margin = new(24, 0, 24, 12), Foreground = Colors.IndianRed.ToBrush() };
    TuneLab.GUI.Components.Button mSendButton = null!;
    TuneLab.GUI.Components.Button mSubmitButton = null!;
    TuneLab.GUI.Components.Button? mMenuButton;
    MenuFlyout mMenuFlyout = null!;
    bool mMenuJustClosed;
    bool mBusy;
    double mBubbleMaxWidth = 230; // 气泡最大宽度，随对话区宽度自适应（见 BuildChatView 的 Bounds 订阅）

    readonly DataDocument mSettingsDocument = new();
    readonly DataPropertyObject mSettings;
    IAgentProjectEditor? mProjectEditor;
    IReadOnlyList<IAgentTool> mTools = [];
    IAgentModelSession? mSession;
    AgentRunner? mRunner;
}
