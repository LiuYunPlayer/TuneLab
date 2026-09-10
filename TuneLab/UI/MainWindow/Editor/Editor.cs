using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.Animation;
using TuneLab.Data;
using TuneLab.Data.Synthesis;
using TuneLab.Audio;
using TuneLab.SDK;
using Timer = System.Timers.Timer;
using Avalonia.Controls;
using System.Threading;
using Avalonia;
using TuneLab.GUI.Components;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Input;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Platform.Storage;
using System.IO;
using TuneLab.GUI.Input;
using TuneLab.Input;
using KeyBinding = TuneLab.GUI.Input.KeyBinding;   // 消歧：Avalonia.Input 也有 KeyBinding
using System.Diagnostics;
using static TuneLab.GUI.Dialog;
using TuneLab.Utils;
using TuneLab.Extensions;
using System.IO.Compression;
using System.Xml.Linq;
using System.Text.Json;
using TuneLab.I18N;
using TuneLab.Configs;
using TuneLab.Scripting;
using Splat;
using System.Reactive.Joins;
using System.Runtime.InteropServices;

using TuneLab.Extensions.Formats;
using TuneLab.Extensions.Formats.TLP;
using TuneLab.Extensions.Instruments;
using TuneLab.Extensions.Voices;
using TuneLab.Commands;
namespace TuneLab.UI;

