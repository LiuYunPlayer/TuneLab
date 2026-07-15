using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.GUI.Input;
using TuneLab.Input;
using TuneLab.Data;
using TuneLab.Utils;
using TuneLab.SDK;
using TuneLab.GUI.Components;
using TuneLab.Configs;
using TuneLab.I18N;
using Avalonia.Threading;

namespace TuneLab.UI;

internal class PianoWindow : DockPanel, PianoRoll.IDependency, PianoScrollView.IDependency, TimelineView.IDependency, ParameterTabBar.IDependency, ParameterTitleBar.IDependency, AutomationRenderer.IDependency, PlayheadLayer.IDependency
{
    public event Action? ActiveAutomationChanged;
    public event Action? VisibleAutomationChanged;
    public event Action? ReadbackVisibilityChanged;
    public IActionEvent WaveformBottomChanged => mWaveformBottomChanged;
    public IActionEvent WaveformVisibleChanged => mWaveformVisibleChanged;
    public IActionEvent ParameterPanelVisibilityChanged => mParameterPanelVisibilityChanged;
    public bool IsWaveformVisible => EditorState.WaveformVisible.Value;
    public bool IsParameterPanelVisible => mParameterHeight > 0.5;
    public TickAxis TickAxis => mTickAxis;
    public PitchAxis PitchAxis => mPitchAxis;
    public IHolder<IMidiPart> PartHolder => mPartHolder;
    public IHolder<ITimeline> TimelineHolder => mPartHolder;
    public IMidiPart? Part
    {
        get => mPartHolder.Value;
        set => mPartHolder.Set(value);
    }
    public ParameterButton PitchButton => mParameterTabBar.PitchButton;
    public PianoScrollView PianoScrollView => mPianoScrollView;
    public AutomationRenderer AutomationRenderer => mParameterContainer.AutomationRenderer;
    public IQuantization Quantization => mQuantization;
    public IPlayhead Playhead => mDependency.Playhead;
    public INotifiableProperty<PianoTool> PianoTool => mDependency.PianoTool;
    public INotifiableProperty<PlayScrollTarget> PlayScrollTarget => mDependency.PlayScrollTarget;
    public AutomationKey? ActiveAutomation
    {
        get
        {
            if (Part == null)
                return null;

            if (mActiveAutomation.HasValue && IsAutomationVisible(mActiveAutomation.Value))
                return mActiveAutomation.Value;

            return AutomationKey.Voice(ConstantDefine.PreCommonAutomationConfigs[0].Key.Id);
        }
        set
        {
            Part?.DeselectAllAutomationPoints();
            mActiveAutomation = value;
            if (mActiveAutomation.HasValue)
            {
                SetAutomationVisible(mActiveAutomation.Value, true);
            }

            ActiveAutomationChanged?.Invoke();
        }
    }
    public IReadOnlyList<AutomationKey> VisibleAutomations => mVisibleAutomations;
    public double WaveformBottom => mPianoScrollView.Bounds.Height - mParameterTitleBar.Height - mParameterContainer.Height;
    public interface IDependency
    {
        IPlayhead Playhead { get; }
        INotifiableProperty<PianoTool> PianoTool { get; }
        INotifiableProperty<PlayScrollTarget> PlayScrollTarget { get; }
    }

