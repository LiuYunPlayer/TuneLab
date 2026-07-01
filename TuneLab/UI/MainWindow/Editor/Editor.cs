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

        mFunctionBar = new(this);
        mPianoWindow = new(this);// { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
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
        mRightSideBar = new() { Width = 320, Margin = new(1, 0, 0, 0) };

        var panel = new DockPanel() { Background = Style.INTERFACE.ToBrush(), Margin = new(1, 0, 0, 0) };
        {
            var hoverBack = Colors.White.Opacity(0.05);
            var settingsButton = new GUI.Components.Button() { Width = 48, Height = 48 }
            .AddContent(new() { Item = new IconItem() { Icon = Assets.Settings, Scale = 4.0 / 3.0 }, ColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5), HoveredColor = Colors.White, PressedColor = Colors.White } });
            settingsButton.Clicked += () => new SettingsWindow().Show(this.Window());
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
        mFunctionBar.GotoStartAsked += () =>
        {
            var startTime = 0;
            AudioEngine.Seek(startTime);
            if (Project == null) 
                return;

            var startTick = Project.TempoManager.GetTick(startTime);
            mTrackWindow.TickAxis.AnimateMoveTickToX(startTick, 0);
            mPianoWindow.TickAxis.AnimateMoveTickToX(startTick, 0);
        };
        mFunctionBar.GotoEndAsked += () =>
        {
            var endTime = AudioEngine.EndTime;
            AudioEngine.Seek(endTime);
            if (Project == null) 
                return;

            var endTick = Project.TempoManager.GetTick(endTime);
            mTrackWindow.TickAxis.AnimateMoveTickToX(endTick, mTrackWindow.TickAxis.ViewLength);
            mPianoWindow.TickAxis.AnimateMoveTickToX(endTick, mPianoWindow.TickAxis.ViewLength);
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
                    mAgentSideBarContentProvider.SetProject(Project);
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

        Menu = CreateMenu();

        mFunctionBar.GotFocus += (s, e) => { mPianoWindow.PianoScrollView.Focus(); };
        mFunctionBar.QuantizationChanged.Subscribe(mPianoWindow.Quantization.Set);
        mFunctionBar.QuantizationChanged.Subscribe(mTrackWindow.Quantization.Set);
        mDocument.StatusChanged += () => { mUndoMenuItem.IsEnabled = mDocument.Undoable(); mRedoMenuItem.IsEnabled = mDocument.Redoable(); };
        mAutoSaveTimer.Tick += (s, e) => { AutoSave(); };
        Settings.AutoSaveInterval.Modified.Subscribe(() => mAutoSaveTimer.Interval = new TimeSpan(0, 0, Settings.AutoSaveInterval), s);
        PlayScrollTarget.Modified.Subscribe(() => Settings.AutoScrollTarget.Value = PlayScrollTarget.Value.ToString(), s);
        PathManager.MakeSureExist(PathManager.AutoSaveFolder);
        PathManager.MakeSureExist(PathManager.AutoSaveHistoryFolder);
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

        e.Handled = true;
        if (e.Match(Key.Space))
        {
            ChangePlayState();
        }
        else if (e.Match(Key.Z, ModifierKeys.Ctrl))
        {
            Undo();
        }
        else if (e.Match(Key.Y, ModifierKeys.Ctrl))
        {
            Redo();
        }
        else if (e.Match(Key.Tab, ModifierKeys.Ctrl))
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
        else if (e.ModifierKeys() == ModifierKeys.None && e.Key >= Key.D1 && e.Key <= Key.D6)
        {
            mPianoWindow.PianoTool.Value = (PianoTool)(e.Key - Key.D1);
        }
        else
        {
            e.Handled = false;
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

            // 回传选中它的那次 peek 的同一窗口（ahead = [currentTime, +∞)，behind = (-∞, currentTime]），
            // 而非 bestSegment 自身——插件据此确定性重导出 peek 报出的同一块。
            if (bestIsAhead)
                best.Dispatch(currentTime, double.MaxValue);
            else
                best.Dispatch(double.MinValue, currentTime);
            idle.Remove(best);
            busy++;
        }
    }

    public void ClearAutoSaveFile()
    {
        mAutoSaveHead = default;
        // Only clear the crash-detection files in AutoSaveFolder (not subdirectories)
        foreach (var file in Directory.GetFiles(PathManager.AutoSaveFolder))
        {
            File.Delete(file);
        }
    }

    async void AutoSave()
    {
        if (mDocument.Project == null || mDocument.IsSaved || mAutoSaveHead == mDocument.Head)
            return;

        var projectInfo = GetProjectInfoForSave();

        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss_");
            var fileName = timestamp + Path.GetFileNameWithoutExtension(mDocument.Name) + "." + ConstantDefine.DefaultProjectExtension;
            var autoSavePath = Path.Combine(PathManager.AutoSaveFolder, fileName);

            await Task.Run(() =>
            {
                if (!FormatsManager.Serialize(projectInfo, ConstantDefine.DefaultProjectExtension, out var stream, out var error))
                {
                    Log.Error("Save file error: " + error);
                    return;
                }

                // Write the auto-save file for crash detection
                using (FileStream fileStream = new FileStream(autoSavePath, FileMode.Create))
                {
                    stream.CopyTo(fileStream);
                }

                // Delete previous crash-detection files (keep only the latest)
                foreach (var file in Directory.GetFiles(PathManager.AutoSaveFolder))
                {
                    if (file != autoSavePath)
                        File.Delete(file);
                }

                // Copy to history folder for multi-version backup
                try
                {
                    PathManager.MakeSureExist(PathManager.AutoSaveHistoryFolder);
                    var historyPath = Path.Combine(PathManager.AutoSaveHistoryFolder, fileName);
                    File.Copy(autoSavePath, historyPath, true);

                    // Rotate history files: delete oldest if exceeding max count
                    var maxCount = Settings.AutoSaveMaxCount.Value;
                    var historyFiles = Directory.GetFiles(PathManager.AutoSaveHistoryFolder)
                        .Select(f => new FileInfo(f))
                        .OrderByDescending(f => f.CreationTime)
                        .ToList();

                    if (historyFiles.Count > maxCount)
                    {
                        foreach (var oldFile in historyFiles.Skip(maxCount))
                        {
                            oldFile.Delete();
                            Log.Debug("Deleted old auto-save history file: " + oldFile.FullName);
                        }
                    }
                }
                catch (Exception historyEx)
                {
                    Log.Error("Failed to manage auto-save history: " + historyEx);
                }
            });

            mAutoSaveHead = mDocument.Head;
            Log.Debug("Project auto saved: " + autoSavePath);
        }
        catch (Exception ex)
        {
            Log.Debug("Write file error: " + ex);
        }
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
                            Dur = 64 * MusicTheory.RESOLUTION * 4
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

    void LoadProject(string path)
    {
        if (!FormatsManager.Deserialize(path, out var info, out var error))
        {
            Log.Error("Deserialize file error: " + error);
            return;
        }

        mDocument.SetProject(CreateProject(info), path);
        RestorePlayhead(info);
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

        if (!FormatsManager.Serialize(mDocument.Project.GetInfo(), extension, out var stream, out var error))
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

        if (!FormatsManager.Serialize(GetProjectInfoForSave(), ConstantDefine.DefaultProjectExtension, out var stream, out var error))
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

    ProjectInfo GetProjectInfoForSave()
    {
        var project = mDocument.Project;
        if (project == null)
            return new();

        var projectInfo = project.GetInfo();
        projectInfo.EditorInfo.PlayheadPos = Playhead.Pos;
        return projectInfo;
    }

    void RestorePlayhead(ProjectInfo info)
    {
        Playhead.Pos = Math.Max(0, info.EditorInfo.PlayheadPos);
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

                    string filePath = Path.Combine(options.ExportPath, options.FileName + "_" + trackName.ToValidFileName() + ".wav");

                    if (trackIndex == -1)
                    {
                        AudioEngine.ExportMaster(filePath, isStereo, options.SampleRate, options.BitDepth, trackProgress);
                    }
                    else if (trackIndex >= 0 && trackIndex < project.Tracks.Count)
                    {
                        var track = project.Tracks[trackIndex];
                        AudioEngine.ExportTrack(filePath, track, isStereo, options.SampleRate, options.BitDepth, trackProgress);
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

            // 读包名（容错）：description.json 缺失/损坏不阻断安装——解压后由 ExtensionManager.Load
            // 优雅记录加载状态。绝不让一个坏包的解析异常冒泡（本方法是 async void，未捕获即崩进程）。
            try
            {
                using var archive = ZipFile.OpenRead(file);
                using var stream = archive.GetEntry("description.json")?.Open();
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
                var menuItem = new MenuItem().SetTrName("New").SetAction(NewProject).SetShortcut(Key.N, ModifierKeys.Ctrl);
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Open").SetAction(OpenProject).SetShortcut(Key.O, ModifierKeys.Ctrl);
                menuBarItem.Items.Add(menuItem);
            }
            {
                mRecentFilesMenu = new MenuItem() { Foreground = Style.TEXT_LIGHT.ToBrush() }.SetTrName("Recent Files");
                UpdateRecentFilesMenu();
                menuBarItem.Items.Add(mRecentFilesMenu);
            }
            {
                var menuItem = new MenuItem().SetTrName("Save").SetAction(async () => { await SaveProject(); }).SetShortcut(Key.S, ModifierKeys.Ctrl);
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Save As").SetAction(async () => { await SaveProjectAs(); }).SetShortcut(Key.S, ModifierKeys.Ctrl | ModifierKeys.Shift);
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem() { Foreground = Style.TEXT_LIGHT.ToBrush() }.SetTrName("Export As (test)");
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
                var menuItem = new MenuItem().SetTrName("Undo").SetAction(Undo).SetInputGesture(Key.Z, ModifierKeys.Ctrl);
                menuBarItem.Items.Add(menuItem);
                mUndoMenuItem = menuItem;
            }
            {
                var menuItem = new MenuItem().SetTrName("Redo").SetAction(Redo).SetInputGesture(Key.Y, ModifierKeys.Ctrl);
                menuBarItem.Items.Add(menuItem);
                mRedoMenuItem = menuItem;
            }
            {
                var menuItem = new MenuItem().SetTrName("Settings").SetAction(() =>
                {
                    var settingsWindow = new SettingsWindow();
                    settingsWindow.Show(this.Window());
                });
                menuBarItem.Items.Add(menuItem);
            }
            menu.Items.Add(menuBarItem);
        }

        {
            var menuBarItem = new MenuItem { Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false }.SetTrName("Project");
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
            menu.Items.Add(menuBarItem);
        }

        {
            var menuBarItem = new MenuItem { Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false }.SetTrName("Transport");
            {
                var menuItem = new MenuItem().
                    SetName("Play".Tr(TC.Menu)).
                    SetAction(ChangePlayState).
                    SetInputGesture(Key.Space);
                void UpdateHeader() => menuItem.SetName(AudioEngine.IsPlaying ? "Pause".Tr(TC.Menu) : "Play".Tr(TC.Menu));
                AudioEngine.PlayStateChanged += UpdateHeader;
                TranslationManager.CurrentLanguage.Modified.Subscribe(UpdateHeader);
                menuBarItem.Items.Add(menuItem);
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
            }
            // 不在本菜单自身 SubmenuOpened 时重建——边打开边换 Items 会让首次悬浮二级（分组）子菜单被立即关闭
            // （与 Recent Files 同坑）。改为：内容须在菜单打开前就备好——靠脚本目录的文件监视器在增删改时提前重建。
            mRebuildScriptsMenu = Rebuild;
            Rebuild();
            SetupScriptsWatcher(Rebuild);
            menu.Items.Add(menuBarItem);
        }

        {
            var menuBarItem = new MenuItem { Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false }.SetTrName("Extensions");
            {
                var menuItem = new MenuItem().SetTrName("Install/Update").SetAction(async () =>
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
                });
                menuBarItem.Items.Add(menuItem);
            }
            menu.Items.Add(menuBarItem);
        }

        {
            var menuBarItem = new MenuItem { Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false }.SetTrName("Help");
            {
                var menuItem = new MenuItem().SetTrName("TuneLab Forum").SetAction(() => ProcessHelper.OpenUrl("https://forum.tunelab.app"));
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("TuneLab GitHub").SetAction(() => ProcessHelper.OpenUrl("https://github.com/LiuYunPlayer/TuneLab"));
                menuBarItem.Items.Add(menuItem);
            }
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
