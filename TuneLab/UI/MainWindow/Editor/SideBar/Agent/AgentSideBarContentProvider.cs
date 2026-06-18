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
using TuneLab.Configs;
using TuneLab.Data;
using TuneLab.Extensions;
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
        mProviderData = new DataPropertyObject(mProviderDocument);

        BuildChatView();
        BuildSettingsView();
        ShowChat();
        SwitchTo(NewContext()); // 立即建立首个空白会话作为当前可见会话

        var engines = AgentModelManager.GetAllAgentModelEngines().ToList();
        // 选项值存不可变引擎 id（用于保存/连接/比较），显示文本用本地化显示名。
        mEngineOptions = engines.Select(e => new ComboBoxOption(PropertyValue.Create(e), AgentModelManager.GetDisplayName(e))).ToList();
        // 上次选中的 provider 存 app Settings；各 provider 的配置值各存 ExtensionSettings.json 的 "agent-model:<id>" 桶。
        var savedEngine = Settings.AgentModelProvider.Value;
        bool hadSaved = !string.IsNullOrEmpty(savedEngine) && engines.Contains(savedEngine);
        string? initial = hadSaved ? savedEngine : (engines.Count > 0 ? engines[0] : null);
        if (initial != null)
        {
            mProviderData.SetValue(EngineKey, PropertyValue.Create(initial));
            mProviderData.Commit();
            LoadProviderSettings(initial); // 载入该 provider 已存设置（含解密密钥）
        }
        // provider 选择走单项 PropertyObjectController（复用属性面板的 INTERFACE 块 + label + margin 样式）。
        mProviderController.SetConfig(BuildProviderConfig(), mProviderData);
        if (initial != null)
            RefreshEnginePropertyPanel(initial);
        // 选择变更经数据对象 Modified 驱动（用户改 combo → 写入 mProviderData → 通知）。
        mProviderData.Modified.Subscribe(OnEngineSelectionChanged);

        // 之前 Submit 过（app Settings 记了 provider）才打开即静默自动接入，直接可聊天；否则首次发送再引导去设置。
        if (hadSaved && TryConnect(savedEngine, out _))
            AppendMessage(mActive, "system", ConnectedNotice()); // 启动即提示连到哪个模型

    }

    // 载入某 provider 已落盘的设置（含解密密钥）进 mSettings。各 provider 各记一份
    //（按来源包分桶 packageId → "agent-model:<id>"，避免不同包同 id provider 设置串味）。
    void LoadProviderSettings(string type)
    {
        var engine = AgentModelManager.GetInitedEngine(type);
        if (engine == null)
            return;
        var values = ExtensionSettingsStore.Load(AgentModelManager.GetPackageId(type), "agent-model:" + type, s => engine.GetPropertyConfig(new PropertyContext(s)));
        foreach (var kv in values)
            mSettings.SetValue(kv.Key, kv.Value);
        mSettings.Commit();
    }

    // 工程切换时由 Editor 调用：重建 Facade 与工具（runner 下次发送时按新工具重建，历史重置）。
    public void SetProject(IProject? project)
    {
        mProjectEditor = project != null ? new ProjectAgentEditor(project, mCurrentPartProvider, mQuantizationProvider) : null;
        mTools = mProjectEditor != null
            ? new List<IAgentTool>
            {
                // Layer 1 只读
                new ListTracksTool(mProjectEditor),
                new GetCurrentPartTool(mProjectEditor),
                new GetPlayheadTool(mProjectEditor),
                new SnapTickTool(mProjectEditor),
                new GetTrackDetailTool(mProjectEditor),
                new GetPartNotesTool(mProjectEditor),
                new GetPartParametersTool(mProjectEditor),
                new GetParameterTool(mProjectEditor),
                // Layer 2 业务级写（各一个可撤销单位）
                new TransposeNotesTool(mProjectEditor),
                new AddVibratoTool(mProjectEditor),
                new ShiftPitchTool(mProjectEditor),
                new SetTrackPropertiesTool(mProjectEditor),
                new AddTrackTool(mProjectEditor),
                new RemoveTrackTool(mProjectEditor),
                new SetTempoTool(mProjectEditor),
                new SetTimeSignatureTool(mProjectEditor),
                // Layer 3 批量 DSL（整批一个可撤销单位）
                new ApplyEditsTool(mProjectEditor),
            }
            : [];
        // 工具随新工程重建：各会话下次发送时按新工具重建 runner（对话历史经 SeedHistory / 会话消息保留）。
        foreach (var c in mContexts)
            c.Runner = null;
    }

    // 由 Editor 注入一次：实时读取钢琴窗当前编辑的 midi part / 当前量化（用户切 part / 改量化即变，故存访问器而非快照）。
    public void SetCurrentPartProvider(Func<IMidiPart?> provider) => mCurrentPartProvider = provider;
    public void SetQuantizationProvider(Func<IQuantization?> provider) => mQuantizationProvider = provider;

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

        // 会话菜单：锚定按钮正下方、左对齐；再次点击关闭（toggle）。每次打开时按本地已存会话动态填充（见 PopulateMenu）。
        // presenter 挂 agent-menu class（见 GlobalStyle.axaml）调成与原生菜单一致的底色/圆角/描边。
        mMenuFlyout = new Flyout() { Placement = PlacementMode.BottomEdgeAlignedLeft };
        mMenuFlyout.FlyoutPresenterClasses.Add("agent-menu");
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
        mTitleLabel.Margin = new(4, 0);
        // 改名提交（Enter / 失焦）：写入当前会话标题并标记为手动标题（不再被自动标题覆盖），已落盘则同步保存。
        mTitleLabel.EndInput.Subscribe(OnTitleEdited);
        header.Children.Add(mTitleLabel); // 填充中间列

        // 底部圆角输入区（描边 + 背景，和上方滚动区分隔）。
        var inputRow = new DockPanel() { LastChildFill = true };
        mSendButton = IconButton(Assets.Send, Style.LIGHT_WHITE.Opacity(0.85), Colors.White);
        mSendButton.Clicked += () => _ = OnSend();
        DockPanel.SetDock(mSendButton, Dock.Right);
        inputRow.Children.Add(mSendButton);
        // 停止键：与发送键同位、仅响应期可见，点击取消正在进行的请求。
        mStopButton = IconButton(Assets.Stop, Style.LIGHT_WHITE.Opacity(0.85), Colors.White);
        mStopButton.IsVisible = false;
        mStopButton.Clicked += () => mActive?.Cts?.Cancel(); // 停止键只取消当前可见会话的在飞请求
        DockPanel.SetDock(mStopButton, Dock.Right);
        inputRow.Children.Add(mStopButton);
        // 图片附件按钮：左侧，仅当前连接的会话声明支持图片输入时可见（见 RefreshAttachAvailability）。
        mAttachButton = IconButton(Assets.Image, Style.LIGHT_WHITE.Opacity(0.6), Colors.White);
        mAttachButton.IsVisible = false;
        ToolTip.SetTip(mAttachButton, "Attach image".Tr(this));
        mAttachButton.Clicked += () => _ = OnAttachClicked();
        DockPanel.SetDock(mAttachButton, Dock.Left);
        inputRow.Children.Add(mAttachButton);
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
            else if (e.Key == Key.V && (e.KeyModifiers & KeyModifiers.Control) != 0)
            {
                // 不设 Handled：剪贴板有图就入待发，同时让 TextBox 的文本粘贴照常进行（图文都在则两者都生效）。
                _ = TryPasteImageAsync();
            }
        }), Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        inputRow.Children.Add(mInput);

        // 待发附件缩略图条（输入行正上方、框内）：每个缩略图右上角带 ✕ 移除；空时整条隐藏。
        mAttachmentStrip.Orientation = Orientation.Horizontal;
        mAttachmentStrip.Spacing = 6;
        mAttachmentStrip.Margin = new(2, 4, 2, 2);
        mAttachmentStrip.IsVisible = false;
        var inputColumn = new StackPanel { Orientation = Orientation.Vertical, Children = { mAttachmentStrip, inputRow } };

        var inputBorder = new Border()
        {
            CornerRadius = new(8),
            BorderThickness = new(1),
            BorderBrush = Style.LIGHT_WHITE.Opacity(0.2).ToBrush(),
            Background = Style.BACK.ToBrush(),
            Margin = new(8),
            Padding = new(6, 2),
            Child = inputColumn,
        };

        // 中间丝滑滚动对话区的挂载点（透明背景让整块区域含消息下方空白都可命中滚轮）。各会话各持一个 ListView，
        // 切换只换 host 的 Child；宽度（=host 宽）对所有会话一致，故宽度订阅挂在 host 上、只更新当前可见会话的气泡。
        mMessagesHost.Background = Brushes.Transparent;
        // 气泡 MaxWidth 随对话区宽度自适应：留出对侧 ~40px 空白（避免占满整宽损可读性）；侧栏拖宽即时更新当前会话现有气泡。
        mMessagesHost.PropertyChanged += (_, e) =>
        {
            if (e.Property != Avalonia.Visual.BoundsProperty)
                return;
            mBubbleMaxWidth = Math.Max(140, mMessagesHost.Bounds.Width - 40);
            mContentMaxWidth = Math.Max(140, mMessagesHost.Bounds.Width - 24);
            ApplyBubbleWidths(mActive.View);
        };

        DockPanel.SetDock(header, Dock.Top);
        mChatView.Children.Add(header);
        var sep = new Border() { Height = 1, Background = Style.BACK.ToBrush() };
        DockPanel.SetDock(sep, Dock.Top);
        mChatView.Children.Add(sep);
        DockPanel.SetDock(inputBorder, Dock.Bottom);
        mChatView.Children.Add(inputBorder);
        // token 用量状态行（输入框正上方、细灰）：会话累计 + 当前上下文占用；空会话隐藏。dock 在 inputBorder 之后 → 位于其上、消息区之下。
        mTokenStatus.FontSize = 11;
        mTokenStatus.Foreground = Style.LIGHT_WHITE.Opacity(0.4).ToBrush();
        mTokenStatus.Margin = new(14, 0, 14, 2);
        mTokenStatus.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        mTokenStatus.IsVisible = false;
        DockPanel.SetDock(mTokenStatus, Dock.Bottom);
        mChatView.Children.Add(mTokenStatus);
        mChatView.Children.Add(mMessagesHost); // 最后一个 → 填充中间

        // 拖拽图片到对话区任意处即入待发（DragOver 仅在支持图片时显示「复制」效果）。
        DragDrop.SetAllowDrop(mChatView, true);
        mChatView.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        mChatView.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    // 刷新 token 状态行为当前会话的口径：会话累计（每轮 total 之和，含工具往返重复前缀）+ 当前上下文占用（最后一次模型调用的输入 token）。
    // 无累计（空会话/端点没返回 usage）则隐藏整行。
    void RefreshTokenStatus()
    {
        var ctx = mActive;
        if (ctx.CumulativeTokens <= 0)
        {
            mTokenStatus.IsVisible = false;
            return;
        }
        mTokenStatus.IsVisible = true;
        mTokenStatus.Text = string.Format("Context {0} · Session {1}".Tr(this), FormatTokens(ctx.ContextTokens), FormatTokens(ctx.CumulativeTokens));
        ToolTip.SetTip(mTokenStatus, string.Format("Current context ~{0:N0} tokens · Session total {1:N0} tokens".Tr(this), ctx.ContextTokens, ctx.CumulativeTokens));
    }

    // 紧凑显示：≥1000 显示为 k（一位小数、去尾零），否则原值。
    static string FormatTokens(long n)
        => n >= 1000 ? (n / 1000.0).ToString("0.#") + "k" : n.ToString();

    // 一轮成功回复后累加该会话的 token 口径：累计 += 本轮 total；上下文 = 本轮最后一次模型调用的输入（≈当前上下文大小，
    // 不用聚合 prompt——那是各轮求和、远大于实际上下文）。仅当前可见会话才刷新状态行。
    void AccumulateTurnTokens(SessionContext ctx, AgentTurnResult reply)
    {
        if (reply.Usage != null)
            ctx.CumulativeTokens += reply.Usage.TotalTokens;
        for (int i = reply.Trajectory.Count - 1; i >= 0; i--)
        {
            var m = reply.Trajectory[i];
            if (m.Role == AgentRole.Assistant && m.Usage != null)
            {
                ctx.ContextTokens = m.Usage.PromptTokens + m.Usage.CompletionTokens;
                break;
            }
        }
        if (ctx == mActive)
            RefreshTokenStatus();
    }

    // 重载会话时从存储重算 token 口径：累计 = 所有 assistant 消息 total 之和；上下文 = 最后一条 assistant 的输入+输出（≈当前上下文）。
    static void RestoreTokenStats(SessionContext ctx, ChatSession session)
    {
        long cumulative = 0;
        int context = 0;
        foreach (var m in session.Messages)
        {
            if (m.Role != "assistant")
                continue;
            if (m.TotalTokens.HasValue)
                cumulative += m.TotalTokens.Value;
            if (m.PromptTokens.HasValue) // 最后一条带用量的 assistant 胜出 → 末轮的上下文占用
                context = (m.PromptTokens ?? 0) + (m.CompletionTokens ?? 0);
        }
        ctx.CumulativeTokens = cumulative;
        ctx.ContextTokens = context;
    }

    // 新建一个会话的消息滚动区（自带动画轴；靠子项 MaxWidth 在无限宽测量下换行——见 ApplyBubbleWidths）。
    static ListView CreateMessagesList()
    {
        var list = new ListView();
        list.Orientation = Orientation.Vertical;
        list.Background = Brushes.Transparent;
        return list;
    }

    // ListView 用无限宽测量子项，子项必须靠 MaxWidth 才会换行。助手容器（去气泡）用近整宽、用户气泡/系统提示留对侧空白。
    void ApplyBubbleWidths(ListView list)
    {
        foreach (var c in list.Content.Children)
            c.MaxWidth = (c.Tag as string) == "assistant" ? mContentMaxWidth : mBubbleMaxWidth;
    }

    void OnMenuButtonClicked()
    {
        if (mMenuJustClosed)
            return; // 再次点击：刚被 light-dismiss 关闭，不重开 → toggle 关闭
        if (mMenuButton != null)
        {
            PopulateMenu();
            mMenuFlyout.ShowAt(mMenuButton);
        }
    }

    // 每次打开时重建内容：New Chat + 会话列表（点击切换/加载、右侧 ✕ 删除、运行中行首亮点）。
    // 列表 = 打开中的会话（含未落盘/正在后台跑的，点击直接切到其活视图）+ 仅存在磁盘上、未打开的会话（点击从盘加载）。
    //   · 关键：未落盘的运行中新会话也必须列出——否则切走后后台虽在跑却永远唤不回（用户实测 bug）。
    //   · 顺序：统一按"会话建立时刻"降序——位置稳定（切换/使用都不打乱），最新建立的在最上，用户可记忆某会话在第几个。
    // 用自定义 Flyout 而非 MenuFlyout：MenuItem 模板保留子菜单箭头/快捷键列，✕ 无法真正贴右；
    // StackPanel 装 DockPanel 行可完全控制布局——所有行同宽、✕ 对齐最右，并支持 hover 高亮与全名 tooltip。
    void PopulateMenu()
    {
        var stack = new StackPanel() { Orientation = Orientation.Vertical, MinWidth = 220 };
        stack.Children.Add(BuildMenuRow("New Chat".Tr(this), null, NewChat, null));

        var entries = new List<MenuEntry>();

        // 打开中的会话里"值得列出"的：正在跑的 或 已落盘的。纯空白未用的新会话不列（无可切换内容）。点击直接切到其活视图。
        foreach (var ctx in mContexts)
        {
            if (!ctx.Busy && ctx.Session == null)
                continue;
            var captured = ctx;
            entries.Add(new MenuEntry(ctx.CreatedAtUnix, ctx.Title, ctx.Busy,
                () => SwitchTo(captured),
                () => { DeleteContext(captured); mMenuFlyout.Hide(); }));
        }

        // 仅存在磁盘、当前未打开的会话（已打开的以活 context 为准、不重复列）。点击从盘加载。
        var openIds = new HashSet<string>(mContexts.Where(c => c.Session != null).Select(c => c.Session!.Id));
        foreach (var session in AgentSessionStore.List())
        {
            if (openIds.Contains(session.Id))
                continue;
            var captured = session;
            var titleText = string.IsNullOrWhiteSpace(session.Title) ? "Untitled".Tr(this) : session.Title;
            entries.Add(new MenuEntry(session.CreatedAtUnix, titleText, false,
                () => LoadSession(captured),
                () => { AgentSessionStore.Delete(captured.Id); mMenuFlyout.Hide(); }));
        }

        if (entries.Count > 0)
            stack.Children.Add(new Border() { Height = 1, Margin = new(8, 4), Background = Style.LIGHT_WHITE.Opacity(0.15).ToBrush() });
        foreach (var e in entries.OrderByDescending(x => x.CreatedAtUnix))
            stack.Children.Add(BuildMenuRow(e.Title, e.Title, e.OnClick, e.OnDelete, e.Running));

        mMenuFlyout.Content = stack;
    }

    // 菜单一条会话项（打开中的或仅磁盘上的）：携带建立时刻供统一排序、标题、是否运行中、点击与删除动作。
    readonly record struct MenuEntry(long CreatedAtUnix, string Title, bool Running, Action OnClick, Action OnDelete);

    // 关闭一个打开中的会话：停掉其在飞请求、移除上下文、删掉磁盘文件（若已落盘）；删的是当前会话则切到新空白会话。
    void DeleteContext(SessionContext ctx)
    {
        ctx.Cts?.Cancel();
        mContexts.Remove(ctx);
        if (ctx.Session != null)
            AgentSessionStore.Delete(ctx.Session.Id);
        if (ctx == mActive)
            NewChat();
    }

    // 单行：标题填充（过长省略号 + 全名 tooltip）、可选右侧 ✕ 删除、可选行首运行指示点。整行 hover 高亮，点击触发 onClick 并关闭菜单。
    Control BuildMenuRow(string text, string? tooltip, Action onClick, Action? onDelete, bool running = false)
    {
        var title = new TextBlock()
        {
            Text = text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 220,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Foreground = Colors.White.ToBrush(),
        };
        var dock = new DockPanel();

        if (running)
        {
            var dot = new TextBlock()
            {
                Text = "●",
                FontSize = 9,
                Margin = new(0, 0, 6, 0),
                Foreground = Style.BUTTON_PRIMARY.ToBrush(), // 后台请求进行中
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            DockPanel.SetDock(dot, Dock.Left);
            dock.Children.Add(dot);
        }

        if (onDelete != null)
        {
            var del = new TextBlock()
            {
                Text = "✕",
                FontSize = 11,
                Margin = new(12, 0, 0, 0),
                Foreground = Style.LIGHT_WHITE.Opacity(0.4).ToBrush(),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            del.PointerEntered += (_, _) => del.Foreground = Colors.IndianRed.ToBrush();
            del.PointerExited += (_, _) => del.Foreground = Style.LIGHT_WHITE.Opacity(0.4).ToBrush();
            del.PointerPressed += (_, e) => { e.Handled = true; onDelete(); }; // Handled 拦掉整行点击，避免删完又加载
            DockPanel.SetDock(del, Dock.Right);
            dock.Children.Add(del);
        }
        dock.Children.Add(title); // 填充剩余宽

        var row = new Border()
        {
            Padding = new(10, 6),
            CornerRadius = new(4),
            Background = Brushes.Transparent,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = dock,
        };
        if (!string.IsNullOrEmpty(tooltip))
            ToolTip.SetTip(row, tooltip);
        row.PointerEntered += (_, _) => row.Background = Style.LIGHT_WHITE.Opacity(0.08).ToBrush();
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        // ✕ 已置 Handled 时此处不触发（默认不收已处理事件）。
        row.PointerPressed += (_, e) => { if (e.Handled) return; onClick(); mMenuFlyout.Hide(); };
        return row;
    }

    // 新建空白会话并切到它——其他会话的后台管线不受影响（不取消、不清空）。
    void NewChat()
    {
        var ctx = NewContext();
        SwitchTo(ctx);
        if (mSession != null) // 空白新对话顶端提示当前连到哪个模型
            AppendMessage(ctx, "system", ConnectedNotice());
    }

    // 创建一个新会话上下文（独立视图 + 独立管线），登记到 mContexts；不切换、不填充内容。
    SessionContext NewContext()
    {
        var ctx = new SessionContext(CreateMessagesList())
        {
            Title = "New Chat".Tr(this),
            CreatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        mContexts.Add(ctx);
        return ctx;
    }

    // 切到某会话：仅换可见视图 + 头部标题 + 发送/停止键状态——不取消、不清空任何会话的在飞管线。
    void SwitchTo(SessionContext ctx)
    {
        mActive = ctx;
        mMessagesHost.Children.Clear();
        mMessagesHost.Children.Add(ctx.View);
        ApplyBubbleWidths(ctx.View); // 离屏期间侧栏可能被拖宽，切回时按当前宽度重排该会话气泡
        SetTitle(ctx.Title);
        RefreshSendControls();
        RefreshTokenStatus();
        ScrollToEnd(ctx);
    }

    // 发送/停止键反映当前会话忙碌态：忙→停止键，闲→发送键。切换会话、忙碌态变化时刷新。
    void RefreshSendControls()
    {
        mSendButton.IsVisible = !mActive.Busy;
        mStopButton.IsVisible = mActive.Busy;
    }

    // 加载已存会话：已打开（可能正在后台跑）则直接激活其活管线，否则新建上下文还原气泡 + 备好 runner 续聊历史
    //（仅对话文本；项目事实续聊时由模型重新调工具读取）。
    void LoadSession(ChatSession session)
    {
        // 已打开 → 直接激活其活视图，绝不重建：避免同一会话双开、丢失正在跑的进度。
        var existing = mContexts.FirstOrDefault(c => c.Session?.Id == session.Id);
        if (existing != null)
        {
            SwitchTo(existing);
            return;
        }

        var ctx = NewContext();
        RebuildHistoryView(ctx, session);

        ctx.Session = session;
        ctx.SeedHistory = ReconstructHistory(session);
        ctx.Runner = null; // 下次发送时用 SeedHistory 重建带历史的 runner
        RestoreTokenStats(ctx, session); // 从存储重算累计/上下文 token，使状态行重载即正确
        ctx.Title = string.IsNullOrWhiteSpace(session.Title) ? "Untitled".Tr(this) : session.Title;
        ctx.CreatedAtUnix = session.CreatedAtUnix; // 按原始创建时刻排序，加载不改变其在列表中的位置
        SwitchTo(ctx);
    }

    // 重建已存会话的对话视图（全量轨迹）：按轮分组、用户气泡 + 重放事件重建分步视图（文本/思考/工具块按序交错），与实时完全一致。
    void RebuildHistoryView(SessionContext ctx, ChatSession session)
    {
        var msgs = session.Messages;

        // 按轮分组——一轮 = 一条 user 消息 + 其后直到下条 user 之前的全部 assistant/tool 消息。
        int i = 0;
        while (i < msgs.Count)
        {
            if (msgs[i].Role == "user")
            {
                AppendUserMessage(ctx, msgs[i].Text, LoadAttachmentBytes(session.Id, msgs[i]));
                i++;
            }
            // 收集到下条 user 之前的助手/工具消息，重放成一条分步视图。容错：轨迹首条若非 user（异常文件），落单消息也独立成组。
            int start = i;
            while (i < msgs.Count && msgs[i].Role != "user")
                i++;
            if (i > start)
                ctx.View.Content.Children.Add(BuildReplayedTurn(msgs, start, i));
        }
    }

    // 把 [start, end) 区间的助手/工具记录重放进一个 AgentTurnView，重建分步视图（与实时同路径），包进助手容器返回。
    // 重放顺序即存储顺序：每条 assistant 先思考、再正文、再它的工具调用(started)；随后的 tool 记录给出对应结果(finished)。
    Control BuildReplayedTurn(List<ChatTurnMessage> msgs, int start, int end)
    {
        var turn = new AgentTurnView();
        var narration = new List<string>();
        int prompt = 0, completion = 0, total = 0;
        bool hasUsage = false;
        for (int k = start; k < end; k++)
        {
            var m = msgs[k];
            if (m.Role == "tool")
            {
                turn.Apply(new AgentToolFinished(m.ToolCallId ?? string.Empty, string.Empty, m.Text, m.IsError));
                continue;
            }
            // assistant
            if (!string.IsNullOrEmpty(m.Reasoning))
                turn.Apply(new AgentReasoningDelta(m.Reasoning));
            if (!string.IsNullOrEmpty(m.Text))
            {
                turn.Apply(new AgentTextDelta(m.Text));
                narration.Add(m.Text);
            }
            if (m.ToolCalls != null)
                foreach (var call in m.ToolCalls)
                    turn.Apply(new AgentToolStarted(call.Id, call.Name, call.ArgumentsJson));
            if (m.TotalTokens.HasValue)
            {
                hasUsage = true;
                prompt += m.PromptTokens ?? 0;
                completion += m.CompletionTokens ?? 0;
                total += m.TotalTokens.Value;
            }
        }
        turn.Seal();
        turn.EndThinking(); // 重载即已完成，移除"生成中"指示
        if (turn.IsEmpty)
            return AssistantContainer(BubbleText("(no text reply)", Colors.White.ToBrush()));
        var usage = hasUsage ? new AgentTokenUsage { PromptTokens = prompt, CompletionTokens = completion, TotalTokens = total } : null;
        turn.Append(BuildFooter(string.Join("\n\n", narration), usage));
        return AssistantContainer(turn.Root);
    }

    // 从会话消息重建 runner 续聊历史，带回完整工具往返（assistant 的工具调用 + tool 结果消息），使「重载 == 实时」
    // ——模型续聊时带上之前调了哪些工具、得到什么结果的上下文，不再失忆。思考(reasoning)不回发（它是输出而非输入）。
    // 旧版纯文本文件无 tool 记录、assistant 也无 ToolCalls，本映射自然降级为纯 user/assistant 文本。
    // 供加载会话、以及聊天中途换模型重连时复用——后者据此让新模型带上完整当前上下文。
    static List<AgentMessage> ReconstructHistory(ChatSession session)
    {
        var history = new List<AgentMessage>();
        foreach (var m in session.Messages)
        {
            switch (m.Role)
            {
                case "assistant":
                    history.Add(new AgentMessage
                    {
                        Role = AgentRole.Assistant,
                        Content = string.IsNullOrEmpty(m.Text) ? null : m.Text,
                        ToolCalls = m.ToolCalls is { Count: > 0 }
                            ? m.ToolCalls.Select(c => new AgentToolCall { Id = c.Id, Name = c.Name, ArgumentsJson = c.ArgumentsJson }).ToList()
                            : null,
                    });
                    break;
                case "tool":
                    history.Add(new AgentMessage { Role = AgentRole.Tool, ToolCallId = m.ToolCallId, Content = m.Text });
                    break;
                default:
                    history.Add(new AgentMessage { Role = AgentRole.User, Content = m.Text, Parts = BuildHistoryParts(session.Id, m) });
                    break;
            }
        }
        return history;
    }

    // 把存储的用户消息附件还原成多模态分片（读 blob 字节），让续聊上下文带上图片。无附件返回 null（退化为纯文本 Content）。
    static IReadOnlyList<AgentContentPart>? BuildHistoryParts(string sessionId, ChatTurnMessage m)
    {
        if (m.Attachments is not { Count: > 0 })
            return null;
        var parts = new List<AgentContentPart>();
        if (!string.IsNullOrEmpty(m.Text))
            parts.Add(AgentContentPart.OfText(m.Text));
        foreach (var a in m.Attachments)
        {
            var bytes = a.Data ?? AgentSessionStore.ReadBlob(sessionId, a.Hash, a.MediaType);
            if (bytes is { Length: > 0 })
                parts.Add(AgentContentPart.OfImage(bytes, a.MediaType));
        }
        return parts.Count > 0 ? parts : null;
    }

    // 读取用户消息各附件的字节（重载渲染缩略图用）：内存里 Data 优先，否则从 blob 读。无则空列表。
    static List<byte[]> LoadAttachmentBytes(string sessionId, ChatTurnMessage m)
    {
        var result = new List<byte[]>();
        if (m.Attachments == null)
            return result;
        foreach (var a in m.Attachments)
        {
            var bytes = a.Data ?? AgentSessionStore.ReadBlob(sessionId, a.Hash, a.MediaType);
            if (bytes is { Length: > 0 })
                result.Add(bytes);
        }
        return result;
    }

    // 一轮成功回复后记入该会话并落盘（取消/出错的轮不记）。新会话首轮顺带触发自动标题。
    // ctx 是发起本轮时捕获的会话——即便用户中途已切到别的会话，也只写它、不串会话。
    // 存全量：用户输入 + runner 返回的有序轨迹（助手回复含思考/工具调用/用量、工具结果含错误标记）原样落盘——
    // 重载即可完整重建分步视图、并把含工具往返的上下文回灌续聊。assistantText 是合并叙述，仅用于自动标题。
    void RecordTurn(SessionContext ctx, string userText, IReadOnlyList<ChatAttachment>? userAttachments, string assistantText, IReadOnlyList<AgentTurnMessage> trajectory)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool isNew = ctx.Session == null;
        ctx.Session ??= new ChatSession { CreatedAtUnix = ctx.CreatedAtUnix }; // 沿用上下文建立时刻，落盘前后排序位置一致
        var session = ctx.Session;
        session.SchemaVersion = 1; // 本轮起以全量轨迹格式落盘
        // 用户消息（带附件则附 ChatAttachment，含原始字节 → Save 落 blob、清单只引用）。
        session.Messages.Add(new ChatTurnMessage { Role = "user", Text = userText, Attachments = userAttachments is { Count: > 0 } ? userAttachments.ToList() : null });
        foreach (var m in trajectory)
            session.Messages.Add(ToStored(m));
        session.UpdatedAtUnix = now;

        if (isNew && ctx.TitleManual && !string.IsNullOrWhiteSpace(ctx.Title))
        {
            // 用户已手动命名 → 直接用它，不占位截断、不触发自动标题。
            session.Title = ctx.Title;
            AgentSessionStore.Save(session);
        }
        else if (isNew)
        {
            session.Title = Truncate(userText, 30); // 先用首条截断占位，确保列表立刻有可读名
            ctx.Title = session.Title;
            if (ctx == mActive)
                SetTitle(session.Title);
            AgentSessionStore.Save(session);
            _ = GenerateTitleAsync(ctx, userText, assistantText); // 随后用 LLM 总结覆盖
        }
        else
        {
            AgentSessionStore.Save(session);
        }
    }

    // 把 runner 的一条轨迹消息转成落盘记录（助手带思考/工具调用/用量，工具带结果/错误标记）。
    static ChatTurnMessage ToStored(AgentTurnMessage m)
    {
        if (m.Role == AgentRole.Tool)
            return new ChatTurnMessage
            {
                Role = "tool",
                Text = m.Content ?? string.Empty,
                ToolCallId = m.ToolCallId,
                IsError = m.IsError,
            };
        return new ChatTurnMessage
        {
            Role = "assistant",
            Text = m.Content ?? string.Empty,
            Reasoning = m.Reasoning,
            ToolCalls = m.ToolCalls?.Select(c => new ChatToolCall { Id = c.Id, Name = c.Name, ArgumentsJson = c.ArgumentsJson }).ToList(),
            PromptTokens = m.Usage?.PromptTokens,
            CompletionTokens = m.Usage?.CompletionTokens,
            TotalTokens = m.Usage?.TotalTokens,
        };
    }

    // 自动标题：用模型把首轮总结成几字标题，覆盖占位的首条截断。失败/未连接则保留占位（已是首条截断）。
    async Task GenerateTitleAsync(SessionContext ctx, string userText, string assistantText)
    {
        var session = ctx.Session;
        var session_model = mSession;
        if (session == null || session_model == null)
            return;
        try
        {
            var request = new AgentModelRequest
            {
                Messages = new List<AgentMessage>
                {
                    new() { Role = AgentRole.System, Content = "Generate a concise title (max 6 words) for this conversation. Reply with only the title text — no quotes, no trailing punctuation, no explanation or any other text." },
                    new() { Role = AgentRole.User, Content = "User: " + userText + "\n\nAssistant: " + Truncate(assistantText, 500) },
                },
            };
            var reply = await session_model.SendAsync(request, CancellationToken.None);
            // 防线：模型没遵守"只回简短标题"——回了一大段、或把工具结果/数据当回复 dump（曾致标题=一长串内容或 {"音轨名称":...} JSON）→
            // 丢弃，保留占位（首条用户消息截断，已是可读标题）。真·6 词标题远短于 60 字、也不会以 { [ 开头。
            var raw = (reply.Content ?? string.Empty).Trim();
            if (raw.Length == 0 || raw.Length > 60 || raw[0] == '{' || raw[0] == '[')
                return;
            var title = SanitizeTitle(raw);
            if (string.IsNullOrEmpty(title))
                return;
            if (ctx.TitleManual) // 生成期间用户已手动改名 → 不覆盖
                return;
            session.Title = title;
            ctx.Title = title;
            AgentSessionStore.Save(session);
            void Apply() { if (mActive == ctx) SetTitle(title); }
            if (Dispatcher.UIThread.CheckAccess()) Apply();
            else Dispatcher.UIThread.Post(Apply);
        }
        catch (Exception ex)
        {
            Log.Info("Agent title generation failed, keeping fallback title: " + ex.Message);
        }
    }

    // 取首行、限长，用于会话标题占位与喂给标题模型的助手文本截断。
    static string Truncate(string text, int max)
    {
        text = (text ?? string.Empty).Trim();
        int nl = text.IndexOfAny(new[] { '\n', '\r' });
        if (nl >= 0)
            text = text[..nl].TrimEnd();
        return text.Length <= max ? text : text[..max].TrimEnd() + "…";
    }

    // 清洗模型给的标题：去引号/换行/末尾标点，限长。
    static string SanitizeTitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        var t = raw.Replace('\r', ' ').Replace('\n', ' ').Trim();
        t = t.Trim('"', '\'', '“', '”', '「', '」', '.', '。', ' ');
        return t.Length <= 40 ? t : t[..40].TrimEnd() + "…";
    }

    public void SetTitle(string title)
    {
        mTitleLabel.Text = title;
        ToolTip.SetTip(mTitleLabel, title);
    }

    // 标题改名提交（EditableLabel 在 Enter / 失焦时触发）：非空且有变化才采用——写入当前会话标题、
    // 标记为手动标题（不再被自动标题覆盖），已落盘则同步保存；为空则还原为当前标题。
    void OnTitleEdited()
    {
        var title = mTitleLabel.Text.Trim();
        if (string.IsNullOrEmpty(title))
        {
            SetTitle(mActive.Title); // 不允许清空，还原
            return;
        }
        if (title == mActive.Title)
            return;
        mActive.Title = title;
        mActive.TitleManual = true;
        SetTitle(title); // 规范化显示文本 + 更新 tooltip
        if (mActive.Session != null)
        {
            mActive.Session.Title = title;
            AgentSessionStore.Save(mActive.Session);
        }
    }

    // 已连接提示文案：用户更关心模型而非供应商——优先模型名，缺省（适配器未用 model 字段）才回退到供应商 type。
    string ConnectedNotice()
    {
        var model = mSettings.GetValue("model", PropertyValue.Create(string.Empty)).ToString();
        return string.IsNullOrEmpty(model)
            ? string.Format("Connected via '{0}'.".Tr(this), CurrentEngineType())
            : string.Format("Connected to '{0}'.".Tr(this), model);
    }

    async Task OnSend()
    {
        // 发起前捕获当前会话——之后所有渲染/落盘都只认这个引用，即便用户中途切到别的会话也不串、不写错会话。
        var ctx = mActive;
        if (ctx.Busy)
            return;

        var text = mInput.Text.Trim();
        var images = mPendingImages.ToList(); // 本轮附件快照（发送即清空待发条，避免下一轮重复带上）
        if (string.IsNullOrEmpty(text) && images.Count == 0)
            return;

        if (mSession == null)
        {
            AppendMessage(ctx, "system", "Not connected. Open settings (gear) to choose a model and submit.".Tr(this));
            ShowSettings();
            return;
        }
        if (mProjectEditor == null)
        {
            AppendMessage(ctx, "system", "No project is open.".Tr(this));
            return;
        }

        mInput.Text = string.Empty;
        mPendingImages.Clear();
        RebuildAttachmentStrip();
        AppendUserMessage(ctx, text, images.Select(i => i.Data).ToList());
        var bubble = AddAssistantBubble(ctx); // 响应期占位气泡（动态等待指示）
        var cts = new CancellationTokenSource();
        ctx.Cts = cts;
        SetBusy(ctx, true);

        // 分步渲染：把 runner 的进度事件（流式文本增量 / 工具开始·完成）按序铺进气泡，全程可见模型在说什么、调了哪个工具、结果如何。
        // 首个事件到达前保持等待动画（ThinkingDots），到达即把气泡内容换成分步视图。各轮叙述各自成段保留——不再被最终文本整体替换。
        // 气泡属于 ctx.View，即便用户切走（视图离屏）流式仍写进它，切回即见进度；滚动只在该会话可见时执行。
        var turn = new AgentTurnView();
        bool swapped = false;
        void EnsureSwapped() { if (!swapped) { bubble.Child = turn.Root; swapped = true; } }
        // Progress<T> 在创建处（UI 线程）的同步上下文上派发，事件按 runner 发出顺序 FIFO 到达——文本与工具步骤的先后关系正确。
        void Handle(AgentEvent e) { EnsureSwapped(); turn.Apply(e); ScrollToEnd(ctx); }

        try
        {
            ctx.Runner ??= new AgentRunner(mSession, mTools, SystemPrompt, ctx.SeedHistory);
            var parts = images.Count > 0 ? images.Select(i => AgentContentPart.OfImage(i.Data, i.MediaType)).ToList() : null;
            var reply = await ctx.Runner.SendAsync(text, new Progress<AgentEvent>(Handle), cts.Token, parts);
            turn.Seal();
            if (turn.IsEmpty)
                bubble.Child = BubbleText("(no text reply)", Colors.White.ToBrush());
            else
                turn.Append(BuildFooter(reply.Text, reply.Usage));
            var attachments = images.Count > 0
                ? images.Select(i => new ChatAttachment { Hash = AgentSessionStore.ComputeHash(i.Data), MediaType = i.MediaType, Data = i.Data }).ToList()
                : null;
            RecordTurn(ctx, text, attachments, reply.Text, reply.Trajectory);
            AccumulateTurnTokens(ctx, reply);
        }
        catch (OperationCanceledException)
        {
            // 用户主动停止：保留已渲染的分步内容 + 末尾灰字 Stopped，并把仍在运行的工具块标记中止，不当错误（红字）。
            turn.Seal();
            turn.MarkPendingAborted();
            EnsureSwapped();
            turn.Append(NoticeLine("Stopped".Tr(this), Style.LIGHT_WHITE.Opacity(0.5).ToBrush()));
        }
        catch (Exception ex)
        {
            // 中途报错同样保留已渲染的分步内容，错误作末尾红字，不丢已输出有效内容。
            turn.Seal();
            turn.MarkPendingAborted();
            EnsureSwapped();
            turn.Append(NoticeLine("Error: " + ex.Message, Colors.IndianRed.ToBrush()));
        }
        finally
        {
            turn.EndThinking(); // 生成结束（成功/停止/出错）：移除底部"生成中"三点动画
            cts.Dispose();
            if (ctx.Cts == cts) // 仅清掉本轮自己的取消源（该会话期间不会有并发的第二轮）
                ctx.Cts = null;
            SetBusy(ctx, false);
            ScrollToEnd(ctx);
        }
    }

    void AppendMessage(SessionContext ctx, string role, string text)
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
        ctx.View.Content.Children.Add(item);
        ScrollToEnd(ctx);
    }

    // 用户消息气泡：纯文本走 BubbleText；带图片则图文竖排（图片在上、文本在下）。供实时发送与重载复用。
    void AppendUserMessage(SessionContext ctx, string text, IReadOnlyList<byte[]> images)
    {
        var content = images.Count > 0 ? BuildUserContent(text, images) : (Control)BubbleText(text, Colors.White.ToBrush());
        ctx.View.Content.Children.Add(Bubble(content, mine: true));
        ScrollToEnd(ctx);
    }

    // 用户气泡内容：每张图片一个受限尺寸的 Image（圆角），其下接文本（若有）。
    Control BuildUserContent(string text, IReadOnlyList<byte[]> images)
    {
        var sp = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        foreach (var data in images)
        {
            var bmp = BitmapFromBytes(data);
            if (bmp == null)
                continue;
            var thumb = new Border
            {
                CornerRadius = new(6),
                ClipToBounds = true,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Child = new Avalonia.Controls.Image { Source = bmp, Stretch = Stretch.Uniform, MaxWidth = 220, MaxHeight = 220 },
            };
            var captured = bmp;
            thumb.PointerPressed += (_, e) => { e.Handled = true; ShowImagePreview(captured); }; // 点击放大预览
            sp.Children.Add(thumb);
        }
        if (!string.IsNullOrEmpty(text))
            sp.Children.Add(BubbleText(text, Colors.White.ToBrush()));
        return sp;
    }

    // 点击会话中的图片 → 盖满主窗的 lightbox：半透明黑底居中显示大图，支持滚轮（以光标为锚点）缩放、中键拖拽平移；
    // 点背景（图片以外区域）或按 Esc 关闭。挂在 OverlayLayer 上以覆盖整窗（非仅侧栏）。
    void ShowImagePreview(Avalonia.Media.Imaging.Bitmap bmp)
    {
        var layer = Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(mRoot);
        if (layer == null)
            return;

        // 单实例守卫：已开则先关旧的（点不同图片即替换，同时复位缩放/平移）。
        if (mImagePreview != null)
            layer.Children.Remove(mImagePreview);

        const double MinScale = 0.1, MaxScale = 10;
        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform(0, 0);
        var image = new Avalonia.Controls.Image
        {
            Source = bmp,
            Stretch = Stretch.None, // 默认按原始尺寸显示、居中（滚轮再缩放）
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            RenderTransformOrigin = Avalonia.RelativePoint.TopLeft, // 配合下方公式：缩放绕图片左上角，平移用视口像素
            RenderTransform = new TransformGroup { Children = { scale, translate } },
        };

        var backdrop = new Border
        {
            Background = new SolidColorBrush(Colors.Black, 0.85),
            ClipToBounds = true, // 放大平移后超出视口的部分裁掉
            Focusable = true,    // 接收 Esc
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Arrow),
            Child = image,
        };
        mImagePreview = backdrop;

        void Close()
        {
            layer.Children.Remove(backdrop);
            if (ReferenceEquals(mImagePreview, backdrop))
                mImagePreview = null;
        }

        // OverlayLayer 继承自 Canvas，不拉伸子项——须把 backdrop 尺寸显式设为 layer 尺寸才能盖满主窗。
        void SyncSize()
        {
            backdrop.Width = layer.Bounds.Width;
            backdrop.Height = layer.Bounds.Height;
        }
        SyncSize();
        EventHandler<Avalonia.AvaloniaPropertyChangedEventArgs> onLayerBounds = (_, e) =>
        {
            if (e.Property == Avalonia.Visual.BoundsProperty)
                SyncSize();
        };
        layer.PropertyChanged += onLayerBounds;

        // 滚轮缩放：以光标位置为锚点（公式 t1 = c - A - f·(c - A - t0)，A=图片布局左上角，f=新旧缩放比）。
        backdrop.PointerWheelChanged += (_, e) =>
        {
            e.Handled = true;
            var s0 = scale.ScaleX;
            var s1 = Math.Clamp(s0 * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15), MinScale, MaxScale);
            if (s1 == s0)
                return;
            var f = s1 / s0;
            var c = e.GetPosition(backdrop);
            var a = image.Bounds.Position; // 居中布局后的左上角（不受 RenderTransform 影响）
            translate.X = c.X - a.X - f * (c.X - a.X - translate.X);
            translate.Y = c.Y - a.Y - f * (c.Y - a.Y - translate.Y);
            scale.ScaleX = scale.ScaleY = s1;
        };

        // 左键/中键拖拽平移；未拖动的点击（窗口任意处，含图片本身）关闭预览。
        var pressed = false;
        var dragged = false;
        var start = default(Avalonia.Point);
        var last = default(Avalonia.Point);
        backdrop.PointerPressed += (_, e) =>
        {
            var p = e.GetCurrentPoint(backdrop).Properties;
            if (!p.IsLeftButtonPressed && !p.IsMiddleButtonPressed)
                return;
            pressed = true;
            dragged = false;
            start = last = e.GetPosition(backdrop);
            e.Pointer.Capture(backdrop);
            e.Handled = true;
        };
        backdrop.PointerMoved += (_, e) =>
        {
            if (!pressed)
                return;
            var now = e.GetPosition(backdrop);
            translate.X += now.X - last.X;
            translate.Y += now.Y - last.Y;
            last = now;
            if (Math.Abs(now.X - start.X) + Math.Abs(now.Y - start.Y) > 4)
                dragged = true; // 超阈值算拖拽，松手不关闭
            e.Handled = true;
        };
        backdrop.PointerReleased += (_, e) =>
        {
            if (!pressed)
                return;
            pressed = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            if (!dragged)
                Close(); // 点击（未拖动）任意处关闭
        };
        backdrop.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        };
        backdrop.DetachedFromVisualTree += (_, _) => layer.PropertyChanged -= onLayerBounds;

        layer.Children.Add(backdrop);
        backdrop.Focus(); // 让 Esc 立即生效
    }

    // ───────────────── 图片附件 ─────────────────

    // 当前连接是否支持图片输入 → 启停📎按钮。在连接建立/切换（TryConnect 成功）与启动时刷新。
    void RefreshAttachAvailability()
    {
        mAttachButton.IsVisible = mSession != null && mSession.SupportedInput.HasFlag(AgentModality.Image);
    }

    // 点📎：多选图片文件 → 读字节 → 限尺寸预处理 → 入待发条。
    async Task OnAttachClicked()
    {
        var top = TopLevel.GetTopLevel(mRoot);
        if (top == null)
            return;
        IReadOnlyList<Avalonia.Platform.Storage.IStorageFile> files;
        try
        {
            files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Attach image".Tr(this),
                AllowMultiple = true,
                FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Images") { Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.bmp" } } },
            });
        }
        catch (Exception ex)
        {
            Log.Warning("Agent image picker failed: " + ex.Message);
            return;
        }

        foreach (var f in files)
            await IngestStorageFileAsync(f);
        RebuildAttachmentStrip();
    }

    // 三条图片入口（点选 / 粘贴 / 拖拽）的共同收口：限尺寸/转码后入待发列表。调用方负责随后 RebuildAttachmentStrip。
    void IngestImage(byte[] raw, string mediaType)
    {
        var (data, mime) = PrepareImage(raw, mediaType);
        if (data.Length > 0)
            mPendingImages.Add(new PendingImage(data, mime));
    }

    // 读一个 StorageFile（点选/拖拽来的文件）为图片附件；非图片扩展名跳过。
    async Task IngestStorageFileAsync(Avalonia.Platform.Storage.IStorageFile f)
    {
        var mime = MimeFromName(f.Name);
        try
        {
            await using var stream = await f.OpenReadAsync();
            using var mem = new System.IO.MemoryStream();
            await stream.CopyToAsync(mem);
            IngestImage(mem.ToArray(), mime);
        }
        catch (Exception ex)
        {
            Log.Warning("Agent failed to read image '" + f.Name + "': " + ex.Message);
        }
    }

    // 粘贴（Ctrl+V）：剪贴板有图片则取出入待发；仅当前会话支持图片时才尝试，否则放行普通文本粘贴。
    async Task TryPasteImageAsync()
    {
        if (mSession == null || !mSession.SupportedInput.HasFlag(AgentModality.Image))
            return;
        var clipboard = TopLevel.GetTopLevel(mRoot)?.Clipboard;
        if (clipboard == null)
            return;
        var img = await TryReadClipboardImageAsync(clipboard);
        if (img is { } x)
        {
            IngestImage(x.Data, x.Mime);
            RebuildAttachmentStrip();
        }
    }

    // 从剪贴板读图片：优先直出格式（PNG）；否则 Windows 的 DIB（无文件头的位图）补上 BMP 文件头还原；都没有则 null。
    // 没命中时记录可用格式，便于在不同来源/平台上排查。
    static async Task<(byte[] Data, string Mime)?> TryReadClipboardImageAsync(Avalonia.Input.Platform.IClipboard clipboard)
    {
        string[] formats;
        try { formats = await clipboard.GetFormatsAsync(); }
        catch { return null; }

        async Task<byte[]?> GetBytes(string fmt)
        {
            if (!formats.Contains(fmt))
                return null;
            try { return await clipboard.GetDataAsync(fmt) as byte[]; }
            catch { return null; }
        }

        foreach (var fmt in new[] { "PNG", "image/png", "public.png" })
            if (await GetBytes(fmt) is { Length: > 8 } png)
                return (png, "image/png");
        foreach (var fmt in new[] { "DeviceIndependentBitmap", "CF_DIB", "DIB" })
            if (await GetBytes(fmt) is { Length: > 40 } dib && DibToBmp(dib) is { } bmp)
                return (bmp, "image/bmp");
        foreach (var fmt in new[] { "image/bmp", "Bitmap" })
            if (await GetBytes(fmt) is { Length: > 14 } bb)
                return (bb, "image/bmp");

        Log.Info("[AgentPaste] no image on clipboard. formats: " + string.Join(", ", formats));
        return null;
    }

    // 给无文件头的 DIB（Windows CF_DIB）补 14 字节 BMP 文件头，拼成可被解码器识别的完整 BMP。
    static byte[]? DibToBmp(byte[] dib)
    {
        try
        {
            int headerSize = BitConverter.ToInt32(dib, 0);          // biSize（BITMAPINFOHEADER=40）
            short bitCount = BitConverter.ToInt16(dib, 14);
            int compression = BitConverter.ToInt32(dib, 16);
            int clrUsed = BitConverter.ToInt32(dib, 32);
            int paletteEntries = clrUsed != 0 ? clrUsed : (bitCount <= 8 ? (1 << bitCount) : 0);
            int masks = compression == 3 ? 12 : 0;                  // BI_BITFIELDS：像素前有 3 个 DWORD 掩码
            int pixelOffset = 14 + headerSize + masks + paletteEntries * 4;
            int fileSize = 14 + dib.Length;
            var bmp = new byte[fileSize];
            bmp[0] = (byte)'B';
            bmp[1] = (byte)'M';
            BitConverter.GetBytes(fileSize).CopyTo(bmp, 2);
            BitConverter.GetBytes(pixelOffset).CopyTo(bmp, 10);
            dib.CopyTo(bmp, 14);
            return bmp;
        }
        catch { return null; }
    }

    // 拖拽：含文件时显示「复制」效果（仅当前会话支持图片）。
    void OnDragOver(object? sender, Avalonia.Input.DragEventArgs e)
    {
        bool ok = (mSession?.SupportedInput.HasFlag(AgentModality.Image) ?? false) && e.Data.Contains(Avalonia.Input.DataFormats.Files);
        e.DragEffects = ok ? Avalonia.Input.DragDropEffects.Copy : Avalonia.Input.DragDropEffects.None;
        e.Handled = true;
    }

    // 拖放：把拖进来的图片文件入待发。
    async void OnDrop(object? sender, Avalonia.Input.DragEventArgs e)
    {
        e.Handled = true;
        if (mSession == null || !mSession.SupportedInput.HasFlag(AgentModality.Image))
            return;
        var files = e.Data.GetFiles();
        if (files == null)
            return;
        bool any = false;
        foreach (var item in files)
        {
            if (item is Avalonia.Platform.Storage.IStorageFile f && IsImageName(f.Name))
            {
                await IngestStorageFileAsync(f);
                any = true;
            }
        }
        if (any)
            RebuildAttachmentStrip();
    }

    static bool IsImageName(string name)
        => System.IO.Path.GetExtension(name).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp";

    // 重建待发缩略图条：每格=44×44 缩略图 + 右上角 ✕ 移除；空则隐藏整条。
    void RebuildAttachmentStrip()
    {
        mAttachmentStrip.Children.Clear();
        foreach (var pending in mPendingImages)
        {
            var captured = pending;
            var bmp = BitmapFromBytes(pending.Data);
            var thumb = new Border
            {
                Width = 44,
                Height = 44,
                CornerRadius = new(4),
                ClipToBounds = true,
                Background = Style.INTERFACE.ToBrush(),
                Cursor = bmp == null ? null : new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Child = bmp == null ? null : new Avalonia.Controls.Image { Source = bmp, Stretch = Stretch.UniformToFill },
            };
            if (bmp != null)
                thumb.PointerPressed += (_, e) => { e.Handled = true; ShowImagePreview(bmp); }; // 点缩略图放大预览（✕ 已 Handled，不冲突）
            var remove = new TextBlock
            {
                Text = "✕",
                FontSize = 10,
                Padding = new(2, 0),
                Foreground = Colors.White.ToBrush(),
                Background = Style.BACK.Opacity(0.75).ToBrush(),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            remove.PointerPressed += (_, e) => { e.Handled = true; mPendingImages.Remove(captured); RebuildAttachmentStrip(); };
            mAttachmentStrip.Children.Add(new Panel { Children = { thumb, remove } });
        }
        mAttachmentStrip.IsVisible = mPendingImages.Count > 0;
    }

    // 限尺寸 + 转码：长边超过 ImageMaxEdge 则等比缩放；非 OpenAI 友好格式（如剪贴板 BMP）一律重编码 PNG。
    // 友好且够小则原样保留（不丢 JPEG 压缩）。解码失败则原样回退（最坏由端点决定接不接受）。
    static (byte[] Data, string MediaType) PrepareImage(byte[] raw, string mediaType)
    {
        const int ImageMaxEdge = 1568;
        bool friendly = mediaType is "image/png" or "image/jpeg" or "image/webp" or "image/gif";
        try
        {
            using var inMem = new System.IO.MemoryStream(raw);
            var bmp = new Avalonia.Media.Imaging.Bitmap(inMem);
            int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height;
            int longest = Math.Max(w, h);
            if (friendly && (longest <= ImageMaxEdge || longest == 0))
                return (raw, mediaType); // 友好格式、尺寸够小 → 原样
            using var outMem = new System.IO.MemoryStream();
            if (longest > ImageMaxEdge && longest > 0)
            {
                double scale = (double)ImageMaxEdge / longest;
                using var scaled = bmp.CreateScaledBitmap(new Avalonia.PixelSize(Math.Max(1, (int)(w * scale)), Math.Max(1, (int)(h * scale))));
                scaled.Save(outMem); // Avalonia Bitmap.Save 输出 PNG
            }
            else
            {
                bmp.Save(outMem); // 仅转码（如 BMP→PNG），不缩放
            }
            return (outMem.ToArray(), "image/png");
        }
        catch (Exception ex)
        {
            Log.Warning("Agent image preprocess failed, sending original: " + ex.Message);
            return (raw, mediaType);
        }
    }

    static Avalonia.Media.Imaging.Bitmap? BitmapFromBytes(byte[]? data)
    {
        if (data is not { Length: > 0 })
            return null;
        try { return new Avalonia.Media.Imaging.Bitmap(new System.IO.MemoryStream(data)); }
        catch { return null; }
    }

    static string MimeFromName(string name)
    {
        var ext = System.IO.Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => "image/png",
        };
    }

    readonly record struct PendingImage(byte[] Data, string MediaType);

    // agent 侧消息容器（无气泡），返回 Border 以便回复回来后替换其内容（动态等待指示 → 分步视图 / 错误文本）。
    Border AddAssistantBubble(SessionContext ctx)
    {
        var bubble = AssistantContainer(AgentTurnView.ThinkingDots());
        ctx.View.Content.Children.Add(bubble);
        ScrollToEnd(ctx);
        return bubble;
    }

    // 助手消息容器：取消气泡（无底色、满宽左对齐）——窄侧栏里把横向空间全留给回复内容，对标 ChatGPT/Claude 弱化回复气泡。
    // Tag="assistant" 让宽度自适应订阅跳过它（不给它套 MaxWidth），区别于用户气泡。
    Border AssistantContainer(Control content) => new()
    {
        Tag = "assistant",
        Background = Brushes.Transparent,
        Margin = new(12, 4),
        // ListView 无限宽测量：靠 MaxWidth 约束才会换行（Stretch 在无限宽下不起作用）。创建即设近整宽，首个 Bounds 事件前也能换行。
        MaxWidth = mContentMaxWidth,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        Child = content,
    };

    // 用户气泡：靠右、主色底；agent 用 AssistantContainer 不再走这里。MaxWidth 随对话区宽度自适应。
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
    // 供加载已存会话时渲染整条助手文本；新消息走 AgentTurnView 分步渲染、末尾单独追加 BuildFooter。
    Control AssistantContent(string markdown, AgentTokenUsage? usage)
        => new StackPanel { Orientation = Orientation.Vertical, Children = { ChatMarkdownRenderer.Render(markdown), BuildFooter(markdown, usage) } };

    // 脚注一行：token 总量靠左（带单位，hover 看输入/输出明细）、Copy 靠右（复制 markdownToCopy 原文；端点未返回 usage 则只有 Copy）。
    Control BuildFooter(string markdownToCopy, AgentTokenUsage? usage)
    {
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
            _ = TopLevel.GetTopLevel(copy)?.Clipboard?.SetTextAsync(markdownToCopy);
        };

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
                Margin = new(0, 0, 16, 0), // 与右侧 Copy 留间距：短回复气泡窄时两者会贴到一起
            };
            ToolTip.SetTip(tokens, string.Format("Input {0:N0} · Output {1:N0}".Tr(this), usage.PromptTokens, usage.CompletionTokens));
            DockPanel.SetDock(tokens, Dock.Left);
            footer.Children.Add(tokens);
        }
        return footer;
    }

    // 一行斜体提示（停止=灰、出错=红），追加在分步内容末尾。用 SelectableTextBlock：报错文案常需复制排查。
    SelectableTextBlock NoticeLine(string text, IBrush color) => new()
    {
        Text = text,
        FontSize = 11,
        FontStyle = FontStyle.Italic,
        Foreground = color,
        TextWrapping = TextWrapping.Wrap,
        Margin = new(0, 4, 0, 0),
    };

    // 自动滚到底：大值经轴内 clamp 到底部（动画轴；轮滚自带顺滑动画）。仅当该会话可见时滚动——离屏会话滚动无意义。
    void ScrollToEnd(SessionContext ctx)
    {
        if (ctx != mActive)
            return;
        Dispatcher.UIThread.Post(() => ctx.View.VerticalAxis.ViewOffset = 1e9, DispatcherPriority.Background);
    }

    // 标记某会话的忙碌态（输入框始终可用，由 ctx.Busy 拦该会话内回车重复发送）。若它是当前可见会话，同步发送/停止键。
    void SetBusy(SessionContext ctx, bool busy)
    {
        ctx.Busy = busy;
        if (ctx == mActive)
            RefreshSendControls();
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
        // Model Provider 选择 + 引擎属性面板都用 PropertyObjectController（同 INTERFACE 块、同 label/margin 样式），连成统一面板。
        content.Children.Add(mProviderController);
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

    // provider 选择的单项 config：复用 ComboBoxConfig（label 走 DisplayText，由属性面板渲成统一样式）。
    ObjectConfig BuildProviderConfig()
    {
        var props = new OrderedMap<string, IControllerConfig>();
        props.Add(EngineKey, new ComboBoxConfig { DisplayText = "Model Provider".Tr(this), Options = mEngineOptions });
        return new ObjectConfig { Properties = props };
    }

    string CurrentEngineType() => mProviderData.GetValue(EngineKey, PropertyValue.Create(string.Empty)).ToString() ?? string.Empty;

    void OnEngineSelectionChanged()
    {
        var type = CurrentEngineType();
        if (string.IsNullOrEmpty(type))
            return;
        LoadProviderSettings(type); // 切到某 provider：先载入它各自已存的设置，再刷新面板
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
        var type = CurrentEngineType();
        if (string.IsNullOrEmpty(type))
            return;

        if (TryConnect(type, out var error))
        {
            SaveSettings(type);
            ShowChat();
            AppendMessage(mActive, "system", ConnectedNotice());
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
            // 聊天中途换模型不丢上下文：每个会话据其已记录对话（RecordTurn 实时维护）重建续聊历史，下次发送时新 runner 带它重建。
            foreach (var c in mContexts)
            {
                if (c.Session != null)
                    c.SeedHistory = ReconstructHistory(c.Session);
                c.Runner = null;
            }
            RefreshAttachAvailability(); // 新连接的会话可能支持/不支持图片 → 启停📎
            return true;
        }
        catch (Exception ex)
        {
            error = "Submit failed: " + ex.Message;
            return false;
        }
    }

    // 持久化当前 provider 的设置（按 IsPassword 标出密钥交存储层加密），并把选中的 provider 记进 app Settings。
    // 走通用 ExtensionSettingsStore，按来源包分桶 packageId → "agent-model:<id>"，每 provider 各一份。
    void SaveSettings(string type)
    {
        var engine = AgentModelManager.GetInitedEngine(type);
        if (engine == null)
            return;

        var config = engine.GetPropertyConfig(new PropertyContext(mSettings.GetInfo()));
        var secrets = ExtensionSettingsStore.PasswordKeys(config);
        // 只存当前 provider schema 里的字段，避免把切换前其他 provider 残留在 mSettings 的键写进本 provider 桶。
        ExtensionSettingsStore.Save(AgentModelManager.GetPackageId(type), "agent-model:" + type, FilterToConfig(mSettings.GetInfo(), config), secrets);

        Settings.AgentModelProvider.Value = type;
        Settings.Save(PathManager.SettingsFilePath);
    }

    // all 中属于 config 声明字段的子集（按当前 provider schema 过滤）。
    static PropertyObject FilterToConfig(PropertyObject all, ObjectConfig config)
    {
        var map = new Map<string, PropertyValue>();
        foreach (var kv in config.Properties)
            if (all.Map.TryGetValue(kv.Key, out var v))
                map.Add(kv.Key, v);
        return new PropertyObject(map);
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
        "You can inspect and edit the current project by calling the provided tools. " +
        "Only call a tool when the user explicitly asks you to inspect or modify the project. " +
        "For greetings, small talk, or statements that are not requests, reply briefly in natural language and do not call any tool. " +
        "When a request does need project facts, call a tool rather than guessing. " +
        "Tool results that appear earlier in this conversation are snapshots of the project as it was at that moment and may now be stale — the user (or your own later edits) may have changed tracks, parts, notes, tempo or counts since. Before any operation that depends on current counts, indices or values (e.g. editing a note by its number, referring to \"track N\", or assuming how many parts exist), re-read the relevant state with the appropriate get tool first rather than trusting an older result. " +
        "Addressing is 1-based everywhere: track 1 is the first track, and part/note numbers are 1-based too. " +
        "Positions and durations are in ticks; call get_project_overview to learn the PPQ (ticks per quarter note) and the tempo/time signature. " +
        "For multi-step or fine-grained edits (writing a melody, editing many notes, drawing pitch/parameter curves), prefer the apply_edits tool so the whole batch is a single undoable change. " +
        "Before editing notes by number, read them with get_part_notes to get current NoteNumbers. " +
        "When the user refers to \"the current part\"/\"this part\" without numbers, call get_current_part to resolve its track/part numbers; when they refer to \"the playhead\"/\"here\"/\"the current position\", call get_playhead to get the tick. " +
        "CRITICAL: every tool argument must be a concrete literal value (a number or string). Never put placeholders, template expressions, code, or references to other tools inside arguments — for example do NOT write \"${get_current_part().trackNumber}\", \"get_part_notes(...)\", or any ${...} expression. There is no inline evaluation. Instead, first call the read tool, read the actual values from its result text, then call the next tool with those literal numbers. " +
        "To transpose notes (e.g. up an octave = +12 semitones) use transpose_notes (a part) or shift_track_pitch (a whole track) — do NOT use set_pitch_line, which only draws a pitch curve and does not move note pitches. " +
        "To add vibrato (颤音) use add_vibrato — do NOT set the VibratoEnvelope automation for this; VibratoEnvelope only scales the depth of an existing vibrato and produces nothing on its own. " +
        "Vibrato is overlaid additively on the pitch line and is independent of it: when drawing a pitch line and adding vibrato over the same span, draw ONE continuous pitch line over the whole span and add vibrato on top — never break or split the pitch line where the vibrato is. " +
        "All tick positions in every tool are ABSOLUTE (global) ticks — the same coordinate space as the playhead, get_project_overview and bar numbers. You never convert between coordinate systems and never subtract a part start. " +
        "The playhead is not grid-aligned; when writing a melody or placing notes on the beat, snap your target ticks with snap_tick first.";

    readonly Panel mRoot = new();
    readonly DockPanel mChatView = new() { LastChildFill = true };
    readonly DockPanel mSettingsView = new() { LastChildFill = true };
    // 可见会话的消息滚动区挂载点：切换会话只换其 Child 为目标会话各自的 ListView（离屏会话的视图被其 SessionContext 持有、不销毁）。
    readonly Panel mMessagesHost = new();
    readonly TextInput mInput = new();
    // token 用量状态行（输入框上方）：显示当前会话的累计 + 上下文占用，随会话切换/每轮刷新（见 RefreshTokenStatus）。
    readonly TextBlock mTokenStatus = new();
    // 图片附件：待发缩略图条 + 待发图片列表（属"当前撰写"状态、与输入框共享、跨会话切换保留）+ 📎按钮。
    readonly StackPanel mAttachmentStrip = new();
    readonly List<PendingImage> mPendingImages = new();
    Control? mImagePreview; // 当前打开的图片 lightbox 浮层（单实例守卫：再点图片先关旧的）
    TuneLab.GUI.Components.Button mAttachButton = null!;
    // 标题：复用轨道名同款 EditableLabel（双击就地改名、Enter/失焦提交），与全局改名交互一致。
    readonly EditableLabel mTitleLabel = new()
    {
        Text = "New Chat",
        FontSize = 12,
        CornerRadius = new(4),
        VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, // 占满中间列，长标题才会省略号化
        Foreground = Style.LIGHT_WHITE.ToBrush(),
        Background = Brushes.Transparent,
        InputBackground = Style.BACK.ToBrush(),
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    readonly PropertyObjectController mProviderController = new(); // provider 选择（单项 combo），复用属性面板样式
    readonly PropertyObjectController mPropertiesController = new();
    readonly Label mStatusLabel = new() { FontSize = 11, Margin = new(24, 0, 24, 12), Foreground = Colors.IndianRed.ToBrush() };
    TuneLab.GUI.Components.Button mSendButton = null!;
    TuneLab.GUI.Components.Button mStopButton = null!;
    TuneLab.GUI.Components.Button mSubmitButton = null!;
    TuneLab.GUI.Components.Button? mMenuButton;
    Flyout mMenuFlyout = null!;
    bool mMenuJustClosed;
    double mBubbleMaxWidth = 230; // 用户气泡/系统提示最大宽度，随对话区宽度自适应（见 BuildChatView 的 Bounds 订阅）
    double mContentMaxWidth = 246; // 助手去气泡容器的近整宽，随对话区宽度自适应

    readonly DataDocument mSettingsDocument = new();
    readonly DataPropertyObject mSettings;
    // provider 选择单独挂一个数据对象（不污染 mSettings 的持久化）；engine 值存于 EngineKey 字段。
    readonly DataDocument mProviderDocument = new();
    readonly DataPropertyObject mProviderData;
    IReadOnlyList<ComboBoxOption> mEngineOptions = [];
    const string EngineKey = "provider";
    IAgentProjectEditor? mProjectEditor;
    Func<IMidiPart?>? mCurrentPartProvider;
    Func<IQuantization?>? mQuantizationProvider;
    IReadOnlyList<IAgentTool> mTools = [];
    IAgentModelSession? mSession;

    // ───────────────── 多会话并行 ─────────────────

    // 每会话各自的管线 + 视图状态。切换会话不取消、不清空——只换 mMessagesHost 显示的视图；
    // 离屏会话的 runner/请求继续在后台跑，流式事件仍写进其（脱离视觉树但被本对象持有的）ListView，切回即见进度。
    sealed class SessionContext
    {
        public readonly ListView View;          // 该会话独立的消息滚动区（离屏时仍保留，承载进行中的占位/分步气泡）
        public ChatSession? Session;             // 落盘模型（null=尚未落盘的新对话，首轮成功后建立）
        public AgentRunner? Runner;              // 该会话的对话主循环（持有累积的对话历史）
        public CancellationTokenSource? Cts;     // 该会话当前在飞请求的取消源（停止键 / 删除该会话时触发）
        public List<AgentMessage>? SeedHistory;  // 加载已存会话 / 中途换模型后用于重建 runner 的历史（仅对话文本）
        public bool Busy;                        // 该会话是否有在飞请求（决定切到它时显示发送键还是停止键）
        public long CumulativeTokens;            // 会话累计 token（每轮 total 之和，含工具往返重复前缀；状态行用）
        public int ContextTokens;                // 当前上下文占用（最后一次模型调用的输入+输出 ≈ 当前上下文大小；状态行用）
        public string Title = "New Chat";        // 该会话标题（切到它时写入头部标签）
        public bool TitleManual;                 // 标题是否被用户手动改过：true 则不再被自动标题覆盖
        public long CreatedAtUnix;               // 会话建立时刻（本地新建=当时；加载已存=其原始创建时刻）。菜单按它降序排，位置稳定、新会话在顶
        public SessionContext(ListView view) => View = view;
    }

    readonly List<SessionContext> mContexts = new(); // 所有打开中的会话（含后台在跑的、未落盘的新对话）
    SessionContext mActive = null!;                  // 当前可见会话（构造期立即建立首个空白会话）
}