internal class Editor : DockPanel, PianoWindow.IDependency, TrackWindow.IDependency, FunctionBar.IDependency
{
    public Menu Menu { get; }
    public TrackWindow TrackWindow => mTrackWindow;
    public PianoWindow PianoWindow => mPianoWindow;
    public ProjectDocument Document => mDocument;
    public Project? Project => mDocument.Project;
    public IPlayhead Playhead => mPlayhead;
    public IHolder<IProject> ProjectHolder => mDocument.ProjectHolder;
    public IHolder<IPart> EditingPart => mPianoWindow.PartHolder;
    public IHolder<IMidiPart> EditingPartHolder => mPianoWindow.PartHolder;
    public TickAxis PianoTickAxis => mPianoWindow.TickAxis;
    public PitchAxis PianoPitchAxis => mPianoWindow.PitchAxis;
    public INotifiableProperty<PianoTool> PianoTool { get; } = new NotifiableProperty<PianoTool>(UI.PianoTool.Note);
    public INotifiableProperty<PlayScrollTarget> PlayScrollTarget { get; } = new NotifiableProperty<PlayScrollTarget>(UI.PlayScrollTarget.None);
    public Editor()
    {
        Background = Style.BACK.ToBrush();
        Focusable = true;
        IsTabStop = false;
        mTrackWindowHeight = EditorState.TrackWindowHeight;
        // 两个纯转发的宿主能力（脚本面写选区 / 换工程文件）：只存 this、不碰任何还没建好的东西，
        // 故最先建——下面接线时它们必须已经在手（少了这一步，脚本面会拿到 null 并如实报"这里没有编辑器"，
        // 而这里明明有）。
        mSelectionWriter = new(this);
        mProjectFileAccess = new(this);
        mEditorViewAccess = new(this);

        mPlayhead = new(this);
        if (Enum.TryParse<PlayScrollTarget>(Settings.AutoScrollTarget.Value, out var autoScrollTarget))
        {
            PlayScrollTarget.Value = autoScrollTarget;
        }

        // 钢琴窗先于功能栏构造：FunctionBar 构造时即订阅 EditingPartHolder（颤音工具可用性）。
        mPianoWindow = new(this);// { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
        mFunctionBar = new(this);
        // agent 经此实时读取"当前编辑 part"（用户说"当前/这个 part"时解析序号）与当前量化（吸附网格）。
        // 宿主内每个入口（侧栏 agent、命令桥 / CLI attach）共用的执行环境：同一个工程、同一个编辑器态。
        // 用户开着界面让外部 agent 干活时，"当前 part"的自然含义就是他正在看的那个（docs/command-surface.md §5.2）。
        HostCommandContext.Provider = () => new CommandContext
        {
            Project = Project,
            Language = () => TranslationManager.CurrentLanguage.Value,
            EditorState = new EditorStateAccess(() => mPianoWindow.Part, () => mPianoWindow.Quantization, CurrentScriptSelection, CurrentPianoScriptSelection, mSelectionWriter),
            // 界面此刻的状态（走带 / 工具 / 面板 / 焦点面）：`editor status` 与 `action run` 的回报读它。
            // 即发即忘的动作（播放是切换、选工具）靠它才不瞎（见 IEditorStatusAccess）。
            // 形参里有三个同型的 Func<bool>，故一律具名传：错位一个不会编译失败，只会让状态报反。
            EditorStatus = new EditorStatusAccess(
                isPlaying: () => AudioEngine.IsPlaying,
                playheadTime: () => AudioEngine.CurrentTime,
                playheadTick: () => Project?.TempoManager.GetTick(AudioEngine.CurrentTime),
                endTime: () => AudioEngine.EndTime,
                currentToolActionId: () => ToolActionId(mPianoWindow.PianoTool.Value),
                isParameterPanelVisible: () => mPianoWindow.IsParameterPanelVisible,
                isWaveformVisible: () => mPianoWindow.IsWaveformVisible,
                sidebarPanelActionId: () => SidebarActionId(mRightSideTabBar.SelectedTab.Value),
                focusedSurface: () => mTrackWindow.IsKeyboardFocusWithin ? "arrangement" : mPianoWindow.IsKeyboardFocusWithin ? "pianoRoll" : null,
                // 挡在界面前面的东西：模态框期间命令桥照常应答，不报这一条就会有"用户什么都看不见，
                // 而外部条条回报成功"的一串命令（见 BlockingUi）。
                blockingDialogs: BlockingUi.Current),
            // 换工程要连带换撤销栈 / 播放头 / 钢琴窗里开着的 part，那些只有 Editor 管得了（见 IProjectFileAccess）。
            ProjectFile = mProjectFileAccess,
            // 挪视野：把用户的视线带到 agent 说的那个地方去（见 IEditorViewAccess）。
            EditorView = mEditorViewAccess,
            MainThread = UiThreadDispatcher.Instance,
        };
        mScriptSideBarContentProvider.SetCurrentPartProvider(() => mPianoWindow.Part);
        mScriptSideBarContentProvider.SetQuantizationProvider(() => mPianoWindow.Quantization);
        mScriptSideBarContentProvider.SetSelectionProvider(CurrentScriptSelection);
        mScriptSideBarContentProvider.SetPianoSelectionProvider(CurrentPianoScriptSelection);
        mScriptSideBarContentProvider.SetSelectionWriter(mSelectionWriter);
        // 用户脚本工具菜单的访问器（顶部 Scripts 菜单 + 各右键菜单共用）：工程随新建/打开切换，故传访问器。
        ScriptToolMenu.Init(() => Project, () => mPianoWindow.Part, () => mPianoWindow.Quantization, CurrentScriptSelection, CurrentPianoScriptSelection, mSelectionWriter);
        mTrackWindow = new(this);
        mRightSideTabBar = new();
        mRightSideBar = new() { Width = 320 };   // 左缘分隔线由 SideBar 自己画（见其构造）

        var panel = new DockPanel() { Background = Style.INTERFACE.ToBrush() };
        {
            // 页签竖条的左缘分隔线：侧栏收起时它直接挨着内容区，同样不能靠露底色的缝分界。
            panel.AddDock(new Border() { Width = 1, Background = Style.DARK.ToBrush() }, Dock.Left);
            var hoverBack = Colors.White.Opacity(0.05);
            var settingsButton = new GUI.Components.Button() { Width = 48, Height = 48 }
            .AddContent(new() { Item = new IconItem() { Icon = Assets.Settings, Scale = 4.0 / 3.0 }, ColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5), HoveredColor = Colors.White, PressedColor = Colors.White } });
            settingsButton.SetAction("app.settings");
            panel.AddDock(settingsButton, Dock.Bottom);
            panel.AddDock(mRightSideTabBar);
        }
        this.AddDock(panel, Dock.Right);

        // 侧栏 + 拖拽手柄同层，该层 ZIndex 抬到 track/piano(默认0) 之上：手柄既满高覆盖侧栏左缘（含与 TrackView 交界段），
        // 又能向左探出、压在内容区之上（缝两侧都可抓）；侧栏列本与内容不重叠，抬 ZIndex 仅让手柄探出那条压住内容。
        // 侧栏隐藏时手柄随之隐藏（可见性绑定）。
        mRightSideBar.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        mRightSideBar.IsVisible = false;
        var sideBarLayer = new Panel() { ZIndex = 1 };
        sideBarLayer.Children.Add(mRightSideBar);

        var sideBarResizer = new Border()
        {
            Width = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            Margin = new(-4, 0, 0, 0), // 左探 4px 到内容区、右留 4px 在侧栏 → 跨缝 ±4px（靠该层 ZIndex 压在内容之上才命中）
            Background = Avalonia.Media.Brushes.Transparent,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.SizeWestEast),
        };
        // 悬浮高亮：居中一条 2px 细线压在接缝上（命中区 8px、可见高亮仅 2px），悬浮约 300ms 后显色（仿 VSCode sash），
        // 移开/松手即隐、拖动中保持显色。
        var resizerLine = new Border() { Width = 4, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        sideBarResizer.Child = resizerLine;
        // 高亮用 highlight color；用封装的 AnimationColor 做淡入淡出（仅动 alpha、保持色相干净），仿 VSCode sash 过渡。
        var resizerHi = Style.HIGH_LIGHT;
        var resizerHiClear = new Avalonia.Media.Color(0, resizerHi.R, resizerHi.G, resizerHi.B);
        var resizerLineBrush = new SolidColorBrush(resizerHiClear);
        resizerLine.Background = resizerLineBrush;
        var resizerLineColor = new AnimationColor() { Value = resizerHiClear };
        resizerLineColor.ValueChanged += () => resizerLineBrush.Color = resizerLineColor.Value;
        void ShowResizerLine() => resizerLineColor.SetTo(resizerHi, 130, AnimationCurve.QuadOut);
        void HideResizerLine() => resizerLineColor.SetTo(resizerHiClear, 130, AnimationCurve.QuadOut);
        var resizerHoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        resizerHoverTimer.Tick += (_, _) => { resizerHoverTimer.Stop(); ShowResizerLine(); };
        bool resizing = false;
        double resizeStartX = 0, resizeStartWidth = 0;
        sideBarResizer.PointerEntered += (_, _) => resizerHoverTimer.Start();
        sideBarResizer.PointerExited += (_, _) => { resizerHoverTimer.Stop(); if (!resizing) HideResizerLine(); };
        sideBarResizer.PointerPressed += (_, e) =>
        {
            resizing = true;
            resizeStartX = e.GetPosition(this).X;
            resizeStartWidth = mRightSideBar.Width;
            resizerHoverTimer.Stop();
            ShowResizerLine(); // 拖动即显色
            e.Pointer.Capture(sideBarResizer);
        };
        sideBarResizer.PointerMoved += (_, e) =>
        {
            if (!resizing)
                return;
            var dx = e.GetPosition(this).X - resizeStartX;
            mRightSideBar.Width = Math.Clamp(resizeStartWidth - dx, 240, 640);
        };
        sideBarResizer.PointerReleased += (_, e) =>
        {
            resizing = false;
            e.Pointer.Capture(null);
            if (!sideBarResizer.IsPointerOver) HideResizerLine();
        };
        sideBarResizer.Bind(Avalonia.Visual.IsVisibleProperty, mRightSideBar.GetObservable(Avalonia.Visual.IsVisibleProperty));
        sideBarLayer.Children.Add(sideBarResizer); // 后加 → 在侧栏之上

        this.AddDock(sideBarLayer, Dock.Right);

        this.AddDock(mTrackWindow, Dock.Top);
        this.AddDock(mFunctionBar, Dock.Top);
        this.AddDock(mPianoWindow);

        MinHeight = mFunctionBar.Height;

        mFunctionBar.Moved += y =>
        {
            TrackWindowHeight = y;
            EditorState.TrackWindowHeight.Value = mTrackWindowHeight;
        };
        ProjectHolder.WillModify.Subscribe(OnProjectWillChange, s);
        ProjectHolder.Modified.Subscribe(OnProjectChanged, s);
        // 在编 part 被摘除（移动/重排会先 Remove 再 Insert）时暂存到 mDetachedEditingPart——SwitchEditingPart(null)
        // 会清空 mEditingPart，故不能再拿它判断复位；待同一 part（或其轨道）重新插入时据此复位，避免钢琴窗变空。
        // 复位成功才清空暂存：多 part/多轨同时挪动时，别让无关的插入提前清掉它。
        ProjectHolder.When(project => project.Tracks.WhenAny(track => track.Parts.ItemRemoved)).Subscribe(part => { if (part == mEditingPart) { mDetachedEditingPart = mEditingPart; SwitchEditingPart(null); } });
        ProjectHolder.When(project => project.Tracks.WhenAny(track => track.Parts.ItemAdded)).Subscribe(part => { if (mDetachedEditingPart != null && part == mDetachedEditingPart) { SwitchEditingPart(mDetachedEditingPart); mDetachedEditingPart = null; } });
        ProjectHolder.When(project => project.Tracks.ItemRemoved).Subscribe(track => { if (track.Parts.Contains(mEditingPart)) { mDetachedEditingPart = mEditingPart; SwitchEditingPart(null); } mExportSideBarContentProvider.RefreshTrackList(); });
        ProjectHolder.When(project => project.Tracks.ItemAdded).Subscribe(track => { if (mDetachedEditingPart != null && track.Parts.Contains(mDetachedEditingPart)) { SwitchEditingPart(mDetachedEditingPart); mDetachedEditingPart = null; } mExportSideBarContentProvider.RefreshTrackList(); });
        ProjectHolder.When(project => project.Tracks.WhenAny(track => track.Name.Modified)).Subscribe(() => mExportSideBarContentProvider.RefreshTrackList());
        mPianoWindow.PartHolder.Modified.Subscribe(() => { mPianoWindow.IsVisible = mPianoWindow.Part != null; mNotePropertySideBarContentProvider.SetPart(mPianoWindow.Part); UpdatePartPanelTarget(); }, s);
        // instrument 音源无颤音系统：编辑 part 切换 / 音源就地换种类时，颤音工具自动退回音符工具（工具栏按钮同步置灰，见 FunctionBar）。
        void EnsurePianoToolAvailable() { if (PianoTool.Value == UI.PianoTool.Vibrato && mPianoWindow.Part?.SoundSource.Kind == SourceKind.Instrument) PianoTool.Value = UI.PianoTool.Note; }
        mPianoWindow.PartHolder.Modified.Subscribe(EnsurePianoToolAvailable, s);
        mPianoWindow.PartHolder.When(part => part.SoundSource.Modified).Subscribe(EnsurePianoToolAvailable);

        // Part 面板焦点感知驱动：焦点在编排区且有选中 part → 显示选中集；否则显示钢琴窗当前编辑 part。
        // GotFocus（冒泡、含已处理）记录最近活跃的编辑区；选中变化 / 编辑 part 变化 / 焦点变化都触发重算。
        mTrackWindow.AddHandler(InputElement.GotFocusEvent, (_, _) => { mPartPanelFocusArea = PartPanelFocusArea.Arrangement; UpdatePartPanelTarget(); }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        mPianoWindow.AddHandler(InputElement.GotFocusEvent, (_, _) => { mPartPanelFocusArea = PartPanelFocusArea.Piano; UpdatePartPanelTarget(); }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        ProjectHolder.When(project => project.Tracks.WhenAny(track => track.Parts.WhenAny(part => part.SelectionChanged))).Subscribe(UpdatePartPanelTarget);
        mPartPropertySideBarContentProvider.TitleChanged += () => mRightSideBar.SetTitle(SideBarTab.PartProperties, mPartPropertySideBarContentProvider.Title);
        UpdatePartPanelTarget();

        mRightSideTabBar.SelectedTab.Modified.Subscribe(() =>
        {
            mRightSideBar.IsVisible = true;
            switch (mRightSideTabBar.SelectedTab.Value)
            {
                case SideBarTab.PartProperties:
                    mRightSideBar.SetContent(SideBarTab.PartProperties, mPartPropertySideBarContentProvider.Content);
                    break;
                case SideBarTab.NoteProperties:
                    mRightSideBar.SetContent(SideBarTab.NoteProperties, mNotePropertySideBarContentProvider.Content);
                    break;
                case SideBarTab.Extensions:
                    mExtensionSideBarContentProvider.RefreshExtensions();
                    mRightSideBar.SetContent(SideBarTab.Extensions, mExtensionSideBarContentProvider.Content);
                    break;
                case SideBarTab.Export:
                    mExportSideBarContentProvider.SetProject(Project);
                    mRightSideBar.SetContent(SideBarTab.Export, mExportSideBarContentProvider.Content);
                    break;
                case SideBarTab.Agent:
                    // 不在此调 SetProject：工程由 OnProjectChanged 统一维护（换工程才重建工具+重置会话）。
                    // tab 反复选中是冗余调用，会白白清空 agent 对话上下文，故只显示内容。
                    mRightSideBar.SetFullContent(SideBarTab.Agent, mAgentSideBarContentProvider.Icon, mAgentSideBarContentProvider.Name, mAgentSideBarContentProvider.Root);
                    break;
                case SideBarTab.Script:
                    mScriptSideBarContentProvider.SetProject(Project);
                    mRightSideBar.SetFullContent(SideBarTab.Script, mScriptSideBarContentProvider.Icon, mScriptSideBarContentProvider.Name, mScriptSideBarContentProvider.Root);
                    break;
                default:
                    mRightSideBar.IsVisible = false;
                    break;
            }
        });
        mRightSideBar.SetContent(SideBarTab.PartProperties, mPartPropertySideBarContentProvider.Content);

        mExtensionSideBarContentProvider.InstallRequested += async () =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
                return;
            var files = await topLevel.OpenFilePickerTracked(new FilePickerOpenOptions
            {
                Title = "Open Tlx File",
                AllowMultiple = true,
                FileTypeFilter = [new("TuneLab Extension") { Patterns = ["*.tlx"] }]
            });
            if (files.IsEmpty()) return;
            var fileList = files.Select(f => f.TryGetLocalPath()).Where(f => f != null).ToArray();
            if (fileList != null) InstallExtensions(fileList);
        };

        mExportSideBarContentProvider.SetDocument(mDocument);
        mExportSideBarContentProvider.ExportRequested += OnExportRequested;

        AddHandler(DragDrop.DropEvent, OnDrop);

        RegisterActions();
        Menu = CreateMenu();

        mFunctionBar.GotFocus += (s, e) => { mPianoWindow.PianoScrollView.Focus(); };
        mFunctionBar.QuantizationChanged.Subscribe(mPianoWindow.Quantization.Set);
        mFunctionBar.QuantizationChanged.Subscribe(mTrackWindow.Quantization.Set);
        mDocument.StatusChanged += () => { mUndoMenuItem.IsEnabled = mDocument.Undoable(); mRedoMenuItem.IsEnabled = mDocument.Redoable(); };
        // 「存回原位」只在崩溃恢复态可见。ProjectNameChanged 覆盖了所有会改变这个状态的时机
        //（SetRecovered 令其可见、SetSavePath / SetProject 令其消失）。
        mDocument.ProjectNameChanged.Subscribe(() =>
        {
            if (mSaveRecoveredMenuItem != null)
                mSaveRecoveredMenuItem.IsVisible = !string.IsNullOrEmpty(mDocument.RecoveredOriginalPath);
        }, s);
        mAutoSaveTimer.Tick += (s, e) => { AutoSave(); };
        Settings.AutoSaveInterval.Modified.Subscribe(() => mAutoSaveTimer.Interval = new TimeSpan(0, 0, Settings.AutoSaveInterval), s);
        PlayScrollTarget.Modified.Subscribe(() => Settings.AutoScrollTarget.Value = PlayScrollTarget.Value.ToString(), s);
        PathManager.MakeSureExist(PathManager.AutoSaveFolder);
        RecentFilesManager.Init();

        NewProject();
        CheckUpdate();
    }

    ~Editor()
    {
        s.DisposeAll();
    }

    public void SwitchEditingPart(IPart? part)
    {
        mLastPart = mEditingPart;
        mEditingPart = part;
        if (part == null)
        {
            mPianoWindow.Part = null;
        }
        else if (part is IMidiPart midiPart)
        {
            mPianoWindow.Part = midiPart;
        }
    }

    // 焦点感知地把目标 part 集下发给 Part 侧栏；合并一拍内的多次触发（框选时每个 part 的 SelectionChanged 都触发）。
    void UpdatePartPanelTarget()
    {
        if (mPartTargetUpdatePending)
            return;
        mPartTargetUpdatePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            mPartTargetUpdatePending = false;
            UpdatePartPanelTargetNow();
        });
    }

    void UpdatePartPanelTargetNow()
    {
        var selected = Project?.Tracks.SelectMany(track => track.Parts).OfType<IMidiPart>().Where(part => part.IsSelected).ToList() ?? new List<IMidiPart>();
        if (mPartPanelFocusArea == PartPanelFocusArea.Arrangement && selected.Count > 0)
            mPartPropertySideBarContentProvider.SetParts(selected, PartPanelSource.Selected);
        else if (mPianoWindow.Part is { } editing)
            mPartPropertySideBarContentProvider.SetParts(new[] { editing }, PartPanelSource.Current);
        else
            mPartPropertySideBarContentProvider.SetParts(Array.Empty<IMidiPart>(), PartPanelSource.Current);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        mTrackWindow.Height = TrackWindowHeight;
        EditorState.TrackWindowHeight.Value = mTrackWindowHeight;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.IsHandledByTextBox())
            return;

        e.Handled = Keymap.TryHandle(KeyScope.Editor, e);
    }

    // Editor 作用域的内置动作 + 它们的默认手势。动作本体进 ActionRegistry（穷尽面：命令面的 `action run`
    // 从那里触发），手势与作用域留在 Keymap（可绑那一半）；两层的关系见 EditorAction 与
    // docs/keybinding-system.md。
    //
    // 【Unavailable 为什么值得逐条填】这些 Execute 从前各自带静默守卫（没工程就 return、没选中就 return），
    // 键盘路径下"按了没反应"用户看得见，可外部触发看不见——回报"已执行"而什么都没发生就是假装成功。
    // 判据上移到这里之后只有一份，键盘、菜单、命令面看到的是同一个答案。
    void RegisterActions()
    {
        // 文件动作全是 Destructive：新建/打开可能丢掉未保存的工作，保存往磁盘写字节，撤销栈都救不回。
        // 三条都可能弹出【需要人应答】的模态，故 Prompts 取判据而非常量——工程已保存时新建不问任何话。
        Keymap.Register(new() { Id = "file.new", DisplayName = () => "New".Tr(TC.Menu), Kind = ActionKind.Destructive, Prompts = () => !mDocument.IsSaved, Execute = NewProject }, KeyScope.Editor, new(Key.N, KeyBinding.PrimaryModifier));
        Keymap.Register(new() { Id = "file.open", DisplayName = () => "Open".Tr(TC.Menu), Kind = ActionKind.Destructive, Prompts = () => true, Execute = OpenProject }, KeyScope.Editor, new(Key.O, KeyBinding.PrimaryModifier));
        // 没有可落地的路径时 save 自己转成 save-as（见 SaveProject），那就要弹文件选择器——如实照说。
        Keymap.Register(new() { Id = "file.save", DisplayName = () => "Save".Tr(TC.Menu), Kind = ActionKind.Destructive, Prompts = () => !HasSaveTarget, Execute = () => { _ = SaveProject(); } }, KeyScope.Editor, new(Key.S, KeyBinding.PrimaryModifier));
        Keymap.Register(new() { Id = "file.saveAs", DisplayName = () => "Save As".Tr(TC.Menu), Kind = ActionKind.Destructive, Prompts = () => true, Execute = () => { _ = SaveProjectAs(); } }, KeyScope.Editor, new(Key.S, KeyBinding.PrimaryModifier | KeyModifiers.Shift));

        // 撤销/重做的可用性与「编辑」菜单的 IsEnabled 同源（ProjectDocument.Undoable/Redoable），故不会出现
        // 菜单灰着、命令面却回报"已执行"。
        Keymap.Register(new() { Id = "edit.undo", DisplayName = () => "Undo".Tr(TC.Menu), Kind = ActionKind.ProjectEdit, Unavailable = () => mDocument.Undoable() ? null : "there is nothing to undo", Execute = Undo }, KeyScope.Editor, new(Key.Z, KeyBinding.PrimaryModifier));
        Keymap.Register(new() { Id = "edit.redo", DisplayName = () => "Redo".Tr(TC.Menu), Kind = ActionKind.ProjectEdit, Unavailable = () => mDocument.Redoable() ? null : "there is nothing to redo", Execute = Redo }, KeyScope.Editor, new(Key.Y, KeyBinding.PrimaryModifier));
        // 剪贴板类动词是"通用动作"：编排区与钢琴窗共享同一个键，触发时按当前聚焦的编辑面路由（见 RouteEdit）。
        // 见 docs/keybinding-system.md §2。copy 与 selectAll 不动工程（只写剪贴板 / 只改选区，撤销栈里没有
        // 它们）故是 AppState；cut/paste/delete 进撤销栈，是 ProjectEdit。
        Keymap.Register(new() { Id = "edit.copy", DisplayName = () => "Copy".Tr(TC.Menu), Kind = ActionKind.AppState, Unavailable = EditSurfaceUnavailable, Execute = () => RouteEdit(p => p.CopySelection(), t => t.CopySelection()) }, KeyScope.Editor, new(Key.C, KeyBinding.PrimaryModifier));
        Keymap.Register(new() { Id = "edit.cut", DisplayName = () => "Cut".Tr(TC.Menu), Kind = ActionKind.ProjectEdit, Unavailable = EditSurfaceUnavailable, Execute = () => RouteEdit(p => p.CutSelection(), t => t.CutSelection()) }, KeyScope.Editor, new(Key.X, KeyBinding.PrimaryModifier));
        Keymap.Register(new() { Id = "edit.paste", DisplayName = () => "Paste".Tr(TC.Menu), Kind = ActionKind.ProjectEdit, Unavailable = EditSurfaceUnavailable, Execute = () => RouteEdit(p => p.PasteSelection(), t => t.PasteSelection()) }, KeyScope.Editor, new(Key.V, KeyBinding.PrimaryModifier));
        Keymap.Register(new() { Id = "edit.delete", DisplayName = () => "Delete".Tr(TC.Menu), Kind = ActionKind.ProjectEdit, Unavailable = EditSurfaceUnavailable, Execute = () => RouteEdit(p => p.DeleteSelection(), t => t.DeleteSelection()) }, KeyScope.Editor, new(Key.Delete));
        Keymap.Register(new() { Id = "edit.selectAll", DisplayName = () => "Select All".Tr(TC.Menu), Kind = ActionKind.AppState, Unavailable = EditSurfaceUnavailable, Execute = () => RouteEdit(p => p.SelectAllInPiano(), t => t.SelectAllInTrack()) }, KeyScope.Editor, new(Key.A, KeyBinding.PrimaryModifier));

        // 域 = 功能身份，不随分发作用域走（见 docs/keybinding-system.md §1.1）：transport 而非 editor.playback。
        // 显示名沿用工具栏（FunctionBar）既有措辞，与 Go to Start / Go to End 按钮一致。
        Keymap.Register(new() { Id = "transport.play", DisplayName = () => "Play/Pause".Tr(TC.Menu), Kind = ActionKind.AppState, Execute = ChangePlayState }, KeyScope.Editor, new(Key.Space));
        Keymap.Register(new() { Id = "transport.gotoStart", DisplayName = () => "Go to Start".Tr(TC.Menu), Kind = ActionKind.AppState, Unavailable = NoProject, Execute = GotoStart }, KeyScope.Editor, new(Key.Home));
        Keymap.Register(new() { Id = "transport.gotoEnd", DisplayName = () => "Go to End".Tr(TC.Menu), Kind = ActionKind.AppState, Unavailable = NoProject, Execute = GotoEnd }, KeyScope.Editor, new(Key.End));
        // 换正在编辑的 part 不动工程数据（撤销栈里没有它），故 AppState。
        Keymap.Register(new() { Id = "part.reopenLast", DisplayName = () => "Reopen Last Part".Tr(TC.Menu), Kind = ActionKind.AppState, Unavailable = ReopenLastPartUnavailable, Execute = () => SwitchEditingPart(mLastPart!), }, KeyScope.Editor, new(Key.Tab, KeyBinding.PrimaryModifier));

        // 域 = view（显示层开关）。参数面板折叠/恢复与拖到最低等价；在 Editor 分发以便钢琴窗/编排区焦点下均可触发。
        Keymap.Register(new() { Id = "view.toggleParameterPanel", DisplayName = () => "Toggle Parameter Panel".Tr(TC.Menu), Kind = ActionKind.AppState, Execute = () => mPianoWindow.ToggleParameterPanel() }, KeyScope.Editor, new(Key.P, KeyBinding.PrimaryModifier));

        // 用户手册（随包发布，见 ManualLibrary）。F1 是「帮助」的通行约定；Global 作用域 = 任何区域按下都开。
        // 手册窗非模态（开着也不挡别的动作），故不算 Prompts。
        Keymap.Register(new() { Id = "app.manual", DisplayName = () => "User Manual".Tr(TC.Menu), Kind = ActionKind.AppState, Execute = () => ManualWindow.Open(this.Window()) }, KeyScope.Global, new(Key.F1));

        // 显示名沿用工具栏（FunctionBar）既有措辞，复用其翻译、与工具栏保持一致。
        RegisterToolAction("tool.note", "Note Tool", Key.D1, UI.PianoTool.Note);
        RegisterToolAction("tool.pitch", "Pitch Pen", Key.D2, UI.PianoTool.Pitch);
        RegisterToolAction("tool.anchor", "Anchor Tool", Key.D3, UI.PianoTool.Anchor);
        // 显示名不带 Pitch：这支笔在音符区固定合成音高、在参数区固定配对回显，作用面不限于音高（见 SynthesisLock）。
        RegisterToolAction("tool.lock", "Locking Brush", Key.D4, UI.PianoTool.Lock);
        RegisterToolAction("tool.vibrato", "Vibrato Tool", Key.D5, UI.PianoTool.Vibrato);

        // ── 走带：Space 那条是【切换】（与工具栏按钮、与人的心理模型一致），另加一对【终态】动作。
        // 外部驱动不应为了"让它播"先问一次现在在不在播；两条都幂等，触发后的实情在回报的状态里
        //（见 IEditorStatusAccess）。二者不占手势：Space 已经在那儿了。
        ActionRegistry.Register(new() { Id = "transport.start", DisplayName = () => "Play".Tr(TC.Menu), Kind = ActionKind.AppState, Execute = AudioEngine.Play });
        ActionRegistry.Register(new() { Id = "transport.pause", DisplayName = () => "Pause".Tr(TC.Menu), Kind = ActionKind.AppState, Execute = AudioEngine.Pause });

        // ── 另两条走磁盘的文件动作（New/Open/Save/Save As 在本方法开头）。
        // 「存回原位」平时在菜单里根本不出现（只在崩溃恢复态可见）：判据取同一个事实，故外部得到的是
        // "现在没有可存回的原位置"，而不是静默什么都不做。它还要人确认覆盖，故 Prompts。
        ActionRegistry.Register(new()
        {
            Id = "file.saveToOriginal",
            DisplayName = () => "Save to Original Location".Tr(TC.Menu),
            Kind = ActionKind.Destructive,
            Unavailable = () => string.IsNullOrEmpty(mDocument.RecoveredOriginalPath)
                ? "this project did not come from a crash recovery, so there is no original location to save it back to"
                : null,
            Prompts = () => true,
            Execute = SaveRecoveredToOriginal,
        });
        // 【音频导出不进动作面】导出混音 / 导出单轨音频都不在这里，理由与 `project export`
        // 拒绝干音频导出同一条（见 ProjectExportCommand）：渲染期界面必须锁住好几分钟，
        // 而"要不要现在把机器占住"是用户的人在环决定。外部能做的是把参数备好（导出设置属于工程数据，
        // 脚本面可写），最后一下由用户按。「导出为<工程格式>」则已有那条命令（它能传路径）。

        // ── 应用与窗口。设置窗是非模态的（Show 而非 ShowDialog）：开着不挡别的动作，故不算 Prompts。
        Keymap.Register(new() { Id = "app.settings", DisplayName = () => "Settings".Tr(TC.Menu), Kind = ActionKind.AppState, Execute = () => SettingsWindow.Open(this.Window()) }, KeyScope.Global);
        // 这两条会启动【用户机器上的外部程序】（文件管理器 / 日志的默认打开方式）。它们在 TuneLab 里
        // 没有任何后果、也无需撤销，故仍是 AppState；"会弹出别的程序"这件事写在命令的文档里。
        ActionRegistry.Register(new() { Id = "app.openDataFolder", DisplayName = () => "Open TuneLab Folder".Tr(TC.Menu), Kind = ActionKind.AppState, Execute = () => ProcessHelper.OpenUrl(PathManager.TuneLabFolder) });
        ActionRegistry.Register(new() { Id = "app.openLog", DisplayName = () => "Open Log".Tr(TC.Menu), Kind = ActionKind.AppState, Execute = () => ProcessHelper.OpenFile(PathManager.LogFilePath) });
        // 两条都停在要人应答的模态上：检查更新弹结果卡片（有更新时是那张更新框），关于框要人关掉。
        ActionRegistry.Register(new() { Id = "app.checkUpdates", DisplayName = () => "Check for Updates...".Tr(TC.Menu), Kind = ActionKind.AppState, Prompts = () => true, Execute = () => CheckUpdate(false) });
        ActionRegistry.Register(new() { Id = "app.about", DisplayName = () => "About TuneLab".Tr(TC.Menu), Kind = ActionKind.AppState, Prompts = () => true, Execute = ShowAbout });

        // ── 侧栏页签：**show / hide 而不是 toggle**。
        // 页签按钮自己是 toggle（点开、再点同一个就关，手快），但动作面给终态：外部驱动不必先读一次
        // "现在开着哪个"才敢按，两次 show 同一个面的结果与一次相同。页签是单槽（一次只开一个面），
        // 故 hide 不需要"关哪个"的选择器。
        foreach (var panel in SidebarPanels)
            RegisterSidebarAction(panel.Id, panel.Name, panel.Tab);
        Keymap.Register(new() { Id = "sidebar.hide", DisplayName = () => "Hide Side Panel".Tr(TC.Menu), Kind = ActionKind.AppState, Execute = () => mRightSideTabBar.SelectedTab.Value = SideBarTab.None }, KeyScope.Editor);

        // ── 视图：波形带（参数区标题栏最左那个开关；显隐是全局编辑器态，见 EditorState.WaveformVisible）。
        // 钢琴窗没开 part 时整个窗都不显示，那个开关也就够不着 → 动作同样不可用。
        Keymap.Register(new()
        {
            Id = "view.toggleWaveform",
            DisplayName = () => "Toggle Waveform".Tr(TC.Menu),
            Kind = ActionKind.AppState,
            Unavailable = () => mPianoWindow.Part == null ? "no part is open in the piano roll, so there is no waveform lane to show" : null,
            Execute = () => mPianoWindow.SetWaveformVisible(!mPianoWindow.IsWaveformVisible),
        }, KeyScope.Editor);

        // ── 量化（吸附网格）：闭集 18 档，逐档一条终态动作——下拉里够得着的每一档外部都够得着（穷尽面的定义）。
        // id 与显示名都由 基数×细分 算出（1/12 就是三连的 1/4：3×4），故这里不再抄一张与下拉并行的表。
        foreach (var quantizationBase in QuantizationBases)
            foreach (var division in QuantizationDivisions)
                RegisterQuantizationAction(quantizationBase, division);
        // 「更细 / 更粗」在同一基数段内上下走一格（1/8→1/16、1/12→1/24）：**只有这一对值得占手势**，
        // 18 档终态不进 keymap——否则快捷键设置页被灌满，那正是两层注册表要避免的（见 EditorAction）。
        Keymap.Register(new() { Id = "quantization.finer", DisplayName = () => "Finer Quantization".Tr(TC.Menu), Kind = ActionKind.AppState, Unavailable = () => QuantizationStepUnavailable(true), Execute = () => StepQuantization(true) }, KeyScope.Editor);
        Keymap.Register(new() { Id = "quantization.coarser", DisplayName = () => "Coarser Quantization".Tr(TC.Menu), Kind = ActionKind.AppState, Unavailable = () => QuantizationStepUnavailable(false), Execute = () => StepQuantization(false) }, KeyScope.Editor);
    }

    // 下拉里那 18 档的两个轴：基数（1 = 二分、3 = 三连、5 = 五连）× 细分。次序与工具栏下拉一致，
    // 故动作的注册序（= `action list` 的呈现序、也是设置页组内序）与用户在界面上看到的次序相同。
    static readonly MusicTheory.QuantizationBase[] QuantizationBases = [MusicTheory.QuantizationBase.Base_1, MusicTheory.QuantizationBase.Base_3, MusicTheory.QuantizationBase.Base_5];
    static readonly MusicTheory.QuantizationDivision[] QuantizationDivisions = [MusicTheory.QuantizationDivision.Division_1, MusicTheory.QuantizationDivision.Division_2, MusicTheory.QuantizationDivision.Division_4, MusicTheory.QuantizationDivision.Division_8, MusicTheory.QuantizationDivision.Division_16, MusicTheory.QuantizationDivision.Division_32];

    // 侧栏六个面：页签 → 动作 id → 显示名。**注册与状态回报（SidebarActionId）共用这一份**，
    // 故不会出现"动作加了、状态里还报不出来"那种半截状态。
    static readonly (SideBarTab Tab, string Id, string Name)[] SidebarPanels =
    [
        (SideBarTab.PartProperties, "sidebar.showPart", "Part Panel"),
        (SideBarTab.NoteProperties, "sidebar.showNote", "Note Panel"),
        (SideBarTab.Agent, "sidebar.showAgent", "Agent Panel"),
        (SideBarTab.Script, "sidebar.showScript", "Script Panel"),
        (SideBarTab.Extensions, "sidebar.showExtensions", "Extensions Panel"),
        (SideBarTab.Export, "sidebar.showExport", "Export Panel"),
    ];

    // 两件事一次做完：进穷尽面，并且可绑——但**不预占默认手势**（这几个面常有人想给个键，键位归用户）。
    void RegisterSidebarAction(string id, string name, SideBarTab tab)
    {
        Keymap.Register(new()
        {
            Id = id,
            DisplayName = () => name.Tr(TC.Menu),
            Kind = ActionKind.AppState,
            Execute = () => mRightSideTabBar.SelectedTab.Value = tab,
        }, KeyScope.Editor);
    }

    void RegisterQuantizationAction(MusicTheory.QuantizationBase quantizationBase, MusicTheory.QuantizationDivision division)
    {
        int denominator = (int)quantizationBase * (int)division;
        ActionRegistry.Register(new()
        {
            Id = "quantization.1_" + denominator,
            // 分数字面量不进翻译（"1/16" 在每种语言里都是 "1/16"）。
            DisplayName = () => "1/" + denominator,
            Kind = ActionKind.AppState,
            Execute = () => mFunctionBar.SetQuantization(quantizationBase, division),
        });
    }

    // 「更细 / 更粗」= 细分 ×2 / ÷2，**基数不动**：三连档里更细一格是 1/12→1/24，而不是跳去二分那一段。
    void StepQuantization(bool finer)
    {
        var quantization = mPianoWindow.Quantization;
        int division = (int)quantization.Division;
        mFunctionBar.SetQuantization(quantization.Base, (MusicTheory.QuantizationDivision)(finer ? division * 2 : division / 2));
    }

    string? QuantizationStepUnavailable(bool finer)
    {
        int division = (int)mPianoWindow.Quantization.Division;
        if (finer && division >= (int)MusicTheory.QuantizationDivision.Division_32)
            return "the quantization is already at its finest division";
        if (!finer && division <= (int)MusicTheory.QuantizationDivision.Division_1)
            return "the quantization is already at its coarsest division";
        return null;
    }

    void RegisterToolAction(string id, string name, Key key, PianoTool tool)
    {
        Keymap.Register(new()
        {
            Id = id,
            DisplayName = () => name.Tr(TC.Menu),
            Kind = ActionKind.AppState,
            // instrument 音源无颤音系统：判据与工具栏按钮同口径。上移到这里之后，外部触发得到的是一句
            // "为什么切不了"，而不是静默不切。
            Unavailable = tool != UI.PianoTool.Vibrato ? null : () => mPianoWindow.Part?.SoundSource.Kind == SourceKind.Instrument
                ? "the part open in the piano roll uses an instrument sound source, which has no vibrato system"
                : null,
            Execute = () => mPianoWindow.PianoTool.Value = tool,
        }, KeyScope.Editor, new(key));
    }

    string? NoProject() => Project == null ? "no project is open" : null;

    // 当前工具报成【动作 id】，好让调用方把状态与 `action list` 里那几条切工具的动作直接对上。
    // 与 RegisterToolAction 的 id 是同一批字面量：多一支笔就要两处一起加，故就近放在一起。
    static string ToolActionId(PianoTool tool) => tool switch
    {
        UI.PianoTool.Note => "tool.note",
        UI.PianoTool.Pitch => "tool.pitch",
        UI.PianoTool.Anchor => "tool.anchor",
        UI.PianoTool.Lock => "tool.lock",
        _ => "tool.vibrato",
    };

    // 开着的侧栏面报成【动作 id】（同 CurrentToolActionId 的理由：调用方能直接与 `action list`
    // 里那六条 show 动作对上）。null = 侧栏此刻没开（SideBarTab.None）。
    static string? SidebarActionId(SideBarTab tab)
    {
        foreach (var panel in SidebarPanels)
            if (panel.Tab == tab)
                return panel.Id;
        return null;
    }

    // 有没有可直接落地的工程路径。没有（新工程，或路径已失效 / 不是本家格式）时 save 自己转成 save-as、
    // 于是会弹文件选择器——SaveProject 与 file.save 的 Prompts 判据共用这一份。
    bool HasSaveTarget => File.Exists(mDocument.Path) && Path.GetExtension(mDocument.Path) == "." + ConstantDefine.DefaultProjectExtension;

    // 剪贴板类动作的可用性：判据与 RouteEdit 的路由同源——先看有没有聚焦的编辑面，再看那个面此刻收不收
    // 编辑命令（鼠标正拖着东西时不收）。
    //
    // 【实测事实，别再想着绕】Avalonia 的 IsKeyboardFocusWithin **随窗口失活就变 false**（2026-09 实测：
    // 用户点过钢琴窗、再切回终端，这里立刻读到"两个面都没焦点"）。而外部驱动的常态恰恰是 TuneLab 不在
    // 前台，所以这批动词从命令面基本只在"用户正坐在 TuneLab 前"时可用。
    //
    // 【为什么不给它加"回落到最后用过的面"】那是把隐式上下文变成可猜的，正好违反命令面自己的规矩
    // （docs/command-surface.md §5.2：没有编辑器态时要求显式传参、不猜）。这批动词的正解是把**对象**说
    // 清楚——复制哪几个音符 / 哪几个 part、粘到哪个 part 的哪个 tick——那样连"哪个面"都不必问。而能表达
    // 对象引用的地方是脚本 API（那里 note / part 就是对象），不是只能传 id 的动作面。
    // 选区写入已经补上（tl.setTrackSelection / part.selectNotes / isSelected，issue #150 的后续项之一）；
    // 剪贴板则**刻意不暴露**（宿主内部剪贴板不是系统剪贴板，见 docs/action-coverage.md §3），故这批动词
    // 里只剩 copy/cut/paste 要么在脚本里按对象表达结果、要么请用户自己按键。
    string? EditSurfaceUnavailable()
    {
        if (mTrackWindow.IsKeyboardFocusWithin)
            return mTrackWindow.CanRunEditCommand ? null : "the arrangement is in the middle of an operation (something is being dragged), so it is not taking edit commands right now";
        if (mPianoWindow.IsKeyboardFocusWithin)
            return mPianoWindow.CanRunEditCommand ? null : "the piano roll is in the middle of an operation (something is being dragged), so it is not taking edit commands right now";
        return "neither the arrangement nor the piano roll has keyboard focus, so this action has nothing to act on — and keyboard focus goes away the moment TuneLab stops being the foreground window, which is the normal state while you drive it from outside. "
            + "These verbs mirror a keypress: they act on whatever is selected in the focused surface. From outside, say the objects explicitly instead — which notes, which parts, into which part at which tick — and that belongs in run_script (tl.*), which needs no focus. "
            + "run_script can already delete and edit notes and parts, and it can WRITE the selection (note/part/track isSelected, part.selectNotes, tl.setTrackSelection / setPianoSelection) — so \"select these for the user\" needs no focus either. "
            + "What it deliberately has no API for is the clipboard itself: for copy/cut/paste specifically, either express the outcome as objects in run_script (read the note infos, add them where they should go) or ask the user to press the shortcut in TuneLab.";
    }

    // 剪贴板类命令按当前键盘焦点路由到对应编辑面：焦点在编排区→track 动作、在钢琴窗→piano 动作。
    // 两面为兄弟节点，焦点至多落在其一，故不歧义。"都不在焦点"这一支由 EditSurfaceUnavailable 挡在前面，
    // 走不到这里（键盘路径上原本也只是空操作）。
    void RouteEdit(Action<PianoWindow> piano, Action<TrackWindow> track)
    {
        if (mTrackWindow.IsKeyboardFocusWithin)
            track(mTrackWindow);
        else if (mPianoWindow.IsKeyboardFocusWithin)
            piano(mPianoWindow);
    }

    // 上一个 part 还够不够得着（工程可能已经换掉、那个 part 可能被删了），以及此刻收不收命令。
    string? ReopenLastPartUnavailable()
    {
        if (mLastPart == null)
            return "no other part has been opened in this session, so there is no previous one to go back to";
        if (!mDocument.Pushable())
            return "the editor is in the middle of an operation, so it is not taking commands right now";
        var track = mLastPart.Track;
        return track.Parts.Contains(mLastPart) && track.Project.Tracks.Contains(track)
            ? null
            : "the part that was open before this one is no longer in the project";
    }

    void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.Data.GetFiles()?.Select(s => s.TryGetLocalPath()!).Where(s => s != null);
        if (files == null)
            return;

        List<string> tlxs = [];
        string? projectFile = null;

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file);
            if (extension == ".tlx")
            {
                tlxs.Add(file);
            }
            else if (extension == ".zip" && Path.GetFileName(file).StartsWith("【vsqx分享平台】"))
            {
                using ZipArchive zip = ZipFile.OpenRead(file);
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (entry.FullName.StartsWith("【调音者："))
                    {
                        using Stream stream = entry.Open();
                        var tempFilePath = Path.Combine(Path.GetTempPath(), entry.FullName);
                        using (var tempFileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                        {
                            stream.CopyTo(tempFileStream);
                        }
                        LoadProject(tempFilePath);
                        break;
                    }
                }
            }
            else if (FormatsManager.GetAllImportFormats().Contains(extension.TrimStart('.')))
            {
                projectFile = file;
            }
        }

        if (projectFile != null)
        {
            e.Handled = true;
            SwitchProjectSafely(() =>
            {
                LoadProject(projectFile);
            });
        }
        else if (!tlxs.IsEmpty())
        {
            e.Handled = true;
            InstallExtensions(tlxs);
        }
    }

    void OnProjectWillChange()
    {
        if (Project == null)
            return;

        SwitchEditingPart(null);
        StopAutoSynthesis();
        mAutoSaveTimer.Stop();
        ClearAutoSaveFile();
    }

    void OnProjectChanged()
    {
        if (Project == null)
            return;

        mExportSideBarContentProvider.SetProject(Project);
        mAgentSideBarContentProvider.SetProject(Project);
        mScriptSideBarContentProvider.SetProject(Project);
        // 工程就绪后重建 Scripts 菜单（菜单可能在首个工程加载前就建好、那时只有占位项）。
        mRebuildScriptsMenu?.Invoke();

        StartAutoSynthesis();
        mAutoSaveTimer.Start();

        if (Project.Tracks.Count == 0)
            return;

        foreach (var part in Project.Tracks.SelectMany(track => track.Parts))
        {
            if (part is MidiPart midiPart)
            {
                SwitchEditingPart(midiPart);
                break;
            }
        }
    }

    void StartAutoSynthesis()
    {
        if (mTimer != null)
            return;

        var context = SynchronizationContext.Current ?? throw new Exception("Can not get SynchronizationContext!");
        mTimer = new(50);
        mTimer.Elapsed += (s, e) => { context.Post(_ => SynthesisNext(), null); };
        mTimer.Start();
    }

    void StopAutoSynthesis()
    {
        if (mTimer == null)
            return;

        mTimer.Stop();
        mTimer.Dispose();
        mTimer = null;
    }

    // 宿主驱动逐步合成（仿 ACE findNextNeedSynthesisContext）：每个调度 tick 在并发上限内
    // 填满空槽。候选 = 各空闲会话的廉价 peek；全局按"播放线就近"排优先——先取播放线之后
    // 最早开始的段，线后全空再取线前最晚开始（离播放线最近）的段。
    // peek→commit 在本同步调用栈内完成（同一调度 tick，无编辑可插入，segment token 安全）。
    void SynthesisNext()
    {
        if (Project == null)
            return;

        int limit = EffectTaskGate.Limit;   // voice 与 effect 并行度同受 Settings.MaxParallelSynthesisTasks 统辖
        int busy = 0;
        var idle = new List<ISynthesisPipeline>();
        foreach (var track in Project.Tracks)
        {
            foreach (var part in track.Parts)
            {
                if (part is not MidiPart midiPart)
                    continue;

                var pipeline = midiPart.SynthesisPipeline;
                if (pipeline == null)
                    continue;

                if (pipeline.IsBusy)
                {
                    busy++;
                    continue;
                }

                if (midiPart.IsSynthesisBatching)
                    continue; // 批量编辑收口前不派活，避免对中间态做无用功

                idle.Add(pipeline);
            }
        }

        double currentTime = AudioEngine.CurrentTime;
        while (busy < limit && idle.Count > 0)
        {
            ISynthesisPipeline? best = null;
            SynthesisRange bestSegment = default;
            bool bestIsAhead = false;
            foreach (var pipeline in idle)
            {
                var peeked = pipeline.PeekNext(currentTime, double.MaxValue);
                bool isAhead = peeked != null;
                peeked ??= pipeline.PeekNext(double.MinValue, currentTime);
                if (peeked is not { } segment)
                    continue;

                bool better = best == null
                    || (isAhead && !bestIsAhead)
                    || (isAhead == bestIsAhead && (isAhead
                        ? segment.StartTime < bestSegment.StartTime
                        : segment.StartTime > bestSegment.StartTime));
                if (better)
                {
                    best = pipeline;
                    bestSegment = segment;
                    bestIsAhead = isAhead;
                }
            }

            if (best == null)
                break;

            mDispatchedThisTick.Add(best);   // 诊断：本轮真的派出去了，其停滞计时重置

            // 回传选中它的那次 peek 的同一窗口（ahead = [currentTime, +∞)，behind = (-∞, currentTime]），
            // 而非 bestSegment 自身——插件据此确定性重导出 peek 报出的同一块。
            if (bestIsAhead)
                best.Dispatch(currentTime, double.MaxValue);
            else
                best.Dispatch(double.MinValue, currentTime);
            idle.Remove(best);
            busy++;
        }

        ReportStalledParts(currentTime, busy, limit);
    }

    // 卡死诊断：某个 part 状态带上明明有「待合成 / 合成中」，却持续没有任何进展——这类故障**不抛异常**
    // （管线卡在在飞态、批量括号漏配平让调度器跳过、part 界把块裁在窗外、会话自报与可派活不一致），
    // 症状全都是「条不动」，光看日志分不出是哪一种。这里在每个调度 tick 判定，同一个 part 最多每
    // StallReportIntervalMs 打一行，把四者各自的量一次打全（见 ISynthesisPipeline.DescribeSchedulingState）。
    // 正常情况下永不触发：只要有派活或没有待办就重置计时。
    void ReportStalledParts(double currentTime, int busy, int limit)
    {
        if (Project == null)
        {
            mDispatchedThisTick.Clear();
            return;
        }

        long now = Environment.TickCount64;
        // 扫描本身每秒最多一次：调度 tick 是 50ms，逐 tick 给每个 part 建状态带列表会让诊断自己变成负担。
        // 代价只是停滞判定的时间分辨率降到 1 秒，而报告阈值是 10 秒，绰绰有余。
        // 被节流掉的 tick **不清**已派活集合：否则这一秒内派出去的活会被忘掉，扫描时误报成停滞。
        if (now - mLastStallScan < StallScanIntervalMs)
            return;
        mLastStallScan = now;
        mSeenParts.Clear();
        foreach (var track in Project.Tracks)
        {
            foreach (var part in track.Parts)
            {
                if (part is not MidiPart midiPart)
                    continue;

                mSeenParts.Add(midiPart);
                var pipeline = midiPart.SynthesisPipeline;
                if (pipeline == null)
                {
                    // 管线为 null = 状态带整条消失且永不合成（重建路径抛异常就会留下这个状态），故单列。
                    ReportStall(midiPart, now, busy, limit, "pipeline is null (no status strip, never synthesizes)");
                    continue;
                }

                bool hasWork = false;
                foreach (var segment in pipeline.GetStatus())
                {
                    if (segment.State is SynthesisDisplayState.Pending or SynthesisDisplayState.Synthesizing)
                    {
                        hasWork = true;
                        break;
                    }
                }

                if (!hasWork || mDispatchedThisTick.Contains(pipeline))
                {
                    mStallSince.Remove(midiPart);
                    continue;
                }

                ReportStall(midiPart, now, busy, limit, pipeline.DescribeSchedulingState(currentTime, double.MaxValue));
            }
        }

        mDispatchedThisTick.Clear();
        if (mStallSince.Count > mSeenParts.Count)
        {
            // part 被删除后清掉其记录（诊断字典不该拖住已消失的 part）
            foreach (var stale in mStallSince.Keys.Where(p => !mSeenParts.Contains(p)).ToList())
                mStallSince.Remove(stale);
        }
    }

    void ReportStall(MidiPart part, long now, int busy, int limit, string detail)
    {
        if (!mStallSince.TryGetValue(part, out var state))
        {
            mStallSince[part] = (now, 0);
            return;   // 首次发现只记时刻：绝大多数「这一轮没派活」是正常的（并发已满 / 刚提交）
        }

        double stalledSeconds = (now - state.Since) / 1000.0;
        if (stalledSeconds < StallReportThresholdSeconds || now - state.LastReport < StallReportIntervalMs)
            return;

        mStallSince[part] = (state.Since, now);
        Log.Warning($"Part at {part.Pos.Value} has pending synthesis but nothing was dispatched for {stalledSeconds:F1}s."
            + $" slots={busy}/{limit} {detail}");
    }

    const double StallReportThresholdSeconds = 10;
    const long StallReportIntervalMs = 30000;
    const long StallScanIntervalMs = 1000;
    long mLastStallScan;
    readonly HashSet<ISynthesisPipeline> mDispatchedThisTick = new();
    readonly HashSet<MidiPart> mSeenParts = new();
    readonly Dictionary<MidiPart, (long Since, long LastReport)> mStallSince = new();

    public void ClearAutoSaveFile()
    {
        mAutoSaveHead = default;
        AutoSaveStore.ClearSentinel();
    }

    async void AutoSave()
    {
        if (mDocument.Project == null || mDocument.IsSaved || mAutoSaveHead == mDocument.Head)
            return;

        var file = new NativeProjectFile
        {
            Project = mDocument.Project.GetInfo(),
            Editor = new EditorInfo { PlayheadPos = Playhead.Pos },
            Export = mDocument.Project.GetExportConfig(),
        };

        // 数据属性只在主线程读：文件名、原工程路径、轮换上限都先取出来再进后台。
        // 恢复出来的工程没有保存路径，但它当时的原路径仍是有意义的基准来源，要继续传下去，
        // 否则从"恢复态"再崩一次就彻底丢掉了原位置。
        var originalPath = string.IsNullOrEmpty(mDocument.Path) ? mDocument.RecoveredOriginalPath : mDocument.Path;
        // 展示名取【语言无关】的文件名，绝不用 mDocument.Name——工程未命名时它是本地化后的"未命名工程"，
        // 持久化下去会让换语言后的恢复显示旧语言的名字，还会把非 ASCII 文本带进文件名。
        // 工程从未保存过则留空，由恢复侧按【当前】语言渲染。
        var projectName = string.IsNullOrEmpty(originalPath) ? string.Empty : Path.GetFileName(originalPath);
        var maxCount = Settings.AutoSaveMaxCount.Value;

        try
        {
            await Task.Run(() =>
            {
                if (!FormatsManager.SerializeNative(file, ConstantDefine.DefaultProjectExtension, out var stream, out var error))
                {
                    Log.Error("Save file error: " + error);
                    return;
                }

                using (stream)
                {
                    AutoSaveStore.Write(stream.CopyTo, projectName, originalPath, maxCount);
                }
            });

            mAutoSaveHead = mDocument.Head;
            Log.Debug("Project auto saved");
        }
        catch (Exception ex)
        {
            Log.Debug("Write file error: " + ex);
        }
    }

    // 把崩溃恢复出来的工程写回它原来的位置。恢复态刻意不绑原路径（一次 Ctrl+S 不该用崩溃时的中间状态
    // 覆盖原文件），所以这是个必须由用户显式发起、且要确认的破坏性动作。
    public async void SaveRecoveredToOriginal()
    {
        var originalPath = mDocument.RecoveredOriginalPath;
        if (string.IsNullOrEmpty(originalPath))
            return;

        var modal = new Dialog();
        modal.SetTitle("Tips".Tr(TC.Dialog));
        // 目标完整路径必须显示出来：这是覆盖用户文件的动作，让他看得见指向的是不是自己那一份。
        modal.SetMessage("Replace this file with the recovered content?".Tr(TC.Dialog) + "\n" + originalPath);
        modal.AddButton("Cancel".Tr(TC.Dialog), ButtonType.Normal);
        modal.AddButton("Replace".Tr(TC.Dialog), ButtonType.Primary).Clicked += () =>
        {
            // 落地那刻重查：确认期间原文件可能已被移走 / 删除，此时不该凭空新建一个。
            if (!File.Exists(originalPath))
            {
                Log.Error("The original file no longer exists: " + originalPath);
                return;
            }

            SaveToFile(originalPath);
            RecentFilesManager.AddFile(originalPath);
        };
        modal.Topmost = true;
        await modal.ShowDialog(this.Window());
    }

    void NewProject()
    {
        SwitchProjectSafely(() =>
        {
            mDocument.SetProject(CreateProject(new ProjectInfo()
            {
                Tracks = [new()
                {
                    Name = "Track".Tr(TC.Document) + "_1",
                    Parts =
                    [
                        new MidiPartInfo()
                        {
                            Name = "Part".Tr(TC.Document) + "_1",
                            EndOffset = 64 * MusicTheory.RESOLUTION * 4,
                            SoundSource = RecentSoundSourceManager.DefaultVoiceSoundSource(),
                        }
                    ]
                }]
            }));
        });
    }

    async void SwitchProjectSafely(Action SwitchProject)
    {
        if (mDocument.IsSaved)
        {
            SwitchProject();
            return;
        }

        var modal = new Dialog();
        modal.SetTitle("Tips".Tr(TC.Dialog));
        modal.SetMessage("The project has not been saved.\n Do you want to save it?".Tr(TC.Dialog));
        modal.AddButton("Cancel".Tr(TC.Dialog), ButtonType.Normal);
        modal.AddButton("No".Tr(TC.Dialog), ButtonType.Normal).Clicked += () => { SwitchProject(); };
        modal.AddButton("Save".Tr(TC.Dialog), ButtonType.Primary).Clicked += async () => { await SaveProject(); SwitchProject(); };
        modal.Topmost = true;
        await modal.ShowDialog(this.Window());
    }

    // 打开失败必须弹窗告知：只 Log 的话用户点了菜单没有任何反应，看到的仍是原来的工程，
    // 分不清“没打开”和“打开的是个空工程”，只有翻日志才知道发生过什么。
    // 两段都要接：反序列化（格式不支持 / 文件损坏）与装配工程（数据非法、引用不到的音源等）。
    // 后者原先完全没有兜底——异常直接抛穿 UI 线程，比静默更糟。
    void LoadProject(string path)
    {
        if (TryLoadProject(path) is { } error)
            _ = this.ShowFileOpenError(path, error);
    }

    // 装载的实心那一半：成功返回 null，失败返回**为什么**。
    // 【为什么要这一层】界面路径要把原因弹给用户，而 `project open` 要把同一句原因放进命令的回报里——
    // 两者是同一件事的两种出口，故装载只写一遍。日志两边都要，留在这里。
    string? TryLoadProject(string path)
    {
        if (!FormatsManager.DeserializeNative(path, out var file, out var error))
        {
            Log.Error("Deserialize file error: " + error);
            return error;
        }

        try
        {
            var project = CreateProject(file.Project);
            project.SetExportConfig(file.Export);
            mDocument.SetProject(project, path);
            Playhead.Pos = Math.Max(0, file.Editor.PlayheadPos);
        }
        catch (Exception ex)
        {
            Log.Error("Load project error: " + ex);
            return ex.Message;
        }

        RecentFilesManager.AddFile(path);
        return null;
    }

    Project CreateProject(ProjectInfo info)
    {
        for (int i = 0; i < info.Tracks.Count; i++)
        {
            if (string.IsNullOrEmpty(info.Tracks[i].Color))
            {
                info.Tracks[i].Color = Style.GetNewColor(i);
            }
        }

        return new Project(info);
    }

    public void OpenProject()
    {
        SwitchProjectSafely(async () =>
        {
            var formats = FormatsManager.GetAllImportFormats();
            var patterns = new List<string>();
            foreach (var format in formats)
            {
                patterns.Add("*." + format);
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
                return;

            var files = await topLevel.OpenFilePickerTracked(new FilePickerOpenOptions
            {
                Title = "Open File",
                AllowMultiple = false,
                FileTypeFilter = [new("Importable Formats") { Patterns = patterns }]
            });
            var path = files.IsEmpty() ? null : files[0].TryGetLocalPath();
            if (path == null)
                return;

            LoadProject(path);
        });
    }

    async public void OpenProjectByPath(string path)
    {
        if (!File.Exists(path))
        {
            var modal = new Dialog();
            modal.SetTitle("Tips".Tr(TC.Dialog));
            modal.SetMessage("The file failed to open because it does not exist.".Tr(TC.Dialog));
            modal.AddButton("OK".Tr(TC.Dialog), ButtonType.Primary);
            modal.Topmost = true;
            await modal.ShowDialog(this.Window());

            return;
        }

        LoadProject(path);
    }

    public async Task SaveProject()
    {
        if (!HasSaveTarget)
        {
            await SaveProjectAs();
            return;
        }
        SaveToFile(mDocument.Path);
        RecentFilesManager.AddFile(mDocument.Path);
    }

    public async Task SaveProjectAs()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var file = await topLevel.SaveFilePickerTracked(new FilePickerSaveOptions
        {
            Title = "Save File".Tr(TC.Dialog),
            DefaultExtension = "." + ConstantDefine.DefaultProjectExtension,
            SuggestedFileName = Path.GetFileNameWithoutExtension(mDocument.Name),
            ShowOverwritePrompt = true,
            FileTypeChoices = [new("TuneLab Project".Tr(TC.Dialog)) { Patterns = ["*." + ConstantDefine.DefaultProjectExtension] }]
        });
        var path = file?.TryGetLocalPath();
        if (path == null)
            return;

        SaveToFile(path);
        RecentFilesManager.AddFile(path);
    }

    public async void ExportAs(string extension)
    {
        if (mDocument.Project == null)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var file = await topLevel.SaveFilePickerTracked(new FilePickerSaveOptions
        {
            Title = "Export As".Tr(TC.Dialog),
            DefaultExtension = extension,
            SuggestedFileName = Path.GetFileNameWithoutExtension(mDocument.Name),
            ShowOverwritePrompt = true,
            FileTypeChoices = [new("Project") { Patterns = ["*." + extension] }]
        });
        var path = file?.TryGetLocalPath();
        if (path == null)
            return;

        var projectFile = new NativeProjectFile
        {
            Project = mDocument.Project.GetInfo(),
            Editor = new EditorInfo { PlayheadPos = Playhead.Pos },
            Export = mDocument.Project.GetExportConfig(),
        };
        if (!FormatsManager.SerializeNative(projectFile, extension, out var stream, out var error))
        {
            Log.Error("Save file error: " + error);
            return;
        }

        using (FileStream fileStream = new FileStream(path, FileMode.Create))
        {
            stream.CopyTo(fileStream);
        }
        RecentFilesManager.AddFile(path);
    }

    // 返回 null = 存下了；否则是**为什么没存下**。
    // 【为什么要返回值】从前这里把序列化失败与写盘失败都咽进日志，界面上看不出区别——那对着屏幕的人
    // 还能发现"标题栏还带着星号"，而命令面照搬就会回报"已保存"而文件根本没写成。菜单那条路的行为不变
    // （调用方忽略返回值，与从前一样只留日志），命令面则据此如实回话。
    string? SaveToFile(string path)
    {
        if (mDocument.Project == null)
            return "no project is open";

        var file = new NativeProjectFile
        {
            Project = mDocument.Project.GetInfo(),
            Editor = new EditorInfo { PlayheadPos = Playhead.Pos },
            Export = mDocument.Project.GetExportConfig(),
        };
        if (!FormatsManager.SerializeNative(file, ConstantDefine.DefaultProjectExtension, out var stream, out var error))
        {
            Log.Error("Save file error: " + error);
            return error;
        }

        try
        {
            using (FileStream fileStream = new FileStream(path, FileMode.Create))
            {
                stream.CopyTo(fileStream);
            }

            ClearAutoSaveFile();

            mDocument.SetSavePath(path);
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug("Write file error: " + ex);
            return ex.Message;
        }
    }

    // 命令面的保存（`project save` / `project save-as`）：**不弹任何框**——没有可保存的目标就如实报错，
    // 由命令指向 save-as，而不是像菜单那条路那样转去弹一个没人应答的文件选择器。
    // path 为空 = 存回当前路径；给了 path = 另存为（保存路径随之改到那里，与界面上的另存为同义）。
    internal string? SaveFromCommand(string? path)
    {
        if (mDocument.Project == null)
            return "no project is open";

        var target = string.IsNullOrEmpty(path) ? (HasSaveTarget ? mDocument.Path : null) : path;
        if (string.IsNullOrEmpty(target))
            return "this project has never been saved, so there is no path to save it back to";

        if (SaveToFile(target) is { } error)
            return error;

        RecentFilesManager.AddFile(target);
        return null;
    }

    public async void ExportMix()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var file = await topLevel.SaveFilePickerTracked(new FilePickerSaveOptions
        {
            Title = "Save File".Tr(TC.Dialog),
            DefaultExtension = ".wav",
            SuggestedFileName = Path.GetFileNameWithoutExtension(mDocument.Name),
            ShowOverwritePrompt = true,
            FileTypeChoices = [new("WAVE File".Tr(TC.Dialog)) { Patterns = ["*.wav"] }]
        });
        var path = file?.TryGetLocalPath();
        if (path == null)
            return;

        try
        {
            AudioEngine.ExportMaster(path, true);
        }
        catch (Exception ex)
        {
            await this.ShowMessage("Error".Tr(TC.Dialog), "Export failed: \n" + ex.Message);
        }
    }

    async void OnExportRequested(ExportOptions options)
    {
        if (Project == null)
            return;

        // 导出范围窗口：全曲 → 无边界；选区 → 编排区范围选区的 tick 区间转时间钳制。
        double startTime = 0;
        double? endTime = null;
        if (options.RangeMode == ExportRangeMode.Selection)
        {
            if (mTrackWindow.TrackScrollView.CurrentSelection is not { } selection || selection.EndTick <= selection.StartTick)
            {
                await this.ShowMessage("Export".Tr(TC.Dialog), "No selection to export.".Tr(TC.Dialog));
                return;
            }

            startTime = Project.TempoManager.GetTime(selection.StartTick);
            endTime = Project.TempoManager.GetTime(selection.EndTick);
        }

        // Create export progress dialog with progress bar
        var exportDialog = new ExportDialog();
        exportDialog.SetTitle("Export".Tr(TC.Dialog));
        exportDialog.SetMessage("Exporting...".Tr(TC.Dialog));
        exportDialog.SetProgress(0);

        var project = Project;
        var totalTracks = options.SelectedTracks.Count;
        string? errorMessage = null;

        // Show dialog non-blocking, run export in background
        _ = Task.Run(async () =>
        {
            try
            {
                if (!Directory.Exists(options.ExportPath))
                    Directory.CreateDirectory(options.ExportPath);

                for (int i = 0; i < totalTracks; i++)
                {
                    var exportTrack = options.SelectedTracks[i];
                    var trackIndex = exportTrack.TrackIndex;
                    bool isStereo = exportTrack.Channels >= 2;
                    string trackName = trackIndex == -1 ? "Master" : $"Track {trackIndex + 1}";
                    if (trackIndex >= 0 && trackIndex < project.Tracks.Count)
                    {
                        var name = project.Tracks[trackIndex].Name.Value;
                        if (!string.IsNullOrEmpty(name))
                            trackName = name;
                    }

                    int trackIdx = i;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        exportDialog.SetMessage("Exporting...".Tr(TC.Dialog));
                        exportDialog.SetStatus($"({trackIdx + 1}/{totalTracks}): {trackName}");
                    });

                    // Progress callback: maps per-track progress [0,1] to overall progress
                    var trackProgress = new Progress<double>(p =>
                    {
                        double overallProgress = (trackIdx + p) / totalTracks;
                        Dispatcher.UIThread.Post(() =>
                        {
                            exportDialog.SetProgress(overallProgress);
                        });
                    });

                    string filePath = Path.Combine(options.ExportPath, options.FileName + "_" + trackName.ToValidFileName() + options.Format.Extension());
                    var settings = new AudioEncodeSettings { Format = options.Format, BitDepth = options.BitDepth, Bitrate = options.Bitrate };

                    if (trackIndex == -1)
                    {
                        AudioEngine.ExportMaster(filePath, isStereo, options.SampleRate, settings, trackProgress, startTime: startTime, endTime: endTime);
                    }
                    else if (trackIndex >= 0 && trackIndex < project.Tracks.Count)
                    {
                        var track = project.Tracks[trackIndex];
                        AudioEngine.ExportTrack(filePath, track, isStereo, options.SampleRate, settings, trackProgress, startTime: startTime, endTime: endTime);
                    }
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                exportDialog.Close();
            });
        });

        await exportDialog.ShowDialog(this.Window());

        if (errorMessage != null)
        {
            await this.ShowMessage("Error".Tr(TC.Dialog), "Export failed: \n".Tr(TC.Dialog) + errorMessage);
        }
        else
        {
            await this.ShowMessage("Export".Tr(TC.Dialog), "Export completed successfully.".Tr(TC.Dialog));
        }
    }

    public void Undo()
    {
        mDocument.Undo();
    }

    public void Redo()
    {
        mDocument.Redo();
    }

    public void AddTrack()
    {
        var project = Project;
        if (project == null)
            return;

        project.NewTrack();
        project.Commit();
    }

    public void ImportAudio()
    {
        if (Project == null)
            return;

        TrackWindow.TrackScrollView.ImportAudioAt(0, Project.Tracks.Count);
    }

    public void ImportTrack()
    {
        if (Project == null)
            return;

        TrackWindow.TrackScrollView.ImportTrack();
    }

    public void ChangePlayState()
    {
        if (AudioEngine.IsPlaying) AudioEngine.Pause();
        else AudioEngine.Play();
    }

    // 跳到工程起点/终点：移动播放头，并让轨道窗与钢琴窗时间轴跟过去（与 FunctionBar 按钮同一路径）。
    void GotoStart()
    {
        var startTime = 0;
        AudioEngine.Seek(startTime);
        if (Project == null)
            return;

        var startTick = Project.TempoManager.GetTick(startTime);
        mTrackWindow.TickAxis.AnimateMoveTickToX(startTick, 0);
        mPianoWindow.TickAxis.AnimateMoveTickToX(startTick, 0);
    }

    void GotoEnd()
    {
        var endTime = AudioEngine.EndTime;
        AudioEngine.Seek(endTime);
        if (Project == null)
            return;

        var endTick = Project.TempoManager.GetTick(endTime);
        mTrackWindow.TickAxis.AnimateMoveTickToX(endTick, mTrackWindow.TickAxis.ViewLength);
        mPianoWindow.TickAxis.AnimateMoveTickToX(endTick, mPianoWindow.TickAxis.ViewLength);
    }

    struct Description
    {
        public string name { get; set; }
    }

    public async void InstallExtensions(IEnumerable<string> files)
    {
        List<string> installedExtension = [];
        List<string> installedNames = [];
        List<string> succeeded = [];
        List<string> failed = [];
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);

            // 读包名（容错）：manifest.json 缺失/损坏不阻断安装——解压后由 ExtensionManager.Load
            // 优雅记录加载状态。绝不让一个坏包的解析异常冒泡（本方法是 async void，未捕获即崩进程）。
            try
            {
                using var archive = ZipFile.OpenRead(file);
                using var stream = archive.GetEntry("manifest.json")?.Open();
                if (stream != null)
                {
                    var description = JsonSerializer.Deserialize<Description>(stream);
                    if (!string.IsNullOrEmpty(description.name))
                        name = description.name;
                }
            }
            catch { /* 用文件名兜底 */ }

            var dir = Path.Combine(PathManager.ExtensionsFolder, name);
            if (Directory.Exists(dir))
            {
                installedExtension.Add(file);
                installedNames.Add(name);
                continue;
            }

            try
            {
                ZipFileHelper.ExtractToDirectory(file, dir);
                ExtensionManager.Load(dir);
                // 解压成功 ≠ 加载成功：坏 manifest 等会被 Load 优雅记成 Failed 而不抛，这里据加载结果归类。
                var result = ExtensionManager.LoadResults.LastOrDefault(r => r.DirectoryPath == dir);
                if (result != null && result.Status == ExtensionLoadStatus.Failed)
                    failed.Add(name + ": " + (result.Error ?? "load failed"));
                else
                    succeeded.Add(name);
            }
            catch (Exception ex)
            {
                failed.Add(name + ": " + ex.Message);
            }
        }

        // 刚 Load 的引擎此刻只完成「注册」尚未 Init。补做启动时对【音源引擎】的急切 Init
        // （见 App.OnFrameworkInitializationCompleted），让新装的音源引擎无需重启即可用。
        // Init 失败与安装成败是两回事（插件已装好，是初始化出错），故不并入安装汇总——
        // 单独弹窗报错，语义与启动时的 Init 失败提示一致。
        // voice 与 instrument 都要做（两者同为 MidiPart 的音源、都有音源目录要露出）；effect 不做也不该做
        // ——它没有音源目录，按 part 用到才 Init 是对的。两处急切 Init 须保持同一集合，别只加一边。
        List<string> initFailed = [];
        if (succeeded.Count > 0)
        {
            ExtensionSettingsManager.ApplyPersisted(); // Init 前回喂已落盘设置（与启动同序）
            // 与启动、headless、`extension install` 同一份判据（SoundSourceEngines.InitAll）。
            initFailed.AddRange(SoundSourceEngines.InitAll());
        }

        // Auto-refresh the extension list in the sidebar
        mExtensionSideBarContentProvider.RefreshExtensions();
        if (mRightSideTabBar.SelectedTab.Value == SideBarTab.Extensions)
            mRightSideBar.SetContent(SideBarTab.Extensions, mExtensionSideBarContentProvider.Content);

        // 批量安装一次性汇总（不再每个包弹一次窗）。各包的实际加载状态见扩展侧边栏。
        if (succeeded.Count > 0 || failed.Count > 0)
        {
            var summary = new List<string>();
            if (succeeded.Count > 0)
                summary.Add("Installed: ".Tr(TC.Dialog) + string.Join(", ", succeeded));
            if (failed.Count > 0)
                summary.Add("Failed: ".Tr(TC.Dialog) + string.Join("; ", failed));
            await this.ShowMessage("Tips".Tr(TC.Dialog), string.Join("\n", summary));
        }

        // Init 失败与安装汇总分开弹窗：插件已装好，仅初始化出错。
        if (initFailed.Count > 0)
            await this.ShowMessage("Error".Tr(TC.Dialog), string.Join("\n\n", initFailed));

        if (installedExtension.IsEmpty())
            return;

        var dialog = new Dialog();
        dialog.SetTitle("Tips".Tr(TC.Dialog));
        dialog.SetMessage(string.Format("Detected {0} already-installed extension(s): {1}.\nDo you want to restart and reinstall them?".Tr(TC.Dialog), installedNames.Count, string.Join(", ", installedNames)));
        dialog.AddButton("Yes".Tr(TC.Dialog), ButtonType.Normal).Clicked += () =>
        {
            List<string> args = ["-restart"];
            args.AddRange(installedExtension);
            string installer = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ExtensionInstaller.exe" : "ExtensionInstaller";
            ProcessHelper.CreateProcess(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, installer), args);
            this.Window().Close();
        };
        dialog.AddButton("No".Tr(TC.Dialog), ButtonType.Primary);
        await dialog.ShowDialog(this.Window());
    }

    private async void UpdateDialog(UpdateInfo mUpdateCheck, bool IsAutoCheck)
    {
        var dialog = new UpdateDialog();
        dialog.SetMessage("Version".Tr(TC.Dialog) + $": {mUpdateCheck.version}\n" + "Public Date".Tr(TC.Dialog) + $": {mUpdateCheck.publishedAt}");
        dialog.SetMDMessage(mUpdateCheck.description ?? "");
        if (IsAutoCheck)
            dialog.AddButton("Ignore".Tr(TC.Dialog), GUI.UpdateDialog.ButtonType.Normal).Clicked += () => AppUpdateManager.SaveIgnoreVersion(mUpdateCheck.version!);
        dialog.AddButton("Later".Tr(TC.Dialog), GUI.UpdateDialog.ButtonType.Normal);
        // 下载期间对话框需保持打开以显示进度，故关闭按钮自带的自动 Close。
        dialog.AddButton("Update".Tr(TC.Dialog), GUI.UpdateDialog.ButtonType.Primary, closeOnClick: false).Clicked
            += () => StartUpdate(dialog, mUpdateCheck);
        await dialog.ShowDialog(this.Window());
    }

    // 整包自更新：下载新安装器（带进度）→ 拉起其 -update 静默模式 → 退出本进程释放文件锁，
    // 由安装器覆盖当前安装目录并重启 TuneLab。
    private void StartUpdate(UpdateDialog announcement, UpdateInfo info)
    {
        announcement.Close();

        // 自更新只在拿到安装器直链时走。服务端给不出这个字段（该平台没有安装器、或服务端尚未提供），
        // 就退回 1.x 的行为：浏览器打开下载页，用户自己下载安装。
        // 另：安装器的 -update 静默覆盖模式目前是 Windows 专属，其它平台一律走下载页。
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(info.installerUrl))
        {
            OpenDownloadPage(info);
            return;
        }

        // 非模态下载（主程序仍可操作、可取消）。下载完请求走正常关闭流程重启——未保存提示等逻辑与手动关闭一致。
        var window = new TuneLab.GUI.ProgressWindow();
        window.SetTitle("Downloading update…".Tr(TC.Dialog));
        window.ShowCancel("Cancel".Tr(TC.Dialog));
        var cts = new System.Threading.CancellationTokenSource();
        window.CancelRequested += () => cts.Cancel();
        window.Opened += async (_, _) =>
        {
            var progress = new Progress<double>(p => { window.SetProgress(p); window.SetStatus($"{p:P0}"); });
            try
            {
                var path = await AppUpdateManager.DownloadInstallerAsync(info.installerUrl!, progress, cts.Token);
                if (cts.IsCancellationRequested) { window.Close(); return; }
                window.Close();
                (this.Window() as MainWindow)?.RequestUpdateRestart(path, DownloadPageUrl(info));
            }
            catch (OperationCanceledException) { window.Close(); }
            catch (Exception ex)
            {
                // 下载不成（网络、或拿到的根本不是安装器）不是死路：让用户去下载页手动装。
                Log.Error($"Update download failed: {ex}");
                window.Close();
                await this.ShowMessage("Update".Tr(TC.Dialog), "Automatic update failed. Opening the download page…".Tr(TC.Dialog));
                OpenDownloadPage(info);
            }
        };
        window.Show(this.Window());
    }

    // 更新公告里那条给人看的下载页；服务端没给就退到官网。
    private static string DownloadPageUrl(UpdateInfo info) => string.IsNullOrEmpty(info.url) ? "https://tunelab.app" : info.url;

    private static void OpenDownloadPage(UpdateInfo info) => ProcessHelper.OpenUrl(DownloadPageUrl(info));

    public async void CheckUpdate(bool IsAutoCheck = true)
    {
        try
        {
            var mUpdateCheck = await AppUpdateManager.CheckForUpdate(IsAutoCheck);
            if (mUpdateCheck != null)
            {
                Log.Info($"Update available: {mUpdateCheck.version}");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    UpdateDialog(mUpdateCheck, IsAutoCheck);
                });
            }
            else
            {
                Log.Info("No update available.");
                if (!IsAutoCheck)
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await this.ShowMessage("Update".Tr(TC.Dialog), "No updates at the moment.".Tr(TC.Dialog));
                    });
            }
        }
        catch (Exception ex)
        {
            Log.Error($"CheckUpdate: {ex.Message}");
            if (!IsAutoCheck)
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await this.ShowMessage("Check update failed".Tr(TC.Dialog), "An error occurred while checking for updates. Please check the log for more details.".Tr(TC.Dialog));
                });
            }
        }
    }

    private void ShowAbout() => _ = new Dialogs.AboutDialog().ShowDialog(this.Window());

    [MemberNotNull(nameof(mUndoMenuItem))]
    [MemberNotNull(nameof(mRedoMenuItem))]
    [MemberNotNull(nameof(mRecentFilesMenu))]
    Menu CreateMenu()
    {
        var menu = new Menu() { Background = Style.BACK.ToBrush(), Height = 40 };
        {
            var menuBarItem = new MenuItem { Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false }.SetTrName("File");
            // 最近文件子菜单按需重建：仅在「文件」菜单打开时刷新，避免在某个最近文件项的点击命令执行期间
            // 清空其所属集合（会移除正在被点击的项，破坏菜单内部选中/弹窗状态，导致下次首次悬浮二级菜单被立即关闭）
            menuBarItem.SubmenuOpened += (_, _) => UpdateRecentFilesMenu();
            {
                var menuItem = new MenuItem().SetTrName("New").SetAction("file.new");
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Open").SetAction("file.open");
                menuBarItem.Items.Add(menuItem);
            }
            {
                mRecentFilesMenu = new MenuItem() { Foreground = Style.TEXT_LIGHT.ToBrush() }.SetTrName("Recent Files");
                UpdateRecentFilesMenu();
                menuBarItem.Items.Add(mRecentFilesMenu);
            }
            {
                var menuItem = new MenuItem().SetTrName("Save").SetAction("file.save");
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Save As").SetAction("file.saveAs");
                menuBarItem.Items.Add(menuItem);
            }
            {
                // 只在"当前工程来自崩溃恢复、且原文件仍在"时出现（用 IsVisible 而非置灰：一个常年灰着的项
                // 只是杂物）。可见性随工程名变化刷新，见构造函数里的订阅。
                mSaveRecoveredMenuItem = new MenuItem().SetTrName("Save to Original Location").SetAction("file.saveToOriginal");
                mSaveRecoveredMenuItem.IsVisible = false;
                menuBarItem.Items.Add(mSaveRecoveredMenuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Add Track").SetAction(AddTrack);
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Import Audio").SetAction(ImportAudio);
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Import Track").SetAction(ImportTrack);
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem() { Foreground = Style.TEXT_LIGHT.ToBrush() }.SetTrName("Export As");
                foreach (var format in FormatsManager.GetAllExportFormats())
                {
                    var menuItem2 = new MenuItem().SetName(format).SetAction(() => ExportAs(format));
                    menuItem.Items.Add(menuItem2);
                }
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Export Mix").SetAction(ExportMix);
                menuBarItem.Items.Add(menuItem);
            }
            menu.Items.Add(menuBarItem);
        }

        {
            var menuBarItem = new MenuItem { Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false }.SetTrName("Edit");
            {
                var menuItem = new MenuItem().SetTrName("Undo").SetAction("edit.undo");
                menuBarItem.Items.Add(menuItem);
                mUndoMenuItem = menuItem;
            }
            {
                var menuItem = new MenuItem().SetTrName("Redo").SetAction("edit.redo");
                menuBarItem.Items.Add(menuItem);
                mRedoMenuItem = menuItem;
            }
            menu.Items.Add(menuBarItem);
        }

        {
            // 用户脚本工具（context=global）：脚本库里定义了 getScriptInfo 的脚本自动出现于此（按 category 分组）。
            // 每次打开时重建——用户增删/改脚本即时反映（与 Recent Files 同范式）。
            var menuBarItem = new MenuItem { Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false }.SetTrName("Scripts");
            void Rebuild()
            {
                menuBarItem.Items.Clear();
                foreach (var item in ScriptToolMenu.BuildGlobalMenuItems(this))
                    menuBarItem.Items.Add(item);
                // 全部工具脚本（不限 context）同步为可绑定命令，供快捷键分发与设置页。
                ScriptToolMenu.SyncActions(this);
            }
            // 不在本菜单自身 SubmenuOpened 时重建——边打开边换 Items 会让首次悬浮二级（分组）子菜单被立即关闭
            // （与 Recent Files 同坑）。改为：内容须在菜单打开前就备好——靠脚本目录的文件监视器在增删改时提前重建。
            mRebuildScriptsMenu = Rebuild;
            Rebuild();
            SetupScriptsWatcher(Rebuild);
            menu.Items.Add(menuBarItem);
        }

        {
            var menuBarItem = new MenuItem { Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false }.SetTrName("Help");
            {
                var menuItem = new MenuItem().SetTrName("User Manual").SetAction("app.manual");
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Open TuneLab Folder").SetAction("app.openDataFolder");
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Open Log").SetAction("app.openLog");
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Check for Updates...").SetAction("app.checkUpdates");
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("About TuneLab").SetAction("app.about");
                menuBarItem.Items.Add(menuItem);
            }
            menu.Items.Add(menuBarItem);
        }

        return menu;
    }

    MenuItem mUndoMenuItem;
    MenuItem mRedoMenuItem;
    MenuItem? mSaveRecoveredMenuItem;
    public MenuItem mRecentFilesMenu;

    // 顶部 Scripts 菜单的重建钩子 + 脚本目录监视器（用户增删改脚本时提前重建菜单，避免边打开边改）。
    Action? mRebuildScriptsMenu;
    System.IO.FileSystemWatcher? mScriptsWatcher;

    void SetupScriptsWatcher(Action rebuild)
    {
        try
        {
            PathManager.MakeSureExist(PathManager.ScriptsFolder);
            mScriptsWatcher = new System.IO.FileSystemWatcher(PathManager.ScriptsFolder, "*.js")
            {
                NotifyFilter = System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            void OnChanged(object? s, System.IO.FileSystemEventArgs e) => Dispatcher.UIThread.Post(rebuild);
            mScriptsWatcher.Created += OnChanged;
            mScriptsWatcher.Deleted += OnChanged;
            mScriptsWatcher.Changed += OnChanged;
            mScriptsWatcher.Renamed += (s, e) => Dispatcher.UIThread.Post(rebuild);
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to watch scripts folder: " + ex.Message);
        }
    }

    class PlayheadForProject : IPlayhead
    {
        public IActionEvent PosChanged => mPosChanged;

        public double Pos
        {
            get => mCursorPos;
            set
            {
                SyncCursorPos(value);
                if (mEditor.Project == null)
                    return;

                AudioEngine.Seek(mEditor.Project.TempoManager.GetTime(mCursorPos));
            }
        }

        public PlayheadForProject(Editor editor)
        {
            mEditor = editor;
            AudioEngine.ProgressChanged += OnAudioEngineProgress;
        }

        ~PlayheadForProject()
        {
            AudioEngine.ProgressChanged -= OnAudioEngineProgress;
        }

        void OnAudioEngineProgress()
        {
            if (mEditor.Project == null)
                return;

            var newCursorPos = mEditor.Project.TempoManager.GetTick(AudioEngine.CurrentTime);
            SyncCursorPos(newCursorPos);
        }

        void SyncCursorPos(double newCursorPos)
        {
            newCursorPos = Math.Max(0, newCursorPos);
            if (mCursorPos == newCursorPos)
                return;

            mCursorPos = newCursorPos;
            mPosChanged.Invoke();
        }

        double mCursorPos = 0;

        readonly Editor mEditor;
        readonly ActionEvent mPosChanged = new();
    }

    double mTrackWindowHeight = 240;
    double TrackWindowHeight
    {
        get => mTrackWindowHeight.Limit(mTrackWindow.MinHeight, Bounds.Height - mFunctionBar.Bounds.Height);
        set { mTrackWindowHeight = value; mTrackWindowHeight = TrackWindowHeight; mTrackWindow.Height = mTrackWindowHeight; }
    }

    private void UpdateRecentFilesMenu()
    {
        mRecentFilesMenu.Items.Clear();
        foreach (var mRecentFile in RecentFilesManager.GetRecentFiles())
        {
            var menuItem = new MenuItem().SetName(mRecentFile.FileName).SetAction(() =>
            {
                SwitchProjectSafely(() => OpenProjectByPath(mRecentFile.FilePath));
                Menu.Close();
            });
            mRecentFilesMenu.Items.Add(menuItem);
        }

        if (mRecentFilesMenu.Items.Count == 0)
        {
            var menuItem = new MenuItem().SetTrName("Empty");
            menuItem.IsEnabled = false;
            mRecentFilesMenu.Items.Add(menuItem);
        }
    }

    Timer? mTimer;
    readonly DispatcherTimer mAutoSaveTimer = new() { Interval = new TimeSpan(0, 0, Settings.AutoSaveInterval) };
    Head mAutoSaveHead;
    // 自动保存的落盘容器（哨兵 + History + 元数据 sidecar）。落点作为构造参数传入，将来换目录不必改这里。
    public AutoSaveStore AutoSaveStore { get; } = new(PathManager.AutoSaveFolder);

    IPart? mEditingPart = null;
    IPart? mDetachedEditingPart = null;   // 轨道被临时摘除（如重排）期间暂存的在编 part，待其轨道重新插入时复位
    IPart? mLastPart = null;

    readonly TrackWindow mTrackWindow;
    // 编排区范围选区（编辑器态）→ 脚本快照（tl.selection()）：UI 侧 0-based 行号在此边界转 1-based 轨道号。无选区 null。
    ScriptSelection? CurrentScriptSelection()
    {
        var sel = mTrackWindow.TrackScrollView.CurrentSelection;
        return sel is { } s ? new ScriptSelection(s.StartTick, s.EndTick, s.StartTrackIndex + 1, s.EndTrackIndex + 1) : null;
    }

    // 钢琴窗范围选区（编辑器态，tick 带）→ 脚本快照（tl.pianoSelection()）。无选区 null。与编排区选区独立并存。
    ScriptPianoSelection? CurrentPianoScriptSelection()
    {
        var sel = mPianoWindow.PianoScrollView.CurrentRegionSelection;
        return sel is { } s ? new ScriptPianoSelection(s.StartTick, s.EndTick) : null;
    }

    // 换工程文件的能力（`project open`）。**只做转发**：未保存的判据、装载与错误措辞都在 Editor 里已有一份。
    sealed class ProjectFileAccess(Editor editor) : IProjectFileAccess
    {
        public bool IsSaved => editor.mDocument.IsSaved;
        public string? Path => editor.mDocument.Path;
        public bool HasSaveTarget => editor.HasSaveTarget;
        public string? Open(string path) => editor.TryLoadProject(path);
        public string? Save(string? path) => editor.SaveFromCommand(path);
    }

    // 挪视野的那道口子（`editor reveal`）。**只做转发**：轴的算术在各轴自己身上（AnimateReveal），
    // 这里只负责"哪两条轴该动"——那是 Editor 才知道的事（编排区与钢琴窗各有一条时间轴）。
    sealed class EditorViewAccess(Editor editor) : IEditorViewAccess
    {
        public IPart? EditingPart => editor.mPianoWindow.Part;

        // 两条时间轴一起走，同 GotoStart/GotoEnd——只挪一条的话，用户在另一个窗里看到的还是原处。
        // 回报的是编排区那条的落点：它恒在，且它看得见整个工程。
        public (double Start, double End) RevealTicks(double startTick, double endTick)
        {
            editor.mPianoWindow.TickAxis.AnimateRevealTicks(startTick, endTick);
            return editor.mTrackWindow.TickAxis.AnimateRevealTicks(startTick, endTick);
        }

        // 1-based 轨号在这里换成视图的 0-based 行号（同 ScriptSelectionWriter 的边界）。
        // 只滚不缩：轨高是用户自己调的。
        public void RevealTrack(int trackNumber)
        {
            var axis = editor.mTrackWindow.TrackVerticalAxis;
            axis.AnimateMovePosToCoor(trackNumber - 1 + 0.5, axis.ViewLength / 2);
        }

        public void RevealPitches(double minPitch, double maxPitch)
            => editor.mPianoWindow.PitchAxis.AnimateRevealPitches(minPitch, maxPitch);

        public void OpenPart(IPart part) => editor.SwitchEditingPart(part);

    }

    // 脚本面写范围选区的那道口子（tl.setTrackSelection 等）。**只做转发**：选区各归两个视图自己持有，
    // 而只有 Editor 同时够得着它们。1-based 轨道号在这里换成视图的 0-based 行号——与上面读那两个方法
    // 是同一处边界，故两个方向的口径不会分叉。
    sealed class ScriptSelectionWriter(Editor editor) : IScriptSelectionWriter
    {
        public void SetTrackSelection(double startTick, double endTick, int startTrackNumber, int endTrackNumber)
            => editor.mTrackWindow.TrackScrollView.SetSelection(new(startTick, endTick, startTrackNumber - 1, endTrackNumber - 1));

        public void ClearTrackSelection() => editor.mTrackWindow.TrackScrollView.ClearSelection();

        public void SetPianoSelection(double startTick, double endTick)
            => editor.mPianoWindow.PianoScrollView.SetRegionSelection(startTick, endTick);

        public void ClearPianoSelection() => editor.mPianoWindow.PianoScrollView.ClearRegionSelection();
    }

    // 建得早（三条脚本运行路径都要它），本身无状态、只在被调用时才碰视图。
    readonly ScriptSelectionWriter mSelectionWriter;
    readonly ProjectFileAccess mProjectFileAccess;
    readonly EditorViewAccess mEditorViewAccess;

    readonly FunctionBar mFunctionBar;
    readonly PianoWindow mPianoWindow;
    readonly SideBar mRightSideBar;
    readonly SideTabBar mRightSideTabBar;

    readonly PartPropertySideBarContentProvider mPartPropertySideBarContentProvider = new();
    readonly NotePropertySideBarContentProvider mNotePropertySideBarContentProvider = new();
    enum PartPanelFocusArea { Piano, Arrangement }
    PartPanelFocusArea mPartPanelFocusArea = PartPanelFocusArea.Piano;
    bool mPartTargetUpdatePending = false;
    readonly ExtensionSideBarContentProvider mExtensionSideBarContentProvider = new();
    readonly ExportSideBarContentProvider mExportSideBarContentProvider = new();
    readonly AgentSideBarContentProvider mAgentSideBarContentProvider = new();
    readonly ScriptSideBarContentProvider mScriptSideBarContentProvider = new();

    readonly PlayheadForProject mPlayhead;

    readonly ProjectDocument mDocument = new();
    readonly DisposableManager s = new();
}