    public PianoWindow(IDependency dependency)
    {
        mDependency = dependency;
        mParameterHeight = GetStoredParameterHeight();

        mTickAxis = new TickAxis();
        mPitchAxis = new PitchAxis();
        mQuantization = new Quantization(MusicTheory.QuantizationBase.Base_1, MusicTheory.QuantizationDivision.Division_8);

        mParameterTabBar = new ParameterTabBar(this);
        this.AddDock(mParameterTabBar, Dock.Bottom);

        var leftPanel = new DockPanel() { Width = ROLL_WIDTH, ClipToBounds = true };
        {
            var box = new Border { Background = GUI.Style.DARK.ToBrush(), Height = TIME_AXIS_HEIGHT };
            leftPanel.AddDock(box, Dock.Top);

            mPianoRoll = new PianoRoll(this);
            leftPanel.AddDock(mPianoRoll);
        }
        this.AddDock(leftPanel, Dock.Left);

        var layerPanel = new LayerPanel() { ClipToBounds = true };
        {
            var pianoLayer = new DockPanel();
            {
                mPianoTimelineView = new TimelineView(this);
                pianoLayer.AddDock(mPianoTimelineView, Dock.Top);

                var pianoScrollViewPanel = new LayerPanel() { ClipToBounds = true };
                {
                    mPianoScrollView = new PianoScrollView(this);
                    pianoScrollViewPanel.Children.Add(mPianoScrollView);

                    mParameterLayer = new DockPanel() { LastChildFill = false, MinHeight = PARAMETER_TITLE_BAR_HEIGHT };
                    {
                        mParameterContainer = new ParameterContainer(this) { Height = mParameterHeight };
                        mParameterLayer.AddDock(mParameterContainer, Dock.Bottom);

                        mParameterTitleBar = new ParameterTitleBar(this) { Height = PARAMETER_TITLE_BAR_HEIGHT };
                        mParameterLayer.AddDock(mParameterTitleBar, Dock.Bottom);
                    }
                    pianoScrollViewPanel.Children.Add(mParameterLayer);

                    // 滚动条置顶层：纵向绑音高轴、贴右边；横向绑时间轴（无界，设 ContentExtentProvider = 内容末尾口径）。
                    // 横向条落在"波形上方"（= note 区下沿）而非窗口最底——由布局 Margin 定位（见 UpdateHorizontalBarMargin）。
                    mVerticalScrollBar = new(mPitchAxis, Orientation.Vertical);
                    mHorizontalScrollBar = new(mTickAxis, Orientation.Horizontal) { ContentExtentProvider = GetContentEndX };
                    pianoScrollViewPanel.Children.Add(mVerticalScrollBar);
                    pianoScrollViewPanel.Children.Add(mHorizontalScrollBar);

                    // 靠近边缘才显示：view 层职责。铺满 pianoScrollViewPanel 但只手柄可命中、其余穿透。
                    mVerticalReveal = new(mVerticalScrollBar, pianoScrollViewPanel, Orientation.Vertical);
                    mHorizontalReveal = new(mHorizontalScrollBar, pianoScrollViewPanel, Orientation.Horizontal);

                    // 横向条底边落在波形上方：以 Margin 定位（布局职责），随参数区高/波形显隐更新。
                    UpdateHorizontalBarMargin();
                    mWaveformBottomChanged.Subscribe(UpdateHorizontalBarMargin, s);
                    mWaveformVisibleChanged.Subscribe(UpdateHorizontalBarMargin, s);
                }
                pianoLayer.AddDock(pianoScrollViewPanel);

                pianoScrollViewPanel.SizeChanged += (s, e) =>
                {
                    mTickAxis.ViewLength = e.NewSize.Width;
                    mPitchAxis.ViewLength = e.NewSize.Height;
                };
            }
            layerPanel.Children.Add(pianoLayer);

            mPlayheadLayer = new PlayheadLayer(this);
            layerPanel.Children.Add(mPlayheadLayer);
        }
        this.AddDock(layerPanel);

        mParameterTabBar.StateChangeAsked += OnParameterTabBarStateChangeAsked;
        mParameterTitleBar.Moved += top =>
        {
            mParameterHeight = mParameterLayer.Bounds.Height - top - mParameterTitleBar.Bounds.Height;
            CorrectParameterHeight(true);
        };
        mParameterLayer.SizeChanged += (s, e) => { CorrectParameterHeight(false); };
        AttachedToVisualTree += (s, e) => BindWindowState();

        ClipToBounds = true;

        ActiveAutomation = AutomationKey.Voice(ConstantDefine.PreCommonAutomationConfigs[0].Key.Id);

        RegisterKeyCommands();
    }

    ~PianoWindow()
    {
        s.DisposeAll();
    }

