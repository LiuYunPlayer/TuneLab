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

        mPlayhead = new(this);
        if (Enum.TryParse<PlayScrollTarget>(Settings.AutoScrollTarget.Value, out var autoScrollTarget))
        {
            PlayScrollTarget.Value = autoScrollTarget;
        }

        // 钢琴窗先于功能栏构造：FunctionBar 构造时即订阅 EditingPartHolder（颤音工具可用性）。
        mPianoWindow = new(this);// { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
        mFunctionBar = new(this);
        // agent 经此实时读取"当前编辑 part"（用户说"当前/这个 part"时解析序号）与当前量化（吸附网格）。
        mAgentSideBarContentProvider.SetCurrentPartProvider(() => mPianoWindow.Part);
        mAgentSideBarContentProvider.SetQuantizationProvider(() => mPianoWindow.Quantization);
        mAgentSideBarContentProvider.SetSelectionProvider(CurrentScriptSelection);
        mAgentSideBarContentProvider.SetPianoSelectionProvider(CurrentPianoScriptSelection);
        mScriptSideBarContentProvider.SetCurrentPartProvider(() => mPianoWindow.Part);
        mScriptSideBarContentProvider.SetQuantizationProvider(() => mPianoWindow.Quantization);
        mScriptSideBarContentProvider.SetSelectionProvider(CurrentScriptSelection);
        mScriptSideBarContentProvider.SetPianoSelectionProvider(CurrentPianoScriptSelection);
        // 用户脚本工具菜单的访问器（顶部 Scripts 菜单 + 各右键菜单共用）：工程随新建/打开切换，故传访问器。
        ScriptToolMenu.Init(() => Project, () => mPianoWindow.Part, () => mPianoWindow.Quantization, CurrentScriptSelection, CurrentPianoScriptSelection);
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
            settingsButton.Clicked += () => SettingsWindow.Open(this.Window());
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
        mFunctionBar.CollapsePropertiesAsked += show => mRightSideBar.IsVisible = show;
        mFunctionBar.GotoStartAsked += GotoStart;
        mFunctionBar.GotoEndAsked += GotoEnd;
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
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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

        RegisterKeyCommands();
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

    // Editor 作用域的内置快捷键命令。手势即当前默认，分发经 Keymap（见 docs/keybinding-system.md）。
    void RegisterKeyCommands()
    {
        Keymap.Register(new() { Id = "file.new", DisplayName = () => "New".Tr(TC.Menu), Scope = KeyScope.Editor, DefaultGesture = new(Key.N, KeyBinding.PrimaryModifier), Execute = NewProject });
        Keymap.Register(new() { Id = "file.open", DisplayName = () => "Open".Tr(TC.Menu), Scope = KeyScope.Editor, DefaultGesture = new(Key.O, KeyBinding.PrimaryModifier), Execute = OpenProject });
        Keymap.Register(new() { Id = "file.save", DisplayName = () => "Save".Tr(TC.Menu), Scope = KeyScope.Editor, DefaultGesture = new(Key.S, KeyBinding.PrimaryModifier), Execute = () => { _ = SaveProject(); } });
        Keymap.Register(new() { Id = "file.saveAs", DisplayName = () => "Save As".Tr(TC.Menu), Scope = KeyScope.Editor, DefaultGesture = new(Key.S, KeyBinding.PrimaryModifier | KeyModifiers.Shift), Execute = () => { _ = SaveProjectAs(); } });

        Keymap.Register(new() { Id = "edit.undo", DisplayName = () => "Undo".Tr(TC.Menu), Scope = KeyScope.Editor, DefaultGesture = new(Key.Z, KeyBinding.PrimaryModifier), Execute = Undo });
        Keymap.Register(new() { Id = "edit.redo", DisplayName = () => "Redo".Tr(TC.Menu), Scope = KeyScope.Editor, DefaultGesture = new(Key.Y, KeyBinding.PrimaryModifier), Execute = Redo });
        // 剪贴板类动词是"通用动作"：编排区与钢琴窗共享同一个键，触发时按当前聚焦的编辑面路由（各面自带操作中守卫）。
        // 见 docs/keybinding-system.md §2。
        Keymap.Register(new() { Id = "edit.copy", DisplayName = () => "Copy".Tr(TC.Menu), Scope = KeyScope.Editor, DefaultGesture = new(Key.C, KeyBinding.PrimaryModifier), Execute = () => RouteEdit(p => p.CopySelection(), t => t.CopySelection()) });
        Keymap.Register(new() { Id = "edit.cut", DisplayName = () => "Cut".Tr(TC.Menu), Scope = KeyScope.Editor, DefaultGesture = new(Key.X, KeyBinding.PrimaryModifier), Execute = () => RouteEdit(p => p.CutSelection(), t => t.CutSelection()) });
        Keymap.Register(new() { Id = "edit.paste", DisplayName = () => "Paste".Tr(TC.Menu), Scope = KeyScope.Editor, DefaultGesture = new(Key.V, KeyBinding.PrimaryModifier), Execute = () => RouteEdit(p => p.PasteSelection(), t => t.PasteSelection()) });
        Keymap.Register(new() { Id = "edit.delete", DisplayName = () => "Delete".Tr(TC.Menu), Scope = KeyScope.Editor, DefaultGesture = new(Key.Delete), Execute = () => RouteEdit(p => p.DeleteSelection(), t => t.DeleteSelection()) });
        Keymap.Register(new() { Id = "edit.selectAll", DisplayName = () => "Select All".Tr(TC.Menu), Scope = KeyScope.Editor, DefaultGesture = new(Key.A, KeyBinding.PrimaryModifier), Execute = () => RouteEdit(p => p.SelectAllInPiano(), t => t.SelectAllInTrack()) });

        // 域 = 功能身份，不随分发作用域走（见 docs/keybinding-system.md §1.1）：transport 而非 editor.playback。
        // 显示名沿用工具栏（FunctionBar）既有措辞，与 Go to Start / Go to End 按钮一致。
        Keymap.Register(new() { Id = "transport.play", DisplayName = () => "Play/Pause".Tr(TC.Menu), Scope = KeyScope.Editor, DefaultGesture = new(Key.Space), Execute = ChangePlayState });
        Keymap.Register(new() { Id = "transport.gotoStart", DisplayName = () => "Go to Start".Tr(TC.Menu), Scope = KeyScope.Editor, DefaultGesture = new(Key.Home), Execute = GotoStart });
        Keymap.Register(new() { Id = "transport.gotoEnd", DisplayName = () => "Go to End".Tr(TC.Menu), Scope = KeyScope.Editor, DefaultGesture = new(Key.End), Execute = GotoEnd });
        Keymap.Register(new() { Id = "part.reopenLast", DisplayName = () => "Reopen Last Part".Tr(TC.Menu), Scope = KeyScope.Editor, DefaultGesture = new(Key.Tab, KeyBinding.PrimaryModifier), Execute = ReopenLastPart });

        // 域 = view（显示层开关）。参数面板折叠/恢复与拖到最低等价；在 Editor 分发以便钢琴窗/编排区焦点下均可触发。
        Keymap.Register(new() { Id = "view.toggleParameterPanel", DisplayName = () => "Toggle Parameter Panel".Tr(TC.Menu), Scope = KeyScope.Editor, DefaultGesture = new(Key.P, KeyBinding.PrimaryModifier), Execute = () => mPianoWindow.ToggleParameterPanel() });

        // 显示名沿用工具栏（FunctionBar）既有措辞，复用其翻译、与工具栏保持一致。
        RegisterToolCommand("tool.note", "Note Tool", Key.D1, UI.PianoTool.Note);
        RegisterToolCommand("tool.pitch", "Pitch Pen", Key.D2, UI.PianoTool.Pitch);
        RegisterToolCommand("tool.anchor", "Anchor Tool", Key.D3, UI.PianoTool.Anchor);
        // 显示名不带 Pitch：这支笔在音符区固定合成音高、在参数区固定配对回显，作用面不限于音高（见 SynthesisLock）。
        RegisterToolCommand("tool.lock", "Locking Brush", Key.D4, UI.PianoTool.Lock);
        RegisterToolCommand("tool.vibrato", "Vibrato Tool", Key.D5, UI.PianoTool.Vibrato);
    }

    void RegisterToolCommand(string id, string name, Key key, PianoTool tool)
    {
        Keymap.Register(new()
        {
            Id = id,
            DisplayName = () => name.Tr(TC.Menu),
            Scope = KeyScope.Editor,
            DefaultGesture = new(key),
            Execute = () =>
            {
                // instrument 音源无颤音系统：快捷键与工具栏同口径拦截。
                if (tool != UI.PianoTool.Vibrato || mPianoWindow.Part?.SoundSource.Kind != SourceKind.Instrument)
                    mPianoWindow.PianoTool.Value = tool;
            }
        });
    }

    // 剪贴板类命令按当前键盘焦点路由到对应编辑面：焦点在编排区→track 动作、在钢琴窗→piano 动作、都不在→空操作。
    // 两面为兄弟节点，焦点至多落在其一，故不歧义。各面方法自带"操作进行中"守卫。
    void RouteEdit(Action<PianoWindow> piano, Action<TrackWindow> track)
    {
        if (mTrackWindow.IsKeyboardFocusWithin)
            track(mTrackWindow);
        else if (mPianoWindow.IsKeyboardFocusWithin)
            piano(mPianoWindow);
    }

    void ReopenLastPart()
    {
        if (mLastPart != null && mDocument.Pushable())
        {
            var track = mLastPart.Track;
            if (track.Parts.Contains(mLastPart) && track.Project.Tracks.Contains(track))
            {
                SwitchEditingPart(mLastPart);
            }
        }
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
        if (!FormatsManager.DeserializeNative(path, out var file, out var error))
        {
            Log.Error("Deserialize file error: " + error);
            _ = this.ShowFileOpenError(path, error);
            return;
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
            _ = this.ShowFileOpenError(path, ex.Message);
            return;
        }

        RecentFilesManager.AddFile(path);
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

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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
        if (!File.Exists(mDocument.Path) || Path.GetExtension(mDocument.Path) != "." + ConstantDefine.DefaultProjectExtension)
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

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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

    void SaveToFile(string path)
    {
        if (mDocument.Project == null)
            return;

        var file = new NativeProjectFile
        {
            Project = mDocument.Project.GetInfo(),
            Editor = new EditorInfo { PlayheadPos = Playhead.Pos },
            Export = mDocument.Project.GetExportConfig(),
        };
        if (!FormatsManager.SerializeNative(file, ConstantDefine.DefaultProjectExtension, out var stream, out var error))
        {
            Log.Error("Save file error: " + error);
            return;
        }

        try
        {
            using (FileStream fileStream = new FileStream(path, FileMode.Create))
            {
                stream.CopyTo(fileStream);
            }

            ClearAutoSaveFile();

            mDocument.SetSavePath(path);
        }
        catch (Exception ex)
        {
            Log.Debug("Write file error: " + ex);
        }
    }

    public async void ExportMix()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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
            foreach (var engine in VoicesManager.GetAllVoiceEngines())
            {
                try
                {
                    VoicesManager.InitEngine(engine); // 已 Init 的引擎为空操作
                }
                catch (Exception ex)
                {
                    initFailed.Add(string.Format("Voice engine [{0}] failed to init:\n{1}", engine, ex.Message));
                }
            }
            foreach (var engine in InstrumentsManager.GetAllInstrumentEngines())
            {
                try
                {
                    InstrumentsManager.InitEngine(engine); // 已 Init 的引擎为空操作
                }
                catch (Exception ex)
                {
                    initFailed.Add(string.Format("Instrument engine [{0}] failed to init:\n{1}", engine, ex.Message));
                }
            }
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
        if (string.IsNullOrEmpty(info.url))
        {
            ProcessHelper.OpenUrl("https://tunelab.app");
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
                var path = await AppUpdateManager.DownloadInstallerAsync(info.url!, progress, cts.Token);
                if (cts.IsCancellationRequested) { window.Close(); return; }
                window.Close();
                (this.Window() as MainWindow)?.RequestUpdateRestart(path);
            }
            catch (OperationCanceledException) { window.Close(); }
            catch (Exception ex)
            {
                Log.Error($"Update download failed: {ex}");
                window.Close();
                await this.ShowMessage("Update".Tr(TC.Dialog), "Update failed. Please try again later.".Tr(TC.Dialog));
            }
        };
        window.Show(this.Window());
    }

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
                var menuItem = new MenuItem().SetTrName("New").SetCommand("file.new");
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Open").SetCommand("file.open");
                menuBarItem.Items.Add(menuItem);
            }
            {
                mRecentFilesMenu = new MenuItem() { Foreground = Style.TEXT_LIGHT.ToBrush() }.SetTrName("Recent Files");
                UpdateRecentFilesMenu();
                menuBarItem.Items.Add(mRecentFilesMenu);
            }
            {
                var menuItem = new MenuItem().SetTrName("Save").SetCommand("file.save");
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Save As").SetCommand("file.saveAs");
                menuBarItem.Items.Add(menuItem);
            }
            {
                // 只在"当前工程来自崩溃恢复、且原文件仍在"时出现（用 IsVisible 而非置灰：一个常年灰着的项
                // 只是杂物）。可见性随工程名变化刷新，见构造函数里的订阅。
                mSaveRecoveredMenuItem = new MenuItem().SetTrName("Save to Original Location").SetAction(SaveRecoveredToOriginal);
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
                var menuItem = new MenuItem().SetTrName("Undo").SetCommand("edit.undo");
                menuBarItem.Items.Add(menuItem);
                mUndoMenuItem = menuItem;
            }
            {
                var menuItem = new MenuItem().SetTrName("Redo").SetCommand("edit.redo");
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
                ScriptToolMenu.SyncKeyCommands(this);
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
                var menuItem = new MenuItem().SetTrName("Open TuneLab Folder").SetAction(() => ProcessHelper.OpenUrl(PathManager.TuneLabFolder));
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Open Log").SetAction(() => ProcessHelper.OpenFile(PathManager.LogFilePath));
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Check for Updates...").SetAction(() => CheckUpdate(false));
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("About TuneLab").SetAction(ShowAbout);
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
