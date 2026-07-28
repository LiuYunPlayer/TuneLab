using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
using TuneLab.Scripting;
using TuneLab.SDK;
using TuneLab.Utils;
using ScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility;
using PlacementMode = Avalonia.Controls.PlacementMode;
using ComboBoxItem = TuneLab.SDK.ComboBoxItem;   // 消歧：避开 Avalonia.Controls.ComboBoxItem

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
        mEngineOptions = engines.Select(e => new ComboBoxItem(PropertyValue.Create(e), AgentModelManager.GetDisplayName(e))).ToList();
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

        // agent 写授权胶囊在对话页 header（见 BuildChatView），即时生效、不经"确认"（授权本就是即时偏好）。
        // 订阅 Settings 本体 → 无论改动来自胶囊还是升级卡片（Confirm 里的"始终允许"）胶囊都实时同步。
        Settings.AgentAuthorization.Modified.Subscribe(RefreshAuthPill);
        RefreshAuthPill();
        // 设置面板 dirty 追踪：字段编辑 / provider 切换即置脏、标题显 *；× 时据此弹「应用/忽略」。
        // 订阅置于 ctor 初始 provider 载入之后，故初始化不误标脏（且每次进面板还会重置）。
        mSettings.Modified.Subscribe(RecomputeSettingsDirty);
        mProviderData.Modified.Subscribe(RecomputeSettingsDirty);

        // 之前 Submit 过（app Settings 记了 provider）才打开即静默自动接入，直接可聊天；否则首次发送再引导去设置。
        if (hadSaved && TryConnect(savedEngine, out _))
            AppendMessage(mActive, "system", ConnectedNotice()); // 启动即提示连到哪个模型

    }

    // 载入某 provider 已落盘的设置（含解密密钥）进 mSettings。各 provider 各记一份——沿用通用
    // ExtensionSettingsStore 的两级键 packageId → "agent-model:<id>"（适配器全是内建，故外层恒是内建包桶）。
    void LoadProviderSettings(string type)
    {
        var engine = AgentModelManager.GetInitedEngine(type);
        if (engine == null)
            return;
        var values = ExtensionSettingsStore.Load(AgentModelManager.GetActivePackageId(type), "agent-model:" + type, s => engine.GetPropertyConfig(new PropertyContext(s)));
        foreach (var kv in values)
            mSettings.SetValue(kv.Key, kv.Value);
        mSettings.Commit();
    }

    // 工程切换时由 Editor 调用（OnProjectChanged）：只把工具重绑到新工程；对话历史是会话的属性、与工程正交，不清空。
    public void SetProject(IProject? project)
    {
        mProject = project;
        // 单一动作面（CodeAct）：编辑工程一律走 run_script（对象式 `tl` API），读取只留一个定向总览，其余读取也走脚本。
        // 另有脚本库管理工具，让 agent 把功能沉淀成可注册进菜单的复用工具，并能读参数/代跑已存脚本（闭环）。
        if (project != null)
        {
            Func<string?> lang = () => TranslationManager.CurrentLanguage.Value;
            // run_script 与 run_saved_script 共用同一写执行器（授权闸门 / 预览 / 收口）——单一动作面 SSOT。
            var writeExecutor = new ScriptWriteExecutor(project, mCurrentPartProvider, mQuantizationProvider, lang, mSelectionProvider, mPianoSelectionProvider, RequestScriptAuthorizationAsync);
            mTools = new List<IAgentTool>
            {
                // 操作工程：定向（看） + 脚本（改/算/细读）
                new GetProjectOverviewTool(project),
                new RunScriptTool(writeExecutor),
                new GetScriptApiTool(),
                // 导出 = importTracks 的对偶。不改工程状态（故不进 tl 面，同 save/delete_script 循例），但写用户磁盘上
                // 任意路径 → 恒过授权闸门。只做工程/MIDI 等格式文件，音频导出是用户的人在环决定、不给 agent。
                new ExportProjectTool(project, RequestScriptAuthorizationAsync),
                // 脚本库管理：把用户想要的功能写成工具脚本存库 → 自动进菜单复用；读参数 / 代跑（闭环）。
                // save(覆盖已存)/delete 是外部文件的破坏性改动 → 过授权闸门（RequestScriptAuthorizationAsync，同工程写）。
                new SaveScriptTool(project, mCurrentPartProvider, mQuantizationProvider, lang, RequestScriptAuthorizationAsync),
                new ListScriptsTool(project, mCurrentPartProvider, mQuantizationProvider, lang),
                new ReadScriptTool(),
                new DeleteScriptTool(RequestScriptAuthorizationAsync),
                new GetScriptInputsTool(project, mCurrentPartProvider, mQuantizationProvider, lang, mSelectionProvider, mPianoSelectionProvider),
                new RunSavedScriptTool(writeExecutor, project, mCurrentPartProvider, mQuantizationProvider, lang, mSelectionProvider, mPianoSelectionProvider),
                // 环境感知（只读）：枚举插件/readme、音源目录、effect 引擎+参数——让 agent 看见宿主装了什么、可推荐什么。
                new ListExtensionsTool(),
                new GetExtensionIntroductionTool(),
                new ListSoundSourcesTool(),
                new ListEffectsTool(),
                // 设置助手（诉求 2）：只读枚举（含"在哪一页哪一行"，可教用户自己改） + 按键改一项（过授权闸门，
                // 与工程写/脚本文件同一档位；改宿主设置不是工程数据、历史记录救不回）。
                new ListSettingsTool(),
                new SetSettingTool(RequestScriptAuthorizationAsync),
                // 快捷键（D 支柱）：查/改绑/冲突。改绑同样过闸门；与 save_script 合起来闭环"写个功能 + 绑个键"。
                new ListKeybindingsTool(),
                new SetKeybindingTool(RequestScriptAuthorizationAsync),
                // 扩展路由：主要用于排障（「我的插件怎么不生效」→ 其实是身份被别的包顶替了），改选同样过闸门。
                new ListExtensionRoutingTool(),
                new SetExtensionRoutingTool(RequestScriptAuthorizationAsync),
                // 扩展启停：把某个包（或包内某个能力）关掉但不卸载。与路由是两根轴——路由在多个实现里挑一个，
                // 启停决定某份实现要不要参与加载（对没有竞争者的独苗同样适用）。读面在 list_extensions。
                new SetExtensionEnabledTool(RequestScriptAuthorizationAsync),
                // 扩展自己的设置（设置窗「扩展」页）：读 schema+当前值 / 改一格。密钥字段只报有无、禁读禁写。
                new ListExtensionSettingsTool(),
                new SetExtensionSettingTool(RequestScriptAuthorizationAsync),
                // 探测沙箱（F 支柱）：可丢弃无头工程里造场景 + 真触发合成 + 读回显，够到静态读够不着的东西
                // （尤其真实音素）。写入不碰用户数据、不需授权（工程跑完即弃）。
                new RunInSandboxTool(),
                // 问用户：在【本轮之内】等到答案再继续，免得把任务切成两轮、丢掉已有进展。不改工程状态、
                // 纯为 agent 自身决策服务，故归工具面（也因为等卡片必须 async，脚本同步跑在 UI 线程会自死锁）。
                new AskUserQuestionTool(RequestUserAnswerAsync),
            };
        }
        else
        {
            mTools = [];
        }
        // 工具随新工程重建；但对话历史不清——与 TryConnect(换模型)/重启(RestoreSession)一致：从已记录会话重建完整续聊
        // 历史（ReconstructHistory 含工具调用/结果），下次发送时新 runner 带它 + 新工具重建。空会话(无 Session)本无历史可留。
        foreach (var c in mContexts)
        {
            if (c.Session != null)
                c.SeedHistory = ReconstructHistory(c.Session);
            c.Runner = null;
        }
    }

    // 由 Editor 注入一次：实时读取钢琴窗当前编辑的 midi part / 当前量化（用户切 part / 改量化即变，故存访问器而非快照）。
    public void SetCurrentPartProvider(Func<IMidiPart?> provider) => mCurrentPartProvider = provider;
    public void SetQuantizationProvider(Func<IQuantization?> provider) => mQuantizationProvider = provider;
    public void SetSelectionProvider(Func<ScriptSelection?> provider) => mSelectionProvider = provider;
    public void SetPianoSelectionProvider(Func<ScriptPianoSelection?> provider) => mPianoSelectionProvider = provider;

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
                // 无底色，反馈全落图标。三态刻意留出区分：收起 0.5 → hover/press 0.8 → 展开纯白。
                // hover 不给纯白（虽与 ⚙ 的 0.6→白 不完全一致）——本按钮多一个「展开」状态维度，
                // 若 hover 也是纯白，悬浮时看起来就和已展开一样、无从分辨。展开态已是纯白，故不再给 hover 色。
                // press 与 hover 同值：不设 PressedColor 会在按下时回退到 Color(0.5)，于是点击途中
                // hover(0.8) → 按下变暗(0.5) → 松开转纯白，中间那下变暗是突兀的闪烁。
                CheckedColorSet = new() { Color = Colors.White },
                UncheckedColorSet = new()
                {
                    Color = Style.LIGHT_WHITE.Opacity(0.5),
                    HoveredColor = Style.LIGHT_WHITE.Opacity(0.8),
                    PressedColor = Style.LIGHT_WHITE.Opacity(0.8),
                },
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

        // agent 写授权胶囊：显示当前档位短名 + ▾，点开三选一、即时生效。放 ⚙ 左侧（右侧后加的 dock item 更靠左）。
        var caret = new TextBlock() { Text = "▾", FontSize = 9, Margin = new(4, 0, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush() };
        var pillInner = new StackPanel() { Orientation = Orientation.Horizontal };
        pillInner.Children.Add(mAuthLabel);
        pillInner.Children.Add(caret);
        mAuthButton = new Border()
        {
            CornerRadius = new(10),
            Padding = new(8, 2),
            Margin = new(0, 0, 2, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Background = Style.INTERFACE.ToBrush(),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = pillInner,
        };
        mAuthButton.PointerEntered += (_, _) => mAuthButton.Background = Style.LIGHT_WHITE.Opacity(0.12).ToBrush();
        mAuthButton.PointerExited += (_, _) => mAuthButton.Background = Style.INTERFACE.ToBrush();
        mAuthButton.PointerPressed += (_, e) => { e.Handled = true; OpenAuthMenu(); };
        DockPanel.SetDock(mAuthButton, Dock.Right);
        header.Children.Add(mAuthButton);
        mAuthFlyout = new Flyout() { Placement = PlacementMode.BottomEdgeAlignedRight };
        mAuthFlyout.FlyoutPresenterClasses.Add("agent-menu");
        RefreshAuthPill();

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
        // MultilineTextInput 基于 AvaloniaEdit，自动换行/多行为内建；AutoGrow 让框高紧贴内容(内容+对称内边距)、封顶 MaxHeight 后内滚。
        mInput.MaxHeight = 120;
        // 对称内边距(6/6) → 行盒在框内居中；VerticalAlignment=Center 让这个紧贴内容的框在输入行里整体居中
        //（否则 DockPanel 会把它拉伸到行高、内容顶对齐而显偏上）。
        mInput.Padding = new(0, 6, ScrollBar.ReservedThickness, 6);
        mInput.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        mInput.AutoGrow = true;
        // 隐藏竖滚动条：内容仍可滚轮/光标跟随滚动；超 MaxHeight 后靠此内滚，不显条更干净。
        mInput.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
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

        // 轮边界插话「待发缓冲」chip（钉在输入框上方、不随消息滚动）：生成中按发送即入此缓冲，runner 到边界吃掉。
        // 左侧 ↪ 标记 + 文本预览（最多两行省略号）；右侧 ✎ 召回到输入框编辑、✕ 丢弃。仅当本会话有 pending 时可见。
        var pendingMark = new TextBlock { Text = "↪", FontSize = 12, Foreground = Style.BUTTON_PRIMARY.ToBrush(), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new(0, 0, 6, 0) };
        mPendingPreview.FontSize = 11;
        mPendingPreview.Foreground = Style.LIGHT_WHITE.Opacity(0.8).ToBrush();
        mPendingPreview.TextWrapping = TextWrapping.Wrap;
        mPendingPreview.MaxLines = 2;
        mPendingPreview.TextTrimming = TextTrimming.CharacterEllipsis;
        mPendingPreview.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        var pendingEdit = new TextBlock { Text = "✎", FontSize = 13, Cursor = new Cursor(StandardCursorType.Hand), Foreground = Style.LIGHT_WHITE.Opacity(0.6).ToBrush(), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new(8, 0, 0, 0) };
        ToolTip.SetTip(pendingEdit, "Edit".Tr(this));
        pendingEdit.PointerPressed += (_, e) => { e.Handled = true; RecallPending(); };
        var pendingDiscard = new TextBlock { Text = "✕", FontSize = 12, Cursor = new Cursor(StandardCursorType.Hand), Foreground = Style.LIGHT_WHITE.Opacity(0.6).ToBrush(), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new(8, 0, 2, 0) };
        ToolTip.SetTip(pendingDiscard, "Discard".Tr(this));
        pendingDiscard.PointerPressed += (_, e) => { e.Handled = true; DiscardPending(); };
        var pendingInner = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(pendingMark, Dock.Left);
        DockPanel.SetDock(pendingDiscard, Dock.Right);
        DockPanel.SetDock(pendingEdit, Dock.Right);
        pendingInner.Children.Add(pendingMark);
        pendingInner.Children.Add(pendingDiscard);
        pendingInner.Children.Add(pendingEdit);
        pendingInner.Children.Add(mPendingPreview); // 填充
        mPendingChip.IsVisible = false;
        mPendingChip.Background = Style.INTERFACE.ToBrush();
        mPendingChip.BorderBrush = Style.BUTTON_PRIMARY.Opacity(0.6).ToBrush();
        mPendingChip.BorderThickness = new(1);
        mPendingChip.CornerRadius = new(6);
        mPendingChip.Padding = new(8, 5);
        mPendingChip.Margin = new(2, 4, 2, 2);
        mPendingChip.Child = pendingInner;

        var inputColumn = new StackPanel { Orientation = Orientation.Vertical, Children = { mPendingChip, mAttachmentStrip, inputRow } };

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
    // 从本轮轨迹算出（模型调用次数、末次调用上下文≈输入+输出）：脚注据此消歧（多次调用标注 "· N calls"），
    // 末次上下文与状态行 Context 同口径，用于 tooltip 桥接。
    static (int calls, int lastContext) TurnBreakdown(IReadOnlyList<AgentTurnMessage> trajectory)
    {
        int calls = 0, lastContext = 0;
        foreach (var m in trajectory)
            if (m.Role == AgentRole.Assistant && m.Usage != null)
            {
                calls++;
                lastContext = m.Usage.PromptTokens + m.Usage.CompletionTokens;
            }
        return (calls, lastContext);
    }

    // 生成过程中每次模型调用返回即累加该会话 token 口径并实时刷新状态行（不必等整轮结束）：
    // 累计 Session += 本次 total；当前上下文 Context = 本次输入+输出（末次调用即当前上下文大小）。
    void AccumulateRoundTokens(SessionContext ctx, AgentRoundUsage g)
    {
        ctx.CumulativeTokens += g.TotalTokens;
        ctx.ContextTokens = g.PromptTokens + g.CompletionTokens;
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

    // ListView 用无限宽测量子项，子项必须靠显式宽度才会换行。助手容器（去气泡）用【定宽】撑满内容列（文字换行 + 复制右对齐都锚
    // 这条随侧栏走的右边缘）；用户气泡/系统提示用 MaxWidth 按内容收缩、留对侧空白。随对话区 resize 更新（见 Bounds 订阅）。
    void ApplyBubbleWidths(ListView list)
    {
        foreach (var c in list.Content.Children)
        {
            if ((c.Tag as string) == "assistant")
                c.Width = mContentMaxWidth;
            else
                c.MaxWidth = mBubbleMaxWidth;
        }
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
                () => { mMenuFlyout.Hide(); _ = ConfirmAndDeleteContext(captured); }));
        }

        // 仅存在磁盘、当前未打开的会话（已打开的以活 context 为准、不重复列）。点击从盘加载。
        var openIds = new HashSet<string>(mContexts.Where(c => c.Session != null).Select(c => c.Session!.Id));
        foreach (var session in AgentSessionStore.List())
        {
            if (openIds.Contains(session.Id))
                continue;
            var captured = session;
            var titleText = string.IsNullOrWhiteSpace(session.Title) ? "Untitled".Tr(this) : session.Title;
            var capturedTitle = titleText;
            entries.Add(new MenuEntry(session.CreatedAtUnix, titleText, false,
                () => LoadSession(captured),
                () => { mMenuFlyout.Hide(); _ = ConfirmAndDeleteSession(captured, capturedTitle); }));
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
        {
            AgentSessionStore.Delete(ctx.Session.Id);
            // 断掉这个 ctx 的落盘能力。Cancel 只发信号、不等待：在飞那一轮的收尾代码（catch 里的 MarkTurnOutcome，
            // 以及生成期间每次工具完成就跑的 FlushTrajectory）稍后才执行，那时若 Session 还挂着，就会把刚删掉的
            // 文件重新写回磁盘——会话"复活"。各落盘路径都有 Session == null 的守卫，置空即一并生效。
            ctx.Session = null;
        }
        if (ctx == mActive)
            NewChat();
    }

    // ✕ 删除走二次确认（与 Script/Properties 侧栏一致；删会话会丢整段对话、且可能取消正在跑的请求，更需谨慎）。
    async Task ConfirmAndDeleteContext(SessionContext ctx)
    {
        if (await ConfirmDeleteSession(ctx.Title))
            DeleteContext(ctx);
    }

    async Task ConfirmAndDeleteSession(ChatSession session, string title)
    {
        if (await ConfirmDeleteSession(title))
            AgentSessionStore.Delete(session.Id);
    }

    async Task<bool> ConfirmDeleteSession(string title)
    {
        var dialog = new TuneLab.GUI.Dialog();
        dialog.SetTitle("Tips".Tr(this));
        dialog.SetMessage(string.Format("Delete session \"{0}\"?".Tr(this), title));
        bool confirmed = false;
        dialog.AddButton("Cancel".Tr(this), TuneLab.GUI.Dialog.ButtonType.Normal);
        var ok = dialog.AddButton("Delete".Tr(this), TuneLab.GUI.Dialog.ButtonType.Primary);
        ok.Pressed += () => confirmed = true;
        dialog.Topmost = true;
        await dialog.ShowDialog(mRoot.Window());
        return confirmed;
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
            // 仅拦掉整行点击；删除放到 PointerReleased——按下时指针仍被 Flyout 捕获、鼠标未松开，此刻开模态确认窗会被系统
            // "点击激活"吞掉首次悬浮/点击（需点两次）。等松开后再触发。
            del.PointerPressed += (_, e) => e.Handled = true;
            del.PointerReleased += (_, e) => { e.Handled = true; onDelete(); };
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
        // 自动跟随的开关由「用户是否主动移动过视野」决定（见 OnMessagesAxisChanged），不靠每次流式增量去量位置——
        // 量位置那套会被布局时序（内容刚长高、轴还没更新）骗过去，一旦误判就永久停止跟随。
        ctx.View.VerticalAxis.AxisChanged += () => OnMessagesAxisChanged(ctx);
        mContexts.Add(ctx);
        return ctx;
    }

    // 切到某会话：仅换可见视图 + 头部标题 + 发送/停止键状态——不取消、不清空任何会话的在飞管线。
    void SwitchTo(SessionContext ctx)
    {
        mActive = ctx;
        mMessagesHost.Children.Clear();
        mMessagesHost.Children.Add(ctx.View);
        mMessagesHost.Children.Add(ctx.Scrollbar);   // 覆盖在消息区之上，只手柄可点、不抢气泡点击
        ApplyBubbleWidths(ctx.View); // 离屏期间侧栏可能被拖宽，切回时按当前宽度重排该会话气泡
        SetTitle(ctx.Title);
        RefreshSendControls();
        RefreshPendingChip(); // chip 跟随可见会话切换（pending 是 per-session 的）
        RefreshTokenStatus();
        ScrollToEnd(ctx, force: true);   // 切到某会话：无条件展示其最新内容
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
        // 一轮的分界只认"常规"用户消息（role==user 且非插话）；插话用户(Interjected)留在当前轮组内、由 BuildReplayedTurn 行内重放。
        static bool IsTurnStart(ChatTurnMessage m) => m.Role == "user" && !m.Interjected;

        int i = 0;
        while (i < msgs.Count)
        {
            ChatTurnMessage? turnStart = null;
            if (IsTurnStart(msgs[i]))
            {
                turnStart = msgs[i];
                AppendUserMessage(ctx, turnStart.Text, LoadAttachmentBytes(session.Id, turnStart));
                i++;
            }
            // 收集到下条"常规"user 之前的助手/工具/插话消息，重放成一条分步视图。容错：轨迹首条若非 user（异常文件），落单消息也独立成组。
            int start = i;
            while (i < msgs.Count && !IsTurnStart(msgs[i]))
                i++;
            bool failed = turnStart != null && !string.IsNullOrEmpty(turnStart.Outcome);
            // 中断态：锚是常规用户消息、Outcome 空着（没有任何收尾代码写过它）、且这一轮没有正常收尾。
            // 它是【推断】出来的而非记录下来的——进程若被强行结束，没有一行代码来得及跑，
            // 为它加个 Outcome 常量也永远不会有人去写。
            bool interrupted = turnStart != null && !failed && !IsTurnComplete(msgs, start, i);
            if (i > start)
                // 仍处于失败态的轮：末尾那条带内错误记录由下面的 BuildErrorEntry 渲染（带重试按钮），
                // 组内不再重复渲染它；已被重试掉的历史错误则照位置行内呈现（留痕）。
                ctx.View.Content.Children.Add(BuildReplayedTurn(msgs, start, i, aborted: failed || interrupted, skipTrailingError: failed));
            // 收场态：出错轮 → 可重试/复制的错误条目（重试仅给最后一轮，其上下文才对得上）；取消轮 → 灰字"已停止"。
            if (failed && turnStart!.Outcome == ChatTurnMessage.OutcomeError)
                ctx.View.Content.Children.Add(BuildErrorEntry(ctx, turnStart, turnStart.ErrorText, allowRetry: i >= msgs.Count));
            else if (failed)
                ctx.View.Content.Children.Add(AssistantContainer(OutcomeNotice(turnStart!)));
            else if (interrupted)
                // [继续] 只给末轮，与 BuildErrorEntry 的 allowRetry 同一口径：RetryAsync 是对【当前上下文】续跑，
                // 非末轮点它就不是"接上那一轮"，结果会落到底部、归属错位。
                ctx.View.Content.Children.Add(BuildInterruptedEntry(ctx, turnStart!, allowContinue: i >= msgs.Count));
        }
    }

    // 该轮是否【正常收尾】。判据 = 最后一条是"不带工具调用的 assistant 回复"——runner 正是在模型给出无工具的
    // 答复时才返回，那条就是本轮的终点。中断（进程消失）则会停在别处：带 tool_calls 的 assistant（工具还没跑完
    // 或结果没来得及落）、一条 tool 结果（下一次模型调用没回来）、或者区间空空如也（首次模型调用就没回来）。
    //
    // 不能改用"区间里有没有 assistant"来判：轨迹现在是边跑边落的（见 FlushTrajectory），中断轮往往已经落下
    // 一大串 assistant/tool，那种判法会把它们全当成正常轮。
    static bool IsTurnComplete(List<ChatTurnMessage> msgs, int start, int end)
    {
        for (int j = end - 1; j >= start; j--)
        {
            var m = msgs[j];
            if (m.Role == ChatTurnMessage.RoleError || m.Role == ChatTurnMessage.RoleNotice)
                continue;   // 带内错误痕迹 / 护栏提示都是宿主写的、不是模型说的话，不参与收尾判定
                            // （护栏提示恰好落在撞上限那轮的末尾，漏掉这一支会把那轮误判成中断）

            // Stopped 的半截回复不算收尾（它恰恰是"没说完"的那条）。当前它只会与 Outcome 一同出现、走不到这里，
            // 但判据该自洽：别让"没说完"被读成"说完了"。
            return m.Role == "assistant" && !m.Stopped && (m.ToolCalls == null || m.ToolCalls.Count == 0);
        }
        return false;
    }

    // 取消/出错轮的末尾状态行：取消渲灰字"已停止"，出错渲红字"Error: 原因"（与实时 catch 路径同措辞）。
    Control OutcomeNotice(ChatTurnMessage turnStart)
        => turnStart.Outcome == ChatTurnMessage.OutcomeError
            ? NoticeLine("Error: " + (turnStart.ErrorText ?? string.Empty), Colors.IndianRed.ToBrush())
            : NoticeLine("Stopped".Tr(this), Style.LIGHT_WHITE.Opacity(0.5).ToBrush());

    // 把 [start, end) 区间的助手/工具记录重放进一个 AgentTurnView，重建分步视图（与实时同路径），包进助手容器返回。
    // 重放顺序即存储顺序：每条 assistant 先思考、再正文、再它的工具调用(started)；随后的 tool 记录给出对应结果(finished)。
    Control BuildReplayedTurn(List<ChatTurnMessage> msgs, int start, int end, bool aborted = false, bool skipTrailingError = false)
    {
        // 仍失败的轮：末尾那条错误记录归 BuildErrorEntry（带重试），此处跳过以免重复。
        if (skipTrailingError && end - 1 >= start && msgs[end - 1].Role == ChatTurnMessage.RoleError)
            end--;
        var turn = new AgentTurnView();
        var narration = new List<string>();
        int prompt = 0, completion = 0, total = 0;
        bool hasUsage = false;
        int calls = 0, lastContext = 0;   // 脚注消歧：模型调用次数 + 末次上下文（与状态行 Context 同口径）
        // 预扫一遍备好 id→结果：问用户那次调用除了照常重放工具块，还要在其后补一个只读问答块
        //（问题+勾选+补充），与实时"工具块 + 卡片"的呈现对齐。工具块本身照旧——它保留参数/结果原文，
        // 排查时有用；问答块负责把内容读得懂。遍历到 assistant 的 tool_call 时结果还没扫到，故先备表。
        var toolResults = new Dictionary<string, string>();
        for (int k = start; k < end; k++)
        {
            var m = msgs[k];
            if (m.Role == "tool" && !string.IsNullOrEmpty(m.ToolCallId))
                toolResults[m.ToolCallId!] = m.Text;
        }
        for (int k = start; k < end; k++)
        {
            var m = msgs[k];
            if (m.Role == "tool")
            {
                turn.Apply(new AgentToolFinished(m.ToolCallId ?? string.Empty, string.Empty, m.Text, m.IsError));
                continue;
            }
            if (m.Role == "user") // 组内 user = 轮边界插话：行内重放成用户小气泡（与实时同路径）
            {
                turn.Apply(new AgentUserInterjection(m.Text));
                continue;
            }
            if (m.Role == ChatTurnMessage.RoleError) // 带内错误痕迹（已被重试掉的那次失败）：原位留痕、暗色不抢戏
            {
                turn.SealText();   // 先把当前文本段定稿，错误行才落在它之后（保持真实先后）
                turn.Append(RetiredErrorLine(m.Text));
                continue;
            }
            if (m.Role == ChatTurnMessage.RoleNotice) // 宿主护栏提示（如撞失控防护上限）：中性灰字，与实时同措辞
            {
                turn.SealText();
                turn.Append(NoticeLine(m.Text, Style.LIGHT_WHITE.Opacity(0.5).ToBrush()));
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
                {
                    turn.Apply(new AgentToolStarted(call.Id, call.Name, call.ArgumentsJson));
                    // 问用户：工具块之后【再】补一个只读问答块，与实时"工具块 + 卡片"的呈现对齐。
                    // 无配对结果 = 问了没等到回答，块内标"未回答"。
                    if (call.Name == AskUserQuestionTool.ToolName)
                        turn.Append(BuildRecordedQuestionBlock(call.ArgumentsJson,
                            call.Id != null && toolResults.TryGetValue(call.Id, out var r) ? r : null));
                }
            if (m.TotalTokens.HasValue)
            {
                hasUsage = true;
                prompt += m.PromptTokens ?? 0;
                completion += m.CompletionTokens ?? 0;
                total += m.TotalTokens.Value;
                calls++;
                lastContext = (m.PromptTokens ?? 0) + (m.CompletionTokens ?? 0);
            }
        }
        turn.Seal();
        if (aborted)
            turn.MarkPendingAborted(); // 失败/取消轮：未完成的工具步标为中止（与实时 catch 一致）
        turn.EndThinking(); // 重载即已完成，移除"生成中"指示
        if (turn.IsEmpty)
            return AssistantContainer(BubbleText("(no text reply)", Colors.White.ToBrush()));
        var usage = hasUsage ? new AgentTokenUsage { PromptTokens = prompt, CompletionTokens = completion, TotalTokens = total } : null;
        // 失败/取消轮不显脚注——与实时一致（实时 catch 不 BuildFooter，只接结局行）。
        if (!aborted)
            turn.Append(BuildFooter(usage, calls, lastContext));
        return AssistantContainer(turn.Root);
    }

    // 从会话消息重建 runner 续聊历史，带回完整工具往返（assistant 的工具调用 + tool 结果消息），使「重载 == 实时」
    // ——模型续聊时带上之前调了哪些工具、得到什么结果的上下文，不再失忆。思考(reasoning)不回发（它是输出而非输入）。
    // 旧版纯文本文件无 tool 记录、assistant 也无 ToolCalls，本映射自然降级为纯 user/assistant 文本。
    // 供加载会话、以及聊天中途换模型重连时复用——后者据此让新模型带上完整当前上下文。
    //
    // 失败/取消轮的用户消息与已完成轮都保留进上下文（与实时 mMessages 一致，失败单位是"一次模型回复"而非整条用户消息，
    // 故不再整轮跳过）——只滤掉"悬空 tool_call"：发起了调用却无配对结果的（取消卡在工具中途会产生），避免端点拒未配对调用。
    static List<AgentMessage> ReconstructHistory(ChatSession session)
    {
        var msgs = session.Messages;
        var resultIds = new HashSet<string>();
        foreach (var m in msgs)
            if (m.Role == "tool" && !string.IsNullOrEmpty(m.ToolCallId))
                resultIds.Add(m.ToolCallId!);

        var history = new List<AgentMessage>();
        foreach (var m in msgs)
        {
            switch (m.Role)
            {
                case "assistant":
                    // 被中止的半截回复只给用户看，不喂回模型：那次回复已作废（用户点停 / 技术失败），
                    // 把半截话塞回上下文会让模型当成自己的既有表态。它在 UI 重放里照常显示（见 BuildReplayedTurn）。
                    if (m.Stopped)
                        continue;

                    var calls = m.ToolCalls;
                    if (string.IsNullOrEmpty(m.Text) && (calls == null || calls.Count == 0))
                        continue;
                    history.Add(new AgentMessage
                    {
                        Role = AgentRole.Assistant,
                        Content = string.IsNullOrEmpty(m.Text) ? null : m.Text,
                        ToolCalls = calls is { Count: > 0 }
                            ? calls.Select(c => new AgentToolCall { Id = c.Id, Name = c.Name, ArgumentsJson = c.ArgumentsJson }).ToList()
                            : null,
                    });
                    // 悬空的调用（发起了却没有配对结果）补一条【结果未知】的合成结果，文案与实时路径共用同一常量
                    // （见 AgentRunner.CloseDanglingToolCalls）——两条路径给模型的话一字不差，续跑行为才不会分家。
                    if (calls is { Count: > 0 })
                        foreach (var c in calls)
                            if (!resultIds.Contains(c.Id))
                                history.Add(new AgentMessage
                                {
                                    Role = AgentRole.Tool,
                                    ToolCallId = c.Id,
                                    Content = AgentRunner.DanglingToolResult,
                                });
                    break;
                case "tool":
                    history.Add(new AgentMessage { Role = AgentRole.Tool, ToolCallId = m.ToolCallId, Content = m.Text });
                    break;
                case ChatTurnMessage.RoleError:
                case ChatTurnMessage.RoleNotice:
                    continue;   // 带内错误痕迹 / 宿主护栏提示都只给用户看（非模型说过的话），绝不喂回模型
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

    // persist-on-send：用户消息在【发送时】即建会话并落盘（不等整轮回复）——支撑并行会话（发 A→切 B→回 A）与崩溃韧性；
    // "首次发送才建会话"也天然跳过从未用过的空 New Chat。返回本轮"锚"= 该用户消息，供轮终态回写 Outcome。
    // ctx 是发起本轮时捕获的会话——即便用户中途切走也只写它、不串会话。
    ChatTurnMessage BeginTurn(SessionContext ctx, string userText, IReadOnlyList<ChatAttachment>? userAttachments)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool isNew = ctx.Session == null;
        ctx.Session ??= new ChatSession { CreatedAtUnix = ctx.CreatedAtUnix }; // 沿用上下文建立时刻，落盘前后排序位置一致
        var session = ctx.Session;
        session.SchemaVersion = 1;
        // 用户消息（带附件则附 ChatAttachment，含原始字节 → Save 落 blob、清单只引用）即为本轮锚。
        var anchor = new ChatTurnMessage { Role = "user", Text = userText, Attachments = userAttachments is { Count: > 0 } ? userAttachments.ToList() : null };
        session.Messages.Add(anchor);
        session.UpdatedAtUnix = now;
        if (isNew)
        {
            // 首轮占位标题即时可读（手动名优先，否则首条截断）；LLM 自动标题推迟到本轮成功完成再覆盖。
            session.Title = ctx.TitleManual && !string.IsNullOrWhiteSpace(ctx.Title) ? ctx.Title : Truncate(userText, 30);
            ctx.Title = session.Title;
            if (ctx == mActive)
                SetTitle(session.Title);
        }
        AgentSessionStore.Save(session);
        return anchor;
    }

    // 轨迹增量落盘：把本轮轨迹里【水位之后】的记录补进会话并存盘，然后推进水位。
    // 由 runner 的 TrajectoryAppended 同步回调驱动（每追加一条即落一次），使进程在下一步消失时，已发生的调用不随内存一起没掉。
    // 幂等：无新增即不写盘。轮终态的三条路径也走它，故"边跑边落"与"轮末补齐"共用同一段逻辑、天然不重复。
    void FlushTrajectory(SessionContext ctx, IReadOnlyList<AgentTurnMessage> trajectory)
    {
        var session = ctx.Session;
        if (session == null)
            return; // 理论不达：BeginTurn 已建会话

        if (ctx.PersistedTurnMessages >= trajectory.Count)
            return;

        for (int i = ctx.PersistedTurnMessages; i < trajectory.Count; i++)
            session.Messages.Add(ToStored(trajectory[i]));
        ctx.PersistedTurnMessages = trajectory.Count;
        session.UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        AgentSessionStore.Save(session);
    }

    // 本轮成功完成：补齐轨迹中尚未落盘的尾部（生成期间已逐条落过，这里通常只剩最后一两条）。
    // 用户消息已在 BeginTurn 写入，故这里只补助手侧。首轮（非手动命名）顺带用 LLM 总结覆盖占位标题。
    void CompleteTurn(SessionContext ctx, string userText, string assistantText, IReadOnlyList<AgentTurnMessage> trajectory, bool isNewSession)
    {
        FlushTrajectory(ctx, trajectory);
        if (isNewSession && !ctx.TitleManual)
            _ = GenerateTitleAsync(ctx, userText, assistantText);
    }

    // 重试成功：清除该轮的失败结局、补齐本次续跑轨迹（用户消息与之前已完成轮已在库中）。
    // 该轮就此变回正常成功轮——重载不再显示错误/重试。
    void ResolveTurn(SessionContext ctx, ChatTurnMessage anchor, IReadOnlyList<AgentTurnMessage> trajectory)
    {
        anchor.Outcome = null;
        anchor.ErrorText = null;
        if (ctx.Session == null)
            return;
        FlushTrajectory(ctx, trajectory);
        // Outcome 的清除本身也要落盘。生成期间轨迹已逐条落过时 Flush 不写盘（这是常态），故无条件再存一次——
        // 否则重试成功后重开，那一轮又变回失败态。
        AgentSessionStore.Save(ctx.Session);
    }

    // 轮终态为取消/出错：回写锚消息的结局，并把失败前已构建的"半截过程"（partialTrajectory）也落盘——供重载如实重放，
    // 做到显示 重载==实时。上下文重建（ReconstructHistory）只砍悬空 tool_call 尾巴、其余照喂（失败单位=单次模型回复，
    // 用户消息与已完成轮永远留在上下文）；UI 重载则渲染半截过程 + "已停止/失败+原因"——真相保留。
    void MarkTurnOutcome(SessionContext ctx, ChatTurnMessage anchor, string outcome, string? errorText, IReadOnlyList<AgentTurnMessage>? partialTrajectory = null)
    {
        anchor.Outcome = outcome;
        anchor.ErrorText = errorText;
        if (ctx.Session == null)
            return;
        // 补齐半截过程中尚未落盘的尾部（生成期间已逐条落过，故这里走水位、不重复写已有的那些）。
        if (partialTrajectory != null)
            FlushTrajectory(ctx, partialTrajectory);
        // 出错另记一条【在带内】的错误记录：它才是"这里当时发生了什么"的位置正确的痕迹。锚上的 Outcome 是
        // "本轮当前是否处于失败态"（重试成功即清），而这条记录永不删——否则重试成功后历史里只剩下半截内容、
        // 没有任何线索说明它为何半截（= 隐藏真实轨迹）。role="error" 不喂模型（ReconstructHistory 跳过）。
        if (outcome == ChatTurnMessage.OutcomeError)
            ctx.Session.Messages.Add(new ChatTurnMessage { Role = ChatTurnMessage.RoleError, Text = errorText ?? string.Empty, IsError = true });
        ctx.Session.UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        AgentSessionStore.Save(ctx.Session);
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
        // 轨迹里的 user = 轮边界插话（常规首条用户消息由 BeginTurn 在发送时另行写入、不经轨迹）。标记 Interjected 以便重载时行内重放。
        if (m.Role == AgentRole.User)
            return new ChatTurnMessage { Role = "user", Text = m.Content ?? string.Empty, Interjected = true };
        return new ChatTurnMessage
        {
            Role = "assistant",
            Text = m.Content ?? string.Empty,
            Reasoning = m.Reasoning,
            ToolCalls = m.ToolCalls?.Select(c => new ChatToolCall { Id = c.Id, Name = c.Name, ArgumentsJson = c.ArgumentsJson }).ToList(),
            Stopped = m.Stopped,
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
            // 标题请求是一次网络往返（数秒），期间会话可能已被删除——那时 ctx.Session 已置空，而这里的局部 session
            // 还持着旧对象，照写就会让删掉的会话复活。比对同一性而非仅判 null：会话被换掉也同样不该写。
            if (ctx.Session != session)
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
        var text = mInput.Text?.Trim() ?? string.Empty;

        // 生成中：把输入并入"轮边界插话"待发缓冲，不另起新 run（runner 到边界吃掉、行内渲染）。仅处理文本；
        // 图片附件留在撰写条等本轮结束（插话 v1 只支持文本）。已有 pending 则换行合并。
        if (ctx.Busy)
        {
            if (string.IsNullOrEmpty(text))
                return;
            ctx.PendingText = string.IsNullOrEmpty(ctx.PendingText) ? text : ctx.PendingText + "\n" + text;
            mInput.Text = string.Empty;
            RefreshPendingChip();
            return;
        }

        var images = mPendingImages.ToList(); // 本轮附件快照（发送即清空待发条，避免下一轮重复带上）
        if (string.IsNullOrEmpty(text) && images.Count == 0)
            return;

        if (mSession == null)
        {
            AppendMessage(ctx, "system", "Not connected. Open settings (gear) to choose a model and submit.".Tr(this));
            ShowSettings();
            return;
        }
        if (mProject == null)
        {
            AppendMessage(ctx, "system", "No project is open.".Tr(this));
            return;
        }

        mInput.Text = string.Empty;
        mPendingImages.Clear();
        RebuildAttachmentStrip();
        // 新消息一发出，之前那次失败就不再是对话末尾 → 收掉它的[重试]（留红字痕迹本身）。
        // 否则那颗按钮点下去会变成"从末尾再续一轮"：结果落在底部、历史归属还会错位（重载路径本就只给最后一轮）。
        ctx.RetireLiveErrorEntry?.Invoke();
        ctx.RetireLiveErrorEntry = null;
        AppendUserMessage(ctx, text, images.Select(i => i.Data).ToList());
        // 附件 → ChatAttachment（含原始字节，Save 落 blob、清单只引用）。
        var attachments = images.Count > 0
            ? images.Select(i => new ChatAttachment { Hash = AgentSessionStore.ComputeHash(i.Data), MediaType = i.MediaType, Data = i.Data }).ToList()
            : null;
        // persist-on-send：用户消息发送即落盘（不等回复），拿到本轮锚以便终态回写结局。
        bool isNewSession = ctx.Session == null;
        var anchor = BeginTurn(ctx, text, attachments);
        ctx.Runner ??= new AgentRunner(mSession, mTools, SystemPrompt, ctx.SeedHistory);
        var parts = images.Count > 0 ? images.Select(i => AgentContentPart.OfImage(i.Data, i.MediaType)).ToList() : null;
        await RunTurnAsync(ctx, anchor,
            (progress, ct, takePending) => ctx.Runner!.SendAsync(text, progress, ct, parts, takePending),
            reply => CompleteTurn(ctx, text, reply.Text, reply.Trajectory, isNewSession));
    }

    // 失败轮的重试：不重发消息，对当前上下文（末尾即那条用户消息 + 已完成轮）续跑；错误条目就地降级留痕，结果续在其后。
    // 仅错误轮给（见 BuildErrorEntry）。重载后 runner 为空则据 SeedHistory 重建（其末尾正是待重试的用户消息 + 已完成轮）。
    async void OnRetry(SessionContext ctx, ChatTurnMessage anchor, Action retireErrorEntry)
    {
        if (ctx.Busy || mSession == null)
            return;
        retireErrorEntry();               // 降级为「（已重试）」留痕行
        ctx.RetireLiveErrorEntry = null;  // 这条已收，别再被后续 OnSend 收第二次
        ctx.Runner ??= new AgentRunner(mSession, mTools, SystemPrompt, ctx.SeedHistory);
        await RunTurnAsync(ctx, anchor,
            (progress, ct, takePending) => ctx.Runner!.RetryAsync(progress, ct, takePending),
            reply => ResolveTurn(ctx, anchor, reply.Trajectory));
    }

    // 跑一轮的可视化脚手架（发送与重试共用）：占位气泡 → 分步渲染 → 成功/取消/出错收尾 + 落盘。
    // runnerCall：实际调用 runner（SendAsync 或 RetryAsync）；onSuccess：成功后的持久化（CompleteTurn / ResolveTurn）。
    async Task RunTurnAsync(SessionContext ctx, ChatTurnMessage anchor,
        Func<IProgress<AgentEvent>, CancellationToken, Func<string?>, Task<AgentTurnResult>> runnerCall,
        Action<AgentTurnResult> onSuccess)
    {
        var bubble = AddAssistantBubble(ctx); // 响应期占位气泡（动态等待指示）
        var cts = new CancellationTokenSource();
        ctx.Cts = cts;
        SetBusy(ctx, true);
        // 新一轮：runner 的 trajectory 是新建的列表，水位随之归零。
        ctx.PersistedTurnMessages = 0;
        // 边跑边落：runner 每往轨迹追加一条即【同步】回调，把它补进会话文件。这样进程在下一步消失（意外关闭 /
        // 崩溃 / 其它）时，已发生的调用不会随内存一起没掉——否则一句话触发的长任务被打断后，会话里只剩用户那一句。
        if (ctx.Runner != null)
            ctx.Runner.TrajectoryAppended = () => FlushTrajectory(ctx, ctx.Runner!.CurrentTrajectory);

        // 分步渲染：把 runner 的进度事件按序铺进气泡，全程可见模型在说什么、调了哪个工具、结果如何。气泡属于 ctx.View，
        // 即便用户切走（视图离屏）流式仍写进它，切回即见进度；滚动只在该会话可见时执行。
        var turn = new AgentTurnView();
        bool swapped = false;
        void EnsureSwapped() { if (!swapped) { bubble.Child = turn.Root; swapped = true; } }
        void Handle(AgentEvent e)
        {
            if (e is AgentRoundUsage g)
                AccumulateRoundTokens(ctx, g);
            EnsureSwapped();
            turn.Apply(e);
            ScrollToEnd(ctx);
        }

        try
        {
            ctx.CurrentTurn = turn;      // 升级卡片渲进本轮步骤流（见 RequestScriptAuthorizationAsync）
            mRunningContext.Value = ctx; // 顺 await 链流进共享工具，让其定位到"触发这一轮"的会话
            // 轮边界软插话钩子：runner 到安全边界取本会话累积的 pending 文本注入续跑（UI 线程同步执行，消费即清 chip）。
            string? TakePending()
            {
                var p = ctx.PendingText;
                if (string.IsNullOrEmpty(p))
                    return null;
                ctx.PendingText = null;
                if (ctx == mActive)
                    RefreshPendingChip();
                return p;
            }
            var reply = await runnerCall(new Progress<AgentEvent>(Handle), cts.Token, TakePending);
            turn.Seal();
            // 宿主护栏截断了本轮（当前只有失控防护）：渲一行中性提示并落一条 notice 记录，别让它安静收场——
            // 那会让用户误判成模型自己停了。提示排在脚注之前（脚注是本轮的收尾统计）。
            if (!string.IsNullOrEmpty(reply.StopNotice))
            {
                EnsureSwapped();
                turn.Append(NoticeLine(reply.StopNotice, Style.LIGHT_WHITE.Opacity(0.5).ToBrush()));
            }
            if (turn.IsEmpty)
                bubble.Child = BubbleText("(no text reply)", Colors.White.ToBrush());
            else
            {
                var (calls, lastCtx) = TurnBreakdown(reply.Trajectory);
                turn.Append(BuildFooter(reply.Usage, calls, lastCtx));
            }
            onSuccess(reply);
            if (!string.IsNullOrEmpty(reply.StopNotice) && ctx.Session != null)
            {
                ctx.Session.Messages.Add(new ChatTurnMessage { Role = ChatTurnMessage.RoleNotice, Text = reply.StopNotice });
                AgentSessionStore.Save(ctx.Session);
            }
            // token 口径已在生成过程中由 AgentRoundUsage 逐次累加（见 AccumulateRoundTokens），此处不再整轮累加。
        }
        catch (OperationCanceledException)
        {
            // 用户主动停止：保留已渲染的分步内容 + 末尾灰字 Stopped，把仍在运行的工具块标记中止，不当错误（红字）。
            turn.Seal();
            turn.MarkPendingAborted();
            EnsureSwapped();
            turn.Append(NoticeLine("Stopped".Tr(this), Style.LIGHT_WHITE.Opacity(0.5).ToBrush()));
            MarkTurnOutcome(ctx, anchor, ChatTurnMessage.OutcomeCancelled, null, ctx.Runner?.CurrentTrajectory);
            // 悬空调用补"结果未知"（不删）——与重载重建同一处置，故"重开前继续"与"重开后继续"行为一致
            ctx.Runner?.CloseDanglingToolCalls();
        }
        catch (Exception ex)
        {
            // 中途报错：保留已渲染的分步内容，错误另起可重试/复制的条目（不丢已输出有效内容）。
            turn.Seal();
            turn.MarkPendingAborted();
            // 本轮一字未出（如首次请求就失败/被风控挡下）→ 撤掉占位气泡，否则留一个空容器占一行；
            // 每次重试失败都会再攒一个。重载路径本就只在有消息时才建轮视图（RebuildHistoryView 的 i > start），此处对齐它。
            if (turn.IsEmpty)
                ctx.View.Content.Children.Remove(bubble);
            else
                EnsureSwapped();
            MarkTurnOutcome(ctx, anchor, ChatTurnMessage.OutcomeError, ex.Message, ctx.Runner?.CurrentTrajectory);
            ctx.Runner?.CloseDanglingToolCalls(); // 悬空调用补"结果未知"；用户消息 + 已完成轮留在上下文，可经重试按钮续跑
            ctx.View.Content.Children.Add(BuildErrorEntry(ctx, anchor, ex.Message, allowRetry: true));
        }
        finally
        {
            turn.EndThinking(); // 生成结束（成功/停止/出错）：移除底部"生成中"三点动画
            ctx.CurrentTurn = null;
            cts.Dispose();
            if (ctx.Cts == cts) // 仅清掉本轮自己的取消源（该会话期间不会有并发的第二轮）
                ctx.Cts = null;
            SetBusy(ctx, false);
            // 运行已结束仍有未消费插话（仅取消/出错路径可能）：可见会话召回输入框，否则清掉（其目标运行已终止）。
            if (!string.IsNullOrEmpty(ctx.PendingText))
            {
                if (ctx == mActive)
                    RecallPending();
                else
                    ctx.PendingText = null;
            }
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
        ScrollToEnd(ctx, force: true);   // 用户刚发送：无条件滚到底展示自己的消息
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
    // 用【显式定宽】撑满内容列（= mContentMaxWidth，随侧栏走）：文字在容器内换行、复制按钮在容器内右对齐，都锚这条统一右边缘，
    // 不随文字长短跳动（若改用 MaxWidth 会按内容收缩、右对齐就锚在离散的文字宽度上）。ListView 无限宽测量下 Stretch 不生效，故给定宽。
    Border AssistantContainer(Control content) => new()
    {
        Tag = "assistant",
        Background = Brushes.Transparent,
        Margin = new(12, 4),
        Width = mContentMaxWidth,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
        Child = content,
    };

    // 用户气泡：靠右、主色底；agent 用 AssistantContainer 不再走这里。MaxWidth 随对话区宽度自适应。
    Border Bubble(Control content, bool mine) => new()
    {
        MaxWidth = mBubbleMaxWidth,
        CornerRadius = new(8),
        Padding = new(10, 6),
        // 右对齐的用户气泡右 margin = 滚动条预留厚度，避免被竖条压住（左对齐系统气泡右侧不贴边、维持 8）。
        Margin = mine ? new(8, 4, ScrollBar.ReservedThickness, 4) : new(8, 4),
        Background = (mine ? Style.BUTTON_PRIMARY : Style.INTERFACE).ToBrush(),
        HorizontalAlignment = mine ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Left,
        Child = content,
    };

    // 纯文本气泡内容（用户消息、占位「…」、错误文本用它）。
    static SelectableTextBlock BubbleText(string text, IBrush foreground)
        => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = foreground, FontSize = 12 };

    // 脚注一行：token 用量（带单位，hover 看明细）。复制已下放到每段返回（见 AgentTurnView 分段 Copy），脚注不再重复放 Copy。
    // usage = 本轮各次模型调用的合计（工具往返会重复前缀，故多次调用时远大于单轮上下文）。modelCalls=本轮模型调用次数、
    // lastContextTokens=末次调用的输入+输出（≈状态行 Context）。多次调用时脚注标注 "· N calls" 并在 tooltip 里和 Context 桥接，
    // 消解"脚注合计 vs 状态行 Context 对不上"的疑惑。端点未返回 usage 则脚注为空行。
    Control BuildFooter(AgentTokenUsage? usage, int modelCalls = 1, int lastContextTokens = 0)
    {
        var footer = new DockPanel { LastChildFill = false, Margin = new(0, 4, 0, 0) };
        if (usage != null)
        {
            bool multi = modelCalls > 1;
            var tokens = new TextBlock
            {
                // 多次调用：标注 "· N calls"，明示该数是 N 次往返的合计（非单轮上下文）。
                Text = multi
                    ? string.Format("{0:N0} tokens · {1} calls", usage.TotalTokens, modelCalls)
                    : string.Format("{0:N0} tokens", usage.TotalTokens),
                FontSize = 11,
                Foreground = Style.LIGHT_WHITE.Opacity(0.4).ToBrush(),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new(0, 0, 16, 0), // 与右侧 Copy 留间距：短回复气泡窄时两者会贴到一起
            };
            ToolTip.SetTip(tokens, multi
                ? string.Format("Total of {0} model calls this turn (tool round-trips repeat the prefix)\nInput {1:N0} · Output {2:N0}\nLast call context ~{3:N0} tokens".Tr(this),
                    modelCalls, usage.PromptTokens, usage.CompletionTokens, lastContextTokens)
                : string.Format("Input {0:N0} · Output {1:N0}".Tr(this), usage.PromptTokens, usage.CompletionTokens));
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
    // 滚到底。force=false（默认，流式增量/新块用）：仅当用户【本就贴着底部】才跟随——若用户已上翻查看前文，
    // 不把滚动条强拉回底（否则边输出边往上看会被每个增量不断打断）。检查在新内容布局刷新之前同步进行，故此刻
    // ViewOffset/ContentLength 反映"新内容加入前"的位置，正好判定用户是否在跟随。force=true（切会话/用户发送）：无条件到底。
    void ScrollToEnd(SessionContext ctx, bool force = false)
    {
        if (force)
            ctx.AutoFollow = true;
        else if (!ctx.AutoFollow)
            return;      // 用户正在上面看别的 → 不抢他的视野
        if (ctx != mActive)
            return;      // 离屏会话不滚（切回时按需处理）；但上面的 AutoFollow 语义仍要维护
        // 两次推进：内容可能在本帧之后才长高（markdown 块重建 / 图片测量），只滚一次会停在旧底部。
        Dispatcher.UIThread.Post(() => Stick(ctx), DispatcherPriority.Background);
        Dispatcher.UIThread.Post(() => Stick(ctx), DispatcherPriority.Loaded);
    }

    // 贴底（只在仍处于跟随态时执行；ViewOffset 会被轴钳到 max）。
    void Stick(SessionContext ctx)
    {
        if (ctx == mActive && ctx.AutoFollow)
            ctx.View.VerticalAxis.ViewOffset = 1e9;
    }

    // 消息区竖轴变化：据此维护"是否仍在跟随底部"。判据是【谁动了视野】而非【当前离底多远】：
    //  · 到底 → 恢复跟随（含我们自己滚到底、用户手动拖回底）；
    //  · 只有 offset 变了（内容长度、视野高度都没变）且不在底部 → 是用户滚轮/拖手柄把视野移开了 → 停止跟随；
    //  · 内容长高 / 视野尺寸变化引起的"离底"不算用户操作（流式输出与拖宽侧栏都属此类），不停跟随。
    void OnMessagesAxisChanged(SessionContext ctx)
    {
        var axis = ctx.View.VerticalAxis;
        double offset = axis.ViewOffset, content = axis.ContentLength, view = axis.ViewLength;
        bool offsetMoved = Math.Abs(offset - ctx.LastViewOffset) > 0.5;
        bool geometryChanged = Math.Abs(content - ctx.LastContentLength) > 0.5 || Math.Abs(view - ctx.LastViewLength) > 0.5;
        ctx.LastViewOffset = offset;
        ctx.LastContentLength = content;
        ctx.LastViewLength = view;

        if (offset >= Math.Max(0, content - view) - AutoFollowSlack)
            ctx.AutoFollow = true;
        else if (offsetMoved && !geometryChanged)
            ctx.AutoFollow = false;
    }

    // 判定"贴底"的容差（约一行高）：略低于底也算回到底部，恢复跟随。
    const double AutoFollowSlack = 24;

    // 标记某会话的忙碌态（输入框始终可用，由 ctx.Busy 拦该会话内回车重复发送）。若它是当前可见会话，同步发送/停止键。
    void SetBusy(SessionContext ctx, bool busy)
    {
        ctx.Busy = busy;
        if (ctx == mActive)
            RefreshSendControls();
    }

    // 按当前可见会话的 PendingText 刷新 chip（显示/隐藏 + 预览文本）。切会话、入队、消费、召回、丢弃后都调用。
    void RefreshPendingChip()
    {
        var p = mActive.PendingText;
        if (string.IsNullOrEmpty(p))
        {
            mPendingChip.IsVisible = false;
            mPendingPreview.Text = string.Empty;
            return;
        }
        mPendingPreview.Text = p;
        mPendingChip.IsVisible = true;
    }

    // ✎：把 pending 召回到输入框编辑（与输入框已有未发文字合并，pending 在前，不丢失），清空缓冲。
    void RecallPending()
    {
        var p = mActive.PendingText;
        if (string.IsNullOrEmpty(p))
            return;
        var cur = mInput.Text?.Trim() ?? string.Empty;
        mInput.Text = string.IsNullOrEmpty(cur) ? p : p + "\n" + cur;
        mActive.PendingText = null;
        RefreshPendingChip();
        mInput.TextArea.Focus();
    }

    // ✕：丢弃 pending（不发出）。
    void DiscardPending()
    {
        mActive.PendingText = null;
        RefreshPendingChip();
    }

    // ───────────────── 设置视图 ─────────────────

    void BuildSettingsView()
    {
        var header = new DockPanel() { Height = 32, LastChildFill = true, Background = Style.INTERFACE.ToBrush() };
        // 返回键放右上角，与对话页 ⚙ 同位置；无底色、icon hover 变色。有未应用改动时点它弹「应用/忽略」。
        var back = IconButton(Assets.WindowClose, Style.LIGHT_WHITE.Opacity(0.6), Colors.White);
        back.Clicked += OnSettingsBack;
        DockPanel.SetDock(back, Dock.Right);
        header.Children.Add(back);
        // 标题存字段：有未应用改动时右上角补 *（同窗口标题栏惯例，见 RefreshSettingsDirtyMark）。
        mSettingsTitleLabel.Content = "Model Settings".Tr(this);
        header.Children.Add(mSettingsTitleLabel);

        var content = new StackPanel() { Orientation = Orientation.Vertical };
        // 授权已移到对话页 header 胶囊（即时生效）；本面板只剩「连接类设置」，全部统一为「确认才生效」，dirty 追踪无一例外。
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
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,   // 原生条隐藏，改挂统一浮层滚动条
            Content = content,
        };
        mSettingsScrollBars = new OverlayScrollBars(scroll, horizontal: false, vertical: true);   // 存字段防 GC

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
        var props = new OrderedMap<PropertyKey, IControllerConfig>();
        props.Add((EngineKey, "Model Provider".Tr(this)), ComboBoxConfig.Create(mEngineOptions));
        return ObjectConfig.Create(props);
    }

    // ───────────────── 授权胶囊（对话页 header） ─────────────────

    // 当前授权档位的短名（胶囊显示用；完整描述见 flyout 行）。
    string AuthShortName(AgentAuthorization level) => level switch
    {
        AgentAuthorization.ReadOnlyAdvice => "Read-only".Tr(this),
        AgentAuthorization.Auto => "Auto".Tr(this),
        _ => "Confirm".Tr(this),
    };

    // 胶囊短名随 Settings.AgentAuthorization 刷新（订阅在 ctor；胶囊/升级卡片任一改动都经此同步）。
    void RefreshAuthPill()
    {
        var level = AgentAuthorizationExtensions.ParseOrDefault(Settings.AgentAuthorization.Value);
        mAuthLabel.Text = AuthShortName(level);
        ToolTip.SetTip(mAuthButton, "Agent authorization".Tr(this));
    }

    // 点开胶囊：三选一 flyout，当前项打勾，选中即生效。
    void OpenAuthMenu()
    {
        var current = AgentAuthorizationExtensions.ParseOrDefault(Settings.AgentAuthorization.Value);
        var stack = new StackPanel() { Orientation = Orientation.Vertical, MinWidth = 200 };
        stack.Children.Add(BuildAuthRow(AgentAuthorization.ReadOnlyAdvice, "Read-only (advise, never apply)".Tr(this), current));
        stack.Children.Add(BuildAuthRow(AgentAuthorization.Confirm, "Confirm each change".Tr(this), current));
        stack.Children.Add(BuildAuthRow(AgentAuthorization.Auto, "Apply automatically".Tr(this), current));
        mAuthFlyout.Content = stack;
        mAuthFlyout.ShowAt(mAuthButton);
    }

    // flyout 一行：左侧勾（当前项）+ 描述文本；hover 高亮；点击即设并关闭。
    Control BuildAuthRow(AgentAuthorization level, string text, AgentAuthorization current)
    {
        var check = new TextBlock() { Text = level == current ? "✓" : string.Empty, Width = 16, FontSize = 11, Foreground = Style.BUTTON_PRIMARY.ToBrush(), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        var title = new TextBlock() { Text = text, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Colors.White.ToBrush() };
        var dock = new DockPanel();
        DockPanel.SetDock(check, Dock.Left);
        dock.Children.Add(check);
        dock.Children.Add(title);
        var row = new Border() { Padding = new(10, 6), CornerRadius = new(4), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = dock };
        row.PointerEntered += (_, _) => row.Background = Style.LIGHT_WHITE.Opacity(0.08).ToBrush();
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        row.PointerPressed += (_, e) => { e.Handled = true; SetAuthorization(level); mAuthFlyout.Hide(); };
        return row;
    }

    // 设授权档位并即时存盘（胶囊短名经 Settings 订阅刷新）。供胶囊与升级卡片"始终允许"共用。
    void SetAuthorization(AgentAuthorization level)
    {
        Settings.AgentAuthorization.Value = level.ToString();
        Settings.Save(PathManager.SettingsFilePath);
    }

    // ───────────────── 升级卡片（Confirm 档：agent 要写时的内联裁决） ─────────────────

    // RunScriptTool 的 confirm 回调：在触发这一轮的对话视图里渲染升级卡片、等用户裁决。
    // 目标会话经 mRunningContext（AsyncLocal，OnSend 埋入）定位——共享工具据此找到正确的那一轮，即便它在后台。
    Task<ScriptAuthDecision> RequestScriptAuthorizationAsync(AgentAuthorizationRequest request, CancellationToken cancellationToken)
    {
        var ctx = mRunningContext.Value ?? mActive;
        var tcs = new TaskCompletionSource<ScriptAuthDecision>();
        void Build()
        {
            var card = BuildAuthRequestCard(request, tcs);
            if (ctx.CurrentTurn != null)
                ctx.CurrentTurn.Append(card); // 插进本轮步骤流，与工具步骤同序
            else
                ctx.View.Content.Children.Add(card);
            ScrollToEnd(ctx);
        }
        // 同 RequestUserAnswerAsync：总是排队，别直接建。工具块经 Progress<AgentEvent> 异步 Post，
        // 而本回调在 UI 线程同步跑——直接建会让卡片落在它所属的那次工具调用【上方】。
        Dispatcher.UIThread.Post(Build);
        // 轮被取消（用户点停）→ 裁决按拒绝收尾（卡片经 tcs 续接切到"已停止"）。
        cancellationToken.Register(() => tcs.TrySetResult(ScriptAuthDecision.Reject));
        return tcs.Task;
    }

    // ask_user_question 的回调：在触发这一轮的对话视图里渲染问答卡片、等用户回答。
    // 目标会话定位同升级卡片（mRunningContext）；不设超时——卡片一直挂着，直到用户回答或点停。
    Task<AgentUserAnswer> RequestUserAnswerAsync(AgentUserQuestion question, CancellationToken cancellationToken)
    {
        var ctx = mRunningContext.Value ?? mActive;
        var tcs = new TaskCompletionSource<AgentUserAnswer>();
        void Build()
        {
            var card = BuildQuestionCard(question, tcs);
            if (ctx.CurrentTurn != null)
                ctx.CurrentTurn.Append(card);   // 插进本轮步骤流，与工具步骤同序
            else
                ctx.View.Content.Children.Add(card);
            ScrollToEnd(ctx);
        }
        // 【总是排队渲染，不走 CheckAccess 直接建】：工具块由 AgentToolStarted 事件渲染，而那条事件走
        // Progress<AgentEvent> 异步 Post 到 UI 线程；本回调却是在 UI 线程上同步执行的。直接建会抢在
        // 工具块之前落地，卡片就跑到"它自己那次调用"的上方去了。Post 让它排在已入队的工具事件之后。
        Dispatcher.UIThread.Post(Build);
        // 轮被取消（用户点停）→ 取消这次等待。刻意不是"返回空答案"：那会让模型以为用户回答了个空，
        // 而事实是这次调用没有结果。抛取消后它成为悬空调用、被如实记作"结果未知"（同其它工具）。
        cancellationToken.Register(() => tcs.TrySetCanceled());
        return tcs.Task;
    }

    // 问答卡片的外壳：实时卡片与重载重建【共用】，否则两边的圆角/底色/边框迟早漂移（重载那次就漏了整层框）。
    Border QuestionCardShell(Control content) => new()
    {
        CornerRadius = new(6),
        Padding = new(10, 8),
        Margin = new(0, 6, 0, 2),
        Background = Style.INTERFACE.ToBrush(),
        BorderBrush = Style.BUTTON_PRIMARY.Opacity(0.5).ToBrush(),
        BorderThickness = new(1),
        Child = content,
    };

    // 只读的问答留痕：问题 + 选项（打勾但不可点，未选的标签压暗）+ 补充文本 +（未回答时）一行说明。
    // 【实时与重载共用这一个函数】——用户一提交就把交互卡片的内容换成它，重开会话时按记录重建也调它，
    // 两边因此长得一模一样（不必再担心"显示 重载==实时"在这里破功）。
    Control QuestionBlockContent(string question, IReadOnlyList<string> options, bool multiple, IReadOnlyList<string> selected, string? text, bool answered)
    {
        var panel = new StackPanel() { Orientation = Orientation.Vertical };
        panel.Children.Add(new SelectableTextBlock()
        {
            Text = question,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Colors.White.ToBrush(),
        });
        if (options.Count > 0)
        {
            var list = new StackPanel() { Orientation = Orientation.Vertical, Spacing = 2, Margin = new(0, 8, 0, 0) };
            foreach (var option in options)
            {
                bool on = selected.Contains(option);
                Toggle box = multiple
                    ? new TuneLab.GUI.Components.CheckBox()
                    : new TuneLab.GUI.Components.RadioButton();
                box.Display(on);
                box.IsHitTestVisible = false;   // 留痕不可改
                box.Margin = new(0, 0, 8, 0);
                box.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                var row = new DockPanel() { LastChildFill = true, Margin = new(2, 4) };
                DockPanel.SetDock(box, Dock.Left);
                row.Children.Add(box);
                row.Children.Add(new SelectableTextBlock()
                {
                    Text = option,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    // 没选中的压暗：一眼看出选了哪个，不必逐个去看勾。
                    Foreground = (on ? Colors.White : Style.LIGHT_WHITE.Opacity(0.45)).ToBrush(),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                });
                list.Children.Add(row);
            }
            panel.Children.Add(list);
        }
        if (!string.IsNullOrEmpty(text))
        {
            // 有选项时这段是"补充"，没选项时它本身就是回答——故前缀分开。
            var prefix = options.Count > 0 ? "Added: ".Tr(this) : string.Empty;
            panel.Children.Add(new SelectableTextBlock()
            {
                Text = prefix + text,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Colors.White.ToBrush(),
                Margin = new(0, 8, 0, 0),
            });
        }
        // 未回答（问了但那一轮被打断）：明说一句，否则"全都没勾"会被读成"用户明确一个都没选"。
        if (!answered)
        {
            var line = NoticeLine("Not answered".Tr(this), Style.LIGHT_WHITE.Opacity(0.5).ToBrush());
            line.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            line.Padding = new(0, 0, 4, 0);
            line.Margin = new(0, 6, 0, 0);
            panel.Children.Add(line);
        }
        return panel;
    }

    // 把 ask_user_question 的一次调用（参数 + 结果）还原成只读问答块。结果为 null = 问了但没等到回答。
    Control BuildRecordedQuestionBlock(string? argumentsJson, string? result)
    {
        string question = string.Empty;
        var options = new List<string>();
        bool multiple = false;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson ?? "{}");
            var root = doc.RootElement;
            question = root.GetString("question") ?? string.Empty;
            if (root.TryGetProperty("options", out var opts) && opts.ValueKind == JsonValueKind.Array)
                foreach (var o in opts.EnumerateArray())
                {
                    var label = ((o.ValueKind == JsonValueKind.String ? o.GetString() : o.ToString()) ?? string.Empty).Trim();
                    if (label.Length > 0 && !options.Contains(label))
                        options.Add(label);
                }
            if (root.TryGetProperty("multiple", out var m) && (m.ValueKind == JsonValueKind.True || m.ValueKind == JsonValueKind.False))
                multiple = m.GetBoolean();
        }
        catch { /* 参数坏了也要能显示：问题留空、当作无选项处理 */ }

        var (selected, text) = ParseQuestionResult(result);
        // 与实时同一层外壳：重载看到的框、底色、圆角都要一模一样。
        return QuestionCardShell(QuestionBlockContent(question, options, multiple, selected, text, answered: result != null));
    }

    // 反解工具回报（格式由 AskUserQuestionTool 生成，逐行、无逗号拼接，故不存在"选项名含逗号"的歧义）：
    //   Selected:\n- a\n- b        选中项各占一行、以 "- " 起头
    //   No option was selected. / Selected: none — … / No options were offered.
    //   末段可选 "Additional input: …" 或 "Input: …"——【其后全部内容都是文本】（它可能自带换行，故固定放最后）。
    static (List<string> Selected, string? Text) ParseQuestionResult(string? result)
    {
        var selected = new List<string>();
        if (string.IsNullOrEmpty(result))
            return (selected, null);

        string? text = null;
        int i = 0;
        while (i < result.Length)
        {
            int lineEnd = result.IndexOf('\n', i);
            if (lineEnd < 0)
                lineEnd = result.Length;
            var line = result[i..lineEnd].TrimEnd('\r');
            foreach (var marker in QuestionTextMarkers)
            {
                int at = line.IndexOf(marker, StringComparison.Ordinal);
                if (at < 0)
                    continue;

                // 标记之后（含本行剩余）全部是用户文本，原样取走。
                text = result[(i + at + marker.Length)..].Trim();
                return (selected, text.Length > 0 ? text : null);
            }
            if (line.StartsWith("- ", StringComparison.Ordinal))
                selected.Add(line[2..].Trim());
            i = lineEnd + 1;
        }
        return (selected, text);
    }

    static readonly string[] QuestionTextMarkers = ["Additional input: ", "Input: "];

    // 问答卡片：问题 + 选项（单选 RadioButton / 多选 CheckBox）+ 自由文本框 + [提交]。
    // 选项与文本框【各自独立】：可以只选、只写、或两者都有；故提交条件是"至少有一样"。
    // 互斥按仓库范式做（同 FunctionBar 的钢琴工具 / ParameterTabBar 的参数 tab）：点击只改"当前选中集合"，
    // 再回头统一刷新全部按钮的显示——控件自己不知道同伴是谁。
    Control BuildQuestionCard(AgentUserQuestion question, TaskCompletionSource<AgentUserAnswer> tcs)
    {
        var panel = new StackPanel() { Orientation = Orientation.Vertical };
        panel.Children.Add(new SelectableTextBlock()
        {
            Text = question.Question,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Colors.White.ToBrush(),
        });

        var selected = new List<string>();
        var refreshers = new List<Action>();
        void Refresh() { foreach (var r in refreshers) r(); }
        // 回答（或取消）后整块换成只读留痕（见 Seal）。settled 声明在选项之前，因为下面每行的点击处理要读它
        // ——虽然换掉整块后旧控件已离开视觉树、收不到事件，这道守卫仍留着防御"提交与替换之间又被点一下"。
        bool settled = false;

        if (question.Options.Count > 0)
        {
            var list = new StackPanel() { Orientation = Orientation.Vertical, Spacing = 2, Margin = new(0, 8, 0, 0) };
            foreach (var option in question.Options)
            {
                var captured = option;
                // 单选圆点 / 多选方框——让"能选几个"一眼可辨。两者都不接 AllowSwitch：本卡片允许一个都不选。
                // 全限定：Avalonia 也有同名 CheckBox/RadioButton，这里要的是自绘的 TuneLab.GUI.Components 版。
                Toggle box = question.Multiple
                    ? new TuneLab.GUI.Components.CheckBox()
                    : new TuneLab.GUI.Components.RadioButton();
                var label = new TextBlock()
                {
                    Text = captured,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Colors.White.ToBrush(),
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                };
                var row = new DockPanel() { LastChildFill = true };
                box.Margin = new(0, 0, 8, 0);
                box.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                DockPanel.SetDock(box, Dock.Left);
                row.Children.Add(box);
                row.Children.Add(label);
                // 整行可点：16px 的方框在窄侧栏里是个太小的靶子，点文字也应生效。
                var hit = new Border()
                {
                    Padding = new(2, 4),
                    CornerRadius = new(4),
                    Background = Brushes.Transparent,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = row,
                };
                void Toggle_()
                {
                    if (settled)
                        return;    // 已回答/已停止：选中态是留痕，不许再动

                    bool nowOn = !selected.Contains(captured);
                    if (!question.Multiple)
                        selected.Clear();          // 单选：改的是"当前选中"这一份状态，不是去命令别的按钮
                    if (nowOn)
                        selected.Add(captured);
                    else
                        selected.Remove(captured);
                    Refresh();
                }
                hit.PointerEntered += (_, _) => { if (!settled) hit.Background = Style.LIGHT_WHITE.Opacity(0.06).ToBrush(); };
                hit.PointerExited += (_, _) => hit.Background = Brushes.Transparent;
                hit.PointerPressed += (_, e) => { e.Handled = true; Toggle_(); };
                refreshers.Add(() => box.Display(selected.Contains(captured)));
                list.Children.Add(hit);
            }
            panel.Children.Add(list);
        }

        // 自由文本：始终给——用户永远要能表达"你的选项都不对"。
        var input = new MultilineTextInput()
        {
            MaxHeight = 96,
            Padding = new(6, 6, ScrollBar.ReservedThickness, 6),
            AutoGrow = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Margin = new(0, 8, 0, 0),
            Watermark = question.Options.Count > 0
                ? "Add anything else (optional)".Tr(this)
                : "Type your answer".Tr(this),
        };
        panel.Children.Add(input);

        var buttons = new StackPanel() { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(0, 10, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        panel.Children.Add(buttons);
        var card = QuestionCardShell(panel);

        // 定格：整块交互面【换成只读留痕块】——与重开会话后重建的是同一个函数，两边长得一模一样。
        // 换掉而不是"逐个禁用"：旧控件离开视觉树后再也收不到事件，不必担心哪里漏封一处又能改了。
        void Seal(IReadOnlyList<string> answeredOptions, string? answeredText, bool answered)
        {
            settled = true;
            card.Child = QuestionBlockContent(question.Question, question.Options, question.Multiple, answeredOptions, answeredText, answered);
        }
        void Submit()
        {
            if (settled || !CanSubmit())
                return;

            var text = (input.Text ?? string.Empty).Trim();
            var picked = selected.ToList();
            Seal(picked, text.Length > 0 ? text : null, answered: true);
            tcs.TrySetResult(new AgentUserAnswer(picked, text.Length > 0 ? text : null));
        }
        // 空答（既没选也没写）对 agent 没有信息量，等于让它白等一场 → 按钮【置灰不可点】。
        // 刻意不做成"能点但没反应"：那让人以为界面卡了，而置灰一眼就看出"还差点东西"。
        //
        // 【多选例外】多选时"一个都不选"本身就是答案（"这几条轨都要处理吗" → 一条都不要），故恒可提交；
        // 单选的"未选"才是没回答。回报会明说是"明确没选"而非"没回答"，免得模型把这两种情形读混。
        bool CanSubmit() => question.Multiple || selected.Count > 0 || (input.Text ?? string.Empty).Trim().Length > 0;

        // 提交按钮自造（不复用无状态的 CardButton）：它要随"有没有内容"实时切换可用态。
        // 串用 "Submit answer" 而非 "Submit"：后者已被设置面板的确认按钮占用（同 section 同键会互相覆盖，语义也不同）。
        var submitLabel = new TextBlock()
        {
            Text = "Submit answer".Tr(this),
            FontSize = 12,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        var submitButton = new Border() { CornerRadius = new(4), Padding = new(12, 5), Child = submitLabel };
        void SyncSubmitState()
        {
            bool on = CanSubmit();
            submitButton.Background = (on ? Style.BUTTON_PRIMARY : Style.LIGHT_WHITE.Opacity(0.08)).ToBrush();
            submitLabel.Foreground = (on ? Colors.White : Style.LIGHT_WHITE.Opacity(0.35)).ToBrush();
            submitButton.Cursor = new Cursor(on ? StandardCursorType.Hand : StandardCursorType.Arrow);
        }
        submitButton.PointerEntered += (_, _) => { if (CanSubmit()) submitButton.Background = Style.BUTTON_PRIMARY_HOVER.ToBrush(); };
        submitButton.PointerExited += (_, _) => SyncSubmitState();
        submitButton.PointerPressed += (_, e) => { e.Handled = true; Submit(); };
        buttons.Children.Add(submitButton);
        // 把按钮态并入 refreshers：Refresh() 因此是"重刷全部显示"的唯一入口——选项一变（Toggle_ 里调它）
        // 按钮可用态跟着重算，不必另铺一条刷新路径。文本框改动同样触发它（只选/只写/两者都有都算有内容）。
        refreshers.Add(SyncSubmitState);
        input.TextChanged.Subscribe(Refresh);
        Refresh();

        // 取消先行（点停）→ 同样定格，但标成【未回答】：当时勾了什么并没提交，不是答案，
        // 显示成"选了 X"会误导。这与重载时遇到悬空调用（问了没结果）的呈现是同一个。
        tcs.Task.ContinueWith(_ =>
        {
            void Finish()
            {
                if (settled)
                    return;

                Seal([], null, answered: false);
            }
            if (Dispatcher.UIThread.CheckAccess()) Finish();
            else Dispatcher.UIThread.Post(Finish);
        }, TaskContinuationOptions.ExecuteSynchronously);

        return card;
    }

    // 卡片：说明 + 三按钮（应用本次/始终允许/拒绝）；裁决后隐藏按钮、留一行结果。"始终允许"顺带切档到 Auto。
    // 文案按写请求种类分：工程编辑=改动数；脚本库删/覆盖=点名脚本 + 不可撤销提示（外部文件、历史记录管理器救不回）；
    // 改设置=点名那一项（用户看见的是【本地化行标】，模型侧才用键，故这里按键回查注册表标签）。
    // 设置键 → 用户看见的本地化行标（找不到则退回键本身，如注册表条目已改名）。
    static string SettingDisplayLabel(string? key)
    {
        foreach (var item in SettingsRegistry.All)
            if (item.Key == key)
                return item.DisplayLabel;
        return key ?? "";
    }

    Control BuildAuthRequestCard(AgentAuthorizationRequest request, TaskCompletionSource<ScriptAuthDecision> tcs)
    {
        var message = new SelectableTextBlock()
        {
            Text = request.Kind switch
            {
                AgentWriteKind.ScriptDelete => string.Format("The agent wants to delete the saved script \"{0}\". This can't be undone.".Tr(this), request.Target),
                AgentWriteKind.ScriptOverwrite => string.Format("The agent wants to overwrite the saved script \"{0}\". This can't be undone.".Tr(this), request.Target),
                AgentWriteKind.SettingChange => string.Format("The agent wants to change the setting \"{0}\" to {1}.".Tr(this), SettingDisplayLabel(request.Target), request.NewValue),
                // 快捷键：绑/解绑两句；夺键时再补一句点名被解绑的命令（知情同意）。命令名用本地化显示名，模型侧才用 id。
                AgentWriteKind.KeybindingChange => (string.IsNullOrEmpty(request.NewValue)
                        ? string.Format("The agent wants to remove the shortcut for \"{0}\".".Tr(this), KeybindingText.LabelOf(request.Target ?? ""))
                        : string.Format("The agent wants to set the shortcut for \"{0}\" to {1}.".Tr(this), KeybindingText.LabelOf(request.Target ?? ""), request.NewValue))
                    + (string.IsNullOrEmpty(request.SecondaryTarget) ? "" :
                        " " + string.Format("This also unbinds the shortcut of \"{0}\".".Tr(this), request.SecondaryTarget)),
                AgentWriteKind.RoutingChange => string.Format("The agent wants \"{1}\" to be the package that provides \"{0}\" (takes effect after a restart).".Tr(this), request.Target, request.NewValue),
                AgentWriteKind.ExtensionSettingChange => string.Format("The agent wants to change the extension setting \"{0}\" to {1}.".Tr(this), request.Target, request.NewValue),
                // 启停：整包与单个能力两种口径，开与关又各一句——四句都写全，不拿"设为 enable/disable"这种
                // 机器味的通用句糊过去（关掉一个能力等于让它从本次运行里消失，用户得一眼看懂关的是什么）。
                AgentWriteKind.ExtensionActivationChange => string.IsNullOrEmpty(request.SecondaryTarget)
                    ? (request.NewValue == "enable"
                        ? string.Format("The agent wants to enable the extension \"{0}\" (takes effect after a restart).".Tr(this), request.Target)
                        : string.Format("The agent wants to disable the extension \"{0}\" (takes effect after a restart).".Tr(this), request.Target))
                    : (request.NewValue == "enable"
                        ? string.Format("The agent wants to enable the \"{0}\" capability of \"{1}\" (takes effect after a restart).".Tr(this), request.Target, request.SecondaryTarget)
                        : string.Format("The agent wants to disable the \"{0}\" capability of \"{1}\" (takes effect after a restart).".Tr(this), request.Target, request.SecondaryTarget)),
                // 导出：卡片必须摆出【完整落地路径】——路径是任意的，用户只有看到它才能判断这一下写到哪。
                // 覆盖另起一句，别把"替换掉已有文件"混在同一句里说轻了。
                AgentWriteKind.ProjectExport => string.Format("The agent wants to export the project as {1} to:\n{0}".Tr(this), request.Target, request.NewValue),
                AgentWriteKind.ProjectExportOverwrite => string.Format("The agent wants to export the project as {1} to:\n{0}".Tr(this), request.Target, request.NewValue)
                    + "\n" + "A file already exists there and will be replaced. This can't be undone.".Tr(this),
                _ => string.Format("The agent wants to apply {0} change(s) to the project.".Tr(this), request.Count),
            },
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Colors.White.ToBrush(),
        };
        // 水平按钮排：用按内容自适应的 Border 按钮（TuneLab.GUI Button 是自绘 Component、不自量宽，水平摆会塌成 0 宽）。
        // 右对齐——操作按钮靠右、与左对齐的说明文字形成左右呼应，不再"全左对齐"。
        var buttons = new StackPanel() { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(0, 10, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var panel = new StackPanel() { Orientation = Orientation.Vertical };
        panel.Children.Add(message);
        panel.Children.Add(buttons);
        var card = new Border()
        {
            CornerRadius = new(6),
            Padding = new(10, 8),
            Margin = new(0, 6, 0, 2),
            Background = Style.INTERFACE.ToBrush(),
            BorderBrush = Style.BUTTON_PRIMARY.Opacity(0.5).ToBrush(),
            BorderThickness = new(1),
            Child = panel,
        };

        bool settled = false;
        // 裁决后的结果行：右对齐，落在按钮原位置，与卡片布局呼应。
        void AddOutcome(string label, IBrush color)
        {
            var line = NoticeLine(label, color);
            line.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            line.Padding = new(0, 0, 4, 0); // 斜体末字右侧斜出留白，避免右对齐时被 TextBlock 边界裁掉
            panel.Children.Add(line);
        }
        void Settle(ScriptAuthDecision decision, string label, IBrush color)
        {
            if (settled)
                return;
            settled = true;
            buttons.IsVisible = false;
            AddOutcome(label, color);
            tcs.TrySetResult(decision);
        }
        var muted = Style.LIGHT_WHITE.Opacity(0.6).ToBrush();
        // "应用本次"（非"应用"）：与右侧"始终允许"对照才说得清——本次落地、档位不变、下次仍问。
        buttons.Children.Add(CardButton("Apply once".Tr(this), primary: true, () => Settle(ScriptAuthDecision.ApplyOnce, "Applied".Tr(this), muted)));
        buttons.Children.Add(CardButton("Always allow".Tr(this), primary: false, () => { SetAuthorization(AgentAuthorization.Auto); Settle(ScriptAuthDecision.ApplyAlways, "Applied · auto-apply on".Tr(this), muted); }));
        buttons.Children.Add(CardButton("Reject".Tr(this), primary: false, () => Settle(ScriptAuthDecision.Reject, "Rejected".Tr(this), muted)));
        // 因取消先行 resolve（点停）→ 卡片切到"已停止"，不留可点按钮。
        tcs.Task.ContinueWith(_ =>
        {
            void Finish()
            {
                if (settled)
                    return;
                settled = true;
                buttons.IsVisible = false;
                AddOutcome("Stopped".Tr(this), Style.LIGHT_WHITE.Opacity(0.5).ToBrush());
            }
            if (Dispatcher.UIThread.CheckAccess()) Finish();
            else Dispatcher.UIThread.Post(Finish);
        }, TaskContinuationOptions.ExecuteSynchronously);

        return card;
    }

    // 出错条目：红字原因 + [复制] +（末轮才有）[重试]。作为 ctx.View 的独立子项（非塞进 turn 内），便于重试时就地降级、结果续在其后。
    // 重试开始不再【删掉】它：那等于抹掉"这里曾经失败过"的痕迹（而半截内容还留着、变得无从解释）。改为
    // 降级成留痕行（与重载时的呈现一致 → 显示 重载==实时）。
    // 【重试的有效期】只到"那次失败仍是对话末尾"为止：RetryAsync 是对当前上下文续跑，一旦用户又发了消息、
    // 末尾已换人，点它就不再是"重试那次"而是"从末尾再续一轮"（结果落在底部、历史归属还会错位）。故新一轮开始时
    // 由 RetireLiveErrorEntry 收掉按钮——与重载路径 allowRetry: i >= msgs.Count（只给最后一轮）同一口径。
    Control BuildErrorEntry(SessionContext ctx, ChatTurnMessage anchor, string? errorText, bool allowRetry)
    {
        var panel = new StackPanel() { Orientation = Orientation.Vertical };
        var notice = NoticeLine("Error: " + (errorText ?? string.Empty), Colors.IndianRed.ToBrush());
        panel.Children.Add(notice);
        var container = AssistantContainer(panel);
        // 右对齐：复制落在与普通消息分段复制同一条右边缘（统一按钮）；重试作为报错轮附加按钮排其左。
        var row = new StackPanel() { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new(0, 4, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        // retried=true（用户点了重试）→ 暗色 +「（已重试）」；false（对话已往下走、这次失败没被重试）→ 只收按钮、
        // 文字与配色不动（与重载时 allowRetry=false 的呈现一字不差）。
        bool retiredAlready = false;
        void Retire(bool retried)
        {
            if (retiredAlready)
                return;
            retiredAlready = true;
            row.IsVisible = false;
            if (retried)
            {
                notice.Text = RetiredErrorText(errorText ?? string.Empty);
                notice.Foreground = RetiredErrorBrush;
            }
        }
        if (allowRetry)
        {
            row.Children.Add(TextButton("Retry".Tr(this), () => OnRetry(ctx, anchor, () => Retire(retried: true))));
            ctx.RetireLiveErrorEntry?.Invoke();          // 同一会话若还挂着更早的可重试条目，先收掉它
            ctx.RetireLiveErrorEntry = () => Retire(retried: false);
        }
        row.Children.Add(TextButton("Copy".Tr(this), () => _ = CopyErrorAsync(errorText ?? string.Empty)));
        panel.Children.Add(row);
        return container;
    }

    // 中断轮的收场条目：中性提示 +（末轮才有）[继续]。刻意不是红字——没有技术错误可报，只是这一轮没等到答复，
    // 故与"已停止"同一档灰。[继续] 直接走 OnRetry → RetryAsync：不追加用户消息、对当前上下文续跑，正是此处所需。
    // 没有它用户只能再发一条消息，而那会让上下文里出现两条连续 user（等于自己把话说了两遍）。
    Control BuildInterruptedEntry(SessionContext ctx, ChatTurnMessage anchor, bool allowContinue)
    {
        var panel = new StackPanel() { Orientation = Orientation.Vertical };
        // 不写原因：可能是意外关闭、崩溃，也可能是别的什么——宿主无从知道（强杀时没有一行收尾代码跑得了），
        // 所以只陈述"这一轮没跑完"这个能确定的事实。
        var notice = NoticeLine(InterruptedText(), Style.LIGHT_WHITE.Opacity(0.5).ToBrush());
        panel.Children.Add(notice);
        var container = AssistantContainer(panel);
        if (allowContinue)
        {
            var row = new StackPanel() { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new(0, 4, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
            bool retiredAlready = false;
            void Retire(bool continued)
            {
                if (retiredAlready)
                    return;

                retiredAlready = true;
                row.IsVisible = false;   // 提示行留着（真相不抹），只收掉按钮
                if (!continued)
                    return;             // 对话自己往下走了：只收按钮，措辞不动（同 BuildErrorEntry 的 retried:false）

                // 点了[继续]：就地把提示降级成留痕（加"（已继续）"，与错误条目的"（已重试）"同一处置），
                // 并落一条 notice 记录。后者是必须的——续跑成功后这一轮就有了正常收尾，而本行提示是【推断】
                // 出来的（见 IsTurnComplete），重开时便不再成立、整行凭空消失，历史里就再没有线索说明中间
                // 为何断过。这与 RoleError"永不删"是同一条理由。落点即此刻轨迹末尾 = 真实的中断处，
                // 续跑的新内容自然排在它之后；即便续跑又失败，这条也依然如实（那里确实断过）。
                notice.Text = ContinuedText();
                if (ctx.Session == null)
                    return;

                ctx.Session.Messages.Add(new ChatTurnMessage { Role = ChatTurnMessage.RoleNotice, Text = ContinuedText() });
                AgentSessionStore.Save(ctx.Session);
            }
            row.Children.Add(TextButton("Continue".Tr(this), () => OnRetry(ctx, anchor, () => Retire(continued: true))));
            // 与错误条目共用这把"新一轮开始就收掉旧按钮"的钩子：用户若直接发新消息，这个 [继续] 也应失效
            //（那时末尾已换人，续跑的语义不再成立）。
            ctx.RetireLiveErrorEntry?.Invoke();
            ctx.RetireLiveErrorEntry = () => Retire(continued: false);
            panel.Children.Add(row);
        }
        return container;
    }

    // 中断提示的两种措辞（实时降级与重载渲染共用，故两处一字不差）：
    //  · 未处理 —— 推断出来的当前态，配 [继续] 按钮；
    //  · 已继续 —— 点过 [继续] 后的留痕，同时作为一条 notice 记录落盘，重开仍可见。
    // 只改措辞不改配色：灰字本就不扎眼，后缀已足以表明它是历史（红字错误才需要额外调暗）。
    string InterruptedText() => "No reply came back for this message — the turn didn't finish.".Tr(this);
    string ContinuedText() => InterruptedText() + " " + "(continued)".Tr(this);

    // 已被重试掉的那次失败：原位留痕的暗色一行（重载路径与实时降级共用同一措辞与配色）。
    Control RetiredErrorLine(string errorText) => NoticeLine(RetiredErrorText(errorText), RetiredErrorBrush);
    string RetiredErrorText(string errorText) => "Error: " + errorText + " " + "(retried)".Tr(this);
    static IBrush RetiredErrorBrush => Colors.IndianRed.Opacity(0.55).ToBrush();

    async Task CopyErrorAsync(string text)
    {
        var clipboard = TopLevel.GetTopLevel(mRoot)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(text);
    }

    // 纯文字按钮（无底色、灰→hover 白）：与脚注 / 分段 Copy 同款样式，用于错误条目的 复制 / 重试等。
    TextBlock TextButton(string text, Action onClick)
    {
        var t = new TextBlock
        {
            Text = text,
            FontSize = 11,
            Foreground = Style.LIGHT_WHITE.Opacity(0.45).ToBrush(),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        t.PointerEntered += (_, _) => t.Foreground = Colors.White.ToBrush();
        t.PointerExited += (_, _) => t.Foreground = Style.LIGHT_WHITE.Opacity(0.45).ToBrush();
        t.PointerPressed += (_, e) => { e.Handled = true; onClick(); };
        return t;
    }

    // 升级卡片的小按钮：按内容自适应的 Border（含文字 + padding + hover）。主按钮用主色，次按钮在深色卡片上用半透明白以保对比。
    Border CardButton(string text, bool primary, Action onClick)
    {
        var label = new TextBlock() { Text = text, FontSize = 12, Foreground = Colors.White.ToBrush(), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        var baseColor = primary ? Style.BUTTON_PRIMARY : Style.LIGHT_WHITE.Opacity(0.1);
        var hoverColor = primary ? Style.BUTTON_PRIMARY_HOVER : Style.LIGHT_WHITE.Opacity(0.2);
        var b = new Border()
        {
            CornerRadius = new(4),
            Padding = new(12, 5),
            Background = baseColor.ToBrush(),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = label,
        };
        b.PointerEntered += (_, _) => b.Background = hoverColor.ToBrush();
        b.PointerExited += (_, _) => b.Background = baseColor.ToBrush();
        b.PointerPressed += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }

    // ───────────────── 设置面板 dirty 追踪 / 返回 ─────────────────

    // 字段编辑 / provider 切换后重算脏态：与进入面板时的快照【值比对】（PropertyObject 深相等），
    // 故改一个值再改回去、数据一致时不算脏。mSuppressDirty 期间的程序化写入（如还原快照）不参与。
    void RecomputeSettingsDirty()
    {
        if (mSuppressDirty || mSettingsSnapshot == null)
            return;
        bool dirty = CurrentEngineType() != mProviderSnapshot || !mSettings.GetInfo().Equals(mSettingsSnapshot);
        if (dirty == mSettingsDirty)
            return;
        mSettingsDirty = dirty;
        RefreshSettingsDirtyMark();
    }

    void RefreshSettingsDirtyMark()
        => mSettingsTitleLabel.Content = "Model Settings".Tr(this) + (mSettingsDirty ? " *" : string.Empty);

    // × 返回：无改动直接回对话；有改动弹「应用/忽略」——应用=连接并落盘（失败留在设置页报错），忽略=还原到进入前再回对话。
    async void OnSettingsBack()
    {
        if (!mSettingsDirty)
        {
            ShowChat();
            return;
        }
        bool apply = await mRoot.ShowConfirm(
            "Model Settings".Tr(this),
            "You have unapplied changes. Apply them?".Tr(this),
            "Apply".Tr(this),
            "Ignore".Tr(this));
        if (apply)
            OnSubmit(); // 成功则内部 ShowChat + 提示已连接；失败则留在设置页显示报错、dirty 与 * 保留
        else
        {
            RestoreSettingsSnapshot();
            ShowChat();
        }
    }

    // 还原到进入设置面板前的状态：重载快照 provider 的已存设置（覆盖用户的未确认编辑）。
    // 依据不变量——每次退出面板都「应用(写盘)」或「忽略(还原)」，故进入时内存态恒等于盘上态，重载盘即回到进入态。
    void RestoreSettingsSnapshot()
    {
        mSuppressDirty = true;
        try
        {
            var type = mProviderSnapshot;
            if (!string.IsNullOrEmpty(type))
            {
                if (type != CurrentEngineType())
                {
                    mProviderData.SetValue(EngineKey, PropertyValue.Create(type)); // 触发 OnEngineSelectionChanged 载入其盘上设置
                    mProviderData.Commit();
                }
                else
                {
                    LoadProviderSettings(type); // provider 未变，直接从盘重载覆盖未确认编辑
                    RefreshEnginePropertyPanel(type);
                }
            }
        }
        finally { mSuppressDirty = false; }
        mSettingsDirty = false;
        RefreshSettingsDirtyMark();
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
            mSettingsDirty = false; // 已应用落盘：清脏（离开前顺带把 * 去掉）
            RefreshSettingsDirtyMark();
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
            // 聊天中途换模型不丢上下文：每个会话据其已记录对话（发送即落盘、逐轮维护）重建续聊历史，下次发送时新 runner 带它重建。
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
    // 走通用 ExtensionSettingsStore 的两级键 packageId → "agent-model:<id>"，每 provider 各一份
    //（适配器全是内建，故外层恒是内建包桶）。
    void SaveSettings(string type)
    {
        var engine = AgentModelManager.GetInitedEngine(type);
        if (engine == null)
            return;

        var config = engine.GetPropertyConfig(new PropertyContext(mSettings.GetInfo()));
        var secrets = ExtensionSettingsStore.PasswordKeys(config);
        // 只存当前 provider schema 里的字段，避免把切换前其他 provider 残留在 mSettings 的键写进本 provider 桶。
        ExtensionSettingsStore.Save(AgentModelManager.GetActivePackageId(type), "agent-model:" + type, FilterToConfig(mSettings.GetInfo(), config), secrets);

        Settings.AgentModelProvider.Value = type;
        Settings.Save(PathManager.SettingsFilePath);
    }

    // all 中属于 config 声明字段的子集（按当前 provider schema 过滤）。
    static PropertyObject FilterToConfig(PropertyObject all, ObjectConfig config)
    {
        var map = new Map<string, PropertyValue>();
        foreach (var kv in config.Properties)
            if (all.Map.TryGetValue(kv.Key.Id, out var v))
                map.Add(kv.Key.Id, v);
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
        // 进入面板：快照当前 provider + 全部字段值（还原基准 + 脏态比对基准）、清 dirty/*（此后编辑与快照不同才算脏）。
        mProviderSnapshot = CurrentEngineType();
        mSettingsSnapshot = mSettings.GetInfo();
        mSettingsDirty = false;
        RefreshSettingsDirtyMark();
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
        "You operate the project almost entirely by writing small JavaScript programs through the run_script tool (the `tl` object API): one script reads, computes and edits in a single step and runs as ONE undoable change. " +
        "Only act on the project when the user explicitly asks you to inspect or modify it; for greetings, small talk or non-requests, reply briefly in natural language and call no tool. " +
        "How to work: " +
        "(1) Call get_project_overview first to orient — it gives PPQ, tempo, time signature and every track with 1-based numbers and part/note counts. " +
        "(2) Before writing your first script in a conversation, call get_script_api once to load the full `tl` API, the handle/tick rules and examples — do not guess method names. " +
        "(3) Do everything else with run_script — reading detail (e.g. print(tl.currentPart().notes())), computing, and all edits. On error the whole run rolls back, so fix the script and re-run rather than patching from a half-applied state. " +
        "Coordinates: positions/durations are ABSOLUTE ticks (tl.ppq = ticks per quarter), pitch is MIDI; get_project_overview addressing is 1-based. " +
        "Editor state lives on `tl`: tl.currentPart() resolves \"this/the current part\", tl.selectedNotes()/tl.selectedParts() are the user's selection, tl.playhead() is \"here\". The grid is not where the playhead is — snap target ticks with tl.snap(...) when placing notes on the beat. " +
        "Vibrato is overlaid additively on the pitch line: when drawing a pitch line and adding vibrato over the same span, draw ONE continuous pitch line over the whole span and add vibrato on top (part.addVibrato) — never split the pitch line where the vibrato is; and do NOT use VibratoEnvelope to create vibrato (it only scales an existing one). " +
        "ALWAYS narrate in natural language what each script does, and why, before or after running it, so the user follows your actions without reading code. " +
        "NEVER announce an action and then stop: if you say you will do something, do it in the SAME reply by calling the tool — a reply that only promises leaves the user waiting and having to nudge you. " +
        "Writes may be gated by the user's authorization setting: every tool result states plainly whether it applied, was refused, or was approved by the user in a confirmation card. Trust that text — never ask the user whether a dialog appeared or whether your change went through. " +
        "When the user wants a REUSABLE feature they can run again from a menu (\"add a menu item that …\", \"make me a tool to …\"), or a one-off they would clearly want repeatedly, author a script tool — define getScriptInfo() + main() (see get_script_api) — and save it with save_script; it registers into the matching menu (top Scripts, or the note / part / piano-blank right-click) for one-click reuse. Use list_scripts / read_script / delete_script to manage them. " +
        "If they also want a keyboard shortcut for it — or for any command — call list_keybindings to find a free gesture, then set_keybinding; a saved script's command id is \"script:<its id>\". " +
        "For questions about TuneLab's own settings (\"where do I change …\", \"how do I set …\"), call list_settings and tell the user the Settings page and row label in their language; call set_setting only when they want YOU to change it for them. " +
        // 导出：两条最容易误导用户的边界写死在提示里——①导出不是保存（说错会让用户以为工程已存盘）；②音频导出是人在环
        // 决定（渲染要锁界面几分钟），agent 不代按，只把用户领到导出面板。
        "To write the project out to a file (project/MIDI formats) use export_project — but it exports a COPY: never tell the user you \"saved\" their project, since their save file and unsaved changes are untouched. " +
        "You CANNOT export audio (wav/mp3/flac/ogg): rendering locks the UI for as long as it takes, so that call is the user's to make — point them at the Export side panel and, if useful, get the settings ready first instead of trying to do it yourself. " +
        "When a plugin \"doesn't work\", diagnose in this order and do NOT stop at the first step: list_extensions (load status + error + whether one of its identities is SHADOWED by another package — loaded is not the same as used), then list_extension_routing if anything is contested, then list_sound_sources / list_effects to confirm the capability itself is there. " +
        "Earlier tool results are stale snapshots (the user or your own edits may have changed things); re-read with get_project_overview or a script before relying on current counts or values.";

    readonly Panel mRoot = new();
    readonly DockPanel mChatView = new() { LastChildFill = true };
    readonly DockPanel mSettingsView = new() { LastChildFill = true };
    // 可见会话的消息滚动区挂载点：切换会话只换其 Child 为目标会话各自的 ListView（离屏会话的视图被其 SessionContext 持有、不销毁）。
    readonly Panel mMessagesHost = new();
    readonly MultilineTextInput mInput = new();
    // token 用量状态行（输入框上方）：显示当前会话的累计 + 上下文占用，随会话切换/每轮刷新（见 RefreshTokenStatus）。
    readonly TextBlock mTokenStatus = new();
    // 图片附件：待发缩略图条 + 待发图片列表（属"当前撰写"状态、与输入框共享、跨会话切换保留）+ 📎按钮。
    readonly StackPanel mAttachmentStrip = new();
    readonly List<PendingImage> mPendingImages = new();
    // 轮边界插话 chip（钉在输入框上方）：mPendingChip=容器、mPendingPreview=文本预览。内容来自 mActive.PendingText（见 RefreshPendingChip）。
    readonly Border mPendingChip = new();
    readonly TextBlock mPendingPreview = new();
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
    // agent 写授权胶囊（对话页 header，即时生效）：胶囊按钮 + 短名标签 + 三选一 flyout。
    readonly TextBlock mAuthLabel = new() { FontSize = 11, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.Opacity(0.85).ToBrush() };
    Border mAuthButton = null!;
    Flyout mAuthFlyout = null!;
    // 设置面板标题（有未应用改动时补 *）+ dirty 状态。
    readonly Label mSettingsTitleLabel = new() { FontSize = 12, Margin = new(8, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Foreground = Style.LIGHT_WHITE.ToBrush() };
    bool mSettingsDirty;
    bool mSuppressDirty;         // 程序化写入（还原快照）期间不置脏
    string mProviderSnapshot = string.Empty; // 进入面板时的 provider（× 忽略时的还原基准 + 脏态比对基准）
    PropertyObject? mSettingsSnapshot;       // 进入面板时的全部字段值（脏态与快照【值比对】：改回原值即不脏）
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
    IReadOnlyList<ComboBoxItem> mEngineOptions = [];
    const string EngineKey = "provider";
    IProject? mProject;
    Func<IMidiPart?>? mCurrentPartProvider;
    Func<IQuantization?>? mQuantizationProvider;
    Func<ScriptSelection?>? mSelectionProvider;
    Func<ScriptPianoSelection?>? mPianoSelectionProvider;
    IReadOnlyList<IAgentTool> mTools = [];
    OverlayScrollBars? mSettingsScrollBars;   // 设置区浮层滚动条（存引用防 GC）
    IAgentModelSession? mSession;

    // ───────────────── 多会话并行 ─────────────────

    // 每会话各自的管线 + 视图状态。切换会话不取消、不清空——只换 mMessagesHost 显示的视图；
    // 离屏会话的 runner/请求继续在后台跑，流式事件仍写进其（脱离视觉树但被本对象持有的）ListView，切回即见进度。
    sealed class SessionContext
    {
        public readonly ListView View;          // 该会话独立的消息滚动区（离屏时仍保留，承载进行中的占位/分步气泡）
        public readonly ScrollBar Scrollbar;    // 绑该 View 竖轴的浮层滚动条，与 View 一同放进 mMessagesHost（覆盖层、只手柄可点）
        public ChatSession? Session;             // 落盘模型（null=尚未落盘的新对话，首轮成功后建立）
        public AgentRunner? Runner;              // 该会话的对话主循环（持有累积的对话历史）
        public AgentTurnView? CurrentTurn;       // 本轮的分步视图（在跑时非空）：升级卡片插入它以与工具步骤同序
        public CancellationTokenSource? Cts;     // 该会话当前在飞请求的取消源（停止键 / 删除该会话时触发）
        public List<AgentMessage>? SeedHistory;  // 加载已存会话 / 中途换模型后用于重建 runner 的历史（仅对话文本）
        // 本轮轨迹【已落盘】到第几条（runner 的 CurrentTrajectory 下标）。轨迹每追加一条即增量落盘（见 FlushTrajectory），
        // 故轮终态的三条路径（CompleteTurn / ResolveTurn / MarkTurnOutcome）只能补写水位之后的部分，否则会重复。
        // 每轮开始归 0（trajectory 是每轮新建的列表）。
        public int PersistedTurnMessages;
        public bool Busy;                        // 该会话是否有在飞请求（决定切到它时显示发送键还是停止键）
        public string? PendingText;              // 生成期间用户累积的"轮边界插话"待发缓冲（runner 到边界吃掉）；仅忙碌态非空，绑定本会话那次运行
        public long CumulativeTokens;            // 会话累计 token（每轮 total 之和，含工具往返重复前缀；状态行用）
        public int ContextTokens;                // 当前上下文占用（最后一次模型调用的输入+输出 ≈ 当前上下文大小；状态行用）
        public string Title = "New Chat";        // 该会话标题（切到它时写入头部标签）
        public bool TitleManual;                 // 标题是否被用户手动改过：true 则不再被自动标题覆盖
        public long CreatedAtUnix;               // 会话建立时刻（本地新建=当时；加载已存=其原始创建时刻）。菜单按它降序排，位置稳定、新会话在顶
        // 自动跟随底部的状态（per-session）：AutoFollow 只由"用户是否主动移动视野"翻转（见 OnMessagesAxisChanged），
        // 三个 Last* 是上次轴通知的快照，用来区分"用户滚了"与"内容长高/视野改尺寸"。
        public bool AutoFollow = true;
        public double LastViewOffset, LastContentLength, LastViewLength;
        // 当前挂着[重试]按钮的那条错误条目的"收按钮"回调（只可能有一条：恒为对话末尾那次失败）。
        // 新一轮开始（用户又发消息 / 点了重试）即调用它——重试只在"那次失败仍是末尾"时才有意义。
        public Action? RetireLiveErrorEntry;
        public SessionContext(ListView view)
        {
            View = view;
            Scrollbar = new ScrollBar(view.VerticalAxis, Orientation.Vertical);
        }
    }

    readonly List<SessionContext> mContexts = new(); // 所有打开中的会话（含后台在跑的、未落盘的新对话）
    SessionContext mActive = null!;                  // 当前可见会话（构造期立即建立首个空白会话）
    // 当前正在跑的会话：OnSend 在 SendAsync 前埋入，顺 await 链流进共享工具，供升级卡片定位到"触发这一轮"的视图。
    readonly AsyncLocal<SessionContext?> mRunningContext = new();
}