    public bool IsAutomationVisible(AutomationKey automation)
    {
        if (Part == null)
            return false;

        if (!Part.IsEffectiveAutomation(automation) && !Part.IsEffectivePiecewiseAutomation(automation)
            && !Part.IsEffectiveNoteLane(automation) && !Part.IsEffectivePhonemeLane(automation))
            return false;

        return mVisibleAutomations.Contains(automation);
    }

    public void SetAutomationVisible(AutomationKey automation, bool isVisible)
    {
        mVisibleAutomations.Remove(automation);

        if (isVisible)
            mVisibleAutomations.Add(automation);

        VisibleAutomationChanged?.Invoke();
    }

    // —— 合成参数回显轨显隐（只读轨集合，按 AutomationKey 分源合并 voice + 各 effect；独立于可编辑轨的
    //    Visible/Active 机制；显隐由参数区标题栏管控）——
    // 每次按源现合并（轨集合随参数 commit 涌现、规模小、不在热路径）：voice 声明 → AutomationKey.Voice，
    // 各 effect 声明 → AutomationKey.Effect(index)。
    public IReadOnlyOrderedMap<AutomationKey, AutomationConfigEntry> ReadbackConfigs
    {
        get
        {
            if (Part == null)
                return sEmptyReadbackConfigs;

            var result = new OrderedMap<AutomationKey, AutomationConfigEntry>();
            foreach (var kvp in Part.SoundSource.SynthesizedParameterConfigs)
                result.Add(AutomationKey.Voice(kvp.Key.Id), new AutomationConfigEntry(kvp.Key, kvp.Value));
            for (int i = 0; i < Part.Effects.Count; i++)
            {
                foreach (var kvp in Part.Effects[i].SynthesizedParameterConfigs)
                    result.Add(AutomationKey.Effect(i, kvp.Key.Id), new AutomationConfigEntry(kvp.Key, kvp.Value));
            }
            return result;
        }
    }

    public void SetWaveformVisible(bool isVisible)
    {
        if (EditorState.WaveformVisible.Value == isVisible)
            return;

        EditorState.WaveformVisible.Value = isVisible;
        mWaveformVisibleChanged.Invoke();
    }

    public bool IsReadbackVisible(AutomationKey key) => mVisibleReadbacks.Contains(key);

    public void SetReadbackVisible(AutomationKey key, bool isVisible)
    {
        bool changed = isVisible ? mVisibleReadbacks.Add(key) : mVisibleReadbacks.Remove(key);
        if (changed)
            ReadbackVisibilityChanged?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.IsHandledByTextBox())
            return;

        if (mParameterContainer.AutomationRenderer.IsOperating)
            return;

        // 命令级快捷键仅在无进行中操作（拖动/缩放等）时分发；进行中的操作态修饰键由各 Operation 自行处理、不经此。
        if (PianoScrollView.OperationState != PianoScrollView.State.None)
            return;

        e.Handled = Keymap.TryHandle(KeyScope.PianoWindow, e);
    }

    // PianoWindow 作用域的内置快捷键命令（钢琴窗专属：移调 / 八度）。剪贴板类动词（复制/剪切/粘贴/删除/全选）
    // 是与编排区共享的通用动作，注册在 Editor 域、由 Editor 按聚焦面路由到下列 *Selection 方法，不在此登记。
    void RegisterKeyCommands()
    {
        // 域 = 功能身份（note 音符级操作），非分发作用域（虽在 PianoWindow 分发）。见 docs/keybinding-system.md §1.1。
        Keymap.Register(new() { Id = "note.octaveUp", DisplayName = () => "Octave Up".Tr(TC.Menu), Scope = KeyScope.PianoWindow, DefaultGesture = new(Key.Up, KeyModifiers.Shift), Execute = () => PianoScrollView.OctaveUp() });
        Keymap.Register(new() { Id = "note.octaveDown", DisplayName = () => "Octave Down".Tr(TC.Menu), Scope = KeyScope.PianoWindow, DefaultGesture = new(Key.Down, KeyModifiers.Shift), Execute = () => PianoScrollView.OctaveDown() });
        Keymap.Register(new() { Id = "note.transposeUp", DisplayName = () => "Semitone Up".Tr(TC.Menu), Scope = KeyScope.PianoWindow, DefaultGesture = new(Key.Up), Execute = () => PianoScrollView.ChangeKey(+1) });
        Keymap.Register(new() { Id = "note.transposeDown", DisplayName = () => "Semitone Down".Tr(TC.Menu), Scope = KeyScope.PianoWindow, DefaultGesture = new(Key.Down), Execute = () => PianoScrollView.ChangeKey(-1) });
    }

    // 剪贴板类命令仅在无进行中操作（拖动/缩放等）时生效——原本由 OnKeyDown 前置守卫，改由 Editor 路由后在此自守。
    bool CanRunEditCommand => !mParameterContainer.AutomationRenderer.IsOperating && PianoScrollView.OperationState == PianoScrollView.State.None;

    public void CopySelection() { if (CanRunEditCommand) PianoScrollView.Copy(); }
    public void CutSelection() { if (CanRunEditCommand) PianoScrollView.Cut(); }
    public void PasteSelection() { if (CanRunEditCommand) PianoScrollView.Paste(); }

    public void DeleteSelection()
    {
        if (!CanRunEditCommand)
            return;
        var automationRenderer = mParameterContainer.AutomationRenderer;
        // Anchor 工具且悬停 automation 时删锚点，否则删所选对象。
        if (PianoTool.Value == UI.PianoTool.Anchor && automationRenderer.IsHover)
            automationRenderer.DeleteSelectedAnchors();
        else
            PianoScrollView.Delete();
    }

    public void SelectAllInPiano()
    {
        if (!CanRunEditCommand)
            return;
        var automationRenderer = mParameterContainer.AutomationRenderer;
        switch (PianoTool.Value)
        {
            case UI.PianoTool.Note:
                Part?.Notes.SelectAllItems();
                break;
            case UI.PianoTool.Vibrato:
                Part?.Vibratos.SelectAllItems();
                break;
            case UI.PianoTool.Anchor:
                if (automationRenderer.IsHover)
                    automationRenderer.SelectAllAnchors();
                else
                {
                    Part?.DeselectAllAutomationPoints();
                    automationRenderer.InvalidateVisual();
                    automationRenderer.RefreshAnchorValueInput();
                    Part?.Pitch.SelectAllAnchors();
                }
                break;
            default:
                break;
        }
    }

    // 一键折叠/恢复参数面板内容高度（标题栏仍保留，与拖到最低等价）。
    // 收起前把当前非零高度写入 EditorState，恢复时再读回；折叠态本身不落盘为 0，避免覆盖原高度。
    public void ToggleParameterPanel()
    {
        if (mParameterHeight > 0.5)
        {
            StoreParameterHeight(mParameterHeight);
            mParameterHeight = 0;
            CorrectParameterHeight(false);
            return;
        }

        var restore = GetStoredParameterHeight();
        if (restore < 1)
            restore = EditorState.Defaults.ParameterPanelHeight;
        mParameterHeight = restore;
        CorrectParameterHeight(false);
    }

    void CorrectParameterHeight(bool saveHeight = false)
    {
        var displayHeight = mParameterHeight.Limit(0, mParameterLayer.Bounds.Height - mParameterTitleBar.Bounds.Height);
        mParameterContainer.Height = displayHeight;
        if (saveHeight)
        {
            mParameterHeight = displayHeight;
            StoreParameterHeight(mParameterHeight);
        }
        mWaveformBottomChanged.Invoke();
        mParameterPanelVisibilityChanged.Invoke();
    }

    void BindWindowState()
    {
        if (mWindowStateBound)
            return;

        mWindow = this.Window();
        mWindowStateBound = true;
        s.Add(mWindow.GetObservable(Window.WindowStateProperty).Subscribe(state =>
        {
            var height = state == WindowState.Maximized ? EditorState.ParameterPanelHeightMaximized.Value : EditorState.ParameterPanelHeightNormal.Value;
            if (Math.Abs(mParameterHeight - height) < 0.1)
                return;

            mParameterHeight = height;
            Dispatcher.UIThread.Post(() => CorrectParameterHeight(false), DispatcherPriority.Background);
        }));
    }

    double GetStoredParameterHeight()
    {
        return EditorState.MainWindowMaximized ? EditorState.ParameterPanelHeightMaximized : EditorState.ParameterPanelHeightNormal;
    }

    void StoreParameterHeight(double height)
    {
        EditorState.ParameterPanelHeight.Value = height;
        if (mWindow != null && mWindow.WindowState == WindowState.Maximized)
        {
            EditorState.ParameterPanelHeightMaximized.Value = height;
        }
        else
        {
            EditorState.ParameterPanelHeightNormal.Value = height;
        }
    }

    void OnParameterTabBarStateChangeAsked(AutomationKey automation, ParameterButton.ButtonState state)
    {
        if (state == ParameterButton.ButtonState.Edit)
            ActiveAutomation = automation;
        else
            SetAutomationVisible(automation, state == ParameterButton.ButtonState.Visible);
    }

    const double TIME_AXIS_HEIGHT = 48;
    // 钢琴键列宽：ParameterTabBar 引用它对齐左侧预留区（面板开关正对键列居中）。
    public const double ROLL_WIDTH = 64;
    // 抬高以容纳回显轨显隐 chip（色块 + 文本）；空白区仍可拖拽改高。
    const double PARAMETER_TITLE_BAR_HEIGHT = 24;

    double mParameterHeight = 200;

    AutomationKey? mActiveAutomation;
    readonly List<AutomationKey> mVisibleAutomations = new();
    // 回显轨显隐集合（按轨 id；默认隐藏，用户经标题栏 chip 点亮）。
    readonly HashSet<AutomationKey> mVisibleReadbacks = new();
    static readonly OrderedMap<AutomationKey, AutomationConfigEntry> sEmptyReadbackConfigs = new();

    readonly ActionEvent mWaveformBottomChanged = new();
    readonly ActionEvent mWaveformVisibleChanged = new();
    readonly ActionEvent mParameterPanelVisibilityChanged = new();
    readonly DisposableManager s = new();

    readonly Quantization mQuantization;
    readonly TickAxis mTickAxis;
    readonly PitchAxis mPitchAxis;

    // 横向滚动条的内容末尾像素长度：当前 part 的末尾 tick × 每 tick 像素（手柄滑到底 = 视口远边正好落在
    // part 末尾；无限拖仍可继续拖过末尾、手柄钳在边缘）。
    double GetContentEndX()
    {
        var part = mPartHolder.Value;
        if (part == null)
            return 0;

        return part.EndPos() * mTickAxis.PixelsPerTick;
    }

    // 横向条落在"波形上方"（note 区下沿）：底边留白 = 参数区高 + 波形高（波形可见时），用 Margin 定位。
    void UpdateHorizontalBarMargin()
    {
        double inset = mParameterTitleBar.Height + mParameterContainer.Height;
        if (IsWaveformVisible)
            inset += PianoScrollView.WAVEFORM_HEIGHT;
        mHorizontalScrollBar.Margin = new Thickness(0, 0, 0, inset);
    }

    readonly PianoScrollView mPianoScrollView;
    readonly TimelineView mPianoTimelineView;
    readonly PlayheadLayer mPlayheadLayer;
    readonly ScrollBar mVerticalScrollBar;
    readonly ScrollBar mHorizontalScrollBar;
    readonly EdgeProximityReveal mVerticalReveal;
    readonly EdgeProximityReveal mHorizontalReveal;
    readonly ParameterContainer mParameterContainer;
    readonly ParameterTitleBar mParameterTitleBar;
    readonly PianoRoll mPianoRoll;
    readonly ParameterTabBar mParameterTabBar;
    readonly DockPanel mParameterLayer;

    readonly Holder<IMidiPart> mPartHolder = new();

    readonly IDependency mDependency;
    bool mWindowStateBound = false;
    Window? mWindow;
}
